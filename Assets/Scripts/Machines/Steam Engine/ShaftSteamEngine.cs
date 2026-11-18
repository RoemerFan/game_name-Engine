using UnityEngine;

// =====================================================================
//  ShaftSteamEngine: Dampfmaschine mit Fliehkraftregler. Wandelt Dampf in ein Drehmoment auf der Welle.
//
//  Ports im Prefab:
//   - Dampf, EINGANG  (Leitung vom Kessel)
//   - Welle           (Wellen-Port; bei der Welle weiter zu Generator, Foerderband, Pumpe)
//
//  So arbeitet sie:
//   - Das Drehmoment ist am groessten im Stillstand und faellt, je schneller sie dreht ("No Load Factor").
//   - Der Regler oeffnet das Ventil, wenn die Drehzahl unter "Governed Rpm" faellt, und schliesst es darueber.
//     So bleibt die Drehzahl auch bei wechselnder Last ungefaehr gleich.
//   - Dampfverbrauch folgt der abgegebenen Leistung. Ohne Dampf kein Moment.
//   - Optional: ein MachineSwitch (Ein/Aus) irgendwo unter der Maschine.
//
//  Passende Werte: Generator-Nenndrehzahl = "Governed Rpm" der Maschine.
// =====================================================================
public class ShaftSteamEngine : MonoBehaviour, IPortOwner, IShaftDevice, ISaveable
{
    [Header("Leistung")]
    [Tooltip("Leistung (kW), die die Maschine bei der Solldrehzahl abgeben kann.")]
    [SerializeField] private float ratedPowerKW = 60f;

    [Tooltip("Solldrehzahl des Reglers (U/min).")]
    [SerializeField] private float governedRpm = 1500f;

    [Tooltip("Leerlaufdrehzahl im Verhaeltnis zur Solldrehzahl (1.25 = dreht ohne Last bis 125 %). " +
             "Naeher an 1 = Moment faellt schneller mit der Drehzahl.")]
    [SerializeField] private float noLoadFactor = 1.25f;

    [Tooltip("Reglerband: Ventil ist bei Solldrehzahl minus diesem Anteil ganz offen und bei plus diesem Anteil zu.")]
    [Range(0.01f, 0.3f)]
    [SerializeField] private float governorBand = 0.05f;

    [Header("Dampf")]
    [Tooltip("Dampfverbrauch pro Sekunde bei voller Leistung.")]
    [SerializeField] private float steamPerSecondAtRated = 3f;

    [Tooltip("Dampfverbrauch pro Sekunde im Leerlauf (laufende Maschine ohne Last).")]
    [SerializeField] private float idleSteamPerSecond = 0.15f;

    [SerializeField] private float steamCapacity = 20f;

    [Header("Mechanik")]
    [Tooltip("Traegheit des Schwungrads in kg*m^2. Mehr = die Drehzahl reagiert traeger.")]
    [SerializeField] private float inertia = 15f;

    [Tooltip("Reibung der Maschine in Prozent des Nennmoments (bei Solldrehzahl).")]
    [Range(0f, 0.2f)]
    [SerializeField] private float frictionFraction = 0.03f;

    [Header("Anzeige")]
    [Tooltip("Teil des Modells, der sich dreht (Schwungrad). Leer = keine Animation.")]
    [SerializeField] private Transform rotor;
    [SerializeField] private Vector3 rotorAxis = Vector3.forward;
    [Tooltip("Optik: Wie viel der echten Drehung gezeigt wird (1 = echt, kleiner = langsamer).")]
    [SerializeField] private float rotorVisualFactor = 0.05f;

    [Header("Schalter (optional)")]
    [SerializeField] private MachineSwitch machineSwitch;

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 3f;

    private float steam;
    private float rpm;
    private float supply = 1f;
    private float throttle;
    private float powerKW;
    private RotorSpin spin;

    public float Rpm { get { return rpm; } }
    public float PowerKW { get { return powerKW; } }

    private bool IsOn { get { return machineSwitch == null || machineSwitch.IsOn; } }

    // Stillstandsmoment (Nm): so, dass bei Solldrehzahl und offenem Ventil die Nennleistung herauskommt.
    private float StallTorque
    {
        get
        {
            float omega = governedRpm * Mathf.PI / 30f;
            float share = 1f - 1f / Mathf.Max(1.01f, noLoadFactor);
            return ratedPowerKW * 1000f / Mathf.Max(1f, omega * share);
        }
    }

    // ---------- IShaftDevice ----------

    public float ShaftInertia { get { return inertia; } }

    public float ShaftRpm
    {
        get { return rpm; }
        set { rpm = Mathf.Max(0f, value); }
    }

    public float ShaftTorque(float r)
    {
        float friction = StallTorque * 0.2f * frictionFraction * Mathf.Clamp01(r / Mathf.Max(1f, governedRpm));

        if (!IsOn || supply <= 0f)
            return -friction;

        // Regler: ganz offen unter Soll minus Band, ganz zu ueber Soll plus Band.
        float open = Mathf.Clamp01((governedRpm * (1f + governorBand) - r) / (governedRpm * governorBand * 2f));
        throttle = open;

        float noLoad = governedRpm * Mathf.Max(1.01f, noLoadFactor);
        float curve = Mathf.Max(0f, 1f - r / noLoad);

        return StallTorque * open * curve * supply - friction;
    }

    public void ShaftTick(float r, float dt)
    {
        rpm = r;

        if (rotor != null)
        {
            if (spin == null)
                spin = new RotorSpin(rotor, rotorAxis);

            spin.Rotate(rpm * 6f * rotorVisualFactor * dt);
        }
    }

    // ---------- Unity ----------

    private void Awake()
    {
        if (machineSwitch == null)
            machineSwitch = GetComponentInChildren<MachineSwitch>();
    }

    private void OnEnable()
    {
        ShaftGrid.Register(this);
    }

    private void OnDisable()
    {
        ShaftGrid.Unregister(this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (dt <= 0f)
            return;

        // Abgegebene Leistung aus dem aktuellen Moment.
        float omega = rpm * Mathf.PI / 30f;
        powerKW = Mathf.Max(0f, ShaftTorque(rpm) * omega / 1000f);

        // Dampfverbrauch: Leerlauf plus Anteil nach Leistung.
        float idle = IsOn ? idleSteamPerSecond * Mathf.Clamp01(rpm / Mathf.Max(1f, governedRpm)) : 0f;
        float perKW = ratedPowerKW > 0f ? steamPerSecondAtRated / ratedPowerKW : 0f;

        float wanted = (idle + perKW * powerKW) * dt;
        float got = Mathf.Min(steam, wanted);
        steam -= got;

        supply = wanted > 0.00001f ? got / wanted : 1f;
    }

    // ---------- IPortOwner ----------

    public float GetAvailable(Port port)
    {
        return 0f;
    }

    public float GetFreeSpace(Port port)
    {
        return port.Medium == PortMedium.Steam ? Mathf.Max(0f, steamCapacity - steam) : 0f;
    }

    public float TakeFrom(Port port, float amount)
    {
        return 0f;
    }

    public float GiveTo(Port port, float amount)
    {
        if (port.Medium != PortMedium.Steam)
            return 0f;

        float accepted = Mathf.Clamp(amount, 0f, steamCapacity - steam);
        steam += accepted;
        return accepted;
    }

    // ---------- Anzeige ----------

    [ContextMenu("Debug: Dampf +10")]
    private void DebugAddSteam()
    {
        steam = Mathf.Min(steamCapacity, steam + 10f);
    }

    private void OnGUI()
    {
        if (!showDebugInfo)
            return;

        Camera cam = TrackManager.Instance != null ? TrackManager.Instance.PlayerCamera : null;

        if (cam == null)
            cam = Camera.main;

        if (cam == null)
            return;

        Vector3 world = infoAnchor != null ? infoAnchor.position : transform.position + Vector3.up * debugHeight;

        if ((world - cam.transform.position).sqrMagnitude > debugDistance * debugDistance)
            return;

        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return;

        string status = Loc.T(
            !IsOn ? "shaftengine.status.off"
            : steam <= 0.05f && rpm < 1f ? "shaftengine.status.nosteam"
            : rpm > 1f ? "shaftengine.status.running"
            : "shaftengine.status.stopped");

        string text = Loc.T("shaftengine.info", status,
            steam.ToString("F1"), steamCapacity,
            rpm.ToString("F0"), powerKW.ToString("F1"), ratedPowerKW.ToString("F0"),
            (throttle * 100f).ToString("F0"));

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("steam", steam);
        bag.SetFloat("rpm", rpm);
    }

    public void LoadState(StateBag bag)
    {
        steam = Mathf.Clamp(bag.GetFloat("steam", steam), 0f, steamCapacity);
        rpm = Mathf.Max(0f, bag.GetFloat("rpm", rpm));
    }
}
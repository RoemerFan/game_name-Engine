using UnityEngine;

// Dampfgenerator: Dampf rein, Laeufer dreht sich, Leistung in kW raus.
// Das PowerGrid fordert ueber SetRequestedPower() Leistung an und liest
// AvailablePowerKW und FrequencyHz aus.
//
// Ports im Prefab: Steam/Input, Power.
public class SteamGenerator : MonoBehaviour, IPortOwner, IPowerProducer, ISaveable
{
    [Header("Leistung")]
    [SerializeField] private float ratedPowerKW = 60f;
    [Tooltip("Dampfverbrauch pro Sekunde bei Nennleistung.")]
    [SerializeField] private float steamPerSecondAtRated = 3f;
    [Tooltip("Dampfverbrauch pro Sekunde im Leerlauf (Laeufer dreht, keine Last).")]
    [SerializeField] private float idleSteamPerSecond = 0.15f;

    [Header("Dampfspeicher")]
    [SerializeField] private float steamCapacity = 20f;

    [Header("Läufer")]
    [SerializeField] private float nominalRpm = 3000f;
    [SerializeField] private float spinUpRpmPerSecond = 300f;
    [SerializeField] private float spinDownRpmPerSecond = 150f;
    [SerializeField] private float nominalFrequencyHz = 50f;

    [Header("Überlast")]
    [Tooltip("Wie stark die Drehzahl bei starker Ueberlast sinkt (0.08 = 8 %).")]
    [SerializeField] private float overloadDroop = 0.08f;
    [Tooltip("Wie schnell die Drehzahl unter Ueberlast faellt (Traegheit).")]
    [SerializeField] private float droopRpmPerSecond = 40f;

    [Header("Optik (optional)")]
    [SerializeField] private Transform rotor;
    private RotorSpin spin; // berechnet die Drehung um die Modellmitte
    [SerializeField] private Vector3 rotorAxis = Vector3.up;
    [Tooltip("Echte 3000 U/min waeren nur Flackern; daher verkleinert.")]
    [SerializeField] private float rotorVisualFactor = 0.05f;

    [Header("Schalter (optional)")]
    [SerializeField] private MachineSwitch machineSwitch;

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [Tooltip("Optional: Leeres Objekt, an dessen Position der Info-Text erscheint. " +
             "Leer = Hoehe ueber dem Nullpunkt der Maschine (siehe 'Debug Height').")]
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 3f;

    private float steam;
    private float rpm;
    private float requestedPowerKW;
    private float deliveredPowerKW;
    private float supply = 1f;

    public float RatedPowerKW => ratedPowerKW;
    public float DeliveredPowerKW => deliveredPowerKW;
    public float Rpm => rpm;

    // 0 = steht, 1 = Laeufer bei mindestens 90 % der Nenndrehzahl.
    public float RpmFactor =>
        Mathf.Clamp01((rpm / nominalRpm - 0.5f) / 0.4f);

    // Leistung, die der Generator im Moment liefern koennte.
    public float AvailablePowerKW =>
        IsOn ? ratedPowerKW * RpmFactor * supply : 0f;

    // Netzfrequenz folgt der Drehzahl.
    public float FrequencyHz => nominalFrequencyHz * rpm / nominalRpm;

    public bool HasSteamShortage => supply < 0.99f;

    // Ausgeschaltet: Der Generator ist vom Netz getrennt, dreht aber weiter.
    private bool IsOn => machineSwitch == null || machineSwitch.IsOn;

    private void Awake()
    {
        if (machineSwitch == null)
            machineSwitch = GetComponentInChildren<MachineSwitch>();
    }

    // Das Stromnetz fordert diese Leistung an.
    public void SetRequestedPower(float kw)
    {
        requestedPowerKW = Mathf.Max(0f, kw);
    }

    private void OnEnable()
    {
        PowerGrid.Register(this);
    }

    private void OnDisable()
    {
        PowerGrid.Unregister(this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        float load = Mathf.Clamp(requestedPowerKW, 0f, ratedPowerKW);
        float loadFraction = ratedPowerKW > 0f ? load / ratedPowerKW : 0f;

        // Dampfbedarf: Leerlauf plus Anteil je nach Last.
        float steamRate = Mathf.Lerp(
            idleSteamPerSecond,
            steamPerSecondAtRated,
            loadFraction
        );

        float wanted = steamRate * dt;
        float got = Mathf.Min(steam, wanted);
        steam -= got;

        // Welcher Anteil des Bedarfs war gedeckt?
        supply = wanted > 0f ? got / wanted : 1f;

        // Ueberlast: Wird mehr verlangt als moeglich, bremst das den Laeufer.
        float overload = ratedPowerKW > 0f
            ? Mathf.Max(0f, requestedPowerKW / ratedPowerKW - 1f)
            : 0f;

        float droop = overloadDroop * Mathf.Clamp01(overload / 0.5f);
        float targetRpm = nominalRpm * supply * (1f - droop);

        float rate;

        if (rpm < targetRpm)
            rate = spinUpRpmPerSecond;
        else if (supply < 0.99f)
            rate = spinDownRpmPerSecond;
        else
            rate = droopRpmPerSecond;

        rpm = Mathf.MoveTowards(rpm, targetRpm, rate * dt);

        deliveredPowerKW = IsOn ? load * supply * RpmFactor : 0f;

        if (rotor != null)
        {
            // Dreht um die eigene Mitte des Modells (siehe RotorSpin).
            if (spin == null)
                spin = new RotorSpin(rotor, rotorAxis);

            spin.Rotate(rpm * 6f * rotorVisualFactor * dt);
        }
    }

    // ---------- IPortOwner ----------

    public float GetAvailable(Port port)
    {
        return 0f;
    }

    public float GetFreeSpace(Port port)
    {
        if (port.Medium == PortMedium.Steam)
            return steamCapacity - steam;

        return 0f;
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

    // ---------- Testhilfe ----------

    [ContextMenu("Debug: Dampf +10")]
    private void DebugAddSteam()
    {
        steam = Mathf.Min(steamCapacity, steam + 10f);
    }

    private void OnGUI()
    {
        if (!showDebugInfo)
            return;

        Camera cam = Camera.main;

        if (cam == null && Camera.allCamerasCount > 0)
            cam = Camera.allCameras[0];

        if (cam == null)
            return;

        // Position des Info-Textes: am Anker, sonst ueber der Maschine.
        Vector3 world = infoAnchor != null
            ? infoAnchor.position
            : transform.position + Vector3.up * debugHeight;

        if ((world - cam.transform.position).sqrMagnitude
            > debugDistance * debugDistance)
            return;

        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return;

        string status = Loc.T(
            !IsOn ? "steamgen.status.decoupled"
            : HasSteamShortage && rpm > 1f ? "steamgen.status.shortage"
            : RpmFactor >= 1f ? "steamgen.status.running"
            : rpm > 1f ? "steamgen.status.spinup"
            : "steamgen.status.stopped");

        string text = Loc.T(
            "steamgen.info",
            status,
            steam.ToString("F1"), steamCapacity,
            rpm.ToString("F0"), FrequencyHz.ToString("F1"),
            deliveredPowerKW.ToString("F1"), ratedPowerKW.ToString("F0"),
            requestedPowerKW.ToString("F1"));

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
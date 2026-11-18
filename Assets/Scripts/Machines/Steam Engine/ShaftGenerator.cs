using UnityEngine;

// =====================================================================
//  ShaftGenerator: Stromgenerator, der von einer WELLE angetrieben wird (z. B. von der Dampfmaschine).
//
//  Ports im Prefab:
//   - Welle   (Wellen-Port)
//   - Strom   (Power-Port zum Stromnetz)
//
//  So arbeitet er:
//   - Er liefert Strom ins bestehende Stromnetz (PowerGrid), solange die Welle schnell genug dreht
//     (ab etwa 60 % der Nenndrehzahl, volle Leistung bei 90 %).
//   - Was er an Strom abgibt, kostet Drehmoment auf der Welle: Leistung = Moment mal Drehzahl / Wirkungsgrad.
//     Reicht die Dampfmaschine nicht, faellt die Drehzahl, und mit ihr Leistung und Netzfrequenz.
//   - Optional: ein MachineSwitch (Ein = am Netz, Aus = abgekoppelt, dreht frei mit).
// =====================================================================
public class ShaftGenerator : MonoBehaviour, IPowerProducer, IShaftDevice, ISaveable
{
    [Header("Leistung")]
    [SerializeField] private float ratedPowerKW = 60f;

    [Tooltip("Drehzahl, bei der der Generator volle Leistung und Nennfrequenz liefert (U/min). " +
             "Sollte gleich der Solldrehzahl der Dampfmaschine sein.")]
    [SerializeField] private float nominalRpm = 1500f;

    [SerializeField] private float nominalFrequencyHz = 50f;

    [Tooltip("Wirkungsgrad (Anteil der mechanischen Leistung, der als Strom ankommt).")]
    [Range(0.5f, 1f)]
    [SerializeField] private float efficiency = 0.92f;

    [Header("Mechanik")]
    [Tooltip("Traegheit des Laeufers in kg*m^2.")]
    [SerializeField] private float inertia = 20f;

    [Tooltip("Reibung im Leerlauf in Nm (bei Nenndrehzahl).")]
    [SerializeField] private float frictionNm = 3f;

    [Header("Anzeige")]
    [SerializeField] private Transform rotor;
    [SerializeField] private Vector3 rotorAxis = Vector3.forward;
    [SerializeField] private float rotorVisualFactor = 0.05f;

    [Header("Schalter (optional)")]
    [SerializeField] private MachineSwitch machineSwitch;

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 3f;

    private float rpm;
    private float requestedPowerKW;
    private float deliveredPowerKW;
    private RotorSpin spin;

    private bool IsOn { get { return machineSwitch == null || machineSwitch.IsOn; } }

    public float RatedPowerKW { get { return ratedPowerKW; } }
    public float DeliveredPowerKW { get { return deliveredPowerKW; } }
    public float Rpm { get { return rpm; } }

    // 0 = zu langsam, 1 = bei mindestens 90 % der Nenndrehzahl.
    public float RpmFactor
    {
        get { return Mathf.Clamp01((rpm / nominalRpm - 0.5f) / 0.4f); }
    }

    // ---------- IPowerProducer ----------

    public float AvailablePowerKW
    {
        get { return IsOn ? ratedPowerKW * RpmFactor : 0f; }
    }

    public float FrequencyHz
    {
        get { return nominalFrequencyHz * rpm / nominalRpm; }
    }

    public void SetRequestedPower(float kw)
    {
        requestedPowerKW = Mathf.Max(0f, kw);
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
        float friction = frictionNm * Mathf.Clamp01(r / Mathf.Max(1f, nominalRpm));

        // Mechanische Leistung = abgegebener Strom / Wirkungsgrad. Moment = Leistung / Winkelgeschwindigkeit.
        float omega = Mathf.Max(r * Mathf.PI / 30f, 15f);
        float load = deliveredPowerKW * 1000f / (Mathf.Max(0.1f, efficiency) * omega);

        return -(load + friction);
    }

    public void ShaftTick(float r, float dt)
    {
        rpm = r;

        float load = Mathf.Clamp(requestedPowerKW, 0f, ratedPowerKW);
        deliveredPowerKW = IsOn ? load * RpmFactor : 0f;

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
        PowerGrid.Register(this);
        ShaftGrid.Register(this);
    }

    private void OnDisable()
    {
        PowerGrid.Unregister(this);
        ShaftGrid.Unregister(this);
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
            !IsOn ? "shaftgen.status.decoupled"
            : RpmFactor >= 1f ? "shaftgen.status.running"
            : rpm > 1f ? "shaftgen.status.slow"
            : "shaftgen.status.stopped");

        string text = Loc.T("shaftgen.info", status,
            rpm.ToString("F0"), FrequencyHz.ToString("F1"),
            deliveredPowerKW.ToString("F1"), ratedPowerKW.ToString("F0"),
            requestedPowerKW.ToString("F1"));

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("rpm", rpm);
    }

    public void LoadState(StateBag bag)
    {
        rpm = Mathf.Max(0f, bag.GetFloat("rpm", rpm));
    }
}
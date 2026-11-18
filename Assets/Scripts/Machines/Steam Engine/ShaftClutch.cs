using UnityEngine;

// =====================================================================
//  ShaftClutch: eine Kupplung auf der Welle. Eingekuppelt sind beide Seiten starr verbunden und drehen
//  gemeinsam; ausgekuppelt dreht jede Seite fuer sich (die Antriebsseite laeuft frei, die Lastseite laeuft aus).
//
//  Ports im Prefab: ZWEI Wellen-Ports, einer pro Seite (A und B). Sie werden in der Reihenfolge der Kinder
//  gefunden oder in "Port A" und "Port B" eingetragen.
//
//  Schalten: ein MachineSwitch (Ein = eingekuppelt, Aus = ausgekuppelt) unter der Kupplung, er wird automatisch
//  gefunden. Ohne Schalter ist die Kupplung immer eingekuppelt.
//
//  RUTSCHKUPPLUNG: Die Kupplung kann nur das Drehmoment "Rated Torque Nm" uebertragen.
//   - Beim Einkuppeln mit unterschiedlichen Drehzahlen rutscht sie zuerst: Die schnelle Seite zieht die langsame
//     mit dem Moment der Kupplung mit, bis beide gleich schnell sind, dann greift sie fest. So gibt es keinen Ruck.
//   - Wird sie im Betrieb ueberlastet (zu grosse Last), rutscht sie wieder durch, statt dass etwas kaputtgeht,
//     und greift erst wieder, wenn beide Seiten gleich schnell sind.
//   - Mit 0 bei "Rated Torque Nm" rutscht sie nie: Einkuppeln gleicht beide Seiten sofort an.
// =====================================================================
public class ShaftClutch : MonoBehaviour, IShaftLink, ISaveable
{
    [Header("Anschluesse")]
    [Tooltip("Wellen-Port der Seite A (leer = erster Wellen-Port unter diesem Objekt).")]
    [SerializeField] private Port portA;

    [Tooltip("Wellen-Port der Seite B (leer = zweiter Wellen-Port unter diesem Objekt).")]
    [SerializeField] private Port portB;

    [Header("Mechanik")]
    [Tooltip("Traegheit jeder Seite in kg*m^2.")]
    [SerializeField] private float inertiaPerSide = 1.5f;

    [Header("Rutschkupplung")]
    [Tooltip("Hoechstes Drehmoment (Nm), das die Kupplung uebertraegt. 0 = rutscht nie. " +
             "Zum Vergleich: Dampfmaschine 60 kW bei 1500 U/min = etwa 380 Nm.")]
    [SerializeField] private float ratedTorqueNm = 600f;

    [Tooltip("Unterschied der Drehzahlen (U/min), unter dem sie fest greift.")]
    [SerializeField] private float lockToleranceRpm = 15f;

    [Tooltip("So lange (Sekunden) rutscht sie mindestens, bevor sie wieder fest greift (gegen Flattern).")]
    [SerializeField] private float minSlipSeconds = 0.4f;

    [Header("Anzeige")]
    [Tooltip("Teil, das sich mit Seite A dreht.")]
    [SerializeField] private Transform rotorA;
    [Tooltip("Teil, das sich mit Seite B dreht.")]
    [SerializeField] private Transform rotorB;
    [SerializeField] private Vector3 rotorAxis = Vector3.right;
    [Tooltip("Optik: Wie viel der echten Drehung gezeigt wird (1 = echt, kleiner = langsamer).")]
    [SerializeField] private float rotorVisualFactor = 0.05f;

    [Header("Schalter (optional)")]
    [SerializeField] private MachineSwitch machineSwitch;

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 2f;

    private readonly ShaftPart sideA = new ShaftPart();
    private readonly ShaftPart sideB = new ShaftPart();
    private RotorSpin spinA;
    private RotorSpin spinB;

    private enum State { Open, Slipping, Locked }

    private State state = State.Locked;
    private State lastState = State.Locked;
    private float slipTimer;
    private float transmitted;

    // Schalter eingeschaltet (Ohne Schalter immer).
    private bool SwitchOn
    {
        get { return machineSwitch == null || machineSwitch.IsOn; }
    }

    // Fuer das Wellennetz: Nur fest eingekuppelt sind beide Seiten starr verbunden.
    public bool Engaged
    {
        get { return state == State.Locked; }
    }

    // Drehmoment, das gerade durch die Kupplung geht (Nm).
    public float TransmittedTorque { get { return transmitted; } }

    public void ReportTorque(float nm)
    {
        transmitted = nm;
    }

    // Rutschmoment: Die schnellere Seite zieht die langsamere. Wird bei kleinem Unterschied sanft.
    private float SlipTorque(float rpmA, float rpmB)
    {
        float diff = (rpmA - rpmB) * Mathf.PI / 30f;
        return ratedTorqueNm * Mathf.Clamp(diff / 10f, -1f, 1f);
    }

    public IShaftDevice SideA { get { return sideA; } }
    public IShaftDevice SideB { get { return sideB; } }
    public float Ratio { get { return 1f; } }
    public float Efficiency { get { return 1f; } }

    private void Awake()
    {
        if (machineSwitch == null)
            machineSwitch = GetComponentInChildren<MachineSwitch>();

        FindPorts();

        sideA.Inertia = inertiaPerSide;
        sideB.Inertia = inertiaPerSide;
        sideA.Tick = TickA;
        sideB.Tick = TickB;

        // Beim Rutschen: Seite A verliert das Moment, das Seite B bekommt.
        sideA.Torque = rpm => state == State.Slipping ? -SlipTorque(rpm, sideB.Rpm) : 0f;
        sideB.Torque = rpm => state == State.Slipping ? SlipTorque(sideA.Rpm, rpm) : 0f;
    }

    // Die beiden Wellen-Ports finden, falls nicht eingetragen.
    private void FindPorts()
    {
        if (portA == null || portB == null)
        {
            foreach (Port p in GetComponentsInChildren<Port>(true))
            {
                if (p.RawMedium != PortMedium.Shaft || p == portA || p == portB)
                    continue;

                if (portA == null)
                    portA = p;
                else if (portB == null)
                    portB = p;
            }
        }

        if (portA == null || portB == null)
            Debug.LogWarning("ShaftClutch: Es werden zwei Wellen-Ports gebraucht (Seite A und B)!", this);

        sideA.Ports = portA != null ? new[] { portA } : new Port[0];
        sideB.Ports = portB != null ? new[] { portB } : new Port[0];
    }

    private void OnEnable()
    {
        state = SwitchOn ? State.Locked : State.Open;
        lastState = state;
        ShaftGrid.Register(sideA, this);
        ShaftGrid.Register(sideB, this);
        ShaftGrid.RegisterLink(this, this);
    }

    private void OnDisable()
    {
        ShaftGrid.Unregister(sideA);
        ShaftGrid.Unregister(sideB);
        ShaftGrid.UnregisterLink(this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        float rpmDiff = Mathf.Abs(sideA.Rpm - sideB.Rpm);

        if (!SwitchOn)
        {
            state = State.Open;
        }
        else if (state == State.Open)
        {
            // Einkuppeln: bei gleicher Drehzahl fest, sonst erst rutschen.
            if (ratedTorqueNm <= 0f || rpmDiff < lockToleranceRpm)
            {
                state = State.Locked;
            }
            else
            {
                state = State.Slipping;
                slipTimer = 0f;
            }
        }
        else if (state == State.Slipping)
        {
            slipTimer += dt;

            if (slipTimer >= minSlipSeconds && rpmDiff < lockToleranceRpm)
                state = State.Locked;
        }
        else if (state == State.Locked)
        {
            // Ueberlast: durchrutschen.
            if (ratedTorqueNm > 0f && transmitted > ratedTorqueNm)
            {
                state = State.Slipping;
                slipTimer = 0f;
            }
        }

        // Zustand geaendert: Inseln neu bilden.
        if (state != lastState)
        {
            lastState = state;
            ShaftGrid.Invalidate();
        }
    }

    private void TickA(float rpm, float dt)
    {
        Spin(ref spinA, rotorA, rpm, dt);
    }

    private void TickB(float rpm, float dt)
    {
        Spin(ref spinB, rotorB, rpm, dt);
    }

    private void Spin(ref RotorSpin spin, Transform rotor, float rpm, float dt)
    {
        if (rotor == null)
            return;

        if (spin == null)
            spin = new RotorSpin(rotor, rotorAxis);

        spin.Rotate(rpm * 6f * rotorVisualFactor * dt);
    }

    // ---------- Anzeige ----------

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
            state == State.Locked ? "shaftclutch.engaged" :
            state == State.Slipping ? "shaftclutch.slipping" : "shaftclutch.disengaged");

        string limit = ratedTorqueNm > 0f ? ratedTorqueNm.ToString("F0") : "-";

        string text = Loc.T("shaftclutch.info", status,
            sideA.Rpm.ToString("F0"), sideB.Rpm.ToString("F0"),
            transmitted.ToString("F0"), limit);

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("rpmA", sideA.Rpm);
        bag.SetFloat("rpmB", sideB.Rpm);
    }

    public void LoadState(StateBag bag)
    {
        sideA.Rpm = Mathf.Max(0f, bag.GetFloat("rpmA", sideA.Rpm));
        sideB.Rpm = Mathf.Max(0f, bag.GetFloat("rpmB", sideB.Rpm));
    }
}
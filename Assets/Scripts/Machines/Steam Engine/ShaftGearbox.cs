using UnityEngine;

// =====================================================================
//  ShaftGearbox: ein Getriebe auf der Welle. Die Ausgangsseite dreht schneller oder langsamer als die Eingangsseite;
//  das Drehmoment verhaelt sich umgekehrt (Leistung bleibt gleich, minus Verluste).
//
//  Beispiel: Uebersetzung 0,5 = Ausgang dreht halb so schnell, hat aber doppeltes Drehmoment. Gut, um ein schweres
//  Foerderband mit einer schnellen Dampfmaschine anzutreiben. Uebersetzung 2 = Ausgang doppelt so schnell.
//
//  Ports im Prefab: ZWEI Wellen-Ports, EINGANG ("Port In") und AUSGANG ("Port Out"). Leer = die ersten beiden
//  Wellen-Ports in der Reihenfolge der Kinder.
//
//  Schalter: ein MachineSwitch macht daraus ein schaltbares Getriebe (Aus = Leerlauf, beide Seiten getrennt).
//
//  UEBERLAST: Das Getriebe haelt "Rated Torque Nm" dauerhaft aus. Bei mehr Moment erhitzt es sich, und nach einiger
//  Zeit geht es kaputt: Der Schalter faellt auf AUS (wie bei einer Sicherung), die Seiten sind getrennt. Zum
//  Zuruecksetzen den Schalter wieder einschalten. Ohne eingetragenen Schalter legt das Skript selbst einen an.
//  Mit 0 bei "Rated Torque Nm" gibt es keine Grenze.
// =====================================================================
public class ShaftGearbox : MonoBehaviour, IShaftLink, ISaveable
{
    [Header("Anschluesse")]
    [SerializeField] private Port portIn;
    [SerializeField] private Port portOut;

    [Header("Uebersetzung")]
    [Tooltip("Drehzahl am Ausgang geteilt durch Drehzahl am Eingang. 0.5 = halb so schnell, 2 = doppelt so schnell.")]
    [SerializeField] private float ratio = 0.5f;

    [Tooltip("Wirkungsgrad (1 = verlustfrei). Bei 0.95 gehen 5 % der Leistung verloren.")]
    [Range(0.5f, 1f)]
    [SerializeField] private float efficiency = 0.95f;

    [Header("Mechanik")]
    [SerializeField] private float inertiaIn = 2f;
    [SerializeField] private float inertiaOut = 2f;

    [Header("Ueberlast")]
    [Tooltip("Dauerhaft zulaessiges Drehmoment am Ausgang (Nm). 0 = keine Grenze.")]
    [SerializeField] private float ratedTorqueNm = 800f;

    [Tooltip("Nach wie vielen Sekunden geht es bei doppeltem Moment kaputt?")]
    [SerializeField] private float tripSecondsAtDouble = 3f;

    [Tooltip("So lange (Sekunden) braucht es zum Abkuehlen.")]
    [SerializeField] private float coolSeconds = 12f;

    [Header("Anzeige")]
    [SerializeField] private Transform rotorIn;
    [SerializeField] private Transform rotorOut;
    [SerializeField] private Vector3 rotorAxis = Vector3.right;
    [SerializeField] private float rotorVisualFactor = 0.05f;

    [Header("Schalter (optional)")]
    [SerializeField] private MachineSwitch machineSwitch;

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 2f;

    private readonly ShaftPart sideIn = new ShaftPart();
    private readonly ShaftPart sideOut = new ShaftPart();
    private RotorSpin spinIn;
    private RotorSpin spinOut;
    private bool lastEngaged = true;
    private float heat;
    private bool broken;
    private float transmitted;

    public bool Engaged
    {
        get { return !broken && (machineSwitch == null || machineSwitch.IsOn); }
    }

    public void ReportTorque(float nm)
    {
        transmitted = nm;
    }

    public IShaftDevice SideA { get { return sideIn; } }
    public IShaftDevice SideB { get { return sideOut; } }
    public float Ratio { get { return Mathf.Max(0.01f, ratio); } }
    public float Efficiency { get { return efficiency; } }

    private void Awake()
    {
        if (machineSwitch == null)
            machineSwitch = GetComponentInChildren<MachineSwitch>();

        // Mit Ueberlastschutz braucht das Getriebe einen Schalter zum Zuruecksetzen.
        if (machineSwitch == null && ratedTorqueNm > 0f)
        {
            machineSwitch = gameObject.AddComponent<MachineSwitch>();
            machineSwitch.Label = "@shaftgear.label";
        }

        if (portIn == null || portOut == null)
        {
            foreach (Port p in GetComponentsInChildren<Port>(true))
            {
                if (p.RawMedium != PortMedium.Shaft || p == portIn || p == portOut)
                    continue;

                if (portIn == null)
                    portIn = p;
                else if (portOut == null)
                    portOut = p;
            }
        }

        if (portIn == null || portOut == null)
            Debug.LogWarning("ShaftGearbox: Es werden zwei Wellen-Ports gebraucht (Eingang und Ausgang)!", this);

        sideIn.Ports = portIn != null ? new[] { portIn } : new Port[0];
        sideOut.Ports = portOut != null ? new[] { portOut } : new Port[0];
        sideIn.Inertia = inertiaIn;
        sideOut.Inertia = inertiaOut;
        sideIn.Tick = (rpm, dt) => Spin(ref spinIn, rotorIn, rpm, dt);
        sideOut.Tick = (rpm, dt) => Spin(ref spinOut, rotorOut, rpm, dt);
    }

    private void OnEnable()
    {
        lastEngaged = Engaged;

        if (machineSwitch != null)
            machineSwitch.Changed += OnSwitchChanged;

        ShaftGrid.Register(sideIn, this);
        ShaftGrid.Register(sideOut, this);
        ShaftGrid.RegisterLink(this, this);
    }

    private void OnDisable()
    {
        if (machineSwitch != null)
            machineSwitch.Changed -= OnSwitchChanged;

        ShaftGrid.Unregister(sideIn);
        ShaftGrid.Unregister(sideOut);
        ShaftGrid.UnregisterLink(this);
    }

    // Einschalten setzt den Defekt zurueck.
    private void OnSwitchChanged(bool on)
    {
        if (on)
            broken = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (Engaged)
        {
            heat = Overload.Step(heat, transmitted, ratedTorqueNm, tripSecondsAtDouble, coolSeconds, dt);

            if (heat >= 1f)
                Break();
        }
        else
        {
            heat = Mathf.Max(0f, heat - dt / Mathf.Max(0.01f, coolSeconds));
        }

        if (Engaged != lastEngaged)
        {
            lastEngaged = Engaged;
            ShaftGrid.Invalidate();
        }
    }

    private void Break()
    {
        broken = true;
        heat = 1f;

        if (machineSwitch != null)
            machineSwitch.SetOn(false);

        Debug.LogWarning("Getriebe ueberlastet: " + name + " (" + transmitted.ToString("F0") + " Nm, erlaubt " +
                         ratedTorqueNm.ToString("F0") + " Nm). Zum Zuruecksetzen den Schalter einschalten.");
    }

    private void Spin(ref RotorSpin spin, Transform rotor, float rpm, float dt)
    {
        if (rotor == null)
            return;

        if (spin == null)
            spin = new RotorSpin(rotor, rotorAxis);

        spin.Rotate(rpm * 6f * rotorVisualFactor * dt);
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

        string limit = ratedTorqueNm > 0f ? ratedTorqueNm.ToString("F0") : "-";

        string text = Loc.T("shaftgear.info",
            Ratio.ToString("0.##"), sideIn.Rpm.ToString("F0"), sideOut.Rpm.ToString("F0"),
            transmitted.ToString("F0"), limit);

        if (broken)
            text = Loc.T("shaftgear.broken") + "\n" + text;
        else if (!Engaged)
            text = Loc.T("shaftclutch.disengaged") + "\n" + text;

        GameSettings.DrawBoxAbove(screen, text);
    }

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("rpmIn", sideIn.Rpm);
        bag.SetFloat("rpmOut", sideOut.Rpm);
    }

    public void LoadState(StateBag bag)
    {
        sideIn.Rpm = Mathf.Max(0f, bag.GetFloat("rpmIn", sideIn.Rpm));
        sideOut.Rpm = Mathf.Max(0f, bag.GetFloat("rpmOut", sideOut.Rpm));
    }
}
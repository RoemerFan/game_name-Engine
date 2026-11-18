using UnityEngine;

// =====================================================================
//  ShaftDrive: macht eine Maschine (Foerderband, Pumpe) zusaetzlich zu einer WELLEN-Maschine.
//  Wird ans Maschinen-Prefab gehaengt, neben ConveyorBelt oder ElectricPump.
//
//  Ports im Prefab: ein Wellen-Port (Port-Stoff "Welle").
//
//  So arbeitet es:
//   - Ist der Wellen-Port NICHT angeschlossen, laeuft die Maschine wie bisher mit Strom.
//   - Ist die Welle angeschlossen, treibt sie die Maschine an. Der Strom zaehlt dann nicht mehr.
//     Das Tempo folgt der Drehzahl (volles Tempo bei "Rated Rpm"). Die Maschine bremst die Welle mit
//     ihrem Moment: je mehr Last, desto mehr (siehe "Rated Power KW" und "Idle Load").
//   - Ist die Maschine ausgeschaltet (MachineSwitch), belastet sie die Welle nicht.
// =====================================================================
public class ShaftDrive : MonoBehaviour, IShaftDevice, ISaveable
{
    [Tooltip("Leistung (kW) bei voller Last und Nenndrehzahl.")]
    [SerializeField] private float ratedPowerKW = 3f;

    [Tooltip("Drehzahl, bei der die Maschine ihr volles Tempo hat (U/min).")]
    [SerializeField] private float ratedRpm = 1500f;

    [Tooltip("So viel Last (Anteil) hat die Maschine, auch wenn sie nichts zu tun hat.")]
    [Range(0f, 1f)]
    [SerializeField] private float idleLoad = 0.15f;

    [Tooltip("Unter diesem Anteil der Nenndrehzahl steht die Maschine.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float minSpeedFraction = 0.1f;

    [Tooltip("Traegheit in kg*m^2.")]
    [SerializeField] private float inertia = 2f;

    [Header("Anzeige")]
    [Tooltip("Optional: Teil, das sich mit der Welle dreht (Antriebsrad).")]
    [SerializeField] private Transform rotor;
    [SerializeField] private Vector3 rotorAxis = Vector3.forward;
    [SerializeField] private float rotorVisualFactor = 0.05f;

    private IShaftDriven machine;
    private Port shaftPort;
    private float rpm;
    private RotorSpin spin;

    // Treibt die Welle die Maschine gerade? (Port angeschlossen und Maschine an)
    public bool IsDriving
    {
        get { return machine != null && shaftPort != null && shaftPort.IsConnected; }
    }

    public float Rpm { get { return rpm; } }

    private void Awake()
    {
        machine = GetComponentInParent<IShaftDriven>();

        if (machine == null)
            machine = GetComponentInChildren<IShaftDriven>();

        foreach (Port p in GetComponentsInChildren<Port>(true))
        {
            if (p.RawMedium == PortMedium.Shaft)
            {
                shaftPort = p;
                break;
            }
        }

        if (machine == null)
            Debug.LogWarning("ShaftDrive: Keine Maschine (ConveyorBelt/ElectricPump) gefunden!", this);

        if (shaftPort == null)
            Debug.LogWarning("ShaftDrive: Kein Wellen-Port gefunden!", this);
    }

    private void OnEnable()
    {
        ShaftGrid.Register(this);
    }

    private void OnDisable()
    {
        ShaftGrid.Unregister(this);

        if (machine != null)
            machine.SetShaftSupply(0f, false);
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
        if (!IsDriving || !machine.ShaftOn)
            return 0f;

        float load = Mathf.Lerp(idleLoad, 1f, Mathf.Clamp01(machine.ShaftLoad01));
        float ratedOmega = Mathf.Max(1f, ratedRpm * Mathf.PI / 30f);
        float ratedTorque = ratedPowerKW * 1000f / ratedOmega;

        // Konstantes Lastmoment (wie ein Foerderband). Es bremst nur, was dreht.
        return -ratedTorque * load * Mathf.Clamp01(r / 30f);
    }

    public void ShaftTick(float r, float dt)
    {
        rpm = r;

        if (machine != null)
        {
            if (IsDriving)
            {
                float fraction = Mathf.Clamp01(rpm / Mathf.Max(1f, ratedRpm));
                machine.SetShaftSupply(fraction < minSpeedFraction ? 0f : fraction, true);
            }
            else
            {
                machine.SetShaftSupply(0f, false);
            }
        }

        if (rotor != null)
        {
            if (spin == null)
                spin = new RotorSpin(rotor, rotorAxis);

            spin.Rotate(rpm * 6f * rotorVisualFactor * dt);
        }
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
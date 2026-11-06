using UnityEngine;

// Elektrische Wasserpumpe: braucht Strom und foerdert Wasser zum Ausgang.
// Ohne Strom oder ausgeschaltet steht sie.
//
// Das Wasser kommt aus zwei Quellen:
//   1. Eigenfoerderung ("Own Liters Per Second"): Die Pumpe holt selbst Wasser
//      aus dem Boden, ohne dass etwas am Eingang haengen muss.
//   2. Wasser vom Eingang: Reicht die Eigenfoerderung nicht fuer die volle
//      Pumpleistung, wird zusaetzlich Wasser von der Leitung am Eingang genommen.
//
// Ports im Prefab: Water/Input, Water/Output, Power.
// Optional: ein MachineSwitch (Ein/Aus) irgendwo unter der Pumpe.
public class ElectricPump : MonoBehaviour, IPortOwner, IPowerConsumer, ISaveable
{
    [Header("Strom")]
    [SerializeField] private float ratedPowerKW = 8f;
    [SerializeField] private float idlePowerKW = 0.3f;
    [Tooltip("Hoeher = wichtiger. Unwichtige Verbraucher fallen zuerst aus.")]
    [SerializeField] private int importance = 10;

    [Header("Wasser")]
    [Tooltip("Hoechste Menge, die die Pumpe pro Sekunde zum Ausgang schiebt.")]
    [SerializeField] private float litersPerSecond = 6f;
    [Tooltip("Wasser, das die Pumpe selbst foerdert (pro Sekunde bei voller Stromversorgung). " +
             "Reicht das nicht fuer die volle Pumpleistung, wird der Rest vom Eingang genommen. " +
             "0 = nur Wasser vom Eingang.")]
    [SerializeField] private float ownLitersPerSecond = 4f;
    [SerializeField] private float bufferCapacity = 10f;

    [Header("Schalter (optional)")]
    [SerializeField] private MachineSwitch machineSwitch;

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [Tooltip("Optional: Leeres Objekt, an dessen Position der Info-Text erscheint. " +
             "Leer = Hoehe ueber dem Nullpunkt der Maschine (siehe 'Debug Height').")]
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 2f;

    private float buffer;
    private float supply;
    private float activity;
    private float movedThisFrame;

    private bool IsOn => machineSwitch == null || machineSwitch.IsOn;

    // Wenig Verbrauch im Leerlauf, volle Leistung beim Pumpen.
    public float DemandKW =>
        IsOn ? Mathf.Lerp(idlePowerKW, ratedPowerKW, activity) : 0f;

    public float RatedPowerKW => ratedPowerKW;
    public int Importance => importance;

    public void SetSupply(float fraction)
    {
        supply = Mathf.Clamp01(fraction);
    }

    private void Awake()
    {
        if (machineSwitch == null)
            machineSwitch = GetComponentInChildren<MachineSwitch>();
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

        if (dt <= 0f)
            return;

        float target = Mathf.Clamp01(
            movedThisFrame / (litersPerSecond * dt)
        );

        activity = Mathf.MoveTowards(activity, target, 2f * dt);
        movedThisFrame = 0f;

        // Eigenfoerderung: Die Pumpe fuellt ihren Puffer selbst auf, solange sie an ist
        // und Strom hat. Was danach noch fehlt, kommt vom Eingang (GiveTo).
        if (ownLitersPerSecond > 0f && IsOn && supply > 0f)
        {
            float own = Mathf.Min(
                ownLitersPerSecond * supply * dt,
                bufferCapacity - buffer);

            if (own > 0f)
                buffer += own;
        }
    }

    // ---------- IPortOwner ----------

    public float GetAvailable(Port port)
    {
        if (port.Medium != PortMedium.Water ||
            port.Direction != PortDirection.Output ||
            !IsOn)
            return 0f;

        // Foerdermenge haengt von der Stromversorgung ab.
        return Mathf.Min(
            buffer,
            litersPerSecond * supply * Time.deltaTime
        );
    }

    public float GetFreeSpace(Port port)
    {
        if (port.Medium == PortMedium.Water &&
            port.Direction == PortDirection.Input &&
            IsOn)
            return bufferCapacity - buffer;

        return 0f;
    }

    public float TakeFrom(Port port, float amount)
    {
        if (port.Medium != PortMedium.Water ||
            port.Direction != PortDirection.Output)
            return 0f;

        float taken = Mathf.Clamp(amount, 0f, buffer);
        buffer -= taken;
        movedThisFrame += taken;
        return taken;
    }

    public float GiveTo(Port port, float amount)
    {
        if (port.Medium != PortMedium.Water ||
            port.Direction != PortDirection.Input)
            return 0f;

        float accepted = Mathf.Clamp(amount, 0f, bufferCapacity - buffer);
        buffer += accepted;
        return accepted;
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
            !IsOn ? "pump.status.off"
            : supply <= 0.01f ? "pump.status.nopower"
            : activity > 0.05f ? "pump.status.pumping"
            : "pump.status.ready");

        string text = Loc.T(
            "pump.info",
            status,
            (supply * 100f).ToString("F0"),
            buffer.ToString("F1"), bufferCapacity,
            DemandKW.ToString("F1"));

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("buffer", buffer);
    }

    public void LoadState(StateBag bag)
    {
        buffer = Mathf.Clamp(bag.GetFloat("buffer", buffer), 0f, bufferCapacity);
    }
}
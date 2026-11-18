using UnityEngine;

// Sicherung: sitzt zwischen zwei Kabeln (zwei Power-Ports) und trennt bei
// Ueberlast. Der Schalthebel (MachineSwitch) faellt dann auf AUS und muss
// von Hand wieder eingeschaltet werden. Man kann sie auch absichtlich
// ausschalten, um einen Netzteil abzutrennen.
//
// Prefab: Modell mit festem Collider, PowerFuse, zwei Power-Ports.
// Optional: ein MachineSwitch (mit Hebel). Fehlt er, wird einer angelegt.
public class PowerFuse : MonoBehaviour, IPowerGate, ISaveable
{
    [Header("Sicherung")]
    [SerializeField] private float ratingKW = 25f;
    [Tooltip("Nach wie vielen Sekunden loest sie bei doppelter Last aus?")]
    [SerializeField] private float tripSecondsAtDoubleLoad = 2f;
    [SerializeField] private float coolSeconds = 8f;

    [Header("Bedienung")]
    [SerializeField] private MachineSwitch handle;

    [Header("Anzeigelampe (optional)")]
    [SerializeField] private Renderer indicator;
    [SerializeField] private Color closedColor = new Color(0.2f, 0.85f, 0.2f);
    [SerializeField] private Color warmColor = new Color(1f, 0.7f, 0.1f);
    [SerializeField] private Color trippedColor = new Color(0.95f, 0.15f, 0.1f);
    [SerializeField] private Color openColor = new Color(0.45f, 0.45f, 0.45f);

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [Tooltip("Optional: Leeres Objekt, an dessen Position der Info-Text erscheint. " +
             "Leer = Hoehe ueber dem Nullpunkt der Maschine (siehe 'Debug Height').")]
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 1.5f;

    private float heat;
    private float flowKW;
    private float reportedFlow;
    private bool tripped;
    private bool lastConducting;

    public float RatingKW => ratingKW;
    public float FlowKW => flowKW;
    public float Heat => heat;
    public bool IsTripped => tripped;

    public bool IsConducting =>
        !tripped && (handle == null || handle.IsOn);

    // Das Stromnetz meldet hier die Leistung, die gerade durchfliesst.
    public void ReportFlow(float kw)
    {
        reportedFlow = kw;
    }

    private void Awake()
    {
        if (handle == null)
            handle = GetComponentInChildren<MachineSwitch>();

        if (handle == null)
        {
            handle = gameObject.AddComponent<MachineSwitch>();
            handle.Label = "@fuse.label";
        }

        lastConducting = IsConducting;
    }

    private void OnEnable()
    {
        handle.Changed += OnHandleChanged;
        PowerGrid.Register(this);
    }

    private void OnDisable()
    {
        if (handle != null)
            handle.Changed -= OnHandleChanged;

        PowerGrid.Unregister(this);
    }

    private void OnHandleChanged(bool on)
    {
        // Einschalten setzt die Ausloesung zurueck.
        // Ist die Sicherung noch heiss, loest sie schnell wieder aus.
        if (on)
            tripped = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        bool conducting = IsConducting;

        flowKW = conducting ? reportedFlow : 0f;
        reportedFlow = 0f;

        if (conducting)
        {
            heat = Overload.Step(
                heat, flowKW, ratingKW,
                tripSecondsAtDoubleLoad, coolSeconds, dt
            );

            if (heat >= 1f)
                Trip();
        }
        else
        {
            heat = Mathf.Max(0f, heat - dt / Mathf.Max(0.01f, coolSeconds));
        }

        // Aenderung des Zustands: Netz neu aufbauen.
        conducting = IsConducting;

        if (conducting != lastConducting)
        {
            lastConducting = conducting;
            PowerGrid.TopologyChanged();
        }

        UpdateVisuals();
    }

    private void Trip()
    {
        tripped = true;
        heat = 1f;

        // Der Hebel faellt auf AUS.
        handle.SetOn(false);

        PowerGrid.LogEvent(Loc.T("grid.event.fuse",
            Loc.Resolve(handle.Label), flowKW.ToString("F1"), ratingKW.ToString("F0")), 2);

        Debug.LogWarning(
            "Sicherung ausgeloest: " + name + " ("
            + flowKW.ToString("F1") + " kW bei "
            + ratingKW.ToString("F0") + " kW)",
            this
        );
    }

    private void UpdateVisuals()
    {
        handle.Info = tripped
            ? Loc.T("fuse.handle.tripped")
            : Loc.T("fuse.handle.load",
                flowKW.ToString("F1"), ratingKW.ToString("F0"));

        if (indicator == null)
            return;

        Color color;

        if (tripped)
            color = trippedColor;
        else if (!IsConducting)
            color = openColor;
        else
            color = Color.Lerp(closedColor, warmColor, Mathf.Clamp01(heat * 2f));

        indicator.material.color = color;
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
            tripped ? "fuse.status.tripped"
            : !IsConducting ? "fuse.status.off"
            : "fuse.status.on");

        string text = Loc.T(
            "fuse.info",
            status,
            flowKW.ToString("F1"), ratingKW.ToString("F0"),
            (heat * 100f).ToString("F0"));

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern ----------
    // Der Griff (MachineSwitch) speichert sich selbst und wird vorher geladen.

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("heat", heat);
        bag.SetBool("tripped", tripped);
    }

    public void LoadState(StateBag bag)
    {
        heat = Mathf.Clamp01(bag.GetFloat("heat", heat));
        tripped = bag.GetBool("tripped", tripped);
        lastConducting = IsConducting;

        PowerGrid.TopologyChanged();
    }
}
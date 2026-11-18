using UnityEngine;

// Strommessgeraet: Wird mitten in ein Kabel gesetzt (zwei Power-Ports) und
// zeigt, wie viel Leistung gerade durch die Stelle fliesst.
// Beeinflusst das Netz nicht, es leitet immer durch.
//
// Prefab: Modell mit festem Collider, dieses Skript, zwei Power-Ports.
public class PowerMeter : MonoBehaviour, IPowerGate, ISaveable
{
    [SerializeField] private string label = "@meter.label";

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [Tooltip("Optional: Leeres Objekt, an dessen Position der Info-Text erscheint. " +
             "Leer = Hoehe ueber dem Nullpunkt der Maschine (siehe 'Debug Height').")]
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 1.5f;

    private float reportedFlow;
    private float shownKW;
    private float totalKWh;

    // Fuer andere Skripte (z. B. spaeter ein Zeigerinstrument am Modell).
    public float FlowKW => shownKW;
    public float TotalKWh => totalKWh;

    // Ein Messgeraet trennt das Netz nie.
    public bool IsConducting => true;

    // Das Stromnetz meldet hier die Leistung, die gerade hindurchfliesst.
    public void ReportFlow(float kw)
    {
        reportedFlow = kw;
    }

    private void OnEnable()
    {
        // Beim Netz anmelden, damit es uns als Teil des Kabels erkennt.
        PowerGrid.Register(this);
    }

    private void OnDisable()
    {
        PowerGrid.Unregister(this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // Gemeldeten Wert abholen und fuer das naechste Bild zuruecksetzen.
        float current = reportedFlow;
        reportedFlow = 0f;

        // Anzeige glaetten, damit sie nicht zittert.
        shownKW = Mathf.Lerp(shownKW, current, 1f - Mathf.Exp(-4f * dt));

        // Gesamtverbrauch in Kilowattstunden (Spielzeit).
        totalKWh += shownKW * dt / 3600f;
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

        string text =
            Loc.T("meter.info",
                Loc.Resolve(label),
                shownKW.ToString("F1"),
                totalKWh.ToString("F3"));

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("kwh", totalKWh);
    }

    public void LoadState(StateBag bag)
    {
        totalKWh = Mathf.Max(0f, bag.GetFloat("kwh", totalKWh));
    }
}
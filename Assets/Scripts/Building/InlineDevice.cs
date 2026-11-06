using UnityEngine;

// Zwischenstation fuer Wasser-, Dampf- und Ruß-Leitungen.
// Das Geraet sitzt mitten in einer Leitung: Es hat mindestens einen Eingang
// und einen Ausgang (Port-Objekte als Kinder) und leitet alles weiter.
// Je nach Einstellung ist es:
//   - Messgeraet:  zeigt, wie viel pro Sekunde durchfliesst und die Gesamtmenge
//   - Verteiler:   mehrere Ausgaenge, die Menge wird aufgeteilt (auch ungleich)
//   - Ventil:      MachineSwitch dazu = auf/zu; "Open Fraction" = Drossel
//   - Ablass:      "Drain Per Second" > 0 laesst Inhalt ins Freie ab
// Alle Funktionen sind frei kombinierbar.
//
// Wichtig: Alle Ports eines Geraets muessen denselben Stoff haben.
public class InlineDevice : MonoBehaviour, IPortOwner, ISaveable
{
    [Header("Allgemein")]
    [SerializeField] private string label = "@inline.label";

    [Tooltip("Wie viel das Geraet kurzzeitig zwischenspeichern kann.")]
    [SerializeField] private float bufferCapacity = 4f;

    [Tooltip("Hoechste Menge pro Sekunde, die das Geraet weitergibt.")]
    [SerializeField] private float maxThroughputPerSecond = 5f;

    [Tooltip("Drossel: 1 = ganz offen, 0 = zu.")]
    [Range(0f, 1f)]
    [SerializeField] private float openFraction = 1f;

    [Header("Ventil (optional)")]
    [Tooltip("Ein MachineSwitch unter diesem Geraet: AUS = Ventil zu.")]
    [SerializeField] private MachineSwitch machineSwitch;

    [Header("Verteilen")]
    [Tooltip("Gewichte der Ausgaenge in der Reihenfolge, wie die Ports in der " +
             "Hierarchy stehen. Leer = alle gleich. Beispiel 2 und 1: Der erste " +
             "Ausgang bekommt doppelt so viel.")]
    [SerializeField] private float[] outputWeights;

    [Header("Ablassen")]
    [Tooltip("Menge pro Sekunde, die ins Freie abfliesst. 0 = kein Ablass.")]
    [SerializeField] private float drainPerSecond = 0f;

    [Tooltip("Optional: Partikel (Spritzen, Dampf), spielen solange abgelassen wird. " +
             "Play On Awake am Partikelsystem ausschalten.")]
    [SerializeField] private ParticleSystem drainEffect;

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [Tooltip("Optional: Leeres Objekt, an dessen Position der Info-Text erscheint. " +
             "Leer = Hoehe ueber dem Nullpunkt der Maschine (siehe 'Debug Height').")]
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 1.5f;

    private Port[] ports;
    private Port[] outputs;

    // Pro Ausgang: Wie viel darf in diesem Bild noch abgegeben werden.
    private float[] allowance;

    private PortMedium medium;
    private float buffer;

    // Messwerte.
    private float passedThisFrame;
    private float drainedThisFrame;
    private float flowPerSecond;
    private float total;

    // Fuer andere Skripte (z. B. spaeter ein Zeigerinstrument am Modell).
    public float FlowPerSecond => flowPerSecond;
    public float TotalPassed => total;

    // Ventil ist offen, wenn es keinen Schalter gibt oder er an ist.
    public bool IsOpen => machineSwitch == null || machineSwitch.IsOn;

    private void Awake()
    {
        // Alle Ports dieses Geraets sammeln.
        ports = GetComponentsInChildren<Port>(true);

        // Die Ausgaenge extra merken, sie bekommen einen Anteil zugeteilt.
        System.Collections.Generic.List<Port> outList =
            new System.Collections.Generic.List<Port>();

        foreach (Port port in ports)
        {
            if (port.Direction == PortDirection.Output)
                outList.Add(port);
        }

        outputs = outList.ToArray();
        allowance = new float[outputs.Length];

        // Der Stoff des Geraets ist der Stoff des ersten Ports.
        if (ports.Length > 0)
            medium = ports[0].Medium;

        foreach (Port port in ports)
        {
            if (port.Medium != medium)
            {
                Debug.LogWarning(
                    "InlineDevice: Alle Ports sollten denselben Stoff haben!",
                    this
                );
                break;
            }
        }

        if (machineSwitch == null)
            machineSwitch = GetComponentInChildren<MachineSwitch>();

        // Abfluss-Effekt anfangs aus.
        if (drainEffect != null)
            drainEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Update()
    {
        // Ablassen: Ein Teil des Inhalts laeuft pro Sekunde ins Freie.
        if (drainPerSecond > 0f && IsOpen && buffer > 0f)
        {
            float amount = Mathf.Min(
                buffer,
                drainPerSecond * openFraction * Time.deltaTime
            );

            buffer -= amount;
            drainedThisFrame += amount;
            total += amount;
        }
    }

    // LateUpdate laeuft nach allen Update-Aufrufen des Bildes. Dort werden
    // die Messwerte gebucht und die Mengen fuer das naechste Bild verteilt.
    private void LateUpdate()
    {
        float dt = Time.deltaTime;

        if (dt <= 0f)
            return;

        // Durchfluss dieses Bildes in "pro Sekunde" umrechnen und glaetten,
        // damit die Anzeige nicht flackert.
        float instant = (passedThisFrame + drainedThisFrame) / dt;
        flowPerSecond = Mathf.Lerp(flowPerSecond, instant, 1f - Mathf.Exp(-3f * dt));

        UpdateDrainEffect(drainedThisFrame > 0f);

        passedThisFrame = 0f;
        drainedThisFrame = 0f;

        RefreshAllowances(dt);
    }

    // Berechnet, wie viel jeder Ausgang im naechsten Bild abgeben darf.
    private void RefreshAllowances(float dt)
    {
        // Gesamtgewicht aller angeschlossenen Ausgaenge.
        float weightSum = 0f;

        for (int i = 0; i < outputs.Length; i++)
        {
            if (outputs[i].IsConnected)
                weightSum += GetWeight(i);
        }

        // Wie viel darf insgesamt hinaus: nicht mehr als im Puffer ist,
        // nicht mehr als der Durchsatz erlaubt, und nichts bei geschlossenem Ventil.
        float releasable = IsOpen
            ? Mathf.Min(buffer, maxThroughputPerSecond * openFraction * dt)
            : 0f;

        // Anteil pro Ausgang nach Gewicht.
        for (int i = 0; i < outputs.Length; i++)
        {
            bool usable = outputs[i].IsConnected && weightSum > 0f;
            allowance[i] = usable ? releasable * GetWeight(i) / weightSum : 0f;
        }
    }

    private float GetWeight(int index)
    {
        if (outputWeights != null && index < outputWeights.Length)
            return Mathf.Max(0f, outputWeights[index]);

        return 1f;
    }

    private void UpdateDrainEffect(bool draining)
    {
        if (drainEffect == null)
            return;

        if (draining && !drainEffect.isEmitting)
            drainEffect.Play(true);
        else if (!draining && drainEffect.isEmitting)
            drainEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private int OutputIndex(Port port)
    {
        for (int i = 0; i < outputs.Length; i++)
        {
            if (outputs[i] == port)
                return i;
        }

        return -1;
    }

    // ---------- IPortOwner: So sprechen die Ports mit dem Geraet ----------

    // Ausgang: Wie viel kann gerade abgegeben werden?
    public float GetAvailable(Port port)
    {
        int index = OutputIndex(port);
        return index >= 0 ? allowance[index] : 0f;
    }

    // Eingang: Wie viel Platz ist noch frei?
    public float GetFreeSpace(Port port)
    {
        if (port.Direction != PortDirection.Input || !IsOpen)
            return 0f;

        return Mathf.Max(0f, bufferCapacity - buffer);
    }

    // Ausgang: Menge wird entnommen und fliesst zum naechsten Geraet.
    public float TakeFrom(Port port, float amount)
    {
        int index = OutputIndex(port);

        if (index < 0)
            return 0f;

        float taken = Mathf.Clamp(amount, 0f, Mathf.Min(allowance[index], buffer));

        allowance[index] -= taken;
        buffer -= taken;

        // Fuer die Messung.
        passedThisFrame += taken;
        total += taken;

        return taken;
    }

    // Eingang: Menge kommt vom vorigen Geraet herein.
    public float GiveTo(Port port, float amount)
    {
        if (port.Direction != PortDirection.Input)
            return 0f;

        float accepted = Mathf.Clamp(
            amount, 0f, Mathf.Max(0f, bufferCapacity - buffer)
        );

        buffer += accepted;
        return accepted;
    }

    // ---------- Anzeige ----------

    private static string MediumName(PortMedium m)
    {
        switch (m)
        {
            case PortMedium.Water: return Loc.T("medium.water");
            case PortMedium.Steam: return Loc.T("medium.steam");
            case PortMedium.Soot: return Loc.T("medium.soot");
            default: return Loc.T("medium.power");
        }
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

        // Nur in der Naehe und nur wenn das Geraet im Bild ist.
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

        string status =
            !IsOpen ? Loc.T("inline.status.closed")
            : openFraction < 0.99f
                ? Loc.T("inline.status.throttled", (openFraction * 100f).ToString("F0"))
            : Loc.T("inline.status.open");

        string text = Loc.T(
            "inline.info",
            Loc.Resolve(label), status, MediumName(medium),
            flowPerSecond.ToString("F1"), total.ToString("F1"),
            buffer.ToString("F1"), bufferCapacity.ToString("F0"));

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("buffer", buffer);
        bag.SetFloat("total", total);
    }

    public void LoadState(StateBag bag)
    {
        buffer = Mathf.Clamp(bag.GetFloat("buffer", buffer), 0f, bufferCapacity);
        total = Mathf.Max(0f, bag.GetFloat("total", total));
    }
}
using System.Collections.Generic;
using UnityEngine;

// Universal-Verteiler: EIN Bauteil fuer Wasser, Dampf, Russ UND Strom.
//
// Alle Ports dieses Bauteils sind "universell" (Medium = Any). Sobald an
// irgendeinem davon etwas angesteckt wird, nehmen ALLE Ports des Bauteils
// dessen Stoff an. Wird alles wieder abgesteckt, ist das Bauteil wieder neutral.
// Man braucht also keine getrennten Verteiler fuer Wasser, Dampf und Strom.
//
// Einrichten im Prefab:
//   1. Dieses Skript auf die Wurzel des Prefabs legen.
//   2. Beliebig viele Kind-Objekte mit einem Port anlegen und beim Port
//      "Medium" auf "Any" stellen (Richtung ist egal, sie wird ignoriert).
//
// Verhalten je nach Stoff:
//   - Wasser/Dampf/Russ: Das Bauteil hat einen kleinen Puffer und verteilt den
//     Inhalt gleichmaessig auf alle angesteckten Ports, die etwas aufnehmen koennen.
//   - Strom: Das Bauteil ist eine reine Verteilerdose und leitet nur weiter
//     (wie PowerJunction).
//   - Kette: Haengen mehrere Universal-Verteiler hintereinander, gibt der Stoff
//     von der Quelle an alle weiter.
public class UniversalJunction : MonoBehaviour, IPortOwner, ISaveable
{
    [Header("Fluessigkeiten / Gase")]
    [Tooltip("Wie viel das Bauteil kurzzeitig zwischenspeichern kann.")]
    [SerializeField] private float bufferCapacity = 4f;

    [Tooltip("Hoechste Menge pro Sekunde, die das Bauteil weitergibt.")]
    [SerializeField] private float maxThroughputPerSecond = 5f;

    [Header("Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [SerializeField] private float debugHeight = 1.2f;

    private Port[] ports;
    private float[] allowance;

    private float buffer;

    // Zwischenspeicher fuer den aktuellen Stoff. Wird nur neu berechnet,
    // wenn irgendwo ein Kabel/Rohr gesteckt oder gezogen wurde.
    private int cachedVersion = -1;
    private PortMedium cachedMedium = PortMedium.Any;

    // Welcher Stoff zuletzt verarbeitet wurde (fuer das Erkennen von Wechseln).
    private PortMedium lastMedium = PortMedium.Any;
    private bool registeredInGrid;

    // Der Stoff dieses Bauteils: der Stoff, der an einer Seite angesteckt ist.
    // "Any" = noch nichts angesteckt.
    public PortMedium CurrentMedium
    {
        get
        {
            if (cachedVersion != Port.ConnectionVersion)
            {
                cachedVersion = Port.ConnectionVersion;
                cachedMedium = FindMedium(new HashSet<UniversalJunction>());
            }

            return cachedMedium;
        }
    }

    // Sammelt die universellen Ports (auch schon vor Awake benutzbar).
    private void EnsurePorts()
    {
        if (ports != null)
            return;

        List<Port> list = new List<Port>();

        foreach (Port p in GetComponentsInChildren<Port>(true))
        {
            if (p.RawMedium == PortMedium.Any)
                list.Add(p);
        }

        ports = list.ToArray();
        allowance = new float[ports.Length];
    }

    // Sucht den Stoff: Erst direkt angesteckte feste Ports, danach
    // (ueber andere Universal-Verteiler hinweg) deren Stoff.
    // "seen" verhindert Endlosschleifen bei Ringen.
    private PortMedium FindMedium(HashSet<UniversalJunction> seen)
    {
        if (!seen.Add(this))
            return PortMedium.Any;

        EnsurePorts();

        foreach (Port p in ports)
        {
            Port other = p.Connected;

            if (other != null && other.RawMedium != PortMedium.Any)
                return other.RawMedium;
        }

        foreach (Port p in ports)
        {
            Port other = p.Connected;

            if (other == null || other.Junction == null)
                continue;

            PortMedium m = other.Junction.FindMedium(seen);

            if (m != PortMedium.Any)
                return m;
        }

        return PortMedium.Any;
    }

    private static bool IsFluid(PortMedium m)
    {
        return m == PortMedium.Water || m == PortMedium.Steam || m == PortMedium.Soot;
    }

    private void Awake()
    {
        EnsurePorts();
    }

    private void OnEnable()
    {
        lastMedium = PortMedium.Any;
    }

    private void OnDisable()
    {
        SetGridRegistered(false);
    }

    private void Update()
    {
        PortMedium m = CurrentMedium;

        if (m == lastMedium)
            return;

        // Stoffwechsel: Der alte Inhalt passt nicht mehr.
        buffer = 0f;
        lastMedium = m;

        // Nur bei Strom gehoert das Bauteil zum Stromnetz.
        SetGridRegistered(m == PortMedium.Power);
    }

    private void SetGridRegistered(bool on)
    {
        if (on == registeredInGrid)
            return;

        registeredInGrid = on;

        if (on)
            PowerGrid.Register(this);
        else
            PowerGrid.Unregister(this);
    }

    // Verteilt im naechsten Bild gleichmaessig auf alle Ports, die etwas aufnehmen koennen.
    private void LateUpdate()
    {
        float dt = Time.deltaTime;

        if (dt <= 0f)
            return;

        EnsurePorts();

        if (!IsFluid(CurrentMedium))
        {
            for (int i = 0; i < allowance.Length; i++)
                allowance[i] = 0f;

            return;
        }

        // Ports, in die etwas abgegeben werden kann: angesteckt, und die Gegenseite
        // ist nicht ein reiner Ausgang (der nimmt nichts an).
        int count = 0;

        for (int i = 0; i < ports.Length; i++)
        {
            if (CanGiveTo(ports[i]))
                count++;
        }

        float releasable = Mathf.Min(buffer, maxThroughputPerSecond * dt);

        for (int i = 0; i < ports.Length; i++)
        {
            allowance[i] = (count > 0 && CanGiveTo(ports[i]))
                ? releasable / count
                : 0f;
        }
    }

    private static bool CanGiveTo(Port p)
    {
        return p != null
            && p.Connected != null
            && p.Connected.Direction != PortDirection.Output;
    }

    private int IndexOf(Port port)
    {
        EnsurePorts();

        for (int i = 0; i < ports.Length; i++)
        {
            if (ports[i] == port)
                return i;
        }

        return -1;
    }

    // ---------- IPortOwner ----------

    public float GetAvailable(Port port)
    {
        if (!IsFluid(CurrentMedium))
            return 0f;

        int index = IndexOf(port);
        return index >= 0 ? allowance[index] : 0f;
    }

    public float GetFreeSpace(Port port)
    {
        if (!IsFluid(CurrentMedium))
            return 0f;

        return Mathf.Max(0f, bufferCapacity - buffer);
    }

    public float TakeFrom(Port port, float amount)
    {
        int index = IndexOf(port);

        if (index < 0 || !IsFluid(CurrentMedium))
            return 0f;

        float taken = Mathf.Clamp(amount, 0f, Mathf.Min(allowance[index], buffer));

        allowance[index] -= taken;
        buffer -= taken;
        return taken;
    }

    public float GiveTo(Port port, float amount)
    {
        if (!IsFluid(CurrentMedium))
            return 0f;

        float accepted = Mathf.Clamp(
            amount, 0f, Mathf.Max(0f, bufferCapacity - buffer));

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
            case PortMedium.Power: return Loc.T("medium.power");
            default: return Loc.T("medium.any");
        }
    }

    private void OnGUI()
    {
        if (!showDebugInfo)
            return;

        Camera cam = Camera.main;

        if (cam == null)
            return;

        Vector3 world = transform.position + Vector3.up * debugHeight;

        if ((world - cam.transform.position).sqrMagnitude
            > debugDistance * debugDistance)
            return;

        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return;

        PortMedium m = CurrentMedium;
        string text;

        if (m == PortMedium.Any)
            text = Loc.T("junction.info.empty", Loc.T("junction.label"));
        else if (IsFluid(m))
            text = Loc.T("junction.info.fluid", Loc.T("junction.label"), MediumName(m),
                buffer.ToString("F1"), bufferCapacity.ToString("F0"));
        else
            text = Loc.T("junction.info.power", Loc.T("junction.label"), MediumName(m));

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
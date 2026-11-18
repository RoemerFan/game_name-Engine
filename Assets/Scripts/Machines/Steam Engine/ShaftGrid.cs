using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  Mechanische Wellen: Drehzahl und Drehmoment.
//
//  Eine Welle verbindet Geraete mit Wellen-Ports (Port-Stoff "Welle"). Alle Geraete, die ueber Wellen
//  zusammenhaengen, drehen gemeinsam mit EINER Drehzahl (eine "Insel"). Ein Geraet kann antreiben
//  (Dampfmaschine), bremsen (Generator, Foerderband, Pumpe) oder beides. Die Insel rechnet nach dem Drehsatz:
//
//     Winkelbeschleunigung = (Summe aller Drehmomente) / (Summe aller Traegheiten)
//
//  Das ergibt von selbst: Mehr Last = Drehzahl faellt. Mehr Traegheit (Schwungrad) = traege Drehzahl.
//  Die Dampfmaschine hat einen Fliehkraftregler, der die Drehzahl haelt.
//
//  Kupplungen und Getriebe (IShaftLink) verbinden zwei Seiten. Bei einem Getriebe dreht die eine Seite
//  schneller oder langsamer: Das Netz rechnet alles auf eine Bezugsdrehzahl um (Drehmoment und Traegheit
//  werden mit der Uebersetzung umgerechnet).
//
//  Jedes Geraet kuemmert sich um sein eigenes Moment (siehe IShaftDevice). Dieses Skript wird automatisch angelegt.
//  Die Rechnung laeuft bei jedem Spieler lokal (wie das Stromnetz).
// =====================================================================

// Ein Geraet an der Welle.
public interface IShaftDevice
{
    // Drehmoment in Nm bei dieser Drehzahl (U/min). Positiv = treibt an, negativ = bremst.
    float ShaftTorque(float rpm);

    // Traegheit in kg*m^2 (Schwungmasse des Geraets).
    float ShaftInertia { get; }

    // Aktuelle Drehzahl (die Insel setzt sie jedes Bild).
    float ShaftRpm { get; set; }

    // Nach jeder Rechnung: neue Drehzahl und Zeitschritt (fuer Optik und Rueckmeldung an die Maschine).
    void ShaftTick(float rpm, float dt);
}

// Optional: Ein Geraet, das nur bestimmte Wellen-Ports hat (statt aller Wellen-Ports unter sich).
public interface IShaftPorts
{
    Port[] ShaftPorts { get; }
}

// Eine Verbindung zwischen zwei Seiten (Kupplung, Getriebe). Beide Seiten sind eigene Geraete (siehe ShaftPart).
public interface IShaftLink
{
    IShaftDevice SideA { get; }
    IShaftDevice SideB { get; }

    // Nur wenn eingekuppelt, sind beide Seiten verbunden. Aenderungen muessen ShaftGrid.Invalidate() melden.
    bool Engaged { get; }

    // Uebersetzung: Drehzahl von Seite B geteilt durch Drehzahl von Seite A (2 = Seite B dreht doppelt so schnell).
    float Ratio { get; }

    // Wirkungsgrad des Getriebes (1 = verlustfrei).
    float Efficiency { get; }

    // Das Netz meldet jedes Bild das Drehmoment (Nm, Betrag, gemessen an Seite B), das gerade durch die Verbindung geht.
    // Damit lassen sich Ueberlast und Durchrutschen erkennen. Bei offener Verbindung kommt 0.
    void ReportTorque(float nm);
}

// Fertiger Baustein fuer eine Seite einer Kupplung oder eines Getriebes (Rotor mit Port, Traegheit und Drehzahl).
public sealed class ShaftPart : IShaftDevice, IShaftPorts
{
    public Port[] Ports = new Port[0];
    public float Inertia = 1f;
    public float Rpm;

    // Optional: eigenes Moment (z. B. Reibung) und Rueckmeldung nach jeder Rechnung.
    public System.Func<float, float> Torque;
    public System.Action<float, float> Tick;

    public Port[] ShaftPorts { get { return Ports; } }
    public float ShaftInertia { get { return Inertia; } }

    public float ShaftRpm
    {
        get { return Rpm; }
        set { Rpm = Mathf.Max(0f, value); }
    }

    public float ShaftTorque(float rpm)
    {
        return Torque != null ? Torque(rpm) : 0f;
    }

    public void ShaftTick(float rpm, float dt)
    {
        Rpm = rpm;

        if (Tick != null)
            Tick(rpm, dt);
    }
}

// Eine Maschine, die ueber die Welle laufen kann (Foerderband, Pumpe) statt mit Strom.
public interface IShaftDriven
{
    // Ist die Maschine eingeschaltet?
    bool ShaftOn { get; }

    // Wie stark ist sie gerade belastet (0 bis 1)?
    float ShaftLoad01 { get; }

    // active = die Welle treibt die Maschine (dann zaehlt der Strom nicht). fraction = Tempo (0 bis 1).
    void SetShaftSupply(float fraction, bool active);
}

public class ShaftGrid : MonoBehaviour
{
    [Tooltip("Lagerreibung: Anteil der Drehzahl, der pro Sekunde verloren geht, wenn nichts antreibt.")]
    [SerializeField] private float bearingDrag = 0.04f;

    [Tooltip("Groesster Rechenschritt in Sekunden (kleiner = genauer).")]
    [SerializeField] private float maxStep = 0.02f;

    private class Device
    {
        public IShaftDevice device;
        public Component component;
        public Port[] ports;
        public int island = -1;
        public float ratio = 1f;   // Drehzahl dieses Geraets = ratio mal Bezugsdrehzahl der Insel
        public float eff = 1f;     // Wirkungsgrad auf dem Weg zur Bezugswelle
        public float reflected;    // Moment dieses Geraets, auf die Bezugswelle umgerechnet (letzter Rechenschritt)
        public float alpha;        // Winkelbeschleunigung der Bezugswelle (letzter Schritt)
        public float omega;        // Winkelgeschwindigkeit der Bezugswelle
    }

    private class Edge
    {
        public Device to;
        public float ratio;   // Drehzahl(to) = ratio * Drehzahl(from)
        public float eff;
        public IShaftLink link;   // gesetzt bei Kupplungen/Getrieben
    }

    private class LinkEntry
    {
        public IShaftLink link;
        public Component owner;
        public Device a;
        public Device b;
        public bool active;                       // eingekuppelt und gueltig (Baum)
        public readonly List<Device> sideB = new List<Device>();
    }

    private static ShaftGrid instance;
    private static bool quitting;

    private readonly List<Device> devices = new List<Device>();
    private readonly List<LinkEntry> links = new List<LinkEntry>();
    private readonly List<List<Device>> islands = new List<List<Device>>();
    private readonly List<bool> jammed = new List<bool>();
    private readonly Dictionary<Port, Device> portDevices = new Dictionary<Port, Device>();
    private bool dirty = true;
    private int lastVersion = -1;

    private static ShaftGrid Instance
    {
        get
        {
            if (instance == null && !quitting)
            {
                GameObject go = new GameObject("ShaftGrid");
                instance = go.AddComponent<ShaftGrid>();
            }

            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void OnApplicationQuit()
    {
        quitting = true;
    }

    // ---------- Anmelden ----------

    // Geraet anmelden, das selbst eine Komponente ist.
    public static void Register(IShaftDevice device)
    {
        Register(device, device as Component);
    }

    // Geraet anmelden, das zu einer anderen Komponente gehoert (z. B. eine Seite einer Kupplung).
    public static void Register(IShaftDevice device, Component owner)
    {
        if (owner == null || Instance == null)
            return;

        Instance.Add(device, owner);
    }

    public static void Unregister(IShaftDevice device)
    {
        if (instance != null)
            instance.Remove(device);
    }

    public static void RegisterLink(IShaftLink link, Component owner)
    {
        if (owner == null || Instance == null)
            return;

        Instance.AddLink(link, owner);
    }

    public static void UnregisterLink(IShaftLink link)
    {
        if (instance == null)
            return;

        instance.links.RemoveAll(l => l.link == link);
        instance.dirty = true;
    }

    // Meldet, dass sich eine Kupplung geaendert hat (Inseln neu bilden).
    public static void Invalidate()
    {
        if (instance != null)
            instance.dirty = true;
    }

    private void Add(IShaftDevice device, Component owner)
    {
        foreach (Device d in devices)
        {
            if (d.device == device)
                return;
        }

        Device entry = new Device();
        entry.device = device;
        entry.component = owner;
        entry.ports = PortsOf(entry);
        devices.Add(entry);
        dirty = true;
    }

    private void Remove(IShaftDevice device)
    {
        devices.RemoveAll(d => d.device == device);
        dirty = true;
    }

    private void AddLink(IShaftLink link, Component owner)
    {
        foreach (LinkEntry l in links)
        {
            if (l.link == link)
                return;
        }

        LinkEntry entry = new LinkEntry();
        entry.link = link;
        entry.owner = owner;
        links.Add(entry);
        dirty = true;
    }

    // Wellen-Ports eines Geraets: vom Geraet selbst angegeben, sonst alle Wellen-Ports unter seiner Komponente.
    private static Port[] PortsOf(Device d)
    {
        IShaftPorts own = d.device as IShaftPorts;

        if (own != null)
            return own.ShaftPorts;

        List<Port> list = new List<Port>();

        foreach (Port p in d.component.GetComponentsInChildren<Port>(true))
        {
            if (p.RawMedium == PortMedium.Shaft)
                list.Add(p);
        }

        return list.ToArray();
    }

    // Drehzahl an einem Port (fuer die Anzeige).
    public static float RpmAt(Port port)
    {
        if (port == null)
            return 0f;

        Device d;

        if (instance != null && instance.portDevices.TryGetValue(port, out d))
            return d.device.ShaftRpm;

        IShaftDevice fallback = port.GetComponentInParent<IShaftDevice>();
        return fallback != null ? fallback.ShaftRpm : 0f;
    }

    // ---------- Inseln bilden ----------

    private Device Find(IShaftDevice device)
    {
        foreach (Device d in devices)
        {
            if (d.device == device)
                return d;
        }

        return null;
    }

    private void Rebuild()
    {
        dirty = false;
        lastVersion = Port.ConnectionVersion;

        devices.RemoveAll(d => d.component == null);
        links.RemoveAll(l => l.owner == null);

        portDevices.Clear();
        Dictionary<Device, List<Edge>> edges = new Dictionary<Device, List<Edge>>();

        foreach (Device d in devices)
        {
            d.island = -1;
            d.ratio = 1f;
            d.eff = 1f;
            d.ports = PortsOf(d);
            edges[d] = new List<Edge>();

            foreach (Port p in d.ports)
                portDevices[p] = d;
        }

        // Verbindungen ueber Leitungen: gleiche Drehzahl.
        foreach (Device d in devices)
        {
            foreach (Port p in d.ports)
            {
                Device other;

                if (p.Connected != null && portDevices.TryGetValue(p.Connected, out other) && other != d)
                    edges[d].Add(new Edge { to = other, ratio = 1f, eff = 1f });
            }
        }

        // Kupplungen und Getriebe: nur wenn eingekuppelt.
        foreach (LinkEntry l in links)
        {
            l.active = false;
            l.sideB.Clear();
            l.a = Find(l.link.SideA);
            l.b = Find(l.link.SideB);

            if (!l.link.Engaged || l.a == null || l.b == null)
                continue;

            float ratio = Mathf.Max(0.01f, l.link.Ratio);
            float eff = Mathf.Clamp(l.link.Efficiency, 0.1f, 1f);

            edges[l.a].Add(new Edge { to = l.b, ratio = ratio, eff = eff, link = l.link });
            edges[l.b].Add(new Edge { to = l.a, ratio = 1f / ratio, eff = eff, link = l.link });
            l.active = true;
        }

        islands.Clear();
        jammed.Clear();

        foreach (Device start in devices)
        {
            if (start.island >= 0)
                continue;

            List<Device> island = new List<Device>();
            Stack<Device> open = new Stack<Device>();
            bool jam = false;

            start.island = islands.Count;
            open.Push(start);

            while (open.Count > 0)
            {
                Device current = open.Pop();
                island.Add(current);

                foreach (Edge e in edges[current])
                {
                    if (e.to.island >= 0)
                    {
                        // Schon besucht: Bei einem Ring muessen die Uebersetzungen zusammenpassen, sonst klemmt die Welle.
                        float expected = current.ratio * e.ratio;

                        if (e.to.island == start.island && Mathf.Abs(expected / e.to.ratio - 1f) > 0.01f)
                            jam = true;

                        continue;
                    }

                    e.to.island = start.island;
                    e.to.ratio = current.ratio * e.ratio;
                    e.to.eff = current.eff * e.eff;
                    open.Push(e.to);
                }
            }

            if (jam)
                Debug.LogWarning("ShaftGrid: Zwei Wege mit verschiedener Uebersetzung verbinden dieselben Wellen. Die Welle klemmt (steht).");

            islands.Add(island);
            jammed.Add(jam);
        }

        // Fuer jede eingekuppelte Verbindung: alles, was hinter Seite B haengt (fuer die Momentmessung).
        foreach (LinkEntry l in links)
        {
            if (!l.active)
                continue;

            HashSet<Device> seen = new HashSet<Device>();
            Stack<Device> open = new Stack<Device>();
            seen.Add(l.b);
            open.Push(l.b);

            while (open.Count > 0)
            {
                Device current = open.Pop();

                foreach (Edge e in edges[current])
                {
                    if (e.link == l.link || !seen.Add(e.to))
                        continue;

                    open.Push(e.to);
                }
            }

            // Erreicht man Seite A auch ohne die Verbindung, ist es ein Ring: Moment laesst sich nicht trennen.
            if (seen.Contains(l.a))
            {
                l.active = false;
                continue;
            }

            l.sideB.AddRange(seen);
        }
    }

    // ---------- Rechnen ----------

    private void Update()
    {
        if (dirty || Port.ConnectionVersion != lastVersion)
            Rebuild();

        float dt = Mathf.Min(Time.deltaTime, 0.1f);

        if (dt <= 0f)
            return;

        for (int i = 0; i < islands.Count; i++)
            Simulate(islands[i], dt, jammed[i]);

        ReportLinkTorques();
    }

    // Misst fuer jede Kupplung/jedes Getriebe das Drehmoment, das hindurchgeht, und meldet es.
    // Seite B und alles dahinter ist ein eigenes System: Traegheitsmoment mal Beschleunigung
    // = aeussere Momente + das Moment, das die Verbindung hineingibt.
    private void ReportLinkTorques()
    {
        foreach (LinkEntry l in links)
        {
            if (!l.active)
            {
                l.link.ReportTorque(0f);
                continue;
            }

            float jb = 0f;
            float external = 0f;

            foreach (Device d in l.sideB)
            {
                jb += Mathf.Max(0.05f, d.device.ShaftInertia) * d.ratio * d.ratio;
                external += d.reflected;
            }

            float drag = -bearingDrag * jb * l.b.omega;
            float torqueIn = jb * l.b.alpha - (external + drag);

            l.link.ReportTorque(Mathf.Abs(torqueIn) / Mathf.Max(0.01f, l.b.ratio));
        }
    }

    private void Simulate(List<Device> island, float dt, bool jam)
    {
        // Alles wird auf die Bezugswelle der Insel umgerechnet:
        //  Traegheit = Summe J * ratio^2     Drehimpuls = Summe J * ratio * omega     Moment = Summe ratio * Moment
        float inertia = 0f;
        float momentum = 0f;

        foreach (Device d in island)
        {
            float j = Mathf.Max(0.05f, d.device.ShaftInertia);
            inertia += j * d.ratio * d.ratio;
            momentum += j * d.ratio * (d.device.ShaftRpm * Mathf.PI / 30f);
        }

        float omega = inertia > 0f ? momentum / inertia : 0f;

        // Verklemmt (widerspruechliche Uebersetzung): nichts dreht.
        if (jam)
            omega = 0f;

        float lastTorque = 0f;

        int steps = Mathf.Clamp(Mathf.CeilToInt(dt / Mathf.Max(0.002f, maxStep)), 1, 8);
        float h = dt / steps;

        for (int s = 0; s < steps; s++)
        {
            float torque = 0f;

            foreach (Device d in island)
            {
                float rpm = omega * d.ratio * 30f / Mathf.PI;
                float t = d.device.ShaftTorque(rpm);

                // Verluste im Getriebe: Wer Leistung abgibt, liefert weniger, wer Leistung braucht, kostet mehr.
                t *= t < 0f ? 1f / d.eff : d.eff;
                d.reflected = d.ratio * t;
                torque += d.reflected;
            }

            // Lagerreibung bremst immer ein wenig.
            torque -= bearingDrag * inertia * omega;

            lastTorque = torque;
            omega += torque / inertia * h;
            omega = Mathf.Max(0f, omega);

            if (jam)
                omega = 0f;

            if (float.IsNaN(omega) || float.IsInfinity(omega))
                omega = 0f;
        }

        foreach (Device d in island)
        {
            d.alpha = jam ? 0f : lastTorque / inertia;
            d.omega = omega;

            float rpm = omega * d.ratio * 30f / Mathf.PI;
            d.device.ShaftRpm = rpm;
            d.device.ShaftTick(rpm, dt);
        }
    }
}
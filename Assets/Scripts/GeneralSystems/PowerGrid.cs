using System.Collections.Generic;
using UnityEngine;

// Erzeuger (z. B. Dampfgenerator).
public interface IPowerProducer
{
    // Leistung, die gerade abgegeben werden koennte.
    float AvailablePowerKW { get; }

    // Netzfrequenz, die der Laeufer gerade vorgibt.
    float FrequencyHz { get; }

    // Das Netz fordert diese Leistung an (kann ueber der Kapazitaet liegen).
    void SetRequestedPower(float kw);
}

// Verbraucher (z. B. Pumpe).
public interface IPowerConsumer
{
    // Aktueller Bedarf.
    float DemandKW { get; }

    // Maximaler Bedarf (fuer die Wiederzuschaltung nach Lastabwurf).
    float RatedPowerKW { get; }

    // Hoeher = wichtiger. Unwichtige Verbraucher werden zuerst abgeworfen.
    int Importance { get; }

    // 0 = kein Strom, 1 = voll versorgt.
    void SetSupply(float fraction);
}

// Stromspeicher (Batterie). Gibt Leistung ab (positiv) oder laedt (negativ).
public interface IPowerStorage
{
    // Bereit (Schalter an, nicht ueberhitzt)?
    bool IsOnline { get; }

    // Darf das Netz allein aufbauen (Inselbetrieb / Schwarzstart)?
    bool IsGridForming { get; }

    // Was gerade maximal abgegeben bzw. aufgenommen werden kann.
    float MaxDischargeKW { get; }
    float MaxChargeKW { get; }

    // Fuer die Anzeige.
    float StoredKWh { get; }
    float CapacityKWh { get; }
    float PowerKW { get; }

    // Vorgabe des Netzes: positiv = abgeben, negativ = laden.
    void SetPower(float kw);
}

// Trennstelle im Netz (Sicherung, spaeter z. B. Schalter oder Trenner).
// Hat zwei Power-Ports. Ist sie nicht leitend, sind beide Seiten getrennt.
public interface IPowerGate
{
    bool IsConducting { get; }

    // Das Netz meldet die Leistung, die gerade hindurchfliesst.
    void ReportFlow(float kw);
}

// Gemeinsames Waermemodell fuer Sicherungen und Kabel.
public static class Overload
{
    // heat: 0 = kalt, 1 = Ausloesung bzw. Durchbrennen.
    // Ueber der Nennlast waermt sich das Teil auf, mit dem Quadrat der Last:
    // bei doppelter Last nach "tripSecondsAtDouble" Sekunden.
    // Unter der Nennlast kuehlt es ab. Nennlast <= 0 bedeutet unbegrenzt.
    public static float Step(
        float heat,
        float flowKW,
        float ratingKW,
        float tripSecondsAtDouble,
        float coolSeconds,
        float dt)
    {
        if (ratingKW <= 0f)
            return 0f;

        float ratio = flowKW / ratingKW;

        if (ratio > 1f)
        {
            heat += (ratio * ratio - 1f)
                / (3f * Mathf.Max(0.01f, tripSecondsAtDouble))
                * dt;
        }
        else
        {
            heat -= dt / Mathf.Max(0.01f, coolSeconds);
        }

        return Mathf.Clamp01(heat);
    }
}

// Wird bei Bedarf automatisch in der Szene angelegt.
//
// Prinzip: Geraete, die ueber Strom-Kabel zusammenhaengen, bilden ein Netz.
// Pro Netz wird die Leistung bilanziert (Erzeugung gegen Last). Es werden
// keine Strom-Einheiten transportiert. Die Frequenz kommt von den
// Generatoren. Ueberlast laesst sie sinken, dagegen gibt es zwei Schutzstufen:
//   1. Unter "shedFrequency": unwichtige Verbraucher werden abgeworfen.
//   2. Unter "blackoutFrequency": das ganze Netz faellt aus und startet
//      nach einer Pause schrittweise neu.
//
// Zusaetzlich wird fuer jedes Kabel und jede Sicherung berechnet, wie viel
// Leistung hindurchfliesst. Dabei wird das Netz als Baum behandelt: Ringe
// aus Kabeln werden aufgeschnitten, die ueberzaehligen Kabel fuehren dann 0.
public class PowerGrid : MonoBehaviour
{
    [Header("Schutz")]
    [SerializeField] private float shedFrequencyHz = 48.5f;
    [SerializeField] private float blackoutFrequencyHz = 47f;
    [SerializeField] private float shedInterval = 1f;
    [SerializeField] private float shedLockoutSeconds = 20f;
    [Range(0.5f, 1f)]
    [SerializeField] private float reconnectMargin = 0.9f;

    [Header("Blackout")]
    [SerializeField] private float blackoutSeconds = 10f;
    [SerializeField] private float restartStaggerSeconds = 3f;

    [Header("Speicher")]
    [Tooltip("Zeitraffer fuer Batterie-Energie: 60 = eine Sekunde im Spiel entspricht einer Minute Energie.")]
    [SerializeField] private float energyTimeScale = 60f;

    [Tooltip("Welcher Teil des Erzeuger-Ueberschusses darf zum Laden genutzt werden.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float chargeSurplusFraction = 0.9f;

    [Header("Anzeige (Netzanzeige)")]
    [Tooltip("Normale Netzfrequenz fuer die Anzeige.")]
    [SerializeField] private float nominalHz = 50f;

    [Tooltip("Alle so viele Sekunden wird ein Messpunkt fuer die Kurven gespeichert.")]
    [SerializeField] private float sampleInterval = 0.5f;

    private class Device
    {
        public Component component;
        public IPowerProducer producer;
        public IPowerConsumer consumer;
        public IPowerGate gate;
        public IPowerStorage storage;
        public Port[] ports;       // Ports, die diesem Geraet wirklich gehoeren
        public Port[] allPorts;    // alle Ports im Objektbaum (auch die von verschachtelten Geraeten)
        public float offlineUntil;
        public bool shed;
        public float shedUntil;

        // Baumstruktur fuer die Lastberechnung.
        public Device parent;
        public Pipe parentCable;
        public float subtree;
    }

    private class Island
    {
        public readonly List<Device> devices = new List<Device>();
        public float lastShedTime = -999f;
        public float capacity;
        public float demand;
        public float frequency;
        public float served = 1f;
        public int producerCount;
        public int consumerCount;
        public bool anyOffline;
        public bool anyShed;
        public string status = "";

        // Speicher
        public int storageCount;
        public float supply;          // Erzeuger + Speicherhilfe (Basis fuer "served")
        public float storageAssist;   // Speicher gibt zusaetzlich ab (Unterversorgung)
        public float storageCharge;   // Speicher laedt aus Ueberschuss
        public bool batteryOnly;      // Netz wird nur von Batterien getragen
        public float storedKWh;
        public float storageCapacityKWh;
        public float storagePowerKW;
        public float dischargeTotal;
        public float chargeTotal;
        public float formingTotal;
    }

    // ---------- Daten fuer die Netzanzeige (PowerGridMonitor) ----------

    // Verlauf der letzten Messpunkte (aelteste zuerst abrufbar).
    public class GridInfo
    {
        public const int Size = 120;

        public int id;
        public string statusKey = "";
        public int producers;
        public int consumers;
        public float capacity;
        public float demand;
        public float frequency;
        public float lastSeen;

        // Speicher (Batterien)
        public int storageCount;
        public float storedKWh;
        public float storageCapacityKWh;
        public float storagePowerKW;   // + abgeben, - laden

        public readonly float[] capacityHistory = new float[Size];
        public readonly float[] demandHistory = new float[Size];
        public readonly float[] frequencyHistory = new float[Size];

        // Anzahl gueltiger Messpunkte und Position des naechsten.
        public int count;
        private int head;

        public void Push(float cap, float dem, float freq)
        {
            capacityHistory[head] = cap;
            demandHistory[head] = dem;
            frequencyHistory[head] = freq;

            head = (head + 1) % Size;
            count = Mathf.Min(count + 1, Size);
        }

        // i = 0 ist der aelteste Messpunkt, count - 1 der neueste.
        public int Index(int i)
        {
            return (head - count + i + Size * 2) % Size;
        }
    }

    // Ein Ereignis (Lastabwurf, Blackout, Sicherung ...).
    public struct GridEvent
    {
        public float time;
        public string text;
        public int severity;   // 0 = Info, 1 = Warnung, 2 = Stoerung
    }

    private readonly Dictionary<int, GridInfo> infoById = new Dictionary<int, GridInfo>();
    private readonly List<GridInfo> infos = new List<GridInfo>();
    private readonly List<GridEvent> events = new List<GridEvent>();
    private float sampleTimer;

    // Netze (nur solche mit Erzeugern oder Verbrauchern) samt Verlauf.
    public static IList<GridInfo> Infos
    {
        get { return instance != null ? instance.infos : EmptyInfos; }
    }

    // Letzte Ereignisse, neueste zuletzt.
    public static IList<GridEvent> Events
    {
        get { return instance != null ? instance.events : EmptyEvents; }
    }

    private static readonly List<GridInfo> EmptyInfos = new List<GridInfo>();
    private static readonly List<GridEvent> EmptyEvents = new List<GridEvent>();

    // Zeitraffer fuer die Energie der Batterien.
    public static float EnergyTimeScale
    {
        get { return instance != null ? instance.energyTimeScale : 60f; }
    }

    public static float NominalHz
    {
        get { return instance != null ? instance.nominalHz : 50f; }
    }

    public static float ShedHz
    {
        get { return instance != null ? instance.shedFrequencyHz : 48.5f; }
    }

    public static float BlackoutHz
    {
        get { return instance != null ? instance.blackoutFrequencyHz : 47f; }
    }

    // Traegt ein Ereignis in die Anzeige ein (z. B. von Sicherungen).
    public static void LogEvent(string text, int severity = 1)
    {
        if (instance == null)
            return;

        GridEvent e = new GridEvent();
        e.time = Time.time;
        e.text = text;
        e.severity = severity;

        instance.events.Add(e);

        // Nur die letzten 20 behalten.
        if (instance.events.Count > 20)
            instance.events.RemoveAt(0);
    }

    // Feste Kennung eines Netzes: das Geraet mit der kleinsten ID. So bleibt die
    // Kurve erhalten, auch wenn sich das Netz neu aufbaut.
    private static int IslandId(Island g)
    {
        int id = int.MaxValue;

        foreach (Device d in g.devices)
        {
            if (d.component != null)
                id = Mathf.Min(id, d.component.GetInstanceID());
        }

        return id;
    }

    // Aktualisiert die Liste fuer die Anzeige und speichert regelmaessig Messpunkte.
    private void UpdateInfos(float dt)
    {
        sampleTimer += dt;

        bool sample = sampleTimer >= sampleInterval;

        if (sample)
            sampleTimer = 0f;

        infos.Clear();

        foreach (Island g in islands)
        {
            if (g.producerCount + g.consumerCount + g.storageCount == 0)
                continue;

            int id = IslandId(g);

            GridInfo info;

            if (!infoById.TryGetValue(id, out info))
            {
                info = new GridInfo();
                info.id = id;
                infoById[id] = info;
            }

            info.statusKey = g.status;
            info.producers = g.producerCount;
            info.consumers = g.consumerCount;
            info.capacity = g.capacity;
            info.demand = g.demand;
            info.frequency = g.frequency;
            info.storageCount = g.storageCount;
            info.storedKWh = g.storedKWh;
            info.storageCapacityKWh = g.storageCapacityKWh;
            info.storagePowerKW = g.storagePowerKW;
            info.lastSeen = Time.time;

            if (sample)
                info.Push(g.capacity, g.demand, g.frequency);

            infos.Add(info);
        }

        // Nicht mehr vorhandene Netze nach einer Minute vergessen.
        if (sample)
        {
            List<int> old = null;

            foreach (KeyValuePair<int, GridInfo> pair in infoById)
            {
                if (Time.time - pair.Value.lastSeen > 60f)
                {
                    if (old == null)
                        old = new List<int>();

                    old.Add(pair.Key);
                }
            }

            if (old != null)
            {
                foreach (int id in old)
                    infoById.Remove(id);
            }
        }
    }

    private static PowerGrid instance;

    private readonly List<Device> devices = new List<Device>();
    private readonly Dictionary<Component, Device> lookup =
        new Dictionary<Component, Device>();
    private readonly Dictionary<Port, Device> portOwners =
        new Dictionary<Port, Device>();
    private readonly List<Island> islands = new List<Island>();

    // Feste Verbindungen ohne Kabel-Anschluss (z. B. Lampen, die an einer Maschine haengen).
    // Mehrere Lampen koennen so am selben Anschluss einer Maschine haengen.
    private readonly List<Port[]> links = new List<Port[]>();

    private bool topologyDirty = true;
    private int lastPortVersion = -1;

    private static PowerGrid Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<PowerGrid>();

                if (instance == null)
                {
                    GameObject go = new GameObject("PowerGrid");
                    instance = go.AddComponent<PowerGrid>();
                }
            }

            return instance;
        }
    }

    // Geraete rufen das in OnEnable / OnDisable auf.
    public static void Register(Component component)
    {
        if (component != null)
            Instance.AddDevice(component);
    }

    public static void Unregister(Component component)
    {
        if (instance != null && component != null)
            instance.RemoveDevice(component);
    }

    // Verbindet zwei Strom-Ports elektrisch, ohne dass ein Port "belegt" wird.
    // Das Geraet von Port A haengt danach im selben Netz wie das Geraet von Port B.
    public static void Link(Port a, Port b)
    {
        if (a == null || b == null || a == b)
            return;

        PowerGrid grid = Instance;

        foreach (Port[] pair in grid.links)
        {
            if ((pair[0] == a && pair[1] == b) || (pair[0] == b && pair[1] == a))
                return;
        }

        grid.links.Add(new Port[] { a, b });
        grid.topologyDirty = true;
    }

    // Gehoert dieser Port einem Stromgeraet im Netz? (Nur nach dem ersten Netzaufbau gueltig.)
    public static bool HasOwner(Port port)
    {
        return instance != null && port != null && instance.portOwners.ContainsKey(port);
    }

    public static void Unlink(Port a, Port b)
    {
        if (instance == null)
            return;

        instance.links.RemoveAll(pair =>
            (pair[0] == a && pair[1] == b) || (pair[0] == b && pair[1] == a));

        instance.topologyDirty = true;
    }

    // Wird z. B. von einer Sicherung aufgerufen, wenn sie schaltet.
    public static void TopologyChanged()
    {
        if (instance != null)
            instance.topologyDirty = true;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        // Die Netzanzeige braucht keine Einrichtung: Sie wird hier mit angelegt.
        if (GetComponent<PowerGridMonitor>() == null)
            gameObject.AddComponent<PowerGridMonitor>();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void AddDevice(Component component)
    {
        if (lookup.ContainsKey(component))
            return;

        List<Port> powerPorts = new List<Port>();

        foreach (Port port in component.GetComponentsInChildren<Port>(true))
        {
            if (port.Medium == PortMedium.Power)
                powerPorts.Add(port);
        }

        Device device = new Device
        {
            component = component,
            producer = component as IPowerProducer,
            consumer = component as IPowerConsumer,
            gate = component as IPowerGate,
            storage = component as IPowerStorage,
            ports = powerPorts.ToArray(),
            allPorts = powerPorts.ToArray()
        };

        devices.Add(device);
        lookup[component] = device;
        topologyDirty = true;
    }

    private void RemoveDevice(Component component)
    {
        Device device;

        if (!lookup.TryGetValue(component, out device))
            return;

        devices.Remove(device);
        lookup.Remove(component);
        topologyDirty = true;
    }

    private void Update()
    {
        if (topologyDirty || Port.ConnectionVersion != lastPortVersion)
            RebuildIslands();

        float now = Time.time;

        foreach (Island island in islands)
            Simulate(island, now);

        UpdateInfos(Time.deltaTime);
    }

    // Bestimmt, welche Geraete ueber Kabel zusammenhaengen. Geoeffnete oder
    // ausgeloeste Sicherungen trennen das Netz.
    private void RebuildIslands()
    {
        topologyDirty = false;
        lastPortVersion = Port.ConnectionVersion;

        islands.Clear();
        portOwners.Clear();

        // Jeder Port gehoert dem am tiefsten verschachtelten Geraet. So kann z. B. eine
        // Lampe IN einer Maschine ihre eigenen Ports behalten, ohne dass die Maschine sie
        // mit beansprucht. Bei gleicher Tiefe zaehlt wie bisher das letzte Geraet.
        Dictionary<Port, int> ownerDepth = new Dictionary<Port, int>();

        foreach (Device device in devices)
        {
            device.parent = null;
            device.parentCable = null;

            int depth = 0;

            for (Transform t = device.component.transform; t != null; t = t.parent)
                depth++;

            foreach (Port port in device.allPorts)
            {
                if (port == null)
                    continue;

                int known;

                if (!ownerDepth.TryGetValue(port, out known) || depth >= known)
                {
                    ownerDepth[port] = depth;
                    portOwners[port] = device;
                }
            }
        }

        foreach (Device device in devices)
        {
            List<Port> owned = new List<Port>();

            foreach (Port port in device.allPorts)
            {
                Device owner;

                if (port != null && portOwners.TryGetValue(port, out owner) && owner == device)
                    owned.Add(port);
            }

            device.ports = owned.ToArray();
        }

        // Feste Verbindungen den Geraeten zuordnen (zerstoerte Ports werden vergessen).
        links.RemoveAll(pair => pair[0] == null || pair[1] == null);

        Dictionary<Device, List<Device>> linked = new Dictionary<Device, List<Device>>();

        foreach (Port[] pair in links)
        {
            Device da, db;

            if (!portOwners.TryGetValue(pair[0], out da) ||
                !portOwners.TryGetValue(pair[1], out db) || da == db)
                continue;

            if (!linked.ContainsKey(da)) linked[da] = new List<Device>();
            if (!linked.ContainsKey(db)) linked[db] = new List<Device>();

            linked[da].Add(db);
            linked[db].Add(da);
        }

        HashSet<Device> visited = new HashSet<Device>();
        Queue<Device> queue = new Queue<Device>();

        foreach (Device start in devices)
        {
            // Trennstellen werden nur ueber ihre Nachbarn erreicht.
            if (start.gate != null)
                continue;

            if (!visited.Add(start))
                continue;

            Island island = new Island();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Device current = queue.Dequeue();
                island.devices.Add(current);

                foreach (Port port in current.ports)
                {
                    if (port == null || port.Connected == null)
                        continue;

                    Device other;

                    if (!portOwners.TryGetValue(port.Connected, out other))
                        continue;

                    // Offene Trennstelle: hier endet das Netz.
                    if (other.gate != null && !other.gate.IsConducting)
                        continue;

                    if (!visited.Add(other))
                        continue;

                    other.parent = current;
                    other.parentCable = port.Cable;
                    queue.Enqueue(other);
                }

                // Feste Verbindungen (ohne Kabel-Anschluss).
                List<Device> extra;

                if (linked.TryGetValue(current, out extra))
                {
                    foreach (Device other in extra)
                    {
                        if (other.gate != null && !other.gate.IsConducting)
                            continue;

                        if (!visited.Add(other))
                            continue;

                        other.parent = current;
                        other.parentCable = null;
                        queue.Enqueue(other);
                    }
                }
            }

            islands.Add(island);
        }
    }

    private void Simulate(Island g, float now)
    {
        Tally(g, now);

        // Abgeworfene Verbraucher wieder zuschalten, wenn genug Reserve da ist.
        bool reconnected = false;

        foreach (Device d in g.devices)
        {
            if (d.consumer == null || !d.shed || now < d.shedUntil)
                continue;

            float needed = d.consumer.RatedPowerKW;

            if (g.demand + needed <= g.supply * reconnectMargin)
            {
                d.shed = false;
                g.demand += needed;
                reconnected = true;
            }
        }

        if (reconnected)
            Tally(g, now);

        if (g.capacity > 0f && g.frequency < blackoutFrequencyHz)
        {
            Trip(g, now);
            Tally(g, now);
        }
        else if (g.capacity > 0f &&
                 g.frequency < shedFrequencyHz &&
                 now - g.lastShedTime >= shedInterval)
        {
            if (ShedOne(g, now))
                Tally(g, now);
        }

        Dispatch(g, now);
        ComputeFlows(g, now);
    }

    // Summiert Erzeugung, Last und Frequenz.
    private void Tally(Island g, float now)
    {
        g.capacity = 0f;
        g.demand = 0f;
        g.producerCount = 0;
        g.consumerCount = 0;
        g.anyOffline = false;
        g.anyShed = false;

        g.storageCount = 0;
        g.storageAssist = 0f;
        g.storageCharge = 0f;
        g.batteryOnly = false;
        g.storedKWh = 0f;
        g.storageCapacityKWh = 0f;
        g.storagePowerKW = 0f;
        g.dischargeTotal = 0f;
        g.chargeTotal = 0f;
        g.formingTotal = 0f;

        float weighted = 0f;

        foreach (Device d in g.devices)
        {
            bool offline = now < d.offlineUntil;

            if (offline)
                g.anyOffline = true;

            if (d.producer != null)
            {
                g.producerCount++;

                if (!offline)
                {
                    float available = d.producer.AvailablePowerKW;
                    g.capacity += available;
                    weighted += available * d.producer.FrequencyHz;
                }
            }

            if (d.consumer != null)
            {
                g.consumerCount++;

                if (d.shed)
                    g.anyShed = true;
                else if (!offline)
                    g.demand += d.consumer.DemandKW;
            }

            if (d.storage != null)
            {
                g.storageCount++;
                g.storedKWh += d.storage.StoredKWh;
                g.storageCapacityKWh += d.storage.CapacityKWh;
                g.storagePowerKW += d.storage.PowerKW;

                if (!offline && d.storage.IsOnline)
                {
                    g.dischargeTotal += d.storage.MaxDischargeKW;
                    g.chargeTotal += d.storage.MaxChargeKW;

                    if (d.storage.IsGridForming)
                        g.formingTotal += d.storage.MaxDischargeKW;
                }
            }
        }

        g.frequency = g.capacity > 0f ? weighted / g.capacity : 0f;
        g.supply = g.capacity;

        if (g.capacity > 0f)
        {
            // Erzeuger laufen: Batterien helfen nur bei Unterversorgung und
            // laden nur aus dem Ueberschuss.
            float deficit = g.demand - g.capacity;

            if (deficit > 0f)
            {
                g.storageAssist = Mathf.Min(g.dischargeTotal, deficit);
                g.supply = g.capacity + g.storageAssist;
            }
            else
            {
                g.storageCharge = Mathf.Min(
                    g.chargeTotal,
                    (g.capacity - g.demand) * chargeSurplusFraction);
            }
        }
        else if (g.formingTotal > 0f)
        {
            // Schwarzstart / Inselbetrieb: Batterien tragen das Netz allein.
            // Die Frequenz sinkt bei Ueberlast, aber nur bis kurz ueber den
            // Blackout (der Wechselrichter regelt selbst).
            g.batteryOnly = true;
            g.capacity = g.formingTotal;
            g.supply = g.formingTotal;

            float overload = Mathf.Max(0f, g.demand / g.formingTotal - 1f);
            g.frequency = nominalHz - 3f * Mathf.Clamp01(overload / 0.5f);
        }
    }

    // Wirft den unwichtigsten Verbraucher ab.
    private bool ShedOne(Island g, float now)
    {
        Device victim = null;

        foreach (Device d in g.devices)
        {
            if (d.consumer == null || d.shed || now < d.offlineUntil)
                continue;

            if (d.consumer.DemandKW < 0.01f)
                continue;

            if (victim == null ||
                d.consumer.Importance < victim.consumer.Importance ||
                (d.consumer.Importance == victim.consumer.Importance &&
                 d.consumer.DemandKW > victim.consumer.DemandKW))
            {
                victim = d;
            }
        }

        if (victim == null)
            return false;

        victim.shed = true;
        victim.shedUntil = now + shedLockoutSeconds;
        g.lastShedTime = now;

        LogEvent(Loc.T("grid.event.shed",
            Loc.MachineName(victim.component.name),
            g.frequency.ToString("F2")), 1);

        Debug.LogWarning(
            "Lastabwurf: " + victim.component.name
            + " (Frequenz " + g.frequency.ToString("F2") + " Hz)",
            victim.component
        );

        return true;
    }

    // Blackout: Alles faellt aus, danach Neustart mit Verzoegerung.
    private void Trip(Island g, float now)
    {
        int order = 0;

        foreach (Device d in g.devices)
        {
            d.shed = false;

            if (d.producer != null)
            {
                d.offlineUntil = now + blackoutSeconds;
            }
            else if (d.storage != null)
            {
                // Batterien trennen sich vom gestoerten Netz - ausser im
                // Inselbetrieb, dort halten sie das Netz am Leben.
                if (!d.storage.IsGridForming)
                    d.offlineUntil = now + blackoutSeconds;
            }
            else if (d.consumer != null)
            {
                order++;

                d.offlineUntil =
                    now + blackoutSeconds + order * restartStaggerSeconds;
            }
        }

        LogEvent(Loc.T("grid.event.blackout", g.frequency.ToString("F2")), 2);

        Debug.LogWarning(
            "BLACKOUT! Netzfrequenz fiel auf "
            + g.frequency.ToString("F2") + " Hz."
        );
    }

    // Gibt Anforderungen an Erzeuger und Versorgung an Verbraucher weiter.
    private void Dispatch(Island g, float now)
    {
        g.served = g.demand > 0.0001f
            ? Mathf.Min(1f, g.supply / g.demand)
            : 1f;

        // Was die Erzeuger selbst liefern muessen (Last + Laden - Batteriehilfe).
        float producerLoad = Mathf.Max(
            0f, g.demand + g.storageCharge - g.storageAssist);

        foreach (Device d in g.devices)
        {
            bool offline = now < d.offlineUntil;

            if (d.producer != null)
            {
                if (offline || g.capacity <= 0f || g.batteryOnly)
                {
                    d.producer.SetRequestedPower(0f);
                }
                else
                {
                    // Last wird nach Leistung auf die Erzeuger verteilt.
                    // Sie darf ueber der Kapazitaet liegen, dann sinkt die
                    // Drehzahl und damit die Frequenz.
                    float share = d.producer.AvailablePowerKW / g.capacity;
                    d.producer.SetRequestedPower(producerLoad * share);
                }
            }

            if (d.storage != null)
                d.storage.SetPower(StorageCommand(g, d, offline));

            if (d.consumer != null)
            {
                d.consumer.SetSupply(
                    (offline || d.shed) ? 0f : g.served
                );
            }
        }

        if (g.batteryOnly && g.consumerCount > 0)
            g.status = "grid.status.battery";
        else if (g.producerCount == 0 && g.consumerCount > 0 && g.storageCount == 0)
            g.status = "grid.status.nogenerator";
        else if (g.anyOffline)
            g.status = "grid.status.blackout";
        else if (g.capacity <= 0f && g.consumerCount > 0)
            g.status = "grid.status.nopower";
        else if (g.anyShed)
            g.status = "grid.status.shedding";
        else if (g.served < 0.999f)
            g.status = "grid.status.underpowered";
        else
            g.status = "grid.status.ok";
    }

    // Vorgabe fuer eine Batterie: positiv = abgeben, negativ = laden.
    private float StorageCommand(Island g, Device d, bool offline)
    {
        if (offline || !d.storage.IsOnline)
            return 0f;

        if (g.batteryOnly)
        {
            // Nur Batterien im Inselbetrieb tragen das Netz.
            if (!d.storage.IsGridForming || g.formingTotal <= 0f)
                return 0f;

            float load = Mathf.Min(g.demand, g.formingTotal);
            return load * d.storage.MaxDischargeKW / g.formingTotal;
        }

        if (g.storageAssist > 0f && g.dischargeTotal > 0f)
            return g.storageAssist * d.storage.MaxDischargeKW / g.dischargeTotal;

        if (g.storageCharge > 0f && g.chargeTotal > 0f)
            return -g.storageCharge * d.storage.MaxChargeKW / g.chargeTotal;

        return 0f;
    }

    // Berechnet, wie viel Leistung durch jedes Kabel und jede Sicherung fliesst.
    // Durch eine Kante im Baum fliesst genau die Summe aller Erzeugung und Last
    // auf der anderen Seite.
    private void ComputeFlows(Island g, float now)
    {
        float generationFactor = (g.capacity > 0f && !g.batteryOnly)
            ? Mathf.Clamp01(
                (g.demand + g.storageCharge - g.storageAssist) / g.capacity)
            : 0f;

        foreach (Device d in g.devices)
        {
            bool offline = now < d.offlineUntil;
            float net = 0f;

            if (d.producer != null && !offline)
                net += d.producer.AvailablePowerKW * generationFactor;

            if (d.consumer != null && !offline && !d.shed)
                net -= d.consumer.DemandKW * g.served;

            // Batterie: positiv = speist ein, negativ = laedt.
            if (d.storage != null && !offline)
                net += d.storage.PowerKW;

            d.subtree = net;
        }

        // Von den Blaettern zur Wurzel (die Liste ist breitenweise sortiert).
        for (int i = g.devices.Count - 1; i >= 0; i--)
        {
            Device d = g.devices[i];

            if (d.parent == null)
                continue;

            d.parent.subtree += d.subtree;

            float flow = Mathf.Abs(d.subtree);

            if (d.parentCable != null)
                d.parentCable.ReportFlow(flow);

            if (d.gate != null)
                d.gate.ReportFlow(flow);
        }
    }
}
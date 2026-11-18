using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// =====================================================================
//  ItemStorage: ein "Lager" fuer lose Gegenstaende (Kohle, Erz, Barren ...).
//  Gedacht fuer Schubkarren, Tender, spaeter Wagen und Kisten.
//
//  So funktioniert es:
//   - Legt man einen Gegenstand in den Aufnahme-Bereich ("Zone"), wird er AUFGENOMMEN: Das
//     lose Stueck verschwindet und erscheint als rein optisches Stueck im Haufen.
//     Dadurch rutscht oder fliegt nichts mehr weg, wenn man die Schubkarre bewegt.
//   - Mit E auf das Lager (bzw. den Haufen) schauen = oberstes Stueck herausnehmen
//     (es landet direkt in der Hand, siehe CoalPickup).
//   - Jedes Stueck bleibt ein GANZES Stueck. So sieht man gut, wie viel noch da ist.
//   - Gespeichert wird mit dem Gegenstand bzw. Fahrzeug, zu dem das Lager gehoert.
//
//  EINRICHTEN:
//   1. Dieses Skript an die Wurzel der Schubkarre / des Tenders.
//   2. "Zone": ein Box Collider (Is Trigger) ueber dem Haufen. Bei der Schubkarre kann
//      es dieselbe Box sein wie "Cargo Zone".
//   3. "Pile Center" (leeres Kind-Objekt in der Mulde) und "Pile Size" fuer den Haufen.
//   4. Optional "Take From": nur diese Collider zaehlen beim Herausnehmen. Leer = alle
//      Collider des Objekts.
//   5. Das Prefab des Gegenstands (z. B. CoalChunk) muss in der Liste "Item Prefabs" am
//      SaveSystem stehen (dort wird es nach seinem Namen gefunden).
//  Mehrspieler: Aufnehmen und Herausnehmen laufen als Kommando (StoreAdd/StoreTake), dadurch
//  haben alle Spieler denselben Haufen. Als Mitspieler erscheint ein herausgenommenes Stueck
//  beim Host und wird ihm sofort in die Hand gegeben.
// =====================================================================
public class ItemStorage : MonoBehaviour, ISaveable
{
    [Header("Aufnahme")]
    [Tooltip("Trigger-Box, in der losgelassene Gegenstaende aufgenommen werden.")]
    public BoxCollider zone;

    [Tooltip("Welche Arten von Gegenstaenden werden aufgenommen?")]
    public ItemKind[] accepts = { ItemKind.Coal };

    [Tooltip("Hoechstzahl Stuecke.")]
    public int maxItems = 30;

    [Tooltip("Hoechstgewicht in kg (0 = unbegrenzt).")]
    public float maxMassKg = 0f;

    [Tooltip("Nur diese Collider zaehlen beim Herausnehmen mit E (leer = alle).")]
    public Collider[] takeFrom;

    [Header("Anzeige des Haufens")]
    [Tooltip("Mitte des Haufens (Boden der Mulde). Leer = Mitte dieses Objekts.")]
    public Transform pileCenter;
    [Tooltip("Grundflaeche und Hoehe, in der die Stuecke verteilt werden (Meter).")]
    public Vector3 pileSize = new Vector3(0.6f, 0.25f, 0.6f);
    [Tooltip("Wie viele Stuecke pro Schicht nebeneinander liegen.")]
    public int piecesPerLayer = 8;
    [Tooltip("Hoehe einer Schicht (Meter).")]
    public float layerHeight = 0.1f;

    // Ein gelagertes Stueck.
    private class Entry
    {
        public string prefab;
        public ItemKind kind;
        public string metal;
        public float mass;
        public GameObject visual;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private static readonly Collider[] buffer = new Collider[64];
    private static readonly HashSet<string> warned = new HashSet<string>();

    private GameObject holder;      // inaktives Objekt: hier werden Kopien vorbereitet
    private float scanTimer;

    public int Count { get { return entries.Count; } }
    public float StoredMassKg { get; private set; }
    public bool IsFull { get { return entries.Count + pendingCount >= maxItems; } }

    // Mitspieler: Stuecke, die schon abgeschickt, aber noch nicht vom Host bestaetigt sind.
    private int pendingCount;
    private float pendingMass;

    // Das zuletzt per Kommando herausgenommene Stueck (fuer den, der es herausgenommen hat).
    public static GameObject lastTaken;

    // ---------- Aufnehmen ----------

    void Update()
    {
        scanTimer -= Time.deltaTime;

        if (scanTimer > 0f)
            return;

        scanTimer = 0.15f;
        Absorb();
    }

    private bool Accepts(ItemKind kind)
    {
        if (accepts == null)
            return false;

        for (int i = 0; i < accepts.Length; i++)
        {
            if (accepts[i] == kind)
                return true;
        }

        return false;
    }

    private void Absorb()
    {
        if (zone == null || IsFull)
            return;

        Transform t = zone.transform;
        Vector3 center = t.TransformPoint(zone.center);
        Vector3 scale = t.lossyScale;
        Vector3 half = Vector3.Scale(zone.size * 0.5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

        int n = Physics.OverlapBoxNonAlloc(center, half, buffer, t.rotation,
            ~0, QueryTriggerInteraction.Ignore);

        List<CollectibleCoal> found = null;

        for (int i = 0; i < n; i++)
        {
            CollectibleCoal item = buffer[i].GetComponentInParent<CollectibleCoal>();

            if (item == null || item is FuelCanister || item is DigTool)
                continue;

            if (found == null)
                found = new List<CollectibleCoal>();

            if (!found.Contains(item))
                found.Add(item);
        }

        if (found == null)
            return;

        foreach (CollectibleCoal item in found)
        {
            if (IsFull)
                return;

            TryStore(item);
        }
    }

    private void TryStore(CollectibleCoal item)
    {
        ItemKind kind = ItemKind.Coal;
        ItemTag tag = item.GetComponentInParent<ItemTag>();

        if (tag != null)
            kind = tag.kind;

        if (!Accepts(kind))
            return;

        // Getragene Gegenstaende (kinematisch) bleiben in der Hand.
        Rigidbody rb = item.GetComponent<Rigidbody>();

        if (rb == null || rb.isKinematic)
            return;

        if (!NetItems.CanAct(item.gameObject))
            return;

        string prefabName = CleanName(item.gameObject.name);
        GameObject prefab = FindPrefab(prefabName);

        if (prefab == null)
        {
            if (warned.Add(prefabName))
                Debug.LogWarning("ItemStorage: Prefab '" + prefabName +
                                 "' steht nicht in SaveSystem > Item Prefabs. Es wird nicht aufgenommen.", this);
            return;
        }

        float mass = (tag != null && tag.massKg > 0f) ? tag.massKg : rb.mass;

        if (maxMassKg > 0f && StoredMassKg + pendingMass + mass > maxMassKg)
            return;

        string metal = tag != null ? (tag.metal ?? "") : "";

        NetItems.Consume(item.gameObject);

        int domain, id, index;

        if (NetSync.IsOnline && Describe(out domain, out id, out index))
        {
            // Online: als Kommando, damit alle Spieler es gleichzeitig aufnehmen.
            pendingCount++;
            pendingMass += mass;

            NetSync.Submit(NetCommand.StoreAdd(domain, id, index, prefabName, metal, (int)kind, mass));
            return;
        }

        ExecuteAdd(prefabName, kind, metal, mass, false);
    }

    // ---------- Mehrspieler ----------

    // Wo haengt dieses Lager? domain: 0 = Maschine / Szenenobjekt, 1 = loser Gegenstand (Schubkarre),
    // 2 = Fahrzeug. id = Nummer des Besitzers, index = Nummer unter den Lagern darin.
    private bool Describe(out int domain, out int id, out int index)
    {
        domain = 0;
        id = 0;
        index = -1;

        TrainCar car = GetComponentInParent<TrainCar>();

        if (car != null)
        {
            if (car.NetId <= 0)
                return false;

            domain = 2;
            id = car.NetId;
            index = Array.IndexOf(car.GetComponentsInChildren<ItemStorage>(true), this);
            return index >= 0;
        }

        NetItem item = GetComponentInParent<NetItem>();

        if (item != null)
        {
            if (item.Id <= 0)
                return false;

            domain = 1;
            id = item.Id;
            index = Array.IndexOf(item.GetComponentsInChildren<ItemStorage>(true), this);
            return index >= 0;
        }

        Transform root;

        if (NetSync.DescribeRoot(this, out id, out root))
        {
            domain = 0;
            index = Array.IndexOf(root.GetComponentsInChildren<ItemStorage>(true), this);
            return index >= 0;
        }

        return false;
    }

    // Findet das Lager zu einer Beschreibung (siehe Describe).
    public static ItemStorage Resolve(int domain, int id, int index)
    {
        Component owner = null;

        if (domain == 2)
        {
            TrackManager m = TrackManager.Instance;
            owner = m != null ? m.FindCar(id) : null;
        }
        else if (domain == 1)
        {
            owner = NetItems.ById(id);
        }
        else
        {
            owner = NetSync.ResolveRoot(id);
        }

        if (owner == null)
            return null;

        ItemStorage[] list = owner.GetComponentsInChildren<ItemStorage>(true);
        return index >= 0 && index < list.Length ? list[index] : null;
    }

    // AUS DEM KOMMANDO: legt ein Stueck oben auf den Haufen.
    public void ExecuteAdd(string prefabName, ItemKind kind, string metal, float mass, bool fromLocalCommand)
    {
        if (fromLocalCommand && pendingCount > 0)
        {
            pendingCount--;
            pendingMass = Mathf.Max(0f, pendingMass - mass);
        }

        if (entries.Count >= maxItems)
            return;

        GameObject prefab = FindPrefab(prefabName);

        if (prefab == null)
            return;

        Entry e = new Entry();
        e.prefab = prefabName;
        e.kind = kind;
        e.metal = metal ?? "";
        e.mass = mass;

        AddEntry(e, prefab);
    }

    // Die Prefab-Namen gehen mit "|" getrennt durchs Netz, deshalb kein "|" im Namen verwenden.
    public void ExecuteAdd(string prefabName, ItemKind kind, string metal, float mass)
    {
        ExecuteAdd(prefabName, kind, metal, mass, false);
    }

    // AUS DEM KOMMANDO: nimmt das oberste Stueck heraus. Das lose Stueck erzeugt nur der Host
    // (er meldet es allen). Gibt es dort zurueck, sonst null.
    public GameObject ExecuteTake(Vector3 position, Quaternion rotation, int taker = 0)
    {
        if (entries.Count == 0)
            return null;

        Entry e = entries[entries.Count - 1];
        GameObject prefab = FindPrefab(e.prefab);

        GameObject go = null;

        if (prefab != null && !NetSync.IsClient)
        {
            go = NetItems.Spawn(prefab, position, rotation, Vector3.zero,
                                e.kind, e.metal, e.mass, true);

            // Hat ein Mitspieler das Stueck genommen, haelt er es sofort in der Hand
            // (der Host merkt sich ihn als Traeger und meldet es allen).
            if (go != null && taker > 0)
                NetItems.GiveToPlayer(go, taker);
        }

        entries.RemoveAt(entries.Count - 1);
        StoredMassKg = Mathf.Max(0f, StoredMassKg - e.mass);

        if (e.visual != null)
            Destroy(e.visual);

        return go;
    }

    private void AddEntry(Entry e, GameObject prefab)
    {
        entries.Add(e);
        StoredMassKg += e.mass;
        e.visual = BuildVisual(prefab, entries.Count - 1);
    }

    // ---------- Herausnehmen ----------

    // Zaehlt dieser Collider beim Herausnehmen mit E?
    public bool AcceptsTakeOut(Collider col)
    {
        if (takeFrom == null || takeFrom.Length == 0)
            return true;

        for (int i = 0; i < takeFrom.Length; i++)
        {
            if (takeFrom[i] != null && (takeFrom[i] == col || col.transform.IsChildOf(takeFrom[i].transform)))
                return true;
        }

        return false;
    }

    // Nimmt das oberste Stueck heraus und erzeugt es als losen Gegenstand an der Position.
    // Gibt ihn zurueck (oder null, wenn leer / nicht moeglich). Als Mitspieler kommt das Stueck
    // vom Host: dann ist das Ergebnis null (CoalPickup wartet auf die Uebergabe in die Hand).
    public GameObject TakeOut(Vector3 position, Quaternion rotation)
    {
        if (entries.Count == 0)
            return null;

        int domain, id, index;

        if (!NetSync.IsOnline || !Describe(out domain, out id, out index))
            return ExecuteTake(position, rotation);

        lastTaken = null;
        NetSync.Submit(NetCommand.StoreTake(domain, id, index, position, rotation));

        GameObject result = lastTaken;
        lastTaken = null;

        return result;
    }

    // ---------- Optik ----------

    private GameObject BuildVisual(GameObject prefab, int index)
    {
        if (holder == null)
        {
            holder = new GameObject("LagerVorbereitung");
            holder.transform.SetParent(transform, false);
            holder.SetActive(false);   // inaktiv: Skripte der Kopie starten nie
        }

        // Kopie im inaktiven Objekt erzeugen: Ihre Skripte laufen dadurch nicht an
        // (sonst wuerde sie sich z. B. als echter Gegenstand anmelden).
        GameObject v = Instantiate(prefab, holder.transform);
        v.name = "Stueck";

        foreach (MonoBehaviour mb in v.GetComponentsInChildren<MonoBehaviour>(true))
            DestroyImmediate(mb);

        foreach (Collider c in v.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(c);

        foreach (Rigidbody r in v.GetComponentsInChildren<Rigidbody>(true))
            DestroyImmediate(r);

        Transform parent = pileCenter != null ? pileCenter : transform;

        v.transform.SetParent(parent, false);

        // Groesse wie das Original in der Welt, auch wenn die Karre skaliert ist.
        Vector3 world = prefab.transform.lossyScale;
        Vector3 ps = parent.lossyScale;

        v.transform.localScale = new Vector3(
            ps.x != 0f ? world.x / ps.x : world.x,
            ps.y != 0f ? world.y / ps.y : world.y,
            ps.z != 0f ? world.z / ps.z : world.z);

        // Platz im Haufen: gleichmaessig verteilt, mit festem Zufall pro Nummer.
        System.Random rng = new System.Random(index * 7919 + 13);
        int perLayer = Mathf.Max(1, piecesPerLayer);
        int layer = index / perLayer;

        float x = ((float)rng.NextDouble() - 0.5f) * pileSize.x;
        float z = ((float)rng.NextDouble() - 0.5f) * pileSize.z;
        float y = Mathf.Min(layer * layerHeight, pileSize.y) + (float)rng.NextDouble() * 0.02f;

        v.transform.localPosition = new Vector3(x, y, z);
        v.transform.localRotation = Quaternion.Euler(
            (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);

        return v;
    }

    private static string CleanName(string name)
    {
        int i = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
        return (i >= 0 ? name.Substring(0, i) : name).Trim();
    }

    private static GameObject FindPrefab(string name)
    {
        SaveSystem save = SaveSystem.Instance;
        return save != null ? save.FindItemPrefab(name) : null;
    }

    // ---------- Speichern ----------

    private static string Format(Entry e)
    {
        return e.prefab + ";" + (int)e.kind + ";" + e.metal + ";" +
               e.mass.ToString("R", CultureInfo.InvariantCulture);
    }

    public void SaveState(StateBag bag)
    {
        bag.SetInt("n", entries.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];

            bag.SetString("i" + i, Format(e));
        }
    }

    public void LoadState(StateBag bag)
    {
        // Unveraendert (z. B. regelmaessiger Abgleich im Mehrspieler)? Dann nichts neu aufbauen.
        int newCount = bag.GetInt("n", 0);

        if (newCount == entries.Count)
        {
            bool same = true;

            for (int i = 0; i < newCount && same; i++)
                same = bag.GetString("i" + i) == Format(entries[i]);

            if (same)
                return;
        }

        // Alte Stuecke entfernen.
        foreach (Entry e in entries)
        {
            if (e.visual != null)
                Destroy(e.visual);
        }

        entries.Clear();
        StoredMassKg = 0f;

        int count = bag.GetInt("n", 0);

        for (int i = 0; i < count; i++)
        {
            string[] s = bag.GetString("i" + i).Split(';');

            if (s.Length < 4)
                continue;

            GameObject prefab = FindPrefab(s[0]);

            if (prefab == null)
            {
                Debug.LogWarning("ItemStorage: Gespeichertes Stueck '" + s[0] +
                                 "' nicht gefunden (SaveSystem > Item Prefabs).", this);
                continue;
            }

            Entry entry = new Entry();
            entry.prefab = s[0];

            int kind;
            int.TryParse(s[1], out kind);
            entry.kind = (ItemKind)kind;
            entry.metal = s[2];

            float mass;
            float.TryParse(s[3], NumberStyles.Float, CultureInfo.InvariantCulture, out mass);
            entry.mass = mass;

            AddEntry(entry, prefab);
        }
    }
}
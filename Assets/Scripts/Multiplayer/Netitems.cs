using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Synchronisation der losen Gegenstaende (Kohle, Erz, Barren, Schlacke, Kanister ...)
// im Mehrspieler.
//
// Prinzip:
//  - Jeder lose Gegenstand (mit "SavedItem") bekommt automatisch eine NetItem-Komponente
//    und vom Host eine Nummer (Id).
//  - Die PHYSIK eines Gegenstands rechnet immer genau ein Spieler: sein "Besitzer"
//    (Owner). Das ist der Host oder derjenige, der ihn zuletzt aufgehoben und fallen
//    gelassen hat. Bei allen anderen ist der Gegenstand "kinematisch" und folgt weich den
//    Positionen, die der Besitzer ca. 10 Mal pro Sekunde schickt.
//  - Aufheben: Der Spieler fragt beim Host an (Grab). Erst wenn der Host bestaetigt, haelt
//    er den Gegenstand. So kann ihn niemand doppelt aufheben.
//  - Erzeugen: Der Host erzeugt neue Gegenstaende (Hochofen, Giesserei) und meldet sie
//    allen. Wer sie selbst erzeugt (Graben), bittet den Host darum (SpawnRequest).
//  - Verbrauchen (Einwurf in Kessel/Ofen/Giesserei/Tank): Nur der Besitzer fuehrt das
//    aus und meldet das Entfernen (Remove) an alle.
//  - Beim Beitreten stehen die Gegenstaende samt Nummer im Spielstand des Hosts.
//
// Gegenstaende ohne "SavedItem" (und ohne Eintrag unter "Item Prefabs" am SaveSystem)
// bleiben lokal, wie vorher.
public class NetItem : MonoBehaviour
{
    public int Id;              // Nummer (0 = keine, nur lokal)
    public int OwnerId;         // wer die Physik rechnet (0 = Host)
    public int HeldBy = -1;     // wer ihn gerade traegt (-1 = niemand)

    private Rigidbody body;
    private Collider[] colliders;
    private FuelCanister canister;

    // Schubkarren behalten ihre Collider, auch wenn ein anderer Spieler sie schiebt
    // (sonst fallen Ladung und Spieler durch sie hindurch).
    private bool keepCollidersWhenHeld;

    private bool netControlled;   // wir haben ihn kinematisch gemacht (und muessen ihn spaeter freigeben)
    private bool hasTarget;
    private Vector3 targetPos;
    private Quaternion targetRot;

    // Zuletzt gesendeter Zustand (nur Aenderungen werden uebertragen).
    private Vector3 sentPos;
    private Quaternion sentRot;
    private float sentLiters;

    public float Liters
    {
        get { return canister != null ? canister.Liters : 0f; }
        set { if (canister != null) canister.Liters = value; }
    }

    // Rechnet DIESER Rechner die Physik?
    public bool IsMine
    {
        get { return !NetSync.IsOnline || OwnerId == NetSync.LocalId; }
    }

    public bool HeldByMe
    {
        get { return NetSync.IsOnline && HeldBy == NetSync.LocalId; }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
        canister = GetComponent<FuelCanister>();
        keepCollidersWhenHeld = GetComponent<Wheelbarrow>() != null;

        MarkSent();
        NetItems.Register(this);
    }

    private void OnDestroy()
    {
        NetItems.Unregister(this);
    }

    private void Update()
    {
        if (!NetSync.IsOnline || !hasTarget)
            return;

        // Nur Gegenstaende, die ein anderer Spieler bewegt, werden nachgefuehrt.
        bool followRemote = !IsMine || (HeldBy >= 0 && HeldBy != NetSync.LocalId);

        if (!followRemote)
            return;

        // Weit weg (neu erschienen oder Sprung): sofort hin.
        if ((transform.position - targetPos).sqrMagnitude > 25f)
        {
            transform.SetPositionAndRotation(targetPos, targetRot);
            return;
        }

        float k = 1f - Mathf.Exp(-18f * Time.deltaTime);

        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, targetPos, k),
            Quaternion.Slerp(transform.rotation, targetRot, k));
    }

    public void SetTarget(Vector3 position, Quaternion rotation)
    {
        targetPos = position;
        targetRot = rotation;

        if (!hasTarget)
        {
            hasTarget = true;
            transform.SetPositionAndRotation(position, rotation);
        }
    }

    // Stellt Physik und Collider passend zu Besitzer und Traeger ein.
    public void ApplyOwnership()
    {
        if (!NetSync.IsOnline)
        {
            ReleaseControl();
            return;
        }

        bool heldByOther = HeldBy >= 0 && HeldBy != NetSync.LocalId;
        bool heldByMe = HeldBy == NetSync.LocalId;

        // Beim eigenen Tragen steuert CoalPickup die Physik.
        if (heldByMe)
            return;

        if (heldByOther || OwnerId != NetSync.LocalId)
        {
            TakeControl(!heldByOther || keepCollidersWhenHeld);
        }
        else
        {
            ReleaseControl();
        }
    }

    private void TakeControl(bool collidersOn)
    {
        netControlled = true;

        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.isKinematic = true;
        }

        SetColliders(collidersOn);
    }

    // Wieder normal von der Physik bewegt (nur, wenn wir ihn vorher uebernommen hatten).
    public void ReleaseControl()
    {
        if (!netControlled)
            return;

        netControlled = false;
        SetColliders(true);

        if (body != null)
        {
            body.isKinematic = false;
            body.useGravity = true;
        }
    }

    private void SetColliders(bool on)
    {
        if (colliders == null)
            return;

        foreach (Collider c in colliders)
        {
            if (c != null)
                c.enabled = on;
        }
    }

    // Hat sich etwas geaendert, das der Besitzer melden muss?
    public bool NeedsSend()
    {
        if (!IsMine)
            return false;

        if (HeldByMe)
            return true;

        if ((transform.position - sentPos).sqrMagnitude > 0.00001f)
            return true;

        if (Quaternion.Angle(transform.rotation, sentRot) > 0.3f)
            return true;

        return Mathf.Abs(Liters - sentLiters) > 0.01f;
    }

    public void MarkSent()
    {
        sentPos = transform.position;
        sentRot = transform.rotation;
        sentLiters = Liters;
    }
}

public static class NetItems
{
    // Nachrichtentypen (laufen ueber NetSync).
    public const byte MsgSpawn = 40;         // Host -> alle: neuer Gegenstand
    public const byte MsgSpawnRequest = 41;  // Mitspieler -> Host: bitte erzeugen
    public const byte MsgStates = 42;        // Besitzer -> alle: Positionen
    public const byte MsgGrab = 43;          // Mitspieler -> Host: aufheben
    public const byte MsgOwner = 44;         // Host -> alle: Besitzer/Traeger geaendert
    public const byte MsgDrop = 45;          // Traeger -> Host: fallen gelassen
    public const byte MsgRemove = 46;        // jemand -> Host -> alle: Gegenstand entfernt

    private const int BatchSize = 100;

    private static readonly List<NetItem> all = new List<NetItem>();
    private static readonly Dictionary<int, NetItem> byId = new Dictionary<int, NetItem>();
    private static readonly List<KeyValuePair<byte, byte[]>> pending =
        new List<KeyValuePair<byte, byte[]>>();

    private static int nextId = 1;

    public static bool IsItemMessage(byte type)
    {
        return type >= MsgSpawn && type <= MsgRemove;
    }

    // ---------- Verzeichnis ----------

    public static void Register(NetItem item)
    {
        if (!all.Contains(item))
            all.Add(item);
    }

    public static void Unregister(NetItem item)
    {
        all.Remove(item);

        NetItem current;

        if (item.Id != 0 && byId.TryGetValue(item.Id, out current) && current == item)
            byId.Remove(item.Id);
    }

    private static NetItem GetOrAdd(GameObject go)
    {
        NetItem item = go.GetComponent<NetItem>();

        if (item == null)
            item = go.AddComponent<NetItem>();

        return item;
    }

    private static NetItem Find(int id)
    {
        NetItem item;
        return byId.TryGetValue(id, out item) && item != null ? item : null;
    }

    private static void AssignId(NetItem item)
    {
        if (item.Id != 0)
            return;

        item.Id = nextId++;
        byId[item.Id] = item;
    }

    // Beim Start als Host: Alle vorhandenen Gegenstaende bekommen Nummern.
    public static void OnHostStarted()
    {
        nextId = 1;
        byId.Clear();

        foreach (NetItem item in all)
        {
            if (item == null)
                continue;

            item.Id = 0;
            item.OwnerId = 0;
            item.HeldBy = -1;
            AssignId(item);
        }
    }

    // Beim Beitreten: Gegenstand aus dem Spielstand des Hosts uebernimmt dessen Nummer.
    public static void Adopt(GameObject go, int id)
    {
        if (go == null || id <= 0)
            return;

        NetItem item = GetOrAdd(go);

        item.Id = id;
        item.OwnerId = 0;
        item.HeldBy = -1;
        byId[id] = item;

        item.ApplyOwnership();
    }

    public static int IdOf(GameObject go)
    {
        NetItem item = go != null ? go.GetComponent<NetItem>() : null;
        return item != null ? item.Id : 0;
    }

    // Verbindung beendet: alle Gegenstaende wieder normal und lokal.
    public static void OnDisconnected()
    {
        pending.Clear();
        byId.Clear();

        foreach (NetItem item in all)
        {
            if (item == null)
                continue;

            item.Id = 0;
            item.OwnerId = 0;
            item.HeldBy = -1;
            item.ReleaseControl();
        }
    }

    // Ein Mitspieler ist gegangen: seine Gegenstaende gehen an den Host, getragene fallen.
    public static void OnPlayerLeft(int player)
    {
        if (NetSync.Mode != NetMode.Host)
            return;

        foreach (NetItem item in all)
        {
            if (item == null || (item.OwnerId != player && item.HeldBy != player))
                continue;

            if (item.OwnerId == player)
                item.OwnerId = 0;

            if (item.HeldBy == player)
                item.HeldBy = -1;

            item.ApplyOwnership();
            BroadcastOwner(item);
        }
    }

    // Der Mitspieler ist fertig geladen: wartende Nachrichten verarbeiten.
    public static void OnReady()
    {
        List<KeyValuePair<byte, byte[]>> list =
            new List<KeyValuePair<byte, byte[]>>(pending);

        pending.Clear();

        foreach (KeyValuePair<byte, byte[]> message in list)
            ProcessClient(message.Key, message.Value);
    }

    // ---------- Fuer Spielskripte ----------

    // Darf dieser Rechner mit dem Gegenstand etwas ausloesen (z. B. in einen Ofen werfen)?
    // Nur der Besitzer; sonst wuerde es jeder Mitspieler einmal tun.
    public static bool CanAct(GameObject itemObject)
    {
        if (!NetSync.IsOnline || itemObject == null)
            return true;

        NetItem item = itemObject.GetComponentInParent<NetItem>();

        if (item == null || item.Id == 0)
            return true;

        return item.OwnerId == NetSync.LocalId && item.HeldBy < 0;
    }

    // Gegenstand verbraucht (eingeworfen, leer ...): bei allen entfernen.
    public static void Consume(GameObject itemObject)
    {
        if (itemObject == null)
            return;

        NetItem item = itemObject.GetComponentInParent<NetItem>();

        if (!NetSync.IsOnline || item == null || item.Id == 0)
        {
            Object.Destroy(itemObject);
            return;
        }

        SendRemove(item.Id);
        Object.Destroy(item.gameObject);
    }

    // Aufheben: Gibt true zurueck, wenn der Gegenstand sofort getragen werden darf (offline oder
    // als Host). Als Mitspieler wartet man auf die Bestaetigung (item.HeldByMe wird wahr).
    public static bool BeginGrab(NetItem item)
    {
        if (!NetSync.IsOnline || item == null || item.Id == 0)
            return true;

        if (item.HeldBy >= 0 && item.HeldBy != NetSync.LocalId)
            return false;

        if (NetSync.Mode == NetMode.Host)
        {
            item.HeldBy = 0;
            item.OwnerId = 0;
            BroadcastOwner(item);
            return true;
        }

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(item.Id);
            w.Flush();
            NetSync.SendItemMessage(MsgGrab, ms.ToArray(), -1);
        }

        return false;
    }

    // Fallen gelassen: Der Traeger bleibt Besitzer und rechnet die Physik weiter.
    public static void Release(NetItem item)
    {
        if (!NetSync.IsOnline || item == null || item.Id == 0)
            return;

        item.HeldBy = -1;
        item.OwnerId = NetSync.LocalId;
        item.MarkSent();

        if (NetSync.Mode == NetMode.Host)
        {
            BroadcastOwner(item);
            return;
        }

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(item.Id);
            w.Flush();
            NetSync.SendItemMessage(MsgDrop, ms.ToArray(), -1);
        }
    }

    // Erzeugt einen losen Gegenstand.
    //  fromMachine = true: Eine Maschine (Hochofen, Giesserei) erzeugt ihn. Das macht nur der Host,
    //                      bei Mitspielern passiert nichts (sie bekommen ihn vom Host).
    //  fromMachine = false: Ein Spieler erzeugt ihn (Graben). Mitspieler bitten den Host darum.
    // Gibt den lokalen Gegenstand zurueck (bei Mitspielern null: er erscheint kurz danach).
    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
        Vector3 velocity, ItemKind kind, string metal, float mass, bool fromMachine)
    {
        if (prefab == null)
            return null;

        if (NetSync.IsClient)
        {
            if (!fromMachine)
                SendSpawnRequest(prefab.name, position, rotation, velocity, kind, metal, mass);

            return null;
        }

        GameObject go = CreateLocal(prefab, position, rotation, kind, metal, mass);

        Rigidbody rb = go.GetComponent<Rigidbody>();

        if (rb != null && velocity != Vector3.zero)
            rb.AddForce(velocity, ForceMode.VelocityChange);

        if (NetSync.Mode == NetMode.Host)
        {
            NetItem item = GetOrAdd(go);
            item.OwnerId = 0;
            item.HeldBy = -1;
            AssignId(item);

            BroadcastSpawn(item, prefab.name, velocity, kind, metal, mass);
        }

        return go;
    }

    // Die Platte/der Gegenstand in der Naehe eines Punkts (z. B. in einer Giessform).
    public static Transform FindNear(Vector3 point, float radius, ItemKind kind)
    {
        float best = radius * radius;
        Transform result = null;

        foreach (NetItem item in all)
        {
            if (item == null)
                continue;

            float d = (item.transform.position - point).sqrMagnitude;

            if (d > best || ItemTag.KindOf(item) != kind)
                continue;

            best = d;
            result = item.transform;
        }

        return result;
    }

    // ---------- Erzeugen ----------

    private static GameObject CreateLocal(GameObject prefab, Vector3 position, Quaternion rotation,
        ItemKind kind, string metal, float mass)
    {
        GameObject go = Object.Instantiate(prefab, position, rotation);
        go.name = prefab.name;

        ItemTag tag = go.GetComponent<ItemTag>();

        if (tag == null)
            tag = go.AddComponent<ItemTag>();

        tag.kind = kind;
        tag.metal = metal ?? "";
        tag.massKg = mass;

        GetOrAdd(go);
        return go;
    }

    private static GameObject FindPrefab(string name)
    {
        SaveSystem save = SaveSystem.Instance;
        return save != null ? save.FindItemPrefab(name) : null;
    }

    private static void WriteSpawn(BinaryWriter w, string prefab, Vector3 position,
        Quaternion rotation, Vector3 velocity, ItemKind kind, string metal, float mass)
    {
        w.Write(prefab ?? "");
        NetIO.Write(w, position);
        NetIO.Write(w, rotation);
        NetIO.Write(w, velocity);
        w.Write((int)kind);
        w.Write(metal ?? "");
        w.Write(mass);
    }

    private static void BroadcastSpawn(NetItem item, string prefab, Vector3 velocity,
        ItemKind kind, string metal, float mass)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(item.Id);
            w.Write(item.OwnerId);
            WriteSpawn(w, prefab, item.transform.position, item.transform.rotation,
                velocity, kind, metal, mass);
            w.Flush();

            NetSync.SendItemMessage(MsgSpawn, ms.ToArray(), -1);
        }
    }

    private static void SendSpawnRequest(string prefab, Vector3 position, Quaternion rotation,
        Vector3 velocity, ItemKind kind, string metal, float mass)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            WriteSpawn(w, prefab, position, rotation, velocity, kind, metal, mass);
            w.Flush();

            NetSync.SendItemMessage(MsgSpawnRequest, ms.ToArray(), -1);
        }
    }

    private static void BroadcastOwner(NetItem item)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(item.Id);
            w.Write(item.OwnerId);
            w.Write(item.HeldBy);
            w.Flush();

            NetSync.SendItemMessage(MsgOwner, ms.ToArray(), -1);
        }
    }

    private static void SendRemove(int id)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(id);
            w.Flush();

            NetSync.SendItemMessage(MsgRemove, ms.ToArray(), -1);
        }
    }

    // ---------- Zustaende senden (vom Runner aufgerufen) ----------

    public static void SendStates()
    {
        List<NetItem> changed = new List<NetItem>();

        foreach (NetItem item in all)
        {
            if (item != null && item.Id != 0 && item.NeedsSend())
                changed.Add(item);
        }

        for (int start = 0; start < changed.Count; start += BatchSize)
        {
            int count = Mathf.Min(BatchSize, changed.Count - start);

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(count);

                for (int i = 0; i < count; i++)
                {
                    NetItem item = changed[start + i];

                    w.Write(item.Id);
                    w.Write(item.OwnerId);
                    w.Write(item.HeldBy);
                    NetIO.Write(w, item.transform.position);
                    NetIO.Write(w, item.transform.rotation);
                    w.Write(item.Liters);

                    item.MarkSent();
                }

                w.Flush();
                NetSync.SendItemMessage(MsgStates, ms.ToArray(), -1);
            }
        }
    }

    // ---------- Nachrichten empfangen ----------

    // Auf dem Host: Nachricht eines Mitspielers.
    public static void HandleHost(int sender, byte type, byte[] payload)
    {
        using (MemoryStream ms = new MemoryStream(payload))
        using (BinaryReader r = new BinaryReader(ms))
        {
            switch (type)
            {
                case MsgSpawnRequest:
                {
                    string prefabName = r.ReadString();
                    Vector3 position = NetIO.ReadVector3(r);
                    Quaternion rotation = NetIO.ReadQuaternion(r);
                    Vector3 velocity = NetIO.ReadVector3(r);
                    ItemKind kind = (ItemKind)r.ReadInt32();
                    string metal = r.ReadString();
                    float mass = r.ReadSingle();

                    GameObject prefab = FindPrefab(prefabName);

                    if (prefab == null)
                    {
                        Debug.LogWarning("Mehrspieler: Gegenstand nicht gefunden (SaveSystem > Item Prefabs): "
                            + prefabName);
                        return;
                    }

                    GameObject go = CreateLocal(prefab, position, rotation, kind, metal, mass);
                    NetItem item = GetOrAdd(go);

                    item.OwnerId = sender;
                    item.HeldBy = -1;
                    AssignId(item);
                    item.ApplyOwnership();
                    item.SetTarget(position, rotation);

                    BroadcastSpawn(item, prefabName, velocity, kind, metal, mass);
                    break;
                }

                case MsgGrab:
                {
                    NetItem item = Find(r.ReadInt32());

                    if (item == null)
                        return;

                    // Nur wenn ihn niemand anderes traegt.
                    if (item.HeldBy < 0 || item.HeldBy == sender)
                    {
                        item.HeldBy = sender;
                        item.OwnerId = sender;
                        item.ApplyOwnership();
                    }

                    // Antwort an alle (auch bei Ablehnung: dann steht der wahre Traeger drin).
                    BroadcastOwner(item);
                    break;
                }

                case MsgDrop:
                {
                    NetItem item = Find(r.ReadInt32());

                    if (item == null || item.HeldBy != sender)
                        return;

                    item.HeldBy = -1;
                    item.OwnerId = sender;
                    item.ApplyOwnership();
                    BroadcastOwner(item);
                    break;
                }

                case MsgStates:
                    ReadStates(r, sender);
                    NetSync.SendItemMessage(type, payload, sender);
                    break;

                case MsgRemove:
                {
                    int id = r.ReadInt32();
                    NetItem item = Find(id);

                    if (item != null)
                        Object.Destroy(item.gameObject);

                    SendRemove(id);
                    break;
                }
            }
        }
    }

    // Auf einem Mitspieler: Nachricht des Hosts (wartet, bis die Welt geladen ist).
    public static void HandleClient(byte type, byte[] payload, bool ready)
    {
        if (!ready)
        {
            pending.Add(new KeyValuePair<byte, byte[]>(type, payload));
            return;
        }

        ProcessClient(type, payload);
    }

    private static void ProcessClient(byte type, byte[] payload)
    {
        using (MemoryStream ms = new MemoryStream(payload))
        using (BinaryReader r = new BinaryReader(ms))
        {
            switch (type)
            {
                case MsgSpawn:
                {
                    int id = r.ReadInt32();
                    int owner = r.ReadInt32();
                    string prefabName = r.ReadString();
                    Vector3 position = NetIO.ReadVector3(r);
                    Quaternion rotation = NetIO.ReadQuaternion(r);
                    Vector3 velocity = NetIO.ReadVector3(r);
                    ItemKind kind = (ItemKind)r.ReadInt32();
                    string metal = r.ReadString();
                    float mass = r.ReadSingle();

                    // Schon vorhanden (stand im Spielstand beim Beitritt).
                    if (Find(id) != null)
                        return;

                    GameObject prefab = FindPrefab(prefabName);

                    if (prefab == null)
                    {
                        Debug.LogWarning("Mehrspieler: Gegenstand nicht gefunden (SaveSystem > Item Prefabs): "
                            + prefabName);
                        return;
                    }

                    GameObject go = CreateLocal(prefab, position, rotation, kind, metal, mass);
                    NetItem item = GetOrAdd(go);

                    item.Id = id;
                    item.OwnerId = owner;
                    item.HeldBy = -1;
                    byId[id] = item;
                    item.SetTarget(position, rotation);
                    item.ApplyOwnership();

                    // Wer ihn selbst erzeugt hat (Graben), rechnet auch die Flugphysik.
                    Rigidbody rb = go.GetComponent<Rigidbody>();

                    if (owner == NetSync.LocalId && rb != null && velocity != Vector3.zero)
                        rb.AddForce(velocity, ForceMode.VelocityChange);

                    item.MarkSent();
                    break;
                }

                case MsgOwner:
                {
                    NetItem item = Find(r.ReadInt32());
                    int owner = r.ReadInt32();
                    int heldBy = r.ReadInt32();

                    if (item == null)
                        return;

                    item.OwnerId = owner;
                    item.HeldBy = heldBy;
                    item.ApplyOwnership();
                    break;
                }

                case MsgStates:
                    ReadStates(r, -1);
                    break;

                case MsgRemove:
                {
                    NetItem item = Find(r.ReadInt32());

                    if (item != null)
                        Object.Destroy(item.gameObject);

                    break;
                }
            }
        }
    }

    // Positionen lesen. sender >= 0: Nur Eintraege akzeptieren, die der Sender besitzt.
    private static void ReadStates(BinaryReader r, int sender)
    {
        int count = r.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            int id = r.ReadInt32();
            int owner = r.ReadInt32();
            int heldBy = r.ReadInt32();
            Vector3 position = NetIO.ReadVector3(r);
            Quaternion rotation = NetIO.ReadQuaternion(r);
            float liters = r.ReadSingle();

            NetItem item = Find(id);

            if (item == null)
                continue;

            // Gegenstaende, die ich selbst besitze, nie von aussen verschieben.
            if (owner == NetSync.LocalId)
                continue;

            if (sender >= 0 && owner != sender)
                continue;

            bool changed = item.OwnerId != owner || item.HeldBy != heldBy;

            item.OwnerId = owner;
            item.HeldBy = heldBy;

            if (changed)
                item.ApplyOwnership();

            item.SetTarget(position, rotation);
            item.Liters = liters;
        }
    }
}

// Schickt regelmaessig die Positionen aller selbst berechneten Gegenstaende.
// Entsteht beim Spielstart von selbst.
public class NetItemsRunner : MonoBehaviour
{
    [Tooltip("So oft pro Sekunde werden Aenderungen der eigenen Gegenstaende verschickt.")]
    [SerializeField] private float sendRate = 10f;

    private float timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindObjectOfType<NetItemsRunner>() == null)
            new GameObject("NetItemsRunner").AddComponent<NetItemsRunner>();
    }

    private void Update()
    {
        if (!NetSync.IsOnline || !NetSync.IsReady)
            return;

        timer += Time.unscaledDeltaTime;

        if (timer < 1f / Mathf.Max(1f, sendRate))
            return;

        timer = 0f;
        NetItems.SendStates();
    }
}
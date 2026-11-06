using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public enum NetMode
{
    Offline,
    Host,
    Client
}

// Mehrspieler (Koop) fuer 2 bis 4 Spieler.
//
// Prinzip: Ein Spieler ist der Host. Alle Spieler rechnen die Welt selbst
// (Maschinen, Strom ...). Alles, was die Welt veraendert (bauen, graben,
// Schalter ...), laeuft als Kommando ueber den Host an alle. Der Host schickt
// zusaetzlich alle paar Sekunden den Zustand aller Maschinen, damit kleine
// Abweichungen sofort wieder ausgeglichen werden.
//
// Beim Beitreten schickt der Host die ganze Welt (wie ein Spielstand):
// Maschinen, Rohre, Gelaende-Aenderungen, Tageszeit.
//
// Die Verbindung ist direkt (TCP, Port frei waehlbar). Im selben Netzwerk
// reicht die IP-Adresse des Hosts. Ueber das Internet muss der Host den Port
// am Router freigeben, oder ihr nutzt ein VPN (z. B. Tailscale, ZeroTier).
public class NetSync : MonoBehaviour
{
    public const int ProtocolVersion = 4;
    public const int MaxPlayers = 4;

    // Nachrichtentypen
    private const byte MsgHello = 1;
    private const byte MsgWelcome = 2;
    private const byte MsgReject = 3;
    private const byte MsgPlayerJoined = 4;
    private const byte MsgPlayerLeft = 5;
    private const byte MsgCommand = 10;
    private const byte MsgPlayerState = 20;
    private const byte MsgSnapshot = 30;

    [Header("Abgleich")]
    [Tooltip("Der Host schickt so oft (in Sekunden) den Zustand aller Maschinen.")]
    [SerializeField] private float snapshotInterval = 1.5f;

    [Tooltip("So oft pro Sekunde wird die eigene Position verschickt.")]
    [SerializeField] private float positionRate = 15f;

    public static NetSync Instance { get; private set; }
    public static NetMode Mode { get; private set; }
    public static int LocalId { get; private set; }

    public static bool IsOnline { get { return Mode != NetMode.Offline; } }
    public static bool IsClient { get { return Mode == NetMode.Client; } }

    // Welt geladen und bereit (als Host sofort, als Mitspieler nach dem Beitritts-Laden).
    public static bool IsReady { get { return ready; } }

    // Schickt eine Nachricht zu losen Gegenstaenden (siehe NetItems.cs): als Mitspieler an den Host,
    // als Host an alle Mitspieler ausser "exceptPlayer" (-1 = an alle).
    public static void SendItemMessage(byte type, byte[] payload, int exceptPlayer)
    {
        if (Instance != null)
            Instance.SendItemInternal(type, payload, exceptPlayer);
    }

    private void SendItemInternal(byte type, byte[] payload, int exceptPlayer)
    {
        if (Mode == NetMode.Client)
        {
            if (clientConnection != null && clientConnection.Alive && ready)
                clientConnection.Send(type, payload);

            return;
        }

        if (Mode == NetMode.Host)
        {
            foreach (Peer peer in peers)
            {
                if (peer.joined && peer.id != exceptPlayer)
                    peer.conn.Send(type, payload);
            }
        }
    }

    // ---------- Anzeige fuer das Menue ----------

    public static string PlayerName
    {
        get { return PlayerPrefs.GetString("net.name", SystemInfoName()); }
        set { PlayerPrefs.SetString("net.name", value); }
    }

    public static string LocalAddresses { get; private set; }

    public static int PlayerCount { get { return names.Count; } }

    public static string StatusText
    {
        get
        {
            switch (Mode)
            {
                case NetMode.Host:
                    return Loc.T("net.status.host", names.Count, hostPort);
                case NetMode.Client:
                    if (connecting) return Loc.T("net.status.connecting", remoteAddress);
                    if (!ready) return Loc.T("net.status.loading");
                    return Loc.T("net.status.client", remoteAddress);
                default:
                    return Loc.T("net.status.offline");
            }
        }
    }

    // Namen aller Spieler: "Name" und bei dir selbst mit Zusatz.
    public static List<string> PlayerList()
    {
        List<string> list = new List<string>();

        foreach (KeyValuePair<int, string> pair in names)
        {
            string text = pair.Value;

            if (pair.Key == LocalId)
                text += "  " + Loc.T("net.you");

            if (pair.Key == 0)
                text += "  (Host)";

            list.Add(text);
        }

        return list;
    }

    private static string SystemInfoName()
    {
        string name = SystemInfo.deviceName;

        if (string.IsNullOrEmpty(name) || name == "<unknown>")
            name = "Player";

        return name.Length > 20 ? name.Substring(0, 20) : name;
    }

    // ---------- Zustand ----------

    private class Peer
    {
        public NetConnection conn;
        public int id = -1;
        public string name = "";
        public bool joined;
    }

    private static readonly Dictionary<int, string> names = new Dictionary<int, string>();
    private static string remoteAddress = "";
    private static int hostPort;
    private static bool connecting;
    private static bool ready;

    private readonly List<Peer> peers = new List<Peer>();
    private readonly Dictionary<int, RemotePlayer> avatars = new Dictionary<int, RemotePlayer>();
    private readonly List<NetCommand> pendingCommands = new List<NetCommand>();

    private Socket listener;
    private NetConnection clientConnection;
    private Socket connectSocket;
    private IAsyncResult connectResult;
    private float connectStart;

    private float snapshotTimer;
    private float positionTimer;
    private CharacterController localController;

    private string message = "";
    private float messageUntil;

    // ---------- Maschinen-Nummern ----------

    private static readonly Dictionary<int, PlacedMachine> machines =
        new Dictionary<int, PlacedMachine>();

    private static int nextId = 1;
    private static List<Transform> sceneRoots;

    // Gibt einer gebauten Maschine ihre Netzwerk-Nummer. id <= 0 = neue vergeben.
    public static void RegisterMachine(GameObject go, int id)
    {
        PlacedMachine pm = go != null ? go.GetComponent<PlacedMachine>() : null;

        if (pm == null)
            return;

        if (id <= 0)
            id = nextId++;
        else
            nextId = Mathf.Max(nextId, id + 1);

        pm.NetId = id;
        machines[id] = pm;
    }

    public static void Unregister(PlacedMachine pm)
    {
        PlacedMachine current;

        if (pm.NetId > 0 && machines.TryGetValue(pm.NetId, out current) && current == pm)
            machines.Remove(pm.NetId);
    }

    public static PlacedMachine FindMachine(int id)
    {
        PlacedMachine pm;

        if (machines.TryGetValue(id, out pm) && pm != null)
            return pm;

        return null;
    }

    // Objekte, die fest in der Szene stehen (nicht gebaut), bekommen negative
    // Nummern, in einer festen Reihenfolge (Name + Ort). Auf allen Rechnern
    // muss dieselbe Szene geladen sein.
    private static void BuildSceneRoots()
    {
        sceneRoots = new List<Transform>();
        HashSet<Transform> set = new HashSet<Transform>();

        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>(true))
        {
            if (!(mb is IInteractable || mb is ISaveable || mb is Port))
                continue;

            if (mb.GetComponentInParent<PlacedMachine>() != null)
                continue;

            if (mb.GetComponentInParent<SavedItem>() != null)
                continue;

            set.Add(mb.transform.root);
        }

        sceneRoots.AddRange(set);
        sceneRoots.Sort((a, b) => string.CompareOrdinal(SceneKey(a), SceneKey(b)));
    }

    private static string SceneKey(Transform t)
    {
        Vector3 p = t.position;

        return t.name + "|" + Mathf.RoundToInt(p.x * 10f) + "|"
            + Mathf.RoundToInt(p.y * 10f) + "|" + Mathf.RoundToInt(p.z * 10f);
    }

    private static int SceneId(Transform root)
    {
        if (sceneRoots == null)
            BuildSceneRoots();

        int i = sceneRoots.IndexOf(root);
        return i >= 0 ? -(i + 1) : 0;
    }

    // Findet die Wurzel zu einer Nummer (Maschine oder Szenenobjekt).
    public static Transform ResolveRoot(int id)
    {
        if (id > 0)
        {
            PlacedMachine pm = FindMachine(id);
            return pm != null ? pm.transform : null;
        }

        if (id < 0)
        {
            if (sceneRoots == null)
                BuildSceneRoots();

            int i = -id - 1;
            return i < sceneRoots.Count ? sceneRoots[i] : null;
        }

        return null;
    }

    private static int RootId(Component c, out Transform root)
    {
        PlacedMachine pm = c.GetComponentInParent<PlacedMachine>();

        if (pm != null)
        {
            root = pm.transform;
            return pm.NetId;
        }

        root = c.transform.root;
        return SceneId(root);
    }

    // ---------- Beschreibungen fuer Kommandos ----------

    public static bool DescribePort(Port port, out int id, out int index)
    {
        id = 0;
        index = -1;

        if (port == null)
            return false;

        Transform root;
        id = RootId(port, out root);

        if (id == 0)
            return false;

        index = Array.IndexOf(root.GetComponentsInChildren<Port>(true), port);
        return index >= 0;
    }

    public static Port ResolvePort(int id, int index)
    {
        Transform root = ResolveRoot(id);

        if (root == null)
            return null;

        Port[] ports = root.GetComponentsInChildren<Port>(true);
        return index >= 0 && index < ports.Length ? ports[index] : null;
    }

    // ---------- Kommandos abschicken ----------

    // Fuehrt ein Kommando aus: im Einzelspiel sofort, als Host sofort und fuer
    // alle, als Mitspieler ueber den Host.
    public static void Submit(NetCommand c)
    {
        if (Instance == null)
        {
            // Sollte nicht vorkommen (Start-Hook legt NetSync an).
            Prepare(c);
            ExecuteStatic(c);
            return;
        }

        Instance.SubmitInternal(c);
    }

    private void SubmitInternal(NetCommand c)
    {
        c.sender = LocalId;

        if (Mode == NetMode.Client)
        {
            if (clientConnection != null && clientConnection.Alive && ready)
                clientConnection.Send(MsgCommand, c.ToBytes());

            return;
        }

        Prepare(c);
        Execute(c);

        if (Mode == NetMode.Host)
            BroadcastCommand(c);
    }

    // Der Host vergibt Nummern fuer neue Maschinen.
    private static void Prepare(NetCommand c)
    {
        if (c.type == NetCommandType.Place && c.netId <= 0)
            c.netId = nextId++;
    }

    // Bequeme Aufrufe fuer Skripte.

    public static void Interact(IInteractable target)
    {
        Component comp = target as Component;
        Transform root = null;
        int id = comp != null ? RootId(comp, out root) : 0;

        if (comp == null || id == 0)
        {
            // Nicht zuzuordnen: nur lokal ausfuehren.
            target.Interact();
            return;
        }

        int index = Array.IndexOf(root.GetComponentsInChildren<IInteractable>(true), target);

        if (index < 0)
        {
            target.Interact();
            return;
        }

        Submit(NetCommand.Interact(id, index));
    }

    public static void AddCoal(Boiler boiler)
    {
        Transform root;
        int id = RootId(boiler, out root);

        if (id == 0)
            boiler.TryAddCoal();
        else
            Submit(NetCommand.AddCoal(id));
    }

    public static void AddFuel(DieselGenerator generator, float liters)
    {
        Transform root;
        int id = RootId(generator, out root);

        if (id == 0)
            generator.AddFuel(liters);
        else
            Submit(NetCommand.AddFuel(id, liters));
    }

    // Wirft Kohle oder Eisenerz in einen Hochofen.
    public static void AddCharge(BlastFurnace furnace, ItemKind kind, string metal)
    {
        Transform root;
        int id = RootId(furnace, out root);

        if (id == 0)
            furnace.TryAdd(kind, metal);
        else
            Submit(NetCommand.AddCharge(id, (int)kind, metal, 0f));
    }

    // Wirft festes Metall (Barren) in eine Giesserei, wo es eingeschmolzen wird.
    public static void AddSolid(Foundry foundry, string metal, float mass)
    {
        Transform root;
        int id = RootId(foundry, out root);

        if (id == 0)
            foundry.AddSolid(metal, mass);
        else
            Submit(NetCommand.AddCharge(id, (int)ItemKind.Ingot, metal, mass));
    }

    // Entfernt ein Rohr/Kabel (z. B. durchgebrannt).
    public static void RemovePipe(Pipe pipe)
    {
        int id, index;

        if (Mode == NetMode.Offline || !DescribePort(pipe.PortA, out id, out index))
        {
            Destroy(pipe.gameObject);
            return;
        }

        Submit(NetCommand.PipeRemove(id, index));
    }

    // ---------- Kommandos ausfuehren (auf allen Rechnern) ----------

    private void Execute(NetCommand c)
    {
        ExecuteStatic(c);
    }

    private static void ExecuteStatic(NetCommand c)
    {
        BuildingSystem building = FindObjectOfType<BuildingSystem>();
        VoxelWorld world = VoxelWorld.Instance;

        switch (c.type)
        {
            case NetCommandType.Place:
            {
                GameObject prefab = building != null ? building.FindPrefab(c.prefab) : null;

                if (prefab == null)
                {
                    Debug.LogWarning("Mehrspieler: Prefab nicht gefunden: " + c.prefab);
                    return;
                }

                GameObject machine = building.SpawnSavedMachine(prefab, c.position, c.rotation);
                RegisterMachine(machine, c.netId);
                ApplyFlatten(c, world);
                break;
            }

            case NetCommandType.Demolish:
            {
                PlacedMachine pm = FindMachine(c.netId);

                if (pm != null && building != null)
                    building.ExecuteDemolish(pm);

                break;
            }

            case NetCommandType.Move:
            {
                PlacedMachine pm = FindMachine(c.netId);

                if (pm != null && building != null)
                {
                    building.ExecuteMove(pm, c.position, c.rotation);
                    ApplyFlatten(c, world);
                }

                break;
            }

            case NetCommandType.PipeAdd:
            {
                Port a = ResolvePort(c.idA, c.portA);
                Port b = ResolvePort(c.idB, c.portB);
                PipeBuilder builder = FindObjectOfType<PipeBuilder>();

                if (a != null && b != null && builder != null)
                    builder.RestorePipe(a, b);

                break;
            }

            case NetCommandType.PipeRemove:
            {
                Port port = ResolvePort(c.idA, c.portA);
                PipeBuilder builder = FindObjectOfType<PipeBuilder>();

                if (port != null && builder != null)
                    builder.RemovePipeAt(port);

                // Falls das Rohr dabei nicht gefunden wurde: trotzdem entfernen.
                if (port != null && port.IsConnected)
                {
                    Pipe cable = port.Cable;
                    port.Disconnect();

                    if (cable != null)
                        Destroy(cable.gameObject);
                }

                break;
            }

            case NetCommandType.Dig:
            {
                if (world == null)
                    return;

                float[] volumes = world.DigSphere(c.position, c.radius);
                world.RebuildDirtyNow();

                // Beute bekommt nur, wer gegraben hat.
                if (c.sender == LocalId && VoxelDigger.Local != null)
                    VoxelDigger.Local.OnDug(volumes, c.position);

                break;
            }

            case NetCommandType.Interact:
            {
                Transform root = ResolveRoot(c.netId);

                if (root == null)
                    return;

                IInteractable[] list = root.GetComponentsInChildren<IInteractable>(true);

                if (c.index >= 0 && c.index < list.Length)
                    list[c.index].Interact();

                break;
            }

            case NetCommandType.AddCoal:
            {
                Transform root = ResolveRoot(c.netId);
                Boiler boiler = root != null ? root.GetComponentInChildren<Boiler>(true) : null;

                if (boiler != null)
                    boiler.TryAddCoal();

                break;
            }

            case NetCommandType.AddFuel:
            {
                Transform root = ResolveRoot(c.netId);
                DieselGenerator generator =
                    root != null ? root.GetComponentInChildren<DieselGenerator>(true) : null;

                if (generator != null)
                    generator.AddFuel(c.amount);

                break;
            }

            case NetCommandType.AddCharge:
            {
                Transform root = ResolveRoot(c.netId);
                ItemKind kind = (ItemKind)c.index;

                // Barren gehen in die Giesserei, Kohle und Erz in den Hochofen.
                if (kind == ItemKind.Ingot)
                {
                    Foundry foundry =
                        root != null ? root.GetComponentInChildren<Foundry>(true) : null;

                    if (foundry != null)
                        foundry.AddSolid(c.prefab, c.amount);
                }
                else
                {
                    BlastFurnace furnace =
                        root != null ? root.GetComponentInChildren<BlastFurnace>(true) : null;

                    if (furnace != null)
                        furnace.TryAdd(kind, c.prefab);
                }

                break;
            }
        }
    }

    private static void ApplyFlatten(NetCommand c, VoxelWorld world)
    {
        if (c.flatten && world != null)
            world.FlattenArea(c.flatCenter, c.flatHalf, c.flatYaw, c.flatBase, c.flatBlend);

        if (c.fillReach > 0f && world != null)
            world.BackfillSlope(c.flatCenter, c.flatHalf, c.flatYaw, c.fillTop, c.fillReach, c.fillSlope);
    }

    // ---------- Start ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindObjectOfType<NetSync>() == null)
            new GameObject("NetSync").AddComponent<NetSync>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        Mode = NetMode.Offline;
        LocalId = 0;
        ready = false;
        connecting = false;

        // Maschinen-Verzeichnis neu (Szene wurde gerade geladen).
        machines.Clear();
        nextId = 1;
        sceneRoots = null;
        names.Clear();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Disconnect(false);
            Instance = null;
        }
    }

    // ---------- Host starten ----------

    public bool StartHost(int port, string playerName, out string error)
    {
        error = null;

        if (Mode != NetMode.Offline)
            return false;

        VoxelWorld world = VoxelWorld.Instance;

        if (world == null || !world.IsReady)
        {
            error = Loc.T("net.error.notready");
            return false;
        }

        try
        {
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Any, port));
            listener.Listen(8);
            listener.Blocking = false;
        }
        catch (Exception e)
        {
            error = Loc.T("net.error.host", e.Message);
            CloseSockets();
            return false;
        }

        PlayerName = playerName;

        Mode = NetMode.Host;
        LocalId = 0;
        hostPort = port;
        ready = true;

        names.Clear();
        names[0] = CleanName(playerName);

        BuildSceneRoots();
        NetItems.OnHostStarted();
        LocalAddresses = FindLocalAddresses();

        ShowMessage(Loc.T("net.status.host", names.Count, port));
        return true;
    }

    private static string FindLocalAddresses()
    {
        try
        {
            StringBuilder sb = new StringBuilder();

            foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                if (sb.Length > 0)
                    sb.Append(", ");

                sb.Append(ip);
            }

            return sb.ToString();
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string CleanName(string name)
    {
        name = (name ?? "").Trim();

        if (name.Length == 0)
            name = "Player";

        return name.Length > 20 ? name.Substring(0, 20) : name;
    }

    // ---------- Beitreten ----------

    public bool Join(string address, int port, string playerName, out string error)
    {
        error = null;

        if (Mode != NetMode.Offline)
            return false;

        VoxelWorld world = VoxelWorld.Instance;

        if (world == null || !world.IsReady)
        {
            error = Loc.T("net.error.notready");
            return false;
        }

        try
        {
            connectSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            connectResult = connectSocket.BeginConnect(address.Trim(), port, null, null);
        }
        catch (Exception e)
        {
            error = Loc.T("net.error.connect", e.Message);
            CloseSockets();
            return false;
        }

        PlayerName = playerName;

        Mode = NetMode.Client;
        remoteAddress = address.Trim();
        connecting = true;
        ready = false;
        connectStart = Time.realtimeSinceStartup;

        names.Clear();
        pendingCommands.Clear();

        return true;
    }

    // ---------- Trennen ----------

    public void Disconnect(bool showMessage = true)
    {
        bool wasOnline = Mode != NetMode.Offline;

        CloseSockets();

        foreach (RemotePlayer avatar in avatars.Values)
        {
            if (avatar != null)
                Destroy(avatar.gameObject);
        }

        avatars.Clear();
        peers.Clear();
        pendingCommands.Clear();
        names.Clear();

        Mode = NetMode.Offline;
        LocalId = 0;
        connecting = false;
        ready = false;

        // Lose Gegenstaende wieder rein lokal.
        NetItems.OnDisconnected();

        if (wasOnline && showMessage)
            ShowMessage(Loc.T("net.status.offline"));
    }

    private void CloseSockets()
    {
        if (listener != null)
        {
            try { listener.Close(); } catch (Exception) { }
            listener = null;
        }

        foreach (Peer peer in peers)
        {
            if (peer.conn != null)
                peer.conn.Close();
        }

        if (clientConnection != null)
        {
            clientConnection.Close();
            clientConnection = null;
        }

        if (connectSocket != null)
        {
            try { connectSocket.Close(); } catch (Exception) { }
            connectSocket = null;
        }

        connectResult = null;
    }

    // ---------- Jedes Bild ----------

    private void Update()
    {
        if (Mode == NetMode.Host)
            UpdateHost();
        else if (Mode == NetMode.Client)
            UpdateClient();
    }

    // ---- Host ----

    private void UpdateHost()
    {
        // Neue Verbindungen annehmen.
        while (listener != null && listener.Poll(0, SelectMode.SelectRead))
        {
            try
            {
                Socket socket = listener.Accept();
                socket.Blocking = false;
                socket.NoDelay = true;

                Peer peer = new Peer();
                peer.conn = new NetConnection(socket);
                peers.Add(peer);
            }
            catch (Exception)
            {
                break;
            }
        }

        // Nachrichten lesen.
        for (int i = peers.Count - 1; i >= 0; i--)
        {
            Peer peer = peers[i];
            peer.conn.Pump();

            byte type;
            byte[] payload;

            while (peer.conn.TryRead(out type, out payload))
                HandleHostMessage(peer, type, payload);

            if (!peer.conn.Alive)
                RemovePeer(peer);
        }

        // Zustand aller Maschinen regelmaessig verteilen.
        snapshotTimer += Time.unscaledDeltaTime;

        if (snapshotTimer >= snapshotInterval)
        {
            snapshotTimer = 0f;

            if (JoinedCount() > 0)
                Broadcast(MsgSnapshot, BuildSnapshot(), null);
        }

        // Eigene Position.
        positionTimer += Time.unscaledDeltaTime;

        if (positionTimer >= 1f / positionRate)
        {
            positionTimer = 0f;

            byte[] state = BuildPlayerState(0);

            if (state != null)
                Broadcast(MsgPlayerState, state, null);
        }
    }

    private int JoinedCount()
    {
        int n = 0;

        foreach (Peer p in peers)
        {
            if (p.joined)
                n++;
        }

        return n;
    }

    private void HandleHostMessage(Peer peer, byte type, byte[] payload)
    {
        try
        {
            if (type == MsgHello && !peer.joined)
            {
                HandleHello(peer, payload);
                return;
            }

            if (!peer.joined)
                return;

            if (type == MsgCommand)
            {
                NetCommand c = NetCommand.FromBytes(payload);
                c.sender = peer.id;

                Prepare(c);
                Execute(c);
                BroadcastCommand(c);
            }
            else if (type == MsgPlayerState)
            {
                ReadPlayerState(payload, peer.id, true);

                // An alle anderen weiterreichen (mit der echten Nummer).
                byte[] relay = RewritePlayerState(payload, peer.id);
                Broadcast(MsgPlayerState, relay, peer);
            }
            else if (NetItems.IsItemMessage(type))
            {
                // Lose Gegenstaende (siehe NetItems.cs).
                NetItems.HandleHost(peer.id, type, payload);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Mehrspieler: Nachricht fehlerhaft: " + e.Message);
        }
    }

    private void HandleHello(Peer peer, byte[] payload)
    {
        int version;
        string name, world;

        using (MemoryStream ms = new MemoryStream(payload))
        using (BinaryReader r = new BinaryReader(ms))
        {
            version = r.ReadInt32();
            name = CleanName(r.ReadString());
            world = r.ReadString();
        }

        VoxelWorld voxel = VoxelWorld.Instance;

        if (version != ProtocolVersion)
        {
            Reject(peer, Loc.T("net.error.version"));
            return;
        }

        if (voxel == null || !voxel.IsReady || world != voxel.WorldSignature)
        {
            Reject(peer, Loc.T("net.error.world"));
            return;
        }

        // Freie Nummer suchen (0 ist der Host).
        int id = -1;

        for (int candidate = 1; candidate < MaxPlayers; candidate++)
        {
            if (!names.ContainsKey(candidate))
            {
                id = candidate;
                break;
            }
        }

        if (id < 0)
        {
            Reject(peer, Loc.T("net.error.full"));
            return;
        }

        peer.id = id;
        peer.name = name;

        // Die ganze Welt als "Spielstand" mitschicken.
        SaveSystem save = SaveSystem.Instance;

        if (save == null)
        {
            Reject(peer, Loc.T("net.error.notready"));
            return;
        }

        string json = JsonUtility.ToJson(save.CaptureData());
        byte[] terrain = save.CaptureTerrainBytes();

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(id);
            w.Write(names[0]);

            w.Write(names.Count);

            foreach (KeyValuePair<int, string> pair in names)
            {
                w.Write(pair.Key);
                w.Write(pair.Value);
            }

            w.Write(json);
            w.Write(terrain.Length);
            w.Write(terrain);
            w.Flush();

            peer.conn.Send(MsgWelcome, ms.ToArray());
        }

        // Den anderen Bescheid sagen und die Figur anlegen.
        names[id] = name;
        peer.joined = true;

        byte[] joined = EncodePlayerEvent(id, name);
        Broadcast(MsgPlayerJoined, joined, peer);

        EnsureAvatar(id, name);
        ShowMessage(Loc.T("net.joined", name));
    }

    private void Reject(Peer peer, string reason)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(reason);
            w.Flush();
            peer.conn.Send(MsgReject, ms.ToArray());
        }

        // Kurz warten lassen, damit die Nachricht noch rausgeht: wird beim
        // naechsten Pump gesendet, danach schliesst sich die Verbindung.
        peer.conn.Pump();
        peer.conn.Close();
    }

    private void RemovePeer(Peer peer)
    {
        peers.Remove(peer);

        if (!peer.joined)
            return;

        names.Remove(peer.id);
        RemoveAvatar(peer.id);

        // Seine Gegenstaende gehen an den Host, getragene fallen herunter.
        NetItems.OnPlayerLeft(peer.id);

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(peer.id);
            w.Flush();
            Broadcast(MsgPlayerLeft, ms.ToArray(), null);
        }

        ShowMessage(Loc.T("net.left", peer.name));
    }

    private void Broadcast(byte type, byte[] payload, Peer except)
    {
        foreach (Peer peer in peers)
        {
            if (peer.joined && peer != except)
                peer.conn.Send(type, payload);
        }
    }

    private void BroadcastCommand(NetCommand c)
    {
        Broadcast(MsgCommand, c.ToBytes(), null);
    }

    // ---- Mitspieler ----

    private void UpdateClient()
    {
        if (connecting)
        {
            UpdateConnecting();
            return;
        }

        if (clientConnection == null)
            return;

        clientConnection.Pump();

        byte type;
        byte[] payload;

        while (clientConnection.TryRead(out type, out payload))
        {
            try
            {
                HandleClientMessage(type, payload);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Mehrspieler: Nachricht fehlerhaft: " + e);
            }
        }

        if (!clientConnection.Alive)
        {
            Disconnect(false);
            ShowMessage(Loc.T("net.error.lost"));
            return;
        }

        if (ready)
        {
            positionTimer += Time.unscaledDeltaTime;

            if (positionTimer >= 1f / positionRate)
            {
                positionTimer = 0f;

                byte[] state = BuildPlayerState(LocalId);

                if (state != null)
                    clientConnection.Send(MsgPlayerState, state);
            }
        }
    }

    private void UpdateConnecting()
    {
        if (connectResult == null || connectSocket == null)
        {
            Disconnect(false);
            return;
        }

        if (!connectResult.IsCompleted)
        {
            if (Time.realtimeSinceStartup - connectStart > 10f)
            {
                Disconnect(false);
                ShowMessage(Loc.T("net.error.connect", "timeout"));
            }

            return;
        }

        try
        {
            connectSocket.EndConnect(connectResult);
            connectSocket.Blocking = false;
            connectSocket.NoDelay = true;
        }
        catch (Exception e)
        {
            Disconnect(false);
            ShowMessage(Loc.T("net.error.connect", e.Message));
            return;
        }

        clientConnection = new NetConnection(connectSocket);
        connectSocket = null;
        connectResult = null;
        connecting = false;

        // Anmelden: Version, Name und Welt (Seed/Groesse) muessen passen.
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(ProtocolVersion);
            w.Write(CleanName(PlayerName));
            w.Write(VoxelWorld.Instance.WorldSignature);
            w.Flush();
            clientConnection.Send(MsgHello, ms.ToArray());
        }
    }

    private void HandleClientMessage(byte type, byte[] payload)
    {
        switch (type)
        {
            case MsgReject:
            {
                string reason;

                using (MemoryStream ms = new MemoryStream(payload))
                using (BinaryReader r = new BinaryReader(ms))
                    reason = r.ReadString();

                Disconnect(false);
                ShowMessage(reason);
                break;
            }

            case MsgWelcome:
                HandleWelcome(payload);
                break;

            case MsgPlayerJoined:
            {
                int id;
                string name;
                DecodePlayerEvent(payload, out id, out name);

                names[id] = name;
                EnsureAvatar(id, name);
                ShowMessage(Loc.T("net.joined", name));
                break;
            }

            case MsgPlayerLeft:
            {
                int id = BitConverter.ToInt32(payload, 0);
                string name;

                if (names.TryGetValue(id, out name))
                    ShowMessage(Loc.T("net.left", name));

                names.Remove(id);
                RemoveAvatar(id);
                break;
            }

            case MsgCommand:
            {
                NetCommand c = NetCommand.FromBytes(payload);

                if (ready)
                    Execute(c);
                else
                    pendingCommands.Add(c);

                break;
            }

            case MsgSnapshot:
                if (ready)
                    ApplySnapshot(payload);

                break;

            case MsgPlayerState:
                ReadPlayerState(payload, -1, false);
                break;

            default:
                // Lose Gegenstaende (siehe NetItems.cs); vor dem Laden werden sie gesammelt.
                if (NetItems.IsItemMessage(type))
                    NetItems.HandleClient(type, payload, ready);

                break;
        }
    }

    private void HandleWelcome(byte[] payload)
    {
        string json;
        byte[] terrain;

        using (MemoryStream ms = new MemoryStream(payload))
        using (BinaryReader r = new BinaryReader(ms))
        {
            LocalId = r.ReadInt32();
            r.ReadString(); // Name des Hosts (steht auch in der Liste)

            int count = r.ReadInt32();
            names.Clear();

            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                names[id] = r.ReadString();
            }

            names[LocalId] = CleanName(PlayerName);

            json = r.ReadString();
            int terrainLength = r.ReadInt32();
            terrain = r.ReadBytes(terrainLength);
        }

        foreach (KeyValuePair<int, string> pair in names)
        {
            if (pair.Key != LocalId)
                EnsureAvatar(pair.Key, pair.Value);
        }

        SaveFileData data = JsonUtility.FromJson<SaveFileData>(json);

        // Feste Szenenobjekte vor dem Laden nummerieren (vor allen Aenderungen).
        BuildSceneRoots();

        StartCoroutine(JoinRoutine(data, terrain));
    }

    private IEnumerator JoinRoutine(SaveFileData data, byte[] terrain)
    {
        bool ok = false;
        bool finished = false;

        // Etwas versetzt zum Host starten, damit man nicht ineinander steht.
        data.playerPosition += Vector3.right * (1.5f * LocalId);

        yield return SaveSystem.Instance.ApplyRoutine(data, terrain, true, success =>
        {
            ok = success;
            finished = true;
        });

        while (!finished)
            yield return null;

        if (Mode != NetMode.Client)
            yield break;

        if (!ok)
        {
            Disconnect(false);
            ShowMessage(Loc.T("net.error.connect", "load"));
            yield break;
        }

        ready = true;

        // Nachrichten zu losen Gegenstaenden, die waehrend des Ladens kamen, verarbeiten.
        NetItems.OnReady();

        // Kommandos, die waehrend des Ladens eingetroffen sind, nachholen.
        foreach (NetCommand c in pendingCommands)
            Execute(c);

        pendingCommands.Clear();
        ShowMessage(Loc.T("net.status.client", remoteAddress));
    }

    // ---------- Zustandsabgleich ----------

    [Serializable]
    public class StateList
    {
        public List<StateBag> bags = new List<StateBag>();
    }

    [Serializable]
    public class SnapshotData
    {
        public float timeOfDay;
        public List<int> ids = new List<int>();
        public List<StateList> states = new List<StateList>();
    }

    private byte[] BuildSnapshot()
    {
        SnapshotData snap = new SnapshotData();

        SkyController sky = SkyController.Instance;

        if (sky != null)
            snap.timeOfDay = sky.TimeOfDay;

        foreach (KeyValuePair<int, PlacedMachine> pair in machines)
        {
            if (pair.Value == null)
                continue;

            AddSnapshotEntry(snap, pair.Key, pair.Value.gameObject);
        }

        if (sceneRoots == null)
            BuildSceneRoots();

        for (int i = 0; i < sceneRoots.Count; i++)
        {
            if (sceneRoots[i] == null)
                continue;

            if (sceneRoots[i].GetComponentInChildren<ISaveable>(true) != null)
                AddSnapshotEntry(snap, -(i + 1), sceneRoots[i].gameObject);
        }

        return Encoding.UTF8.GetBytes(JsonUtility.ToJson(snap));
    }

    private static void AddSnapshotEntry(SnapshotData snap, int id, GameObject go)
    {
        StateList list = new StateList();
        list.bags = SaveSystem.CaptureStates(go);

        if (list.bags.Count == 0)
            return;

        snap.ids.Add(id);
        snap.states.Add(list);
    }

    private void ApplySnapshot(byte[] payload)
    {
        SnapshotData snap = JsonUtility.FromJson<SnapshotData>(Encoding.UTF8.GetString(payload));

        if (snap == null)
            return;

        for (int i = 0; i < snap.ids.Count && i < snap.states.Count; i++)
        {
            Transform root = ResolveRoot(snap.ids[i]);

            if (root != null)
                SaveSystem.ApplyStates(root.gameObject, snap.states[i].bags);
        }

        SkyController sky = SkyController.Instance;

        if (sky != null)
            sky.TimeOfDay = snap.timeOfDay;
    }

    // ---------- Spieler-Figuren ----------

    private byte[] BuildPlayerState(int id)
    {
        VoxelWorld world = VoxelWorld.Instance;
        Transform p = world != null ? world.player : null;

        if (p == null || !p.gameObject.activeInHierarchy)
            return null;

        if (localController == null)
            localController = p.GetComponent<CharacterController>();

        // Die Fuesse: Mitte der Kapsel minus halbe Hoehe.
        Vector3 feet = p.position;

        if (localController != null)
        {
            feet = p.TransformPoint(localController.center)
                - Vector3.up * (localController.height * 0.5f * p.lossyScale.y);
        }

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(id);
            NetIO.Write(w, feet);
            w.Write(p.eulerAngles.y);
            w.Flush();
            return ms.ToArray();
        }
    }

    // Liest eine Position. forcedId >= 0: die Nummer aus der Verbindung gilt
    // (der Host vertraut der Nummer im Paket nicht).
    private void ReadPlayerState(byte[] payload, int forcedId, bool fromPeer)
    {
        int id;
        Vector3 feet;
        float yaw;

        using (MemoryStream ms = new MemoryStream(payload))
        using (BinaryReader r = new BinaryReader(ms))
        {
            id = r.ReadInt32();
            feet = NetIO.ReadVector3(r);
            yaw = r.ReadSingle();
        }

        if (fromPeer)
            id = forcedId;

        if (id == LocalId)
            return;

        string name;

        if (!names.TryGetValue(id, out name))
            return;

        EnsureAvatar(id, name).SetTarget(feet, yaw);
    }

    private static byte[] RewritePlayerState(byte[] payload, int id)
    {
        byte[] copy = (byte[])payload.Clone();
        BitConverter.GetBytes(id).CopyTo(copy, 0);
        return copy;
    }

    private RemotePlayer EnsureAvatar(int id, string name)
    {
        RemotePlayer avatar;

        if (avatars.TryGetValue(id, out avatar) && avatar != null)
            return avatar;

        avatar = RemotePlayer.Create(id, name);
        avatars[id] = avatar;
        return avatar;
    }

    private void RemoveAvatar(int id)
    {
        RemotePlayer avatar;

        if (avatars.TryGetValue(id, out avatar))
        {
            if (avatar != null)
                Destroy(avatar.gameObject);

            avatars.Remove(id);
        }
    }

    private static byte[] EncodePlayerEvent(int id, string name)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(id);
            w.Write(name);
            w.Flush();
            return ms.ToArray();
        }
    }

    private static void DecodePlayerEvent(byte[] payload, out int id, out string name)
    {
        using (MemoryStream ms = new MemoryStream(payload))
        using (BinaryReader r = new BinaryReader(ms))
        {
            id = r.ReadInt32();
            name = r.ReadString();
        }
    }

    // ---------- Anzeige ----------

    public static void Toast(string text)
    {
        if (Instance != null)
            Instance.ShowMessage(text);
    }

    private void ShowMessage(string text)
    {
        message = text;
        messageUntil = Time.unscaledTime + 4f;
    }

    private void OnGUI()
    {
        if (Time.unscaledTime < messageUntil && !string.IsNullOrEmpty(message))
            GameSettings.DrawBoxBelowCenter(message, 130f);
    }
}
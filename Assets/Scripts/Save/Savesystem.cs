using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using UnityEngine;

// Speichern und Laden.
//
// Wird beim Spielstart automatisch angelegt (man muss nichts in die Szene
// legen). Will man die Einstellungen aendern oder Gegenstaende speichern,
// legt man das Skript auf ein Objekt in der Szene.
//
// Was gespeichert wird:
//   - Gelaende: nur die Veraenderungen gegenueber dem Anfangszustand
//     (Graben, Einebnen). Die Welt selbst entsteht wieder aus dem Seed.
//   - Alle gebauten Maschinen samt Zustand (Kessel, Schalter, Batterie ...)
//   - Alle Rohre und Kabel
//   - Lose Gegenstaende mit dem Skript "SavedItem"
//   - Spielerposition, Tageszeit, Wolken, Spielzeit
//
// Plaetze: 0 = Autospeicherung, 1 = Schnellspeicherung (F5 / F9),
//          2 bis 4 = freie Plaetze (im Menue unter "Spielstaende").
//
// Dateien liegen in Application.persistentDataPath/saves
// (Im Editor: Menue Window > ... oder "Debug: Speicherordner zeigen" per
// Rechtsklick auf die Komponente).
public class SaveSystem : MonoBehaviour
{
    public const int SlotCount = 5;
    private const int FileVersion = 1;

    [Header("Tasten")]
    [SerializeField] private KeyCode quickSaveKey = KeyCode.F5;
    [SerializeField] private KeyCode quickLoadKey = KeyCode.F9;

    [Header("Autospeicherung")]
    [Tooltip("Alle so viele Minuten wird automatisch gespeichert. 0 = aus.")]
    [SerializeField] private float autosaveMinutes = 5f;

    [Header("Gegenstaende")]
    [Tooltip("Prefabs aller losen Gegenstaende mit SavedItem (Kohle, Kanister ...).")]
    [SerializeField] private GameObject[] itemPrefabs;

    public static SaveSystem Instance { get; private set; }

    // True, solange ein Spielstand geladen wird.
    public static bool IsLoading { get; private set; }

    // Fortschritt des aktuellen Ladevorgangs (0 bis 1) fuer die Ladeanzeige.
    public static float LoadProgress { get; private set; }

    // Informationen zu einem Platz (fuer die Anzeige im Menue).
    public class SlotInfo
    {
        public bool exists;
        public string time = "";
        public float playSeconds;
        public int machines;
    }

    private static readonly SlotInfo[] slotInfos = new SlotInfo[SlotCount];

    private float playSeconds;
    private float autosaveTimer;
    private string message = "";
    private float messageUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindObjectOfType<SaveSystem>() == null)
            new GameObject("SaveSystem").AddComponent<SaveSystem>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        IsLoading = false;
        RefreshSlots();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ---------- Oeffentliche Bedienung ----------

    public static string SlotName(int slot)
    {
        if (slot == 0) return Loc.T("save.slot.auto");
        if (slot == 1) return Loc.T("save.slot.quick");
        return Loc.T("save.slot.n", slot - 1);
    }

    public static SlotInfo GetSlotInfo(int slot)
    {
        if (slot < 0 || slot >= SlotCount || slotInfos[slot] == null)
            return new SlotInfo();

        return slotInfos[slot];
    }

    // ---------- Loeschen (mit doppelter Bestaetigung) ----------

    private static int deleteSlot = -1;
    private static int deleteStage;      // 1 = erste Rueckfrage, 2 = zweite Rueckfrage
    private static float deleteExpire;   // nach dieser Zeit verfaellt die Rueckfrage

    // Loescht einen Spielstand endgueltig (beide Dateien).
    public static bool Delete(int slot)
    {
        if (IsLoading || slot < 0 || slot >= SlotCount)
            return false;

        try
        {
            string json = FilePath(slot, "json");
            string terrain = FilePath(slot, "terrain");

            if (File.Exists(json)) File.Delete(json);
            if (File.Exists(terrain)) File.Delete(terrain);
        }
        catch (Exception e)
        {
            Debug.LogError("Loeschen fehlgeschlagen: " + e);
            return false;
        }

        RefreshSlots();

        if (Instance != null)
            Instance.ShowMessage(Loc.T("save.deleted", SlotName(slot)));

        return true;
    }

    // Zeichnet den Loeschen-Knopf eines Platzes. Erst nach ZWEI Rueckfragen wird geloescht;
    // jede Rueckfrage verfaellt nach 5 Sekunden. Fuer Menues (SettingsMenu, MainMenu).
    public static void DrawDeleteButton(int slot, bool exists, GUIStyle style)
    {
        bool active = deleteSlot == slot && Time.unscaledTime < deleteExpire;

        if (!active && deleteSlot == slot)
        {
            deleteSlot = -1;
            deleteStage = 0;
        }

        string text = !active ? Loc.T("save.delete")
            : deleteStage == 1 ? Loc.T("save.delete.confirm1")
            : Loc.T("save.delete.confirm2");

        bool oldEnabled = GUI.enabled;
        Color oldBackground = GUI.backgroundColor;

        GUI.enabled = oldEnabled && exists && !IsLoading;

        // Waehrend einer Rueckfrage rot, damit man sie nicht uebersieht.
        if (active)
            GUI.backgroundColor = new Color(1f, 0.35f, 0.3f);

        if (MenuNav.Button(text, style))
        {
            if (!active)
            {
                deleteSlot = slot;
                deleteStage = 1;
                deleteExpire = Time.unscaledTime + 5f;
            }
            else if (deleteStage == 1)
            {
                deleteStage = 2;
                deleteExpire = Time.unscaledTime + 5f;
            }
            else
            {
                deleteSlot = -1;
                deleteStage = 0;
                Delete(slot);
            }
        }

        GUI.backgroundColor = oldBackground;
        GUI.enabled = oldEnabled;
    }

    // Liest die Kopfdaten aller Plaetze neu ein.
    public static void RefreshSlots()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            SlotInfo info = new SlotInfo();
            slotInfos[i] = info;

            string path = FilePath(i, "json");

            if (!File.Exists(path) || !File.Exists(FilePath(i, "terrain")))
                continue;

            try
            {
                SaveFileData data = JsonUtility.FromJson<SaveFileData>(File.ReadAllText(path));

                if (data == null)
                    continue;

                info.exists = true;
                info.time = data.savedAt;
                info.playSeconds = data.playSeconds;
                info.machines = data.machines != null ? data.machines.Count : 0;
            }
            catch (Exception)
            {
                info.exists = false;
            }
        }
    }

    public static string FormatPlayTime(float seconds)
    {
        int total = Mathf.FloorToInt(seconds);
        return (total / 3600) + ":" + ((total / 60) % 60).ToString("00");
    }

    public static bool Save(int slot)
    {
        return Instance != null && Instance.DoSave(slot, false);
    }

    public static void Load(int slot)
    {
        if (Instance != null)
            Instance.StartLoad(slot);
    }

    private void Update()
    {
        playSeconds += Time.unscaledDeltaTime;

        if (IsLoading)
            return;

        if (GameInput.KeyDown(quickSaveKey))
            DoSave(1, false);

        if (GameInput.KeyDown(quickLoadKey))
            StartLoad(1);

        // Autospeicherung
        if (autosaveMinutes > 0.01f)
        {
            autosaveTimer += Time.unscaledDeltaTime;

            if (autosaveTimer >= autosaveMinutes * 60f)
            {
                // Waehrend des Umplatzierens nicht speichern: kurz spaeter nochmal.
                BuildingSystem building = FindObjectOfType<BuildingSystem>();

                if (building != null && building.IsCarrying)
                    autosaveTimer = autosaveMinutes * 60f - 10f;
                else
                {
                    autosaveTimer = 0f;
                    DoSave(0, true);
                }
            }
        }
    }

    // ---------- Pfade ----------

    private static string FilePath(int slot, string extension)
    {
        return Path.Combine(
            Path.Combine(Application.persistentDataPath, "saves"),
            "slot" + slot + "." + extension);
    }

    // Schreibt zuerst in eine Hilfsdatei, damit ein Absturz mitten im
    // Speichern keinen alten Spielstand zerstoert.
    private static void ReplaceFile(string temp, string target)
    {
        if (File.Exists(target))
            File.Delete(target);

        File.Move(temp, target);
    }

    // ---------- Speichern ----------

    private bool DoSave(int slot, bool autosave)
    {
        VoxelWorld world = VoxelWorld.Instance;

        if (IsLoading || world == null || !world.IsReady)
            return false;

        // Im Mehrspieler speichert nur der Host.
        if (NetSync.IsClient)
        {
            if (!autosave)
                ShowMessage(Loc.T("net.error.client"));

            return false;
        }

        try
        {
            // Wird gerade eine Maschine getragen, wieder abstellen.
            BuildingSystem building = FindObjectOfType<BuildingSystem>();

            if (building != null && building.IsCarrying)
                building.CancelPlacement();

            SaveFileData data = Capture(world);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath(slot, "json")));

            string terrainTemp = FilePath(slot, "terrain") + ".tmp";
            string jsonTemp = FilePath(slot, "json") + ".tmp";

            using (FileStream fs = File.Create(terrainTemp))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Compress))
            using (BinaryWriter writer = new BinaryWriter(gz))
            {
                world.WriteChanges(writer);
            }

            File.WriteAllText(jsonTemp, JsonUtility.ToJson(data));

            // Zuerst das Gelaende, dann die Kopfdatei: Nur wenn beides da ist,
            // gilt der Platz als gueltig.
            ReplaceFile(terrainTemp, FilePath(slot, "terrain"));
            ReplaceFile(jsonTemp, FilePath(slot, "json"));
        }
        catch (Exception e)
        {
            Debug.LogError("Speichern fehlgeschlagen: " + e);
            ShowMessage(Loc.T("save.error.save"));
            return false;
        }

        RefreshSlots();

        ShowMessage(Loc.T("save.saved", SlotName(slot)));
        return true;
    }

    // Fuer das Netzwerk: Zustand der Welt als Daten und Gelaende als Bytes.
    public SaveFileData CaptureData()
    {
        return Capture(VoxelWorld.Instance);
    }

    public byte[] CaptureTerrainBytes()
    {
        using (MemoryStream ms = new MemoryStream())
        {
            using (GZipStream gz = new GZipStream(ms, CompressionMode.Compress, true))
            using (BinaryWriter writer = new BinaryWriter(gz))
            {
                VoxelWorld.Instance.WriteChanges(writer);
            }

            return ms.ToArray();
        }
    }

    // Sammelt den Zustand der ganzen Welt ein.
    private SaveFileData Capture(VoxelWorld world)
    {
        SaveFileData data = new SaveFileData();
        data.version = FileVersion;
        data.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        data.playSeconds = playSeconds;
        data.world = world.WorldSignature;

        SkyController sky = SkyController.Instance;

        if (sky != null)
        {
            data.timeOfDay = sky.TimeOfDay;
            data.cloudOffset = sky.CloudOffset;
        }

        if (world.player != null)
        {
            data.playerPosition = world.player.position;
            data.playerYaw = world.player.eulerAngles.y;
        }

        // Maschinen
        PlacedMachine[] machines = FindObjectsOfType<PlacedMachine>();
        Dictionary<PlacedMachine, int> machineIndex = new Dictionary<PlacedMachine, int>();
        Dictionary<PlacedMachine, Port[]> machinePorts = new Dictionary<PlacedMachine, Port[]>();

        foreach (PlacedMachine m in machines)
        {
            MachineRecord rec = new MachineRecord();
            rec.prefab = m.Prefab != null ? m.Prefab.name : CleanName(m.name);
            rec.position = m.transform.position;
            rec.rotation = m.transform.rotation;
            rec.states = CaptureStates(m.gameObject);
            rec.netId = m.NetId;

            machineIndex[m] = data.machines.Count;
            machinePorts[m] = m.GetComponentsInChildren<Port>(true);
            data.machines.Add(rec);
        }

        // Rohre und Kabel: Jeder Anschluss wird ueber Maschine + Port-Nummer beschrieben.
        foreach (Pipe pipe in FindObjectsOfType<Pipe>())
        {
            if (pipe.PortA == null || pipe.PortB == null)
                continue;

            PlacedMachine ma = pipe.PortA.GetComponentInParent<PlacedMachine>();
            PlacedMachine mb = pipe.PortB.GetComponentInParent<PlacedMachine>();

            if (ma == null || mb == null ||
                !machineIndex.ContainsKey(ma) || !machineIndex.ContainsKey(mb))
                continue;

            int pa = Array.IndexOf(machinePorts[ma], pipe.PortA);
            int pb = Array.IndexOf(machinePorts[mb], pipe.PortB);

            if (pa < 0 || pb < 0)
                continue;

            PipeRecord pr = new PipeRecord();
            pr.machineA = machineIndex[ma];
            pr.portA = pa;
            pr.machineB = machineIndex[mb];
            pr.portB = pb;
            data.pipes.Add(pr);
        }

        // Lose Gegenstaende
        foreach (SavedItem item in FindObjectsOfType<SavedItem>())
        {
            ItemRecord rec = new ItemRecord();
            rec.prefab = CleanName(item.name);
            rec.position = item.transform.position;
            rec.rotation = item.transform.rotation;
            rec.states = CaptureStates(item.gameObject);
            rec.netId = NetItems.IdOf(item.gameObject);
            data.items.Add(rec);
        }

        return data;
    }

    private static string CleanName(string name)
    {
        return name.Replace("(Clone)", "").Trim();
    }

    public static List<StateBag> CaptureStates(GameObject go)
    {
        List<StateBag> list = new List<StateBag>();

        foreach (ISaveable saveable in go.GetComponentsInChildren<ISaveable>(true))
        {
            StateBag bag = new StateBag();
            saveable.SaveState(bag);
            list.Add(bag);
        }

        return list;
    }

    // Schalter werden zuerst geladen: Beim Umlegen reagieren andere
    // Komponenten (z. B. Sicherung, Motor), und deren gespeicherter Zustand
    // soll danach den Vorrang haben.
    public static void ApplyStates(GameObject go, List<StateBag> states)
    {
        ISaveable[] comps = go.GetComponentsInChildren<ISaveable>(true);

        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < comps.Length && i < states.Count; i++)
            {
                bool isSwitch = comps[i] is MachineSwitch;

                if ((pass == 0) == isSwitch)
                    comps[i].LoadState(states[i]);
            }
        }
    }

    // ---------- Laden ----------

    private void StartLoad(int slot)
    {
        if (IsLoading)
            return;

        // Waehrend einer Mehrspieler-Sitzung wuerde Laden die anderen aus dem
        // Gleichgewicht bringen.
        if (NetSync.IsOnline)
        {
            ShowMessage(Loc.T("net.error.noload"));
            return;
        }

        StartCoroutine(LoadRoutine(slot));
    }

    private bool TryRead(int slot, out SaveFileData data)
    {
        data = null;

        try
        {
            if (!File.Exists(FilePath(slot, "json")) || !File.Exists(FilePath(slot, "terrain")))
            {
                ShowMessage(Loc.T("save.error.missing"));
                return false;
            }

            data = JsonUtility.FromJson<SaveFileData>(File.ReadAllText(FilePath(slot, "json")));
        }
        catch (Exception e)
        {
            Debug.LogError("Laden fehlgeschlagen: " + e);
            ShowMessage(Loc.T("save.error.load", e.Message));
            return false;
        }

        if (data == null)
        {
            ShowMessage(Loc.T("save.error.load", "?"));
            return false;
        }

        return true;
    }

    private static MonoBehaviour FindController(Transform player)
    {
        if (player == null)
            return null;

        foreach (MonoBehaviour mb in player.GetComponentsInParent<MonoBehaviour>(true))
        {
            if (mb.GetType().Name == "FirstPersonController")
                return mb;
        }

        foreach (MonoBehaviour mb in player.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb.GetType().Name == "FirstPersonController")
                return mb;
        }

        return null;
    }

    private IEnumerator LoadRoutine(int slot)
    {
        VoxelWorld world = VoxelWorld.Instance;

        if (world == null || !world.IsReady)
            yield break;

        SaveFileData data;

        if (!TryRead(slot, out data))
            yield break;

        if (data.world != world.WorldSignature)
        {
            ShowMessage(Loc.T("save.error.world"));
            yield break;
        }

        byte[] terrain;

        try
        {
            terrain = File.ReadAllBytes(FilePath(slot, "terrain"));
        }
        catch (Exception e)
        {
            Debug.LogError("Laden fehlgeschlagen: " + e);
            ShowMessage(Loc.T("save.error.load", e.Message));
            yield break;
        }

        bool ok = false;

        yield return ApplyRoutine(data, terrain, true, success => ok = success);

        if (ok)
            ShowMessage(Loc.T("save.loaded", SlotName(slot)));
    }

    // Stellt eine Welt aus Daten her (Spielstand oder vom Host geschickt).
    // restorePlayer: Spieler an die gespeicherte Position setzen.
    public IEnumerator ApplyRoutine(
        SaveFileData data, byte[] terrain, bool restorePlayer, Action<bool> done)
    {
        VoxelWorld world = VoxelWorld.Instance;

        if (world == null || !world.IsReady)
        {
            if (done != null) done(false);
            yield break;
        }

        IsLoading = true;
        LoadProgress = 0.02f;

        // Spieler anhalten, damit er waehrend des Ladens nicht durch die Welt faellt.
        MonoBehaviour controller = FindController(world.player);

        if (controller != null)
            controller.enabled = false;

        BuildingSystem building = FindObjectOfType<BuildingSystem>();

        if (building != null)
            building.CancelPlacement();

        // 1) Alles Gebaute entfernen.
        foreach (PlacedMachine m in FindObjectsOfType<PlacedMachine>())
            Destroy(m.gameObject);

        foreach (Pipe p in FindObjectsOfType<Pipe>())
            Destroy(p.gameObject);

        foreach (SavedItem item in FindObjectsOfType<SavedItem>())
            Destroy(item.gameObject);

        // Zerstoerte Objekte verschwinden erst am Ende des Bildes.
        yield return null;

        // 2) Gelaende wiederherstellen.
        LoadProgress = 0.1f;
        bool terrainOk = false;

        try
        {
            using (MemoryStream ms = new MemoryStream(terrain))
            using (GZipStream gz = new GZipStream(ms, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(gz))
            {
                terrainOk = world.ReadChanges(reader);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Gelaende laden fehlgeschlagen: " + e);
        }

        if (!terrainOk)
            ShowMessage(Loc.T("save.error.load", "terrain"));

        // 2b) Gelaende rund um Spieler, Maschinen und Gegenstaende vorladen, BEVOR sie
        //     erscheinen - sonst fielen lose Gegenstaende durch den noch fehlenden Boden.
        world.PreloadAround(data.playerPosition, 4f);

        foreach (MachineRecord rec in data.machines)
            world.PreloadAround(rec.position, 2f);

        foreach (ItemRecord rec in data.items)
            world.PreloadAround(rec.position, 1f);

        int preloadStart = Mathf.Max(1, world.PendingChunks);

        while (world.PendingChunks > 0)
        {
            LoadProgress = 0.1f + 0.25f * (1f - (float)world.PendingChunks / preloadStart);
            yield return null;
        }

        // 3) Maschinen neu bauen.
        LoadProgress = 0.35f;
        List<GameObject> built = new List<GameObject>();

        foreach (MachineRecord rec in data.machines)
        {
            GameObject prefab = building != null ? building.FindPrefab(rec.prefab) : null;

            if (prefab == null)
            {
                Debug.LogWarning("Spielstand: Prefab nicht gefunden: " + rec.prefab);
                built.Add(null);
                continue;
            }

            GameObject machine = building.SpawnSavedMachine(prefab, rec.position, rec.rotation);
            NetSync.RegisterMachine(machine, rec.netId);
            built.Add(machine);
        }

        // Einmal warten, damit alle Start()-Methoden gelaufen sind. Sonst
        // wuerden sie die geladenen Werte wieder ueberschreiben.
        yield return null;

        for (int i = 0; i < built.Count; i++)
        {
            if (built[i] != null)
                ApplyStates(built[i], data.machines[i].states);
        }

        // 4) Rohre und Kabel.
        LoadProgress = 0.55f;
        PipeBuilder pipeBuilder = FindObjectOfType<PipeBuilder>();

        if (pipeBuilder != null)
        {
            foreach (PipeRecord pr in data.pipes)
            {
                Port a = FindPort(built, pr.machineA, pr.portA);
                Port b = FindPort(built, pr.machineB, pr.portB);

                if (a != null && b != null)
                    pipeBuilder.RestorePipe(a, b);
            }
        }

        // 5) Lose Gegenstaende.
        LoadProgress = 0.7f;
        foreach (ItemRecord rec in data.items)
        {
            GameObject prefab = FindItemPrefab(rec.prefab);

            if (prefab == null)
            {
                Debug.LogWarning("Spielstand: Gegenstand nicht gefunden (SaveSystem > Item Prefabs): " + rec.prefab);
                continue;
            }

            GameObject item = Instantiate(prefab, rec.position, rec.rotation);
            item.name = prefab.name;
            ApplyStates(item, rec.states);

            // Beitritt im Mehrspieler: die Nummer des Hosts uebernehmen.
            if (NetSync.IsClient)
                NetItems.Adopt(item, rec.netId);
        }

        // 6) Himmel und Zeit.
        LoadProgress = 0.8f;
        SkyController sky = SkyController.Instance;

        if (sky != null)
        {
            sky.TimeOfDay = data.timeOfDay;
            sky.CloudOffset = data.cloudOffset;
        }

        playSeconds = data.playSeconds;
        autosaveTimer = 0f;

        // Ab jetzt halten die Maschinen und Gegenstaende selbst ihr Gelaende geladen.
        world.ClearPreload();

        // 7) Warten, bis das Gelaende fertig berechnet ist, dann den Spieler setzen.
        int startPending = Mathf.Max(1, world.PendingChunks);

        while (world.PendingChunks > 0)
        {
            LoadProgress = 0.85f + 0.15f * (1f - (float)world.PendingChunks / startPending);
            yield return null;
        }

        LoadProgress = 1f;

        if (restorePlayer && world.player != null)
        {
            CharacterController cc = world.player.GetComponent<CharacterController>();

            if (cc != null)
                cc.enabled = false;

            world.player.position = data.playerPosition;
            world.player.rotation = Quaternion.Euler(0f, data.playerYaw, 0f);

            if (cc != null)
                cc.enabled = true;

            Physics.SyncTransforms();
        }

        if (controller != null && !SettingsMenu.IsOpen)
            controller.enabled = true;

        IsLoading = false;

        if (done != null)
            done(terrainOk);
    }

    private static Port FindPort(List<GameObject> built, int machine, int port)
    {
        if (machine < 0 || machine >= built.Count || built[machine] == null)
            return null;

        Port[] ports = built[machine].GetComponentsInChildren<Port>(true);

        return port >= 0 && port < ports.Length ? ports[port] : null;
    }

    public GameObject FindItemPrefab(string name)
    {
        if (itemPrefabs == null)
            return null;

        foreach (GameObject prefab in itemPrefabs)
        {
            if (prefab != null && prefab.name == name)
                return prefab;
        }

        return null;
    }

    // ---------- Anzeige ----------

    private void ShowMessage(string text)
    {
        message = text;
        messageUntil = Time.unscaledTime + 3f;
    }

    private void OnGUI()
    {
        if (IsLoading)
        {
            // Das Hauptmenue zeigt den Balken selbst.
            if (!MainMenu.IsOpen)
            {
                string text = NetSync.IsClient ? NetSync.StatusText : Loc.T("save.loading");

                LoadingScreen.DrawFullscreen(
                    text + "  " + Mathf.RoundToInt(LoadProgress * 100f) + " %",
                    LoadProgress,
                    0.85f);
            }
        }
        else if (Time.unscaledTime < messageUntil)
            GameSettings.DrawBoxBelowCenter(message, 80f);
    }

    [ContextMenu("Debug: Speicherordner zeigen")]
    private void DebugShowFolder()
    {
        Debug.Log("Spielstaende liegen in: " +
            Path.Combine(Application.persistentDataPath, "saves"));
    }
}
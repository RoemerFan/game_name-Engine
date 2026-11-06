using UnityEngine;

// Einfaches Hauptmenue beim Spielstart: Spiel starten, Spiel laden, Mehrspieler,
// Einstellungen, Beenden. Zeichnet sich selbst (kein Canvas noetig).
//
// Einrichten: Das Skript an ein beliebiges Objekt in der Szene haengen (zum Beispiel an
// den Spieler oder ein leeres Objekt "MainMenu").
//
// Fuer Tests: Im Inspector "Show Main Menu" ausschalten (oder das Skript abschalten).
// Dann startet das Spiel wie bisher direkt, ohne Menue.
//
// Waehrend das Menue offen ist, ist die Spielersteuerung aus, die Maus frei, und alle
// anderen Menues und Bedienungen sind gesperrt (SettingsMenu.IsOpen ist dann wahr).
public class MainMenu : MonoBehaviour
{
    [Header("Hauptmenue")]
    [Tooltip("Aus = Es gibt kein Hauptmenue, das Spiel startet direkt (praktisch fuer Tests).")]
    [SerializeField] private bool showMainMenu = true;

    [Tooltip("Titel oben im Menue.")]
    [SerializeField] private string gameTitle = "FACTORY";

    [Tooltip("Farbe (mit Transparenz) ueber der Spielwelt hinter dem Menue.")]
    [SerializeField] private Color backgroundColor = new Color(0.04f, 0.05f, 0.07f, 0.88f);

    [Tooltip("Optional: Hintergrundbild (wird bildschirmfuellend gezeigt).")]
    [SerializeField] private Texture backgroundImage;

    [Tooltip("Optional: Dein Spieler-Steuerungsskript (wird im Menue abgeschaltet). " +
             "Leer = wird automatisch gesucht (FirstPersonController).")]
    [SerializeField] private MonoBehaviour playerController;

    // Wird von anderen Skripten abgefragt (SettingsMenu.IsOpen schliesst es ein).
    public static bool IsOpen { get; private set; }

    private static MainMenu instance;

    // Gibt es ein Hauptmenue in der Szene? (Dann zeigt das Pausenmenue den Knopf dafuer.)
    public static bool Exists
    {
        get { return instance != null; }
    }

    // 0 = Hauptseite, 1 = Spiel laden, 2 = Mehrspieler, 3 = Einstellungen
    private int page;
    private Vector2 saveScroll;

    // Laden: Das Menue schliesst sich, sobald das Laden fertig ist.
    private bool pendingLoad;
    private float loadWait;

    // Mehrspieler-Seite
    private string netName;
    private string joinAddress = "127.0.0.1";
    private string portText = "7777";

    private GUIStyle windowStyle;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle buttonStyle;
    private GUIStyle fieldStyle;
    private float builtForScale = -1f;

    // ---------- Start / Ende ----------

    private void Awake()
    {
        instance = this;
        IsOpen = false;

        if (!showMainMenu)
            return;

        // Das Steuerungsskript wird schon hier abgeschaltet, noch bevor es seine Start()-Methode
        // ausfuehrt (sonst wuerde es die Maus einfangen).
        Open();
    }

    private void OnDisable()
    {
        if (IsOpen)
            Close();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    // Oeffnet das Hauptmenue (z. B. aus einem anderen Skript).
    public static void Show()
    {
        if (instance != null && !IsOpen)
            instance.Open();
    }

    private void FindController()
    {
        if (playerController != null)
            return;

        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
        {
            if (mb.GetType().Name == "FirstPersonController")
            {
                playerController = mb;
                return;
            }
        }
    }

    private void Open()
    {
        IsOpen = true;
        page = 0;
        pendingLoad = false;

        FindController();

        if (playerController != null)
            playerController.enabled = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Close()
    {
        IsOpen = false;
        pendingLoad = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (playerController != null)
            playerController.enabled = true;
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        // Die Maus bleibt frei, und die Steuerung aus (falls jemand sie zwischendurch aendert).
        if (Cursor.lockState != CursorLockMode.None)
            Cursor.lockState = CursorLockMode.None;

        Cursor.visible = true;

        if (playerController != null && playerController.enabled)
            playerController.enabled = false;

        // Nach dem Laden eines Spielstands schliesst sich das Menue.
        if (pendingLoad)
        {
            loadWait += Time.unscaledDeltaTime;

            if (loadWait > 0.5f && !SaveSystem.IsLoading)
                Close();
            else if (loadWait > 120f)
                pendingLoad = false; // Sicherung: nicht ewig warten

            return;
        }

        // Zurueck-Knopf / Esc: von einer Unterseite zur Hauptseite.
        if (page != 0 && (GameInput.Pressed(GameAction.Back) || GameInput.KeyDown(KeyCode.Escape)))
            page = 0;
    }

    // ---------- Darstellung ----------

    private void EnsureStyles()
    {
        float scale = Mathf.Max(1f, GameSettings.TextScale);

        if (windowStyle != null && Mathf.Approximately(builtForScale, scale))
            return;

        builtForScale = scale;

        windowStyle = new GUIStyle(GUI.skin.box);

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = Mathf.RoundToInt(44f * scale);
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.normal.textColor = Color.white;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = Mathf.RoundToInt(18f * scale);
        labelStyle.normal.textColor = Color.white;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = Mathf.RoundToInt(20f * scale);
        buttonStyle.fixedHeight = Mathf.RoundToInt(46f * scale);

        fieldStyle = new GUIStyle(GUI.skin.textField);
        fieldStyle.fontSize = Mathf.RoundToInt(18f * scale);
    }

    private void OnGUI()
    {
        if (!IsOpen)
            return;

        // Ganz oben zeichnen, ueber allen anderen Anzeigen.
        GUI.depth = -1000;

        EnsureStyles();

        float scale = builtForScale;
        Rect full = new Rect(0f, 0f, Screen.width, Screen.height);

        // Hintergrund
        Color oldColor = GUI.color;

        if (backgroundImage != null)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(full, backgroundImage, ScaleMode.ScaleAndCrop);
        }

        GUI.color = backgroundColor;
        GUI.DrawTexture(full, Texture2D.whiteTexture);
        GUI.color = oldColor;

        MenuNav.Begin();

        // Mittlere Spalte, nie breiter als der Bildschirm.
        float width = Mathf.Min(Screen.width - 20f, 480f * scale);
        float height = Screen.height - 40f;

        GUILayout.BeginArea(new Rect((Screen.width - width) * 0.5f, 20f, width, height));

        switch (page)
        {
            case 1: DrawLoad(); break;
            case 2: DrawMultiplayer(); break;
            case 3: DrawSettings(); break;
            default: DrawMain(); break;
        }

        GUILayout.EndArea();
        MenuNav.End();
    }

    private static bool WorldReady()
    {
        VoxelWorld world = VoxelWorld.Instance;

        // Keine VoxelWorld aktiv (z. B. in der Hierarchie abgeschaltet, um schneller zu testen):
        // Dann gibt es nichts zu warten.
        return world == null || world.IsReady;
    }

    // Hauptseite
    private void DrawMain()
    {
        GUILayout.FlexibleSpace();

        GUILayout.Label(gameTitle, titleStyle);
        GUILayout.Space(24f);

        bool ready = WorldReady();

        // Solange die Welt noch entsteht, kann man noch nichts starten.
        GUI.enabled = ready;

        if (MenuNav.Button(Loc.T("mainmenu.start"), buttonStyle))
            Close();

        GUILayout.Space(8f);

        if (MenuNav.Button(Loc.T("mainmenu.load"), buttonStyle))
        {
            page = 1;
            SaveSystem.RefreshSlots();
        }

        GUILayout.Space(8f);

        if (MenuNav.Button(Loc.T("mainmenu.multiplayer"), buttonStyle))
            page = 2;

        GUI.enabled = true;

        GUILayout.Space(8f);

        if (MenuNav.Button(Loc.T("mainmenu.settings"), buttonStyle))
            page = 3;

        GUILayout.Space(8f);

        if (MenuNav.Button(Loc.T("mainmenu.quit"), buttonStyle))
            Quit();

        if (!ready)
        {
            // Ladebalken, solange die Welt noch entsteht.
            GUILayout.Space(16f);

            LoadingScreen.LayoutBar(
                Loc.T("world.loading", Mathf.RoundToInt(VoxelWorld.Instance.Progress * 100f)),
                VoxelWorld.Instance.Progress);
        }

        GUILayout.FlexibleSpace();
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Spielstand laden
    private void DrawLoad()
    {
        GUILayout.Label(Loc.T("mainmenu.load"), titleStyle);
        GUILayout.Space(10f);

        if (pendingLoad)
        {
            GUILayout.FlexibleSpace();

            LoadingScreen.LayoutBar(
                Loc.T("mainmenu.loading") + "  " + Mathf.RoundToInt(SaveSystem.LoadProgress * 100f) + " %",
                SaveSystem.LoadProgress);

            GUILayout.FlexibleSpace();
            return;
        }

        saveScroll = GUILayout.BeginScrollView(saveScroll);

        for (int slot = 0; slot < SaveSystem.SlotCount; slot++)
        {
            SaveSystem.SlotInfo info = SaveSystem.GetSlotInfo(slot);

            GUILayout.Label(SaveSystem.SlotName(slot), labelStyle);

            string text = info.exists
                ? Loc.T("save.info", info.time,
                    SaveSystem.FormatPlayTime(info.playSeconds), info.machines)
                : Loc.T("save.empty");

            GUILayout.Label(text, GameSettings.Box(false));

            GUILayout.BeginHorizontal();

            GUI.enabled = info.exists;

            if (MenuNav.Button(Loc.T("save.button.load"), buttonStyle))
            {
                pendingLoad = true;
                loadWait = 0f;
                SaveSystem.Load(slot);
            }

            GUI.enabled = true;

            // Loeschen: Zweimal bestaetigen.
            SaveSystem.DrawDeleteButton(slot, info.exists, buttonStyle);

            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        GUILayout.EndScrollView();

        GUILayout.FlexibleSpace();

        if (MenuNav.Button(Loc.T("mainmenu.back"), buttonStyle))
            page = 0;
    }

    // Mehrspieler: Hosten oder beitreten
    private void DrawMultiplayer()
    {
        if (netName == null)
            netName = NetSync.PlayerName;

        NetSync net = NetSync.Instance;

        GUILayout.Label(Loc.T("mainmenu.multiplayer"), titleStyle);
        GUILayout.Space(10f);
        GUILayout.Label(NetSync.StatusText, labelStyle);
        GUILayout.Space(8f);

        if (net == null)
        {
            GUILayout.FlexibleSpace();

            if (MenuNav.Button(Loc.T("mainmenu.back"), buttonStyle))
                page = 0;

            return;
        }

        if (!NetSync.IsOnline)
        {
            GUILayout.Label(Loc.T("net.name"), labelStyle);
            netName = GUILayout.TextField(netName, 20, fieldStyle);

            GUILayout.Space(6f);
            GUILayout.Label(Loc.T("net.port"), labelStyle);
            portText = GUILayout.TextField(portText, 5, fieldStyle);

            int port;
            bool portOk = int.TryParse(portText, out port) && port >= 1024 && port <= 65535;

            GUILayout.Space(6f);

            GUI.enabled = portOk;

            if (MenuNav.Button(Loc.T("net.host"), buttonStyle))
            {
                string error;

                if (net.StartHost(port, netName, out error))
                    Close();
                else
                    NetSync.Toast(error);
            }

            GUILayout.Space(10f);
            GUILayout.Label(Loc.T("net.address"), labelStyle);
            joinAddress = GUILayout.TextField(joinAddress, 60, fieldStyle);

            if (MenuNav.Button(Loc.T("net.join"), buttonStyle))
            {
                string error;

                if (net.Join(joinAddress, port, netName, out error))
                    Close();
                else
                    NetSync.Toast(error);
            }

            GUI.enabled = true;

            if (!portOk)
                GUILayout.Label(Loc.T("net.error.port"), labelStyle);
        }
        else
        {
            GUILayout.Label(Loc.T("net.players"), labelStyle);

            foreach (string player in NetSync.PlayerList())
                GUILayout.Label("  " + player, labelStyle);

            GUILayout.FlexibleSpace();

            if (MenuNav.Button(Loc.T("net.disconnect"), buttonStyle))
                net.Disconnect();
        }

        GUILayout.FlexibleSpace();

        GUILayout.Label(Loc.T("net.hint.lan"), GameSettings.Box(false));

        if (MenuNav.Button(Loc.T("mainmenu.back"), buttonStyle))
            page = 0;
    }

    // Einstellungen: Sprache und Textgroesse
    private void DrawSettings()
    {
        GUILayout.Label(Loc.T("mainmenu.settings"), titleStyle);
        GUILayout.Space(14f);

        GUILayout.Label(Loc.T("settings.language"), labelStyle);

        GUILayout.BeginHorizontal();

        foreach (Loc.LanguageInfo info in Loc.Languages)
        {
            bool selected = info.code == GameSettings.Language;

            if (MenuNav.Toggle(selected, info.name, buttonStyle) && !selected)
                GameSettings.Language = info.code;
        }

        GUILayout.EndHorizontal();

        GUILayout.Space(14f);

        GUILayout.Label(
            Loc.T("settings.textsize", Mathf.RoundToInt(GameSettings.TextScale * 100f)),
            labelStyle);

        float newScale = MenuNav.Slider(
            GameSettings.TextScale,
            GameSettings.MinTextScale,
            GameSettings.MaxTextScale,
            0.05f,
            GUILayout.Height(28f));

        GameSettings.TextScale = Mathf.Round(newScale / 0.05f) * 0.05f;

        GUILayout.Space(8f);

        GUILayout.BeginHorizontal();

        if (MenuNav.Button(Loc.T("settings.small"), buttonStyle)) GameSettings.TextScale = 1f;
        if (MenuNav.Button(Loc.T("settings.medium"), buttonStyle)) GameSettings.TextScale = 1.5f;
        if (MenuNav.Button(Loc.T("settings.large"), buttonStyle)) GameSettings.TextScale = 2f;
        if (MenuNav.Button(Loc.T("settings.xlarge"), buttonStyle)) GameSettings.TextScale = 2.75f;

        GUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();

        if (MenuNav.Button(Loc.T("mainmenu.back"), buttonStyle))
            page = 0;
    }
}
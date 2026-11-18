using UnityEngine;

// Einstellungsmenue. Mit Esc oeffnen und schliessen.
// Zeichnet sich selbst (kein Canvas noetig): einfach an ein beliebiges Objekt
// in der Szene haengen, zum Beispiel an den Spieler.
//
// Aktuelle Einstellungen:
//   - Textgroesse (wirkt auf alle Info-Texte im Spiel)
// Weitere Einstellungen lassen sich unten in DrawContent() ergaenzen.
public class SettingsMenu : MonoBehaviour
{
    [Tooltip("Taste zum Oeffnen/Schliessen.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;

    [Tooltip("Zweite Taste. Im Unity-Editor behandelt Esc die Maus selbst " +
             "(gibt sie frei); F10 funktioniert dort zuverlaessig.")]
    [SerializeField] private KeyCode alternativeKey = KeyCode.F10;

    [Tooltip("Optional: Dein Spieler-Steuerungsskript (wird beim Oeffnen abgeschaltet, " +
             "damit sich die Kamera nicht mitdreht). Leer = wird automatisch gesucht " +
             "(FirstPersonController am Spieler).")]
    [SerializeField] private MonoBehaviour playerController;

    // Wird von anderen Skripten abgefragt (z. B. das Bausystem).
    // Wahr, solange dieses Menue ODER das Hauptmenue offen ist (alles andere ist dann gesperrt).
    public static bool IsOpen
    {
        get { return isOpen || MainMenu.IsOpen; }
    }

    private static bool isOpen;

    private GUIStyle windowStyle;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle buttonStyle;
    private GUIStyle sampleStyle;
    private float builtForScale = -1f;

    // 0 = Einstellungen, 1 = Spielstaende (Speichern und Laden), 2 = Mehrspieler
    private int page;
    private Vector2 saveScroll;

    // Mehrspieler-Seite
    private string netName;
    private string joinAddress = "127.0.0.1";
    private string portText = "7777";
    private GUIStyle fieldStyle;

    void Awake()
    {
        // Steuerungsskript automatisch finden, falls nichts eingetragen ist.
        // (Man kann ein Skript aus dem Project-Fenster nicht in so ein Feld ziehen,
        // es muss die Komponente aus der Szene sein.)
        if (playerController == null)
        {
            foreach (MonoBehaviour mb in GetComponentsInParent<MonoBehaviour>())
            {
                if (mb.GetType().Name == "FirstPersonController")
                {
                    playerController = mb;
                    break;
                }
            }
        }
    }

    void Update()
    {
        // Esc / F10 / Options-Knopf oeffnet und schliesst, der Zurueck-Knopf
        // (Kreis) schliesst ebenfalls.
        bool pressed =
            GameInput.Pressed(GameAction.Pause, toggleKey) ||
            GameInput.KeyDown(alternativeKey) ||
            (isOpen && GameInput.Pressed(GameAction.Back));

        if (!pressed)
            return;

        if (isOpen)
        {
            Close();
        }
        // Nicht oeffnen, solange das Baumenue oder das Hauptmenue offen ist (sie benutzen die
        // Maus selbst). Der Mauszustand wird bewusst nicht geprueft: Im Editor gibt Esc die
        // Maus schon frei, bevor dieses Skript laeuft.
        else if (!BuildingSystem.IsMenuOpen && !MainMenu.IsOpen)
        {
            Open();
        }
    }

    void OnDisable()
    {
        // Falls das Skript abgeschaltet wird, nichts blockiert zuruecklassen.
        if (isOpen)
            Close();
    }

    void Open()
    {
        isOpen = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (playerController != null)
            playerController.enabled = false;
    }

    void Close()
    {
        isOpen = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (playerController != null)
            playerController.enabled = true;
    }

    // ---------- Darstellung ----------

    // Stile passend zur Textgroesse neu bauen (dieses Menue soll selbst
    // lesbar sein, auch bei kleiner Einstellung).
    void EnsureStyles()
    {
        float scale = Mathf.Max(1f, GameSettings.TextScale);

        if (windowStyle != null && Mathf.Approximately(builtForScale, scale))
            return;

        builtForScale = scale;

        int pad = Mathf.RoundToInt(16f * scale);

        windowStyle = new GUIStyle(GUI.skin.box);
        windowStyle.padding = new RectOffset(pad, pad, pad, pad);

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = Mathf.RoundToInt(26f * scale);
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.normal.textColor = Color.white;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = Mathf.RoundToInt(18f * scale);
        labelStyle.normal.textColor = Color.white;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = Mathf.RoundToInt(18f * scale);
        buttonStyle.fixedHeight = Mathf.RoundToInt(40f * scale);
    }

    void OnGUI()
    {
        if (!isOpen)
            return;

        EnsureStyles();
        MenuNav.Begin();

        float scale = builtForScale;

        // Fenster in der Mitte; so breit wie noetig, aber nie breiter als der Bildschirm.
        float width = Mathf.Min(Screen.width - 20f, 520f * scale);
        float height = Mathf.Min(Screen.height - 20f, 560f * scale);

        Rect area = new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);

        GUI.Box(area, GUIContent.none, windowStyle);

        GUILayout.BeginArea(
            new Rect(area.x + 16f * scale, area.y + 16f * scale,
                     area.width - 32f * scale, area.height - 32f * scale));

        DrawContent();

        GUILayout.EndArea();
        MenuNav.End();
    }

    // Seite "Mehrspieler": Spiel hosten oder einem Spiel beitreten.
    void DrawMultiplayer()
    {
        if (fieldStyle == null || netName == null)
        {
            fieldStyle = new GUIStyle(GUI.skin.textField);
            fieldStyle.fontSize = Mathf.RoundToInt(18f * builtForScale);
            netName = NetSync.PlayerName;
        }

        fieldStyle.fontSize = Mathf.RoundToInt(18f * builtForScale);

        NetSync net = NetSync.Instance;

        GUILayout.Label(NetSync.StatusText, labelStyle);
        GUILayout.Space(8f);

        if (net == null)
        {
            GUILayout.FlexibleSpace();

            if (MenuNav.Button(Loc.T("settings.close"), buttonStyle))
                Close();

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

            if (NetSync.Mode == NetMode.Host && !string.IsNullOrEmpty(NetSync.LocalAddresses))
            {
                GUILayout.Space(8f);
                GUILayout.Label(Loc.T("net.addresses", NetSync.LocalAddresses), labelStyle);
            }

            GUILayout.FlexibleSpace();

            if (MenuNav.Button(Loc.T("net.disconnect"), buttonStyle))
                net.Disconnect();
        }

        GUILayout.FlexibleSpace();

        GUILayout.Label(Loc.T("net.hint.lan"), GameSettings.Box(false));

        if (MenuNav.Button(Loc.T("settings.close"), buttonStyle))
            Close();
    }

    // Seite "Spielstaende": Speichern und Laden fuer alle Plaetze.
    void DrawSaves()
    {
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

            // Die Autospeicherung wird nur vom Spiel geschrieben.
            GUI.enabled = slot != 0;

            if (MenuNav.Button(Loc.T("save.button.save"), buttonStyle))
                SaveSystem.Save(slot);

            GUI.enabled = info.exists;

            if (MenuNav.Button(Loc.T("save.button.load"), buttonStyle))
            {
                // Zuerst das Menue schliessen (gibt die Steuerung frei), dann laden.
                Close();
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

        if (MenuNav.Button(Loc.T("settings.close"), buttonStyle))
            Close();
    }

    // Inhalt des Menues. Weitere Einstellungen hier unten ergaenzen.
    void DrawContent()
    {
        GUILayout.Label(Loc.T("settings.title"), titleStyle);
        GUILayout.Space(8f);

        // Reiter: Einstellungen / Spielstaende
        GUILayout.BeginHorizontal();

        if (MenuNav.Toggle(page == 0, Loc.T("save.page.settings"), buttonStyle) && page != 0)
            page = 0;

        if (MenuNav.Toggle(page == 1, Loc.T("save.page.saves"), buttonStyle) && page != 1)
        {
            page = 1;
            SaveSystem.RefreshSlots();
        }

        if (MenuNav.Toggle(page == 2, Loc.T("net.page"), buttonStyle) && page != 2)
            page = 2;

        GUILayout.EndHorizontal();
        GUILayout.Space(10f);

        if (page == 1)
        {
            DrawSaves();
            return;
        }

        if (page == 2)
        {
            DrawMultiplayer();
            return;
        }

        // ----- Sprache -----
        GUILayout.Label(Loc.T("settings.language"), labelStyle);

        GUILayout.BeginHorizontal();

        foreach (Loc.LanguageInfo info in Loc.Languages)
        {
            bool selected = info.code == GameSettings.Language;

            // Die gewaehlte Sprache ist als "eingedrueckter" Knopf zu erkennen.
            bool pressed = MenuNav.Toggle(selected, info.name, buttonStyle);

            if (pressed && !selected)
                GameSettings.Language = info.code;
        }

        GUILayout.EndHorizontal();

        GUILayout.Space(14f);

        // ----- Textgroesse -----
        GUILayout.Label(
            Loc.T("settings.textsize", Mathf.RoundToInt(GameSettings.TextScale * 100f)),
            labelStyle);

        float newScale = MenuNav.Slider(
            GameSettings.TextScale,
            GameSettings.MinTextScale,
            GameSettings.MaxTextScale,
            0.05f,
            GUILayout.Height(28f));

        // In 5%-Schritten einrasten
        newScale = Mathf.Round(newScale / 0.05f) * 0.05f;
        GameSettings.TextScale = newScale;

        GUILayout.Space(8f);

        // Schnellauswahl
        GUILayout.BeginHorizontal();

        if (MenuNav.Button(Loc.T("settings.small"), buttonStyle)) GameSettings.TextScale = 1f;
        if (MenuNav.Button(Loc.T("settings.medium"), buttonStyle)) GameSettings.TextScale = 1.5f;
        if (MenuNav.Button(Loc.T("settings.large"), buttonStyle)) GameSettings.TextScale = 2f;
        if (MenuNav.Button(Loc.T("settings.xlarge"), buttonStyle)) GameSettings.TextScale = 2.75f;

        GUILayout.EndHorizontal();

        GUILayout.Space(14f);

        // Vorschau mit der eingestellten Groesse
        GUILayout.Label(Loc.T("settings.preview"), labelStyle);
        GUILayout.Box(Loc.T("settings.preview.text"), GameSettings.Box(false));

        GUILayout.FlexibleSpace();

        // ----- Unten -----
        GUILayout.BeginHorizontal();

        if (MenuNav.Button(Loc.T("settings.reset"), buttonStyle))
            GameSettings.ResetAll();

        // Zurueck zum Hauptmenue (nur, wenn eines in der Szene ist).
        if (MainMenu.Exists && MenuNav.Button(Loc.T("mainmenu.tomenu"), buttonStyle))
        {
            Close();
            MainMenu.Show();
        }

        if (MenuNav.Button(Loc.T("settings.close"), buttonStyle))
            Close();

        GUILayout.EndHorizontal();
    }
}
using System.Collections.Generic;
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

    // 0 = Einstellungen, 1 = Spielstaende (Speichern und Laden), 2 = Mehrspieler, 3 = Tastenbelegung
    private int page;
    private Vector2 keysScroll;
    private GUIStyle smallStyle;
    private GUIStyle headStyle;
    private float contentWidth = 480f;
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
        // Wartet ein Belegungsfeld auf einen Controller-Knopf? Dann nimmt es ihn an, ohne dass der
        // Knopf (z. B. Options oder Kreis) das Menue schliesst.
        bool wasListening = isOpen && listenColumn >= 0;

        if (wasListening && listenColumn == 2 && Time.frameCount > listenFrame)
        {
            PadBtn padButton = GameInput.ListenPad();

            if (padButton != PadBtn.None)
            {
                GameInput.SetPad(listenAction, padButton);
                StopListening();
            }
        }

        if (wasListening)
            return;

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
        listenColumn = -1;

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

        smallStyle = new GUIStyle(GUI.skin.label);
        smallStyle.fontSize = Mathf.RoundToInt(15f * scale);
        smallStyle.normal.textColor = Color.white;
        smallStyle.wordWrap = true;

        cellStyle = new GUIStyle(GUI.skin.button);
        cellStyle.fontSize = Mathf.RoundToInt(15f * scale);
        cellStyle.wordWrap = true;

        conflictStyle = new GUIStyle(cellStyle);
        conflictStyle.normal.textColor = new Color(1f, 0.45f, 0.4f);
        conflictStyle.hover.textColor = new Color(1f, 0.45f, 0.4f);

        headStyle = new GUIStyle(smallStyle);
        headStyle.fontStyle = FontStyle.Bold;
        headStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);

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
        float width = Mathf.Min(Screen.width - 20f, (page == 3 ? 820f : 520f) * scale);
        float height = Mathf.Min(Screen.height - 20f, 560f * scale);

        Rect area = new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);

        GUI.Box(area, GUIContent.none, windowStyle);

        contentWidth = area.width - 32f * scale;

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

    // ---------- Seite "Tastenbelegung" ----------
    //
    // Hier kann man Tasten, Maustasten und Controller-Knoepfe selbst belegen: Feld anklicken, dann
    // die gewuenschte Taste druecken (Esc = abbrechen, Rueck/Entf = frei lassen). Mit dem Controller
    // lassen sich die Controller-Felder bedienen. Das "x" setzt die Zeile auf Standard zurueck.

    // Eine Zeile der Tabelle. Ist "header" gesetzt, ist fn eine Ueberschrift (Loc-Schluessel).
    private struct BindRow
    {
        public string fn;
        public GameAction action;
        public int group;          // Doppelbelegungen werden nur innerhalb einer Gruppe angemahnt
        public bool header;
        public bool info;          // nur Anzeige, nicht aenderbar
        public string infoKey, infoMouse, infoPad;
    }

    private static List<BindRow> bindRows;

    // Wartet gerade ein Feld auf eine Eingabe? (column: 0 Taste, 1 Maus, 2 Controller, sonst -1)
    private GameAction listenAction;
    private int listenColumn = -1;
    private int listenFrame;
    private int blockFrame = -10;

    private GUIStyle cellStyle;
    private GUIStyle conflictStyle;

    private static void AddHeader(List<BindRow> list, string key)
    {
        BindRow r = new BindRow();
        r.fn = key;
        r.header = true;
        list.Add(r);
    }

    private static void AddBind(List<BindRow> list, string fn, GameAction action, int group)
    {
        BindRow r = new BindRow();
        r.fn = fn;
        r.action = action;
        r.group = group;
        list.Add(r);
    }

    private static List<BindRow> BindRows()
    {
        if (bindRows != null)
            return bindRows;

        List<BindRow> l = new List<BindRow>();

        AddHeader(l, "keys.g.move");
        BindRow walk = new BindRow();
        walk.fn = "keys.f.walk";
        walk.info = true;
        walk.infoKey = Loc.T("keys.see");
        walk.infoPad = Loc.T("keys.pad.leftstick");
        l.Add(walk);
        BindRow look = new BindRow();
        look.fn = "keys.f.look";
        look.info = true;
        look.infoMouse = Loc.T("keys.mouse.move");
        look.infoPad = Loc.T("keys.pad.rightstick");
        l.Add(look);
        AddBind(l, "keys.f.forward", GameAction.MoveForward, 0);
        AddBind(l, "keys.f.back", GameAction.MoveBack, 0);
        AddBind(l, "keys.f.left", GameAction.MoveLeft, 0);
        AddBind(l, "keys.f.right", GameAction.MoveRight, 0);
        AddBind(l, "keys.f.jump", GameAction.Jump, 0);

        AddHeader(l, "keys.g.items");
        AddBind(l, "keys.f.interact", GameAction.Interact, 0);
        AddBind(l, "keys.f.pickup", GameAction.Pickup, 0);
        AddBind(l, "keys.f.place", GameAction.Place, 0);
        AddBind(l, "keys.f.release", GameAction.Release, 0);

        AddHeader(l, "keys.g.build");
        AddBind(l, "keys.f.primary", GameAction.Primary, 1);
        AddBind(l, "keys.f.secondary", GameAction.Secondary, 1);
        AddBind(l, "keys.f.buildmenu", GameAction.BuildMenu, 1);
        AddBind(l, "keys.f.rotateleft", GameAction.RotateLeft, 1);
        AddBind(l, "keys.f.rotateright", GameAction.RotateRight, 1);
        AddBind(l, "keys.f.grid", GameAction.GridToggle, 1);
        AddBind(l, "keys.f.gridanchor", GameAction.GridAnchor, 1);
        AddBind(l, "keys.f.backfill", GameAction.Backfill, 1);

        AddHeader(l, "keys.g.pipes");
        AddBind(l, "keys.f.pipemode", GameAction.PipeMode, 1);
        AddBind(l, "keys.f.remove", GameAction.Remove, 1);

        AddHeader(l, "keys.g.track");
        AddBind(l, "keys.f.trackmode", GameAction.TrackTool, 1);
        AddBind(l, "keys.f.parallel", GameAction.TrackParallel, 1);
        AddBind(l, "keys.f.parallelswitch", GameAction.TrackParallelSwitch, 1);
        AddBind(l, "keys.f.turntable", GameAction.TrackTurntable, 1);
        AddBind(l, "keys.f.shaft", GameAction.TrackShaft, 1);
        AddBind(l, "keys.f.locoplace", GameAction.TrackLoco, 1);
        AddBind(l, "keys.f.locotype", GameAction.TrackLocoType, 1);

        AddHeader(l, "keys.g.train");
        AddBind(l, "keys.f.throttleup", GameAction.ThrottleUp, 2);
        AddBind(l, "keys.f.throttledown", GameAction.ThrottleDown, 2);
        AddBind(l, "keys.f.reverser", GameAction.Reverser, 2);
        AddBind(l, "keys.f.brake", GameAction.Brake, 2);
        AddBind(l, "keys.f.horn", GameAction.Horn, 2);

        AddHeader(l, "keys.g.misc");
        AddBind(l, "keys.f.monitor", GameAction.Monitor, 0);
        AddBind(l, "keys.f.pause", GameAction.Pause, 0);
        AddBind(l, "keys.f.quicksave", GameAction.QuickSave, 0);
        AddBind(l, "keys.f.quickload", GameAction.QuickLoad, 0);
        AddBind(l, "keys.f.timeskip", GameAction.TimeSkip, 0);

        bindRows = l;
        return l;
    }

    private void StopListening()
    {
        listenColumn = -1;
        blockFrame = Time.frameCount;
    }

    // Nimmt die naechste Taste / Maustaste fuer das wartende Feld an (Controller: siehe Update).
    private void HandleListen()
    {
        if (listenColumn < 0 || Time.frameCount <= listenFrame)
            return;

        Event e = Event.current;

        if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                StopListening();
                e.Use();
                return;
            }

            bool clear = e.keyCode == KeyCode.Backspace || e.keyCode == KeyCode.Delete;

            if (listenColumn == 0)
            {
                GameInput.SetKey(listenAction, clear ? KeyCode.None : e.keyCode);
                StopListening();
                e.Use();
            }
            else if (clear)
            {
                if (listenColumn == 1)
                    GameInput.SetMouse(listenAction, -1);
                else
                    GameInput.SetPad(listenAction, PadBtn.None);

                StopListening();
                e.Use();
            }

            return;
        }

        if (listenColumn == 1 && e.type == EventType.MouseDown && e.button >= 0 && e.button <= 2)
        {
            GameInput.SetMouse(listenAction, e.button);
            StopListening();
            e.Use();
        }
    }

    // Gibt es in derselben Gruppe noch eine andere Funktion mit derselben Taste / Maustaste?
    private static bool HasConflict(BindRow r, int column)
    {
        foreach (BindRow o in BindRows())
        {
            if (o.header || o.info || o.action == r.action || o.group != r.group)
                continue;

            if (column == 0 && GameInput.KeyOf(r.action) != KeyCode.None &&
                GameInput.KeyOf(o.action) == GameInput.KeyOf(r.action))
                return true;

            if (column == 1 && GameInput.MouseOf(r.action) >= 0 &&
                GameInput.MouseOf(o.action) == GameInput.MouseOf(r.action))
                return true;
        }

        return false;
    }

    // Ein anklickbares Feld. Gibt true zurueck, wenn es angeklickt wurde.
    private bool BindCell(string text, float width, bool listeningHere, bool changed, bool conflict, bool nav)
    {
        string label = listeningHere
            ? Loc.T("keys.listen")
            : (string.IsNullOrEmpty(text) ? "-" : text) + (changed ? " *" : "");

        GUIStyle style = conflict ? conflictStyle : cellStyle;

        bool clicked = nav
            ? MenuNav.Button(label, style, GUILayout.Width(width))
            : GUILayout.Button(label, style, GUILayout.Width(width));

        return clicked && Time.frameCount > blockFrame;
    }

    private void Cell(string text, float width, GUIStyle style)
    {
        GUILayout.Label(string.IsNullOrEmpty(text) ? "-" : text, style, GUILayout.Width(width));
    }

    // Tabelle: links die Funktion, daneben Tastatur, Maus und Controller.
    void DrawKeys()
    {
        HandleListen();

        float inner = Mathf.Max(200f, contentWidth - 30f * builtForScale);
        float resetWidth = inner * 0.07f;
        float fnWidth = inner * 0.30f;
        float colWidth = (inner - fnWidth - resetWidth) / 3f;

        // Kopfzeile (bleibt stehen).
        GUILayout.BeginHorizontal();
        Cell(Loc.T("keys.col.function"), fnWidth, headStyle);
        Cell(Loc.T("keys.col.keyboard"), colWidth, headStyle);
        Cell(Loc.T("keys.col.mouse"), colWidth, headStyle);
        Cell(Loc.T("keys.col.pad"), colWidth, headStyle);
        GUILayout.EndHorizontal();

        // Mit dem Controller: rechter Stick scrollt (Maus: Mausrad).
        if (Event.current.type == EventType.Layout)
            keysScroll.y -= GameInput.RightStickY() * 900f * Time.unscaledDeltaTime;

        keysScroll = GUILayout.BeginScrollView(keysScroll);

        foreach (BindRow row in BindRows())
        {
            if (row.header)
            {
                GUILayout.Space(6f);
                GUILayout.Label(Loc.T(row.fn), headStyle);
                continue;
            }

            GUILayout.BeginHorizontal();
            Cell(Loc.T(row.fn), fnWidth, smallStyle);

            if (row.info)
            {
                Cell(row.infoKey, colWidth, smallStyle);
                Cell(row.infoMouse, colWidth, smallStyle);
                Cell(row.infoPad, colWidth, smallStyle);
                GUILayout.Space(resetWidth);
                GUILayout.EndHorizontal();
                continue;
            }

            GameAction a = row.action;

            // Taste
            bool here = listenColumn == 0 && listenAction == a;

            if (BindCell(GameInput.KeyName(GameInput.KeyOf(a)), colWidth, here,
                         GameInput.IsKeyChanged(a), HasConflict(row, 0), false))
            {
                if (here) StopListening(); else { listenAction = a; listenColumn = 0; listenFrame = Time.frameCount; }
            }

            // Maus
            here = listenColumn == 1 && listenAction == a;

            if (BindCell(GameInput.MouseName(GameInput.MouseOf(a)), colWidth, here,
                         GameInput.IsMouseChanged(a), HasConflict(row, 1), false))
            {
                if (here) StopListening(); else { listenAction = a; listenColumn = 1; listenFrame = Time.frameCount; }
            }

            // Controller (mit Controller-Auswahl bedienbar)
            here = listenColumn == 2 && listenAction == a;

            if (BindCell(GameInput.PadLabel(a), colWidth, here, GameInput.IsPadChanged(a), false, true))
            {
                if (here) StopListening(); else { listenAction = a; listenColumn = 2; listenFrame = Time.frameCount; }
            }

            // Zeile auf Standard zuruecksetzen
            if (GameInput.IsChanged(a))
            {
                if (GUILayout.Button("×", cellStyle, GUILayout.Width(resetWidth)))
                    GameInput.ResetAction(a);
            }
            else
            {
                GUILayout.Space(resetWidth);
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6f);
        GUILayout.Label(Loc.T("keys.note"), smallStyle);

        GUILayout.EndScrollView();

        GUILayout.Space(6f);

        if (MenuNav.Button(Loc.T("keys.reset"), buttonStyle))
        {
            GameInput.ResetAllBindings();
            StopListening();
        }

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

        if (MenuNav.Toggle(page == 3, Loc.T("keys.page"), buttonStyle) && page != 3)
            page = 3;

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

        if (page == 3)
        {
            DrawKeys();
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
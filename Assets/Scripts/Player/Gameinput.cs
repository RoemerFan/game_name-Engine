using System;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

// Aktionen des Spiels. Jede Aktion hat eine Taste (oder Maustaste) und
// einen Knopf am Controller.
//
//   Aktion        Tastatur/Maus      PlayStation        Xbox
//   Interact      F (einstellbar)    Quadrat            X
//   Pickup        E                  L2                 LT
//   Place         R                  R1                 RB      (Gegenstand sanft ablegen)
//   Release       Q                  L1                 LB      (an Ort und Stelle loslassen, ohne Schwung)
//   Primary       Linksklick         R2                 RT
//   Secondary     Rechtsklick        Kreis              B
//   RotateLeft    Q                  L1                 LB
//   RotateRight   E                  R1                 RB
//   BuildMenu     B                  Dreieck            Y
//   GridToggle    G                  Steuerkreuz links  Steuerkreuz links
//   GridAnchor    T                  Steuerkreuz rechts Steuerkreuz rechts
//   PipeMode      L                  Steuerkreuz oben   Steuerkreuz oben
//   Remove        X                  Steuerkreuz unten  Steuerkreuz unten
//   Pause         Esc / F10          Options            Menu
//   Monitor       F3                 Create/Share       View
//   Confirm       Enter              Kreuz              A      (nur in Menues)
//   Back          -                  Kreis              B      (nur in Menues)
//
// Bewegung, Kamera, Springen und Sprinten bleiben beim Bewegungsskript.
public enum GameAction
{
    None,
    Interact,
    Pickup,
    Place,
    Release,
    Primary,
    Secondary,
    RotateLeft,
    RotateRight,
    BuildMenu,
    GridToggle,
    GridAnchor,
    PipeMode,
    Remove,             // Entfernen: Rohr, Gleis, Lok (je nach aktivem Werkzeug)
    // Gleis-Werkzeug (nur aktiv, solange es eingeschaltet ist)
    TrackParallel,
    TrackParallelSwitch,
    TrackLoco,
    TrackLocoType,
    TrackTurntable,
    TrackShaft,
    // Lok fahren (nur aktiv, solange man auf der Lok steht)
    ThrottleUp,
    ThrottleDown,
    Reverser,
    Brake,
    Horn,
    // Weitere (frueher feste Tasten)
    TrackTool,
    Backfill,
    QuickSave,
    QuickLoad,
    TimeSkip,
    Jump,
    MoveForward,
    MoveBack,
    MoveLeft,
    MoveRight,
    Pause,
    Monitor,
    Confirm,
    Back
}

// Knoepfe des Controllers (fuer die Belegung in den Einstellungen).
public enum PadBtn
{
    None,
    South, East, West, North,                 // Kreuz/A, Kreis/B, Quadrat/X, Dreieck/Y
    LeftShoulder, RightShoulder,              // L1/LB, R1/RB
    LeftTrigger, RightTrigger,                // L2/LT, R2/RT
    DpadUp, DpadDown, DpadLeft, DpadRight,
    LeftStick, RightStick,                    // Stick druecken (L3/R3)
    Start, Select
}

// Einheitliche Eingabe fuer Tastatur, Maus und Controller.
//
// Benutzung:  GameInput.Pressed(GameAction.Interact, interactKey)
// ist wahr, wenn die Taste ODER der Controller-Knopf gedrueckt wurde.
//
// Mit dem neuen Input System (Package "Input System") funktioniert auch der
// Controller. Ohne dieses Package bleibt es bei Tastatur und Maus.
public static class GameInput
{
    // Wurde zuletzt der Controller benutzt? (Bestimmt die Anzeige der Hinweise.)
    public static bool UsingGamepad { get; internal set; }

    // ---------- Belegung (vom Spieler in den Einstellungen aenderbar) ----------
    //
    // Es werden nur Abweichungen von der Standardbelegung gespeichert (PlayerPrefs "keybindings").
    // Jede Aktion hat bis zu drei Belegungen: Taste, Maustaste und Controller-Knopf. Skripte fragen weiter
    // mit Pressed/Held(aktion, standardTaste) ab; die Taste des Spielers hat Vorrang.

    private static readonly Dictionary<GameAction, KeyCode> keyOverride = new Dictionary<GameAction, KeyCode>();
    private static readonly Dictionary<GameAction, PadBtn> padOverride = new Dictionary<GameAction, PadBtn>();
    private static readonly Dictionary<GameAction, int> mouseOverride = new Dictionary<GameAction, int>();
    private static bool bindingsLoaded;

    private const string PrefsKey = "keybindings";

    private static void EnsureLoaded()
    {
        if (bindingsLoaded)
            return;

        bindingsLoaded = true;

        try
        {
            string text = PlayerPrefs.GetString(PrefsKey, "");

            foreach (string entry in text.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = entry.Split(',');
                int a;

                if (f.Length < 4 || !int.TryParse(f[0], out a))
                    continue;

                GameAction action = (GameAction)a;
                int v;

                if (int.TryParse(f[1], out v)) keyOverride[action] = (KeyCode)v;
                if (int.TryParse(f[2], out v)) padOverride[action] = (PadBtn)v;
                if (int.TryParse(f[3], out v)) mouseOverride[action] = v;
            }
        }
        catch (Exception)
        {
            keyOverride.Clear();
            padOverride.Clear();
            mouseOverride.Clear();
        }
    }

    private static void SaveBindings()
    {
        HashSet<GameAction> all = new HashSet<GameAction>(keyOverride.Keys);
        all.UnionWith(padOverride.Keys);
        all.UnionWith(mouseOverride.Keys);

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        foreach (GameAction a in all)
        {
            KeyCode k; PadBtn p; int m;

            sb.Append((int)a).Append(',')
              .Append(keyOverride.TryGetValue(a, out k) ? ((int)k).ToString() : "x").Append(',')
              .Append(padOverride.TryGetValue(a, out p) ? ((int)p).ToString() : "x").Append(',')
              .Append(mouseOverride.TryGetValue(a, out m) ? m.ToString() : "x").Append(';');
        }

        PlayerPrefs.SetString(PrefsKey, sb.ToString());
        PlayerPrefs.Save();
    }

    // Die Taste, die ein Skript abfragen soll: die des Spielers, sonst die vom Skript (Inspector).
    public static KeyCode EffectiveKey(GameAction action, KeyCode scriptKey)
    {
        if (action == GameAction.None)
            return scriptKey;

        EnsureLoaded();

        KeyCode k;
        return keyOverride.TryGetValue(action, out k) ? k : scriptKey;
    }

    // Standardbelegungen (fuer die Anzeige und zum Zuruecksetzen).
    public static KeyCode DefaultKey(GameAction a)
    {
        switch (a)
        {
            case GameAction.Interact: return KeyCode.F;
            case GameAction.Pickup: return KeyCode.E;
            case GameAction.Place: return KeyCode.R;
            case GameAction.Release: return KeyCode.Q;
            case GameAction.RotateLeft: return KeyCode.Q;
            case GameAction.RotateRight: return KeyCode.E;
            case GameAction.BuildMenu: return KeyCode.B;
            case GameAction.GridToggle: return KeyCode.G;
            case GameAction.GridAnchor: return KeyCode.T;
            case GameAction.PipeMode: return KeyCode.L;
            case GameAction.Remove: return KeyCode.X;
            case GameAction.TrackTool: return KeyCode.K;
            case GameAction.TrackParallel: return KeyCode.P;
            case GameAction.TrackParallelSwitch: return KeyCode.O;
            case GameAction.TrackLoco: return KeyCode.J;
            case GameAction.TrackLocoType: return KeyCode.N;
            case GameAction.TrackTurntable: return KeyCode.U;
            case GameAction.TrackShaft: return KeyCode.M;
            case GameAction.ThrottleUp: return KeyCode.R;
            case GameAction.ThrottleDown: return KeyCode.F;
            case GameAction.Reverser: return KeyCode.V;
            case GameAction.Brake: return KeyCode.B;
            case GameAction.Horn: return KeyCode.H;
            case GameAction.Backfill: return KeyCode.H;
            case GameAction.Monitor: return KeyCode.F3;
            case GameAction.Pause: return KeyCode.Escape;
            case GameAction.QuickSave: return KeyCode.F5;
            case GameAction.QuickLoad: return KeyCode.F9;
            case GameAction.TimeSkip: return KeyCode.Y;
            case GameAction.Jump: return KeyCode.Space;
            case GameAction.MoveForward: return KeyCode.W;
            case GameAction.MoveBack: return KeyCode.S;
            case GameAction.MoveLeft: return KeyCode.A;
            case GameAction.MoveRight: return KeyCode.D;
        }

        return KeyCode.None;
    }

    public static PadBtn DefaultPad(GameAction a)
    {
        switch (a)
        {
            case GameAction.Interact: return PadBtn.West;
            case GameAction.Pickup: return PadBtn.LeftTrigger;
            case GameAction.Place: return PadBtn.RightShoulder;
            case GameAction.Release: return PadBtn.LeftShoulder;
            case GameAction.Primary: return PadBtn.RightTrigger;
            case GameAction.Secondary: return PadBtn.East;
            case GameAction.RotateLeft: return PadBtn.LeftShoulder;
            case GameAction.RotateRight: return PadBtn.RightShoulder;
            case GameAction.BuildMenu: return PadBtn.North;
            case GameAction.GridToggle: return PadBtn.DpadLeft;
            case GameAction.GridAnchor: return PadBtn.DpadRight;
            case GameAction.PipeMode: return PadBtn.DpadUp;
            case GameAction.Remove: return PadBtn.DpadDown;
            case GameAction.Pause: return PadBtn.Start;
            case GameAction.Monitor: return PadBtn.Select;
            case GameAction.Confirm: return PadBtn.South;
            case GameAction.Back: return PadBtn.East;
            case GameAction.Jump: return PadBtn.South;

            // Gleis-Werkzeug (gilt nur, solange es eingeschaltet ist)
            case GameAction.TrackParallel: return PadBtn.DpadUp;
            case GameAction.TrackParallelSwitch: return PadBtn.LeftStick;
            case GameAction.TrackLoco: return PadBtn.RightShoulder;
            case GameAction.TrackLocoType: return PadBtn.LeftShoulder;
            case GameAction.TrackTurntable: return PadBtn.RightStick;

            // Lok fahren (gilt nur auf der Lok)
            case GameAction.ThrottleUp: return PadBtn.RightTrigger;
            case GameAction.ThrottleDown: return PadBtn.LeftTrigger;
            case GameAction.Reverser: return PadBtn.DpadUp;
            case GameAction.Brake: return PadBtn.East;
            case GameAction.Horn: return PadBtn.DpadDown;
        }

        return PadBtn.None;
    }

    public static int DefaultMouse(GameAction a)
    {
        if (a == GameAction.Primary) return 0;
        if (a == GameAction.Secondary) return 1;
        return -1;
    }

    // Aktuelle Belegung (Spieler oder Standard).
    public static KeyCode KeyOf(GameAction a)
    {
        EnsureLoaded();

        KeyCode k;
        return keyOverride.TryGetValue(a, out k) ? k : DefaultKey(a);
    }

    public static PadBtn PadOf(GameAction a)
    {
        EnsureLoaded();

        PadBtn b;
        return padOverride.TryGetValue(a, out b) ? b : DefaultPad(a);
    }

    // -1 = keine Maustaste.
    public static int MouseOf(GameAction a)
    {
        if (a == GameAction.None)
            return -1;

        EnsureLoaded();

        int m;
        return mouseOverride.TryGetValue(a, out m) ? m : DefaultMouse(a);
    }

    // Hat der Spieler an dieser Aktion etwas geaendert?
    public static bool IsChanged(GameAction a)
    {
        EnsureLoaded();
        return keyOverride.ContainsKey(a) || padOverride.ContainsKey(a) || mouseOverride.ContainsKey(a);
    }

    public static bool IsKeyChanged(GameAction a) { EnsureLoaded(); return keyOverride.ContainsKey(a); }
    public static bool IsMouseChanged(GameAction a) { EnsureLoaded(); return mouseOverride.ContainsKey(a); }
    public static bool IsPadChanged(GameAction a) { EnsureLoaded(); return padOverride.ContainsKey(a); }

    // KeyCode.None = bewusst nicht belegt.
    public static void SetKey(GameAction a, KeyCode key)
    {
        EnsureLoaded();

        if (key == DefaultKey(a)) keyOverride.Remove(a); else keyOverride[a] = key;
        SaveBindings();
    }

    public static void SetPad(GameAction a, PadBtn b)
    {
        EnsureLoaded();

        if (b == DefaultPad(a)) padOverride.Remove(a); else padOverride[a] = b;
        SaveBindings();
    }

    // button: 0 links, 1 rechts, 2 Mitte, -1 keine.
    public static void SetMouse(GameAction a, int button)
    {
        EnsureLoaded();

        if (button == DefaultMouse(a)) mouseOverride.Remove(a); else mouseOverride[a] = button;
        SaveBindings();
    }

    public static void ResetAction(GameAction a)
    {
        EnsureLoaded();

        keyOverride.Remove(a);
        padOverride.Remove(a);
        mouseOverride.Remove(a);
        SaveBindings();
    }

    public static void ResetAllBindings()
    {
        EnsureLoaded();

        keyOverride.Clear();
        padOverride.Clear();
        mouseOverride.Clear();
        SaveBindings();
    }

    // Lesbarer Name einer Taste.
    public static string KeyName(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.None: return null;
            case KeyCode.Escape: return "Esc";
            case KeyCode.Return: return "Enter";
            case KeyCode.Space: return Loc.T("keys.key.space");
            case KeyCode.UpArrow: return "↑";
            case KeyCode.DownArrow: return "↓";
            case KeyCode.LeftArrow: return "←";
            case KeyCode.RightArrow: return "→";
            case KeyCode.LeftShift: return "Shift L";
            case KeyCode.RightShift: return "Shift R";
            case KeyCode.LeftControl: return "Strg L";
            case KeyCode.RightControl: return "Strg R";
            case KeyCode.LeftAlt: return "Alt L";
            case KeyCode.RightAlt: return "Alt R";
        }

        string name = k.ToString();
        return name.StartsWith("Alpha") ? name.Substring(5) : name;
    }

    public static string MouseName(int button)
    {
        if (button == 0) return Loc.T("input.mouse.left");
        if (button == 1) return Loc.T("input.mouse.right");
        if (button == 2) return Loc.T("input.mouse.middle");
        return null;
    }

    // Name eines Controller-Knopfes (PlayStation oder Xbox), null = nicht belegt.
    public static string PadBtnName(PadBtn b, bool ps)
    {
        switch (b)
        {
            case PadBtn.South: return ps ? Loc.T("pad.cross") : "A";
            case PadBtn.East: return ps ? Loc.T("pad.circle") : "B";
            case PadBtn.West: return ps ? Loc.T("pad.square") : "X";
            case PadBtn.North: return ps ? Loc.T("pad.triangle") : "Y";
            case PadBtn.LeftShoulder: return ps ? "L1" : "LB";
            case PadBtn.RightShoulder: return ps ? "R1" : "RB";
            case PadBtn.LeftTrigger: return ps ? "L2" : "LT";
            case PadBtn.RightTrigger: return ps ? "R2" : "RT";
            case PadBtn.DpadUp: return Loc.T("pad.dpad.up");
            case PadBtn.DpadDown: return Loc.T("pad.dpad.down");
            case PadBtn.DpadLeft: return Loc.T("pad.dpad.left");
            case PadBtn.DpadRight: return Loc.T("pad.dpad.right");
            case PadBtn.LeftStick: return ps ? "L3" : "LS";
            case PadBtn.RightStick: return ps ? "R3" : "RS";
            case PadBtn.Start: return ps ? "Options" : "Menu";
            case PadBtn.Select: return ps ? "Create" : "View";
        }

        return null;
    }

    // ---------- Abfragen ----------

    // Gerade in diesem Bild gedrueckt.
    public static bool Pressed(GameAction action, KeyCode key = KeyCode.None)
    {
        return KeyDown(EffectiveKey(action, key)) || MouseDown(action) || PadDown(action);
    }

    // Wird gerade gehalten.
    public static bool Held(GameAction action, KeyCode key = KeyCode.None)
    {
        return KeyHeld(EffectiveKey(action, key)) || MouseHeld(action) || PadHeld(action);
    }

    // ---------- Tastatur ----------

    public static bool KeyDown(KeyCode key)
    {
        if (key == KeyCode.None)
            return false;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        Key mapped;

        if (keyboard == null || !TryMapKey(key, out mapped))
            return false;

        return keyboard[mapped].wasPressedThisFrame;
#else
        return Input.GetKeyDown(key);
#endif
    }

    public static bool KeyHeld(KeyCode key)
    {
        if (key == KeyCode.None)
            return false;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        Key mapped;

        if (keyboard == null || !TryMapKey(key, out mapped))
            return false;

        return keyboard[mapped].isPressed;
#else
        return Input.GetKey(key);
#endif
    }

    // ---------- Maus ----------

    private static bool MouseDown(GameAction action)
    {
        int b = MouseOf(action);

        if (b < 0)
            return false;

#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;

        if (mouse == null)
            return false;

        ButtonControl c = b == 0 ? mouse.leftButton : (b == 1 ? mouse.rightButton : mouse.middleButton);
        return c.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(b);
#endif
    }

    private static bool MouseHeld(GameAction action)
    {
        int b = MouseOf(action);

        if (b < 0)
            return false;

#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;

        if (mouse == null)
            return false;

        ButtonControl c = b == 0 ? mouse.leftButton : (b == 1 ? mouse.rightButton : mouse.middleButton);
        return c.isPressed;
#else
        return Input.GetMouseButton(b);
#endif
    }

    // ---------- Controller ----------

    private static bool PadDown(GameAction action)
    {
#if ENABLE_INPUT_SYSTEM
        ButtonControl button = PadButton(action);
        return button != null && button.wasPressedThisFrame;
#else
        return false;
#endif
    }

    private static bool PadHeld(GameAction action)
    {
#if ENABLE_INPUT_SYSTEM
        ButtonControl button = PadButton(action);
        return button != null && button.isPressed;
#else
        return false;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static ButtonControl PadButton(GameAction action)
    {
        Gamepad pad = Gamepad.current;

        if (pad == null)
            return null;

        return PadControl(pad, PadOf(action));
    }

    private static ButtonControl PadControl(Gamepad pad, PadBtn b)
    {
        switch (b)
        {
            case PadBtn.South: return pad.buttonSouth;
            case PadBtn.East: return pad.buttonEast;
            case PadBtn.West: return pad.buttonWest;
            case PadBtn.North: return pad.buttonNorth;
            case PadBtn.LeftShoulder: return pad.leftShoulder;
            case PadBtn.RightShoulder: return pad.rightShoulder;
            case PadBtn.LeftTrigger: return pad.leftTrigger;
            case PadBtn.RightTrigger: return pad.rightTrigger;
            case PadBtn.DpadUp: return pad.dpad.up;
            case PadBtn.DpadDown: return pad.dpad.down;
            case PadBtn.DpadLeft: return pad.dpad.left;
            case PadBtn.DpadRight: return pad.dpad.right;
            case PadBtn.LeftStick: return pad.leftStickButton;
            case PadBtn.RightStick: return pad.rightStickButton;
            case PadBtn.Start: return pad.startButton;
            case PadBtn.Select: return pad.selectButton;
        }

        return null;
    }

    // Fuer die Belegung: welcher Knopf wurde in diesem Bild neu gedrueckt? (None = keiner)
    public static PadBtn ListenPad()
    {
        Gamepad pad = Gamepad.current;

        if (pad == null)
            return PadBtn.None;

        foreach (PadBtn b in System.Enum.GetValues(typeof(PadBtn)))
        {
            if (b == PadBtn.None)
                continue;

            ButtonControl c = PadControl(pad, b);

            if (c != null && c.wasPressedThisFrame)
                return b;
        }

        return PadBtn.None;
    }

    // Tastennamen des alten KeyCode im neuen Input System finden.
    private static readonly Dictionary<KeyCode, Key> keyMap = new Dictionary<KeyCode, Key>();

    private static bool TryMapKey(KeyCode code, out Key key)
    {
        if (keyMap.TryGetValue(code, out key))
            return key != Key.None;

        string name = code.ToString();

        // Die Namen weichen bei ein paar Tasten ab.
        if (name.StartsWith("Alpha")) name = "Digit" + name.Substring(5);
        else if (code == KeyCode.Return) name = "Enter";
        else if (code == KeyCode.LeftControl) name = "LeftCtrl";
        else if (code == KeyCode.RightControl) name = "RightCtrl";
        else if (code == KeyCode.Backspace) name = "Backspace";

        Key result;

        if (!Enum.TryParse(name, true, out result))
            result = Key.None;

        keyMap[code] = result;
        key = result;

        return result != Key.None;
    }

    // Ist der Controller gerade ein PlayStation-Controller?
    public static bool IsPlayStationPad
    {
        get { return Gamepad.current is UnityEngine.InputSystem.DualShock.DualShockGamepad; }
    }
#else
    public static bool IsPlayStationPad { get { return false; } }

    public static PadBtn ListenPad() { return PadBtn.None; }
#endif

    // ---------- Klettern ----------

    // Hoch/runter-Eingabe von -1 bis 1: W/S, Pfeiltasten oder linker Stick.
    public static float VerticalAxis()
    {
        float v = 0f;

        if (Held(GameAction.MoveForward, KeyCode.W) || KeyHeld(KeyCode.UpArrow)) v += 1f;
        if (Held(GameAction.MoveBack, KeyCode.S) || KeyHeld(KeyCode.DownArrow)) v -= 1f;

#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;

        if (pad != null)
        {
            float y = pad.leftStick.ReadValue().y;

            if (Mathf.Abs(y) > 0.2f)
                v += y;
        }
#endif

        return Mathf.Clamp(v, -1f, 1f);
    }

    // ---------- Menuesteuerung (Stick / Steuerkreuz) ----------

    // Richtung fuer die Menues: -1, 0 oder 1. Rohwert, ohne Wiederholung.
    public static Vector2Int MenuDirection()
    {
        int x = 0;
        int y = 0;

        if (KeyHeld(KeyCode.LeftArrow)) x -= 1;
        if (KeyHeld(KeyCode.RightArrow)) x += 1;
        if (KeyHeld(KeyCode.UpArrow)) y += 1;
        if (KeyHeld(KeyCode.DownArrow)) y -= 1;

#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;

        if (pad != null)
        {
            Vector2 stick = pad.leftStick.ReadValue();

            if (pad.dpad.left.isPressed || stick.x < -0.6f) x -= 1;
            if (pad.dpad.right.isPressed || stick.x > 0.6f) x += 1;
            if (pad.dpad.up.isPressed || stick.y > 0.6f) y += 1;
            if (pad.dpad.down.isPressed || stick.y < -0.6f) y -= 1;
        }
#endif

        return new Vector2Int(Mathf.Clamp(x, -1, 1), Mathf.Clamp(y, -1, 1));
    }

    // ---------- Hinweise (welche Taste soll angezeigt werden?) ----------

    // Name der Taste fuer Hinweistexte, je nach zuletzt benutztem Geraet.
    public static string Hint(GameAction action, KeyCode key = KeyCode.None)
    {
        if (UsingGamepad)
        {
            string pad = PadName(action);

            if (!string.IsNullOrEmpty(pad))
                return pad;
        }

        KeyCode effective = EffectiveKey(action, key);

        if (effective != KeyCode.None)
            return KeyName(effective);

        int mouse = MouseOf(action);

        if (mouse >= 0)
            return MouseName(mouse);

        switch (action)
        {
            case GameAction.Primary: return Loc.T("input.mouse.left");
            case GameAction.Secondary: return Loc.T("input.mouse.right");
            case GameAction.RotateLeft: return "Q";
            case GameAction.RotateRight: return "E";
            case GameAction.Interact: return "F";
            case GameAction.Pickup: return "E";
            case GameAction.Place: return "R";
            case GameAction.Release: return "Q";
            case GameAction.BuildMenu: return "B";
        }

        return action.ToString();
    }

    // Name des Controller-Knopfs fuer die Tabelle in den Einstellungen: Ist ein Controller
    // angeschlossen, nur dessen Namen, sonst beide ("Quadrat / X").
    public static string PadLabel(GameAction action)
    {
        bool connected = false;

#if ENABLE_INPUT_SYSTEM
        connected = Gamepad.current != null;
#endif

        if (connected)
            return PadName(action, IsPlayStationPad);

        string a = PadName(action, true);
        string b = PadName(action, false);

        if (string.IsNullOrEmpty(a))
            return null;

        return a == b ? a : a + " / " + b;
    }

    // Name eines Knopfes ohne Aktion (nur fuer die Tabelle): "south" = unterer Knopf (Kreuz / A).
    public static string PadLabelRaw(string which)
    {
        if (which == "south")
        {
            bool connected = false;
#if ENABLE_INPUT_SYSTEM
            connected = Gamepad.current != null;
#endif
            if (connected)
                return IsPlayStationPad ? Loc.T("pad.cross") : "A";

            return Loc.T("pad.cross") + " / A";
        }

        return null;
    }

    // Rechter Stick, senkrecht (-1..1): zum Scrollen von Listen mit dem Controller.
    public static float RightStickY()
    {
#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;

        if (pad != null)
        {
            float y = pad.rightStick.ReadValue().y;
            return Mathf.Abs(y) > 0.2f ? y : 0f;
        }
#endif
        return 0f;
    }

    private static string PadName(GameAction action)
    {
        return PadName(action, IsPlayStationPad);
    }

    private static string PadName(GameAction action, bool ps)
    {
        return PadBtnName(PadOf(action), ps);
    }

    // Ersetzt Platzhalter wie <primary> in Texten durch die passende Taste.
    public static string ReplaceTokens(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
            return text;

        text = text.Replace("<primary>", Hint(GameAction.Primary));
        text = text.Replace("<secondary>", Hint(GameAction.Secondary));
        text = text.Replace("<rotl>", Hint(GameAction.RotateLeft));
        text = text.Replace("<rotr>", Hint(GameAction.RotateRight));

        return text;
    }
}

// Merkt sich, ob zuletzt Tastatur/Maus oder Controller benutzt wurde.
// Legt sich beim Spielstart selbst an.
public class GameInputTracker : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindObjectOfType<GameInputTracker>() == null)
            new GameObject("GameInputTracker").AddComponent<GameInputTracker>();
    }

#if ENABLE_INPUT_SYSTEM
    private void Update()
    {
        Gamepad pad = Gamepad.current;

        if (pad != null && PadActive(pad))
            GameInput.UsingGamepad = true;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        bool keyboardUsed = keyboard != null && keyboard.anyKey.wasPressedThisFrame;

        bool mouseUsed = mouse != null &&
            (mouse.delta.ReadValue().sqrMagnitude > 4f ||
             mouse.leftButton.wasPressedThisFrame ||
             mouse.rightButton.wasPressedThisFrame);

        if (keyboardUsed || mouseUsed)
            GameInput.UsingGamepad = false;
    }

    private static bool PadActive(Gamepad pad)
    {
        if (pad.leftStick.ReadValue().sqrMagnitude > 0.25f) return true;
        if (pad.rightStick.ReadValue().sqrMagnitude > 0.25f) return true;

        return pad.buttonSouth.wasPressedThisFrame
            || pad.buttonEast.wasPressedThisFrame
            || pad.buttonWest.wasPressedThisFrame
            || pad.buttonNorth.wasPressedThisFrame
            || pad.leftShoulder.wasPressedThisFrame
            || pad.rightShoulder.wasPressedThisFrame
            || pad.leftTrigger.wasPressedThisFrame
            || pad.rightTrigger.wasPressedThisFrame
            || pad.startButton.wasPressedThisFrame
            || pad.selectButton.wasPressedThisFrame
            || pad.dpad.up.wasPressedThisFrame
            || pad.dpad.down.wasPressedThisFrame
            || pad.dpad.left.wasPressedThisFrame
            || pad.dpad.right.wasPressedThisFrame;
    }
#endif
}
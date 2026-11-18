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
//   PipeRemove    X                  Steuerkreuz unten  Steuerkreuz unten
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
    PipeRemove,
    Pause,
    Monitor,
    Confirm,
    Back
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

    // ---------- Abfragen ----------

    // Gerade in diesem Bild gedrueckt.
    public static bool Pressed(GameAction action, KeyCode key = KeyCode.None)
    {
        return KeyDown(key) || MouseDown(action) || PadDown(action);
    }

    // Wird gerade gehalten.
    public static bool Held(GameAction action, KeyCode key = KeyCode.None)
    {
        return KeyHeld(key) || MouseHeld(action) || PadHeld(action);
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
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;

        if (mouse == null)
            return false;

        if (action == GameAction.Primary) return mouse.leftButton.wasPressedThisFrame;
        if (action == GameAction.Secondary) return mouse.rightButton.wasPressedThisFrame;
        return false;
#else
        if (action == GameAction.Primary) return Input.GetMouseButtonDown(0);
        if (action == GameAction.Secondary) return Input.GetMouseButtonDown(1);
        return false;
#endif
    }

    private static bool MouseHeld(GameAction action)
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;

        if (mouse == null)
            return false;

        if (action == GameAction.Primary) return mouse.leftButton.isPressed;
        if (action == GameAction.Secondary) return mouse.rightButton.isPressed;
        return false;
#else
        if (action == GameAction.Primary) return Input.GetMouseButton(0);
        if (action == GameAction.Secondary) return Input.GetMouseButton(1);
        return false;
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

        switch (action)
        {
            case GameAction.Interact: return pad.buttonWest;
            case GameAction.Pickup: return pad.leftTrigger;
            case GameAction.Place: return pad.rightShoulder;
            case GameAction.Release: return pad.leftShoulder;
            case GameAction.Primary: return pad.rightTrigger;
            case GameAction.Secondary: return pad.buttonEast;
            case GameAction.RotateLeft: return pad.leftShoulder;
            case GameAction.RotateRight: return pad.rightShoulder;
            case GameAction.BuildMenu: return pad.buttonNorth;
            case GameAction.GridToggle: return pad.dpad.left;
            case GameAction.GridAnchor: return pad.dpad.right;
            case GameAction.PipeMode: return pad.dpad.up;
            case GameAction.PipeRemove: return pad.dpad.down;
            case GameAction.Pause: return pad.startButton;
            case GameAction.Monitor: return pad.selectButton;
            case GameAction.Confirm: return pad.buttonSouth;
            case GameAction.Back: return pad.buttonEast;
        }

        return null;
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
#endif

    // ---------- Klettern ----------

    // Hoch/runter-Eingabe von -1 bis 1: W/S, Pfeiltasten oder linker Stick.
    public static float VerticalAxis()
    {
        float v = 0f;

        if (KeyHeld(KeyCode.W) || KeyHeld(KeyCode.UpArrow)) v += 1f;
        if (KeyHeld(KeyCode.S) || KeyHeld(KeyCode.DownArrow)) v -= 1f;

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

        if (key != KeyCode.None)
            return key.ToString();

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

    private static string PadName(GameAction action)
    {
        bool ps = IsPlayStationPad;

        switch (action)
        {
            case GameAction.Interact: return ps ? Loc.T("pad.square") : "X";
            case GameAction.Pickup: return ps ? "L2" : "LT";
            case GameAction.Place: return ps ? "R1" : "RB";
            case GameAction.Release: return ps ? "L1" : "LB";
            case GameAction.Primary: return ps ? "R2" : "RT";
            case GameAction.Secondary: return ps ? Loc.T("pad.circle") : "B";
            case GameAction.RotateLeft: return ps ? "L1" : "LB";
            case GameAction.RotateRight: return ps ? "R1" : "RB";
            case GameAction.BuildMenu: return ps ? Loc.T("pad.triangle") : "Y";
            case GameAction.GridToggle: return Loc.T("pad.dpad.left");
            case GameAction.GridAnchor: return Loc.T("pad.dpad.right");
            case GameAction.PipeMode: return Loc.T("pad.dpad.up");
            case GameAction.PipeRemove: return Loc.T("pad.dpad.down");
            case GameAction.Pause: return ps ? "Options" : "Menu";
            case GameAction.Monitor: return ps ? "Create" : "View";
            case GameAction.Confirm: return ps ? Loc.T("pad.cross") : "A";
            case GameAction.Back: return ps ? Loc.T("pad.circle") : "B";
        }

        return null;
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
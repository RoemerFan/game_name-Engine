using UnityEngine;

// Menuesteuerung mit Controller (und Pfeiltasten) fuer die IMGUI-Menues
// (Einstellungen, Spielstaende) und das Baumenue.
//
// Es gibt eine Auswahl ("Fokus"), die mit Steuerkreuz oder linkem Stick
// bewegt wird. Kreuz (PlayStation) bzw. A (Xbox) oder Enter bestaetigt.
// Mit der Maus funktioniert alles weiter wie bisher.
//
// IMGUI-Menues benutzen statt GUILayout.Button/Toggle/HorizontalSlider
// einfach MenuNav.Button/Toggle/Slider und rufen am Anfang von OnGUI
// MenuNav.Begin() auf.
[DefaultExecutionOrder(-100)]
public class MenuNav : MonoBehaviour
{
    // Eingaben dieses Bildes (mit Wiederholung bei gedrueckt halten).
    public static int MoveX { get; private set; }
    public static int MoveY { get; private set; }
    public static bool Confirm { get; private set; }

    private const int MaxWidgets = 128;

    private static int lastActiveFrame = -10;
    private static int focus;
    private static int counter;
    private static int widgetCount;
    private static readonly bool[] enabledFlags = new bool[MaxWidgets];
    private static readonly bool[] sliderFlags = new bool[MaxWidgets];

    private static bool confirmUsed;
    private static bool moveUsed;
    private static int scrollRequest = -1;

    private static Texture2D line;

    // Wiederholung beim Gedrueckthalten.
    private Vector2Int lastDirection;
    private float repeatTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindObjectOfType<MenuNav>() == null)
            new GameObject("MenuNav").AddComponent<MenuNav>();
    }

    private static bool IsActive
    {
        get { return Time.frameCount - lastActiveFrame <= 1; }
    }

    private void Update()
    {
        confirmUsed = false;
        moveUsed = false;

        Vector2Int dir = GameInput.MenuDirection();

        int x = 0;
        int y = 0;

        if (dir != Vector2Int.zero)
        {
            if (dir != lastDirection)
            {
                // Neue Richtung: sofort ein Schritt, dann Pause bis zur Wiederholung.
                x = dir.x;
                y = dir.y;
                repeatTimer = 0.4f;
            }
            else
            {
                repeatTimer -= Time.unscaledDeltaTime;

                if (repeatTimer <= 0f)
                {
                    x = dir.x;
                    y = dir.y;
                    repeatTimer = 0.12f;
                }
            }
        }

        lastDirection = dir;

        MoveX = x;
        MoveY = y;
        Confirm = GameInput.Pressed(GameAction.Confirm, KeyCode.Return);

        if (!IsActive)
        {
            // Menue zu: beim naechsten Oeffnen wieder oben anfangen.
            focus = 0;
            return;
        }

        // Fokus bewegen. Auf einem Schieberegler verstellen links/rechts den
        // Wert, nur hoch/runter bewegt dann den Fokus.
        bool sliderFocused = focus < widgetCount && sliderFlags[focus];

        int step = y != 0 ? -y : (sliderFocused ? 0 : x);

        if (step != 0)
            MoveFocus(step);
    }

    // Eine Position weiter (+1) oder zurueck (-1), uebersprungen werden
    // ausgegraute Elemente.
    private static void MoveFocus(int step)
    {
        if (widgetCount <= 0)
            return;

        int next = focus;

        for (int i = 0; i < widgetCount; i++)
        {
            next = Mathf.Clamp(next + step, 0, widgetCount - 1);

            if (enabledFlags[next])
            {
                focus = next;
                scrollRequest = next;
                return;
            }

            if (next == 0 || next == widgetCount - 1)
                break;
        }
    }

    // ---------- Fuer IMGUI-Menues ----------

    // Am Anfang von OnGUI aufrufen.
    public static void Begin()
    {
        lastActiveFrame = Time.frameCount;

        // Jeder OnGUI-Durchlauf zaehlt die Elemente von vorn. Die Anzahl wird
        // am Ende des Zeichnens (Repaint) in End() gemerkt.
        counter = 0;
    }

    // Am Ende von OnGUI aufrufen.
    public static void End()
    {
        if (Event.current != null && Event.current.type == EventType.Repaint)
        {
            widgetCount = Mathf.Min(counter, MaxWidgets);
            focus = Mathf.Clamp(focus, 0, Mathf.Max(0, widgetCount - 1));
        }
    }

    private static int Register(bool slider)
    {
        int index = counter++;

        if (index < MaxWidgets)
        {
            enabledFlags[index] = GUI.enabled;
            sliderFlags[index] = slider;
        }

        return index;
    }

    private static bool IsFocused(int index)
    {
        return GameInput.UsingGamepad && IsActive && index == focus;
    }

    // Rahmen um das ausgewaehlte Element.
    private static void Decorate(int index)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return;

        if (!IsFocused(index))
            return;

        Rect r = GUILayoutUtility.GetLastRect();

        if (line == null)
        {
            line = Texture2D.whiteTexture;
        }

        Color old = GUI.color;
        GUI.color = new Color(1f, 0.85f, 0.2f, 1f);

        float t = 3f;
        GUI.DrawTexture(new Rect(r.x - t, r.y - t, r.width + 2 * t, t), line);
        GUI.DrawTexture(new Rect(r.x - t, r.yMax, r.width + 2 * t, t), line);
        GUI.DrawTexture(new Rect(r.x - t, r.y, t, r.height), line);
        GUI.DrawTexture(new Rect(r.xMax, r.y, t, r.height), line);

        GUI.color = old;

        // Bei langen Listen den ausgewaehlten Eintrag ins Bild scrollen.
        if (scrollRequest == index)
        {
            GUI.ScrollTo(r);
            scrollRequest = -1;
        }
    }

    // Bestaetigen: nur im Zeichen-Durchlauf (Repaint) und nur einmal pro Bild.
    private static bool Fire(int index)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return false;

        if (!Confirm || confirmUsed || !GUI.enabled || index != focus || !IsActive)
            return false;

        confirmUsed = true;
        return true;
    }

    public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options)
    {
        int index = Register(false);
        bool clicked = GUILayout.Button(text, style, options);

        Decorate(index);

        if (!clicked && Fire(index))
            clicked = true;

        return clicked;
    }

    // Wie GUILayout.Toggle: gibt den neuen Wert zurueck.
    public static bool Toggle(bool value, string text, GUIStyle style, params GUILayoutOption[] options)
    {
        int index = Register(false);
        bool result = GUILayout.Toggle(value, text, style, options);

        Decorate(index);

        if (Fire(index))
            result = !value;

        return result;
    }

    // Wie GUILayout.HorizontalSlider; links/rechts am Controller verstellt
    // den Wert um "step".
    public static float Slider(
        float value, float min, float max, float step, params GUILayoutOption[] options)
    {
        int index = Register(true);
        float result = GUILayout.HorizontalSlider(value, min, max, options);

        Decorate(index);

        if (IsFocused(index) &&
            Event.current != null && Event.current.type == EventType.Repaint &&
            MoveX != 0 && !moveUsed)
        {
            moveUsed = true;
            result = Mathf.Clamp(value + MoveX * step, min, max);
        }

        return result;
    }
}
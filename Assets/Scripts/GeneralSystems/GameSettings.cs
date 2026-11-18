using System;
using UnityEngine;

// Zentrale Spiel-Einstellungen (gespeichert, bleiben nach dem Neustart erhalten)
// plus Hilfsfunktionen, mit denen alle Info-Texte im Spiel gezeichnet werden.
// Alle Skripte, die Texte anzeigen, benutzen diese Funktionen. Dadurch wirkt
// die Einstellung "Textgroesse" ueberall gleichzeitig.
public static class GameSettings
{
    // ---------- Textgroesse ----------

    public const float MinTextScale = 0.75f;
    public const float MaxTextScale = 3f;
    public const float DefaultTextScale = 1.5f;

    const string TextScaleKey = "Settings.TextScale";

    static float textScale = -1f;

    // Wird ausgeloest, wenn eine Einstellung geaendert wurde.
    public static event Action Changed;

    // Faktor fuer alle Texte: 1 = Normalgroesse, 2 = doppelt so gross.
    public static float TextScale
    {
        get
        {
            if (textScale < 0f)
                textScale = Mathf.Clamp(
                    PlayerPrefs.GetFloat(TextScaleKey, DefaultTextScale),
                    MinTextScale, MaxTextScale);

            return textScale;
        }
        set
        {
            value = Mathf.Clamp(value, MinTextScale, MaxTextScale);

            if (Mathf.Approximately(value, textScale))
                return;

            textScale = value;
            PlayerPrefs.SetFloat(TextScaleKey, value);

            if (Changed != null)
                Changed();
        }
    }

    // ---------- Sprache ----------

    const string LanguageKey = "Settings.Language";

    static string language;

    // Code der gewaehlten Sprache, z. B. "de" oder "en" (siehe Loc.cs).
    public static string Language
    {
        get
        {
            if (language == null)
            {
                string stored = PlayerPrefs.GetString(LanguageKey, "");

                if (string.IsNullOrEmpty(stored))
                {
                    // Erster Start: Sprache des Computers, sonst Deutsch.
                    stored = Application.systemLanguage == SystemLanguage.English
                        ? "en" : "de";
                }

                language = stored;
            }

            return language;
        }
        set
        {
            if (value == language)
                return;

            language = value;
            PlayerPrefs.SetString(LanguageKey, value);

            if (Changed != null)
                Changed();
        }
    }

    public static void ResetAll()
    {
        TextScale = DefaultTextScale;
        Language = "de";
    }

    // Schriftgroesse fuer eine Basisgroesse (z. B. 15) mit Einstellung.
    public static int FontSize(int baseSize)
    {
        return Mathf.Max(8, Mathf.RoundToInt(baseSize * TextScale));
    }

    // ---------- Stile (werden bei Aenderung automatisch neu erstellt) ----------
    // Hinweis: Muss innerhalb von OnGUI aufgerufen werden.

    static GUIStyle leftBox;
    static GUIStyle centerBox;
    static float stylesBuiltForScale = -1f;

    static void EnsureStyles()
    {
        if (leftBox != null &&
            Mathf.Approximately(stylesBuiltForScale, TextScale))
            return;

        stylesBuiltForScale = TextScale;

        leftBox = MakeBox(TextAnchor.MiddleLeft);
        centerBox = MakeBox(TextAnchor.MiddleCenter);
    }

    static GUIStyle MakeBox(TextAnchor alignment)
    {
        GUIStyle style = new GUIStyle(GUI.skin.box);

        style.fontSize = FontSize(15);
        style.alignment = alignment;
        style.wordWrap = false;
        style.richText = false;
        style.normal.textColor = Color.white;

        // Innenabstand waechst mit der Schrift.
        int px = Mathf.RoundToInt(10f * TextScale);
        int py = Mathf.RoundToInt(6f * TextScale);
        style.padding = new RectOffset(px, px, py, py);

        return style;
    }

    // Stil fuer einfache Texte in Menues (z. B. Ueberschriften): wird von
    // anderen Skripten bei Bedarf benutzt.
    public static GUIStyle Box(bool centered)
    {
        EnsureStyles();
        return centered ? centerBox : leftBox;
    }

    // ---------- Zeichnen ----------

    // Zeichnet eine Textbox so gross, wie der Text es braucht, und haelt
    // sie innerhalb des Bildschirms.
    static void DrawBox(string text, GUIStyle style, float anchorX, float anchorY,
                        float pivotX, float pivotY)
    {
        Vector2 size = style.CalcSize(new GUIContent(text));

        float x = anchorX - size.x * pivotX;
        float y = anchorY - size.y * pivotY;

        x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - size.x));
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - size.y));

        GUI.Box(new Rect(x, y, size.x, size.y), text, style);
    }

    // Box ueber einem Punkt im Raum ("screen" = Ergebnis von WorldToScreenPoint).
    public static void DrawBoxAbove(Vector3 screen, string text)
    {
        EnsureStyles();
        DrawBox(text, leftBox, screen.x, Screen.height - screen.y, 0.5f, 1f);
    }

    // Box oben links (z. B. Stromnetz-Anzeige).
    public static void DrawBoxTopLeft(string text, float margin = 10f)
    {
        EnsureStyles();
        DrawBox(text, leftBox, margin, margin, 0f, 0f);
    }

    // Box unten in der Mitte (Hinweise zu Bau-, Abriss- und Leitungsmodus).
    public static void DrawBoxBottomCenter(string text, float bottomMargin = 20f)
    {
        EnsureStyles();
        DrawBox(text, centerBox, Screen.width * 0.5f, Screen.height - bottomMargin, 0.5f, 1f);
    }

    // Box mittig, "offsetY" Pixel unter der Bildschirmmitte (Interaktions-Hinweis).
    public static void DrawBoxBelowCenter(string text, float offsetY = 40f)
    {
        EnsureStyles();
        DrawBox(text, centerBox, Screen.width * 0.5f,
                Screen.height * 0.5f + offsetY * TextScale, 0.5f, 0f);
    }
}
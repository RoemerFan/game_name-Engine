using UnityEngine;

// Gemeinsame Ladeanzeige (Text mit Prozent und ein Fortschrittsbalken). Wird beim Erzeugen
// der Welt, beim Laden eines Spielstands und im Hauptmenue benutzt, damit alles gleich aussieht.
public static class LoadingScreen
{
    private static readonly Color BarBack = new Color(1f, 1f, 1f, 0.15f);
    private static readonly Color BarBorder = new Color(1f, 1f, 1f, 0.35f);
    private static readonly Color BarFill = new Color(0.95f, 0.62f, 0.15f, 1f);
    private static readonly Color ScreenBack = new Color(0.04f, 0.05f, 0.07f, 1f);

    private static GUIStyle labelStyle;
    private static float builtForScale = -1f;

    private static float Scale
    {
        get { return Mathf.Max(1f, GameSettings.TextScale); }
    }

    private static void EnsureStyle()
    {
        if (labelStyle != null && Mathf.Approximately(builtForScale, Scale))
            return;

        builtForScale = Scale;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = Mathf.RoundToInt(20f * Scale);
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.normal.textColor = Color.white;
    }

    // Nur den Balken zeichnen (progress von 0 bis 1).
    public static void Bar(Rect rect, float progress)
    {
        Color old = GUI.color;

        // Rahmen und Hintergrund
        GUI.color = BarBorder;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        Rect inner = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);

        GUI.color = BarBack;
        GUI.DrawTexture(inner, Texture2D.whiteTexture);

        // Fuellung
        float fill = Mathf.Clamp01(progress);

        if (fill > 0f)
        {
            GUI.color = BarFill;
            GUI.DrawTexture(
                new Rect(inner.x, inner.y, inner.width * fill, inner.height),
                Texture2D.whiteTexture);
        }

        GUI.color = old;
    }

    // Text und Balken untereinander in einem GUILayout-Bereich (z. B. im Menue).
    public static void LayoutBar(string text, float progress)
    {
        EnsureStyle();

        GUILayout.Label(text, labelStyle);

        Rect rect = GUILayoutUtility.GetRect(10f, 22f * Scale, GUILayout.ExpandWidth(true));
        Bar(rect, progress);
    }

    // Ladeanzeige mitten auf dem Bildschirm. backgroundAlpha: 1 = Bildschirm komplett abgedeckt,
    // kleiner = Spielwelt scheint abgedunkelt durch.
    public static void DrawFullscreen(string text, float progress, float backgroundAlpha)
    {
        EnsureStyle();

        // Ganz oben zeichnen.
        GUI.depth = -500;

        Color old = GUI.color;
        Color back = ScreenBack;
        back.a = backgroundAlpha;

        GUI.color = back;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;

        float width = Mathf.Min(Screen.width - 40f, 560f * Scale);
        float textHeight = 40f * Scale;
        float barHeight = 22f * Scale;

        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height * 0.5f - (textHeight + barHeight) * 0.5f;

        GUI.Label(new Rect(x, y, width, textHeight), text, labelStyle);
        Bar(new Rect(x, y + textHeight, width, barHeight), progress);
    }
}
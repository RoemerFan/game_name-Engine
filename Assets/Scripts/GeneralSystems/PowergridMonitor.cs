using UnityEngine;

// Netzanzeige oben links. Wird vom PowerGrid automatisch angelegt.
//
// Taste F3 schaltet durch:  Kompakt  ->  Mit Kurven und Ereignissen  ->  Aus
//   - Kompakt: Zustand, Erzeugung, Last und Frequenz jedes Netzes
//   - Kurven:  Verlauf der letzten Minute (Last/Erzeugung und Frequenz)
//              sowie die letzten Ereignisse (Lastabwurf, Blackout, Sicherungen ...)
// Alle Groessen folgen der Einstellung "Textgroesse".
public class PowerGridMonitor : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F3;

    private enum Mode { Compact, Full, Hidden }
    private Mode mode = Mode.Compact;

    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle headerStyle;
    private GUIStyle textStyle;
    private GUIStyle smallStyle;
    private float builtForScale = -1f;

    // Farben
    private static readonly Color loadColor = new Color(0.35f, 0.65f, 1f, 0.85f);
    private static readonly Color generationColor = new Color(0.4f, 0.9f, 0.4f, 1f);
    private static readonly Color okColor = new Color(0.45f, 0.95f, 0.45f);
    private static readonly Color warnColor = new Color(1f, 0.8f, 0.25f);
    private static readonly Color dangerColor = new Color(1f, 0.35f, 0.3f);
    private static readonly Color idleColor = new Color(0.75f, 0.75f, 0.75f);

    private void Update()
    {
        if (GameInput.Pressed(GameAction.Monitor, toggleKey))
            mode = (Mode)(((int)mode + 1) % 3);
    }

    // Stile passend zur Textgroesse bauen.
    private void EnsureStyles()
    {
        float scale = GameSettings.TextScale;

        if (panelStyle != null && Mathf.Approximately(builtForScale, scale))
            return;

        builtForScale = scale;

        int pad = Mathf.RoundToInt(10f * scale);

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.padding = new RectOffset(pad, pad, pad, pad);

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = GameSettings.FontSize(14);
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = Color.white;

        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = GameSettings.FontSize(15);
        headerStyle.fontStyle = FontStyle.Bold;

        textStyle = new GUIStyle(GUI.skin.label);
        textStyle.fontSize = GameSettings.FontSize(13);
        textStyle.normal.textColor = Color.white;
        textStyle.wordWrap = true;

        smallStyle = new GUIStyle(GUI.skin.label);
        smallStyle.fontSize = GameSettings.FontSize(11);
        smallStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        smallStyle.richText = true;
    }

    private void OnGUI()
    {
        if (mode == Mode.Hidden)
            return;

        EnsureStyles();

        float scale = builtForScale;
        float width = Mathf.Min(Screen.width - 20f, 400f * scale);

        GUILayout.BeginArea(new Rect(10f, 10f, width, Screen.height - 20f));
        GUILayout.BeginVertical(panelStyle);

        GUILayout.Label(Loc.T("grid.hud.title", GameInput.Hint(GameAction.Monitor, toggleKey)), titleStyle);

        System.Collections.Generic.IList<PowerGrid.GridInfo> infos = PowerGrid.Infos;

        if (infos.Count == 0)
            GUILayout.Label(Loc.T("grid.hud.none"), textStyle);

        for (int i = 0; i < infos.Count; i++)
        {
            PowerGrid.GridInfo info = infos[i];

            // Name und Zustand, farbig nach Zustand.
            headerStyle.normal.textColor = StatusColor(info.statusKey);

            GUILayout.Space(4f * scale);
            GUILayout.Label(
                Loc.T("grid.hud.net", i + 1, StatusText(info.statusKey)),
                headerStyle);

            GUILayout.Label(
                Loc.T("grid.hud.values",
                    info.capacity.ToString("F1"),
                    info.demand.ToString("F1"),
                    info.frequency.ToString("F2")).TrimStart(),
                textStyle);

            // Batterien: Ladestand und Leistung (+ speist ein, - laedt).
            if (info.storageCount > 0)
            {
                int percent = info.storageCapacityKWh > 0f
                    ? Mathf.RoundToInt(info.storedKWh / info.storageCapacityKWh * 100f)
                    : 0;

                GUILayout.Label(
                    Loc.T("grid.hud.storage",
                        info.storageCount,
                        info.storedKWh.ToString("F1"),
                        info.storageCapacityKWh.ToString("F1"),
                        percent,
                        info.storagePowerKW.ToString("+0.0;-0.0;0.0")),
                    textStyle);
            }

            if (mode == Mode.Full)
            {
                GUILayout.Label(
                    Loc.T("grid.hud.counts", info.producers, info.consumers),
                    smallStyle);

                DrawPowerChart(info, width, scale);
                DrawFrequencyChart(info, width, scale);
            }
        }

        if (mode == Mode.Full)
            DrawEvents(scale);

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    // ---------- Zustand ----------

    private static string StatusText(string key)
    {
        return string.IsNullOrEmpty(key) ? "-" : Loc.T(key);
    }

    private static Color StatusColor(string key)
    {
        switch (key)
        {
            case "grid.status.ok":
            case "grid.status.battery": return okColor;
            case "grid.status.shedding":
            case "grid.status.underpowered": return warnColor;
            case "grid.status.blackout": return dangerColor;
            default: return idleColor;
        }
    }

    // ---------- Kurven ----------

    // Zeichnet ein einfarbiges Rechteck (ohne Linienfunktion, nur mit Rechtecken).
    private static void Fill(Rect r, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    // Leistung: Balken = Last, gruene Linie = Erzeugung.
    private void DrawPowerChart(PowerGrid.GridInfo info, float width, float scale)
    {
        GUILayout.Label(
            Loc.T("grid.chart.power") + "   " +
            "<color=#59a6ff>##</color> " + Loc.T("grid.chart.load") + "   " +
            "<color=#66e666>##</color> " + Loc.T("grid.chart.gen"),
            smallStyle);

        Rect r = GUILayoutUtility.GetRect(
            width - 20f * scale, 64f * scale, GUILayout.ExpandWidth(true));

        if (Event.current.type != EventType.Repaint)
            return;

        Fill(r, new Color(0f, 0f, 0f, 0.35f));

        // Hoechster Wert bestimmt den Massstab.
        float top = 10f;

        for (int i = 0; i < info.count; i++)
        {
            int k = info.Index(i);
            top = Mathf.Max(top, info.capacityHistory[k], info.demandHistory[k]);
        }

        top *= 1.15f;

        float barWidth = r.width / PowerGrid.GridInfo.Size;

        for (int i = 0; i < info.count; i++)
        {
            int k = info.Index(i);

            // Neuester Messpunkt rechts.
            float x = r.xMax - (info.count - i) * barWidth;

            float loadH = Mathf.Clamp01(info.demandHistory[k] / top) * r.height;
            Fill(new Rect(x, r.yMax - loadH, barWidth + 0.5f, loadH), loadColor);

            float genY = r.yMax - Mathf.Clamp01(info.capacityHistory[k] / top) * r.height;
            Fill(new Rect(x, genY - scale, barWidth + 0.5f, 2f * scale), generationColor);
        }

        // Skala
        GUI.Label(new Rect(r.x + 3f, r.y, r.width, 16f * scale),
            top.ToString("F0") + " kW", smallStyle);
    }

    // Frequenz: Punkte, dazu Linien fuer Normal-, Lastabwurf- und Blackout-Frequenz.
    private void DrawFrequencyChart(PowerGrid.GridInfo info, float width, float scale)
    {
        GUILayout.Label(Loc.T("grid.chart.freq"), smallStyle);

        Rect r = GUILayoutUtility.GetRect(
            width - 20f * scale, 56f * scale, GUILayout.ExpandWidth(true));

        if (Event.current.type != EventType.Repaint)
            return;

        Fill(r, new Color(0f, 0f, 0f, 0.35f));

        float nominal = PowerGrid.NominalHz;

        // Anzeigebereich: etwas ueber Normal, deutlich darunter (bis unter Blackout).
        float low = PowerGrid.BlackoutHz - 1f;
        float high = nominal + 2f;

        // Hilfslinien
        DrawHLine(r, FreqToY(r, nominal, low, high), new Color(1f, 1f, 1f, 0.35f), scale);
        DrawHLine(r, FreqToY(r, PowerGrid.ShedHz, low, high), new Color(warnColor.r, warnColor.g, warnColor.b, 0.5f), scale);
        DrawHLine(r, FreqToY(r, PowerGrid.BlackoutHz, low, high), new Color(dangerColor.r, dangerColor.g, dangerColor.b, 0.5f), scale);

        float stepX = r.width / PowerGrid.GridInfo.Size;
        float dot = 3f * scale;

        for (int i = 0; i < info.count; i++)
        {
            int k = info.Index(i);
            float f = info.frequencyHistory[k];

            float x = r.xMax - (info.count - i) * stepX;
            float y = FreqToY(r, f, low, high);

            // Farbe nach Abstand zur Normalfrequenz.
            Color c = f >= PowerGrid.ShedHz + 0.5f ? okColor
                    : f >= PowerGrid.ShedHz ? warnColor
                    : dangerColor;

            Fill(new Rect(x, y - dot * 0.5f, Mathf.Max(stepX, dot), dot), c);
        }

        GUI.Label(new Rect(r.x + 3f, r.y, r.width, 16f * scale),
            high.ToString("F0") + " Hz", smallStyle);
        GUI.Label(new Rect(r.x + 3f, r.yMax - 16f * scale, r.width, 16f * scale),
            low.ToString("F0") + " Hz", smallStyle);
    }

    private static float FreqToY(Rect r, float f, float low, float high)
    {
        float t = Mathf.InverseLerp(low, high, f);
        return r.yMax - t * r.height;
    }

    private static void DrawHLine(Rect r, float y, Color color, float scale)
    {
        Fill(new Rect(r.x, y - 0.5f * scale, r.width, Mathf.Max(1f, scale)), color);
    }

    // ---------- Ereignisse ----------

    private void DrawEvents(float scale)
    {
        GUILayout.Space(6f * scale);
        GUILayout.Label(Loc.T("grid.hud.events"), titleStyle);

        System.Collections.Generic.IList<PowerGrid.GridEvent> events = PowerGrid.Events;

        if (events.Count == 0)
        {
            GUILayout.Label(Loc.T("grid.hud.noevents"), smallStyle);
            return;
        }

        // Die letzten sechs, neueste zuerst.
        int shown = 0;

        for (int i = events.Count - 1; i >= 0 && shown < 6; i--, shown++)
        {
            PowerGrid.GridEvent e = events[i];

            int age = Mathf.RoundToInt(Time.time - e.time);

            textStyle.normal.textColor =
                e.severity >= 2 ? dangerColor :
                e.severity == 1 ? warnColor : Color.white;

            GUILayout.Label(Loc.T("grid.hud.eventline", age, e.text), textStyle);
        }

        textStyle.normal.textColor = Color.white;
    }
}
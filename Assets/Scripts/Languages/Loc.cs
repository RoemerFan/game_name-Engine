using System.Collections.Generic;
using UnityEngine;

// Uebersetzungs-System.
//
// Alle Texte im Spiel stehen nicht mehr im Code, sondern in Textdateien:
//     Assets/Resources/Localization/de.txt, en.txt, ...
// Im Code wird nur noch ein "Schluessel" angegeben:
//     Loc.T("boiler.status.dry")                      ->  "TROCKEN!"
//     Loc.T("build.target", maschinenName)            ->  "Ziel: Dampfkessel"
// ({0}, {1} ... in der Textdatei werden durch die uebergebenen Werte ersetzt.)
//
// Eine neue Sprache hinzufuegen:
//   1. de.txt kopieren, zum Beispiel als fr.txt, und die rechte Seite uebersetzen
//   2. in languages.txt eine Zeile  fr=Francais  ergaenzen
// Fertig: Die Sprache erscheint im Einstellungsmenue.
//
// Fehlt ein Schluessel in der gewaehlten Sprache, wird der deutsche Text benutzt;
// fehlt er auch dort, erscheint "#schluessel" (so findet man vergessene Texte).
public static class Loc
{
    const string Folder = "Localization/";
    const string FallbackCode = "de";

    public struct LanguageInfo
    {
        public string code;
        public string name;
    }

    static List<LanguageInfo> languages;
    static Dictionary<string, string> fallback;
    static Dictionary<string, string> current;
    static string loadedCode;

    // Alle verfuegbaren Sprachen (aus languages.txt).
    public static IList<LanguageInfo> Languages
    {
        get
        {
            LoadLanguageList();
            return languages;
        }
    }

    // Uebersetzt einen Schluessel in die aktuelle Sprache.
    public static string T(string key)
    {
        EnsureLoaded();

        string value;

        // Platzhalter wie <primary> werden durch die passende Taste ersetzt
        // (Maus oder Controller, je nach zuletzt benutztem Geraet).
        if (current.TryGetValue(key, out value))
            return GameInput.ReplaceTokens(value);

        if (fallback.TryGetValue(key, out value))
            return GameInput.ReplaceTokens(value);

        return "#" + key;
    }

    // Wie T, ersetzt aber {0}, {1} ... durch die uebergebenen Werte.
    public static string T(string key, params object[] args)
    {
        string template = T(key);

        try
        {
            return string.Format(template, args);
        }
        catch (System.FormatException)
        {
            // Fehlerhafter Platzhalter in der Textdatei: Text unveraendert zeigen.
            return template;
        }
    }

    // Gibt es diesen Schluessel?
    public static bool Has(string key)
    {
        EnsureLoaded();
        return current.ContainsKey(key) || fallback.ContainsKey(key);
    }

    // Bezeichnungen, die im Inspector eingetragen werden, koennen mit "@"
    // beginnen: "@fuse.label" wird uebersetzt, normaler Text bleibt wie er ist.
    public static string Resolve(string text)
    {
        if (!string.IsNullOrEmpty(text) && text[0] == '@')
            return T(text.Substring(1));

        return text;
    }

    // Anzeigename einer Maschine: "machine.<Prefab-Name>", sonst der Prefab-Name selbst.
    public static string MachineName(string prefabName)
    {
        string key = "machine." + prefabName;

        if (Has(key))
            return T(key);

        // Im Inspector (BuildCategory > Display Name) eingetragener Name.
        string custom;

        if (customMachineNames.TryGetValue(prefabName, out custom))
            return custom;

        return prefabName;
    }

    static readonly Dictionary<string, string> customMachineNames = new Dictionary<string, string>();

    // Merkt sich einen im Inspector eingetragenen Anzeigenamen einer Maschine.
    // (Eintraege in den Sprachdateien haben Vorrang.)
    public static void RegisterMachineName(string prefabName, string displayName)
    {
        if (!string.IsNullOrEmpty(prefabName) && !string.IsNullOrEmpty(displayName))
            customMachineNames[prefabName] = displayName;
    }

    // ---------- Laden ----------

    static void LoadLanguageList()
    {
        if (languages != null)
            return;

        languages = new List<LanguageInfo>();

        TextAsset list = Resources.Load<TextAsset>(Folder + "languages");

        if (list != null)
        {
            Dictionary<string, string> entries = Parse(list.text);

            foreach (KeyValuePair<string, string> pair in entries)
            {
                LanguageInfo info = new LanguageInfo();
                info.code = pair.Key;
                info.name = pair.Value;
                languages.Add(info);
            }
        }

        // Ohne languages.txt gibt es wenigstens Deutsch.
        if (languages.Count == 0)
        {
            LanguageInfo info = new LanguageInfo();
            info.code = FallbackCode;
            info.name = "Deutsch";
            languages.Add(info);
        }
    }

    // Laedt die Textdateien neu, wenn sich die Sprache geaendert hat.
    static void EnsureLoaded()
    {
        string code = GameSettings.Language;

        if (current != null && code == loadedCode)
            return;

        if (fallback == null)
            fallback = LoadFile(FallbackCode);

        current = code == FallbackCode ? fallback : LoadFile(code);
        loadedCode = code;
    }

    static Dictionary<string, string> LoadFile(string code)
    {
        TextAsset file = Resources.Load<TextAsset>(Folder + code);

        if (file == null)
        {
            Debug.LogWarning("Sprachdatei nicht gefunden: Resources/" + Folder + code + ".txt");
            return new Dictionary<string, string>();
        }

        return Parse(file.text);
    }

    // Liest Zeilen der Form  schluessel=Text.  Leere Zeilen und Zeilen mit #
    // am Anfang werden ignoriert. "\n" im Text wird zum Zeilenumbruch.
    static Dictionary<string, string> Parse(string text)
    {
        Dictionary<string, string> result = new Dictionary<string, string>();

        string[] lines = text.Split('\n');

        foreach (string raw in lines)
        {
            string line = raw.Trim('\r', '\uFEFF');

            if (line.Length == 0 || line.TrimStart()[0] == '#')
                continue;

            int equals = line.IndexOf('=');

            if (equals <= 0)
                continue;

            string key = line.Substring(0, equals).Trim();
            string value = line.Substring(equals + 1).Replace("\\n", "\n");

            result[key] = value;
        }

        return result;
    }
}
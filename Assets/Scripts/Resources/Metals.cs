using System.Collections.Generic;
using UnityEngine;

// Daten eines Metalls. Wird im Inspector beim "MetalRegistry" eingetragen.
[System.Serializable]
public class MetalDef
{
    [Tooltip("Kennwort, z. B. Iron. Dieses Wort traegt man im ItemTag (Metal) des Erz-/Barren-Prefabs ein.")]
    public string id = "Iron";
    [Tooltip("Anzeigename, falls es keinen Textschluessel gibt (oder der Schluessel nicht in den Sprachdateien steht).")]
    public string displayName = "Eisen";
    [Tooltip("Optional: Textschluessel fuer den Namen in den Sprachdateien, z. B. metal.iron.")]
    public string nameKey = "";
    [Tooltip("Ab dieser Temperatur ist das Metall fluessig (Grad Celsius).")]
    public float meltTempC = 1200f;
    [Tooltip("Kilogramm Metall pro Erzstueck.")]
    public float oreYieldKg = 5f;
    [Tooltip("Kilogramm Schlacke pro Erzstueck.")]
    public float slagPerOreKg = 3f;
    [Tooltip("Kohlestuecke pro Erzstueck.")]
    public float coalPerOre = 0.8f;
    [Tooltip("Farbe in der Anzeige.")]
    public Color color = new Color(0.55f, 0.55f, 0.58f);
}

// Eine Legierungs-Regel: Metall A + Metall B ergibt "result" (Reihenfolge egal).
[System.Serializable]
public class AlloyRule
{
    public string metalA = "Copper";
    public string metalB = "Tin";
    public string result = "Bronze";
}

// Alle Metalle des Spiels - als Nachschlagewerk fuer Hochofen und Giesserei.
//
// Die Daten selbst stehen NICHT mehr hier im Code, sondern im Inspector: Man haengt das
// Skript "MetalRegistry" an ein Objekt der Szene und pflegt dort die Liste der Metalle und
// der Legierungs-Regeln. Ein neues Metall braucht keine neue Maschinenvariante: Eintrag in der
// Liste, dem Erz-Prefab im ItemTag die Metall-ID geben, fertig.
//
// Gibt es in der Szene keine MetalRegistry, gilt nur Eisen (wie bisher).
public static class Metals
{
    private static readonly List<MetalDef> list = new List<MetalDef> { new MetalDef { nameKey = "metal.iron" } };
    private static readonly List<AlloyRule> alloys = new List<AlloyRule>();

    public static MetalDef Default { get { return list[0]; } }

    // Wird von der MetalRegistry beim Start aufgerufen und ersetzt alle Daten.
    public static void SetAll(IList<MetalDef> defs, IList<AlloyRule> rules)
    {
        list.Clear();
        alloys.Clear();

        if (defs != null)
        {
            foreach (MetalDef d in defs)
            {
                if (d != null && !string.IsNullOrEmpty(d.id) && IndexOf(d.id) < 0)
                    list.Add(d);
            }
        }

        // Es muss immer mindestens ein Metall geben.
        if (list.Count == 0)
            list.Add(new MetalDef());

        if (rules != null)
        {
            foreach (AlloyRule r in rules)
            {
                if (r != null)
                    alloys.Add(r);
            }
        }
    }

    // Fuegt zur Laufzeit ein Metall hinzu (oder ersetzt eines mit gleicher ID).
    public static void Register(MetalDef def)
    {
        if (def == null || string.IsNullOrEmpty(def.id))
            return;

        int index = IndexOf(def.id);

        if (index >= 0)
            list[index] = def;
        else
            list.Add(def);
    }

    // Gibt die Daten zu einer ID zurueck (null = unbekannt oder leer).
    public static MetalDef Get(string id)
    {
        int index = IndexOf(id);
        return index >= 0 ? list[index] : null;
    }

    public static bool IsKnown(string id)
    {
        return IndexOf(id) >= 0;
    }

    // Nummer in der Liste (fuer Speichern). -1 = unbekannt.
    // Wichtig: Die Reihenfolge der Liste im Inspector nicht aendern, wenn schon Spielstaende
    // existieren (neue Metalle immer unten anhaengen).
    public static int IndexOf(string id)
    {
        if (string.IsNullOrEmpty(id))
            return -1;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].id == id)
                return i;
        }

        return -1;
    }

    public static string IdAt(int index)
    {
        return index >= 0 && index < list.Count ? list[index].id : "";
    }

    // Anzeigename (uebersetzt). Leer/unbekannt = "-".
    public static string Name(string id)
    {
        MetalDef def = Get(id);

        if (def == null)
            return "-";

        if (!string.IsNullOrEmpty(def.nameKey) && Loc.Has(def.nameKey))
            return Loc.T(def.nameKey);

        return string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
    }

    // Kann man zwei Metalle in einer Schmelze mischen? Wenn ja, ist "result" das
    // entstehende Metall. Die Regeln stehen im Inspector (MetalRegistry > Alloys).
    public static bool TryMix(string a, string b, out string result)
    {
        result = a;

        // Gleiche Metalle (oder leer) mischen sich immer.
        if (string.IsNullOrEmpty(a))
        {
            result = b;
            return true;
        }

        if (string.IsNullOrEmpty(b) || a == b)
            return true;

        foreach (AlloyRule r in alloys)
        {
            bool match = (r.metalA == a && r.metalB == b) || (r.metalA == b && r.metalB == a);

            if (match && IsKnown(r.result))
            {
                result = r.result;
                return true;
            }
        }

        result = "";
        return false;
    }
}

// Wird von Maschinen mit Schmelze-Anschluessen (Medium "Melt") zusaetzlich zu IPortOwner
// implementiert. Zur Schmelze gehoeren Metallart und Temperatur, deshalb reicht die
// normale Mengen-Schnittstelle dafuer nicht.
public interface IMeltOwner : IPortOwner
{
    // Ausgang: Welches Metall gibt dieser Port gerade ab? ("" = nichts)
    string GetMelt(Port port);

    // Ausgang: Temperatur der Schmelze in Grad Celsius.
    float GetMeltTemp(Port port);

    // Eingang: Nimmt dieser Port gerade diese Metallart an?
    bool CanAcceptMelt(Port port, string metal);

    // Eingang: Nimmt bis zu "kg" Schmelze auf, gibt die angenommene Menge zurueck.
    float GiveMelt(Port port, string metal, float kg, float tempC);
}
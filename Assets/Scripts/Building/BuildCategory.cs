using UnityEngine;

// Alte Kategorie-Auswahl (nur noch fuer bestehende Prefabs, die sie schon eingestellt haben).
// Neue Kategorien legst du im Inspector an: BuildMenuController > Categories.
public enum BuildCategoryType
{
    Other,       // Sonstiges
    Power,       // Strom & Licht
    Production,  // Produktion (Hochofen, Giesserei ...)
    Fluids,      // Wasser & Dampf (Kessel, Pumpen ...)
    Transport,   // Foerderbaender, Aufzuege ...
    Structures   // Bauteile (Bodenplatten, Waende ...)
}

// Optional an die Wurzel eines Maschinen-Prefabs haengen: legt fest, in welcher
// Kategorie es im Baumenue erscheint (und optional, wie es heisst).
//
// Ohne dieses Skript landen Bodenplatten und Waende (Komponente BuildPiece) bei "structures",
// alles andere bei "other".
public class BuildCategory : MonoBehaviour
{
    [Tooltip("Kennwort der Kategorie, wie in der Liste beim BuildMenuController eingetragen " +
             "(z. B. power, production, fluids, transport, structures, other oder eine eigene). " +
             "Leer = die alte Auswahl unten wird benutzt.")]
    public string categoryId = "";

    [Tooltip("Alte Auswahl, wird nur benutzt, wenn 'Category Id' leer ist.")]
    public BuildCategoryType category = BuildCategoryType.Other;

    [Tooltip("Name im Baumenue. Leer = Eintrag aus den Sprachdateien (machine.<Prefab-Name>), " +
             "sonst der Prefab-Name. Ein Eintrag in den Sprachdateien hat Vorrang.")]
    public string displayName = "";

    // Kennwort der Kategorie eines Prefabs bestimmen (immer kleingeschrieben).
    public static string IdOf(GameObject prefab)
    {
        if (prefab == null)
            return "other";

        BuildCategory set = prefab.GetComponent<BuildCategory>();

        if (set != null)
        {
            if (!string.IsNullOrEmpty(set.categoryId))
                return set.categoryId.Trim().ToLowerInvariant();

            return set.category.ToString().ToLowerInvariant();
        }

        if (prefab.GetComponent<BuildPiece>() != null)
            return "structures";

        return "other";
    }

    // Angezeigter Name eines Prefabs aus dem Inspector (leer = nicht gesetzt).
    public static string DisplayNameOf(GameObject prefab)
    {
        if (prefab == null)
            return "";

        BuildCategory set = prefab.GetComponent<BuildCategory>();

        return set != null ? set.displayName : "";
    }
}
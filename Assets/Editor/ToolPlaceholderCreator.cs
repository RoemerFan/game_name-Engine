#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// =====================================================================
//  ToolPlaceholderCreator: erzeugt Schaufel und Spitzhacke als PLATZHALTER-Prefabs (nur Editor).
//
//  WICHTIG: Diese Datei gehoert in einen Ordner namens "Editor" (z. B. Assets/Editor/), NICHT nach Assets/Scripts.
//
//  Benutzung: Menue  Factocreative > Platzhalter > Werkzeuge erzeugen
//  Es entstehen in Assets/Prefabs/Placeholders/:  ShovelPlaceholder und PickaxePlaceholder.
//  Danach:
//   1. Beide Prefabs in die Liste "Item Prefabs" am SaveSystem eintragen (sonst werden sie nicht gespeichert).
//   2. Zum Testen ein Prefab in die Szene ziehen (auf den Boden, leicht ueber dem Gelaende) und im Spiel mit E aufheben.
//  Aufbau: Wurzel (Rigidbody, Collider, DigTool, SavedItem) > "SwingPivot" (am Griffende) > Modell.
//  Vorne = +Z. Eigenes Modell: unter "SwingPivot" setzen, die Platzhalter-Teile loeschen.
// =====================================================================
public static class ToolPlaceholderCreator
{
    private const string Folder = "Assets/Prefabs/Placeholders";

    [MenuItem("Factocreative/Platzhalter/Werkzeuge erzeugen")]
    public static void CreateAll()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        Material wood = MakeMaterial("ToolWood", new Color(0.5f, 0.33f, 0.18f));
        Material metal = MakeMaterial("ToolMetal", new Color(0.55f, 0.57f, 0.6f));

        Save(BuildShovel(wood, metal), "ShovelPlaceholder");
        Save(BuildPickaxe(wood, metal), "PickaxePlaceholder");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Werkzeug-Platzhalter erzeugt in " + Folder + ". Trage sie beim SaveSystem in 'Item Prefabs' ein.");
    }

    // ---------- Schaufel: schnell in Erde, grosse Mulde, haelt nicht lange ----------

    private static GameObject BuildShovel(Material wood, Material metal)
    {
        GameObject root = NewTool("ShovelPlaceholder", out Transform pivot);

        // Stiel (0,9 m) und Blatt vorne.
        Part(pivot, PrimitiveType.Cube, "Stiel", new Vector3(0f, 0f, 0.45f), new Vector3(0.05f, 0.05f, 0.9f), wood);
        Part(pivot, PrimitiveType.Cube, "Blatt", new Vector3(0f, -0.02f, 0.98f), new Vector3(0.22f, 0.03f, 0.3f), metal);

        DigTool t = root.GetComponent<DigTool>();
        t.speedMultiplier = 1.6f;
        t.radiusMultiplier = 1.25f;
        t.maxDurability = 150f;
        t.materials = new[]
        {
            Effect(BlockType.Grass, 1.6f, 0.6f),
            Effect(BlockType.Dirt, 1.6f, 0.6f),
            Effect(BlockType.Stone, 0.5f, 2.5f),
            Effect(BlockType.Coal, 0.8f, 1.5f),
            Effect(BlockType.Iron, 0.4f, 3f)
        };

        return root;
    }

    // ---------- Spitzhacke: schnell in Stein, Kohle und Erz; kleineres Loch, haelt lange ----------

    private static GameObject BuildPickaxe(Material wood, Material metal)
    {
        GameObject root = NewTool("PickaxePlaceholder", out Transform pivot);

        Part(pivot, PrimitiveType.Cube, "Stiel", new Vector3(0f, 0f, 0.45f), new Vector3(0.05f, 0.05f, 0.9f), wood);
        Part(pivot, PrimitiveType.Cube, "Kopf", new Vector3(0f, 0f, 0.92f), new Vector3(0.5f, 0.05f, 0.06f), metal);

        DigTool t = root.GetComponent<DigTool>();
        t.speedMultiplier = 1.5f;
        t.radiusMultiplier = 0.9f;
        t.maxDurability = 250f;
        t.materials = new[]
        {
            Effect(BlockType.Grass, 0.9f, 1f),
            Effect(BlockType.Dirt, 0.9f, 1f),
            Effect(BlockType.Stone, 1.8f, 1f),
            Effect(BlockType.Coal, 1.8f, 0.8f),
            Effect(BlockType.Iron, 1.6f, 1f)
        };

        return root;
    }

    // ---------- gemeinsame Teile ----------

    // Wurzel mit Physik, Collider, DigTool, SavedItem und dem Schwenk-Pivot am Griffende.
    private static GameObject NewTool(string name, out Transform pivot)
    {
        GameObject root = new GameObject(name);

        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.mass = 3f;

        // Ein Collider fuer die ganze Laenge (sonst rollt das Werkzeug wie ein Stab).
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(0.3f, 0.1f, 1.2f);
        box.center = new Vector3(0f, 0f, 0f);

        GameObject p = new GameObject("SwingPivot");
        p.transform.SetParent(root.transform, false);
        p.transform.localPosition = new Vector3(0f, 0f, -0.45f);   // Griffende
        pivot = p.transform;

        DigTool tool = root.AddComponent<DigTool>();
        tool.swingPivot = pivot;

        // Haltung in der Hand: etwas rechts und tiefer, weiter weg, damit der Stiel nicht im Gesicht steckt.
        tool.holdOffset = new Vector3(0.3f, -0.15f, -0.2f);

        root.AddComponent<SavedItem>();
        return root;
    }

    private static DigTool.MaterialEffect Effect(BlockType material, float speed, float wear)
    {
        DigTool.MaterialEffect e = new DigTool.MaterialEffect();
        e.material = material;
        e.speedFactor = speed;
        e.wearFactor = wear;
        return e;
    }

    private static void Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(g.GetComponent<Collider>());
    }

    private static void Save(GameObject go, string name)
    {
        PrefabUtility.SaveAsPrefabAsset(go, Folder + "/" + name + ".prefab");
        Object.DestroyImmediate(go);
    }

    private static Material MakeMaterial(string name, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
            return existing;

        Material m = new Material(Shader.Find("Standard"));
        m.color = color;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
#endif
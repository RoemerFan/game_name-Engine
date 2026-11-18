#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// =====================================================================
//  HosePlaceholderCreator: erzeugt einen Wasserkran (Wassersaeule mit Schlauch) als PLATZHALTER-Prefab (nur Editor).
//
//  WICHTIG: Diese Datei gehoert in einen Ordner namens "Editor" (z. B. Assets/Editor/), NICHT nach Assets/Scripts.
//
//  Benutzung: Menue  Factocreative > Platzhalter > Wasserkran mit Schlauch erzeugen
//  Es entsteht Assets/Prefabs/Placeholders/HoseReelPlaceholder (Kategorie "fluids").
//  Das Prefab traegst du beim BuildMenuController > Machine Prefabs ein.
//  Im Prefab liegen zwei leere Objekte: "Schlauchauslass" (hier kommt der Schlauch heraus) und "Duesenhalter"
//  (hier haengt die Duese). Eigenes Modell: Objekte mit denselben Namen ans Modell setzen und im HoseReel eintragen.
// =====================================================================
public static class HosePlaceholderCreator
{
    private const string Folder = "Assets/Prefabs/Placeholders";

    [MenuItem("Factocreative/Platzhalter/Wasserkran mit Schlauch erzeugen")]
    public static void Create()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        Material blue = MakeMaterial("HoseBlue", new Color(0.2f, 0.45f, 0.8f));
        Material grey = MakeMaterial("HoseGrey", new Color(0.4f, 0.4f, 0.43f));

        GameObject root = new GameObject("HoseReelPlaceholder");

        // Fundament und Saeule
        Visual(root.transform, PrimitiveType.Cube, "Sockel", new Vector3(0f, 0.1f, 0f), new Vector3(0.7f, 0.2f, 0.7f), grey);
        Visual(root.transform, PrimitiveType.Cylinder, "Saeule", new Vector3(0f, 1.0f, 0f), new Vector3(0.3f, 0.8f, 0.3f), blue);
        Visual(root.transform, PrimitiveType.Sphere, "Kopf", new Vector3(0f, 1.85f, 0f), new Vector3(0.38f, 0.38f, 0.38f), blue);

        // Trommel (Schlauchrolle) an der Seite
        GameObject drum = Visual(root.transform, PrimitiveType.Cylinder, "Trommel", new Vector3(0.3f, 1.2f, 0f),
                                 new Vector3(0.5f, 0.12f, 0.5f), grey);
        drum.transform.localEulerAngles = new Vector3(0f, 0f, 90f);

        // Massiver Collider (Saeule), damit man nicht hindurchlaeuft
        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 1f, 0f);
        capsule.radius = 0.35f;
        capsule.height = 2f;

        GameObject outlet = new GameObject("Schlauchauslass");
        outlet.transform.SetParent(root.transform, false);
        outlet.transform.localPosition = new Vector3(0.55f, 1.2f, 0f);

        GameObject holder = new GameObject("Duesenhalter");
        holder.transform.SetParent(root.transform, false);
        holder.transform.localPosition = new Vector3(0.6f, 1.05f, 0.1f);
        holder.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);

        HoseReel reel = root.AddComponent<HoseReel>();
        SetObject(reel, "hoseOutlet", outlet.transform);
        SetObject(reel, "nozzleHolder", holder.transform);

        BuildCategory c = root.AddComponent<BuildCategory>();
        c.categoryId = "fluids";
        c.displayName = "Wasserkran mit Schlauch";

        string path = Folder + "/HoseReelPlaceholder.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Wasserkran-Platzhalter erzeugt: " + path + ". Trage ihn beim BuildMenuController > Machine Prefabs ein.");
    }

    private static GameObject Visual(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    private static void SetObject(Object target, string field, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogWarning("Feld '" + field + "' nicht gefunden in " + target.GetType().Name);
            return;
        }

        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
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
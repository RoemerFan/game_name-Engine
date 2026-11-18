#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// =====================================================================
//  ConveyorPlaceholderCreator: erzeugt zwei Foerderband-PLATZHALTER als Prefabs (nur Editor).
//
//  WICHTIG: Diese Datei gehoert in einen Ordner namens "Editor" (z. B. Assets/Editor/), NICHT nach Assets/Scripts.
//
//  Benutzung: Menue  Factocreative > Platzhalter > Foerderbaender erzeugen
//  Es entstehen in Assets/Prefabs/Placeholders/:
//    - ConveyorPlaceholder           einfaches Band (4 m) mit Stromanschluss
//    - TelescopicConveyorPlaceholder Teleskopband mit zwei Hebeln (Neigung / Ausfahren) und Stromanschluss
//  Die Prefabs traegst du wie jede andere Maschine beim BuildMenuController > Machine Prefabs ein
//  (Kategorie "transport" ist schon gesetzt). Die Hierarchie zeigt dir, wie ein eigenes Modell aufgebaut
//  sein muss (siehe auch die Kommentare in ConveyorBelt.cs und TelescopicBelt.cs).
//
//  Masse: Bandbreite 0,8 m, Bandflaeche 0,12 m dick, vorne = +Z (Laufrichtung).
// =====================================================================
public static class ConveyorPlaceholderCreator
{
    private const string Folder = "Assets/Prefabs/Placeholders";

    [MenuItem("Factocreative/Platzhalter/Foerderbaender erzeugen")]
    public static void CreateAll()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        Material grey = MakeMaterial("ConveyorGrey", new Color(0.35f, 0.36f, 0.4f), null);
        Material dark = MakeMaterial("ConveyorDark", new Color(0.12f, 0.12f, 0.13f), null);
        Material yellow = MakeMaterial("ConveyorYellow", new Color(0.9f, 0.7f, 0.1f), null);
        Material green = MakeMaterial("ConveyorLeverGreen", new Color(0.2f, 0.75f, 0.25f), null);
        Material blue = MakeMaterial("ConveyorLeverBlue", new Color(0.2f, 0.4f, 0.9f), null);
        Material belt = MakeMaterial("ConveyorBelt", Color.white, MakeStripeTexture());

        SavePrefab(BuildSimple(grey, dark, yellow, belt), "ConveyorPlaceholder");
        SavePrefab(BuildTelescopic(grey, dark, yellow, green, blue, belt), "TelescopicConveyorPlaceholder");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Foerderband-Platzhalter erzeugt in " + Folder + ". Trage sie beim BuildMenuController > Machine Prefabs ein.");
    }

    // ---------- Einfaches Band ----------

    private static GameObject BuildSimple(Material grey, Material dark, Material yellow, Material belt)
    {
        GameObject root = new GameObject("ConveyorPlaceholder");

        // Beine.
        Visual("BeinVL", root.transform, new Vector3(-0.35f, 0.25f, -1.7f), new Vector3(0.1f, 0.5f, 0.1f), grey);
        Visual("BeinVR", root.transform, new Vector3(0.35f, 0.25f, -1.7f), new Vector3(0.1f, 0.5f, 0.1f), grey);
        Visual("BeinHL", root.transform, new Vector3(-0.35f, 0.25f, 1.7f), new Vector3(0.1f, 0.5f, 0.1f), grey);
        Visual("BeinHR", root.transform, new Vector3(0.35f, 0.25f, 1.7f), new Vector3(0.1f, 0.5f, 0.1f), grey);

        // Rahmen und Seitenwaende.
        Visual("Rahmen", root.transform, new Vector3(0f, 0.48f, 0f), new Vector3(0.9f, 0.06f, 4f), dark);
        Visual("WandL", root.transform, new Vector3(-0.43f, 0.66f, 0f), new Vector3(0.05f, 0.2f, 4f), yellow);
        Visual("WandR", root.transform, new Vector3(0.43f, 0.66f, 0f), new Vector3(0.05f, 0.2f, 4f), yellow);

        // Bandflaeche: Der Collider ist die Flaeche, auf der die Stuecke laufen (kein Trigger, +Z = Laufrichtung).
        GameObject surface = new GameObject("Bandflaeche");
        surface.transform.SetParent(root.transform, false);
        surface.transform.localPosition = new Vector3(0f, 0.57f, 0f);

        BoxCollider box = surface.AddComponent<BoxCollider>();
        box.size = new Vector3(0.8f, 0.12f, 4f);

        // Sichtbares Band mit Streifen-Textur, die beim Laufen mitwandert.
        GameObject beltVisual = Visual("BandOptik", surface.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.8f, 0.03f, 4f), belt);

        ConveyorBelt conveyor = root.AddComponent<ConveyorBelt>();
        SetObject(conveyor, "surface", box);
        SetObject(conveyor, "beltRenderer", beltVisual.GetComponent<Renderer>());
        SetFloat(conveyor, "texturesPerMeter", 0.25f);

        AddPowerPort(root.transform, new Vector3(-0.55f, 0.5f, -1.2f));
        AddShaftDrive(root, new Vector3(0.55f, 0.5f, -1.2f));
        AddCategory(root, "Foerderband (Platzhalter)");

        return root;
    }

    // ---------- Teleskopband ----------

    private static GameObject BuildTelescopic(Material grey, Material dark, Material yellow,
                                              Material green, Material blue, Material belt)
    {
        GameObject root = new GameObject("TelescopicConveyorPlaceholder");

        // Sockel (fest) mit festen Collidern, damit man davor stehen kann.
        GameObject baseVisual = Visual("Sockel", root.transform, new Vector3(0f, 0.35f, 0f), new Vector3(1.2f, 0.7f, 1.2f), grey);
        baseVisual.AddComponent<BoxCollider>();

        // Der Arm: Ursprung = Gelenk, +Z = Bandrichtung. Alles, was mitkippen soll, ist Kind davon.
        GameObject arm = new GameObject("Arm");
        arm.transform.SetParent(root.transform, false);
        arm.transform.localPosition = new Vector3(0f, 0.8f, 0f);

        // Bandflaeche (Collider) am Arm: laenger werdend beim Ausfahren.
        GameObject surface = new GameObject("Bandflaeche");
        surface.transform.SetParent(arm.transform, false);

        BoxCollider box = surface.AddComponent<BoxCollider>();
        box.size = new Vector3(0.8f, 0.12f, 3f);
        box.center = new Vector3(0f, 0f, 1.5f);

        // Optik des Bandes: Ursprung am HINTEREN Ende (wird beim Ausfahren in Z gestreckt).
        GameObject stretch = new GameObject("BandStreckung");
        stretch.transform.SetParent(arm.transform, false);
        GameObject beltVisual = Visual("BandOptik", stretch.transform, new Vector3(0f, 0.05f, 1.5f), new Vector3(0.8f, 0.03f, 3f), belt);

        Visual("WandL", arm.transform, new Vector3(-0.43f, 0.12f, 1.5f), new Vector3(0.05f, 0.2f, 3f), yellow);
        Visual("WandR", arm.transform, new Vector3(0.43f, 0.12f, 1.5f), new Vector3(0.05f, 0.2f, 3f), yellow);
        Visual("Rahmen", arm.transform, new Vector3(0f, -0.06f, 1.5f), new Vector3(0.9f, 0.06f, 3f), dark);

        // Teleskopteile: schieben sich beim Ausfahren in Bandrichtung heraus (innen nach aussen).
        GameObject section1 = new GameObject("Teleskop1");
        section1.transform.SetParent(arm.transform, false);
        Visual("Optik", section1.transform, new Vector3(0f, -0.11f, 2.2f), new Vector3(0.74f, 0.06f, 1.6f), grey);

        GameObject section2 = new GameObject("Teleskop2");
        section2.transform.SetParent(arm.transform, false);
        Visual("Optik", section2.transform, new Vector3(0f, -0.17f, 2.2f), new Vector3(0.68f, 0.06f, 1.6f), dark);

        // Die Bauteile.
        ConveyorBelt conveyor = root.AddComponent<ConveyorBelt>();
        SetObject(conveyor, "surface", box);
        SetObject(conveyor, "beltRenderer", beltVisual.GetComponent<Renderer>());
        SetFloat(conveyor, "texturesPerMeter", 0.33f);

        TelescopicBelt telescopic = root.AddComponent<TelescopicBelt>();
        telescopic.arm = arm.transform;
        telescopic.surface = box;
        telescopic.sections = new Transform[] { section1.transform, section2.transform };
        telescopic.stretchParts = new Transform[] { stretch.transform };

        // Zwei Hebel am Sockel: Neigung und Laenge.
        AddLever(root.transform, "HebelNeigung", new Vector3(0.7f, 0.55f, -0.3f), BoomLever.Function.Angle, telescopic, green);
        AddLever(root.transform, "HebelLaenge", new Vector3(0.7f, 0.55f, 0.15f), BoomLever.Function.Extension, telescopic, blue);

        AddPowerPort(root.transform, new Vector3(-0.65f, 0.45f, -0.4f));
        AddShaftDrive(root, new Vector3(0.65f, 0.3f, -0.55f));
        AddCategory(root, "Teleskop-Foerderband (Platzhalter)");

        return root;
    }

    // Ein Hebel: leeres Objekt mit Trigger-Box, darin der Griff (Pivot unten, kippt um X).
    private static void AddLever(Transform parent, string name, Vector3 pos, BoomLever.Function function,
                                 TelescopicBelt belt, Material material)
    {
        GameObject lever = new GameObject(name);
        lever.transform.SetParent(parent, false);
        lever.transform.localPosition = pos;

        BoxCollider trigger = lever.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(0.25f, 0.4f, 0.25f);
        trigger.center = new Vector3(0f, 0.15f, 0f);

        GameObject handle = new GameObject("Griff");
        handle.transform.SetParent(lever.transform, false);
        Visual("Stange", handle.transform, new Vector3(0f, 0.15f, 0f), new Vector3(0.04f, 0.3f, 0.04f), material);
        Visual("Knauf", handle.transform, new Vector3(0f, 0.31f, 0f), new Vector3(0.09f, 0.09f, 0.09f), material);

        BoomLever boom = lever.AddComponent<BoomLever>();
        boom.belt = belt;
        boom.function = function;
        boom.handle = handle.transform;
    }

    // ---------- Gemeinsames ----------

    // Stromanschluss: Port (Strom) mit kleinem Trigger zum Anvisieren. Die Steckdose zeichnet das Spiel selbst.
    private static void AddPowerPort(Transform parent, Vector3 pos)
    {
        GameObject go = new GameObject("StromPort");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;

        SphereCollider col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.15f;

        Port port = go.AddComponent<Port>();
        SerializedObject so = new SerializedObject(port);
        SerializedProperty medium = so.FindProperty("medium");

        if (medium != null)
            medium.enumValueIndex = (int)PortMedium.Power;

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Wellenanschluss: Port (Welle) mit Trigger zum Anvisieren und das ShaftDrive-Skript. Ohne angeschlossene Welle
    // laeuft das Band weiter mit Strom.
    private static void AddShaftDrive(GameObject root, Vector3 pos)
    {
        GameObject go = new GameObject("WellenPort");
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);   // Wellen-Stutzen zeigt zur Seite (+X)

        SphereCollider col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.15f;

        Port port = go.AddComponent<Port>();
        port.Configure(PortMedium.Shaft, PortDirection.Both, 0f);

        root.AddComponent<ShaftDrive>();
    }

    private static void AddCategory(GameObject root, string displayName)
    {
        BuildCategory c = root.AddComponent<BuildCategory>();
        c.categoryId = "transport";
        c.displayName = displayName;
    }

    // Ein Quader nur zum Anschauen (ohne Collider).
    private static GameObject Visual(string name, Transform parent, Vector3 pos, Vector3 scale, Material material)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = material;
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

    private static void SetFloat(Object target, string field, float value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogWarning("Feld '" + field + "' nicht gefunden in " + target.GetType().Name);
            return;
        }

        p.floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SavePrefab(GameObject go, string name)
    {
        string path = Folder + "/" + name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    private static Material MakeMaterial(string name, Color color, Texture2D texture)
    {
        string path = Folder + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
            return existing;

        Material m = new Material(Shader.Find("Standard"));
        m.color = color;
        m.SetFloat("_Glossiness", 0.15f);

        if (texture != null)
            m.mainTexture = texture;

        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // Dunkles Band mit hellen Querstreifen, damit man die Bewegung sieht.
    private static Texture2D MakeStripeTexture()
    {
        string path = Folder + "/ConveyorStripes.asset";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        if (existing != null)
            return existing;

        Texture2D t = new Texture2D(8, 32, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Repeat;
        t.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < 32; y++)
        {
            Color c = (y % 8) < 2 ? new Color(0.55f, 0.55f, 0.58f) : new Color(0.14f, 0.14f, 0.15f);

            for (int x = 0; x < 8; x++)
                t.SetPixel(x, y, c);
        }

        t.Apply();
        AssetDatabase.CreateAsset(t, path);
        return t;
    }
}
#endif
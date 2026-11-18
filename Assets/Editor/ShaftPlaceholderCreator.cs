#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// =====================================================================
//  ShaftPlaceholderCreator: erzeugt Dampfmaschine und Wellengenerator als PLATZHALTER-Prefabs (nur Editor).
//
//  WICHTIG: Diese Datei gehoert in einen Ordner namens "Editor" (z. B. Assets/Editor/), NICHT nach Assets/Scripts.
//
//  Benutzung: Menue  Factocreative > Platzhalter > Dampfmaschine und Wellengenerator erzeugen (macht auch Kupplung und Getriebe)
//  Es entstehen in Assets/Prefabs/Placeholders/:
//    - ShaftSteamEnginePlaceholder  Dampfmaschine: Dampf-Eingang, Wellen-Port, drehendes Schwungrad
//    - ShaftGeneratorPlaceholder    Generator: Wellen-Port, Strom-Port, drehender Laeufer
//    - ShaftClutchPlaceholder       Kupplung: zwei Wellen-Ports, Schalter (Ein = eingekuppelt)
//    - ShaftGearboxPlaceholder      Getriebe: Eingang links, Ausgang rechts, Uebersetzung 0,5 (im Skript einstellbar)
//    - ShaftAngleGearPlaceholder    Winkelgetriebe (InlineDevice im Wellen-Modus): Welle kommt von links (-X), geht
//                                   nach vorn (+Z) weiter, Uebersetzung 1, mit Schalter (Kupplung)
//  Alle traegst du beim BuildMenuController > Machine Prefabs ein (Kategorien "power").
//  Die Foerderband-Platzhalter (Menue "Foerderbaender erzeugen") haben jetzt auch einen Wellen-Port.
//  Verbinden: Rohr-Werkzeug, von Wellen-Port zu Wellen-Port (wie bei Leitungen). Eine Welle verbindet
//  beliebig viele Maschinen, sie drehen dann gemeinsam. Die Ausrichtung: Der Port zeigt mit seiner blauen
//  Achse (Z) nach aussen, dort geht die Welle los.
// =====================================================================
public static class ShaftPlaceholderCreator
{
    private const string Folder = "Assets/Prefabs/Placeholders";

    [MenuItem("Factocreative/Platzhalter/Dampfmaschine und Wellengenerator erzeugen")]
    public static void CreateAll()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        Material dark = MakeMaterial("ShaftDark", new Color(0.18f, 0.18f, 0.2f));
        Material red = MakeMaterial("ShaftRed", new Color(0.65f, 0.15f, 0.12f));
        Material steel = MakeMaterial("ShaftSteel", new Color(0.6f, 0.62f, 0.66f));
        Material green = MakeMaterial("ShaftGreen", new Color(0.2f, 0.5f, 0.3f));

        Save(BuildEngine(dark, red, steel), "ShaftSteamEnginePlaceholder");
        Save(BuildGenerator(dark, green, steel), "ShaftGeneratorPlaceholder");
        Save(BuildClutch(dark, steel, red), "ShaftClutchPlaceholder");
        Save(BuildGearbox(dark, steel, red), "ShaftGearboxPlaceholder");
        Save(BuildAngleGear(dark, steel, red), "ShaftAngleGearPlaceholder");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Dampfmaschine und Wellengenerator (Platzhalter) erzeugt in " + Folder + ".");
    }

    // ---------- Dampfmaschine: Wellen-Port zu +X, Dampf-Eingang nach oben ----------

    private static GameObject BuildEngine(Material dark, Material red, Material steel)
    {
        GameObject root = new GameObject("ShaftSteamEnginePlaceholder");

        Part(root.transform, PrimitiveType.Cube, "Sockel", new Vector3(0f, 0.15f, 0f), Vector3.zero, new Vector3(2.2f, 0.3f, 1.2f), dark);
        Part(root.transform, PrimitiveType.Cylinder, "Zylinder", new Vector3(-0.6f, 0.75f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.55f, 0.6f, 0.55f), red);
        Part(root.transform, PrimitiveType.Cube, "Stuetze", new Vector3(0.6f, 0.65f, 0f), Vector3.zero, new Vector3(0.3f, 0.7f, 0.9f), dark);

        // Schwungrad: dreht um die Wellenachse (X).
        GameObject rotor = new GameObject("Schwungrad");
        rotor.transform.SetParent(root.transform, false);
        rotor.transform.localPosition = new Vector3(0.6f, 0.95f, 0f);

        GameObject wheel = Part(rotor.transform, PrimitiveType.Cylinder, "Rad", Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.9f, 0.06f, 0.9f), steel);
        Part(rotor.transform, PrimitiveType.Cube, "Speiche", new Vector3(0f, 0.3f, 0f), Vector3.zero, new Vector3(0.08f, 0.15f, 0.08f), red);

        // Massiv: Collider fuer Boden, Zylinder und Rad.
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.65f, 0f);
        box.size = new Vector3(2.2f, 1.3f, 1.2f);

        // Dampf-Eingang oben am Zylinder (blaue Achse nach oben).
        GameObject steamPort = MakePort(root.transform, "DampfPort", new Vector3(-0.6f, 1.2f, 0f), Quaternion.Euler(-90f, 0f, 0f),
                                        PortMedium.Steam, PortDirection.Input, 0f);

        // Wellen-Port seitlich am Schwungrad (blaue Achse nach +X).
        GameObject shaftPort = MakePort(root.transform, "WellenPort", new Vector3(1.0f, 0.95f, 0f), Quaternion.Euler(0f, 90f, 0f),
                                        PortMedium.Shaft, PortDirection.Both, 0f);

        ShaftSteamEngine engine = root.AddComponent<ShaftSteamEngine>();
        SetObject(engine, "rotor", rotor.transform);
        SetVector(engine, "rotorAxis", Vector3.right);

        AddCategory(root, "Dampfmaschine (Platzhalter)");
        return root;
    }

    // ---------- Wellengenerator: Wellen-Port zu -X, Strom-Port zu +Z ----------

    private static GameObject BuildGenerator(Material dark, Material green, Material steel)
    {
        GameObject root = new GameObject("ShaftGeneratorPlaceholder");

        Part(root.transform, PrimitiveType.Cube, "Sockel", new Vector3(0f, 0.15f, 0f), Vector3.zero, new Vector3(1.8f, 0.3f, 1.0f), dark);
        Part(root.transform, PrimitiveType.Cylinder, "Gehaeuse", new Vector3(0.1f, 0.7f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.9f, 0.6f, 0.9f), green);

        GameObject rotor = new GameObject("Laeufer");
        rotor.transform.SetParent(root.transform, false);
        rotor.transform.localPosition = new Vector3(-0.65f, 0.7f, 0f);
        Part(rotor.transform, PrimitiveType.Cylinder, "Kupplung", Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.4f, 0.06f, 0.4f), steel);
        Part(rotor.transform, PrimitiveType.Cube, "Marke", new Vector3(0f, 0.18f, 0f), Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f), dark);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.6f, 0f);
        box.size = new Vector3(1.8f, 1.2f, 1.0f);

        MakePort(root.transform, "WellenPort", new Vector3(-0.95f, 0.7f, 0f), Quaternion.Euler(0f, -90f, 0f),
                 PortMedium.Shaft, PortDirection.Both, 0f);
        MakePort(root.transform, "StromPort", new Vector3(0.1f, 0.55f, 0.55f), Quaternion.identity,
                 PortMedium.Power, PortDirection.Both, 0f);

        ShaftGenerator gen = root.AddComponent<ShaftGenerator>();
        SetObject(gen, "rotor", rotor.transform);
        SetVector(gen, "rotorAxis", Vector3.right);

        AddCategory(root, "Wellengenerator (Platzhalter)");
        return root;
    }

    // ---------- Kupplung: Seite A links (-X), Seite B rechts (+X), Schalter oben ----------

    private static GameObject BuildClutch(Material dark, Material steel, Material red)
    {
        GameObject root = new GameObject("ShaftClutchPlaceholder");

        Part(root.transform, PrimitiveType.Cube, "Sockel", new Vector3(0f, 0.15f, 0f), Vector3.zero, new Vector3(1.4f, 0.3f, 0.8f), dark);

        GameObject rotorA = new GameObject("ScheibeA");
        rotorA.transform.SetParent(root.transform, false);
        rotorA.transform.localPosition = new Vector3(-0.3f, 0.65f, 0f);
        Part(rotorA.transform, PrimitiveType.Cylinder, "Scheibe", Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.7f, 0.1f, 0.7f), steel);
        Part(rotorA.transform, PrimitiveType.Cube, "Marke", new Vector3(0f, 0.3f, 0f), Vector3.zero, new Vector3(0.2f, 0.08f, 0.08f), red);

        GameObject rotorB = new GameObject("ScheibeB");
        rotorB.transform.SetParent(root.transform, false);
        rotorB.transform.localPosition = new Vector3(0.3f, 0.65f, 0f);
        Part(rotorB.transform, PrimitiveType.Cylinder, "Scheibe", Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.7f, 0.1f, 0.7f), steel);
        Part(rotorB.transform, PrimitiveType.Cube, "Marke", new Vector3(0f, 0.3f, 0f), Vector3.zero, new Vector3(0.2f, 0.08f, 0.08f), red);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.55f, 0f);
        box.size = new Vector3(1.4f, 1.1f, 0.8f);

        MakePort(root.transform, "WellenPortA", new Vector3(-0.7f, 0.65f, 0f), Quaternion.Euler(0f, -90f, 0f),
                 PortMedium.Shaft, PortDirection.Both, 0f);
        MakePort(root.transform, "WellenPortB", new Vector3(0.7f, 0.65f, 0f), Quaternion.Euler(0f, 90f, 0f),
                 PortMedium.Shaft, PortDirection.Both, 0f);

        AddSwitch(root.transform, new Vector3(0f, 1.1f, 0.25f), red, dark);

        ShaftClutch clutch = root.AddComponent<ShaftClutch>();
        SetObject(clutch, "rotorA", rotorA.transform);
        SetObject(clutch, "rotorB", rotorB.transform);
        SetVector(clutch, "rotorAxis", Vector3.right);

        AddCategory(root, "Wellenkupplung (Platzhalter)");
        return root;
    }

    // ---------- Getriebe: Eingang links (kleines Rad), Ausgang rechts (grosses Rad) = Uebersetzung 0,5 ----------

    private static GameObject BuildGearbox(Material dark, Material steel, Material red)
    {
        GameObject root = new GameObject("ShaftGearboxPlaceholder");

        Part(root.transform, PrimitiveType.Cube, "Sockel", new Vector3(0f, 0.15f, 0f), Vector3.zero, new Vector3(1.7f, 0.3f, 0.9f), dark);
        Part(root.transform, PrimitiveType.Cube, "Gehaeuse", new Vector3(0f, 0.8f, -0.25f), Vector3.zero, new Vector3(1.5f, 1.0f, 0.3f), dark);

        GameObject gearIn = new GameObject("RadEingang");
        gearIn.transform.SetParent(root.transform, false);
        gearIn.transform.localPosition = new Vector3(-0.4f, 0.8f, 0.05f);
        Part(gearIn.transform, PrimitiveType.Cylinder, "Rad", Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.45f, 0.08f, 0.45f), steel);
        Part(gearIn.transform, PrimitiveType.Cube, "Marke", new Vector3(0f, 0.2f, 0f), Vector3.zero, new Vector3(0.12f, 0.06f, 0.06f), red);

        GameObject gearOut = new GameObject("RadAusgang");
        gearOut.transform.SetParent(root.transform, false);
        gearOut.transform.localPosition = new Vector3(0.4f, 0.8f, 0.05f);
        Part(gearOut.transform, PrimitiveType.Cylinder, "Rad", Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.9f, 0.08f, 0.9f), steel);
        Part(gearOut.transform, PrimitiveType.Cube, "Marke", new Vector3(0f, 0.4f, 0f), Vector3.zero, new Vector3(0.12f, 0.06f, 0.06f), red);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.7f, 0f);
        box.size = new Vector3(1.7f, 1.4f, 0.9f);

        GameObject portIn = MakePort(root.transform, "WellenPortEingang", new Vector3(-0.85f, 0.8f, 0.05f), Quaternion.Euler(0f, -90f, 0f),
                                     PortMedium.Shaft, PortDirection.Both, 0f);
        GameObject portOut = MakePort(root.transform, "WellenPortAusgang", new Vector3(0.85f, 0.8f, 0.05f), Quaternion.Euler(0f, 90f, 0f),
                                      PortMedium.Shaft, PortDirection.Both, 0f);

        ShaftGearbox gearbox = root.AddComponent<ShaftGearbox>();
        SetObject(gearbox, "portIn", portIn.GetComponent<Port>());
        SetObject(gearbox, "portOut", portOut.GetComponent<Port>());
        SetObject(gearbox, "rotorIn", gearIn.transform);
        SetObject(gearbox, "rotorOut", gearOut.transform);
        SetVector(gearbox, "rotorAxis", Vector3.right);

        AddCategory(root, "Wellengetriebe (Platzhalter)");
        return root;
    }

    // ---------- Winkelgetriebe: Port A links (-X), Port B vorn (+Z), 90 Grad Richtungswechsel ----------

    private static GameObject BuildAngleGear(Material dark, Material steel, Material red)
    {
        GameObject root = new GameObject("ShaftAngleGearPlaceholder");

        Part(root.transform, PrimitiveType.Cube, "Sockel", new Vector3(0f, 0.1f, 0f), Vector3.zero, new Vector3(1.0f, 0.2f, 1.0f), dark);
        Part(root.transform, PrimitiveType.Cube, "Gehaeuse", new Vector3(0f, 0.6f, 0f), Vector3.zero, new Vector3(0.8f, 0.8f, 0.8f), dark);

        GameObject gearA = new GameObject("RadA");
        gearA.transform.SetParent(root.transform, false);
        gearA.transform.localPosition = new Vector3(-0.3f, 0.6f, 0f);
        Part(gearA.transform, PrimitiveType.Cylinder, "Rad", Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.4f, 0.08f, 0.4f), steel);
        Part(gearA.transform, PrimitiveType.Cube, "Marke", new Vector3(0f, 0.2f, 0f), Vector3.zero, new Vector3(0.1f, 0.06f, 0.06f), red);

        GameObject gearB = new GameObject("RadB");
        gearB.transform.SetParent(root.transform, false);
        gearB.transform.localPosition = new Vector3(0f, 0.6f, 0.3f);
        Part(gearB.transform, PrimitiveType.Cylinder, "Rad", Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(0.4f, 0.08f, 0.4f), steel);
        Part(gearB.transform, PrimitiveType.Cube, "Marke", new Vector3(0f, 0.2f, 0f), Vector3.zero, new Vector3(0.06f, 0.06f, 0.1f), red);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.5f, 0f);
        box.size = new Vector3(1.0f, 1.0f, 1.0f);

        GameObject portA = MakePort(root.transform, "WellenPortA", new Vector3(-0.5f, 0.6f, 0f), Quaternion.Euler(0f, -90f, 0f),
                                    PortMedium.Shaft, PortDirection.Both, 0f);
        GameObject portB = MakePort(root.transform, "WellenPortB", new Vector3(0f, 0.6f, 0.5f), Quaternion.identity,
                                    PortMedium.Shaft, PortDirection.Both, 0f);

        AddSwitch(root.transform, new Vector3(0f, 1.0f, 0f), red, dark);

        InlineDevice device = root.AddComponent<InlineDevice>();
        SetObject(device, "rotorA", gearA.transform);
        SetObject(device, "rotorB", gearB.transform);
        SetVector(device, "rotorAxisA", Vector3.right);
        SetVector(device, "rotorAxisB", Vector3.forward);

        AddCategory(root, "Winkelgetriebe (Platzhalter)");
        return root;
    }

    // Ein Schalter (MachineSwitch) mit Hebel: Trigger zum Anvisieren, Hebel kippt um X.
    private static void AddSwitch(Transform parent, Vector3 pos, Material knob, Material rod)
    {
        GameObject sw = new GameObject("Schalter");
        sw.transform.SetParent(parent, false);
        sw.transform.localPosition = pos;

        BoxCollider trigger = sw.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(0.25f, 0.35f, 0.25f);
        trigger.center = new Vector3(0f, 0.12f, 0f);

        GameObject lever = new GameObject("Hebel");
        lever.transform.SetParent(sw.transform, false);
        Part(lever.transform, PrimitiveType.Cube, "Stange", new Vector3(0f, 0.1f, 0f), Vector3.zero, new Vector3(0.03f, 0.2f, 0.03f), rod);
        Part(lever.transform, PrimitiveType.Sphere, "Knauf", new Vector3(0f, 0.22f, 0f), Vector3.zero, new Vector3(0.07f, 0.07f, 0.07f), knob);

        MachineSwitch machineSwitch = sw.AddComponent<MachineSwitch>();
        SetObject(machineSwitch, "lever", lever.transform);
    }

    // ---------- Hilfen ----------

    private static GameObject MakePort(Transform parent, string name, Vector3 pos, Quaternion rot,
                                       PortMedium medium, PortDirection direction, float flow)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;

        SphereCollider col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.18f;

        Port port = go.AddComponent<Port>();
        port.Configure(medium, direction, flow);
        return go;
    }

    private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler,
                                   Vector3 scale, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    private static void AddCategory(GameObject root, string displayName)
    {
        BuildCategory c = root.AddComponent<BuildCategory>();
        c.categoryId = "power";
        c.displayName = displayName;
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

    private static void SetVector(Object target, string field, Vector3 value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogWarning("Feld '" + field + "' nicht gefunden in " + target.GetType().Name);
            return;
        }

        p.vector3Value = value;
        so.ApplyModifiedPropertiesWithoutUndo();
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
using UnityEngine;

// =====================================================================
//  CabPanelBuilder: baut ein Bedienpult als PLATZHALTER: ein Instrumentenbrett (Geschwindigkeit, Dampfdruck, Wasser im Kessel,
//  Vorrat und Wasser im Tender) und ein Hebelpult (Regler, Fahrtrichtung, Bremse, Pfeife).
//  Wird fuer die Platzhalter-Lok (TrackManager > Cab Controls) und fuer das Prefab aus dem Editor-Menue benutzt.
//
//  Lage: Das Brett haengt an der Fuehrerstand-Vorderwand (Blick nach hinten, -Z), das Pult steht rechts.
//  Es ist auf den Platzhalter-Fuehrerstand der Lok abgestimmt (siehe TrainPlaceholders.Body).
// =====================================================================
public static class CabPanelBuilder
{
    // material: liefert zu einer Farbe ein Material (zur Laufzeit zur Not ein erzeugtes, im Editor ein gespeichertes).
    public static void Build(Transform parent, System.Func<Color, Material> material)
    {
        Material dark = material(new Color(0.12f, 0.12f, 0.13f));
        Material board = material(new Color(0.25f, 0.2f, 0.15f));
        Material white = material(new Color(0.92f, 0.92f, 0.88f));
        Material red = material(new Color(0.75f, 0.12f, 0.1f));
        Material yellow = material(new Color(0.9f, 0.75f, 0.1f));
        Material blue = material(new Color(0.2f, 0.4f, 0.85f));
        Material brown = material(new Color(0.35f, 0.2f, 0.1f));
        Material steel = material(new Color(0.6f, 0.62f, 0.66f));

        // ---------- Instrumentenbrett ----------

        GameObject panel = new GameObject("Instrumentenbrett");
        panel.transform.SetParent(parent, false);
        panel.transform.localPosition = new Vector3(0f, 1.8f, -0.54f);

        Part(panel.transform, PrimitiveType.Cube, "Brett", Vector3.zero, new Vector3(1.3f, 0.5f, 0.04f), board);

        AddDial(panel.transform, "Tacho", new Vector3(-0.45f, 0f, -0.03f), TrainGauge.Source.Speed, 25f, white, dark, red);
        AddDial(panel.transform, "Manometer", new Vector3(-0.15f, 0f, -0.03f), TrainGauge.Source.BoilerPressure, 0f, white, dark, red);

        AddFillGauge(panel.transform, "KesselWasser", new Vector3(0.15f, 0f, -0.03f), TrainGauge.Source.BoilerWater, steel, dark, blue);
        AddFillGauge(panel.transform, "TenderVorrat", new Vector3(0.3f, 0f, -0.03f), TrainGauge.Source.TenderFuel, steel, dark, brown);
        AddFillGauge(panel.transform, "TenderWasser", new Vector3(0.45f, 0f, -0.03f), TrainGauge.Source.TenderWater, steel, dark, blue);

        // ---------- Hebelpult (rechts im Fuehrerstand) ----------

        GameObject console = new GameObject("Hebelpult");
        console.transform.SetParent(parent, false);
        console.transform.localPosition = new Vector3(0.5f, 0f, -1.25f);

        GameObject body = Part(console.transform, PrimitiveType.Cube, "Pult", new Vector3(0f, 0.55f, 0f), new Vector3(0.3f, 1.1f, 0.8f), dark);
        body.AddComponent<BoxCollider>();

        AddLever(console.transform, "HebelRegler", new Vector3(0f, 1.1f, 0.3f), TrainLever.Kind.Throttle, red, dark);
        AddLever(console.transform, "HebelRichtung", new Vector3(0f, 1.1f, 0.1f), TrainLever.Kind.Reverser, yellow, dark);
        AddLever(console.transform, "HebelBremse", new Vector3(0f, 1.1f, -0.1f), TrainLever.Kind.Brake, blue, dark);
        AddLever(console.transform, "ZugPfeife", new Vector3(0f, 1.1f, -0.3f), TrainLever.Kind.Horn, white, dark);
    }

    // Rundes Zeiger-Instrument am Brett: Scheibe, Rand und Zeiger. Der Blick geht nach -Z.
    private static void AddDial(Transform parent, string name, Vector3 pos, TrainGauge.Source source, float maxValue,
                                Material face, Material rim, Material needleMaterial)
    {
        GameObject dial = new GameObject(name);
        dial.transform.SetParent(parent, false);
        dial.transform.localPosition = pos;

        // Zylinder liegen mit der Achse in Z (Drehung 90 Grad um X).
        Part(dial.transform, PrimitiveType.Cylinder, "Rand", Vector3.zero, new Vector3(0.26f, 0.008f, 0.26f), rim, new Vector3(90f, 0f, 0f));
        Part(dial.transform, PrimitiveType.Cylinder, "Scheibe", new Vector3(0f, 0f, -0.006f), new Vector3(0.23f, 0.006f, 0.23f), face, new Vector3(90f, 0f, 0f));

        GameObject pivot = new GameObject("ZeigerPivot");
        pivot.transform.SetParent(dial.transform, false);
        pivot.transform.localPosition = new Vector3(0f, 0f, -0.016f);

        Part(pivot.transform, PrimitiveType.Cube, "Zeiger", new Vector3(0f, 0.05f, 0f), new Vector3(0.012f, 0.1f, 0.004f), needleMaterial);

        TrainGauge gauge = dial.AddComponent<TrainGauge>();
        gauge.source = source;
        gauge.style = TrainGauge.Style.Needle;
        gauge.needle = pivot.transform;
        gauge.needleAxis = Vector3.forward;

        // Von hinten gesehen (Blick nach +Z) laeuft der Zeiger mit negativem Winkel im Uhrzeigersinn.
        gauge.needleMinAngle = 120f;
        gauge.needleMaxAngle = -120f;

        if (maxValue > 0f)
            gauge.maxValue = maxValue;
    }

    // Senkrechtes Anzeigerohr mit wachsender Fuellung.
    private static void AddFillGauge(Transform parent, string name, Vector3 pos, TrainGauge.Source source,
                                     Material frame, Material back, Material fluid)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;

        Part(g.transform, PrimitiveType.Cube, "Rahmen", Vector3.zero, new Vector3(0.07f, 0.34f, 0.01f), frame);
        Part(g.transform, PrimitiveType.Cube, "Grund", new Vector3(0f, 0f, -0.006f), new Vector3(0.045f, 0.3f, 0.006f), back);

        // Fuellung: Quader mit Ursprung in der Mitte; "Pivot At Bottom" aus.
        GameObject fill = Part(g.transform, PrimitiveType.Cube, "Fuellung", new Vector3(0f, 0f, -0.012f), new Vector3(0.03f, 0.28f, 0.006f), fluid);

        TrainGauge gauge = g.AddComponent<TrainGauge>();
        gauge.source = source;
        gauge.style = TrainGauge.Style.Fill;
        gauge.fill = fill.transform;
        gauge.pivotAtBottom = false;
        gauge.fullHeight = 1f;
    }

    // Ein Hebel: Trigger-Box zum Anvisieren, Griff mit Pivot am Fuss (kippt um X, also nach vorn und hinten).
    private static void AddLever(Transform parent, string name, Vector3 pos, TrainLever.Kind kind, Material knob, Material rod)
    {
        GameObject lever = new GameObject(name);
        lever.transform.SetParent(parent, false);
        lever.transform.localPosition = pos;

        BoxCollider trigger = lever.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(0.2f, 0.45f, 0.2f);
        trigger.center = new Vector3(0f, 0.2f, 0f);

        GameObject handle = new GameObject("Griff");
        handle.transform.SetParent(lever.transform, false);

        Part(handle.transform, PrimitiveType.Cube, "Stange", new Vector3(0f, 0.15f, 0f), new Vector3(0.03f, 0.3f, 0.03f), rod);
        Part(handle.transform, PrimitiveType.Sphere, "Knauf", new Vector3(0f, 0.32f, 0f), new Vector3(0.08f, 0.08f, 0.08f), knob);

        TrainLever tl = lever.AddComponent<TrainLever>();
        tl.kind = kind;
        tl.handle = handle.transform;
        tl.handleAxis = Vector3.right;
        tl.minAngle = -35f;
        tl.maxAngle = 35f;
    }

    private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m)
    {
        return Part(parent, type, name, pos, scale, m, Vector3.zero);
    }

    private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m,
                                   Vector3 euler)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;

        Collider c = g.GetComponent<Collider>();

        if (c != null)
            Object.DestroyImmediate(c);

        return g;
    }
}
using UnityEngine;

// =====================================================================
//  TrainPlaceholders: einfache Platzhalter-Modelle aus Wuerfeln und Zylindern.
//  Sie werden benutzt, solange im Lok-Typ (TrackManager > Locomotives) kein eigenes
//  Prefab eingetragen ist. Ein Koerper-Platzhalter fuer alle, dazu je ein Platzhalter
//  fuer Antrieb (Dampf / Diesel / Elektro) und Tender (Kohle / Diesel / Batterie).
//
//  Alle Teile haben ihren Ursprung in der Lok-Mitte auf Schienen-Oberkante, vorne = +Z.
//  Plattform: z = -3,4 ... 2, Oberkante y = 0,5.
// =====================================================================
public static class TrainPlaceholders
{
    private static readonly Color green = new Color(0.12f, 0.35f, 0.2f);
    private static readonly Color black = new Color(0.1f, 0.1f, 0.11f);
    private static readonly Color red = new Color(0.55f, 0.12f, 0.1f);
    private static readonly Color brown = new Color(0.35f, 0.22f, 0.13f);
    private static readonly Color orange = new Color(0.85f, 0.5f, 0.1f);
    private static readonly Color blue = new Color(0.15f, 0.3f, 0.6f);
    private static readonly Color grey = new Color(0.3f, 0.3f, 0.33f);

    // ---------- Koerper: Plattform, Fuehrerstand, Raeder ----------

    public static GameObject Body(bool cabControls = false)
    {
        GameObject root = new GameObject("Lokomotive");

        // Plattform: der Collider, auf dem man steht.
        Prim(PrimitiveType.Cube, "Plattform", root.transform,
             new Vector3(0f, 0.4f, -0.7f), Vector3.zero, new Vector3(1.5f, 0.2f, 5.4f), green, true);

        // Fuehrerstand: vier Pfosten und ein Dach (zum Hineinstellen offen).
        Vector3 postScale = new Vector3(0.08f, 1.6f, 0.08f);
        Prim(PrimitiveType.Cube, "Pfosten1", root.transform, new Vector3(-0.7f, 1.3f, -0.55f), Vector3.zero, postScale, brown, false);
        Prim(PrimitiveType.Cube, "Pfosten2", root.transform, new Vector3(0.7f, 1.3f, -0.55f), Vector3.zero, postScale, brown, false);
        Prim(PrimitiveType.Cube, "Pfosten3", root.transform, new Vector3(-0.7f, 1.3f, -1.9f), Vector3.zero, postScale, brown, false);
        Prim(PrimitiveType.Cube, "Pfosten4", root.transform, new Vector3(0.7f, 1.3f, -1.9f), Vector3.zero, postScale, brown, false);
        Prim(PrimitiveType.Cube, "Dach", root.transform,
             new Vector3(0f, 2.15f, -1.2f), Vector3.zero, new Vector3(1.6f, 0.1f, 1.7f), green, false);

        // Raeder (nur Optik): Achsen laufen quer, Radius 0,3.
        float[] wheelZ = { -1.0f, 0f, 1.0f };

        for (int i = 0; i < wheelZ.Length; i++)
        {
            Prim(PrimitiveType.Cylinder, "RadL" + i, root.transform,
                 new Vector3(-0.55f, 0.3f, wheelZ[i]), new Vector3(0f, 0f, 90f), new Vector3(0.6f, 0.04f, 0.6f), black, false);
            Prim(PrimitiveType.Cylinder, "RadR" + i, root.transform,
                 new Vector3(0.55f, 0.3f, wheelZ[i]), new Vector3(0f, 0f, 90f), new Vector3(0.6f, 0.04f, 0.6f), black, false);
        }

        // Optional: Instrumentenbrett und Hebel im Fuehrerstand (siehe CabPanelBuilder).
        if (cabControls)
            CabPanelBuilder.Build(root.transform, c => TrackManager.MakeMaterial(c, 0.2f));

        return root;
    }

    // ---------- Antriebe ----------

    public static GameObject Engine(FuelType type, Transform parent)
    {
        GameObject engine = new GameObject("Antrieb");
        engine.transform.SetParent(parent, false);

        switch (type)
        {
            case FuelType.Diesel:
                BuildDiesel(engine);
                break;

            case FuelType.Electric:
                BuildElectric(engine);
                break;

            default:
                BuildSteam(engine);
                break;
        }

        return engine;
    }

    private static void BuildSteam(GameObject engine)
    {
        Transform t = engine.transform;

        // Kessel (liegender Zylinder) und Schornstein.
        Prim(PrimitiveType.Cylinder, "Kessel", t,
             new Vector3(0f, 0.95f, 0.8f), new Vector3(90f, 0f, 0f), new Vector3(0.9f, 1.1f, 0.9f), black, false);
        Prim(PrimitiveType.Cylinder, "Schornstein", t,
             new Vector3(0f, 1.65f, 1.55f), Vector3.zero, new Vector3(0.22f, 0.35f, 0.22f), black, false);

        // Feuerbuechse zwischen Kessel und Fuehrerstand (fest, damit man sie anvisieren kann).
        Prim(PrimitiveType.Cube, "Feuerbuechse", t,
             new Vector3(0f, 1.0f, -0.35f), Vector3.zero, new Vector3(1.0f, 1.0f, 0.3f), red, true);

        // Der Kessel selbst: Kohle, Feuer, Wasser, Dampf. Startet mit Wasser, damit man gleich testen kann.
        Boiler boiler = engine.AddComponent<Boiler>();
        boiler.AddWater(100f);

        engine.AddComponent<SteamEngine>();

        // Feuerklappe: Kohle, die man hier hineinwirft oder davor ablegt, kommt ins Feuer.
        GameObject inlet = new GameObject("Feuerklappe");
        inlet.transform.SetParent(t, false);
        inlet.transform.localPosition = new Vector3(0f, 0.85f, -0.65f);

        BoxCollider box = inlet.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(0.7f, 0.7f, 0.35f);

        inlet.AddComponent<CoalInlet>();
    }

    private static void BuildDiesel(GameObject engine)
    {
        Transform t = engine.transform;

        Prim(PrimitiveType.Cube, "Motorhaube", t,
             new Vector3(0f, 0.95f, 0.8f), Vector3.zero, new Vector3(1.1f, 0.9f, 2.2f), orange, true);
        Prim(PrimitiveType.Cylinder, "Auspuff", t,
             new Vector3(0.3f, 1.6f, 1.4f), Vector3.zero, new Vector3(0.12f, 0.25f, 0.12f), black, false);

        engine.AddComponent<DieselEngine>();
    }

    private static void BuildElectric(GameObject engine)
    {
        Transform t = engine.transform;

        Prim(PrimitiveType.Cube, "Motorkasten", t,
             new Vector3(0f, 0.95f, 0.8f), Vector3.zero, new Vector3(1.1f, 0.9f, 2.2f), blue, true);
        Prim(PrimitiveType.Cube, "Stromabnehmer", t,
             new Vector3(0f, 1.5f, 0.8f), Vector3.zero, new Vector3(0.9f, 0.05f, 0.1f), grey, false);

        engine.AddComponent<ElectricEngine>();
    }

    // ---------- Tender ----------

    public static GameObject Tender(FuelType type, Transform parent)
    {
        GameObject tender = new GameObject("Tender");
        tender.transform.SetParent(parent, false);

        switch (type)
        {
            case FuelType.Diesel:
                BuildDieselTender(tender);
                break;

            case FuelType.Electric:
                BuildBatteryTender(tender);
                break;

            default:
                BuildCoalTender(tender);
                break;
        }

        return tender;
    }

    private static void BuildCoalTender(GameObject tender)
    {
        Transform t = tender.transform;

        // Kohlekasten: Boden-Collider zum Anvisieren, Waende ebenfalls fest.
        Prim(PrimitiveType.Cube, "KohleBoden", t,
             new Vector3(0f, 0.55f, -2.7f), Vector3.zero, new Vector3(1.3f, 0.1f, 1.3f), black, true);
        Prim(PrimitiveType.Cube, "KastenL", t,
             new Vector3(-0.65f, 0.85f, -2.7f), Vector3.zero, new Vector3(0.08f, 0.5f, 1.3f), brown, true);
        Prim(PrimitiveType.Cube, "KastenR", t,
             new Vector3(0.65f, 0.85f, -2.7f), Vector3.zero, new Vector3(0.08f, 0.5f, 1.3f), brown, true);
        Prim(PrimitiveType.Cube, "KastenHinten", t,
             new Vector3(0f, 0.85f, -3.35f), Vector3.zero, new Vector3(1.3f, 0.5f, 0.08f), brown, true);
        Prim(PrimitiveType.Cube, "KastenVorne", t,
             new Vector3(0f, 0.85f, -2.05f), Vector3.zero, new Vector3(1.3f, 0.5f, 0.08f), brown, true);

        // Aufnahme-Zone ueber dem Kasten, Haufen auf dem Boden.
        GameObject zoneObject = new GameObject("KohleZone");
        zoneObject.transform.SetParent(t, false);
        zoneObject.transform.localPosition = new Vector3(0f, 0.95f, -2.7f);

        BoxCollider zoneBox = zoneObject.AddComponent<BoxCollider>();
        zoneBox.isTrigger = true;
        zoneBox.size = new Vector3(1.1f, 0.7f, 1.1f);

        GameObject pile = new GameObject("KohleHaufen");
        pile.transform.SetParent(t, false);
        pile.transform.localPosition = new Vector3(0f, 0.6f, -2.7f);

        ItemStorage storage = tender.AddComponent<ItemStorage>();
        storage.zone = zoneBox;
        storage.pileCenter = pile.transform;
        storage.pileSize = new Vector3(1.0f, 0.3f, 1.0f);
        storage.maxItems = 40;
        storage.piecesPerLayer = 10;
        storage.layerHeight = 0.1f;
        storage.takeFrom = t.Find("KohleBoden").GetComponents<Collider>();

        TrainTender trainTender = tender.AddComponent<TrainTender>();
        trainTender.fuelType = FuelType.Coal;
        trainTender.coalStorage = storage;

        // Wassertank (Platzhalter wird beim Start angelegt, siehe TrainTender.EnsureWaterTank).
        trainTender.waterCapacity = 300f;
        trainTender.waterStart = 200f;
    }

    private static void BuildDieselTender(GameObject tender)
    {
        Transform t = tender.transform;

        // Liegender Tank mit Einfuellstutzen.
        Prim(PrimitiveType.Cylinder, "Tank", t,
             new Vector3(0f, 1.0f, -2.5f), new Vector3(90f, 0f, 0f), new Vector3(0.95f, 0.8f, 0.95f), red, true);
        Prim(PrimitiveType.Cylinder, "Stutzen", t,
             new Vector3(0f, 1.52f, -2.5f), Vector3.zero, new Vector3(0.2f, 0.05f, 0.2f), black, false);

        GameObject inlet = new GameObject("Einfuellstutzen");
        inlet.transform.SetParent(t, false);
        inlet.transform.localPosition = new Vector3(0f, 1.7f, -2.5f);

        BoxCollider box = inlet.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(0.6f, 0.5f, 0.6f);
        inlet.AddComponent<TenderFuelInlet>();

        TrainTender trainTender = tender.AddComponent<TrainTender>();
        trainTender.fuelType = FuelType.Diesel;
        trainTender.capacity = 200f;
        trainTender.startAmount = 100f;
    }

    private static void BuildBatteryTender(GameObject tender)
    {
        Transform t = tender.transform;

        Prim(PrimitiveType.Cube, "Batterie", t,
             new Vector3(0f, 0.9f, -2.7f), Vector3.zero, new Vector3(1.2f, 0.8f, 1.3f), grey, true);
        Prim(PrimitiveType.Cube, "Streifen", t,
             new Vector3(0f, 1.0f, -2.7f), Vector3.zero, new Vector3(1.22f, 0.12f, 1.32f), orange, false);

        TrainTender trainTender = tender.AddComponent<TrainTender>();
        trainTender.fuelType = FuelType.Electric;
        trainTender.capacity = 40f;
        trainTender.startAmount = 40f;
    }

    // ---------- Wagen ----------

    // Offener Gueterwagen mit Bodenklappe (Platzhalter).
    // Maasse: Laenge 6,4 m (z = -3,2 ... 3,2), Boden-Oberkante y = 0,5, Waende bis y = 1,3.
    // In der Mitte liegt die Bodenklappe (1 x 1 m): Die Platte hat ihr Gelenk bei z = -0,5 und kippt
    // beim Oeffnen mit der +Z-Seite nach unten. Der Hebel sitzt aussen an der rechten Wand (x = +0,95).
    public static GameObject WagonBody(WagonKind kind)
    {
        GameObject root = new GameObject("Wagen");
        Transform t = root.transform;

        Wagon wagon = root.AddComponent<Wagon>();

        // Fahrwerk: Achsen bei +-2 m, Enden bei +-3,2 m.
        wagon.wheelBase = 4f;
        wagon.frontClearance = 1.2f;
        wagon.rearClearance = 1.2f;
        wagon.riderOffset = new Vector3(0f, 1.2f, 0f);
        wagon.riderHalfExtents = new Vector3(0.9f, 1.2f, 3.0f);

        // Boden: zwei feste Haelften und zwei Seitenstreifen, dazwischen die Oeffnung der Klappe.
        Prim(PrimitiveType.Cube, "BodenHinten", t, new Vector3(0f, 0.45f, -1.85f), Vector3.zero, new Vector3(1.5f, 0.1f, 2.7f), grey, true);
        Prim(PrimitiveType.Cube, "BodenVorne", t, new Vector3(0f, 0.45f, 1.85f), Vector3.zero, new Vector3(1.5f, 0.1f, 2.7f), grey, true);
        Prim(PrimitiveType.Cube, "BodenLinks", t, new Vector3(-0.625f, 0.45f, 0f), Vector3.zero, new Vector3(0.25f, 0.1f, 1f), grey, true);
        Prim(PrimitiveType.Cube, "BodenRechts", t, new Vector3(0.625f, 0.45f, 0f), Vector3.zero, new Vector3(0.25f, 0.1f, 1f), grey, true);

        // Waende (fest, damit man den Haufen anvisieren kann).
        Prim(PrimitiveType.Cube, "WandL", t, new Vector3(-0.75f, 0.9f, 0f), Vector3.zero, new Vector3(0.08f, 0.8f, 6.4f), brown, true);
        Prim(PrimitiveType.Cube, "WandR", t, new Vector3(0.75f, 0.9f, 0f), Vector3.zero, new Vector3(0.08f, 0.8f, 6.4f), brown, true);
        Prim(PrimitiveType.Cube, "WandVorne", t, new Vector3(0f, 0.9f, 3.16f), Vector3.zero, new Vector3(1.5f, 0.8f, 0.08f), brown, true);
        Prim(PrimitiveType.Cube, "WandHinten", t, new Vector3(0f, 0.9f, -3.16f), Vector3.zero, new Vector3(1.5f, 0.8f, 0.08f), brown, true);

        // Raeder (nur Optik).
        float[] wheelZ = { -2f, -1.4f, 1.4f, 2f };

        for (int i = 0; i < wheelZ.Length; i++)
        {
            Prim(PrimitiveType.Cylinder, "RadL" + i, t, new Vector3(-0.55f, 0.3f, wheelZ[i]), new Vector3(0f, 0f, 90f), new Vector3(0.6f, 0.04f, 0.6f), black, false);
            Prim(PrimitiveType.Cylinder, "RadR" + i, t, new Vector3(0.55f, 0.3f, wheelZ[i]), new Vector3(0f, 0f, 90f), new Vector3(0.6f, 0.04f, 0.6f), black, false);
        }

        // Bodenklappe: ein Gelenk-Objekt, daran die Platte (Collider bleibt, wird beim Oeffnen abgeschaltet).
        GameObject hinge = new GameObject("KlappeGelenk");
        hinge.transform.SetParent(t, false);
        hinge.transform.localPosition = new Vector3(0f, 0.45f, -0.5f);

        Prim(PrimitiveType.Cube, "Klappe", hinge.transform, new Vector3(0f, 0f, 0.5f), Vector3.zero, new Vector3(1f, 0.08f, 1f), red, true);
        Collider plateCollider = hinge.transform.Find("Klappe").GetComponent<Collider>();

        // Punkt unter der Klappe, an dem die Stuecke herausfallen.
        GameObject drop = new GameObject("KlappeAuswurf");
        drop.transform.SetParent(t, false);
        drop.transform.localPosition = new Vector3(0f, 0.2f, 0f);

        wagon.hatchPlate = hinge.transform;
        wagon.hatchCollider = plateCollider;
        wagon.hatchDrop = drop.transform;

        // Hebel aussen an der rechten Wand.
        GameObject lever = new GameObject("KlappenHebel");
        lever.transform.SetParent(t, false);
        lever.transform.localPosition = new Vector3(0.95f, 0.85f, 0f);

        BoxCollider leverBox = lever.AddComponent<BoxCollider>();
        leverBox.isTrigger = true;
        leverBox.size = new Vector3(0.4f, 0.7f, 0.4f);

        Prim(PrimitiveType.Cube, "HebelStange", lever.transform, new Vector3(0f, 0.2f, 0f), Vector3.zero, new Vector3(0.06f, 0.5f, 0.06f), orange, false);

        CarHatchLever hatchLever = lever.AddComponent<CarHatchLever>();
        hatchLever.wagon = wagon;
        hatchLever.handle = lever.transform.Find("HebelStange");

        // Ladezone: Trigger-Box ueber dem Boden, Haufen auf dem Boden. Nimmt alle Arten Gegenstaende an.
        GameObject zoneObject = new GameObject("LadeZone");
        zoneObject.transform.SetParent(t, false);
        zoneObject.transform.localPosition = new Vector3(0f, 1.0f, 0f);

        BoxCollider zoneBox = zoneObject.AddComponent<BoxCollider>();
        zoneBox.isTrigger = true;
        zoneBox.size = new Vector3(1.3f, 1.0f, 5.8f);

        GameObject pile = new GameObject("LadungHaufen");
        pile.transform.SetParent(t, false);
        pile.transform.localPosition = new Vector3(0f, 0.52f, 0f);

        ItemStorage storage = root.AddComponent<ItemStorage>();
        storage.zone = zoneBox;
        storage.pileCenter = pile.transform;
        storage.pileSize = new Vector3(1.3f, 0.6f, 5.6f);
        storage.accepts = new ItemKind[] { ItemKind.Coal, ItemKind.Ore, ItemKind.Ingot, ItemKind.Slag };
        storage.maxItems = 120;
        storage.piecesPerLayer = 30;
        storage.layerHeight = 0.12f;

        wagon.storage = storage;
        wagon.RefreshParts();

        return root;
    }

    // ---------- Hilfe ----------

    private static void Prim(PrimitiveType type, string name, Transform parent,
                             Vector3 pos, Vector3 euler, Vector3 scale, Color color, bool keepCollider)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;

        g.GetComponent<Renderer>().sharedMaterial = TrackManager.MakeMaterial(color, 0.2f);

        if (!keepCollider)
        {
            Collider c = g.GetComponent<Collider>();

            if (c != null)
                Object.DestroyImmediate(c);
        }
    }
}
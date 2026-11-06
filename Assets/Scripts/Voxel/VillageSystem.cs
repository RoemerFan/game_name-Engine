using System.Collections.Generic;
using UnityEngine;

// Ein Gebaeude-Typ, aus dem Doerfer bestehen. Im Inspector aenderbar.
// Ohne Prefab wird ein einfacher Platzhalter (Quader mit Dach) gebaut.
[System.Serializable]
public class VillageBuildingType
{
    public string name = "House";

    [Tooltip("Grundflaeche in Metern: X = Breite entlang der Strasse, Y = Tiefe. Die Vorderseite (Tuer) soll in Richtung +Z des Prefabs zeigen.")]
    public Vector2 footprint = new Vector2(7f, 6f);

    [Tooltip("Wie oft dieser Typ vorkommt (relativ zu den anderen). 0 = wird nie zufaellig gewaehlt.")]
    public float weight = 1f;

    [Tooltip("Prefabs dieses Typs (Ursprung am Boden, Mitte der Grundflaeche). Mehrere = zufaellige Auswahl. Leer = Platzhalter.")]
    public GameObject[] prefabs;

    [Header("Platzhalter (wenn kein Prefab)")]
    public float fallbackHeight = 3.5f;
    public Color fallbackColor = new Color(0.72f, 0.62f, 0.48f);
}

// Plan eines einzelnen Teils eines Dorfes (Gebaeude, Strassenstueck oder Schild).
public class VillagePart
{
    public int type;          // Index in buildingTypes, oder PartStreet / PartSign
    public Vector3 pos;       // Mitte der Grundflaeche (Hoehe = Dorfniveau)
    public float yaw;
    public Vector2 size;      // Grundflaeche
    public int variant;       // Zufallszahl fuer die Prefab-Auswahl
}

// Der komplette, aus dem Seed berechnete Plan eines Dorfes.
public class VillagePlan
{
    public int id;
    public string name;
    public Vector3 center;     // Mitte (y = Niveau des Dorfes)
    public float yaw;          // Richtung der Hauptstrasse
    public float level;        // Hoehe, auf die alles eingeebnet wird
    public float streetLength;
    public float radius;       // alles liegt innerhalb dieses Radius um die Mitte
    public List<VillagePart> parts = new List<VillagePart>();
    public string[] produces;   // Namen aus VillageSystem > Goods
    public string[] needs;
}

// Zufaellige, aber vom Seed abhaengige Doerfer.
//
// So funktioniert es:
//  - Die Welt wird in Regionen eingeteilt (z. B. 220 x 220 m). Aus Seed + Regionsnummer wird ein
//    Zufallswert berechnet: Er entscheidet, OB in der Region ein Dorf liegt und WIE es aussieht.
//    Gleicher Seed = gleiche Doerfer, auf jedem Rechner (auch im Mehrspieler).
//  - Das Gelaende wird als "Generator-Operation" eingeebnet. Das passiert, noch bevor die Spalte
//    des Gelaendes entsteht, und wird nicht extra gespeichert.
//  - Die Gebaeude werden erst gebaut, wenn der Spieler nah genug ist und der Boden fertig ist,
//    und wieder entfernt, wenn er sich entfernt. Aenderungen der Spieler am Gelaende bleiben erhalten.
//
// EINRICHTUNG: Dieses Skript an ein beliebiges Objekt in der Szene haengen (z. B. das der VoxelWorld).
public class VillageSystem : MonoBehaviour
{
    public const int PartStreet = -1;
    public const int PartSign = -2;

    [Header("Verteilung")]
    [Tooltip("Kantenlaenge einer Region in Metern. In jeder Region liegt hoechstens ein Dorf.")]
    public float regionSizeMeters = 220f;
    [Range(0f, 1f)]
    [Tooltip("Wahrscheinlichkeit, dass eine Region ein Dorf bekommt.")]
    public float villageChance = 0.45f;
    [Tooltip("Mindestabstand zum Startpunkt der Welt in Metern.")]
    public float minDistanceFromSpawn = 30f;
    [Tooltip("Hoechster erlaubter Hoehenunterschied am Dorfplatz (Meter). Zu huegelige Stellen bekommen kein Dorf.")]
    public float maxHeightRange = 4f;

    [Header("Groesse")]
    public int minBuildings = 4;
    public int maxBuildings = 10;
    public float streetWidth = 5f;
    [Tooltip("Groesster Radius eines Dorfes um seine Mitte. Die Regionsgroesse muss mindestens das Doppelte sein.")]
    public float maxVillageRadius = 70f;

    [Header("Gelaende")]
    [Tooltip("Breite des weichen Uebergangs am Rand der eingeebneten Flaechen.")]
    public float flattenBlend = 2.5f;
    [Tooltip("So weit wird um das Dorf herum die Erde an die Umgebung angepasst (Boeschung).")]
    public float backfillReach = 6f;
    public float backfillSlope = 0.9f;

    [Header("Namen und Waren")]
    [Tooltip("Erste Namenshaelften (Stein, Eich ...). Ein Dorfname = zufaellig eine hier + eine aus 'Name Suffixes'.")]
    public List<string> namePrefixes = new List<string>
    {
        "Stein", "Eich", "Kohl", "Berg", "Wald", "Muehl", "Bach", "Eisen", "Linden", "Birken",
        "Hohen", "Nieder", "Ober", "Rot", "Gruen", "Alt", "Neu", "Wolfs", "Fuchs", "Buchen"
    };
    [Tooltip("Zweite Namenshaelften (dorf, heim ...).")]
    public List<string> nameSuffixes = new List<string>
    {
        "dorf", "heim", "hausen", "bach", "hof", "feld", "tal", "berg", "bruck", "stadt", "au", "hagen"
    };
    [Tooltip("Waren, die Doerfer herstellen oder brauchen (Grundlage fuer Auftraege). Mindestens 3 eintragen.")]
    public List<string> goods = new List<string>
    {
        "Coal", "IronOre", "IronIngots", "Wood", "Food", "Tools"
    };

    [Header("Gebaeude")]
    [Tooltip("Der letzte Eintrag mit Gewicht 0 wird als Brunnen am Ende der Strasse benutzt (falls vorhanden).")]
    public List<VillageBuildingType> buildingTypes = new List<VillageBuildingType>
    {
        new VillageBuildingType { name = "House",    footprint = new Vector2(7f, 6f),   weight = 5f, fallbackHeight = 3.5f, fallbackColor = new Color(0.78f, 0.68f, 0.52f) },
        new VillageBuildingType { name = "BigHouse", footprint = new Vector2(9f, 7f),   weight = 2f, fallbackHeight = 5f,   fallbackColor = new Color(0.70f, 0.55f, 0.45f) },
        new VillageBuildingType { name = "Shop",     footprint = new Vector2(8f, 6f),   weight = 1f, fallbackHeight = 3.5f, fallbackColor = new Color(0.60f, 0.66f, 0.72f) },
        new VillageBuildingType { name = "Barn",     footprint = new Vector2(11f, 8f),  weight = 1f, fallbackHeight = 5f,   fallbackColor = new Color(0.55f, 0.30f, 0.25f) },
        new VillageBuildingType { name = "Well",     footprint = new Vector2(2.6f, 2.6f), weight = 0f, fallbackHeight = 1.2f, fallbackColor = new Color(0.55f, 0.55f, 0.55f) }
    };
    [Tooltip("Zusaetzliche Drehung der Prefabs (Grad), falls ihre Vorderseite nicht nach +Z zeigt.")]
    public float prefabYawOffset = 0f;
    [Tooltip("Material fuer Platzhalter. Leer = einfaches Standard-Material.")]
    public Material fallbackMaterial;
    public Color streetColor = new Color(0.38f, 0.35f, 0.31f);

    [Header("Aufbau im Spiel")]
    [Tooltip("Gebaeude im Umkreis von so vielen Metern um den Spieler werden aufgebaut.")]
    public float spawnRadius = 130f;
    [Tooltip("Gebaeude weiter weg als spawnRadius + dieser Wert werden wieder entfernt.")]
    public float despawnMargin = 40f;

    public static VillageSystem Instance { get; private set; }

    VoxelWorld world;

    // Regionen, die schon geplant sind (Wert null = kein Dorf).
    readonly Dictionary<long, VillagePlan> plans = new Dictionary<long, VillagePlan>();
    readonly List<VillagePlan> known = new List<VillagePlan>();

    // Aufgebaute Teile je Dorf (gleicher Index wie VillagePlan.parts).
    readonly Dictionary<int, GameObject[]> spawnedParts = new Dictionary<int, GameObject[]>();
    readonly Dictionary<int, GameObject> roots = new Dictionary<int, GameObject>();

    readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

    float nextTick;

    // Alle bisher bekannten (geplanten) Doerfer.
    public IList<VillagePlan> KnownVillages { get { return known; } }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        world = VoxelWorld.Instance;

        if (world == null)
        {
            Debug.LogWarning("VillageSystem: keine VoxelWorld gefunden.");
            enabled = false;
            return;
        }

        // Bevor eine Spalte des Gelaendes entsteht, werden die Doerfer in ihrer Naehe geplant.
        world.ColumnGenerating += OnColumnGenerating;
    }

    void OnDestroy()
    {
        if (world != null)
            world.ColumnGenerating -= OnColumnGenerating;

        if (Instance == this)
            Instance = null;
    }

    // =====================================================================
    //  Planung (rein aus dem Seed)
    // =====================================================================

    static int Hash(int seed, int a, int b)
    {
        unchecked
        {
            int h = seed;
            h = h * 73856093 ^ a * 19349663 ^ b * 83492791;
            h ^= h >> 13;
            h *= 1274126177;
            h ^= h >> 16;
            return h;
        }
    }

    static long RegionKey(int rx, int rz)
    {
        return ((long)rx << 32) | (uint)rz;
    }

    // Wird kurz vor dem Erzeugen einer Gelaende-Spalte aufgerufen.
    void OnColumnGenerating(Vector3 min, Vector3 max)
    {
        // Der Rand muss auch Boeschung und Uebergang abdecken, die ueber den Dorfradius hinausreichen.
        EnsureRegionsIn(min, max, maxVillageRadius + backfillReach + flattenBlend + 5f);
    }

    // Plant alle Regionen, die einen Bereich (plus Rand) beruehren.
    void EnsureRegionsIn(Vector3 min, Vector3 max, float expand)
    {
        Vector3 o = world.transform.position;

        int rx0 = Mathf.FloorToInt((min.x - expand - o.x) / regionSizeMeters);
        int rx1 = Mathf.FloorToInt((max.x + expand - o.x) / regionSizeMeters);
        int rz0 = Mathf.FloorToInt((min.z - expand - o.z) / regionSizeMeters);
        int rz1 = Mathf.FloorToInt((max.z + expand - o.z) / regionSizeMeters);

        for (int rz = rz0; rz <= rz1; rz++)
            for (int rx = rx0; rx <= rx1; rx++)
                EnsureRegion(rx, rz);
    }

    void EnsureRegion(int rx, int rz)
    {
        long key = RegionKey(rx, rz);

        if (plans.ContainsKey(key))
            return;

        VillagePlan plan = BuildPlan(rx, rz);
        plans[key] = plan;

        if (plan == null)
            return;

        known.Add(plan);
        RegisterTerrain(plan);
    }

    // Alle Doerfer im Umkreis (auch noch nicht besuchte). Fuer Auftraege, Karte, Eisenbahn ...
    public List<VillagePlan> FindVillagesAround(Vector3 pos, float radius)
    {
        List<VillagePlan> result = new List<VillagePlan>();

        if (world == null)
            return result;

        EnsureRegionsIn(pos - new Vector3(radius, 0f, radius), pos + new Vector3(radius, 0f, radius), 0f);

        foreach (VillagePlan p in known)
        {
            Vector3 d = p.center - pos;
            d.y = 0f;

            if (d.magnitude <= radius)
                result.Add(p);
        }

        result.Sort((a, b) =>
            (a.center - pos).sqrMagnitude.CompareTo((b.center - pos).sqrMagnitude));

        return result;
    }

    VillagePlan BuildPlan(int rx, int rz)
    {
        int regionHash = Hash(world.seed, rx, rz);
        System.Random rnd = new System.Random(regionHash);

        if (rnd.NextDouble() > villageChance)
            return null;

        Vector3 origin = world.transform.position;

        // Der Mittelpunkt liegt so weit vom Regionsrand weg, dass sich zwei Doerfer nie beruehren.
        float margin = Mathf.Min(maxVillageRadius, regionSizeMeters * 0.45f);
        float span = regionSizeMeters - 2f * margin;

        // Mehrere Kandidaten ausprobieren und den ebensten nehmen.
        Vector3 bestCenter = Vector3.zero;
        float bestRange = float.MaxValue;
        float bestLevel = 0f;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            float x = origin.x + rx * regionSizeMeters + margin + (float)rnd.NextDouble() * span;
            float z = origin.z + rz * regionSizeMeters + margin + (float)rnd.NextDouble() * span;

            Vector3 c = new Vector3(x, 0f, z);

            if (!world.IsInsideWorld(c, maxVillageRadius * 0.5f + 20f))
                continue;

            Vector3 spawn = world.SpawnPoint();
            Vector3 toSpawn = c - spawn;
            toSpawn.y = 0f;

            if (toSpawn.magnitude < minDistanceFromSpawn + maxVillageRadius * 0.5f)
                continue;

            // Hoehen auf einem Raster (7 x 7) um den Mittelpunkt messen.
            List<float> heights = new List<float>();
            float min = float.MaxValue, max = float.MinValue;

            for (int i = -3; i <= 3; i++)
            {
                for (int j = -3; j <= 3; j++)
                {
                    float h = world.GetNaturalHeight(x + i * 10f, z + j * 10f);
                    heights.Add(h);

                    if (h < min) min = h;
                    if (h > max) max = h;
                }
            }

            float range = max - min;

            if (range < bestRange)
            {
                heights.Sort();

                bestRange = range;
                bestCenter = c;

                // Mittlere Hoehe (Median), auf 5 cm gerundet.
                bestLevel = Mathf.Round(heights[heights.Count / 2] / 0.05f) * 0.05f;
            }
        }

        if (bestRange > maxHeightRange)
            return null;

        VillagePlan plan = new VillagePlan();
        plan.id = regionHash;
        plan.center = new Vector3(bestCenter.x, bestLevel, bestCenter.z);
        plan.level = bestLevel;
        plan.yaw = rnd.Next(0, 24) * 15f;
        plan.name = MakeName(rnd);

        LayoutVillage(plan, rnd);
        PickGoods(plan, rnd);

        return plan;
    }

    // Verteilt Strasse, Gebaeude und Schild.
    void LayoutVillage(VillagePlan plan, System.Random rnd)
    {
        Quaternion rot = Quaternion.Euler(0f, plan.yaw, 0f);
        Vector3 right = rot * Vector3.right;     // entlang der Hauptstrasse
        Vector3 across = rot * Vector3.forward;  // quer dazu

        int count = rnd.Next(minBuildings, maxBuildings + 1);
        float maxLength = 2f * (maxVillageRadius - 14f);

        float setback = 2.5f;

        // Zwei Seiten der Strasse; jedes Haus kommt auf die Seite mit weniger Platzverbrauch.
        float[] cursor = { 0f, 0f };

        List<VillagePart> buildings = new List<VillagePart>();
        List<float> alongList = new List<float>();

        for (int i = 0; i < count; i++)
        {
            int type = PickType(rnd);

            if (type < 0)
                break;

            Vector2 fp = buildingTypes[type].footprint;

            int side = cursor[0] <= cursor[1] ? 0 : 1;

            if (cursor[side] + fp.x > maxLength)
            {
                side = 1 - side;

                if (cursor[side] + fp.x > maxLength)
                    break;
            }

            float along = cursor[side] + fp.x * 0.5f;
            cursor[side] += fp.x + 2f + (float)rnd.NextDouble() * 3f;

            float sideSign = side == 0 ? 1f : -1f;
            float offset = sideSign * (streetWidth * 0.5f + setback + fp.y * 0.5f);

            VillagePart b = new VillagePart();
            b.type = type;
            b.size = fp;
            b.variant = rnd.Next();

            // Die Vorderseite (+Z) zeigt zur Strasse.
            b.yaw = plan.yaw + (side == 0 ? 180f : 0f);
            b.pos = new Vector3(0f, plan.level, offset);   // x wird gleich gesetzt

            buildings.Add(b);
            alongList.Add(along);
        }

        float length = Mathf.Max(cursor[0], cursor[1]) + 6f;
        length = Mathf.Max(length, 20f);

        plan.streetLength = length;

        // Strasse und Gebaeude um die Mitte zentrieren.
        for (int i = 0; i < buildings.Count; i++)
        {
            float a = alongList[i] - length * 0.5f + 3f;
            float o = buildings[i].pos.z;

            buildings[i].pos = plan.center + right * a + across * o;
            buildings[i].pos.y = plan.level;
        }

        // Strasse in Stuecken von etwa 8 m (so werden sie einzeln aufgebaut).
        int segments = Mathf.Max(1, Mathf.CeilToInt(length / 8f));
        float segLength = length / segments;

        for (int i = 0; i < segments; i++)
        {
            VillagePart s = new VillagePart();
            s.type = PartStreet;
            s.size = new Vector2(segLength, streetWidth);
            s.yaw = plan.yaw;
            s.pos = plan.center + right * (-length * 0.5f + segLength * (i + 0.5f));
            s.pos.y = plan.level;

            plan.parts.Add(s);
        }

        plan.parts.AddRange(buildings);

        // Brunnen am Ende der Strasse (Typ mit Gewicht 0, falls vorhanden).
        int well = FindWellType();

        if (well >= 0)
        {
            VillagePart w = new VillagePart();
            w.type = well;
            w.size = buildingTypes[well].footprint;
            w.yaw = plan.yaw;
            w.variant = rnd.Next();
            w.pos = plan.center + right * (length * 0.5f + 3.5f);
            w.pos.y = plan.level;

            plan.parts.Add(w);
        }

        // Schild am Anfang der Strasse.
        VillagePart sign = new VillagePart();
        sign.type = PartSign;
        sign.size = new Vector2(1.5f, 1.5f);
        sign.yaw = plan.yaw;
        sign.pos = plan.center + right * (-length * 0.5f - 2f) + across * (streetWidth * 0.5f + 1.2f);
        sign.pos.y = plan.level;

        plan.parts.Add(sign);

        // Alles liegt innerhalb dieses Radius.
        plan.radius = length * 0.5f + 12f;
    }

    // Waehlt einen Gebaeude-Typ nach Gewicht. -1 = keiner vorhanden.
    int PickType(System.Random rnd)
    {
        float total = 0f;

        foreach (VillageBuildingType t in buildingTypes)
            total += Mathf.Max(0f, t.weight);

        if (total <= 0f)
            return -1;

        float roll = (float)rnd.NextDouble() * total;

        for (int i = 0; i < buildingTypes.Count; i++)
        {
            roll -= Mathf.Max(0f, buildingTypes[i].weight);

            if (roll <= 0f && buildingTypes[i].weight > 0f)
                return i;
        }

        for (int i = buildingTypes.Count - 1; i >= 0; i--)
        {
            if (buildingTypes[i].weight > 0f)
                return i;
        }

        return -1;
    }

    int FindWellType()
    {
        for (int i = buildingTypes.Count - 1; i >= 0; i--)
        {
            if (buildingTypes[i].weight <= 0f)
                return i;
        }

        return -1;
    }

    string MakeName(System.Random rnd)
    {
        if (namePrefixes == null || namePrefixes.Count == 0 || nameSuffixes == null || nameSuffixes.Count == 0)
            return "Dorf" + rnd.Next(100, 999);

        return namePrefixes[rnd.Next(namePrefixes.Count)] + nameSuffixes[rnd.Next(nameSuffixes.Count)];
    }

    // Was das Dorf herstellt und was es braucht (stabil pro Seed; Grundlage fuer Auftraege).
    void PickGoods(VillagePlan plan, System.Random rnd)
    {
        // Ohne (genug) Waren im Inspector gibt es keine Wirtschaftsdaten.
        if (goods == null || goods.Count < 2)
        {
            plan.produces = new string[0];
            plan.needs = new string[0];
            return;
        }

        List<int> pool = new List<int>();

        for (int i = 0; i < goods.Count; i++)
            pool.Add(i);

        // Mischen
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            int tmp = pool[i];
            pool[i] = pool[j];
            pool[j] = tmp;
        }

        // Bei wenigen Waren: so viele nehmen, wie es gibt (mindestens eine pro Seite).
        int p = Mathf.Min(rnd.Next(1, 3), pool.Count - 1);
        int n = Mathf.Min(rnd.Next(1, 3), pool.Count - p);

        plan.produces = new string[p];
        plan.needs = new string[n];

        for (int i = 0; i < p; i++)
            plan.produces[i] = goods[pool[i]];

        for (int i = 0; i < n; i++)
            plan.needs[i] = goods[pool[p + i]];
    }

    // Meldet das Einebnen bei der Welt an: erst alle Flaechen, danach die Boeschungen
    // (damit die Boeschungen nicht in eingeebnete Nachbarflaechen hineinarbeiten).
    void RegisterTerrain(VillagePlan plan)
    {
        List<VillagePart> flat = new List<VillagePart>();

        foreach (VillagePart part in plan.parts)
        {
            if (part.type != PartSign)
                flat.Add(part);
        }

        foreach (VillagePart part in flat)
        {
            // Etwas grosszuegiger als das Gebaeude (Vorplatz).
            Vector2 half = part.size * 0.5f + new Vector2(1f, 1f);

            if (part.type == PartStreet)
                half = new Vector2(part.size.x * 0.5f + 0.5f, part.size.y * 0.5f + 1f);

            world.AddGeneratorFlatten(part.pos, half, part.yaw, plan.level, flattenBlend);
        }

        foreach (VillagePart part in flat)
        {
            Vector2 half = part.size * 0.5f + new Vector2(1f, 1f);

            if (part.type == PartStreet)
                half = new Vector2(part.size.x * 0.5f + 0.5f, part.size.y * 0.5f + 1f);

            world.AddGeneratorBackfill(part.pos, half, part.yaw, plan.level, backfillReach, backfillSlope);
        }
    }

    // =====================================================================
    //  Aufbau im Spiel
    // =====================================================================

    void Update()
    {
        if (world == null || !world.IsReady || Time.unscaledTime < nextTick)
            return;

        nextTick = Time.unscaledTime + 0.4f;

        if (world.player == null || !world.player.gameObject.activeInHierarchy)
            return;

        Vector3 pp = world.player.position;

        // Nur Doerfer in der Naehe beachten.
        for (int i = 0; i < known.Count; i++)
        {
            VillagePlan plan = known[i];

            Vector3 dc = plan.center - pp;
            dc.y = 0f;

            if (dc.magnitude > spawnRadius + plan.radius + despawnMargin + 20f)
            {
                if (spawnedParts.ContainsKey(plan.id))
                    DespawnAll(plan);

                continue;
            }

            UpdateVillage(plan, pp);
        }

        // Doerfer in Reichweite planen (falls die Gegend noch nicht geladen wurde).
        EnsureRegionsIn(pp, pp, spawnRadius + maxVillageRadius);
    }

    void UpdateVillage(VillagePlan plan, Vector3 playerPos)
    {
        GameObject[] spawned;

        if (!spawnedParts.TryGetValue(plan.id, out spawned))
        {
            spawned = new GameObject[plan.parts.Count];
            spawnedParts[plan.id] = spawned;
        }

        for (int i = 0; i < plan.parts.Count; i++)
        {
            VillagePart part = plan.parts[i];

            Vector3 d = part.pos - playerPos;
            d.y = 0f;
            float dist = d.magnitude;

            if (spawned[i] == null)
            {
                // Erst bauen, wenn der Boden darunter fertig ist.
                if (dist < spawnRadius && world.IsMeshedAround(part.pos, 1.5f))
                    spawned[i] = SpawnPart(plan, part);
            }
            else if (dist > spawnRadius + despawnMargin)
            {
                Destroy(spawned[i]);
                spawned[i] = null;
            }
        }
    }

    void DespawnAll(VillagePlan plan)
    {
        GameObject[] spawned;

        if (spawnedParts.TryGetValue(plan.id, out spawned))
        {
            for (int i = 0; i < spawned.Length; i++)
            {
                if (spawned[i] != null)
                    Destroy(spawned[i]);
            }
        }

        spawnedParts.Remove(plan.id);

        GameObject root;

        if (roots.TryGetValue(plan.id, out root))
        {
            if (root != null)
                Destroy(root);

            roots.Remove(plan.id);
        }
    }

    // Wurzelobjekt des Dorfes (mit VillageInfo) - entsteht beim ersten gebauten Teil.
    GameObject GetRoot(VillagePlan plan)
    {
        GameObject root;

        if (roots.TryGetValue(plan.id, out root) && root != null)
            return root;

        root = new GameObject("Village_" + plan.name);
        root.transform.SetParent(transform, false);
        root.transform.position = plan.center;

        VillageInfo info = root.AddComponent<VillageInfo>();
        info.plan = plan;

        roots[plan.id] = root;

        return root;
    }

    GameObject SpawnPart(VillagePlan plan, VillagePart part)
    {
        GameObject root = GetRoot(plan);

        // Der Boden wurde auf "level - FlattenSink" eingeebnet; ein paar cm tiefer stehen,
        // damit zwischen Gebaeude und Boden kein Spalt bleibt.
        Vector3 pos = new Vector3(part.pos.x, plan.level - VoxelWorld.FlattenSink - 0.03f, part.pos.z);

        GameObject go;

        if (part.type == PartStreet)
            go = BuildStreet(part);
        else if (part.type == PartSign)
            go = BuildSign(plan);
        else
            go = BuildBuilding(part);

        go.transform.SetParent(root.transform, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, part.yaw + (part.type >= 0 ? prefabYawOffset : 0f), 0f);

        // Haelt den Boden unter dem Objekt geladen, solange es steht.
        VoxelWorld.AddAnchor(go.transform, 1);

        return go;
    }

    GameObject BuildBuilding(VillagePart part)
    {
        VillageBuildingType type = buildingTypes[part.type];

        if (type.prefabs != null && type.prefabs.Length > 0)
        {
            GameObject prefab = type.prefabs[Mathf.Abs(part.variant) % type.prefabs.Length];

            if (prefab != null)
            {
                GameObject instance = Instantiate(prefab);
                instance.name = type.name;
                return instance;
            }
        }

        // Platzhalter: Quader mit etwas ueberstehendem Dach.
        GameObject root = new GameObject(type.name);

        float w = type.footprint.x;
        float d = type.footprint.y;
        float h = type.fallbackHeight;

        bool isWell = type.weight <= 0f;

        if (isWell)
        {
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.transform.SetParent(root.transform, false);
            shaft.transform.localScale = new Vector3(w * 0.8f, h * 0.5f, d * 0.8f);
            shaft.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            SetColor(shaft, type.fallbackColor);
            return root;
        }

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(w, h, d);
        body.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
        SetColor(body, type.fallbackColor);

        GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "Roof";
        roof.transform.SetParent(root.transform, false);
        roof.transform.localScale = new Vector3(w + 0.8f, 0.35f, d + 0.8f);
        roof.transform.localPosition = new Vector3(0f, h + 0.17f, 0f);
        SetColor(roof, type.fallbackColor * 0.55f);

        return root;
    }

    GameObject BuildStreet(VillagePart part)
    {
        // Flache Platte knapp ueber dem Boden (begehbar).
        GameObject s = GameObject.CreatePrimitive(PrimitiveType.Cube);
        s.name = "Street";
        s.transform.localScale = new Vector3(part.size.x, 0.12f, part.size.y);

        SetColor(s, streetColor);

        // Die Platte soll mit ihrer Oberkante knapp ueber dem Boden liegen.
        GameObject holder = new GameObject("StreetPart");
        s.transform.SetParent(holder.transform, false);
        s.transform.localPosition = new Vector3(0f, 0.09f, 0f);

        return holder;
    }

    GameObject BuildSign(VillagePlan plan)
    {
        GameObject root = new GameObject("Sign");

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pole.transform.SetParent(root.transform, false);
        pole.transform.localScale = new Vector3(0.15f, 2.2f, 0.15f);
        pole.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        SetColor(pole, new Color(0.35f, 0.25f, 0.15f));

        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.transform.SetParent(root.transform, false);
        board.transform.localScale = new Vector3(0.1f, 0.6f, 2.0f);
        board.transform.localPosition = new Vector3(0f, 1.9f, 0f);
        SetColor(board, new Color(0.5f, 0.38f, 0.22f));

        // Beschriftung auf beiden Seiten.
        AddText(root.transform, plan.name, new Vector3(0.07f, 1.9f, 0f), 90f);
        AddText(root.transform, plan.name, new Vector3(-0.07f, 1.9f, 0f), -90f);

        return root;
    }

    void AddText(Transform parent, string text, Vector3 localPos, float yaw)
    {
        GameObject t = new GameObject("Text");
        t.transform.SetParent(parent, false);
        t.transform.localPosition = localPos;
        t.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        TextMesh tm = t.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 48;
        tm.characterSize = 0.045f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        MeshRenderer mr = t.GetComponent<MeshRenderer>();

        if (mr != null && tm.font != null)
            mr.sharedMaterial = tm.font.material;
    }

    void SetColor(GameObject go, Color color)
    {
        Renderer r = go.GetComponent<Renderer>();

        if (r == null)
            return;

        color.a = 1f;

        Material m;

        if (!materials.TryGetValue(color, out m))
        {
            if (fallbackMaterial != null)
            {
                m = new Material(fallbackMaterial);
            }
            else
            {
                Shader shader = Shader.Find("Standard");

                if (shader == null)
                    shader = Shader.Find("Sprites/Default");

                m = new Material(shader);
            }

            m.color = color;
            materials[color] = m;
        }

        r.sharedMaterial = m;
    }
}
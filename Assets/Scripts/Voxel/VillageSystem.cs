using System.Collections.Generic;
using UnityEngine;

// Ein Gebaeude-Typ, aus dem Doerfer bestehen. Im Inspector aenderbar.
// Ohne Prefab wird ein einfacher Platzhalter (Quader mit Dach) gebaut.
// Grundpreis einer Ware (im Inspector bearbeitbar).
[System.Serializable]
public class VillageGoodPrice
{
    public string good = "Coal";
    [Tooltip("Preis pro Stueck bei mittlerem Lagerbestand.")]
    public float basePrice = 5f;
}

// Wo im Dorf steht ein Gebaeude-Typ?
public enum VillagePlacement
{
    Street,     // entlang der Strassen (normale Haeuser)
    Center,     // an der Hauptstrasse in der Dorfmitte (Rathaus, Brunnen ...)
    StreetEnd   // quer am Ende der Hauptstrasse (Bahnhof ...)
}

[System.Serializable]
public class VillageBuildingType
{
    public string name = "House";

    [Tooltip("Grundflaeche in Metern: X = Breite entlang der Strasse, Y = Tiefe. Die Vorderseite (Tuer) soll in Richtung +Z des Prefabs zeigen.")]
    public Vector2 footprint = new Vector2(7f, 6f);

    [Tooltip("Wie oft dieser Typ vorkommt (relativ zu den anderen). 0 = wird nie zufaellig gewaehlt.")]
    public float weight = 1f;

    [Header("Vorkommen")]
    [Tooltip("Wo im Dorf steht er? Street = entlang der Strassen, Center = in der Mitte, StreetEnd = am Ende der Hauptstrasse. Center und StreetEnd werden nur gebaut, wenn 'Min Per Village' > 0 ist.")]
    public VillagePlacement placement = VillagePlacement.Street;
    [Tooltip("Mindestens so oft kommt er in einem Dorf vor (0 = nur zufaellig). 1 = in jedem passenden Dorf einmal (z. B. Rathaus, Bahnhof).")]
    public int minPerVillage = 0;
    [Tooltip("Hoechstens so oft kommt er in einem Dorf vor (0 = unbegrenzt).")]
    public int maxPerVillage = 0;
    [Range(0f, 1f)]
    [Tooltip("Wahrscheinlichkeit, dass ein Dorf die Pflicht-Gebaeude ('Min Per Village') ueberhaupt bekommt.")]
    public float appearChance = 1f;
    [Tooltip("Erst Doerfer mit mindestens so vielen Gebaeuden bekommen diesen Typ (kleine Doerfer bleiben ohne Rathaus, Bahnhof ...).")]
    public int minVillageBuildings = 0;

    [Tooltip("Prefabs dieses Typs (Ursprung am Boden, Mitte der Grundflaeche). Mehrere = zufaellige Auswahl. Leer = Platzhalter.")]
    public GameObject[] prefabs;

    [Header("Platzhalter (wenn kein Prefab)")]
    public float fallbackHeight = 3.5f;
    public Color fallbackColor = new Color(0.72f, 0.62f, 0.48f);
    [Tooltip("Platzhalter als Zylinder statt Quader (Brunnen, Turm).")]
    public bool fallbackRound = false;
}

// Plan eines einzelnen Teils eines Dorfes (Gebaeude, Strassenstueck oder Schild).
public class VillagePart
{
    public int type;          // Index in buildingTypes, oder PartStreet / PartSign
    public string typeName;   // Name des Typs (z. B. "Station"), "Street" oder "Sign"
    public Vector3 pos;       // Mitte der Grundflaeche (Hoehe = Dorfniveau)
    public float yaw;
    public Vector2 size;      // Grundflaeche
    public int variant;       // Zufallszahl fuer die Prefab-Auswahl
    public string good;       // nur bei Minen (PartMine): welche Ware dort gefoerdert wird
    public bool groundPrepared;   // nur bei Minen: Boden wurde schon eingeebnet
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

    // Zusaetzliche Reichweite fuer Minen weit ausserhalb des Dorfes (Meter ab der Mitte): Solange der Spieler
    // in dieser Naehe ist, werden auch die Minenteile gebaut.
    public float extraRadius;
    public string[] needs;

    // Haltepunkt am Bahnhof (nur wenn das Dorf einen Bahnhof hat): das Stationsgleis laeuft von
    // stopA nach stopB (Hoehe = Bodenhoehe, Richtung stopYaw in Grad). Daran halten spaeter die Zuege.
    public bool hasStop;
    public Vector3 stopA, stopB;
    public float stopYaw;

    // Erstes Teil mit diesem Typ-Namen (z. B. "Station"); null = gibt es in diesem Dorf nicht.
    public VillagePart FindPart(string typeName)
    {
        foreach (VillagePart p in parts)
        {
            if (p.typeName == typeName)
                return p;
        }

        return null;
    }

    public bool Has(string typeName)
    {
        return FindPart(typeName) != null;
    }
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
    public const int PartPlatform = -3;   // Bahnsteig am Bahnhof (Platzhalter)
    public const int PartMine = -5;       // Verladestation einer Mine (Tagebau)
    public const int PartRailBed = -4;    // nur Gelaende-Einebnung fuer das Stationsgleis (kein Objekt)

    [Header("Verteilung")]
    [Tooltip("Kantenlaenge einer Region in Metern. In jeder Region liegt hoechstens ein Dorf.")]
    public float regionSizeMeters = 260f;
    [Range(0f, 1f)]
    [Tooltip("Wahrscheinlichkeit, dass eine Region ein Dorf bekommt.")]
    public float villageChance = 0.45f;
    [Tooltip("Mindestabstand zum Startpunkt der Welt in Metern.")]
    public float minDistanceFromSpawn = 30f;
    [Tooltip("Hoechster erlaubter Hoehenunterschied am Dorfplatz (Meter). Zu huegelige Stellen bekommen kein Dorf.")]
    public float maxHeightRange = 4f;

    [Header("Groesse")]
    [Tooltip("Gebaeude pro Dorf (zufaellig dazwischen, Strassen nicht mitgezaehlt).")]
    public int minBuildings = 6;
    public int maxBuildings = 18;
    public float streetWidth = 5f;
    [Tooltip("Hoechstzahl an Seitenstrassen pro Dorf (0 = nur die Hauptstrasse). Seitenstrassen gibt es erst ab 6 Gebaeuden.")]
    public int maxBranches = 3;
    [Tooltip("Groesster Radius eines Dorfes um seine Mitte. Die Regionsgroesse muss mindestens das Doppelte sein.")]
    public float maxVillageRadius = 90f;

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

    [Header("Wirtschaft")]
    [Tooltip("So viel passt von jeder Ware ins Lager eines Dorfes.")]
    public float stockCapacity = 100f;
    [Tooltip("So viele Stueck pro Minute (Spielzeit) stellt ein Dorf von jeder seiner Waren her.")]
    public float productionPerMinute = 2f;
    [Tooltip("So viele Stueck pro Minute (Spielzeit) verbraucht ein Dorf von jeder Ware, die es braucht.")]
    public float consumptionPerMinute = 1.5f;
    [Tooltip("Grundpreise. Waren ohne Eintrag kosten den Standardpreis.")]
    public List<VillageGoodPrice> goodPrices = new List<VillageGoodPrice>
    {
        new VillageGoodPrice { good = "Coal",       basePrice = 5f },
        new VillageGoodPrice { good = "IronOre",    basePrice = 8f },
        new VillageGoodPrice { good = "IronIngots", basePrice = 40f },
        new VillageGoodPrice { good = "Wood",       basePrice = 6f },
        new VillageGoodPrice { good = "Food",       basePrice = 10f },
        new VillageGoodPrice { good = "Tools",      basePrice = 60f }
    };
    public float defaultPrice = 10f;
    [Tooltip("Zeigt in der Naehe eines Dorfes Lager, Ansehen und Geld als Text an (zum Testen).")]
    public bool showHud = true;

    [Header("Gebaeude")]
    [Tooltip("Alle Gebaeude-Typen. Normale Haeuser: Gewicht > 0. Sondergebaeude (Rathaus, Bahnhof, Fabrik ...): 'Min Per Village' und 'Max Per Village' einstellen, Reihenfolge egal.")]
    public List<VillageBuildingType> buildingTypes = new List<VillageBuildingType>
    {
        new VillageBuildingType { name = "House",    footprint = new Vector2(7f, 6f),   weight = 5f, fallbackHeight = 3.5f, fallbackColor = new Color(0.78f, 0.68f, 0.52f) },
        new VillageBuildingType { name = "BigHouse", footprint = new Vector2(9f, 7f),   weight = 2f, fallbackHeight = 5f,   fallbackColor = new Color(0.70f, 0.55f, 0.45f) },
        new VillageBuildingType { name = "Shop",     footprint = new Vector2(8f, 6f),   weight = 1f, maxPerVillage = 2, fallbackHeight = 3.5f, fallbackColor = new Color(0.60f, 0.66f, 0.72f) },
        new VillageBuildingType { name = "Barn",     footprint = new Vector2(11f, 8f),  weight = 1f, maxPerVillage = 2, fallbackHeight = 5f,   fallbackColor = new Color(0.55f, 0.30f, 0.25f) },
        new VillageBuildingType { name = "Well",     footprint = new Vector2(2.6f, 2.6f), weight = 0f, placement = VillagePlacement.Center, minPerVillage = 1, maxPerVillage = 1, fallbackHeight = 1.2f, fallbackColor = new Color(0.55f, 0.55f, 0.55f), fallbackRound = true },
        new VillageBuildingType { name = "TownHall", footprint = new Vector2(12f, 9f),  weight = 0f, placement = VillagePlacement.Center, minPerVillage = 1, maxPerVillage = 1, appearChance = 0.8f, minVillageBuildings = 10, fallbackHeight = 6.5f, fallbackColor = new Color(0.82f, 0.78f, 0.68f) },
        new VillageBuildingType { name = "Station",  footprint = new Vector2(14f, 8f),  weight = 0f, placement = VillagePlacement.StreetEnd, minPerVillage = 1, maxPerVillage = 1, appearChance = 0.6f, minVillageBuildings = 8, fallbackHeight = 4.5f, fallbackColor = new Color(0.50f, 0.36f, 0.30f) },
        new VillageBuildingType { name = "Factory",  footprint = new Vector2(16f, 12f), weight = 0.6f, maxPerVillage = 1, minVillageBuildings = 9, fallbackHeight = 7f, fallbackColor = new Color(0.45f, 0.45f, 0.48f) }
    };
    [Tooltip("Zusaetzliche Drehung der Prefabs (Grad), falls ihre Vorderseite nicht nach +Z zeigt.")]
    public float prefabYawOffset = 0f;
    [Tooltip("Material fuer Platzhalter. Leer = einfaches Standard-Material.")]
    public Material fallbackMaterial;
    public Color streetColor = new Color(0.38f, 0.35f, 0.31f);

    [Header("Bahnhof")]
    [Range(0f, 1f)]
    [Tooltip("Wahrscheinlichkeit, dass ein ausreichend grosses Dorf einen Bahnhof bekommt (0 = nie, 1 = immer). " +
             "Gilt unabhaengig von der Liste 'Building Types': Fehlt dort der Typ 'Station', wird er automatisch ergaenzt.")]
    public float stationChance = 0.6f;
    [Tooltip("Ab so vielen Gebaeuden bekommt ein Dorf einen Bahnhof.")]
    public int stationMinBuildings = 6;
    [Tooltip("Laenge des Stationsgleises hinter dem Bahnhof (Meter, hoechstens 56). Es wird beim Annaehern gebaut.")]
    public float stationTrackLength = 44f;
    [Tooltip("Laenge des Bahnsteigs (Meter, Platzhalter).")]
    public float platformLength = 26f;
    [Tooltip("Zusaetzlicher Platz hinter dem Bahnhof fuer Bahnsteig und Gleis (Meter). Verkuerzt die Hauptstrasse entsprechend.")]
    public float stationDepthExtra = 9f;

    [Header("Minen (Tagebau)")]
    [Tooltip("Dorf-Waren Coal / IronOre werden im Tagebau aus einem Vorkommen in dieser Entfernung (Meter) gefoerdert. " +
             "Gibt es keines, stellt das Dorf die Ware nicht her.")]
    public float mineSearchRadius = 250f;
    [Tooltip("So viel Kubikmeter muessen mindestens erreichbar sein, damit sich eine Mine lohnt.")]
    public float mineMinReachableM3 = 15f;
    [Tooltip("Kubikmeter Vorkommen, die ein Stueck Kohle bzw. Erz verbrauchen (passend zum VoxelDigger).")]
    public float coalVolumePerUnit = 0.05f;
    public float ironVolumePerUnit = 0.08f;
    [Tooltip("Zum Testen: Pro Stueck wird so viel mehr aus dem Vorkommen entnommen (50 = die Grube waechst sichtbar schnell).")]
    public float mineVolumeScale = 1f;
    [Tooltip("Optional: eigene Verladestation. Leer = Platzhalter. Das Skript MineStation wird bei Bedarf ergaenzt. " +
             "Der Ursprung liegt am Boden, die Vorderseite (+Z) zeigt zur Grube.")]
    public GameObject mineStationPrefab;
    public Vector2 mineStationFootprint = new Vector2(10f, 8f);

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
    Material textMaterial;

    // Wirtschaft: Zustand der Doerfer und die Spielzeit, nach der sich ihr Lager richtet.
    readonly Dictionary<int, VillageState> states = new Dictionary<int, VillageState>();
    double simSeconds;

    // Geladene Wirtschaftsdaten, falls das Laden vor dem Start dieses Skripts passiert.
    static StateBag pendingEconomy;

    float nextTick;

    // Alle bisher bekannten (geplanten) Doerfer.
    public IList<VillagePlan> KnownVillages { get { return known; } }

    void Awake()
    {
        Instance = this;
        EnsureStationType();
    }

    // Der Bahnhof muss nicht in der Inspector-Liste stehen (alte Szenen haben nur die Liste von damals):
    // Fehlt der Typ "Station", wird er mit Standardwerten hinten angehaengt. Wahrscheinlichkeit und
    // Mindestgroesse kommen immer aus den Feldern "Station Chance" und "Station Min Buildings".
    void EnsureStationType()
    {
        VillageBuildingType station = null;

        foreach (VillageBuildingType t in buildingTypes)
        {
            if (t != null && t.name == "Station")
            {
                station = t;
                break;
            }
        }

        if (station == null)
        {
            station = new VillageBuildingType
            {
                name = "Station",
                footprint = new Vector2(14f, 8f),
                weight = 0f,
                placement = VillagePlacement.StreetEnd,
                minPerVillage = 1,
                maxPerVillage = 1,
                fallbackHeight = 4.5f,
                fallbackColor = new Color(0.50f, 0.36f, 0.30f)
            };

            buildingTypes.Add(station);
        }

        // Ein Bahnhof gehoert an das Strassenende und kommt hoechstens einmal vor.
        station.placement = VillagePlacement.StreetEnd;
        station.minPerVillage = Mathf.Max(1, station.minPerVillage);
        station.maxPerVillage = 1;
        station.weight = 0f;
        station.appearChance = stationChance;
        station.minVillageBuildings = stationMinBuildings;
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

        if (pendingEconomy != null)
        {
            ReadEconomy(pendingEconomy);
            pendingEconomy = null;
        }
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

            if (!world.IsInsideWorld(c, maxVillageRadius + 10f))
                continue;

            Vector3 spawn = world.SpawnPoint();
            Vector3 toSpawn = c - spawn;
            toSpawn.y = 0f;

            if (toSpawn.magnitude < minDistanceFromSpawn + maxVillageRadius * 0.5f)
                continue;

            // Hoehen auf einem Raster (9 x 9) um den Mittelpunkt messen.
            float step = maxVillageRadius * 0.12f;
            List<float> heights = new List<float>();
            float min = float.MaxValue, max = float.MinValue;

            for (int i = -4; i <= 4; i++)
            {
                for (int j = -4; j <= 4; j++)
                {
                    float h = world.GetNaturalHeight(x + i * step, z + j * step);
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

    // =====================================================================
    //  Layout: Strassennetz und Gebaeude
    // =====================================================================

    // Ein gedrehtes Rechteck in der Dorf-Ebene. Koordinaten: x = entlang der Hauptstrasse,
    // y = quer dazu (jeweils relativ zur Dorfmitte, noch nicht gedreht).
    class Box2
    {
        public Vector2 c;       // Mitte
        public Vector2 half;    // halbe Groesse
        public Vector2 ax;      // Richtung der Breite (Einheitsvektor); quer dazu: (-ax.y, ax.x)
    }

    class StreetInfo
    {
        public Vector2 start;   // Anfang (Mitte der Strassenbreite)
        public Vector2 dir;     // Richtung (Einheitsvektor)
        public float length;
        public float width;
        public Box2 box;
    }

    // Hilfsdaten waehrend der Planung eines Dorfes.
    class Layout
    {
        public VillagePlan plan;
        public System.Random rnd;
        public List<StreetInfo> streets = new List<StreetInfo>();
        public List<Box2> buildingBoxes = new List<Box2>();
        public List<VillagePart> buildings = new List<VillagePart>();
        public List<VillagePart> extras = new List<VillagePart>();   // Bahnsteig, Gleisbett
        public int[] counts;
    }

    // Ein Platz entlang einer Strassenseite.
    class SideCursor
    {
        public int street;
        public int side;        // +1 oder -1
        public float t;         // bis hierhin ist die Seite belegt (Meter ab Strassenanfang)
        public bool full;
    }

    static Vector2 Perp(Vector2 v)
    {
        return new Vector2(-v.y, v.x);
    }

    // Ueberlappen sich zwei gedrehte Rechtecke (mit etwas Abstand)? (Trennachsen-Test)
    static bool Overlap(Box2 a, Box2 b, float margin)
    {
        Vector2 pa = Perp(a.ax);
        Vector2 pb = Perp(b.ax);
        Vector2 d = b.c - a.c;

        for (int i = 0; i < 4; i++)
        {
            Vector2 u = i == 0 ? a.ax : (i == 1 ? pa : (i == 2 ? b.ax : pb));

            float ra = a.half.x * Mathf.Abs(Vector2.Dot(a.ax, u)) + a.half.y * Mathf.Abs(Vector2.Dot(pa, u));
            float rb = b.half.x * Mathf.Abs(Vector2.Dot(b.ax, u)) + b.half.y * Mathf.Abs(Vector2.Dot(pb, u));

            if (Mathf.Abs(Vector2.Dot(d, u)) > ra + rb + margin)
                return false;
        }

        return true;
    }

    // Eine Dorf-Koordinate in die Welt umrechnen.
    Vector3 ToWorld(VillagePlan plan, Vector2 p)
    {
        Quaternion rot = Quaternion.Euler(0f, plan.yaw, 0f);

        Vector3 pos = plan.center + (rot * Vector3.right) * p.x + (rot * Vector3.forward) * p.y;
        pos.y = plan.level;

        return pos;
    }

    // Ein Strassenstueck anlegen.
    StreetInfo MakeStreet(Vector2 start, Vector2 dir, float length, float width)
    {
        StreetInfo s = new StreetInfo();
        s.start = start;
        s.dir = dir;
        s.length = length;
        s.width = width;

        s.box = new Box2();
        s.box.c = start + dir * (length * 0.5f);
        s.box.half = new Vector2(length * 0.5f, width * 0.5f);
        s.box.ax = dir;

        return s;
    }

    // Haengt eine Seitenstrasse an eine Strasse an (rechtwinklig). null = passt nicht.
    StreetInfo AddBranch(Layout lay, int parentIndex, float tParent, int side, float length, float width)
    {
        StreetInfo parent = lay.streets[parentIndex];

        Vector2 normal = Perp(parent.dir) * side;
        Vector2 start = parent.start + parent.dir * tParent + normal * (parent.width * 0.5f);

        // Nicht ueber den Dorfrand hinaus.
        while (length >= 12f && (start + normal * length).magnitude > maxVillageRadius - 14f)
            length -= 2f;

        if (length < 12f)
            return null;

        StreetInfo s = MakeStreet(start, normal, length, width);

        // Nicht in andere Strassen hineinlaufen (die eigene Elternstrasse ausgenommen).
        for (int i = 0; i < lay.streets.Count; i++)
        {
            if (i != parentIndex && Overlap(s.box, lay.streets[i].box, 3f))
                return null;
        }

        lay.streets.Add(s);
        return s;
    }

    // Das Rechteck eines Gebaeudes an einer Strassenseite. "yawLocal" = Drehung im Dorf-System,
    // die Vorderseite (+Z des Gebaeudes) zeigt zur Strasse.
    Box2 BuildingBox(StreetInfo street, float t, int side, Vector2 footprint, out float yawLocal)
    {
        Vector2 normal = Perp(street.dir) * side;
        Vector2 onStreet = street.start + street.dir * t;

        Vector2 center = onStreet + normal * (street.width * 0.5f + 2.5f + footprint.y * 0.5f);

        Vector2 front = -normal;
        yawLocal = Mathf.Atan2(front.x, front.y) * Mathf.Rad2Deg;

        float rad = yawLocal * Mathf.Deg2Rad;

        Box2 box = new Box2();
        box.c = center;
        box.half = footprint * 0.5f;
        box.ax = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad));

        return box;
    }

    // Passt das Rechteck? (innerhalb des Dorfradius, nicht auf Strassen oder anderen Gebaeuden)
    bool CanPlace(Layout lay, Box2 box, int ownStreet)
    {
        if (box.c.magnitude + box.half.magnitude > maxVillageRadius - 6f)
            return false;

        for (int i = 0; i < lay.streets.Count; i++)
        {
            if (i != ownStreet && Overlap(box, lay.streets[i].box, 1.2f))
                return false;
        }

        for (int i = 0; i < lay.buildingBoxes.Count; i++)
        {
            if (Overlap(box, lay.buildingBoxes[i], 1.5f))
                return false;
        }

        return true;
    }

    void AddBuilding(Layout lay, int type, Box2 box, float yawLocal)
    {
        VillageBuildingType t = buildingTypes[type];

        VillagePart b = new VillagePart();
        b.type = type;
        b.typeName = t.name;
        b.size = t.footprint;
        b.yaw = lay.plan.yaw + yawLocal;
        b.pos = ToWorld(lay.plan, box.c);
        b.variant = lay.rnd.Next();

        lay.buildings.Add(b);
        lay.buildingBoxes.Add(box);
        lay.counts[type]++;
    }

    // Ist dieser Eintrag ein altes "Gewicht 0"-Brunnen-Muster (aus Szenen vor den neuen Feldern)?
    static bool IsLegacyWell(VillageBuildingType t)
    {
        return t.weight <= 0f && t.minPerVillage <= 0 && t.maxPerVillage <= 0;
    }

    // Wie oft MUSS dieser Typ im Dorf vorkommen (vor dem Wuerfeln der Wahrscheinlichkeit)?
    static int MandatoryCount(VillageBuildingType t)
    {
        if (t.minPerVillage > 0)
            return t.minPerVillage;

        return IsLegacyWell(t) ? 1 : 0;
    }

    // Waehlt zufaellig einen Typ fuer die Strassenreihen: nur Typen mit Gewicht > 0, die ihr Maximum
    // noch nicht erreicht haben und fuer diese Dorfgroesse erlaubt sind. -1 = keiner uebrig.
    int PickRandomType(Layout lay, int target)
    {
        float total = 0f;

        for (int i = 0; i < buildingTypes.Count; i++)
            total += RandomWeight(lay, i, target);

        if (total <= 0f)
            return -1;

        float roll = (float)lay.rnd.NextDouble() * total;

        for (int i = 0; i < buildingTypes.Count; i++)
        {
            float w = RandomWeight(lay, i, target);

            if (w <= 0f)
                continue;

            roll -= w;

            if (roll <= 0f)
                return i;
        }

        for (int i = buildingTypes.Count - 1; i >= 0; i--)
        {
            if (RandomWeight(lay, i, target) > 0f)
                return i;
        }

        return -1;
    }

    float RandomWeight(Layout lay, int i, int target)
    {
        VillageBuildingType t = buildingTypes[i];

        if (t.weight <= 0f || t.placement != VillagePlacement.Street)
            return 0f;

        if (target < t.minVillageBuildings)
            return 0f;

        if (t.maxPerVillage > 0 && lay.counts[i] >= t.maxPerVillage)
            return 0f;

        return t.weight;
    }

    // Verteilt Strassen, Gebaeude und Schild.
    void LayoutVillage(VillagePlan plan, System.Random rnd)
    {
        Layout lay = new Layout();
        lay.plan = plan;
        lay.rnd = rnd;
        lay.counts = new int[buildingTypes.Count];

        int target = rnd.Next(minBuildings, maxBuildings + 1);

        // 1) Welche Sondergebaeude hat dieses Dorf? (Mindestanzahl, Dorfgroesse, Wahrscheinlichkeit)
        List<int> mandatoryStreet = new List<int>();
        List<int> mandatoryCenter = new List<int>();
        List<int> mandatoryEnd = new List<int>();
        float endReserve = 0f;

        for (int i = 0; i < buildingTypes.Count; i++)
        {
            VillageBuildingType t = buildingTypes[i];
            int must = MandatoryCount(t);

            if (must <= 0)
                continue;

            bool legacy = IsLegacyWell(t);
            double roll = rnd.NextDouble();

            if (target < t.minVillageBuildings || roll > t.appearChance)
                continue;

            VillagePlacement place = legacy ? VillagePlacement.Center : t.placement;

            for (int k = 0; k < must; k++)
            {
                if (place == VillagePlacement.Center) mandatoryCenter.Add(i);
                else if (place == VillagePlacement.StreetEnd) mandatoryEnd.Add(i);
                else mandatoryStreet.Add(i);
            }

            if (place == VillagePlacement.StreetEnd)
                endReserve = Mathf.Max(endReserve, t.footprint.y + 4f + (t.name == "Station" ? stationDepthExtra : 0f));
        }

        // 2) Hauptstrasse
        float maxMain = 2f * (maxVillageRadius - 16f - endReserve);
        float length = Mathf.Lerp(Mathf.Min(30f, maxMain), maxMain, (float)rnd.NextDouble());
        length = Mathf.Max(length, 24f);

        lay.streets.Add(MakeStreet(new Vector2(-length * 0.5f, 0f), new Vector2(1f, 0f), length, streetWidth));
        plan.streetLength = length;

        // 3) Seitenstrassen (und selten Seitenstrassen von Seitenstrassen)
        int maxB = target >= 9 ? maxBranches : (target >= 6 ? Mathf.Min(1, maxBranches) : 0);
        int branchCount = maxB > 0 ? rnd.Next(1, maxB + 1) : 0;

        List<float> usedT = new List<float>();

        for (int b = 0; b < branchCount; b++)
        {
            if (length < 34f)
                break;

            float t = Mathf.Lerp(12f, length - 12f, (float)rnd.NextDouble());

            bool tooClose = false;

            foreach (float u in usedT)
            {
                if (Mathf.Abs(u - t) < 16f)
                    tooClose = true;
            }

            if (tooClose)
                continue;

            usedT.Add(t);

            // Eine Seite oder beide (Kreuzung).
            int mode = rnd.Next(3);
            float branchLength = 16f + (float)rnd.NextDouble() * 28f;

            for (int s = 0; s < 2; s++)
            {
                if (mode != 2 && mode != s)
                    continue;

                int side = s == 0 ? 1 : -1;
                float len = mode == 2 ? branchLength * (0.7f + (float)rnd.NextDouble() * 0.5f) : branchLength;

                StreetInfo branch = AddBranch(lay, 0, t, side, len, streetWidth * 0.8f);

                // Lange Seitenstrassen bekommen manchmal noch eine Abzweigung.
                if (branch != null && branch.length >= 28f && rnd.NextDouble() < 0.4)
                {
                    int sub = lay.streets.Count - 1;
                    float subT = branch.length * (0.5f + (float)rnd.NextDouble() * 0.3f);
                    int subSide = rnd.Next(2) == 0 ? 1 : -1;

                    AddBranch(lay, sub, subT, subSide, 12f + (float)rnd.NextDouble() * 12f, streetWidth * 0.8f);
                }
            }
        }

        // 4) Sondergebaeude in der Mitte (Rathaus, Brunnen ...): an der Hauptstrasse nahe der Mitte
        foreach (int type in mandatoryCenter)
        {
            Vector2 fp = buildingTypes[type].footprint;
            bool done = false;

            for (int k = 0; k < 10 && !done; k++)
            {
                float off = k == 0 ? 0f : ((k + 1) / 2) * (fp.x + 3f) * (k % 2 == 1 ? 1f : -1f);
                float t = length * 0.5f + off;

                if (t - fp.x * 0.5f < 1f || t + fp.x * 0.5f > length - 1f)
                    continue;

                int firstSide = rnd.Next(2) == 0 ? 1 : -1;

                for (int s = 0; s < 2 && !done; s++)
                {
                    float yaw;
                    Box2 box = BuildingBox(lay.streets[0], t, s == 0 ? firstSide : -firstSide, fp, out yaw);

                    if (CanPlace(lay, box, 0))
                    {
                        AddBuilding(lay, type, box, yaw);
                        done = true;
                    }
                }
            }
        }

        // 5) Sondergebaeude am Ende der Hauptstrasse (Bahnhof ...): quer davor, Vorderseite zur Strasse
        foreach (int type in mandatoryEnd)
        {
            Vector2 fp = buildingTypes[type].footprint;

            Box2 box = new Box2();
            box.c = new Vector2(length * 0.5f + 1.5f + fp.y * 0.5f, 0f);
            box.half = fp * 0.5f;

            // Vorderseite zeigt nach -X (zur Strasse): Drehung 270 Grad.
            box.ax = new Vector2(Mathf.Cos(270f * Mathf.Deg2Rad), -Mathf.Sin(270f * Mathf.Deg2Rad));

            if (CanPlace(lay, box, 0))
            {
                AddBuilding(lay, type, box, 270f);

                if (buildingTypes[type].name == "Station")
                    AddStationTrack(lay, box.c.x + fp.y * 0.5f);
            }
        }

        // 6) Die Reihen entlang der Strassen fuellen
        List<SideCursor> cursors = new List<SideCursor>();

        for (int i = 0; i < lay.streets.Count; i++)
        {
            for (int s = 0; s < 2; s++)
            {
                SideCursor c = new SideCursor();
                c.street = i;
                c.side = s == 0 ? 1 : -1;
                c.t = 3f;

                cursors.Add(c);
            }
        }

        // Zuerst die Pflichtgebaeude an Strassen, dann zufaellige.
        List<int> queue = new List<int>(mandatoryStreet);

        for (int guard = 0; guard < 600 && lay.buildings.Count < target; guard++)
        {
            int type = queue.Count > 0 ? queue[0] : PickRandomType(lay, target);

            if (type < 0)
                break;

            // Die Seite waehlen, die (im Verhaeltnis zur Laenge) am leersten ist.
            SideCursor best = null;
            float bestFill = float.MaxValue;

            foreach (SideCursor c in cursors)
            {
                if (c.full)
                    continue;

                float fill = c.t / lay.streets[c.street].length;

                if (fill < bestFill)
                {
                    bestFill = fill;
                    best = c;
                }
            }

            if (best == null)
                break;

            StreetInfo street = lay.streets[best.street];
            Vector2 fp = buildingTypes[type].footprint;

            bool placed = false;

            for (int tries = 0; tries < 8 && !placed; tries++)
            {
                float t = best.t + fp.x * 0.5f;

                if (t + fp.x * 0.5f > street.length - 1f)
                {
                    best.full = true;
                    break;
                }

                float yaw;
                Box2 box = BuildingBox(street, t, best.side, fp, out yaw);

                if (CanPlace(lay, box, best.street))
                {
                    AddBuilding(lay, type, box, yaw);
                    best.t += fp.x + 2f + (float)rnd.NextDouble() * 3f;
                    placed = true;
                }
                else
                {
                    best.t += 3f;
                }
            }

            if (placed && queue.Count > 0)
                queue.RemoveAt(0);
        }

        // 7) Strassen in Stuecke von etwa 8 m teilen (so werden sie einzeln aufgebaut)
        for (int i = 0; i < lay.streets.Count; i++)
        {
            StreetInfo st = lay.streets[i];

            int segments = Mathf.Max(1, Mathf.CeilToInt(st.length / 8f));
            float segLength = st.length / segments;

            // Drehung so, dass die lokale X-Achse in Strassenrichtung zeigt.
            float yawLocal = Mathf.Atan2(-st.dir.y, st.dir.x) * Mathf.Rad2Deg;

            for (int k = 0; k < segments; k++)
            {
                VillagePart seg = new VillagePart();
                seg.type = PartStreet;
                seg.typeName = "Street";
                seg.size = new Vector2(segLength, st.width);
                seg.yaw = plan.yaw + yawLocal;
                seg.pos = ToWorld(plan, st.start + st.dir * (segLength * (k + 0.5f)));

                plan.parts.Add(seg);
            }
        }

        plan.parts.AddRange(lay.buildings);
        plan.parts.AddRange(lay.extras);

        // 8) Schild am Anfang der Hauptstrasse
        VillagePart sign = new VillagePart();
        sign.type = PartSign;
        sign.typeName = "Sign";
        sign.size = new Vector2(1.5f, 1.5f);
        sign.yaw = plan.yaw;
        sign.pos = ToWorld(plan, new Vector2(-length * 0.5f - 2f, streetWidth * 0.5f + 1.2f));

        plan.parts.Add(sign);

        // Alles liegt innerhalb dieses Radius um die Mitte.
        float radius = 0f;

        foreach (VillagePart part in plan.parts)
        {
            Vector3 d = part.pos - plan.center;
            d.y = 0f;

            radius = Mathf.Max(radius, d.magnitude + part.size.magnitude * 0.5f);
        }

        plan.radius = radius + 4f;
    }

    // Bahnsteig und Stationsgleis hinter dem Bahnhof. backX = Rueckseite des Gebaeudes (Dorf-Koordinate x).
    // Das Gleis verlaeuft quer zur Hauptstrasse (parallel zur Vorderseite des Bahnhofs).
    void AddStationTrack(Layout lay, float backX)
    {
        VillagePlan plan = lay.plan;

        float len = Mathf.Clamp(stationTrackLength, 20f, 56f);
        float platLen = Mathf.Clamp(platformLength, 8f, len - 6f);

        float trackX = backX + 5.2f;
        float platX = backX + 2.2f;

        // Die Laengsachse dieser Teile ist die Dorf-Querrichtung (+y): Drehung = Dorfdrehung - 90 Grad.
        float alongYaw = plan.yaw - 90f;

        VillagePart platform = new VillagePart();
        platform.type = PartPlatform;
        platform.typeName = "Platform";
        platform.size = new Vector2(platLen, 3.4f);
        platform.yaw = alongYaw;
        platform.pos = ToWorld(plan, new Vector2(platX, 0f));
        lay.extras.Add(platform);

        VillagePart bed = new VillagePart();
        bed.type = PartRailBed;
        bed.typeName = "RailBed";
        bed.size = new Vector2(len + 2f, 7f);
        bed.yaw = alongYaw;
        bed.pos = ToWorld(plan, new Vector2(platX + 1.2f, 0f));
        lay.extras.Add(bed);

        // Haltepunkt: Anfang (A) und Ende (B) des Stationsgleises auf Bodenhoehe.
        float y = plan.level - VoxelWorld.FlattenSink;

        plan.stopA = ToWorld(plan, new Vector2(trackX, -len * 0.5f));
        plan.stopB = ToWorld(plan, new Vector2(trackX, len * 0.5f));
        plan.stopA.y = y;
        plan.stopB.y = y;
        plan.stopYaw = TrackEdge.DirToYaw(plan.stopB - plan.stopA);
        plan.hasStop = true;
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
    //  Wirtschaft: Lager, Preise, Ansehen
    // =====================================================================

    // Stabile Zahl aus einem Text (string.GetHashCode ist nicht auf jedem Rechner gleich).
    static int StableHash(string text)
    {
        unchecked
        {
            int h = 17;

            foreach (char c in text)
                h = h * 31 + c;

            return h;
        }
    }

    // Zustand eines Dorfes (wird beim ersten Zugriff angelegt und auf den neuesten Stand gerechnet).
    public VillageState GetState(VillagePlan plan)
    {
        VillageState state;

        if (!states.TryGetValue(plan.id, out state))
        {
            state = new VillageState();
            state.id = plan.id;
            state.lastSim = simSeconds;

            // Anfangsbestand: Gestern hergestellte Waren sind eher voll, benoetigte eher leer.
            foreach (string good in plan.produces)
            {
                System.Random r = new System.Random(plan.id ^ StableHash(good));
                state.stock[good] = stockCapacity * (0.45f + 0.35f * (float)r.NextDouble());
            }

            foreach (string good in plan.needs)
            {
                System.Random r = new System.Random(plan.id ^ StableHash(good) ^ 77);
                state.stock[good] = stockCapacity * (0.1f + 0.3f * (float)r.NextDouble());
            }

            states[plan.id] = state;
            return state;
        }

        Advance(plan, state);
        return state;
    }

    // Rechnet das Lager bis jetzt nach: Hergestelltes waechst, Benoetigtes wird verbraucht.
    void Advance(VillagePlan plan, VillageState state)
    {
        double dt = simSeconds - state.lastSim;

        // Nicht bei jeder Abfrage rechnen (das Minen-Gelaende ist nicht billig): erst nach ein paar Sekunden.
        if (dt < 3.0)
            return;

        state.lastSim = simSeconds;

        foreach (string good in plan.produces)
        {
            float v;
            state.stock.TryGetValue(good, out v);

            float units = productionPerMinute * (float)dt / 60f;

            // Kohle und Erz kommen aus einem Vorkommen (Tagebau): nur so viel, wie noch da ist.
            BlockType block;

            if (MinedBlock(good, out block))
                units = MineUnits(plan, good, block, Mathf.Min(units, Mathf.Max(0f, stockCapacity - v)));

            state.stock[good] = Mathf.Min(stockCapacity, v + units);
        }

        foreach (string good in plan.needs)
        {
            float v;
            state.stock.TryGetValue(good, out v);
            state.stock[good] = Mathf.Max(0f, v - consumptionPerMinute * (float)dt / 60f);
        }
    }

    // ---------- Minen (Tagebau) ----------

    // Welche Waren aus dem Boden kommen (und aus welchem Erz).
    static bool MinedBlock(string good, out BlockType block)
    {
        if (good == "Coal")
        {
            block = BlockType.Coal;
            return true;
        }

        if (good == "IronOre")
        {
            block = BlockType.Iron;
            return true;
        }

        block = BlockType.Stone;
        return false;
    }

    class MineAssign
    {
        public long depositId;
        public Vector3 center;      // Mitte des Vorkommens (Welt)
        public BlockType block;
    }

    // Welches Vorkommen foerdert ein Dorf fuer eine Ware?
    readonly Dictionary<string, MineAssign> mines = new Dictionary<string, MineAssign>();

    static string MineKey(VillagePlan plan, string good)
    {
        return plan.id + "|" + good;
    }

    // Foerdert bis zu "units" Stueck aus dem Vorkommen des Dorfes; gibt zurueck, wie viele wirklich kamen.
    float MineUnits(VillagePlan plan, string good, BlockType block, float units)
    {
        if (units <= 0f || world == null || !world.IsReady || world.Ore == null)
            return 0f;

        Deposit dep;

        if (!TryGetMineDeposit(plan, good, block, out dep))
            return 0f;

        float volPerUnit = Mathf.Max(0.0001f, (block == BlockType.Coal ? coalVolumePerUnit : ironVolumePerUnit) * mineVolumeScale);
        // Die Rampe liegt seitlich der Grube: 90 Grad zur Seite, auf der die Verladestation steht (Richtung zum Dorf).
        Vector3 toVillage = plan.center - dep.center;
        toVillage.y = 0f;

        if (toVillage.sqrMagnitude < 1f)
            toVillage = Vector3.right;

        toVillage.Normalize();
        Vector3 side = new Vector3(-toVillage.z, 0f, toVillage.x);
        float rampYaw = Mathf.Atan2(-side.z, side.x) * Mathf.Rad2Deg;

        float taken = world.MineDeposit(dep, units * volPerUnit, rampYaw);

        return taken / volPerUnit;
    }

    // Das Vorkommen der Mine (neu suchen, wenn es noch keines gibt oder es erschoepft ist).
    bool TryGetMineDeposit(VillagePlan plan, string good, BlockType block, out Deposit dep)
    {
        string key = MineKey(plan, good);
        MineAssign assign;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (mines.TryGetValue(key, out assign) && assign != null)
            {
                List<Deposit> found = world.FindDeposits(assign.center, 3f, block);
                int index = found.FindIndex(q => q.id == assign.depositId);

                if (index >= 0 && found[index].reachableM3 - world.Ore.MinedByVillages(assign.depositId) > 0.01f)
                {
                    dep = found[index];
                    return true;
                }

                // Erschoepft: naechstes suchen.
                mines.Remove(key);
            }

            if (attempt == 0 && !AssignMine(plan, good, block))
                break;
        }

        dep = new Deposit();
        return false;
    }

    // Sucht das naechste Vorkommen in Reichweite, das genug hergibt, und baut dort eine Verladestation ein.
    bool AssignMine(VillagePlan plan, string good, BlockType block)
    {
        List<Deposit> near = world.FindDeposits(plan.center, mineSearchRadius, block);

        foreach (Deposit d in near)
        {
            float left = d.reachableM3 - world.Ore.MinedByVillages(d.id);

            if (left < mineMinReachableM3)
                continue;

            MineAssign a = new MineAssign();
            a.depositId = d.id;
            a.center = d.center;
            a.block = block;
            mines[MineKey(plan, good)] = a;

            AddMineStation(plan, good, d);
            return true;
        }

        return false;
    }

    // Verladestation am Grubenrand, auf der dem Dorf zugewandten Seite.
    void AddMineStation(VillagePlan plan, string good, Deposit d)
    {
        // Schon eine Station fuer diese Ware an diesem Vorkommen?
        foreach (VillagePart existing in plan.parts)
        {
            if (existing.type == PartMine && existing.good == good &&
                Vector2.Distance(new Vector2(existing.pos.x, existing.pos.z), new Vector2(d.center.x, d.center.z)) < Mathf.Max(d.radiusMajor, 5f) * 2f + 40f)
                return;
        }

        Vector3 toVillage = plan.center - d.center;
        toVillage.y = 0f;

        if (toVillage.sqrMagnitude < 1f)
            toVillage = Vector3.right;

        toVillage.Normalize();

        float reach = Mathf.Max(d.radiusMajor, d.radiusMinor) * 1.3f + 14f;
        Vector3 sp = d.center + toVillage * reach;
        sp.y = world.GetNaturalHeight(sp.x, sp.z);

        // Vorderseite (+Z) zeigt zur Grube.
        Vector3 toPit = -toVillage;

        VillagePart part = new VillagePart();
        part.type = PartMine;
        part.typeName = "Mine";
        part.good = good;
        part.pos = sp;
        part.yaw = Mathf.Atan2(toPit.x, toPit.z) * Mathf.Rad2Deg;
        part.size = mineStationFootprint;
        part.variant = (int)(d.id & 0xFFFF);

        plan.parts.Add(part);

        // Die Teile werden nur gebaut, wenn der Spieler im Umkreis des Dorfes ist: bis zur Station erweitern.
        Vector3 dc = sp - plan.center;
        dc.y = 0f;
        plan.extraRadius = Mathf.Max(plan.extraRadius, dc.magnitude + mineStationFootprint.magnitude);
    }

    // Fuer die Anzeige an der Verladestation.
    public bool DescribeMine(VillagePlan plan, string good, out float remainingM3, out float totalM3, out float depthM)
    {
        remainingM3 = totalM3 = depthM = 0f;

        BlockType block;

        if (world == null || world.Ore == null || !MinedBlock(good, out block))
            return false;

        MineAssign a;

        if (!mines.TryGetValue(MineKey(plan, good), out a))
            return false;

        List<Deposit> found = world.FindDeposits(a.center, 3f, block);
        int index = found.FindIndex(q => q.id == a.depositId);

        if (index < 0)
            return false;

        Deposit d = found[index];
        remainingM3 = d.remainingM3;
        totalM3 = d.volumeM3;
        depthM = d.cutY == float.MaxValue ? 0f : Mathf.Max(0f, d.surfaceY - d.cutY);
        return true;
    }

    public float Stock(VillagePlan plan, string good)
    {
        float v;
        GetState(plan).stock.TryGetValue(good, out v);
        return v;
    }

    public float Reputation(VillagePlan plan)
    {
        return GetState(plan).reputation;
    }

    public bool Produces(VillagePlan plan, string good)
    {
        return System.Array.IndexOf(plan.produces, good) >= 0;
    }

    public bool Needs(VillagePlan plan, string good)
    {
        return System.Array.IndexOf(plan.needs, good) >= 0;
    }

    float BasePrice(string good)
    {
        foreach (VillageGoodPrice p in goodPrices)
        {
            if (p != null && p.good == good)
                return p.basePrice;
        }

        return defaultPrice;
    }

    // Guter Ruf macht bis zu 20 % bessere Preise.
    float ReputationBonus(VillageState state)
    {
        return 1f + Mathf.Clamp(state.reputation, 0f, 100f) * 0.002f;
    }

    // Das zahlt das Dorf pro Stueck, wenn du eine Ware lieferst, die es braucht.
    // Je leerer das Lager, desto mehr.
    public float PayoutPerUnit(VillagePlan plan, string good)
    {
        VillageState state = GetState(plan);
        float fill = Mathf.Clamp01(Stock(plan, good) / Mathf.Max(1f, stockCapacity));

        return BasePrice(good) * (0.6f + 1.0f * (1f - fill)) * ReputationBonus(state);
    }

    // Das kostet eine Ware pro Stueck, die das Dorf herstellt. Je voller das Lager, desto guenstiger.
    public float PricePerUnit(VillagePlan plan, string good)
    {
        float fill = Mathf.Clamp01(Stock(plan, good) / Mathf.Max(1f, stockCapacity));

        return BasePrice(good) * (1.4f - 0.7f * fill) * 1.2f;
    }

    // Eine Ware beim Dorf abliefern. Gibt das erhaltene Geld zurueck (-1 = das Dorf braucht die Ware nicht).
    // Das Geld wird dem Spieler gutgeschrieben, das Ansehen steigt.
    public float Deliver(VillagePlan plan, string good, float amount)
    {
        if (amount <= 0f || !Needs(plan, good))
            return -1f;

        VillageState state = GetState(plan);

        float payout = PayoutPerUnit(plan, good) * amount;

        float v;
        state.stock.TryGetValue(good, out v);
        state.stock[good] = Mathf.Min(stockCapacity, v + amount);

        state.reputation = Mathf.Min(100f, state.reputation + amount * BasePrice(good) * 0.01f);

        PlayerWallet.Add(payout);

        return payout;
    }

    // Eine Ware beim Dorf kaufen (nur Waren, die es herstellt). false = nicht genug Lager oder Geld.
    // Das Ausgeben der Ware als Gegenstand uebernimmt der Aufrufer.
    public bool Buy(VillagePlan plan, string good, float amount)
    {
        if (amount <= 0f || !Produces(plan, good))
            return false;

        VillageState state = GetState(plan);

        float v;
        state.stock.TryGetValue(good, out v);

        if (v < amount)
            return false;

        if (!PlayerWallet.TrySpend(PricePerUnit(plan, good) * amount))
            return false;

        state.stock[good] = v - amount;
        return true;
    }

    // ---------- Speichern und Laden ----------

    // Schreibt Geld und Doerfer in den Beutel (vom SaveSystem aufgerufen).
    public static void SaveEconomy(StateBag bag)
    {
        PlayerWallet.Save(bag);

        if (Instance != null)
            Instance.WriteEconomy(bag);
    }

    // Stellt Geld und Doerfer aus dem Beutel wieder her.
    public static void LoadEconomy(StateBag bag)
    {
        PlayerWallet.Load(bag);

        if (Instance != null)
            Instance.ReadEconomy(bag);
        else
            pendingEconomy = bag;
    }

    void WriteEconomy(StateBag bag)
    {
        bag.SetFloat("sim", (float)simSeconds);

        List<string> ids = new List<string>();

        foreach (KeyValuePair<int, VillageState> pair in states)
        {
            VillageState st = pair.Value;
            string id = st.id.ToString();

            ids.Add(id);

            bag.SetFloat("r." + id, st.reputation);
            bag.SetFloat("t." + id, (float)st.lastSim);

            List<string> goodNames = new List<string>();

            foreach (KeyValuePair<string, float> g in st.stock)
            {
                goodNames.Add(g.Key.Replace(",", ""));
                bag.SetFloat("s." + id + "." + g.Key.Replace(",", ""), g.Value);
            }

            bag.SetString("g." + id, string.Join(",", goodNames.ToArray()));
        }

        bag.SetString("ids", string.Join(",", ids.ToArray()));
    }

    void ReadEconomy(StateBag bag)
    {
        states.Clear();

        if (bag == null)
        {
            simSeconds = 0.0;
            return;
        }

        simSeconds = bag.GetFloat("sim", 0f);

        string idList = bag.GetString("ids", "");

        if (string.IsNullOrEmpty(idList))
            return;

        foreach (string id in idList.Split(','))
        {
            int parsed;

            if (!int.TryParse(id, out parsed))
                continue;

            VillageState st = new VillageState();
            st.id = parsed;
            st.reputation = bag.GetFloat("r." + id, 0f);
            st.lastSim = bag.GetFloat("t." + id, (float)simSeconds);

            string goodList = bag.GetString("g." + id, "");

            if (!string.IsNullOrEmpty(goodList))
            {
                foreach (string good in goodList.Split(','))
                    st.stock[good] = bag.GetFloat("s." + id + "." + good, 0f);
            }

            states[parsed] = st;
        }
    }

    // ---------- Anzeige zum Testen ----------

    GUIStyle hudStyle;

    void OnGUI()
    {
        if (!showHud || world == null || !world.IsReady || world.player == null)
            return;

        // Das naechste Dorf in Reichweite suchen.
        Vector3 pp = world.player.position;
        VillagePlan near = null;
        float best = float.MaxValue;

        foreach (VillagePlan p in known)
        {
            Vector3 d = p.center - pp;
            d.y = 0f;

            float dist = d.magnitude;

            if (dist < p.radius + 10f && dist < best)
            {
                best = dist;
                near = p;
            }
        }

        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.box);
            hudStyle.alignment = TextAnchor.UpperLeft;
            hudStyle.fontSize = 14;
            hudStyle.normal.textColor = Color.white;
        }

        string text = "Geld: " + PlayerWallet.Money.ToString("0.0");

        if (near != null)
        {
            VillageState st = GetState(near);

            text = near.name + "   Ansehen: " + st.reputation.ToString("0") + "   " + text + "\n";
            text += "stellt her: " + string.Join(", ", near.produces) + "\n";
            text += "braucht: " + string.Join(", ", near.needs) + "\n";

            foreach (KeyValuePair<string, float> g in st.stock)
                text += g.Key + ": " + g.Value.ToString("0") + " / " + stockCapacity.ToString("0") + "\n";
        }

        GUI.Box(new Rect(10f, Screen.height - 30f - (near != null ? 20f * (3 + GetState(near).stock.Count) : 0f),
                         280f, 30f + (near != null ? 20f * (3 + GetState(near).stock.Count) : 0f)), text, hudStyle);
    }

    // =====================================================================
    //  Aufbau im Spiel
    // =====================================================================

    void Update()
    {
        simSeconds += Time.deltaTime;

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

            // Doerfer, deren Mine in Reichweite liegen koennte, rechnen ihre Wirtschaft (und finden ihr Vorkommen).
            if (dc.magnitude <= spawnRadius + plan.radius + mineSearchRadius)
                GetState(plan);

            if (dc.magnitude > spawnRadius + plan.radius + plan.extraRadius + despawnMargin + 20f)
            {
                if (spawnedParts.ContainsKey(plan.id))
                    DespawnAll(plan);

                continue;
            }

            UpdateVillage(plan, pp);
        }

        // Doerfer in Reichweite planen (falls die Gegend noch nicht geladen wurde).
        EnsureRegionsIn(pp, pp, spawnRadius + maxVillageRadius + mineSearchRadius);
    }

    void UpdateVillage(VillagePlan plan, Vector3 playerPos)
    {
        GameObject[] spawned;

        if (!spawnedParts.TryGetValue(plan.id, out spawned))
        {
            spawned = new GameObject[plan.parts.Count];
            spawnedParts[plan.id] = spawned;
        }
        else if (spawned.Length < plan.parts.Count)
        {
            // Neue Teile (Minen) sind dazugekommen.
            System.Array.Resize(ref spawned, plan.parts.Count);
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

        // Minen stehen irgendwo draussen: eigene Hoehe, Boden darunter wird eingeebnet.
        if (part.type == PartMine)
        {
            pos.y = part.pos.y - VoxelWorld.FlattenSink - 0.03f;

            // Einmal einebnen (die Operation bleibt im Gelaende gespeichert).
            if (!part.groundPrepared)
            {
                part.groundPrepared = true;
                world.FlattenArea(new Vector3(part.pos.x, part.pos.y, part.pos.z), part.size * 0.5f, part.yaw, part.pos.y, 2f);
            }
        }

        GameObject go;

        if (part.type == PartStreet)
            go = BuildStreet(part);
        else if (part.type == PartSign)
            go = BuildSign(plan);
        else if (part.type == PartPlatform)
            go = BuildPlatform(part);
        else if (part.type == PartRailBed)
            go = new GameObject("RailBed");
        else if (part.type == PartMine)
            go = BuildMine(plan, part);
        else
            go = BuildBuilding(part);

        go.transform.SetParent(root.transform, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, part.yaw + (part.type >= 0 ? prefabYawOffset : 0f), 0f);

        // Haelt den Boden unter dem Objekt geladen, solange es steht.
        VoxelWorld.AddAnchor(go.transform, 1);

        return go;
    }

    // Verladestation: eigenes Prefab oder Platzhalter (baut MineStation selbst). Das Skript MineStation wird bei Bedarf ergaenzt.
    GameObject BuildMine(VillagePlan plan, VillagePart part)
    {
        GameObject go;

        if (mineStationPrefab != null)
        {
            go = Instantiate(mineStationPrefab);
            go.name = "MineStation";
        }
        else
        {
            go = new GameObject("MineStation");
        }

        MineStation station = go.GetComponent<MineStation>();

        if (station == null)
            station = go.AddComponent<MineStation>();

        station.Setup(plan, part.good, mineStationPrefab == null, part.size);
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

        if (type.fallbackRound || IsLegacyWell(type))
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

    // Bahnsteig (Platzhalter): niedrige Kante aus hellem Beton, Oberflaeche etwas ueber dem Boden.
    GameObject BuildPlatform(VillagePart part)
    {
        GameObject holder = new GameObject("Platform");

        GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = "Slab";
        slab.transform.SetParent(holder.transform, false);
        slab.transform.localScale = new Vector3(part.size.x, 0.4f, part.size.y);
        slab.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        SetColor(slab, new Color(0.62f, 0.62f, 0.60f));

        // Ein kleines Dach ueber der Mitte, damit man den Bahnsteig von weitem erkennt.
        GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "Roof";
        roof.transform.SetParent(holder.transform, false);
        roof.transform.localScale = new Vector3(Mathf.Min(10f, part.size.x * 0.5f), 0.15f, part.size.y - 0.4f);
        roof.transform.localPosition = new Vector3(0f, 2.9f, 0f);
        SetColor(roof, new Color(0.35f, 0.25f, 0.2f));

        for (int i = -1; i <= 1; i += 2)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post";
            post.transform.SetParent(holder.transform, false);
            post.transform.localScale = new Vector3(0.15f, 2.5f, 0.15f);
            post.transform.localPosition = new Vector3(i * roof.transform.localScale.x * 0.45f, 1.65f, 0f);
            SetColor(post, new Color(0.25f, 0.25f, 0.27f));
        }

        return holder;
    }

    GameObject BuildStreet(VillagePart part)
    {
        // Flache Platte knapp ueber dem Boden (begehbar).
        GameObject s = GameObject.CreatePrimitive(PrimitiveType.Cube);
        s.name = "Street";
        s.transform.localScale = new Vector3(part.size.x, 0.3f, part.size.y);

        SetColor(s, streetColor);

        // Die Platte soll mit ihrer Oberkante knapp ueber dem Boden liegen.
        GameObject holder = new GameObject("StreetPart");
        s.transform.SetParent(holder.transform, false);
        s.transform.localPosition = new Vector3(0f, -0.10f, 0f);

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
        // Der Text der Seite +X schaut nach +X (Drehung -90), der der Seite -X nach -X (Drehung +90).
        GameObject front = AddText(root.transform, plan.name, new Vector3(0.07f, 1.9f, 0f), -90f);
        GameObject back = AddText(root.transform, plan.name, new Vector3(-0.07f, 1.9f, 0f), 90f);

        // Zeigt immer nur die Seite, die man gerade sieht (sonst ueberlagern sich beide Texte).
        VillageSign sign = root.AddComponent<VillageSign>();
        sign.front = front;
        sign.back = back;

        return root;
    }

    GameObject AddText(Transform parent, string text, Vector3 localPos, float yaw)
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
        {
            // Eigene Kopie des Schrift-Materials MIT Tiefentest: Sonst wird der Text immer ueber
            // alles gemalt, und die Rueckseite des Schildes schimmert durch (Spiegelschrift).
            if (textMaterial == null)
            {
                textMaterial = new Material(tm.font.material);
                textMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            }

            mr.sharedMaterial = textMaterial;
        }

        return t;
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
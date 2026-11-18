using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// =====================================================================
//  TrackManager: verwaltet alle Gleise der Welt.
//
//  EINMAL in der Szene an ein leeres GameObject haengen (z. B. neben die VoxelWorld).
//  Es kuemmert sich um:
//   - das Gleisnetz (TrackNetwork: Punkte und Stuecke),
//   - das Einebnen des Gelaendes unter den Gleisen (Bett, Einschnitt, Damm),
//   - das Erzeugen der Gleis-Optik (aus Code ODER aus deinem eigenen Modell),
//   - das Speichern und Laden,
//   - das Aufstellen von Lokomotiven.
//
//  EIGENES GLEISMODELL (optional): siehe das Feld "customPiece" unten.
// =====================================================================
// Ein Lok-Typ: Koerper + Antrieb + Tender. Leere Felder = Platzhalter.
[System.Serializable]
public class LocomotiveRecipe
{
    [Tooltip("Kennwort des Typs (wird im Spielstand gespeichert). Nur Buchstaben/Ziffern, eindeutig, nicht aendern.")]
    public string id = "loco";

    [Tooltip("Name im Spiel (Anzeige im Gleisbau).")]
    public string displayName = "Lok";

    [Tooltip("Koerper: Prefab mit dem Skript 'Locomotive' (Plattform, Fuehrerstand, Raeder). Leer = Platzhalter.")]
    public GameObject bodyPrefab;

    [Tooltip("Antrieb: Prefab mit SteamEngine (+ Boiler), DieselEngine oder ElectricEngine. " +
             "Leer = Platzhalter passend zu 'Engine Placeholder'.")]
    public GameObject enginePrefab;
    [Tooltip("Welcher Platzhalter-Antrieb, wenn kein Antriebs-Prefab eingetragen ist.")]
    public FuelType enginePlaceholder = FuelType.Coal;

    [Tooltip("Tender: Prefab mit TrainTender (Kohlelager, Dieseltank oder Batterie). Leer = Platzhalter.")]
    public GameObject tenderPrefab;
    [Tooltip("Welcher Platzhalter-Tender, wenn kein Tender-Prefab eingetragen ist.")]
    public FuelType tenderPlaceholder = FuelType.Coal;
}

// Wagen-Typen: so viele Platzhalter-Arten gibt es.
public enum WagonKind
{
    Hopper   // offener Wagen mit Bodenklappe (Erz, Kohle, Barren ...)
}

// Ein Wagen-Typ. Leeres Prefab = Platzhalter.
[System.Serializable]
public class WagonRecipe
{
    [Tooltip("Kennwort des Typs (wird im Spielstand gespeichert). Nur Buchstaben/Ziffern, eindeutig, nicht aendern.")]
    public string id = "hopper";

    [Tooltip("Name im Spiel (Anzeige im Gleisbau).")]
    public string displayName = "Gueterwagen";

    [Tooltip("Eigenes Modell: Prefab mit dem Skript 'Wagon' (Ladezone, Bodenklappe ...). Leer = Platzhalter.")]
    public GameObject bodyPrefab;

    [Tooltip("Welcher Platzhalter, wenn kein Prefab eingetragen ist.")]
    public WagonKind placeholder = WagonKind.Hopper;
}

public class TrackManager : MonoBehaviour
{
    public static TrackManager Instance { get; private set; }

    [Header("Welt")]
    [Tooltip("Die Voxel-Welt. Leer lassen = wird automatisch gesucht.")]
    public VoxelWorld world;

    [Header("Aussehen (aus Code erzeugt)")]
    [Tooltip("Optional: eigene Materialien. Leer = einfache Standard-Farben.")]
    public Material ballastMaterial;
    public Material sleeperMaterial;
    public Material railMaterial;

    [Header("Eigenes Gleisstueck (optional)")]
    [Tooltip("Ein Prefab fuer EIN Meter Gleis. Wenn gesetzt, wird es statt der erzeugten Optik\n" +
             "alle 1 m entlang der Kurve aufgestellt.\n\n" +
             "SO MODELLIEREN:\n" +
             " - Gleis laeuft entlang der +Z-Achse (nach vorne).\n" +
             " - Ursprung (Pivot) = Mitte des ANFANGS des Stuecks: x = 0 (Mitte zwischen den\n" +
             "   Schienen), z = 0 (Anfang), y = 0 (Boden, auf dem der Schotter aufliegt).\n" +
             " - Das Stueck ist genau 1,0 m lang (z von 0 bis 1).\n" +
             " - Spur 1,0 m (Innenkante bis Innenkante), also Schienenmitte bei x = +-0,535.\n" +
             " - Schiene: 0,07 m breit, Oberkante bei y = 0,39. Schwelle 1,5 x 0,16 x 0,1 m,\n" +
             "   y = 0,20 bis 0,30. Schotter: Oberkante y = 0,20, unten bei y = -0,10.\n" +
             " - Anfang und Ende muessen gleich aussehen (nahtlos aneinander).\n" +
             "Hat das Prefab keinen Collider, kommt ein unsichtbarer Schotter-Collider dazu.")]
    public GameObject customPiece;

    [Header("Gleisende (Prellbock)")]
    [Tooltip("Am freien Ende jedes Gleises automatisch einen Prellbock aufstellen. Er verschwindet, " +
             "sobald das Gleis weitergebaut wird.")]
    public bool autoBufferStops = true;
    [Tooltip("Optional: eigenes Prellbock-Prefab.\n\n" +
             "SO MODELLIEREN:\n" +
             " - Ursprung (Pivot) = Mitte des Gleisendes, auf Hoehe des Bodens (y = 0).\n" +
             " - +Z zeigt vom Gleis WEG (die Richtung, in die das Gleis weiterfuehren wuerde).\n" +
             " - Die Anschlagflaeche (wo die Lok anstoesst) liegt bei z = 0, das Modell erstreckt sich\n" +
             "   nach hinten bis etwa z = 0,5. Schienen-Oberkante liegt bei y = 0,39,\n" +
             "   Schotter-Oberkante bei y = 0,20, Breite ca. 1,7 m.")]
    public GameObject bufferStopPrefab;

    [Header("Gelaende unter den Gleisen")]
    [Tooltip("Halbe Breite des eingeebneten Streifens (Meter).")]
    public float bedHalfWidth = 1.3f;
    [Tooltip("So weich geht das Bett seitlich in das alte Gelaende ueber (Meter).")]
    public float bedBlend = 1.2f;
    [Tooltip("Laenge der einzelnen Einebnungs-Abschnitte (Meter). Kleiner = glatter, aber mehr Operationen.")]
    public float bedStep = 1.0f;
    [Tooltip("Boeschung (Damm / Einschnitt) neben dem Gleis anlegen.")]
    public bool embankments = true;
    public float embankmentReach = 3f;
    [Tooltip("Steilheit der Boeschung (1 = 45 Grad).")]
    public float embankmentSlope = 1f;

    [Header("Lok-Typen")]
    [Tooltip("Alle Lok-Typen, die man aufs Gleis setzen kann. Jeder Typ ist aus drei austauschbaren Teilen " +
             "zusammengesetzt: Koerper, Antrieb und Tender. Ist bei einem Teil kein Prefab eingetragen, " +
             "wird ein einfacher Platzhalter benutzt.\n\n" +
             "Im Spiel wechselt man den Typ im Gleisbau-Modus mit der Taste N (Einstellung am TrackBuilder).")]
    public LocomotiveRecipe[] locomotives =
    {
        new LocomotiveRecipe { id = "steam", displayName = "Dampflok",
            enginePlaceholder = FuelType.Coal, tenderPlaceholder = FuelType.Coal },
        new LocomotiveRecipe { id = "diesel", displayName = "Diesellok",
            enginePlaceholder = FuelType.Diesel, tenderPlaceholder = FuelType.Diesel },
        new LocomotiveRecipe { id = "electric", displayName = "E-Lok",
            enginePlaceholder = FuelType.Electric, tenderPlaceholder = FuelType.Electric }
    };

    [Header("Entladeschacht")]
    [Tooltip("Laenge des Schachts im Gleis (Meter). Dort fehlen Schwellen und die Mitte des Schotters; die Schotterraender bleiben.")]
    public float shaftLength = 3.5f;

    [Header("Wagen und Kupplungen")]
    [Tooltip("Alle Wagen-Typen. Im Gleisbau-Modus wechselt man mit der Taste N erst durch die Loks, dann durch die Wagen.")]
    public WagonRecipe[] wagons =
    {
        new WagonRecipe { id = "hopper", displayName = "Gueterwagen (Bodenklappe)", placeholder = WagonKind.Hopper }
    };

    [Tooltip("Fahrzeuge kuppeln von selbst an, wenn sie langsam genug auf ein freies Ende auffahren.")]
    public bool autoCouple = true;

    [Tooltip("Platzhalter-Loks bekommen im Fuehrerstand ein Instrumentenbrett (Tacho, Dampfdruck, Wasser, Vorraete) " +
             "und Hebel (Regler, Fahrtrichtung, Bremse, Pfeife). Gilt nur fuer Loks ohne eigenes Koerper-Prefab.")]
    public bool cabControls = false;

    [Tooltip("Bis zu dieser Geschwindigkeit (m/s) wird automatisch angekuppelt. Schneller = die Puffer stossen an.")]
    public float couplingSpeed = 2f;

    // Der im Gleisbau-Modus gewaehlte Fahrzeug-Typ: zuerst die Loks, dann die Wagen (Nummer in beiden Listen zusammen).
    [HideInInspector] public int selectedLocomotive;

    // Anzahl aller waehlbaren Fahrzeug-Typen (Loks + Wagen).
    public int VehicleTypeCount
    {
        get
        {
            return (locomotives != null ? locomotives.Length : 0) + (wagons != null ? wagons.Length : 0);
        }
    }

    // Name des Fahrzeug-Typs mit dieser Nummer (Loks zuerst, dann Wagen).
    public string VehicleTypeName(int index)
    {
        int l = locomotives != null ? locomotives.Length : 0;

        if (index < l)
            return locomotives[Mathf.Max(index, 0)].displayName;

        int w = index - l;
        return wagons != null && w >= 0 && w < wagons.Length ? wagons[w].displayName : "";
    }

    // Das Gleisnetz selbst.
    public readonly TrackNetwork Network = new TrackNetwork();

    private readonly Dictionary<int, GameObject> edgeObjects = new Dictionary<int, GameObject>();

    // Schotterstreifen zwischen dicht parallelen Gleisen (Schluessel = beide Stueck-Nummern).
    private readonly Dictionary<long, GameObject> fillers = new Dictionary<long, GameObject>();
    private readonly Dictionary<int, GameObject> bumpers = new Dictionary<int, GameObject>();

    private Material defaultBallast, defaultSleeper, defaultRail;

    void Awake()
    {
        Instance = this;

        if (world == null)
            world = FindObjectOfType<VoxelWorld>();

        TrainCar.AutoCouple = autoCouple;
        TrainCar.CouplingSpeed = couplingSpeed;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        // Mehrspieler: der Host verschickt den Zustand der Loks.
        TrainNet.Tick(Time.deltaTime);

        CullFarTracks();

        // Baut die vorgebauten Strecken zwischen den Bahnhoefen (nur auf dem Host, siehe dort).
        railGenerator.Tick(this);

    }

    // =====================================================================
    //  Vorgebaute Strecken zwischen Bahnhoefen (siehe RailNetworkGenerator)
    // =====================================================================

    [Header("Weichen")]
    [Tooltip("So viele Meter bleibt ein Abzweig nach der Weiche auf der Hoehe des Hauptgleises, bevor er ansteigt oder faellt. " +
             "Verhindert, dass sich beide Gleise im Boden schneiden. 0 = sofort ansteigen.")]
    public float switchLevelRun = 12f;

    [Header("Vorgebaute Strecken")]
    [Tooltip("Aus: Es werden keine Strecken zwischen den Bahnhoefen vorgebaut (der Spieler baut alles selbst).")]
    public bool generateRailNetwork = true;
    [Tooltip("Wie viele Nachbar-Bahnhoefe jeder Bahnhof (hoechstens) anbindet. Ein Stationsgleis hat zwei Enden, mehr als 2 gibt es nicht.")]
    [Range(1, 2)] public int railLinksPerStation = 2;
    [Tooltip("Laengste Luftlinie zwischen zwei Bahnhoefen, die noch verbunden werden (Meter).")]
    public float railMaxLinkDistance = 650f;
    [Tooltip("Eine Strecke wird gebaut, wenn der Spieler naeher als so viele Meter an einem der beiden Bahnhoefe ist.")]
    public float railTriggerDistance = 220f;
    [Tooltip("Groesster Einschnitt bzw. Damm (Meter) gegenueber dem natuerlichen Gelaende. Hoeher = geradere Strecken, aber mehr Erdarbeiten. " +
             "Ein Gleis, das tief im Boden liegt, ist ein Einschnitt: dann hier einen kleineren Wert eintragen (z. B. 1,5).")]
    public float railMaxCutFill = 2.0f;
    [Tooltip("Wie sehr die Wegsuche versucht, am Gelaende zu bleiben statt einzuschneiden oder aufzuschuetten. " +
             "Hoeher = die Strecke folgt dem Gelaende enger (oder weicht Haengen aus), 0 = Erdarbeiten sind egal.")]
    [Range(0f, 8f)] public float railTerrainFollowing = 2.5f;
    [Tooltip("Hoechste Steigung der vorgebauten Strecken in Prozent (die Bauregel erlaubt 8).")]
    [Range(1f, 8f)] public float railMaxGradePercent = 6f;
    [Tooltip("Obergrenze fuer die Wegsuche (Zustaende). Mehr = findet auch verwinkelte Wege, braucht aber laenger.")]
    public int railSearchBudget = 9000;
    [Tooltip("Gleise, die weiter als so viele Meter vom Spieler weg sind, werden ausgeblendet (das Gelaende dort ist ja auch nicht geladen). 0 = nie ausblenden.")]
    public float railVisibleDistance = 180f;

    [Tooltip("Die Kamera des Spielers (wie beim Gleisbauer). Leer = die Kamera mit dem Tag 'MainCamera', sonst die Kamera am Spieler.")]
    public Camera playerCamera;

    // Die Kamera, nach der sich Streckenbau und Ausblenden richten.
    public Camera PlayerCamera
    {
        get
        {
            if (playerCamera == null)
                playerCamera = Camera.main;

            if (playerCamera == null)
            {
                FirstPersonController fpc = FindObjectOfType<FirstPersonController>();

                if (fpc != null)
                    playerCamera = fpc.GetComponentInChildren<Camera>();
            }

            return playerCamera;
        }
    }

    private readonly RailNetworkGenerator railGenerator = new RailNetworkGenerator();
    private float nextCull;
    private readonly List<int> cullIds = new List<int>();

    // Blendet Gleise ein und aus, je nachdem wie weit sie vom Spieler weg sind.
    private void CullFarTracks()
    {
        if (Time.time < nextCull)
            return;

        nextCull = Time.time + 0.5f;

        if (railVisibleDistance <= 1f)
            return;

        Camera cam = PlayerCamera;

        if (cam == null)
            return;

        Vector3 p = cam.transform.position;
        float r = railVisibleDistance;

        foreach (KeyValuePair<int, GameObject> kv in edgeObjects)
        {
            TrackEdge e;

            if (kv.Value == null || !Network.edges.TryGetValue(kv.Key, out e) || e.pts == null || e.pts.Length == 0)
                continue;

            // Naeher als r am Anfang, in der Mitte oder am Ende?
            bool near = Flat(e.pts[0] - p) < r
                     || Flat(e.pts[e.pts.Length / 2] - p) < r
                     || Flat(e.pts[e.pts.Length - 1] - p) < r;

            if (kv.Value.activeSelf != near)
                kv.Value.SetActive(near);
        }

        // Weichenzungen und Hebel gehoeren zu einem Punkt des Netzes.
        foreach (KeyValuePair<int, GameObject> kv in switchMarks)
        {
            TrackNode n;

            if (kv.Value == null || !Network.nodes.TryGetValue(kv.Key, out n))
                continue;

            bool near = Flat(n.pos - p) < r;

            if (kv.Value.activeSelf != near)
                kv.Value.SetActive(near);
        }

        foreach (KeyValuePair<int, SwitchLever> kv in levers)
        {
            TrackNode n;

            if (kv.Value == null || !Network.nodes.TryGetValue(kv.Key, out n))
                continue;

            bool near = Flat(n.pos - p) < r;

            if (kv.Value.gameObject.activeSelf != near)
                kv.Value.gameObject.SetActive(near);
        }

        foreach (KeyValuePair<int, GameObject> kv in bumpers)
        {
            TrackNode n;

            if (kv.Value == null || !Network.nodes.TryGetValue(kv.Key, out n))
                continue;

            bool near = Flat(n.pos - p) < r;

            if (kv.Value.activeSelf != near)
                kv.Value.SetActive(near);
        }
    }

    // Fuer den Streckenbauer: Schneidet die Kurve ein bestehendes Gleis? (a / b = Anfang und Ende, dort wird nicht geprueft.)
    public bool CrossesTracks(TrackEdge test, Vector3 a, Vector3 b)
    {
        Vector3 where;
        return OverlapsExisting(test, true, a, 3f, true, b, 3f, out where);
    }

    // Fuer den Streckenbauer: Liegt ein Gleis (ausser Bruecken) naeher als "radius" an diesem Punkt (Meter, Draufsicht)?
    public bool TrackNear(Vector3 p, float radius)
    {
        foreach (TrackEdge e in Network.edges.Values)
        {
            if (e.pts == null || Network.bridgeEdges.Contains(e.id))
                continue;

            for (int i = 0; i < e.pts.Length; i += 4)
            {
                if (Flat(e.pts[i] - p) < radius)
                    return true;
            }
        }

        return false;
    }

    // Naechste Fahrzeug-Nummer (bei allen Spielern gleich, weil alle dieselben Kommandos ausfuehren).
    private int nextCarId = 1;

    // Fahrzeug nach Nummer (oder null).
    public TrainCar FindCar(int id)
    {
        if (id <= 0)
            return null;

        foreach (TrainCar car in TrainCar.All)
        {
            if (car != null && car.NetId == id)
                return car;
        }

        return null;
    }

    // =====================================================================
    //  Planen und Pruefen
    // =====================================================================

    public struct TrackPlan
    {
        public bool valid;
        public string error;        // Loc-Schluessel, wenn nicht gueltig
        public TrackEdge curve;     // die geplante Kurve (auch fuer die Vorschau)
        public float yawStart, yawEnd;
        public bool hasProblem;     // Wo es hakt (fuer die rote Markierung in der Vorschau)
        public Vector3 problem;
    }

    // Plant ein Gleisstueck von a nach b.
    //  hasStartYaw: Das Stueck muss am Anfang in diese Richtung (Grad) fahren (Anschluss an ein Ende).
    //  hasEndYaw:   Das Stueck muss am Ende in diese Richtung ankommen.
    // Ohne vorgegebene Richtung wird ein sauberer Kreisbogen bzw. eine Gerade gebaut.
    public TrackPlan Plan(Vector3 a, bool hasStartYaw, float startYaw,
                          Vector3 b, bool hasEndYaw, float endYaw)
    {
        TrackPlan plan = new TrackPlan();

        Vector3 flat = b - a;
        flat.y = 0f;
        float chord = flat.magnitude;

        if (chord < 0.5f)
        {
            plan.error = "track.err.short";
            return plan;
        }

        float chordYaw = TrackEdge.DirToYaw(flat);

        if (!hasStartYaw)
            startYaw = chordYaw;

        // Ohne Vorgabe: Endrichtung = Spiegelung der Startrichtung an der Verbindungslinie
        // (ergibt einen Kreisbogen, keine S-Kurve).
        if (!hasEndYaw)
            endYaw = chordYaw + Mathf.DeltaAngle(startYaw, chordYaw);

        plan.yawStart = startYaw;
        plan.yawEnd = endYaw;

        // Man kann nicht "rueckwaerts" aus einem Ende heraus bauen.
        bool badStart = Mathf.Abs(Mathf.DeltaAngle(startYaw, chordYaw)) > 80f;
        bool badEnd = Mathf.Abs(Mathf.DeltaAngle(chordYaw, endYaw)) > 80f;

        if (badStart || badEnd)
        {
            // Keine brauchbare Kurve: als Vorschau eine Gerade zeigen, mit der Markierung an der falschen Seite.
            TrackEdge fallback = new TrackEdge();
            fallback.yawStart = chordYaw;
            fallback.yawEnd = chordYaw;
            fallback.Build(a, b);
            plan.curve = fallback;
            plan.hasProblem = true;
            plan.problem = badStart ? a : b;
            plan.error = "track.err.curve";
            return plan;
        }

        TrackEdge test = new TrackEdge();
        test.yawStart = startYaw;
        test.yawEnd = endYaw;
        test.Build(a, b);
        plan.curve = test;

        if (test.length < TrackSpec.MinEdgeLength)
        {
            plan.error = "track.err.short";
            return plan;
        }

        if (test.length > TrackSpec.MaxEdgeLength)
        {
            plan.error = "track.err.long";
            plan.hasProblem = true;
            plan.problem = b;
            return plan;
        }

        if (Mathf.Abs(b.y - a.y) / chord > TrackSpec.MaxGrade)
        {
            plan.error = "track.err.steep";
            plan.hasProblem = true;
            plan.problem = b;
            return plan;
        }

        // Engste Stelle der Kurve suchen (Richtungsaenderung pro Meter).
        float minRadius = float.MaxValue;
        Vector3 tightest = a;

        for (int i = 0; i + 4 < test.pts.Length; i += 2)
        {
            Vector3 t0 = test.tans[i];     t0.y = 0f;
            Vector3 t1 = test.tans[i + 4]; t1.y = 0f;

            float angle = Vector3.Angle(t0, t1) * Mathf.Deg2Rad;
            float ds = test.cum[i + 4] - test.cum[i];

            if (angle > 0.0001f && ds / angle < minRadius)
            {
                minRadius = ds / angle;
                tightest = test.pts[i + 2];
            }
        }

        if (minRadius < TrackSpec.MinRadius)
        {
            plan.error = "track.err.curve";
            plan.hasProblem = true;
            plan.problem = tightest;
            return plan;
        }

        plan.valid = true;
        return plan;
    }

    // Kreuzt oder beruehrt die Kurve ein bestehendes Gleis? (Nur beim Bauen geprueft,
    // nicht in der Vorschau - es ist etwas rechenaufwendig.)
    // Rund um Start- und Endpunkt (skipRadius) wird nicht geprueft: dort schliesst die Kurve an.
    private bool OverlapsExisting(TrackEdge test, bool hasA, Vector3 posA, float radA,
                                  bool hasB, Vector3 posB, float radB, out Vector3 where)
    {
        const float minDistance = 1.8f;
        where = Vector3.zero;

        for (int i = 0; i < test.pts.Length; i += 3)
        {
            Vector3 p = test.pts[i];

            if (hasA && Flat(p - posA) < radA) continue;
            if (hasB && Flat(p - posB) < radB) continue;

            foreach (TrackEdge e in Network.edges.Values)
            {
                // Schnelltest: Liegt das Stueck sicher zu weit weg (Bogenlaenge >= Luftlinie)?
                if (Flat(e.pts[0] - test.pts[0]) > e.length + test.length + minDistance)
                    continue;

                for (int j = 0; j < e.pts.Length; j += 3)
                {
                    Vector3 q = e.pts[j];

                    if (Mathf.Abs(q.y - p.y) < 1.5f && Flat(q - p) < minDistance)
                    {
                        where = q;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static float Flat(Vector3 v)
    {
        v.y = 0f;
        return v.magnitude;
    }

    // =====================================================================
    //  Bauen und Entfernen
    // =====================================================================

    // Ein Punkt MITTEN auf einem Gleis, an dem ein neues Gleis abzweigen darf (= Weiche).
    public class TrackJoin
    {
        public int edge;          // das Gleisstueck, das dort geteilt wird
        public float dist;        // Strecke vom Anfang des Stuecks
        public Vector3 pos;       // genauer Punkt auf dem Gleis
        public float yawFwd;      // Fahrtrichtung (Grad) des Gleises dort (Richtung wachsender dist)
    }

    // So nah muss man am Gleis zielen, damit eine Weiche entsteht (Meter).
    private const float JoinSnapRadius = 1.0f;

    // Ein Gleis darf nur geteilt werden, wenn kein Fahrzeug darauf steht und beide Teile
    // lang genug bleiben.
    public TrackJoin FindJoin(Vector3 p, float radius)
    {
        int edgeId;
        float dist, distance;

        if (!Network.Project(p, out edgeId, out dist, out distance) || distance > radius)
            return null;

        // Auf der Bruecke einer Drehscheibe entsteht keine Weiche.
        if (Network.bridgeEdges.Contains(edgeId))
            return null;

        TrackEdge e = Network.edges[edgeId];

        if (dist < TrackSpec.MinEdgeLength || dist > e.length - TrackSpec.MinEdgeLength)
            return null;

        foreach (TrainCar car in TrainCar.All)
        {
            if (car != null && car.pos.edge == edgeId)
                return null;
        }

        Vector3 pos, tan;
        e.Evaluate(dist, out pos, out tan);

        TrackJoin j = new TrackJoin();
        j.edge = edgeId;
        j.dist = dist;
        j.pos = pos;
        j.yawFwd = TrackEdge.DirToYaw(tan);

        return j;
    }

    // Von zwei Richtungen (y und y+180) die, die naeher an "target" liegt.
    private static float ClosestYaw(float y, float target)
    {
        return Mathf.Abs(Mathf.DeltaAngle(y, target)) <= 90f ? y : y + 180f;
    }

    private static bool TouchesEdge(TrackNode n, int edgeId)
    {
        return n != null && (n.edgeA == edgeId || n.edgeB == edgeId || n.edgeC == edgeId);
    }

    // Prueft ein Gleisstueck, ohne etwas zu veraendern. Gibt den Plan zurueck (plan.valid).
    // startNode / endNode sind vorhandene freie Enden. Sind sie null, wird geschaut, ob der Punkt
    // mitten auf einem Gleis liegt (dann entsteht dort eine Weiche), sonst ist es ein neuer Punkt.
    // Die Positionen werden auf den genauen Punkt zurechtgerueckt.
    // startYawOverride / endYawOverride (NaN = keine): feste Richtung an einem FREIEN Punkt
    // (fuer Parallelgleise und Weichenkurven).
    private TrackPlan PlanBuild(TrackNode startNode, ref Vector3 startPos,
                                TrackNode endNode, ref Vector3 endPos, bool checkOverlap,
                                out string error, out TrackJoin startJoin, out TrackJoin endJoin,
                                float startYawOverride = float.NaN, float endYawOverride = float.NaN)
    {
        error = null;
        startJoin = null;
        endJoin = null;

        TrackPlan none = new TrackPlan();

        bool hasStart = false, hasEnd = false;
        float startYaw = 0f, endYaw = 0f;

        if (startNode != null)
        {
            startPos = startNode.pos;

            if (!Network.OutwardYaw(startNode, out startYaw))
            {
                error = "track.err.node";
                return none;
            }

            hasStart = true;
        }
        else
        {
            startJoin = FindJoin(startPos, JoinSnapRadius);

            if (startJoin != null)
            {
                startPos = startJoin.pos;
                hasStart = true;
            }
            else if (!float.IsNaN(startYawOverride))
            {
                startYaw = startYawOverride;
                hasStart = true;
            }
        }

        if (endNode != null)
        {
            if (endNode == startNode)
            {
                error = "track.err.node";
                return none;
            }

            endPos = endNode.pos;

            float outward;

            if (!Network.OutwardYaw(endNode, out outward))
            {
                error = "track.err.node";
                return none;
            }

            endYaw = outward + 180f;
            hasEnd = true;
        }
        else
        {
            endJoin = FindJoin(endPos, JoinSnapRadius);

            if (endJoin != null)
            {
                endPos = endJoin.pos;
                hasEnd = true;
            }
            else if (!float.IsNaN(endYawOverride))
            {
                endYaw = endYawOverride;
                hasEnd = true;
            }
        }

        // Nicht zweimal dasselbe Gleis teilen und sich nicht ans eigene Gleis haengen.
        if (startJoin != null && endJoin != null && startJoin.edge == endJoin.edge)
        {
            error = "track.err.node";
            return none;
        }

        if ((startNode != null && endJoin != null && TouchesEdge(startNode, endJoin.edge)) ||
            (endNode != null && startJoin != null && TouchesEdge(endNode, startJoin.edge)))
        {
            error = "track.err.node";
            return none;
        }

        // Die Richtung an einer Weiche: das neue Gleis fuehrt in Richtung des Ziels weiter.
        Vector3 chord = endPos - startPos;
        chord.y = 0f;
        float chordYaw = chord.sqrMagnitude > 1e-6f ? TrackEdge.DirToYaw(chord) : 0f;

        if (startJoin != null)
            startYaw = ClosestYaw(startJoin.yawFwd, chordYaw);

        if (endJoin != null)
            endYaw = ClosestYaw(endJoin.yawFwd, chordYaw);

        TrackPlan plan = Plan(startPos, hasStart, startYaw, endPos, hasEnd, endYaw);

        if (!plan.valid)
        {
            error = plan.error;
            return plan;
        }

        // Mit Anlauf wird der Anstieg kuerzer, also steiler: Auch das muss unter der Grenze bleiben.
        if ((startJoin != null || endJoin != null) && switchLevelRun > 0.01f)
        {
            float run = (startJoin != null ? switchLevelRun : 0f) + (endJoin != null ? switchLevelRun : 0f);
            float usable = Mathf.Max(plan.curve.length * 0.2f, plan.curve.length - Mathf.Min(run, plan.curve.length * 0.8f));

            if (1.5f * Mathf.Abs(endPos.y - startPos.y) / usable > TrackSpec.MaxGrade)
            {
                error = "track.err.steep";
                plan.valid = false;
                plan.hasProblem = true;
                plan.problem = endPos;
                plan.error = error;
                return plan;
            }
        }

        if (checkOverlap)
        {
            bool hasA = startNode != null || startJoin != null;
            bool hasB = endNode != null || endJoin != null;

            // An einer Weiche zweigt das Gleis flach ab: dort ist der Abstand zum Hauptgleis
            // noch klein, deshalb wird ein laengeres Stueck nicht geprueft.
            float radA = startJoin != null ? 14f : 3f;
            float radB = endJoin != null ? 14f : 3f;

            Vector3 where;

            if (OverlapsExisting(plan.curve, hasA, startPos, radA, hasB, endPos, radB, out where))
            {
                error = "track.err.occupied";
                plan.valid = false;
                plan.hasProblem = true;
                plan.problem = where;
                plan.error = error;
                return plan;
            }
        }

        return plan;
    }

    // Teilt das Gleis an der Stelle und gibt den neuen Punkt zurueck.
    private TrackNode SplitForJoin(TrackJoin join, out TrackEdge first, out TrackEdge second)
    {
        int old = join.edge;

        TrackNode p = Network.SplitEdge(old, join.dist, out first, out second);

        if (p == null)
            return null;

        DestroyVisual(old);
        CreateVisual(first);
        CreateVisual(second);

        return p;
    }

    // Legt bei der Weiche fest, welches Gleis das Einzelgleis ist.
    private void FinishSwitch(TrackNode p, TrackEdge added, TrackJoin join, TrackEdge first, TrackEdge second)
    {
        // "first" fuehrt vom Punkt nach hinten (yawFwd + 180), "second" nach vorne (yawFwd).
        // Das neue Gleis liegt auf der Seite, in die es wegfuehrt: das Einzelgleis ist dann das
        // andere der beiden.
        float leave = Network.LeaveYaw(p, added);
        bool forward = Mathf.Abs(Mathf.DeltaAngle(leave, join.yawFwd)) < 90f;

        p.stem = forward ? first.id : second.id;
        p.active = -1;

        RefreshSwitch(p.id);
    }

    // Setzt einen geprueften Plan um. Gibt den Endpunkt zurueck (oder null).
    private TrackNode CommitBuild(TrackPlan plan, TrackNode startNode, Vector3 startPos, TrackJoin startJoin,
                                  TrackNode endNode, Vector3 endPos, TrackJoin endJoin)
    {
        TrackNode startSplit = null, endSplit = null;
        TrackEdge s1 = null, s2 = null, e1 = null, e2 = null;

        if (startNode == null && startJoin != null)
        {
            startNode = SplitForJoin(startJoin, out s1, out s2);
            startSplit = startNode;

            if (startNode == null)
                return null;
        }

        if (endNode == null && endJoin != null)
        {
            endNode = SplitForJoin(endJoin, out e1, out e2);
            endSplit = endNode;

            if (endNode == null)
                return null;
        }

        if (startNode == null) startNode = Network.AddNode(startPos);
        if (endNode == null) endNode = Network.AddNode(endPos);

        TrackEdge edge = Network.AddEdge(startNode.id, endNode.id, plan.yawStart, plan.yawEnd);

        if (edge == null)
            return null;

        // Ein Abzweig bleibt erst ein Stueck eben, dann steigt er zu seinem Ziel an.
        if (switchLevelRun > 0.01f && (startSplit != null || endSplit != null))
        {
            edge.holdA = startSplit != null ? switchLevelRun : 0f;
            edge.holdB = endSplit != null ? switchLevelRun : 0f;
            Network.RebuildEdge(edge);
        }

        FlattenBed(edge);
        ReflattenNeighbors(edge);
        CreateVisual(edge);

        if (startSplit != null) FinishSwitch(startSplit, edge, startJoin, s1, s2);
        if (endSplit != null) FinishSwitch(endSplit, edge, endJoin, e1, e2);

        RefreshBumper(startNode);
        RefreshBumper(endNode);

        return endNode;
    }

    // Findet einen Punkt: nach Nummer, sonst ein freies Ende an genau dieser Stelle.
    private TrackNode ResolveNode(int id, Vector3 pos)
    {
        TrackNode n;

        if (id > 0 && Network.nodes.TryGetValue(id, out n))
            return n;

        return Network.FindNode(pos, 0.3f, true);
    }

    // AUS DEM KOMMANDO: baut das Stueck (bei allen Spielern gleich).
    public bool ExecuteBuild(int startId, Vector3 startPos, int endId, Vector3 endPos,
                             float startYawOverride = float.NaN, float endYawOverride = float.NaN)
    {
        TrackNode startNode = ResolveNode(startId, startPos);
        TrackNode endNode = ResolveNode(endId, endPos);

        string error;
        TrackJoin startJoin, endJoin;
        TrackPlan plan = PlanBuild(startNode, ref startPos, endNode, ref endPos, true,
                                   out error, out startJoin, out endJoin,
                                   startYawOverride, endYawOverride);

        if (!plan.valid)
            return false;

        return CommitBuild(plan, startNode, startPos, startJoin, endNode, endPos, endJoin) != null;
    }

    // Vorschau fuer das Werkzeug: genau dieselbe Pruefung wie beim Bauen (auch "Gleis im Weg").
    // realEnd = der Punkt, an dem das Gleis wirklich endet (z. B. eingerastet auf einem Gleis).
    public TrackPlan PreviewBuild(TrackNode startNode, Vector3 startPos, TrackNode endNode, Vector3 endPos,
                                  out Vector3 realEnd)
    {
        string error;
        TrackJoin sj, ej;

        TrackPlan plan = PlanBuild(startNode, ref startPos, endNode, ref endPos, true, out error, out sj, out ej);
        realEnd = endPos;

        if (!plan.valid && plan.error == null)
            plan.error = error;

        return plan;
    }

    // Vom Werkzeug aufgerufen: prueft hier und schickt dann das Kommando (Einzelspiel: sofort).
    // lastPos = Ort des neuen Endpunkts (dort geht es weiter).
    public bool RequestBuild(TrackNode startNode, Vector3 startPos,
                             TrackNode endNode, Vector3 endPos,
                             out string error, out Vector3 lastPos)
    {
        lastPos = endPos;

        TrackJoin startJoin, endJoin;
        TrackPlan plan = PlanBuild(startNode, ref startPos, endNode, ref endPos, true,
                                   out error, out startJoin, out endJoin);

        if (!plan.valid)
            return false;

        lastPos = endPos;

        NetSync.Submit(NetCommand.TrackBuild(
            startNode != null ? startNode.id : 0, startPos,
            endNode != null ? endNode.id : 0, endPos));

        return true;
    }

    // =====================================================================
    //  Parallelgleise und Weichen (Werkzeug "Parallel")
    // =====================================================================

    [Header("Parallelgleise")]
    [Tooltip("Abstand von Gleismitte zu Gleismitte bei Parallelgleisen und Weichen (Meter). " +
             "Das Gleisbett liegt dabei auf derselben Hoehe, das Gelaende dazwischen wird mit eingeebnet.")]
    public float parallelSpacing = 3.0f;

    // Groesster Abstand zwischen zwei Gleisen, zwischen denen das Gelaende eingeebnet wird (Meter).
    private const float MaxStripGap = 5.5f;

    public struct ParallelSeg
    {
        public Vector3 a, b;
        public float startYaw, endYaw;
    }

    private static Vector3 RightOf(Vector3 tan)
    {
        tan.y = 0f;

        if (tan.sqrMagnitude < 1e-8f)
            return Vector3.right;

        return Vector3.Cross(Vector3.up, tan.normalized);
    }

    // Gleis anzielen: Stueck, Strecke und Seite (+1 = rechts vom wachsenden dist, -1 = links).
    public bool PickTrack(Vector3 aim, float radius, out int edgeId, out float dist, out int side)
    {
        side = 1;
        float distance;

        if (!Network.Project(aim, out edgeId, out dist, out distance) || distance > radius)
            return false;

        // Die Bruecke einer Drehscheibe kann nicht parallel gelegt werden.
        if (Network.bridgeEdges.Contains(edgeId))
            return false;

        Vector3 pos, tan;
        Network.edges[edgeId].Evaluate(dist, out pos, out tan);

        Vector3 d = aim - pos;
        d.y = 0f;
        side = Vector3.Dot(d, RightOf(tan)) >= 0f ? 1 : -1;

        return true;
    }

    // Punkt neben dem Gleis (gleiche Hoehe, seitlich versetzt) und Fahrtrichtung dort.
    // dir: +1 = in Richtung wachsender dist fahren, -1 = entgegengesetzt.
    public Vector3 ParallelPoint(int edgeId, float dist, int side, int dir, out float yaw)
    {
        TrackEdge e = Network.edges[edgeId];

        Vector3 pos, tan;
        e.Evaluate(dist, out pos, out tan);

        yaw = TrackEdge.DirToYaw(tan) + (dir < 0 ? 180f : 0f);

        return pos + RightOf(tan) * (side * Mathf.Max(2.2f, parallelSpacing));
    }

    // Zweiter Klick: Strecke auf demselben Stueck. Liegt der Klick auf einem Nachbarstueck,
    // reicht das Parallelgleis bis zum Ende des ersten Stuecks.
    public bool ResolveParallelEnd(int edge1, float dist1, Vector3 aim, out float dist2, out string error)
    {
        error = null;
        dist2 = dist1;

        int edge2;
        float d, distance;

        if (!Network.Project(aim, out edge2, out d, out distance) || distance > 4f)
        {
            error = "track.err.parallelEdge";
            return false;
        }

        TrackEdge e1 = Network.edges[edge1];

        if (edge2 == edge1)
        {
            dist2 = d;
            return true;
        }

        TrackNode na, nb;
        Network.nodes.TryGetValue(e1.a, out na);
        Network.nodes.TryGetValue(e1.b, out nb);

        if (TouchesEdge(nb, edge2))
        {
            dist2 = e1.length;
            return true;
        }

        if (TouchesEdge(na, edge2))
        {
            dist2 = 0f;
            return true;
        }

        error = "track.err.parallelEdge";
        return false;
    }

    // Plant die Gleise: mit Weiche eine Uebergangskurve (S-Kurve) vom Hauptgleis zum Parallelgleis,
    // danach das Parallelgleis. Die Uebergangskurve wird so lang gewaehlt wie noetig, damit der
    // Kurvenradius reicht.
    public bool PlanParallel(int edgeId, float d1, float d2, int side, bool withSwitch,
                             List<ParallelSeg> segs, List<Vector3> preview, out string error)
    {
        error = null;

        TrackEdge e;

        if (!Network.edges.TryGetValue(edgeId, out e))
        {
            error = "track.err.node";
            return false;
        }

        d1 = Mathf.Clamp(d1, 0f, e.length);
        d2 = Mathf.Clamp(d2, 0f, e.length);

        float length = Mathf.Abs(d2 - d1);
        int dir = d2 >= d1 ? 1 : -1;

        if (length < TrackSpec.MinEdgeLength + 1f)
        {
            error = "track.err.short";
            return false;
        }

        float yawA = 0f, yawB = 0f;
        Vector3 mainPos, mainTan;
        e.Evaluate(d1, out mainPos, out mainTan);

        float transition = 0f;

        if (withSwitch)
        {
            if (FindJoin(mainPos, 0.5f) == null)
            {
                error = "track.err.join";
                return false;
            }

            yawA = TrackEdge.DirToYaw(mainTan) + (dir < 0 ? 180f : 0f);

            // Kuerzeste Uebergangskurve, die gueltig ist.
            bool found = false;
            float maxL = Mathf.Min(length - TrackSpec.MinEdgeLength, TrackSpec.MaxEdgeLength);

            for (float L = 3f; L <= maxL; L += 1f)
            {
                Vector3 b = ParallelPoint(edgeId, d1 + dir * L, side, dir, out yawB);
                TrackPlan p = Plan(mainPos, true, yawA, b, true, yawB);

                if (p.valid)
                {
                    transition = L;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                error = "track.err.parallelShort";
                return false;
            }
        }

        segs.Clear();

        Vector3 startOffset;
        float startYaw;

        if (withSwitch)
        {
            float yawEnd1;
            Vector3 endOffset = ParallelPoint(edgeId, d1 + dir * transition, side, dir, out yawEnd1);

            ParallelSeg s1 = new ParallelSeg();
            s1.a = mainPos;
            s1.b = endOffset;
            s1.startYaw = yawA;
            s1.endYaw = yawEnd1;
            segs.Add(s1);

            startOffset = endOffset;
            startYaw = yawEnd1;
        }
        else
        {
            startOffset = ParallelPoint(edgeId, d1, side, dir, out startYaw);
        }

        float endYaw;
        Vector3 endPoint = ParallelPoint(edgeId, d2, side, dir, out endYaw);

        ParallelSeg s2 = new ParallelSeg();
        s2.a = startOffset;
        s2.b = endPoint;
        s2.startYaw = startYaw;
        s2.endYaw = endYaw;
        segs.Add(s2);

        // Jedes Stueck pruefen (Kurvenradius, Steigung, Laenge).
        if (preview != null)
            preview.Clear();

        foreach (ParallelSeg seg in segs)
        {
            TrackPlan p = Plan(seg.a, true, seg.startYaw, seg.b, true, seg.endYaw);

            if (p.curve != null && preview != null)
                preview.AddRange(p.curve.pts);

            if (!p.valid)
            {
                error = p.error;
                return false;
            }
        }

        return true;
    }

    // ---------- Parallelgleis in Etappen ----------
    //
    // Man kann das freie Ende eines Parallelgleises anklicken und von dort weiterbauen, ohne dass ein
    // neuer Uebergang (Weiche) entsteht. Das neue Stueck bleibt im selben Abstand zum Hauptgleis.

    public TrackNode GetNode(int id)
    {
        TrackNode n;
        return Network.nodes.TryGetValue(id, out n) ? n : null;
    }

    // Zielt man auf das freie Ende eines Gleises, das neben einem anderen Gleis in dessen Richtung
    // verlaeuft? Dann: das Ende (node) und das Hauptgleis (Stueck, Strecke, Seite).
    public bool FindParallelAttach(Vector3 aim, out TrackNode node, out int mainEdge, out float mainDist, out int side)
    {
        mainEdge = -1;
        mainDist = 0f;
        side = 1;

        node = Network.FindNode(aim, 1.8f, true);

        float outYaw;

        if (node == null || node.tt >= 0 || node.Degree != 1 || !Network.OutwardYaw(node, out outYaw))
        {
            node = null;
            return false;
        }

        // Naechster Punkt eines anderen Gleises (nicht das, an dem das Ende haengt).
        float best = float.MaxValue;
        TrackEdge bestEdge = null;
        int bestIndex = -1;

        foreach (TrackEdge e in Network.edges.Values)
        {
            if (e.id == node.edgeA || e.id == node.edgeB || e.id == node.edgeC || Network.bridgeEdges.Contains(e.id))
                continue;

            for (int i = 0; i < e.pts.Length; i++)
            {
                float d = Flat(e.pts[i] - node.pos);

                if (d < best)
                {
                    best = d;
                    bestEdge = e;
                    bestIndex = i;
                }
            }
        }

        float spacing = Mathf.Max(2.2f, parallelSpacing);

        if (bestEdge == null || best < 2.0f || Mathf.Abs(best - spacing) > 0.9f)
        {
            node = null;
            return false;
        }

        Vector3 pos, tan;
        bestEdge.Evaluate(bestEdge.cum[bestIndex], out pos, out tan);

        // Das Ende muss in (oder gegen) die Richtung des Hauptgleises zeigen.
        float mainYaw = TrackEdge.DirToYaw(tan);

        if (Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(outYaw, mainYaw)),
                      Mathf.Abs(Mathf.DeltaAngle(outYaw, mainYaw + 180f))) > 20f)
        {
            node = null;
            return false;
        }

        Vector3 dv = node.pos - pos;
        dv.y = 0f;
        side = Vector3.Dot(dv, RightOf(tan)) >= 0f ? 1 : -1;

        mainEdge = bestEdge.id;
        mainDist = bestEdge.cum[bestIndex];
        return true;
    }

    // Plant das Stueck vom freien Ende "node" bis zur Strecke d2 auf dem Hauptgleis.
    public bool PlanParallelExtend(TrackNode node, int edgeId, float d1, float d2, int side,
                                   List<ParallelSeg> segs, List<Vector3> preview, out string error)
    {
        error = null;
        segs.Clear();

        if (preview != null)
            preview.Clear();

        TrackEdge e;
        float outYaw;

        if (node == null || !Network.edges.TryGetValue(edgeId, out e) || !Network.OutwardYaw(node, out outYaw))
        {
            error = "track.err.node";
            return false;
        }

        d2 = Mathf.Clamp(d2, 0f, e.length);

        Vector3 pos1, tan1;
        e.Evaluate(d1, out pos1, out tan1);

        // Tatsaechlicher seitlicher Abstand des Endes (damit das neue Stueck genau gerade anschliesst).
        Vector3 dv = node.pos - pos1;
        dv.y = 0f;
        float offset = dv.magnitude;

        // Weiterbauen darf man nur in die Richtung, in die das Ende zeigt.
        int dir = d2 >= d1 ? 1 : -1;
        float mainYaw = TrackEdge.DirToYaw(tan1) + (dir < 0 ? 180f : 0f);

        if (Mathf.Abs(Mathf.DeltaAngle(outYaw, mainYaw)) > 25f)
        {
            error = "track.err.parallelDir";
            return false;
        }

        if (Mathf.Abs(d2 - d1) < TrackSpec.MinEdgeLength + 1f)
        {
            error = "track.err.short";
            return false;
        }

        Vector3 pos2, tan2;
        e.Evaluate(d2, out pos2, out tan2);

        ParallelSeg s = new ParallelSeg();
        s.a = node.pos;
        s.startYaw = outYaw;
        s.b = pos2 + RightOf(tan2) * (side * offset);
        s.endYaw = TrackEdge.DirToYaw(tan2) + (dir < 0 ? 180f : 0f);
        segs.Add(s);

        TrackPlan p = Plan(s.a, true, s.startYaw, s.b, true, s.endYaw);

        if (p.curve != null && preview != null)
            preview.AddRange(p.curve.pts);

        if (!p.valid)
        {
            error = p.error;
            return false;
        }

        return true;
    }

    // Baut das Stueck von einem freien Ende aus (prueft auch Ueberlappung).
    public bool RequestParallelExtend(TrackNode node, int edgeId, float d1, float d2, int side, out string error)
    {
        List<ParallelSeg> segs = new List<ParallelSeg>();

        if (!PlanParallelExtend(node, edgeId, d1, d2, side, segs, null, out error))
            return false;

        ParallelSeg seg = segs[0];
        Vector3 a = seg.a, b = seg.b;
        TrackJoin sj, ej;

        TrackPlan plan = PlanBuild(node, ref a, null, ref b, true, out error, out sj, out ej,
                                   seg.startYaw, seg.endYaw);

        if (!plan.valid)
            return false;

        NetSync.Submit(NetCommand.TrackBuild(node.id, seg.a, 0, seg.b, seg.startYaw, seg.endYaw));
        return true;
    }

    // Vorschau fuer das Parallelgleis: prueft jedes Stueck genau wie beim echten Bau (auch Ueberlappung mit
    // anderen Gleisen) und gibt die Kurven samt Ergebnis zurueck. attach != null = Anknuepfen an ein freies Ende.
    // problem = Ort, der den Bau verhindert (falls bekannt).
    public bool PreviewParallel(TrackNode attach, int edgeId, float d1, float d2, int side, bool withSwitch,
                                List<TrackEdge> curves, List<bool> valids,
                                out string error, out bool hasProblem, out Vector3 problem)
    {
        curves.Clear();
        valids.Clear();
        hasProblem = false;
        problem = Vector3.zero;

        List<ParallelSeg> segs = new List<ParallelSeg>();
        string planError;

        bool all = attach != null
            ? PlanParallelExtend(attach, edgeId, d1, d2, side, segs, null, out planError)
            : PlanParallel(edgeId, d1, d2, side, withSwitch, segs, null, out planError);

        string first = all ? null : planError;

        for (int i = 0; i < segs.Count; i++)
        {
            ParallelSeg seg = segs[i];
            Vector3 a = seg.a, b = seg.b;
            TrackJoin sj, ej;
            string err;

            TrackPlan p = PlanBuild(i == 0 ? attach : null, ref a, null, ref b, true, out err, out sj, out ej,
                                    seg.startYaw, seg.endYaw);

            TrackEdge curve = p.curve;
            bool valid = p.valid;

            // Frueh abgelehnt (keine Kurve): wenigstens die reine Kurve zeigen.
            if (curve == null)
            {
                TrackPlan raw = Plan(seg.a, true, seg.startYaw, seg.b, true, seg.endYaw);
                curve = raw.curve;
                valid = false;

                if (!hasProblem && raw.hasProblem)
                {
                    hasProblem = true;
                    problem = raw.problem;
                }
            }

            if (!valid)
            {
                all = false;

                if (first == null)
                    first = err != null ? err : p.error;

                if (!hasProblem && p.hasProblem)
                {
                    hasProblem = true;
                    problem = p.problem;
                }
            }

            if (curve != null)
            {
                curves.Add(curve);
                valids.Add(valid);
            }
        }

        error = first;
        return all;
    }

    // Baut Parallelgleis bzw. Weiche: prueft hier (auch Ueberlappung) und schickt die Kommandos.
    public bool RequestParallel(int edgeId, float d1, float d2, int side, bool withSwitch, out string error)
    {
        List<ParallelSeg> segs = new List<ParallelSeg>();

        if (!PlanParallel(edgeId, d1, d2, side, withSwitch, segs, null, out error))
            return false;

        foreach (ParallelSeg seg in segs)
        {
            Vector3 a = seg.a, b = seg.b;
            TrackJoin sj, ej;

            TrackPlan p = PlanBuild(null, ref a, null, ref b, true, out error, out sj, out ej,
                                    seg.startYaw, seg.endYaw);

            if (!p.valid)
                return false;
        }

        foreach (ParallelSeg seg in segs)
            NetSync.Submit(NetCommand.TrackBuild(0, seg.a, 0, seg.b, seg.startYaw, seg.endYaw));

        return true;
    }

    // Entfernt ein Gleisstueck (nur, wenn kein Wagen darauf steht). Das Gelaende bleibt eingeebnet.
    public bool TryRemove(int edgeId, out string error)
    {
        error = null;

        if (Network.bridgeEdges.Contains(edgeId))
        {
            error = "track.err.bridge";
            return false;
        }

        foreach (TrainCar car in TrainCar.All)
        {
            if (car.pos.edge == edgeId)
            {
                error = "track.err.cars";
                return false;
            }
        }

        TrackEdge removed;
        int nodeA = -1, nodeB = -1;

        if (Network.edges.TryGetValue(edgeId, out removed))
        {
            nodeA = removed.a;
            nodeB = removed.b;

            // Das Einzelgleis einer Weiche bleibt, bis die Abzweige entfernt sind.
            if (IsSwitchStem(nodeA, edgeId) || IsSwitchStem(nodeB, edgeId))
            {
                error = "track.err.stem";
                return false;
            }
        }

        Network.RemoveEdge(edgeId);
        DestroyVisual(edgeId);

        RefreshBumper(nodeA);
        RefreshBumper(nodeB);
        RefreshSwitch(nodeA);
        RefreshSwitch(nodeB);
        return true;
    }

    // Vom Werkzeug aufgerufen: prueft und schickt das Kommando zum Entfernen.
    public bool RequestRemove(int edgeId, out string error)
    {
        error = null;

        if (Network.bridgeEdges.Contains(edgeId))
        {
            error = "track.err.bridge";
            return false;
        }

        foreach (TrainCar car in TrainCar.All)
        {
            if (car != null && car.pos.edge == edgeId)
            {
                error = "track.err.cars";
                return false;
            }
        }

        TrackEdge e;

        if (Network.edges.TryGetValue(edgeId, out e) && (IsSwitchStem(e.a, edgeId) || IsSwitchStem(e.b, edgeId)))
        {
            error = "track.err.stem";
            return false;
        }

        NetSync.Submit(NetCommand.TrackRemove(edgeId));
        return true;
    }

    private bool IsSwitchStem(int nodeId, int edgeId)
    {
        TrackNode n;

        return nodeId >= 0 && Network.nodes.TryGetValue(nodeId, out n) && n.IsSwitch &&
               Network.StemOf(n) == edgeId;
    }

    // AUS DEM KOMMANDO.
    public void ExecuteRemoveEdge(int edgeId)
    {
        string error;
        TryRemove(edgeId, out error);
    }

    // Stellt die Gleisbetten bestehender Gleise neben einem NEUEN Stueck wieder her. Die Boeschung des neuen
    // Stuecks (Erde, die bis zu seiner Hoehe aufsteigt) darf ein benachbartes, tiefer liegendes Gleis nicht
    // zuschuetten, z. B. an einer Weiche, deren Abzweig aus einer Mulde heraus ansteigt.
    private void ReflattenNeighbors(TrackEdge e)
    {
        if (world == null || e.pts == null || e.pts.Length == 0)
            return;

        float reach = bedHalfWidth + (embankments ? embankmentReach : 0f) + 1.5f;
        List<VoxelWorld.PathBox> boxes = new List<VoxelWorld.PathBox>();

        foreach (TrackEdge o in Network.edges.Values)
        {
            if (o.id == e.id || o.pts == null || o.pts.Length == 0 || Network.bridgeEdges.Contains(o.id))
                continue;

            // Schnelltest: Liegt das Stueck sicher zu weit weg?
            if (Flat(o.pts[0] - e.pts[0]) > o.length + e.length + reach)
                continue;

            int n = Mathf.Max(1, Mathf.CeilToInt(o.length / Mathf.Max(0.25f, bedStep)));
            float step = o.length / n;

            for (int i = 0; i < n; i++)
            {
                Vector3 pos, tan;
                o.Evaluate((i + 0.5f) * step, out pos, out tan);

                bool near = false;

                for (int j = 0; j < e.pts.Length; j += 4)
                {
                    if (Flat(e.pts[j] - pos) < reach)
                    {
                        near = true;
                        break;
                    }
                }

                if (!near)
                    continue;

                VoxelWorld.PathBox box = new VoxelWorld.PathBox();
                box.center = pos;
                box.half = new Vector2(bedHalfWidth, step * 0.5f + 0.1f);
                box.yaw = TrackEdge.DirToYaw(tan);
                box.baseY = pos.y;
                boxes.Add(box);
            }
        }

        // Ohne neue Boeschung: nur das Bett selbst wird auf seine Hoehe gebracht.
        if (boxes.Count > 0)
            world.FlattenPath(boxes, bedBlend, 0f, embankmentSlope, 4);
    }

    // Ebnet das Gelaende entlang eines Stuecks ein.
    private void FlattenBed(TrackEdge e)
    {
        if (world == null)
            return;

        int n = Mathf.Max(1, Mathf.CeilToInt(e.length / Mathf.Max(0.25f, bedStep)));
        float step = e.length / n;

        List<VoxelWorld.PathBox> boxes = new List<VoxelWorld.PathBox>(n);

        for (int i = 0; i < n; i++)
        {
            Vector3 pos, tan;
            e.Evaluate((i + 0.5f) * step, out pos, out tan);

            VoxelWorld.PathBox box = new VoxelWorld.PathBox();
            box.center = pos;
            box.half = new Vector2(bedHalfWidth, step * 0.5f + 0.1f);
            box.yaw = TrackEdge.DirToYaw(tan);
            box.baseY = pos.y;

            boxes.Add(box);
        }

        AddStrips(e, boxes);

        world.FlattenPath(boxes, bedBlend,
                          embankments ? embankmentReach : 0f, embankmentSlope, 4);
    }

    // Liegt ein anderes Gleis parallel daneben, wird auch das Gelaende DAZWISCHEN auf dieselbe
    // Hoehe eingeebnet (sonst bliebe zwischen den Betten ein Graben oder Huegel).
    private void AddStrips(TrackEdge e, List<VoxelWorld.PathBox> boxes)
    {
        float stepLength = Mathf.Max(1f, bedStep * 2f);
        int n = Mathf.Max(1, Mathf.CeilToInt(e.length / stepLength));
        float step = e.length / n;

        for (int i = 0; i < n; i++)
        {
            Vector3 pos, tan;
            e.Evaluate((i + 0.5f) * step, out pos, out tan);

            // Naechster Punkt eines anderen Gleises.
            float best = float.MaxValue;
            Vector3 bestPoint = Vector3.zero;

            foreach (TrackEdge other in Network.edges.Values)
            {
                if (other.id == e.id || Network.bridgeEdges.Contains(other.id))
                    continue;

                for (int j = 0; j < other.pts.Length; j += 2)
                {
                    float d = Flat(other.pts[j] - pos);

                    if (d < best && Mathf.Abs(other.pts[j].y - pos.y) < 0.5f)
                    {
                        best = d;
                        bestPoint = other.pts[j];
                    }
                }
            }

            // Nur bei echtem Parallelabstand (nicht dort, wo sich Gleise treffen).
            if (best < 2.0f || best > MaxStripGap)
                continue;

            VoxelWorld.PathBox strip = new VoxelWorld.PathBox();
            strip.center = (pos + bestPoint) * 0.5f;
            strip.half = new Vector2(best * 0.5f + 0.1f, step * 0.5f + 0.1f);
            strip.yaw = TrackEdge.DirToYaw(tan);
            strip.baseY = (pos.y + bestPoint.y) * 0.5f;

            boxes.Add(strip);
        }
    }

    // =====================================================================
    //  Optik
    // =====================================================================

    private void DestroyVisual(int edgeId)
    {
        GameObject go;

        if (edgeObjects.TryGetValue(edgeId, out go))
        {
            if (go != null)
                Destroy(go);

            edgeObjects.Remove(edgeId);
        }

        // Streifen, an denen dieses Stueck beteiligt war, gehen mit.
        List<long> gone = null;

        foreach (long key in fillers.Keys)
        {
            if ((int)(key >> 32) == edgeId || (int)(key & 0xFFFFFFFFL) == edgeId)
            {
                if (gone == null)
                    gone = new List<long>();

                gone.Add(key);
            }
        }

        if (gone != null)
        {
            foreach (long key in gone)
            {
                if (fillers[key] != null)
                    Destroy(fillers[key]);

                fillers.Remove(key);
            }
        }
    }

    private static long PairKey(int a, int b)
    {
        int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
        return ((long)lo << 32) | (uint)hi;
    }

    // Fuellt das Schotterbett zwischen diesem Stueck und dicht parallelen Nachbarn auf. Jedes Paar bekommt
    // genau einen Streifen: Er entsteht, wenn das zweite der beiden Stuecke seine Optik bekommt.
    private void CreateFillers(TrackEdge e)
    {
        if (e.pts == null || e.pts.Length == 0 || Network.bridgeEdges.Contains(e.id))
            return;

        foreach (TrackEdge o in Network.edges.Values)
        {
            if (o.id == e.id || Network.bridgeEdges.Contains(o.id) || !edgeObjects.ContainsKey(o.id))
                continue;

            long key = PairKey(e.id, o.id);

            if (fillers.ContainsKey(key))
                continue;

            // Schnelltest: sicher zu weit weg (Bogenlaenge >= Luftlinie)?
            if (Flat(o.pts[0] - e.pts[0]) > o.length + e.length + MaxStripGap + 2f)
                continue;

            Mesh mesh = TrackMesh.FillerMesh(e, o, MaxStripGap);

            if (mesh == null)
                continue;

            EnsureMaterials();

            GameObject go = new GameObject("Gleisfuellung_" + e.id + "_" + o.id);
            go.transform.SetParent(transform, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            Vector3 ps = transform.lossyScale;
            go.transform.localScale = new Vector3(
                ps.x != 0f ? 1f / ps.x : 1f, ps.y != 0f ? 1f / ps.y : 1f, ps.z != 0f ? 1f / ps.z : 1f);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ballastMaterial != null ? ballastMaterial : defaultBallast;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            BuildSurface surface = go.AddComponent<BuildSurface>();
            surface.type = BuildSurfaceType.Ground;

            fillers[key] = go;
        }
    }

    private void EnsureMaterials()
    {
        if (defaultBallast != null)
            return;

        defaultBallast = MakeMaterial(new Color(0.46f, 0.45f, 0.43f), 0f);
        defaultSleeper = MakeMaterial(new Color(0.33f, 0.22f, 0.13f), 0f);
        defaultRail = MakeMaterial(new Color(0.30f, 0.30f, 0.33f), 0.5f);
    }

    public static Material MakeMaterial(Color color, float metallic)
    {
        Shader shader = Shader.Find("Standard");

        if (shader == null)
            shader = Shader.Find("Diffuse");

        Material m = new Material(shader);
        m.color = color;

        if (m.HasProperty("_Metallic"))
            m.SetFloat("_Metallic", metallic);

        if (m.HasProperty("_Glossiness"))
            m.SetFloat("_Glossiness", 0.2f);

        return m;
    }

    private void CreateVisual(TrackEdge e)
    {
        DestroyVisual(e.id);

        GameObject go = new GameObject("Gleis_" + e.id);
        go.transform.SetParent(transform, false);

        // Das Mesh liegt in Weltkoordinaten: Das Objekt muss deshalb genau im Nullpunkt stehen,
        // egal wo (und wie gross) der TrackManager selbst in der Szene ist.
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;

        Vector3 ps = transform.lossyScale;
        go.transform.localScale = new Vector3(
            ps.x != 0f ? 1f / ps.x : 1f, ps.y != 0f ? 1f / ps.y : 1f, ps.z != 0f ? 1f / ps.z : 1f);

        // Boden-Flaeche fuer das Bausystem (man darf auf dem Gleisbett bauen).
        BuildSurface surface = go.AddComponent<BuildSurface>();
        surface.type = BuildSurfaceType.Ground;

        bool custom = customPiece != null;

        try
        {
            if (custom)
                BuildCustomPieces(e, go.transform);
            else
                BuildGeneratedMesh(e, go);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Gleis konnte nicht erzeugt werden: " + ex);
        }

        // Ohne eigenen Collider ein unsichtbarer Schotter-Collider, damit man darauf laufen kann.
        if (go.GetComponentInChildren<Collider>() == null)
        {
            MeshCollider mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = TrackMesh.BallastMesh(e);
        }

        edgeObjects[e.id] = go;

        CreateFillers(e);
    }

    private void BuildGeneratedMesh(TrackEdge e, GameObject go)
    {
        EnsureMaterials();

        Mesh mesh = TrackMesh.FullMesh(e);

        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new Material[]
        {
            ballastMaterial != null ? ballastMaterial : defaultBallast,
            sleeperMaterial != null ? sleeperMaterial : defaultSleeper,
            railMaterial != null ? railMaterial : defaultRail
        };

        // Der Schotter dient auch als Boden zum Laufen.
        MeshCollider mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = TrackMesh.BallastMesh(e);
    }

    // Stellt das eigene Gleisstueck alle ~1 m entlang der Kurve auf.
    private void BuildCustomPieces(TrackEdge e, Transform parent)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(e.length / TrackSpec.PieceLength));
        float step = e.length / count;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos, tan;
            e.Evaluate(i * step, out pos, out tan);

            GameObject piece = Instantiate(customPiece, parent);
            piece.transform.position = pos;
            piece.transform.rotation = Quaternion.LookRotation(tan, Vector3.up);

            // Leicht dehnen, damit kein Spalt zwischen den Stuecken bleibt.
            piece.transform.localScale = new Vector3(1f, 1f, step / TrackSpec.PieceLength);
        }
    }

    // =====================================================================
    //  Prellbock am Gleisende
    // =====================================================================

    private void RefreshBumper(TrackNode node)
    {
        if (node != null)
            RefreshBumper(node.id);
    }

    // Stellt am Punkt einen Prellbock auf, wenn dort ein Gleis endet - sonst entfernt er ihn.
    private void RefreshBumper(int nodeId)
    {
        if (nodeId < 0)
            return;

        TrackNode node;
        Network.nodes.TryGetValue(nodeId, out node);

        float yaw = 0f;
        bool needed = autoBufferStops && node != null && node.tt < 0 && Network.OutwardYaw(node, out yaw);

        GameObject existing;
        bumpers.TryGetValue(nodeId, out existing);

        if (!needed)
        {
            if (existing != null)
                Destroy(existing);

            bumpers.Remove(nodeId);
            return;
        }

        if (existing != null)
            Destroy(existing);

        GameObject go = bufferStopPrefab != null
            ? Instantiate(bufferStopPrefab)
            : BuildDefaultBumper();

        go.name = "Prellbock_" + nodeId;
        go.transform.SetParent(transform, true);
        go.transform.SetPositionAndRotation(node.pos, Quaternion.Euler(0f, yaw, 0f));

        bumpers[nodeId] = go;
    }

    private void RefreshAllBumpers()
    {
        foreach (TrackNode n in Network.nodes.Values)
        {
            RefreshBumper(n.id);
            RefreshSwitch(n.id);
        }
    }

    // =====================================================================
    //  Weichen: Hebel zum Umlegen
    // =====================================================================

    [Header("Weichen")]
    [Tooltip("Optional: eigener Weichenhebel. Ursprung = Fuss des Hebels. Ein Kind namens \"Handle\" " +
             "kippt beim Umlegen (um die lokale Z-Achse). Ohne Eintrag wird ein einfacher Hebel gebaut.")]
    public GameObject switchLeverPrefab;
    [Tooltip("Taste zum Umlegen (auf den Hebel zielen).")]
    public KeyCode switchKey = KeyCode.F;
    [Tooltip("Abstand des Hebels von der Gleismitte (Meter).")]
    public float leverSideOffset = 1.9f;
    [Tooltip("So weit kippt der Hebel (Grad).")]
    public float leverTilt = 30f;

    private readonly Dictionary<int, SwitchLever> levers = new Dictionary<int, SwitchLever>();

    // Fahrtrichtung (Grad), mit der man vom Einzelgleis in die Weiche hineinfaehrt.
    private float SwitchTravelYaw(TrackNode n)
    {
        TrackEdge stem = Network.edges[Network.StemOf(n)];
        return Network.LeaveYaw(n, stem) + 180f;
    }

    // Stellt am Punkt einen Hebel auf, wenn dort eine Weiche ist - sonst entfernt er ihn.
    private void RefreshSwitch(int nodeId)
    {
        if (nodeId < 0)
            return;

        TrackNode n;
        Network.nodes.TryGetValue(nodeId, out n);

        SwitchLever lever;
        levers.TryGetValue(nodeId, out lever);

        if (n == null || !n.IsSwitch)
        {
            if (lever != null)
                Destroy(lever.gameObject);

            levers.Remove(nodeId);
            DestroySwitchMark(nodeId);
            return;
        }

        int b0, b1;
        Network.Branches(n, out b0, out b1);

        float travel = SwitchTravelYaw(n);

        // Der abzweigende Ast (der mit der groesseren Abweichung von der Geraden).
        TrackEdge e0 = Network.edges[b0];
        TrackEdge e1 = Network.edges[b1];
        float d0 = Mathf.Abs(Mathf.DeltaAngle(travel, Network.LeaveYaw(n, e0)));
        float d1 = Mathf.Abs(Mathf.DeltaAngle(travel, Network.LeaveYaw(n, e1)));
        TrackEdge diverge = d1 > d0 ? e1 : e0;

        Vector3 right = Quaternion.Euler(0f, travel, 0f) * Vector3.right;
        Vector3 away = TrackEdge.YawToDir(Network.LeaveYaw(n, diverge));

        // Hebel auf die Seite, auf die das Abzweiggleis NICHT zeigt.
        float side = Vector3.Dot(away, right) > 0f ? -1f : 1f;
        Vector3 position = n.pos + right * (side * leverSideOffset);

        // Auf dem Boden stehen (Gelaende dort), sonst auf Hoehe des Gleisbetts.
        if (world != null)
            position.y = world.GetSurfaceWorldY(position.x, position.z);

        if (lever == null)
        {
            lever = CreateLever(nodeId);
            levers[nodeId] = lever;
        }

        lever.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, travel, 0f));

        // Hebelstellung: Gerade = in die eine Richtung, Abzweig = in die andere.
        bool divergeActive = Network.ActiveBranch(n) == diverge.id;
        lever.SetTilt(divergeActive ? leverTilt : -leverTilt);

        // Die Weiche selbst zeigt, wohin sie gestellt ist.
        RefreshSwitchMark(n, Network.edges[Network.ActiveBranch(n)], divergeActive);
    }

    // ---------- Anzeige der Weichenstellung (Platzhalter) ----------

    private readonly Dictionary<int, GameObject> switchMarks = new Dictionary<int, GameObject>();
    private Material markMaterial;

    // Legt auf die Schienen des gestellten Astes (die ersten Meter ab der Weiche) helle graue "Zungen".
    // So sieht man, wohin die Weiche steht: Die Zungen liegen immer an dem Ast, auf den sie gestellt ist.
    private void RefreshSwitchMark(TrackNode n, TrackEdge active, bool diverging)
    {
        DestroySwitchMark(n.id);

        // Blankes Grau, etwas heller als die Schienen: Die Zungen liegen am gestellten Ast.
        if (markMaterial == null) markMaterial = MakeMaterial(new Color(0.62f, 0.63f, 0.66f), 0.5f);

        GameObject root = new GameObject("Weichenzungen_" + n.id);
        root.transform.SetParent(transform, true);
        switchMarks[n.id] = root;

        Material mat = markMaterial;

        const float length = 3.2f;
        const float seg = 0.4f;

        float railX = TrackSpec.Gauge * 0.5f + TrackSpec.RailHeadWidth * 0.5f;
        float railY = TrackSpec.BallastHeight + TrackSpec.SleeperHeight + TrackSpec.RailHeight * 0.5f + 0.012f;
        bool fromStart = active.a == n.id;
        int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(length, active.length * 0.5f) / seg));

        for (int i = 0; i < count; i++)
        {
            float d = (i + 0.5f) * seg;
            float dist = fromStart ? d : active.length - d;

            Vector3 pos, tan;
            active.Evaluate(dist, out pos, out tan);

            Vector3 fwd = fromStart ? tan : -tan;
            fwd.y = 0f;

            if (fwd.sqrMagnitude < 1e-6f)
                continue;

            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = "Zunge";
                g.transform.SetParent(root.transform, true);
                g.transform.position = pos + right * (side * railX) + Vector3.up * railY;
                g.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                g.transform.localScale = new Vector3(TrackSpec.RailHeadWidth * 1.5f, TrackSpec.RailHeight * 0.7f, seg * 1.08f);
                g.GetComponent<Renderer>().sharedMaterial = mat;
                Destroy(g.GetComponent<Collider>());
            }
        }
    }

    private void DestroySwitchMark(int nodeId)
    {
        GameObject old;

        if (switchMarks.TryGetValue(nodeId, out old))
        {
            if (old != null)
                Destroy(old);

            switchMarks.Remove(nodeId);
        }
    }

    private SwitchLever CreateLever(int nodeId)
    {
        bool ownPrefab = switchLeverPrefab != null;
        GameObject go = ownPrefab ? Instantiate(switchLeverPrefab) : BuildDefaultLever();
        go.name = "Weichenhebel_" + nodeId;
        go.transform.SetParent(transform, true);

        SwitchLever lever = go.GetComponent<SwitchLever>();

        if (lever == null)
            lever = go.AddComponent<SwitchLever>();

        lever.nodeId = nodeId;

        if (lever.handle == null)
            lever.handle = go.transform.Find("Handle");

        lever.Init();

        // Zum Zielen: ein Trigger-Collider, falls das Prefab keinen hat. Der Platzhalter-Hebel hat nie einen
        // (Destroy wirkt erst am Ende des Bildes, darum wird hier nicht danach gesucht).
        if (!ownPrefab || go.GetComponentInChildren<Collider>() == null)
        {
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.7f, 0f);
            box.size = new Vector3(0.7f, 1.4f, 0.7f);
        }

        return lever;
    }

    // Einfacher Hebel: Sockel plus kippbarer Griff. Ursprung = Fuss.
    private GameObject BuildDefaultLever()
    {
        GameObject root = new GameObject("Weichenhebel");

        Color dark = new Color(0.2f, 0.2f, 0.22f);
        Color red = new Color(0.75f, 0.15f, 0.1f);

        Block(root.transform, "Sockel", new Vector3(0f, 0.15f, 0f), new Vector3(0.4f, 0.3f, 0.4f), dark, false);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(root.transform, false);
        handle.transform.localPosition = new Vector3(0f, 0.3f, 0f);

        Block(handle.transform, "Stange", new Vector3(0f, 0.45f, 0f), new Vector3(0.08f, 0.9f, 0.08f), dark, false);
        Block(handle.transform, "Griff", new Vector3(0f, 0.95f, 0f), new Vector3(0.22f, 0.12f, 0.12f), red, false);

        return root;
    }

    // Text, wenn man auf einen Weichenhebel zielt (der PlayerInteractor zeigt ihn an).
    public string SwitchPrompt(int nodeId)
    {
        TrackNode n;

        if (!Network.nodes.TryGetValue(nodeId, out n) || !n.IsSwitch)
            return "";

        return Loc.T("track.switch.prompt", BranchName(n));
    }

    // Weiche umlegen (als Kommando, damit es bei allen Spielern passiert).
    public void ToggleSwitch(int nodeId)
    {
        NetSync.Submit(NetCommand.SwitchSet(nodeId, -1));
    }

    // Text, wenn man auf den Pfosten eines Drehscheiben-Ausgangs zielt.
    public string TurntablePrompt(int ttId, int slot)
    {
        TurntableView view;

        if (!turntables.TryGetValue(ttId, out view))
            return "";

        if (view.IsMoving)
            return Loc.T("track.tt.moving");

        int halfSlots = view.data.slots / 2;

        if (slot % halfSlots == view.data.slot % halfSlots)
            return Loc.T("track.tt.here");

        return Loc.T("track.tt.prompt", slot + 1);
    }

    // Name der Richtung, auf die die Weiche gestellt ist.
    private string BranchName(TrackNode n)
    {
        TrackEdge active = Network.edges[Network.ActiveBranch(n)];
        float delta = Mathf.DeltaAngle(SwitchTravelYaw(n), Network.LeaveYaw(n, active));

        if (Mathf.Abs(delta) < 3f)
            return Loc.T("track.switch.straight");

        return Loc.T(delta > 0f ? "track.switch.right" : "track.switch.left");
    }

    void OnGUI()
    {
        if (Time.time < noticeUntil && !string.IsNullOrEmpty(notice) && !SettingsMenu.IsOpen)
        {
            GameSettings.DrawBoxBelowCenter(notice);
            return;
        }
    }

    // AUS DEM KOMMANDO: Weiche umlegen (edgeId < 0 = auf den anderen Abzweig).
    public void ExecuteSwitch(int nodeId, int edgeId)
    {
        if (Network.SetSwitch(nodeId, edgeId))
            RefreshSwitch(nodeId);
    }

    // Einfacher Prellbock aus Quadern. Ursprung = Gleisende, +Z = vom Gleis weg.
    private GameObject BuildDefaultBumper()
    {
        GameObject root = new GameObject("Prellbock");

        Color red = new Color(0.55f, 0.12f, 0.1f);
        Color dark = new Color(0.2f, 0.2f, 0.22f);

        Block(root.transform, "Fuss", new Vector3(0f, 0.275f, 0.3f), new Vector3(1.7f, 0.15f, 0.6f), dark, false);
        Block(root.transform, "PfostenL", new Vector3(-0.7f, 0.65f, 0.3f), new Vector3(0.15f, 0.9f, 0.15f), dark, true);
        Block(root.transform, "PfostenR", new Vector3(0.7f, 0.65f, 0.3f), new Vector3(0.15f, 0.9f, 0.15f), dark, true);
        Block(root.transform, "Balken", new Vector3(0f, 0.9f, 0.125f), new Vector3(1.7f, 0.25f, 0.25f), red, true);

        return root;
    }

    private static void Block(Transform parent, string name, Vector3 pos, Vector3 scale,
                              Color color, bool collider)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color, 0.1f);

        if (!collider)
            Destroy(g.GetComponent<Collider>());
    }

    // =====================================================================
    //  Drehscheibe
    // =====================================================================

    [Header("Drehscheibe")]
    [Tooltip("Halbmesser der Grube in Metern. Die Bruecke ist doppelt so lang (bei 8 m also 16 m).")]
    [Range(5f, 25f)] public float turntableRadius = 8f;
    [Tooltip("Anzahl der Ausgaenge am Grubenrand (gerade Zahl, 4 bis 16).")]
    [Range(4, 16)] public int turntableSlots = 8;
    [Tooltip("Wie schnell sich die Bruecke dreht (Grad pro Sekunde).")]
    public float turntableSpeed = 20f;
    [Tooltip("Optional: eigenes Modell. Ursprung = Mitte der Grube. Ein Kind namens \"Bridge\" ist die " +
             "drehbare Bruecke (Laenge entlang der lokalen Z-Achse, Oberkante des Gleises auf Bodenhoehe " +
             "+ 0,2 m). Ohne Eintrag wird ein Platzhalter aus Quadern gebaut.")]
    public GameObject turntablePrefab;

    private readonly Dictionary<int, TurntableView> turntables = new Dictionary<int, TurntableView>();
    private int nextTurntable = 1;

    // Kurze Meldung (z. B. "Auf der Bruecke steht ein Fahrzeug"), wenn man am Pfosten F drueckt.
    private string notice;
    private float noticeUntil;

    private void Notify(string key)
    {
        notice = Loc.T(key);
        noticeUntil = Time.time + 2.5f;
    }

    private int SlotCount
    {
        get { return Mathf.Clamp((turntableSlots / 2) * 2, 4, 16); }
    }

    // Steht ein Fahrzeug auf diesem Gleisstueck?
    private bool CarOnEdge(int edgeId)
    {
        foreach (TrainCar car in TrainCar.All)
        {
            if (car != null && car.pos.edge == edgeId)
                return true;
        }

        return false;
    }

    // Die Drehscheibe, in deren Grube dieser Punkt liegt (oder null).
    public TurntableData TurntableAt(Vector3 p)
    {
        foreach (TurntableView v in turntables.Values)
        {
            if (v != null && Flat(p - v.data.center) < v.data.radius + 0.5f)
                return v.data;
        }

        return null;
    }

    // Darf hier eine Drehscheibe stehen? (Kein Gleis und keine andere Drehscheibe im Weg.)
    public bool CheckTurntable(Vector3 center, float radius, out string error)
    {
        error = null;

        foreach (TurntableView v in turntables.Values)
        {
            if (v != null && Flat(v.data.center - center) < v.data.radius + radius + 3f)
            {
                error = "track.err.occupied";
                return false;
            }
        }

        foreach (TrackEdge e in Network.edges.Values)
        {
            for (int i = 0; i < e.pts.Length; i += 2)
            {
                if (Flat(e.pts[i] - center) < radius + 1.5f && Mathf.Abs(e.pts[i].y - center.y) < 1.5f)
                {
                    error = "track.err.occupied";
                    return false;
                }
            }
        }

        return true;
    }

    // Vom Werkzeug aufgerufen: prueft und schickt das Kommando (Einzelspiel: sofort).
    public bool RequestTurntable(Vector3 center, float baseYaw, out string error)
    {
        float radius = Mathf.Clamp(turntableRadius, 5f, 25f);

        if (!CheckTurntable(center, radius, out error))
            return false;

        NetSync.Submit(NetCommand.TurntablePlace(center, baseYaw, SlotCount, radius));
        return true;
    }

    // AUS DEM KOMMANDO: stellt die Drehscheibe auf (bei allen Spielern gleich).
    public void ExecuteTurntablePlace(Vector3 center, float baseYaw, int slots, float radius)
    {
        slots = Mathf.Clamp((slots / 2) * 2, 4, 16);

        TurntableData d = new TurntableData();
        d.id = nextTurntable++;
        d.center = center;
        d.baseYaw = baseYaw;
        d.slots = slots;
        d.radius = Mathf.Clamp(radius, 5f, 25f);
        d.nodeIds = new int[slots];
        d.slot = 0;

        for (int k = 0; k < slots; k++)
        {
            TrackNode n = Network.AddNode(d.SlotPos(k));
            n.tt = d.id;
            n.ttYaw = d.SlotYaw(k);
            d.nodeIds[k] = n.id;
        }

        TrackEdge bridge = Network.AddBridge(d.nodeIds[0], d.nodeIds[slots / 2], d.SlotYaw(0) + 180f);

        if (bridge != null)
            d.bridgeEdge = bridge.id;

        FlattenTurntable(d);
        CreateTurntableView(d);
    }

    private void CreateTurntableView(TurntableData d)
    {
        GameObject go = new GameObject("Drehscheibe_" + d.id);
        go.transform.SetParent(transform, false);

        TurntableView view = go.AddComponent<TurntableView>();
        view.Build(d, turntablePrefab, turntableSpeed);

        turntables[d.id] = view;
    }

    // Ebnet das Gelaende unter der Grube ein (Streifen, die zusammen einen Kreis ergeben).
    private void FlattenTurntable(TurntableData d)
    {
        if (world == null)
            return;

        float r = d.radius + 0.6f;
        List<VoxelWorld.PathBox> boxes = new List<VoxelWorld.PathBox>();

        for (float z = -r + 0.5f; z <= r - 0.5f + 0.01f; z += 1f)
        {
            float w = Mathf.Sqrt(Mathf.Max(0f, r * r - z * z));

            if (w < 0.3f)
                continue;

            VoxelWorld.PathBox box = new VoxelWorld.PathBox();
            box.center = d.center + new Vector3(0f, 0f, z);
            box.half = new Vector2(w, 0.55f);
            box.yaw = 0f;
            box.baseY = d.center.y;
            boxes.Add(box);
        }

        world.FlattenPath(boxes, bedBlend, embankments ? embankmentReach : 0f, embankmentSlope, 4);
    }

    // Welches Brueckenende (a) steht nach dem Drehen auf dem Ausgang "slot" oder dem gegenueber?
    // Gewaehlt wird der kuerzere Drehweg. Liefert -1, wenn die Bruecke dort schon steht.
    private int PickTurnTarget(TurntableData d, int slot)
    {
        int n = d.slots;
        int half = n / 2;
        int k1 = ((slot % n) + n) % n;
        int k2 = (k1 + half) % n;

        if (k1 % half == d.slot % half)
            return -1;

        float from = d.SlotYaw(d.slot);
        float d1 = Mathf.Abs(Mathf.DeltaAngle(from, d.SlotYaw(k1)));
        float d2 = Mathf.Abs(Mathf.DeltaAngle(from, d.SlotYaw(k2)));

        return d1 <= d2 ? k1 : k2;
    }

    // Steht ein Fahrzeug so, dass es beim Drehen mit einem Teil auf der Bruecke und mit dem anderen
    // auf dem Gleis davor stuende? (Fahrzeuge ganz auf der Bruecke sind in Ordnung und drehen mit.)
    private bool CarNearBridgeEnds(TurntableData d)
    {
        int half = d.slots / 2;
        int nodeA = d.nodeIds[d.slot];
        int nodeB = d.nodeIds[(d.slot + half) % d.slots];

        foreach (TrainCar car in TrainCar.All)
        {
            if (car == null)
                continue;

            TrackEdge e;

            if (!Network.edges.TryGetValue(car.pos.edge, out e))
                continue;

            // Halbe Fahrzeuglaenge (grob) plus Sicherheitsabstand.
            float reach = car.wheelBase * 0.5f + Mathf.Max(car.frontClearance, car.rearClearance) + 0.5f;

            if (e.id == d.bridgeEdge)
            {
                if (car.pos.dist < reach || e.length - car.pos.dist < reach)
                    return true;

                continue;
            }

            if (e.a == nodeA || e.a == nodeB)
            {
                if (car.pos.dist < reach)
                    return true;
            }

            if (e.b == nodeA || e.b == nodeB)
            {
                if (e.length - car.pos.dist < reach)
                    return true;
            }
        }

        return false;
    }

    // Vom Pfosten aufgerufen: Bruecke auf Ausgang "slot" drehen.
    public bool RequestTurntableRotate(int ttId, int slot)
    {
        TurntableView v;

        if (!turntables.TryGetValue(ttId, out v))
            return false;

        // Waehrend einer Drehung nimmt die Scheibe keine neuen Befehle an.
        if (v.IsMoving)
            return false;

        if (PickTurnTarget(v.data, slot) < 0)
            return false;

        if (CarNearBridgeEnds(v.data))
        {
            Notify("track.err.ttNear");
            return false;
        }

        NetSync.Submit(NetCommand.TurntableRotate(ttId, slot));
        return true;
    }

    // AUS DEM KOMMANDO: Bruecke loesen und drehen; sie wird nach der Drehung am Ziel eingehaengt.
    public void ExecuteTurntableRotate(int ttId, int slot)
    {
        TurntableView v;

        if (!turntables.TryGetValue(ttId, out v) || v.IsMoving)
            return;

        int target = PickTurnTarget(v.data, slot);

        if (target < 0 || CarNearBridgeEnds(v.data))
            return;

        v.detached = Network.DetachBridge(v.data.bridgeEdge);

        if (v.detached == null)
            return;

        v.TurnTo(target);
    }

    // Von der Drehscheibe aufgerufen, wenn die Drehung zu Ende ist: Bruecke einhaengen.
    public void FinishTurntable(int ttId)
    {
        TurntableView v;

        if (!turntables.TryGetValue(ttId, out v) || v.detached == null)
            return;

        TurntableData d = v.data;
        int half = d.slots / 2;

        Network.AttachBridge(v.detached, d.nodeIds[d.slot], d.nodeIds[(d.slot + half) % d.slots], d.SlotYaw(d.slot) + 180f);
        v.detached = null;
    }

    // Kann man die Drehscheibe abbauen? (Keine Gleise mehr dran, kein Fahrzeug darauf.)
    private bool CanRemoveTurntable(TurntableView v, out string error)
    {
        error = null;

        if (v.IsMoving)
        {
            error = "track.err.ttMoving";
            return false;
        }

        foreach (int id in v.data.nodeIds)
        {
            TrackNode n;

            if (Network.nodes.TryGetValue(id, out n) && Network.ExternalEdgeCount(n) > 0)
            {
                error = "track.err.ttBusy";
                return false;
            }
        }

        if (CarOnEdge(v.data.bridgeEdge))
        {
            error = "track.err.ttCars";
            return false;
        }

        return true;
    }

    public bool RequestTurntableRemove(int ttId, out string error)
    {
        error = null;
        TurntableView v;

        if (!turntables.TryGetValue(ttId, out v))
            return false;

        if (!CanRemoveTurntable(v, out error))
            return false;

        NetSync.Submit(NetCommand.TurntableRemove(ttId));
        return true;
    }

    // AUS DEM KOMMANDO: baut die Drehscheibe ab. Das eingeebnete Gelaende bleibt.
    public void ExecuteTurntableRemove(int ttId)
    {
        TurntableView v;
        string error;

        if (!turntables.TryGetValue(ttId, out v) || !CanRemoveTurntable(v, out error))
            return;

        Network.RemoveBridge(v.data.bridgeEdge);

        foreach (int id in v.data.nodeIds)
            Network.nodes.Remove(id);

        Destroy(v.gameObject);
        turntables.Remove(ttId);
    }

    // Aus dem Speicherstand (siehe SaveAll).
    private void LoadTurntable(string text)
    {
        string[] s = (text ?? "").Split(';');

        if (s.Length < 10)
            return;

        TurntableData d = new TurntableData();
        d.id = (int)P(s[0]);
        d.center = new Vector3(P(s[1]), P(s[2]), P(s[3]));
        d.baseYaw = P(s[4]);
        d.slots = Mathf.Clamp(((int)P(s[5]) / 2) * 2, 4, 16);
        d.radius = P(s[6]);
        d.bridgeEdge = (int)P(s[7]);
        d.slot = (int)P(s[8]);

        string[] ids = s[9].Split(',');

        if (ids.Length != d.slots)
            return;

        d.nodeIds = new int[d.slots];

        for (int k = 0; k < d.slots; k++)
        {
            d.nodeIds[k] = (int)P(ids[k]);

            TrackNode n;

            if (!Network.nodes.TryGetValue(d.nodeIds[k], out n))
                return;

            n.tt = d.id;
            n.ttYaw = d.SlotYaw(k);
        }

        int half = d.slots / 2;
        d.slot = Mathf.Clamp(d.slot, 0, d.slots - 1);

        Network.AddBridge(d.nodeIds[d.slot], d.nodeIds[(d.slot + half) % d.slots], d.SlotYaw(d.slot) + 180f, d.bridgeEdge);
        CreateTurntableView(d);
    }

    // =====================================================================
    //  Entladeschacht
    // =====================================================================

    // Darf an dieser Stelle (Stueck, Meter der Mitte) ein Schacht hin?
    public bool CheckShaft(int edgeId, float dist, out string error)
    {
        error = null;
        TrackEdge e;

        if (!Network.edges.TryGetValue(edgeId, out e) || Network.bridgeEdges.Contains(edgeId))
        {
            error = "track.err.shaftBridge";
            return false;
        }

        float half = Mathf.Clamp(shaftLength, 2f, 12f) * 0.5f;
        float d0 = dist - half, d1 = dist + half;

        if (d0 < 1.5f || d1 > e.length - 1.5f)
        {
            error = "track.err.shaftEdge";
            return false;
        }

        for (int i = 0; i < e.shafts.Count; i++)
        {
            if (e.shafts[i].x - 0.5f < d1 && e.shafts[i].y + 0.5f > d0)
            {
                error = "track.err.shaftOverlap";
                return false;
            }
        }

        return true;
    }

    // Vom Werkzeug: Schacht an der angezielten Stelle bauen (Kommando).
    public bool RequestShaft(Vector3 aim, out string error)
    {
        int edge;
        float dist, distance;

        if (!Network.Project(aim, out edge, out dist, out distance) || distance > 2.5f)
        {
            error = "track.err.noTrack";
            return false;
        }

        if (!CheckShaft(edge, dist, out error))
            return false;

        NetSync.Submit(NetCommand.TrackShaft(edge, dist, Mathf.Clamp(shaftLength, 2f, 12f)));
        return true;
    }

    // Vom Werkzeug: Schacht entfernen (das Gleis wird wieder normal; die Grube bleibt, man kann sie zuschuetten).
    public bool RequestShaftRemove(Vector3 aim)
    {
        int edge;
        float dist, distance;

        if (!Network.Project(aim, out edge, out dist, out distance) || distance > 2.5f)
            return false;

        TrackEdge e;

        if (!Network.edges.TryGetValue(edge, out e) || !e.InShaft(dist))
            return false;

        NetSync.Submit(NetCommand.TrackShaftRemove(edge, dist));
        return true;
    }

    // AUS DEM KOMMANDO: Schacht anlegen, Gleis neu zeichnen und die Grube darunter graben.
    public void ExecuteShaft(int edgeId, float dist, float length)
    {
        TrackEdge e;

        if (!Network.edges.TryGetValue(edgeId, out e))
            return;

        float half = Mathf.Clamp(length, 2f, 12f) * 0.5f;
        float d0 = Mathf.Max(0f, dist - half), d1 = Mathf.Min(e.length, dist + half);

        e.shafts.Add(new Vector2(d0, d1));
        e.shafts.Sort((p, q) => p.x.CompareTo(q.x));

        CreateVisual(e);

        // Grube: Kugeln entlang der Mitte (gleich bei allen Spielern, laeuft ueber den Operationen-Verlauf der Welt).
        if (world != null)
        {
            float step = 0.5f;
            int count = Mathf.Max(1, Mathf.CeilToInt((d1 - d0) / step));

            for (int i = 0; i <= count; i++)
            {
                Vector3 pos, tan;
                e.Evaluate(Mathf.Lerp(d0 + 0.2f, d1 - 0.2f, count == 0 ? 0f : (float)i / count), out pos, out tan);
                world.DigSphere(pos - Vector3.up * 0.5f, 0.6f);
            }

            world.RebuildDirtyNow();
        }
    }

    public void ExecuteShaftRemove(int edgeId, float dist)
    {
        TrackEdge e;

        if (!Network.edges.TryGetValue(edgeId, out e))
            return;

        for (int i = 0; i < e.shafts.Count; i++)
        {
            if (dist > e.shafts[i].x && dist < e.shafts[i].y)
            {
                e.shafts.RemoveAt(i);
                CreateVisual(e);
                return;
            }
        }
    }

    // =====================================================================
    //  Fahrzeuge
    // =====================================================================

    // Stellt eine Lok auf das Gleis. Gibt sie zurueck (oder null).
    public Locomotive SpawnLocomotive(int edge, float dist, int dir, int recipeIndex = -1, int netId = 0)
    {
        TrackEdge e;

        if (!Network.edges.TryGetValue(edge, out e))
            return null;

        if (locomotives == null || locomotives.Length == 0)
        {
            Debug.LogWarning("TrackManager: Die Liste 'Locomotives' ist leer.", this);
            return null;
        }

        if (recipeIndex < 0 || recipeIndex >= locomotives.Length)
            recipeIndex = Mathf.Clamp(selectedLocomotive, 0, locomotives.Length - 1);

        LocomotiveRecipe recipe = locomotives[recipeIndex];

        // 1) Koerper
        GameObject go = recipe.bodyPrefab != null ? Instantiate(recipe.bodyPrefab) : TrainPlaceholders.Body(cabControls);
        go.transform.SetParent(null);

        Locomotive loco = go.GetComponent<Locomotive>();

        if (loco == null)
            loco = go.AddComponent<Locomotive>();

        // 2) Antrieb und Tender an ihre Plaetze setzen.
        Transform engineParent = loco.engineSocket != null ? loco.engineSocket : go.transform;
        Transform tenderParent = loco.tenderSocket != null ? loco.tenderSocket : go.transform;

        if (recipe.enginePrefab != null)
            Instantiate(recipe.enginePrefab, engineParent);
        else
            TrainPlaceholders.Engine(recipe.enginePlaceholder, engineParent);

        if (recipe.tenderPrefab != null)
            Instantiate(recipe.tenderPrefab, tenderParent);
        else
            TrainPlaceholders.Tender(recipe.tenderPlaceholder, tenderParent);

        loco.recipeId = recipe.id;
        loco.RefreshParts();

        // Nummer vergeben (bei allen Spielern gleich) bzw. aus dem Spielstand uebernehmen.
        loco.NetId = netId > 0 ? netId : nextCarId;
        nextCarId = Mathf.Max(nextCarId, loco.NetId + 1);

        // Genug Platz zu den Enden lassen, damit beide Achsen auf dem Gleis stehen.
        float margin = loco.wheelBase * 0.5f + Mathf.Max(loco.frontClearance, loco.rearClearance) + 0.2f;
        dist = Mathf.Clamp(dist, Mathf.Min(margin, e.length * 0.5f), Mathf.Max(e.length - margin, e.length * 0.5f));

        TrackPos p = new TrackPos();
        p.edge = edge;
        p.dist = dist;
        p.dir = dir >= 0 ? 1 : -1;

        loco.Place(p);
        return loco;
    }

    // Vom Werkzeug: Lok oder Wagen aufstellen (Kommando). "selectedLocomotive" zaehlt erst die Loks, dann die Wagen.
    public void RequestSpawnLoco(int edge, float dist, int dir)
    {
        int total = VehicleTypeCount;

        if (total == 0)
            return;

        int recipe = Mathf.Clamp(selectedLocomotive, 0, total - 1);
        NetSync.Submit(NetCommand.LocoSpawn(edge, dist, dir, recipe));
    }

    // Vom Werkzeug: Fahrzeug entfernen (Kommando).
    public void RequestRemoveCar(TrainCar car)
    {
        if (car == null)
            return;

        if (car.NetId > 0)
            NetSync.Submit(NetCommand.LocoRemove(car.NetId));
        else
            Destroy(car.gameObject);
    }

    // AUS DEM KOMMANDO.
    public void ExecuteSpawnLoco(int edge, float dist, int dir, int recipe)
    {
        int l = locomotives != null ? locomotives.Length : 0;

        if (recipe >= l)
            SpawnWagon(edge, dist, dir, recipe - l);
        else
            SpawnLocomotive(edge, dist, dir, recipe);
    }

    // Stellt einen Wagen auf das Gleis. Gibt ihn zurueck (oder null).
    public Wagon SpawnWagon(int edge, float dist, int dir, int recipeIndex, int netId = 0)
    {
        TrackEdge e;

        if (!Network.edges.TryGetValue(edge, out e))
            return null;

        if (wagons == null || wagons.Length == 0)
        {
            Debug.LogWarning("TrackManager: Die Liste 'Wagons' ist leer.", this);
            return null;
        }

        recipeIndex = Mathf.Clamp(recipeIndex, 0, wagons.Length - 1);
        WagonRecipe recipe = wagons[recipeIndex];

        GameObject go = recipe.bodyPrefab != null ? Instantiate(recipe.bodyPrefab) : TrainPlaceholders.WagonBody(recipe.placeholder);
        go.transform.SetParent(null);

        Wagon wagon = go.GetComponent<Wagon>();

        if (wagon == null)
            wagon = go.AddComponent<Wagon>();

        wagon.recipeId = recipe.id;
        wagon.RefreshParts();

        wagon.NetId = netId > 0 ? netId : nextCarId;
        nextCarId = Mathf.Max(nextCarId, wagon.NetId + 1);

        float margin = wagon.wheelBase * 0.5f + Mathf.Max(wagon.frontClearance, wagon.rearClearance) + 0.2f;
        dist = Mathf.Clamp(dist, Mathf.Min(margin, e.length * 0.5f), Mathf.Max(e.length - margin, e.length * 0.5f));

        TrackPos p = new TrackPos();
        p.edge = edge;
        p.dist = dist;
        p.dir = dir >= 0 ? 1 : -1;

        wagon.Place(p);
        return wagon;
    }

    // Index eines Wagen-Typs anhand seines Kennworts (-1 = nicht gefunden).
    private int WagonRecipeIndex(string id)
    {
        if (wagons == null || string.IsNullOrEmpty(id))
            return -1;

        for (int i = 0; i < wagons.Length; i++)
        {
            if (wagons[i] != null && wagons[i].id == id)
                return i;
        }

        return -1;
    }

    // AUS DEM KOMMANDO: Kuppeln, Abkuppeln, Wagen-Funktionen.
    public void ExecuteCouple(int idA, int endA, int idB, int endB)
    {
        TrainCar a = FindCar(idA);
        TrainCar b = FindCar(idB);

        if (a != null && b != null && TrainCar.Couple(a, endA, b, endB))
            a.SnapConsist();
    }

    public void ExecuteUncouple(int id, int end)
    {
        TrainCar.Uncouple(FindCar(id), end);
    }

    public void ExecuteCarFunction(int id, int function)
    {
        Wagon wagon = FindCar(id) as Wagon;

        if (wagon != null && function == 0)
            wagon.ToggleHatch();
    }

    public void ExecuteRemoveLoco(int netId)
    {
        TrainCar car = FindCar(netId);

        if (car != null)
            Destroy(car.gameObject);
    }

    // Nummer eines Lok-Typs anhand seines Kennworts (-1 = nicht gefunden).
    private int RecipeIndex(string id)
    {
        if (locomotives == null || string.IsNullOrEmpty(id))
            return -1;

        for (int i = 0; i < locomotives.Length; i++)
        {
            if (locomotives[i] != null && locomotives[i].id == id)
                return i;
        }

        return -1;
    }

    // =====================================================================
    //  Speichern und Laden
    // =====================================================================

    private static string F(float v)
    {
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    private static float P(string s)
    {
        float v;
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        return v;
    }

    // Schaechte als Text: "von:bis,von:bis" (leer = keine).
    private static string ShaftsToString(TrackEdge e)
    {
        string text = "";

        for (int i = 0; i < e.shafts.Count; i++)
            text += (i > 0 ? "," : "") + F(e.shafts[i].x) + ":" + F(e.shafts[i].y);

        return text;
    }

    private static void StringToShafts(TrackEdge e, string text)
    {
        e.shafts.Clear();

        if (string.IsNullOrEmpty(text))
            return;

        string[] parts = text.Split(',');

        for (int i = 0; i < parts.Length; i++)
        {
            string[] p = parts[i].Split(':');

            if (p.Length == 2)
                e.shafts.Add(new Vector2(P(p[0]), P(p[1])));
        }
    }

    public static void SaveAll(StateBag bag)
    {
        if (Instance == null || bag == null)
            return;

        TrackNetwork net = Instance.Network;

        // Zaehler fuer neue Nummern: muessen bei allen Spielern gleich sein.
        bag.SetInt("nn", net.nextNode);
        bag.SetInt("ne", net.nextEdge);
        bag.SetInt("nc", Instance.nextCarId);

        bag.SetInt("n.count", net.nodes.Count);
        int i = 0;

        foreach (TrackNode n in net.nodes.Values)
        {
            // Ab Feld 5: Weiche (Einzelgleis; gestellter Abzweig), sonst -1.
            int stem = n.IsSwitch ? net.StemOf(n) : -1;
            int active = n.IsSwitch ? net.ActiveBranch(n) : -1;

            bag.SetString("n" + i, n.id + ";" + F(n.pos.x) + ";" + F(n.pos.y) + ";" + F(n.pos.z) +
                                   ";" + stem + ";" + active);
            i++;
        }

        // Die Bruecken der Drehscheiben stehen bei den Drehscheiben (nicht hier).
        int edgeCount = 0;

        foreach (TrackEdge e in net.edges.Values)
        {
            if (net.bridgeEdges.Contains(e.id))
                continue;

            bag.SetString("e" + edgeCount, e.id + ";" + e.a + ";" + e.b + ";" + F(e.yawStart) + ";" + F(e.yawEnd) + ";" + F(e.holdA) + ";" + F(e.holdB) + ";" + ShaftsToString(e));
            edgeCount++;
        }

        bag.SetInt("e.count", edgeCount);

        // Drehscheiben: Nummer, Mitte, Richtung, Ausgaenge, Halbmesser, Brueckenstueck, Stellung, Punkte.
        bag.SetInt("nt", Instance.nextTurntable);
        bag.SetInt("t.count", Instance.turntables.Count);
        i = 0;

        foreach (TurntableView view in Instance.turntables.Values)
        {
            TurntableData d = view.data;
            string ids = string.Join(",", System.Array.ConvertAll(d.nodeIds, x => x.ToString()));

            bag.SetString("t" + i, d.id + ";" + F(d.center.x) + ";" + F(d.center.y) + ";" + F(d.center.z) +
                                   ";" + F(d.baseYaw) + ";" + d.slots + ";" + F(d.radius) +
                                   ";" + d.bridgeEdge + ";" + d.slot + ";" + ids);
            i++;
        }

        // Welche Strecken der Streckenbauer schon gebaut oder verworfen hat.
        RailNetworkGenerator.Save(bag);

        // Kupplungen: jede Verbindung einmal (von dem Fahrzeug mit der kleineren Nummer aus).
        int linkCount = 0;

        foreach (TrainCar car in TrainCar.All)
        {
            if (car == null || car.NetId <= 0)
                continue;

            for (int end = 0; end < 2; end++)
            {
                TrainCar other = car.LinkAt(end);

                if (other != null && other.NetId > car.NetId)
                {
                    bag.SetString("cl" + linkCount, car.NetId + ";" + end + ";" + other.NetId + ";" + car.LinkEndAt(end));
                    linkCount++;
                }
            }
        }

        bag.SetInt("cl.count", linkCount);

        bag.SetInt("c.count", TrainCar.All.Count);

        for (i = 0; i < TrainCar.All.Count; i++)
        {
            bag.SetString("c" + i, TrainCar.All[i].Serialize());

            // Zustand der Teile am Fahrzeug (Kessel, Kohlelager ...): Schluessel "c<Nr>.<Teil>.<Name>".
            ISaveable[] parts = TrainCar.All[i].GetComponentsInChildren<ISaveable>(true);

            for (int j = 0; j < parts.Length; j++)
            {
                StateBag tmp = new StateBag();
                parts[j].SaveState(tmp);

                for (int k = 0; k < tmp.keys.Count; k++)
                    bag.SetString("c" + i + "." + j + "." + tmp.keys[k], tmp.values[k]);
            }
        }
    }

    // Baut das ganze Gleisnetz aus einem Speicherstand neu auf.
    public static void LoadAll(StateBag bag)
    {
        TrackManager m = Instance;

        if (m == null)
            return;

        m.ClearAll();

        if (bag == null || !bag.Has("n.count"))
            return;

        int nodeCount = bag.GetInt("n.count");
        List<int[]> switches = new List<int[]>();

        for (int i = 0; i < nodeCount; i++)
        {
            string[] s = bag.GetString("n" + i).Split(';');

            if (s.Length < 4)
                continue;

            m.Network.AddNode(new Vector3(P(s[1]), P(s[2]), P(s[3])), (int)P(s[0]));

            if (s.Length >= 6 && (int)P(s[4]) >= 0)
                switches.Add(new int[] { (int)P(s[0]), (int)P(s[4]), (int)P(s[5]) });
        }

        int edgeCount = bag.GetInt("e.count");

        for (int i = 0; i < edgeCount; i++)
        {
            string[] s = bag.GetString("e" + i).Split(';');

            if (s.Length < 5)
                continue;

            TrackEdge e = m.Network.AddEdge((int)P(s[1]), (int)P(s[2]), P(s[3]), P(s[4]), (int)P(s[0]));

            // Anlauf eines Weichen-Abzweigs (neuere Spielstaende).
            if (e != null && s.Length >= 7)
            {
                e.holdA = P(s[5]);
                e.holdB = P(s[6]);

                if (e.holdA > 0.01f || e.holdB > 0.01f)
                    m.Network.RebuildEdge(e);
            }

            // Entladeschaechte (ab dem 8. Feld).
            if (e != null && s.Length >= 8)
                StringToShafts(e, s[7]);

            if (e != null)
                m.CreateVisual(e);
        }

        RailNetworkGenerator.Load(bag);

        // Drehscheiben (nach den Gleisen, vor den Fahrzeugen: Loks koennen auf der Bruecke stehen).
        m.nextTurntable = Mathf.Max(m.nextTurntable, bag.GetInt("nt", 1));
        int turntableCount = bag.GetInt("t.count", 0);

        for (int i = 0; i < turntableCount; i++)
            m.LoadTurntable(bag.GetString("t" + i));

        // Weichen: Einzelgleis und Stellung wiederherstellen.
        foreach (int[] sw in switches)
        {
            TrackNode n;

            if (m.Network.nodes.TryGetValue(sw[0], out n) && n.IsSwitch)
            {
                n.stem = sw[1];
                n.active = sw[2];
            }
        }

        m.RefreshAllBumpers();

        int carCount = bag.GetInt("c.count");

        for (int i = 0; i < carCount; i++)
            m.SpawnFromString(bag.GetString("c" + i), bag, i);

        // Kupplungen wiederherstellen (die Wagen stehen schon an ihren gespeicherten Plaetzen).
        int linkCount = bag.GetInt("cl.count", 0);

        for (int i = 0; i < linkCount; i++)
        {
            string[] l = bag.GetString("cl" + i).Split(';');

            if (l.Length >= 4)
                TrainCar.Couple(m.FindCar((int)P(l[0])), (int)P(l[1]), m.FindCar((int)P(l[2])), (int)P(l[3]));
        }

        // Zaehler wie beim Speichern (es koennen Nummern vergeben gewesen sein, die es nicht mehr gibt).
        m.Network.nextNode = Mathf.Max(m.Network.nextNode, bag.GetInt("nn", 1));
        m.Network.nextEdge = Mathf.Max(m.Network.nextEdge, bag.GetInt("ne", 1));
        m.nextCarId = Mathf.Max(m.nextCarId, bag.GetInt("nc", 1));
    }

    // Positionen aller Fahrzeuge (zum Vorladen des Gelaendes beim Laden).
    public static List<Vector3> CarPositions(StateBag bag)
    {
        List<Vector3> result = new List<Vector3>();

        if (bag == null || !bag.Has("n.count"))
            return result;

        // Die Positionen liegen auf den Gleisen: Wir nehmen die Gleispunkte als Naeherung.
        int nodeCount = bag.GetInt("n.count");

        for (int i = 0; i < nodeCount; i++)
        {
            string[] s = bag.GetString("n" + i).Split(';');

            if (s.Length >= 4)
                result.Add(new Vector3(P(s[1]), P(s[2]), P(s[3])));
        }

        return result;
    }

    private void SpawnFromString(string text, StateBag bag, int index)
    {
        if (string.IsNullOrEmpty(text))
            return;

        string[] s = text.Split(';');

        // Format: Typ;Kante;Strecke;Richtung;weitere Werte ...
        if (s.Length >= 7 && s[0] == "wagon")
        {
            int wRecipe = Mathf.Max(WagonRecipeIndex(s[5]), 0);
            Wagon wagon = SpawnWagon((int)P(s[1]), P(s[2]), (int)P(s[3]), wRecipe, (int)P(s[6]));

            if (wagon == null)
                return;

            wagon.Deserialize(s);
            LoadCarParts(wagon, bag, index);
            return;
        }

        if (s.Length < 4 || s[0] != "loco")
            return;

        // Das 8. Feld ist das Kennwort des Lok-Typs (aeltere Staende haben keins: dann der erste Typ).
        int recipe = s.Length > 7 ? RecipeIndex(s[7]) : 0;

        // Das 9. Feld ist die Fahrzeug-Nummer (aeltere Staende: neue vergeben).
        int carId = s.Length > 8 ? (int)P(s[8]) : 0;

        Locomotive loco = SpawnLocomotive((int)P(s[1]), P(s[2]), (int)P(s[3]), Mathf.Max(recipe, 0), carId);

        if (loco == null)
            return;

        loco.Deserialize(s);

        LoadCarParts(loco, bag, index);
    }

    // Teile am Fahrzeug wiederherstellen (Kessel, Kohlelager, Ladung, Klappe ...).
    private void LoadCarParts(TrainCar car, StateBag bag, int index)
    {
        ISaveable[] parts = car.GetComponentsInChildren<ISaveable>(true);

        for (int j = 0; j < parts.Length; j++)
        {
            string prefix = "c" + index + "." + j + ".";
            StateBag tmp = new StateBag();

            for (int k = 0; k < bag.keys.Count; k++)
            {
                if (bag.keys[k].StartsWith(prefix, System.StringComparison.Ordinal))
                    tmp.keys.Add(bag.keys[k].Substring(prefix.Length));
                else
                    continue;

                tmp.values.Add(bag.values[k]);
            }

            if (tmp.keys.Count > 0)
                parts[j].LoadState(tmp);
        }
    }

    // Loescht alle Gleise und Fahrzeuge (vor dem Laden).
    public void ClearAll()
    {
        for (int i = TrainCar.All.Count - 1; i >= 0; i--)
        {
            if (TrainCar.All[i] != null)
                Destroy(TrainCar.All[i].gameObject);
        }

        TrainCar.All.Clear();
        nextCarId = 1;
        RailNetworkGenerator.Reset();

        foreach (GameObject go in edgeObjects.Values)
        {
            if (go != null)
                Destroy(go);
        }

        edgeObjects.Clear();

        foreach (GameObject go in fillers.Values)
        {
            if (go != null)
                Destroy(go);
        }

        fillers.Clear();

        foreach (GameObject go in bumpers.Values)
        {
            if (go != null)
                Destroy(go);
        }

        bumpers.Clear();

        foreach (SwitchLever lever in levers.Values)
        {
            if (lever != null)
                Destroy(lever.gameObject);
        }

        levers.Clear();

        foreach (GameObject mark in switchMarks.Values)
        {
            if (mark != null)
                Destroy(mark);
        }

        switchMarks.Clear();

        foreach (TurntableView view in turntables.Values)
        {
            if (view != null)
                Destroy(view.gameObject);
        }

        turntables.Clear();
        nextTurntable = 1;
        Network.Clear();
    }
}

// =====================================================================
//  TrackMesh: erzeugt die Gleis-Optik aus Code (Schotter, Schwellen, Schienen).
//  Alles mit flachen Flaechen (harte Kanten), damit es "gebaut" aussieht.
// =====================================================================
public static class TrackMesh
{
    // Ein Rahmen entlang der Kurve.
    private struct Frame
    {
        public Vector3 pos, right, up, fwd;
    }

    private static Frame GetFrame(TrackEdge e, float dist)
    {
        Vector3 pos, tan;
        e.Evaluate(dist, out pos, out tan);

        Vector3 flat = tan;
        flat.y = 0f;

        if (flat.sqrMagnitude < 1e-6f)
            flat = Vector3.forward;

        flat.Normalize();

        Frame f = new Frame();
        f.pos = pos;
        f.fwd = tan;
        f.right = Vector3.Cross(Vector3.up, flat).normalized;
        f.up = Vector3.Cross(tan, f.right).normalized;

        return f;
    }

    // Eine Flaeche aus vier Punkten. "outward" gibt an, wohin sie zeigen soll.
    private static void Quad(List<Vector3> v, List<Vector3> n, List<int> t,
                             Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);

        bool flip = Vector3.Dot(normal, outward) < 0f;

        if (normal.sqrMagnitude < 1e-12f)
            normal = outward;

        normal = (flip ? -normal : normal).normalized;

        int i = v.Count;

        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal);

        if (!flip)
        {
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }
        else
        {
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }
    }

    // Ein Quader (Schwelle).
    private static void Box(List<Vector3> v, List<Vector3> n, List<int> t,
                            Vector3 center, Vector3 right, Vector3 up, Vector3 fwd, Vector3 half)
    {
        Vector3 r = right * half.x, u = up * half.y, f = fwd * half.z;

        Vector3 p000 = center - r - u - f, p100 = center + r - u - f;
        Vector3 p010 = center - r + u - f, p110 = center + r + u - f;
        Vector3 p001 = center - r - u + f, p101 = center + r - u + f;
        Vector3 p011 = center - r + u + f, p111 = center + r + u + f;

        Quad(v, n, t, p010, p110, p111, p011, up);     // oben
        Quad(v, n, t, p000, p100, p101, p001, -up);    // unten
        Quad(v, n, t, p100, p110, p111, p101, right);  // rechts
        Quad(v, n, t, p000, p010, p011, p001, -right); // links
        Quad(v, n, t, p001, p101, p111, p011, fwd);    // vorne
        Quad(v, n, t, p000, p100, p110, p010, -fwd);   // hinten
    }

    // Nur der Schotter (auch als Collider benutzt).
    public static Mesh BallastMesh(TrackEdge e)
    {
        List<Vector3> v = new List<Vector3>();
        List<Vector3> n = new List<Vector3>();
        List<int> t = new List<int>();

        AddBallast(e, v, n, t);

        Mesh mesh = new Mesh();
        mesh.name = "Schotter";
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();

        return mesh;
    }

    // Tiefe der Schachtwaende unter der Gleishoehe und halbe Breite der Schachtoeffnung (Meter).
    public const float ShaftDepth = 1.0f;
    public const float ShaftHalfWidth = 0.45f;

    private static void AddBallast(TrackEdge e, List<Vector3> v, List<Vector3> n, List<int> t)
    {
        // Das Stueck in normale Abschnitte und Schaechte aufteilen.
        float d = 0f;

        for (int i = 0; i < e.shafts.Count; i++)
        {
            Vector2 sh = e.shafts[i];

            if (sh.x > d + 0.01f)
                AddBallastRange(e, d, sh.x, v, n, t, false, d < 0.01f, false);

            AddBallastRange(e, sh.x, sh.y, v, n, t, true, false, false);
            d = sh.y;
        }

        if (d < e.length - 0.01f)
            AddBallastRange(e, d, e.length, v, n, t, false, d < 0.01f, true);
    }

    // Schotterbett zwischen den Metern d0 und d1. Mit "hole" fehlt die Mitte (Schacht): aussen bleiben
    // die Schotterraender stehen, innen gehen Waende in die Tiefe.
    private static void AddBallastRange(TrackEdge e, float d0, float d1, List<Vector3> v, List<Vector3> n, List<int> t,
                                        bool hole, bool capStart, bool capEnd)
    {
        int rings = Mathf.Max(2, Mathf.CeilToInt((d1 - d0) / 0.5f) + 1);

        Vector3[] p0 = new Vector3[rings], p1 = new Vector3[rings];
        Vector3[] p2 = new Vector3[rings], p3 = new Vector3[rings];
        Vector3[] i1 = new Vector3[rings], i2 = new Vector3[rings];
        Vector3[] b1 = new Vector3[rings], b2 = new Vector3[rings];

        for (int i = 0; i < rings; i++)
        {
            Frame f = GetFrame(e, d0 + (d1 - d0) * i / (rings - 1));

            Vector3 low = f.pos - f.up * TrackSpec.BallastSink;
            Vector3 high = f.pos + f.up * TrackSpec.BallastHeight;

            p0[i] = low - f.right * TrackSpec.BallastHalfBottom;
            p1[i] = high - f.right * TrackSpec.BallastHalfTop;
            p2[i] = high + f.right * TrackSpec.BallastHalfTop;
            p3[i] = low + f.right * TrackSpec.BallastHalfBottom;

            // Schacht: Kante der Oeffnung oben und unten.
            Vector3 bottom = f.pos - f.up * ShaftDepth;
            i1[i] = high - f.right * ShaftHalfWidth;
            i2[i] = high + f.right * ShaftHalfWidth;
            b1[i] = bottom - f.right * ShaftHalfWidth;
            b2[i] = bottom + f.right * ShaftHalfWidth;
        }

        for (int i = 0; i < rings - 1; i++)
        {
            Frame f = GetFrame(e, d0 + (d1 - d0) * (i + 0.5f) / (rings - 1));

            if (!hole)
            {
                // linke Boeschung, Oberseite, rechte Boeschung
                Quad(v, n, t, p0[i], p1[i], p1[i + 1], p0[i + 1], -f.right + f.up);
                Quad(v, n, t, p1[i], p2[i], p2[i + 1], p1[i + 1], f.up);
                Quad(v, n, t, p2[i], p3[i], p3[i + 1], p2[i + 1], f.right + f.up);
            }
            else
            {
                // aussen: Boeschung und ein Streifen Oberseite bis zur Oeffnung; innen: Schachtwand
                Quad(v, n, t, p0[i], p1[i], p1[i + 1], p0[i + 1], -f.right + f.up);
                Quad(v, n, t, p1[i], i1[i], i1[i + 1], p1[i + 1], f.up);
                Quad(v, n, t, i1[i], b1[i], b1[i + 1], i1[i + 1], f.right);

                Quad(v, n, t, i2[i], p2[i], p2[i + 1], i2[i + 1], f.up);
                Quad(v, n, t, p2[i], p3[i], p3[i + 1], p2[i + 1], f.right + f.up);
                Quad(v, n, t, i2[i], b2[i], b2[i + 1], i2[i + 1], -f.right);
            }
        }

        int last = rings - 1;

        if (hole)
        {
            // Stirnwaende des Schachts (zeigen in den Schacht hinein).
            Quad(v, n, t, i1[0], i2[0], b2[0], b1[0], GetFrame(e, d0).fwd);
            Quad(v, n, t, i1[last], i2[last], b2[last], b1[last], -GetFrame(e, d1).fwd);
            return;
        }

        // Endkappen: Sonst sieht man das Gleisende von vorne nur als Loch (Rueckseiten werden nicht gezeichnet).
        if (capStart)
            Quad(v, n, t, p0[0], p1[0], p2[0], p3[0], -GetFrame(e, d0).fwd);

        if (capEnd)
            Quad(v, n, t, p0[last], p1[last], p2[last], p3[last], GetFrame(e, d1).fwd);
    }

    // Schotterstreifen zwischen zwei Gleisen, die dicht nebeneinander laufen (Oberkante wie das Schotterbett).
    // Gibt null zurueck, wenn die beiden Stuecke nirgends parallel nebeneinander liegen.
    public static Mesh FillerMesh(TrackEdge e, TrackEdge o, float maxCenterGap)
    {
        int rings = Mathf.Max(2, Mathf.CeilToInt(e.length / 0.5f) + 1);

        Vector3[] a = new Vector3[rings], b = new Vector3[rings];
        bool[] ok = new bool[rings];

        float minCenterGap = 2f * TrackSpec.BallastHalfTop + 0.05f;

        for (int i = 0; i < rings; i++)
        {
            Frame f = GetFrame(e, e.length * i / (rings - 1));

            Vector3 flatFwd = f.fwd;
            flatFwd.y = 0f;
            flatFwd.Normalize();

            // Naechster Punkt des anderen Gleises.
            float best = float.MaxValue;
            int bj = -1;

            for (int j = 0; j < o.pts.Length; j++)
            {
                Vector3 dd = o.pts[j] - f.pos;
                dd.y = 0f;
                float sq = dd.sqrMagnitude;

                if (sq < best)
                {
                    best = sq;
                    bj = j;
                }
            }

            if (bj < 0)
                continue;

            Vector3 q = o.pts[bj];
            Vector3 d3 = q - f.pos;
            d3.y = 0f;

            float gap = d3.magnitude;

            if (gap < minCenterGap || gap > maxCenterGap || Mathf.Abs(q.y - f.pos.y) > 0.4f)
                continue;

            // Es muss wirklich seitlich daneben liegen und parallel verlaufen.
            Vector3 dir = d3 / gap;
            Frame fo = GetFrame(o, o.cum[bj]);

            Vector3 flatFwdO = fo.fwd;
            flatFwdO.y = 0f;
            flatFwdO.Normalize();

            // Auch Gleise, die sich langsam voneinander entfernen (an einer Weiche, bis etwa 37 Grad), und Gleise
            // in Gegenrichtung gehoeren dazu: sonst bleibt an der Weiche ein Dreieck ohne Schotter.
            if (Mathf.Abs(Vector3.Dot(dir, flatFwd)) > 0.65f || Mathf.Abs(Vector3.Dot(flatFwd, flatFwdO)) < 0.8f)
                continue;

            a[i] = f.pos + f.up * TrackSpec.BallastHeight + dir * TrackSpec.BallastHalfTop;
            b[i] = q + fo.up * TrackSpec.BallastHeight - dir * TrackSpec.BallastHalfTop;
            ok[i] = true;
        }

        List<Vector3> v = new List<Vector3>();
        List<Vector3> n = new List<Vector3>();
        List<int> t = new List<int>();

        float wall = TrackSpec.BallastHeight + TrackSpec.BallastSink;

        for (int i = 0; i < rings; i++)
        {
            if (!ok[i])
                continue;

            if (i + 1 < rings && ok[i + 1])
                Quad(v, n, t, a[i], b[i], b[i + 1], a[i + 1], Vector3.up);

            // Stirnwand am Anfang und Ende jedes Streifens (sonst sieht man darunter ins Leere).
            Frame f = GetFrame(e, e.length * i / (rings - 1));

            if (i == 0 || !ok[i - 1])
                Quad(v, n, t, a[i], b[i], b[i] - Vector3.up * wall, a[i] - Vector3.up * wall, -f.fwd);

            if (i == rings - 1 || !ok[i + 1])
                Quad(v, n, t, a[i], b[i], b[i] - Vector3.up * wall, a[i] - Vector3.up * wall, f.fwd);
        }

        if (t.Count == 0)
            return null;

        Mesh mesh = new Mesh();
        mesh.name = "Schotterfuellung";
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();

        return mesh;
    }

    // Schotter + Schwellen + Schienen als ein Mesh mit drei Teil-Meshes (Submeshes).
    public static Mesh FullMesh(TrackEdge e)
    {
        List<Vector3> v = new List<Vector3>();
        List<Vector3> n = new List<Vector3>();
        List<int> tBallast = new List<int>();
        List<int> tSleeper = new List<int>();
        List<int> tRail = new List<int>();

        // 1) Schotter
        AddBallast(e, v, n, tBallast);

        // 2) Schwellen
        int sleepers = Mathf.Max(1, Mathf.FloorToInt(e.length / TrackSpec.SleeperSpacing));
        float sleeperStep = e.length / sleepers;

        for (int i = 0; i < sleepers; i++)
        {
            // Im Schacht liegen keine Schwellen.
            if (e.InShaft((i + 0.5f) * sleeperStep, 0.12f))
                continue;

            Frame f = GetFrame(e, (i + 0.5f) * sleeperStep);

            Vector3 center = f.pos + f.up * (TrackSpec.BallastHeight + TrackSpec.SleeperHeight * 0.5f);

            Box(v, n, tSleeper, center, f.right, f.up, f.fwd,
                new Vector3(TrackSpec.SleeperLength * 0.5f, TrackSpec.SleeperHeight * 0.5f,
                            TrackSpec.SleeperWidth * 0.5f));
        }

        // 3) Schienen
        int rings = Mathf.Max(2, Mathf.CeilToInt(e.length / 0.25f) + 1);
        float railCenter = TrackSpec.Gauge * 0.5f + TrackSpec.RailHeadWidth * 0.5f;
        float yLow = TrackSpec.BallastHeight + TrackSpec.SleeperHeight;
        float yHigh = yLow + TrackSpec.RailHeight;
        float hw = TrackSpec.RailHeadWidth * 0.5f;

        // Im Schacht liegen die Schienen auf je einem Stahltraeger auf den Schotterraendern.
        for (int k = 0; k < e.shafts.Count; k++)
        {
            Vector2 sh = e.shafts[k];
            int segs = Mathf.Max(1, Mathf.CeilToInt((sh.y - sh.x) / 0.5f));
            float st = (sh.y - sh.x) / segs;

            for (int i = 0; i < segs; i++)
            {
                Frame f = GetFrame(e, sh.x + (i + 0.5f) * st);

                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 c = f.pos + f.up * (TrackSpec.BallastHeight + TrackSpec.SleeperHeight * 0.5f) +
                                f.right * (side * railCenter);

                    Box(v, n, tRail, c, f.right, f.up, f.fwd,
                        new Vector3(hw * 1.4f, TrackSpec.SleeperHeight * 0.5f, st * 0.5f + 0.01f));
                }
            }
        }

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3[] lowIn = new Vector3[rings], lowOut = new Vector3[rings];
            Vector3[] highIn = new Vector3[rings], highOut = new Vector3[rings];

            for (int i = 0; i < rings; i++)
            {
                Frame f = GetFrame(e, e.length * i / (rings - 1));

                // "innen" = zur Gleismitte hin
                Vector3 inner = f.right * (side * (railCenter - hw));
                Vector3 outer = f.right * (side * (railCenter + hw));

                lowIn[i] = f.pos + f.up * yLow + inner;
                lowOut[i] = f.pos + f.up * yLow + outer;
                highIn[i] = f.pos + f.up * yHigh + inner;
                highOut[i] = f.pos + f.up * yHigh + outer;
            }

            for (int i = 0; i < rings - 1; i++)
            {
                Frame f = GetFrame(e, e.length * (i + 0.5f) / (rings - 1));

                Quad(v, n, tRail, highIn[i], highOut[i], highOut[i + 1], highIn[i + 1], f.up);
                Quad(v, n, tRail, lowOut[i], highOut[i], highOut[i + 1], lowOut[i + 1], f.right * side);
                Quad(v, n, tRail, lowIn[i], highIn[i], highIn[i + 1], lowIn[i + 1], -f.right * side);
            }

            // Endkappen der Schiene (Gleisende von vorne).
            int endRing = rings - 1;
            Quad(v, n, tRail, lowIn[0], highIn[0], highOut[0], lowOut[0], -GetFrame(e, 0f).fwd);
            Quad(v, n, tRail, lowIn[endRing], highIn[endRing], highOut[endRing], lowOut[endRing],
                 GetFrame(e, e.length).fwd);
        }

        Mesh mesh = new Mesh();
        mesh.name = "Gleis";
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(tBallast, 0);
        mesh.SetTriangles(tSleeper, 1);
        mesh.SetTriangles(tRail, 2);
        mesh.RecalculateBounds();

        return mesh;
    }
}
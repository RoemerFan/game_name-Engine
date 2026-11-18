using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Das glatte Gelaende (Surface Nets) - jetzt als STREAMING-Welt.
//
// So funktioniert es:
//  - Die Welt ist ein 3D-Gitter aus Punkten (Abstand "cellSize", z. B. 0,5 m).
//    Jeder Punkt hat einen DICHTEWERT: positiv = Erde/Stein, negativ = Luft.
//    Die Oberflaeche liegt dort, wo der Wert von positiv auf negativ wechselt.
//  - Aus diesen Werten wird ein glattes Mesh berechnet (Surface Nets).
//  - Jeder Punkt hat ausserdem ein Material (Gras, Erde, Stein, Kohle, Eisen).
//
// NEU (Streaming):
//  - Die Welt wird in SPALTEN (16 x 16 Zellen, volle Hoehe) zerlegt. Nur Spalten in der Naehe
//    des Spielers (und seiner Maschinen) existieren im Speicher. Alles andere wird bei Bedarf
//    aus dem Seed neu berechnet - die Welt kann also (fast) beliebig gross sein.
//  - Jede Spalte entsteht rein aus (Seed, Position): Gleicher Seed = gleiche Welt.
//  - Alles, was der Spieler veraendert (Graben, Einebnen, Boeschung), wird als Liste von
//    OPERATIONEN gemerkt (Op-Log). Wird eine Spalte neu erzeugt, spielt sie die Operationen,
//    die sie betreffen, einfach noch einmal ab. Gespeichert wird nur diese Liste.
//
// WICHTIG: Das Objekt mit diesem Skript NICHT drehen oder skalieren.
// Die Ecke (unten, links, hinten) der Welt liegt an seiner Position.
public class VoxelWorld : MonoBehaviour
{
    // Ein Chunk besteht aus 16 x 16 x 16 Zellen.
    public const int ChunkCells = 16;

    // 16 = 2^4: Damit laesst sich "durch 16 teilen" (auch bei negativen Zahlen) per Bitverschiebung rechnen.
    const int Shift = 4;
    const int Mask = 15;

    // Materialien, die ein eigenes Mesh-Teil (Submesh) bekommen: Gras ... Eisen.
    const int MaterialCount = 5;

    [Header("Groesse")]
    [Tooltip("Abstand der Gitterpunkte in Metern. Kleiner = feiner, aber deutlich teurer.")]
    public float cellSize = 0.5f;
    [Tooltip("Aus: Die Welt ist ein begrenztes Rechteck (Chunks X / Z unten). An: Die Welt ist unendlich gross, die Ecke der Welt ist dann der Startpunkt.")]
    public bool infiniteWorld = false;
    [Tooltip("Groesse der begrenzten Welt in Chunks (ein Chunk ist 16 Zellen gross, bei 0,5 m also 8 m). Mit Streaming sind auch 128 x 128 moeglich.")]
    public int chunksX = 12;
    [Tooltip("Hoehe der Welt in Chunks.")]
    public int chunksY = 4;
    public int chunksZ = 12;

    [Header("Gelaende")]
    public int seed = 12345;
    [Tooltip("Mittlere Hoehe der Oberflaeche ueber dem Weltboden (Meter).")]
    public float baseHeightMeters = 12f;
    [Tooltip("Wie stark die Huegel ausschlagen (Meter).")]
    public float hillHeightMeters = 3f;
    [Tooltip("Groesse der Huegel: kleiner = breitere Huegel.")]
    public float hillScale = 0.03f;
    [Tooltip("Dicke der Grasschicht (Meter).")]
    public float grassDepthMeters = 0.5f;
    [Tooltip("Tiefe, bis zu der Erde liegt, danach Stein (Meter).")]
    public float dirtDepthMeters = 1.5f;

    [Header("Hoehlen")]
    public float caveScale = 0.08f;
    [Tooltip("Hoeher = weniger Hoehlen (ca. 0.55 viele ... 0.7 kaum).")]
    public float caveThreshold = 0.62f;
    [Tooltip("So tief unter der Oberflaeche beginnen Hoehlen (Meter).")]
    public float caveMinDepthMeters = 2.5f;

    [Header("Kohle")]
    public float coalScale = 0.2f;
    [Tooltip("Hoeher = weniger Kohle im Untergrund.")]
    public float coalThreshold = 0.64f;
    [Tooltip("Kohle an der Oberflaeche (ueber Tage).")]
    public float coalOutcropScale = 0.12f;
    public float coalOutcropThreshold = 0.7f;
    public float coalOutcropDepthMeters = 1.5f;

    [Header("Eisen (tief im Untergrund)")]
    public float ironScale = 0.25f;
    public float ironThreshold = 0.66f;
    public float ironMinDepthMeters = 4f;

    [Header("Darstellung")]
    [Tooltip("Optional: eigenes Material mit dem Shader 'Custom/VoxelTerrain'. Leer = wird automatisch erstellt.")]
    public Material worldMaterial;
    [Tooltip("Hier die Datei VoxelTerrain.shader hineinziehen. Nur so ist sicher, dass der Shader auch in einem fertigen Build (nicht nur im Editor) enthalten ist.")]
    public Shader terrainShader;
    [Tooltip("So viele Meter deckt eine Textur-Wiederholung ab.")]
    public float textureMeters = 1.5f;
    [Tooltip("1 = sehr weiche Materialuebergaenge, hoeher = schaerfer.")]
    [Range(1f, 6f)]
    public float blendSharpness = 1.8f;

    [Header("Spieler")]
    [Tooltip("Der Spieler wird erst nach der Erzeugung aktiviert und auf die Oberflaeche gesetzt.")]
    public Transform player;
    public float spawnHeightOffset = 1.5f;

    [Header("Streaming")]
    [Tooltip("So viele Chunks (rund um den Spieler) werden angezeigt. 10 Chunks = 80 m bei 0,5 m Zellen. Groesser = weitere Sicht, aber mehr Speicher/Rechenzeit.")]
    [Range(4, 40)]
    public int viewRadiusChunks = 10;
    [Tooltip("So viele Chunks rund um den Startpunkt muessen beim Laden der Welt fertig sein, bevor es losgeht.")]
    [Range(2, 10)]
    public int initialRadiusChunks = 4;
    [Tooltip("Um jede Maschine (und jeden Gegenstand) bleibt die Welt in diesem Radius (in Chunks) geladen, auch wenn der Spieler weit weg ist. Sonst wuerden sie durch den Boden fallen.")]
    public int anchorRadiusChunks = 2;

    [Header("Erzeugung")]
    [Tooltip("So viele Millisekunden pro Bild darf das Nachladen im Spiel rechnen.")]
    public float maxMillisecondsPerFrame = 8f;
    [Tooltip("Beim Laden der Welt / eines Spielstands darf es mehr sein.")]
    public float loadMillisecondsPerFrame = 30f;
    [Tooltip("So viele geaenderte Chunks werden pro Bild neu berechnet.")]
    public int rebuildChunksPerFrame = 8;

    // ----- Zustand -----
    public bool IsReady { get; private set; }
    public float Progress { get; private set; }
    public event System.Action WorldReady;

    // Anzahl Zellen pro Achse (bei einer unendlichen Welt ohne Bedeutung)
    public int CellsX { get { return chunksX * ChunkCells; } }
    public int CellsY { get { return chunksY * ChunkCells; } }
    public int CellsZ { get { return chunksZ * ChunkCells; } }

    // Anzahl Gitterpunkte in der Hoehe (eins mehr als Zellen)
    int PY { get { return CellsY + 1; } }

    // ---------- Datenstrukturen ----------

    // Ein Chunk: ein 16er-Wuerfel in einer Spalte, mit eigenem Mesh und Collider.
    class Chunk
    {
        public Column column;
        public int cx, cy, cz;
        public GameObject go;
        public Mesh mesh;
        public MeshFilter filter;
        public MeshCollider collider;
    }

    // Eine Spalte: 16 x 16 Punkte in X/Z, ueber die ganze Hoehe.
    // Die Punkte (x, z) = (cx*16 .. cx*16+15, cz*16 .. cz*16+15) gehoeren zu dieser Spalte.
    // Der Punkt am rechten/hinteren Rand gehoert schon zur Nachbarspalte.
    class Column
    {
        public int cx, cz;
        public float[] d;        // Dichte; Index = (lz * PY + y) * 16 + lx
        public byte[] m;         // Material, gleicher Index
        public Chunk[] chunks;   // null = noch kein Mesh
        public bool meshed;
    }

    // Eine Gitterzelle, die die Oberflaeche schneidet.
    struct Cell
    {
        public bool valid;
        public Vector3 pos;       // Eckpunkt in Metern (relativ zum Chunk)
        public Vector3 normal;    // glatte Richtung der Flaeche
        public Vector4 weights;   // Anteil Gras, Erde, Stein, Kohle (Summe mit Eisen = 1)
        public float iron;        // Anteil Eisen
        public int vertex;        // Nummer im Mesh (-1 = noch nicht eingetragen)
    }

    // Eine Veraenderung der Welt (fuer das Op-Log).
    // Alle Koordinaten sind relativ zur Ecke der Welt (Position dieses Objekts), damit sich
    // gespeicherte Welten auch dann laden lassen, wenn das Objekt verschoben wurde.
    const byte OpDig = 1;        // Kugel graben:    c = Mitte, p0 = Radius
    const byte OpFlatten = 2;    // Einebnen:        c, half (schon vergroessert), yaw, p0 = Hoehe (mit Versenkung), p1 = Ueberblendung
    const byte OpBackfill = 3;   // Boeschung:       c, half, yaw, p0 = Niveau, p1 = Reichweite, p2 = Steigung

    struct Op
    {
        public byte type;
        public Vector3 c;
        public Vector2 half;
        public float yaw, p0, p1, p2;
    }

    // Kleinster Quader, der alle geaenderten Punkte enthaelt.
    struct PointBox
    {
        public bool any;
        public int minX, minY, minZ, maxX, maxY, maxZ;

        public void Add(int x, int y, int z)
        {
            if (!any)
            {
                any = true;
                minX = maxX = x;
                minY = maxY = y;
                minZ = maxZ = z;
                return;
            }

            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (z < minZ) minZ = z;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            if (z > maxZ) maxZ = z;
        }
    }

    // Ein Auftrag fuer das Nachladen: Spalte erzeugen oder Mesh bauen.
    struct Work
    {
        public int cx, cz;
        public bool mesh;
        public float prio;
    }

    // Was soll mit einer Spalte passieren? (Ergebnis der Abstandsrechnung)
    struct Wanted
    {
        public int cx, cz;
        public float dist;
        public bool load, mesh, keepData, keepMesh;
    }

    // Ein Punkt, um den die Welt kurzzeitig sicher geladen sein soll (z. B. beim Laden eines Spielstands).
    class Preload
    {
        public Vector3 pos;
        public float radius;
        public float expire;
    }

    // ---------- Speicher ----------

    readonly Dictionary<long, Column> columns = new Dictionary<long, Column>();
    readonly HashSet<Chunk> dirtyChunks = new HashSet<Chunk>();
    readonly List<Chunk> rebuildList = new List<Chunk>();

    // Alle Veraenderungen in der Reihenfolge, in der sie passiert sind.
    List<Op> ops = new List<Op>();

    // Zu jeder Spalte: Nummern der Operationen, die sie betreffen (auch wenn sie gerade nicht geladen ist).
    readonly Dictionary<long, List<int>> columnOps = new Dictionary<long, List<int>>();

    // GENERATOR-Operationen (z. B. eingeebnete Doerfer): Sie gehoeren zur Weltgenerierung, werden
    // NICHT gespeichert (beim Start aus dem Seed neu erzeugt) und vor den Spieler-Aenderungen abgespielt.
    readonly List<Op> genOps = new List<Op>();
    readonly Dictionary<long, List<int>> genColumnOps = new Dictionary<long, List<int>>();

    // Wird kurz VOR dem Erzeugen einer Spalte aufgerufen (Ecken der Spalte in Weltkoordinaten).
    // Hier koennen Generatoren (Doerfer) ihre Gelaende-Operationen anmelden.
    public event System.Action<Vector3, Vector3> ColumnGenerating;

    float[] noiseOffsets = new float[24];

    Material terrainMaterial;
    Texture2D[] textures = new Texture2D[MaterialCount];

    // ---------- Streaming-Zustand ----------

    readonly List<Work> queue = new List<Work>();
    int queueIndex;
    float nextRefresh;

    readonly Dictionary<long, Wanted> wanted = new Dictionary<long, Wanted>();
    readonly List<long> keyScratch = new List<long>();
    readonly List<Preload> preloads = new List<Preload>();
    readonly Dictionary<long, int> anchorSeen = new Dictionary<long, int>();

    // Maschinen und Gegenstaende halten die Welt um sich herum geladen.
    static readonly Dictionary<Transform, int> anchors = new Dictionary<Transform, int>();

    // Spalten mit Spieler-Naehe, die bei PendingChunks als "dringend" zaehlen.
    const float UrgentRadius = 3f;

    // ---------- Zwischenspeicher fuer den Mesh-Bau ----------

    // Die 18 x 18 x 18 Punkte, die ein Chunk zum Meshen braucht (eine Reihe Rand rundherum).
    const int Sc = ChunkCells + 2;
    static readonly float[] scratchDensity = new float[Sc * Sc * Sc];
    static readonly byte[] scratchMaterial = new byte[Sc * Sc * Sc];

    static readonly Cell[] cellCache =
        new Cell[(ChunkCells + 1) * (ChunkCells + 1) * (ChunkCells + 1)];
    static readonly float[] cornerDensity = new float[8];

    // Zwischenspeicher fuer die Hoehen einer Spalte
    static readonly float[] heightScratch = new float[ChunkCells * ChunkCells];
    static readonly float[] outcropScratch = new float[ChunkCells * ChunkCells];

    static readonly List<Vector3> verts = new List<Vector3>();
    static readonly List<Vector3> normals = new List<Vector3>();
    static readonly List<Color> colors = new List<Color>();     // Gewichte Gras/Erde/Stein/Kohle
    static readonly List<Vector2> uvs = new List<Vector2>();    // x = Gewicht Eisen
    static readonly List<int> triangles = new List<int>();

    // Die 12 Kanten einer Zelle als Paare von Eckennummern.
    // Eckennummer: x + 2*y + 4*z (je 0 oder 1).
    static readonly int[,] EdgeCorners =
    {
        {0,1},{2,3},{4,5},{6,7},   // Kanten in X-Richtung
        {0,2},{1,3},{4,6},{5,7},   // Kanten in Y-Richtung
        {0,4},{1,5},{2,6},{3,7}    // Kanten in Z-Richtung
    };

    // Die 4 Zellen, die sich eine Gitterkante teilen (in Umlaufreihenfolge),
    // je Achse als Verschiebung (x, y, z) gegenueber dem Startpunkt der Kante.
    static readonly int[,,] QuadCells =
    {
        { {0,-1,-1}, {0,0,-1}, {0,0,0}, {0,-1,0} },   // Kante in X-Richtung
        { {-1,0,-1}, {0,0,-1}, {0,0,0}, {-1,0,0} },   // Kante in Y-Richtung
        { {-1,-1,0}, {0,-1,0}, {0,0,0}, {-1,0,0} }    // Kante in Z-Richtung
    };

    GUIStyle statusStyle;

    // Waehrend die Welt entsteht, ist der Spieler (und seine Kamera) ausgeschaltet. Ohne Kamera
    // zeigt Unity den Hinweis "No cameras rendering". Diese Kamera ersetzt sie nur zum Laden.
    GameObject loadingCamera;

    // Es gibt nur eine Welt: Andere Skripte (z. B. das Bausystem) finden sie hierueber.
    public static VoxelWorld Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    // =====================================================================
    //  Start
    // =====================================================================

    void Start()
    {
        // Zufalls-Verschiebungen fuer die Rauschfelder (aus dem Seed).
        System.Random rnd = new System.Random(seed);

        for (int i = 0; i < noiseOffsets.Length; i++)
            noiseOffsets[i] = (float)rnd.NextDouble() * 1000f;

        CreateMaterials();

        // Der Spieler wartet, bis der Boden existiert (sonst faellt er durch).
        if (player != null)
            player.gameObject.SetActive(false);

        // Keine Kamera mehr aktiv? Dann eine einfache Ladekamera, damit der Ladebalken
        // statt der Unity-Meldung erscheint.
        if (Camera.allCamerasCount == 0)
        {
            loadingCamera = new GameObject("LoadingCamera");

            Camera cam = loadingCamera.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.07f);
            cam.cullingMask = 0;
            cam.depth = -100f;
        }

        StartCoroutine(InitialLoad());
    }

    // Erstellt die Texturen und das Gelaende-Material.
    void CreateMaterials()
    {
        for (int i = 0; i < MaterialCount; i++)
            textures[i] = BlockInfo.CreateTexture((BlockType)(i + 1), seed);

        // Ein selbst zugewiesenes Material wird nur benutzt, wenn es wirklich
        // den Gelaende-Shader hat (erkennbar an der Eigenschaft _GrassTex).
        // Sonst wuerden alle Texturen fehlen.
        if (worldMaterial != null && worldMaterial.HasProperty("_GrassTex"))
        {
            terrainMaterial = new Material(worldMaterial);
        }
        else
        {
            if (worldMaterial != null)
                Debug.LogWarning("Das 'World Material' hat nicht den Shader 'Custom/VoxelTerrain'. " +
                    "Es wird ignoriert; bitte das Feld leer lassen.");

            // Reihenfolge: 1. im Inspector zugewiesen (im Build sicher enthalten),
            // 2. per Name suchen (geht im Editor), 3. aus einem Resources-Ordner.
            Shader shader = terrainShader;

            if (shader == null)
                shader = Shader.Find("Custom/VoxelTerrain");

            if (shader == null)
                shader = Resources.Load<Shader>("VoxelTerrain");

            if (shader == null)
            {
                Debug.LogError("Shader 'Custom/VoxelTerrain' nicht gefunden! Bitte VoxelTerrain.shader " +
                    "ins Projekt legen und beim VoxelWorld im Feld 'Terrain Shader' eintragen. " +
                    "Ohne ihn gibt es keine Boden-Texturen.");

                shader = Shader.Find("Standard");
            }

            terrainMaterial = new Material(shader);
        }

        terrainMaterial.name = "Terrain";

        // Texturen und Einstellungen an den Shader uebergeben.
        if (terrainMaterial.HasProperty("_GrassTex"))
        {
            terrainMaterial.SetTexture("_GrassTex", textures[0]);
            terrainMaterial.SetTexture("_DirtTex", textures[1]);
            terrainMaterial.SetTexture("_StoneTex", textures[2]);
            terrainMaterial.SetTexture("_CoalTex", textures[3]);
            terrainMaterial.SetTexture("_IronTex", textures[4]);
            terrainMaterial.SetFloat("_TexScale", textureMeters);
            terrainMaterial.SetFloat("_Sharp", blendSharpness);
        }
        else
        {
            // Ersatz ohne eigenen Shader: nur die Stein-Textur.
            terrainMaterial.mainTexture = textures[2];
        }
    }

    // =====================================================================
    //  Hilfsfunktionen: Spalten und Schluessel
    // =====================================================================

    // Eindeutige Nummer einer Spalte (auch fuer negative Koordinaten).
    static long ColumnKey(int cx, int cz)
    {
        return ((long)cx << 32) | (uint)cz;
    }

    // Gehoert diese Spalte zur Welt? (Eine unendliche Welt hat keine Grenzen.)
    bool InWorld(int cx, int cz)
    {
        return infiniteWorld || (cx >= 0 && cz >= 0 && cx < chunksX && cz < chunksZ);
    }

    // Gehoert dieser Gitterpunkt (in X/Z) zur Welt?
    bool PointInWorld(int x, int z)
    {
        return infiniteWorld || (x >= 0 && z >= 0 && x < CellsX && z < CellsZ);
    }

    Column GetColumn(int cx, int cz)
    {
        Column c;
        columns.TryGetValue(ColumnKey(cx, cz), out c);
        return c;
    }

    // =====================================================================
    //  Erzeugung einer Spalte (rein aus Seed + Position)
    // =====================================================================

    // Rauschen in 3D: Unity hat nur 2D-Rauschen, deshalb wird der Mittelwert
    // aus drei Ebenen (XY, YZ, ZX) gebildet. "o" waehlt die Verschiebung.
    float Noise3(float mx, float my, float mz, float scale, int o)
    {
        float a = Mathf.PerlinNoise(mx * scale + noiseOffsets[o],     my * scale + noiseOffsets[o + 1]);
        float b = Mathf.PerlinNoise(my * scale + noiseOffsets[o + 2], mz * scale + noiseOffsets[o + 3]);
        float c = Mathf.PerlinNoise(mz * scale + noiseOffsets[o + 4], mx * scale + noiseOffsets[o + 5]);

        return (a + b + c) / 3f;
    }

    // Oberflaechenhoehe (Meter ueber der Weltecke) und Kohle-Rauschen an einer Stelle.
    void HeightAt(float mx, float mz, out float height, out float outcrop)
    {
        // Zwei Rauschschichten: grosse Huegel + kleine Unebenheiten.
        float n =
            Mathf.PerlinNoise(mx * hillScale + noiseOffsets[0], mz * hillScale + noiseOffsets[1]) * 0.65f +
            Mathf.PerlinNoise(mx * hillScale * 3f + noiseOffsets[2], mz * hillScale * 3f + noiseOffsets[3]) * 0.35f;

        float h = baseHeightMeters + (n - 0.5f) * 2f * hillHeightMeters;

        height = Mathf.Clamp(h, cellSize * 3f, CellsY * cellSize - cellSize * 2f);

        // Kohle-Flecken an der Oberflaeche
        outcrop = Mathf.PerlinNoise(
            mx * coalOutcropScale + noiseOffsets[22],
            mz * coalOutcropScale + noiseOffsets[23]);
    }

    // Die URSPRUENGLICHE Gelaendehoehe an einer Stelle (Weltkoordinate), ohne dass die Gegend
    // geladen sein muss und ohne Veraenderungen durch den Spieler.
    // Gedacht fuer Planung, z. B. wo ein Dorf entstehen soll.
    public float GetNaturalHeight(float worldX, float worldZ)
    {
        Vector3 l = new Vector3(worldX, 0f, worldZ) - transform.position;

        float h, o;
        HeightAt(l.x, l.z, out h, out o);

        return transform.position.y + h;
    }

    // Berechnet alle Punkte einer Spalte.
    void GenerateColumnData(Column col)
    {
        int x0 = col.cx * ChunkCells;
        int z0 = col.cz * ChunkCells;

        // Schritt 1: Hoehenkarte der 16 x 16 Spalten dieser Spalte
        for (int lz = 0; lz < ChunkCells; lz++)
        {
            for (int lx = 0; lx < ChunkCells; lx++)
            {
                float h, o;
                HeightAt((x0 + lx) * cellSize, (z0 + lz) * cellSize, out h, out o);

                heightScratch[lz * ChunkCells + lx] = h;
                outcropScratch[lz * ChunkCells + lx] = o;
            }
        }

        // Schritt 2: Dichte und Material jedes Gitterpunkts
        for (int lz = 0; lz < ChunkCells; lz++)
        {
            for (int y = 0; y < PY; y++)
            {
                for (int lx = 0; lx < ChunkCells; lx++)
                {
                    GeneratePoint(col, lx, y, lz, x0 + lx, z0 + lz,
                        heightScratch[lz * ChunkCells + lx],
                        outcropScratch[lz * ChunkCells + lx]);
                }
            }
        }
    }

    // Berechnet Dichte und Material eines Gitterpunkts.
    void GeneratePoint(Column col, int lx, int y, int lz, int px, int pz,
                       float surface, float outcrop)
    {
        int index = (lz * PY + y) * ChunkCells + lx;

        float mx = px * cellSize;
        float my = y * cellSize;
        float mz = pz * cellSize;

        // Tiefe unter der Oberflaeche (negativ = darueber, also Luft).
        float depth = surface - my;

        // Grundform: positiv unter der Oberflaeche, negativ darueber.
        float d = depth;

        // Hoehlen: nur unter der Oberflaeche pruefen (spart viel Rechenzeit).
        if (depth > caveMinDepthMeters - 2f)
        {
            float n = Noise3(mx, my, mz, caveScale, 4);

            // Wo das Rauschen ueber der Schwelle liegt, ist Luft (negativer Wert).
            float cave = (caveThreshold - n) * 8f;

            // Nahe der Oberflaeche werden Hoehlen weich ausgeblendet,
            // damit sie nicht an der Oberflaeche aufbrechen.
            cave += Mathf.Max(0f, caveMinDepthMeters - depth) * 2f;

            d = Mathf.Min(d, cave);
        }

        // Am Rand einer begrenzten Welt faellt das Gelaende zu Luft ab (sonst gaebe es
        // dort eine offene Schnittflaeche).
        if (!infiniteWorld)
        {
            int edgeCells = Mathf.Min(
                Mathf.Min(px, CellsX - px),
                Mathf.Min(pz, CellsZ - pz));

            d = Mathf.Min(d, edgeCells * cellSize - cellSize * 0.5f);
        }

        // Die beiden untersten Punkt-Ebenen sind immer fest (Weltboden).
        if (y <= 1)
            d = Mathf.Max(d, cellSize * 0.5f);

        col.d[index] = d;

        if (d <= 0f)
        {
            col.m[index] = 0;
            return;
        }

        // ----- Material -----
        BlockType type;

        if (depth < grassDepthMeters) type = BlockType.Grass;
        else if (depth < dirtDepthMeters) type = BlockType.Dirt;
        else type = BlockType.Stone;

        // Kohle an der Oberflaeche (ueber Tage)
        if (depth < coalOutcropDepthMeters && outcrop > coalOutcropThreshold)
        {
            type = BlockType.Coal;
        }
        else if (depth > 0.6f)
        {
            // Erzadern im Untergrund
            if (Noise3(mx, my, mz, coalScale, 10) > coalThreshold)
            {
                type = BlockType.Coal;
            }
            else if (depth > ironMinDepthMeters &&
                     type == BlockType.Stone &&
                     Noise3(mx, my, mz, ironScale, 16) > ironThreshold)
            {
                type = BlockType.Iron;
            }
        }

        col.m[index] = (byte)type;
    }

    // =====================================================================
    //  Start der Welt
    // =====================================================================

    // Der Startpunkt (Weltkoordinate): Mitte einer begrenzten Welt, sonst die Weltecke.
    public Vector3 SpawnPoint()
    {
        Vector3 o = transform.position;

        if (infiniteWorld)
            return o;

        return new Vector3(
            o.x + CellsX * cellSize * 0.5f,
            o.y,
            o.z + CellsZ * cellSize * 0.5f);
    }

    // Laedt die Gegend um den Startpunkt, setzt dann den Spieler.
    IEnumerator InitialLoad()
    {
        Vector3 spawn = SpawnPoint();

        PreloadAround(spawn, initialRadiusChunks);

        // Das Laden uebernimmt Update(); hier wird nur gewartet und der Balken gefuellt.
        yield return null;

        int total = Mathf.Max(1, PendingChunks);

        while (PendingChunks > 0)
        {
            Progress = 0.98f * (1f - (float)PendingChunks / total);
            yield return null;
        }

        ClearPreload();

        IsReady = true;
        Progress = 1f;

        PlacePlayer();

        // Die Ladekamera wird nicht mehr gebraucht (die Kamera des Spielers ist wieder da).
        if (loadingCamera != null)
            Destroy(loadingCamera);

        if (WorldReady != null)
            WorldReady();
    }

    // Setzt den Spieler auf die Oberflaeche am Startpunkt.
    void PlacePlayer()
    {
        if (player == null)
            return;

        Vector3 spawn = SpawnPoint();

        player.position = new Vector3(
            spawn.x,
            GetSurfaceWorldY(spawn.x, spawn.z) + spawnHeightOffset,
            spawn.z
        );

        player.gameObject.SetActive(true);
    }

    // =====================================================================
    //  Streaming: Welche Spalten sollen gerade existieren?
    // =====================================================================

    // Haelt die Welt um einen Punkt herum (Radius in Chunks) fuer einige Zeit sicher geladen.
    // Das Speichersystem nutzt es, damit beim Laden eines Spielstands erst alles steht,
    // bevor Maschinen und Gegenstaende erscheinen.
    public void PreloadAround(Vector3 worldPos, float radiusChunks = UrgentRadius)
    {
        Preload p = new Preload();
        p.pos = worldPos;
        p.radius = radiusChunks;
        p.expire = Time.unscaledTime + 60f;

        preloads.Add(p);

        // Sofort neu planen.
        nextRefresh = 0f;
    }

    public void ClearPreload()
    {
        preloads.Clear();
    }

    // Maschinen und Gegenstaende melden sich hier an: Um sie herum bleibt die Welt geladen,
    // auch wenn der Spieler weit weg ist. radiusChunks < 0 = Standard (anchorRadiusChunks).
    public static void AddAnchor(Transform t, int radiusChunks = -1)
    {
        if (t != null)
            anchors[t] = radiusChunks;
    }

    public static void RemoveAnchor(Transform t)
    {
        if (t != null)
            anchors.Remove(t);
    }

    // Vermerkt, welche Spalten um einen Punkt gebraucht werden.
    void AddWanted(Vector3 worldPos, float radius)
    {
        float colSize = ChunkCells * cellSize;
        Vector3 origin = transform.position;

        float fx = (worldPos.x - origin.x) / colSize;
        float fz = (worldPos.z - origin.z) / colSize;

        int bx = Mathf.FloorToInt(fx);
        int bz = Mathf.FloorToInt(fz);
        int range = Mathf.CeilToInt(radius + 2.5f);

        for (int dz = -range; dz <= range; dz++)
        {
            for (int dx = -range; dx <= range; dx++)
            {
                int cx = bx + dx;
                int cz = bz + dz;

                if (!InWorld(cx, cz))
                    continue;

                float ddx = cx + 0.5f - fx;
                float ddz = cz + 0.5f - fz;
                float dist = Mathf.Sqrt(ddx * ddx + ddz * ddz);

                // Daten bleiben bis radius + 2,5, das Mesh bis radius + 1 (Spielraum gegen Flackern).
                if (dist > radius + 2.5f)
                    continue;

                long key = ColumnKey(cx, cz);
                Wanted w;

                if (!wanted.TryGetValue(key, out w))
                {
                    w = new Wanted();
                    w.cx = cx;
                    w.cz = cz;
                    w.dist = dist;
                }
                else if (dist < w.dist)
                {
                    w.dist = dist;
                }

                w.keepData = true;

                if (dist <= radius + 1.5f) w.load = true;
                if (dist <= radius + 1f) w.keepMesh = true;
                if (dist <= radius) w.mesh = true;

                wanted[key] = w;
            }
        }
    }

    // Plant neu: Was muss geladen, gemesht oder entladen werden?
    void RefreshStreaming()
    {
        wanted.Clear();

        bool playerActive = player != null && player.gameObject.activeInHierarchy;

        if (playerActive)
            AddWanted(player.position, viewRadiusChunks);

        // Vorladepunkte (laufen nach einer Weile von selbst ab).
        for (int i = preloads.Count - 1; i >= 0; i--)
        {
            if (Time.unscaledTime > preloads[i].expire)
                preloads.RemoveAt(i);
            else
                AddWanted(preloads[i].pos, preloads[i].radius);
        }

        // Maschinen und Gegenstaende: gleiche Spalten nur einmal bearbeiten.
        anchorSeen.Clear();
        List<Transform> dead = null;
        float colSize = ChunkCells * cellSize;

        foreach (KeyValuePair<Transform, int> pair in anchors)
        {
            if (pair.Key == null)
            {
                if (dead == null) dead = new List<Transform>();
                dead.Add(pair.Key);
                continue;
            }

            int r = pair.Value >= 0 ? pair.Value : anchorRadiusChunks;
            Vector3 pos = pair.Key.position;

            int ax = Mathf.FloorToInt((pos.x - transform.position.x) / colSize);
            int az = Mathf.FloorToInt((pos.z - transform.position.z) / colSize);
            long ak = ColumnKey(ax, az);

            int seenRadius;

            if (anchorSeen.TryGetValue(ak, out seenRadius) && seenRadius >= r)
                continue;

            anchorSeen[ak] = r;
            AddWanted(pos, r);
        }

        if (dead != null)
        {
            foreach (Transform t in dead)
                anchors.Remove(t);
        }

        // Ohne aktiven Spieler (und ohne Vorladepunkt) wenigstens den Startpunkt halten.
        if (IsReady && !playerActive && preloads.Count == 0 && anchors.Count == 0)
            AddWanted(SpawnPoint(), viewRadiusChunks);

        // ----- Entladen -----
        keyScratch.Clear();
        keyScratch.AddRange(columns.Keys);

        for (int i = 0; i < keyScratch.Count; i++)
        {
            Column col = columns[keyScratch[i]];
            Wanted w;

            if (!wanted.TryGetValue(keyScratch[i], out w) || !w.keepData)
                DestroyColumn(col);
            else if (col.meshed && !w.keepMesh)
                UnmeshColumn(col);
        }

        // ----- Auftraege: nahe zuerst -----
        queue.Clear();
        queueIndex = 0;

        foreach (KeyValuePair<long, Wanted> pair in wanted)
        {
            Wanted w = pair.Value;
            Column col = GetColumn(w.cx, w.cz);

            if (col == null)
            {
                if (w.load)
                    queue.Add(new Work { cx = w.cx, cz = w.cz, mesh = false, prio = w.dist });
            }
            else if (!col.meshed && w.mesh)
            {
                // Meshen erst nach dem Laden der Nachbarn (daher etwas spaeter einsortiert).
                queue.Add(new Work { cx = w.cx, cz = w.cz, mesh = true, prio = w.dist + 1.5f });
            }
        }

        queue.Sort((a, b) => a.prio.CompareTo(b.prio));
    }

    // Arbeitet die Auftraege ab, solange die Zeit reicht.
    void StreamStep(float budgetMs)
    {
        bool fast = !IsReady || preloads.Count > 0;

        if (Time.unscaledTime >= nextRefresh)
        {
            RefreshStreaming();
            nextRefresh = Time.unscaledTime + (fast ? 0f : 0.2f);
        }

        if (queueIndex >= queue.Count)
            return;

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        while (queueIndex < queue.Count)
        {
            Work w = queue[queueIndex++];
            Column col = GetColumn(w.cx, w.cz);

            if (!w.mesh)
            {
                if (col == null)
                    LoadColumn(w.cx, w.cz);
            }
            else if (col != null && !col.meshed && NeighborsLoaded(col))
            {
                MeshColumn(col);
            }

            if (sw.ElapsedMilliseconds > budgetMs)
                break;
        }
    }

    // Sind alle 8 Nachbarn geladen? (Das Mesh braucht ihre Randpunkte.)
    bool NeighborsLoaded(Column col)
    {
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!InWorld(col.cx + dx, col.cz + dz))
                    continue;

                if (GetColumn(col.cx + dx, col.cz + dz) == null)
                    return false;
            }
        }

        return true;
    }

    // Erzeugt eine Spalte und spielt die Veraenderungen ab, die sie betreffen.
    void LoadColumn(int cx, int cz)
    {
        Column col = new Column();
        col.cx = cx;
        col.cz = cz;

        int n = ChunkCells * PY * ChunkCells;
        col.d = new float[n];
        col.m = new byte[n];

        // Generatoren (Doerfer) duerfen sich anmelden, bevor die Spalte entsteht.
        if (ColumnGenerating != null)
        {
            Vector3 o = transform.position;
            float size = ChunkCells * cellSize;

            Vector3 min = new Vector3(o.x + cx * size, o.y, o.z + cz * size);
            ColumnGenerating(min, min + new Vector3(size, 0f, size));
        }

        GenerateColumnData(col);

        long key = ColumnKey(cx, cz);
        columns[key] = col;

        PointBox ignored = new PointBox();
        List<int> list;

        // Erst die Weltgenerierung (Doerfer) ...
        if (genColumnOps.TryGetValue(key, out list))
        {
            for (int i = 0; i < list.Count; i++)
                ApplyOp(genOps[list[i]], col, null, ref ignored);
        }

        // ... dann die Veraenderungen des Spielers.
        if (columnOps.TryGetValue(key, out list))
        {
            for (int i = 0; i < list.Count; i++)
                ApplyOp(ops[list[i]], col, null, ref ignored);
        }
    }

    void MeshColumn(Column col)
    {
        col.chunks = new Chunk[chunksY];

        for (int cy = 0; cy < chunksY; cy++)
        {
            Chunk c = new Chunk();
            c.column = col;
            c.cx = col.cx;
            c.cy = cy;
            c.cz = col.cz;

            col.chunks[cy] = c;
            RebuildChunk(c);
        }

        col.meshed = true;
    }

    // Entfernt Meshes und Collider einer Spalte (die Daten bleiben).
    void UnmeshColumn(Column col)
    {
        if (col.chunks != null)
        {
            foreach (Chunk c in col.chunks)
            {
                if (c == null)
                    continue;

                dirtyChunks.Remove(c);

                if (c.go != null)
                    Destroy(c.go);

                if (c.mesh != null)
                    Destroy(c.mesh);
            }
        }

        col.chunks = null;
        col.meshed = false;
    }

    // Wirft eine Spalte komplett aus dem Speicher. Spaeter wird sie neu erzeugt.
    void DestroyColumn(Column col)
    {
        UnmeshColumn(col);
        columns.Remove(ColumnKey(col.cx, col.cz));
    }

    // Zaehlt die Spalten rund um einen Punkt, die noch kein Mesh haben.
    int CountUnmeshedAround(Vector3 worldPos, float radius)
    {
        float colSize = ChunkCells * cellSize;
        Vector3 origin = transform.position;

        float fx = (worldPos.x - origin.x) / colSize;
        float fz = (worldPos.z - origin.z) / colSize;

        int bx = Mathf.FloorToInt(fx);
        int bz = Mathf.FloorToInt(fz);
        int range = Mathf.CeilToInt(radius);

        int count = 0;

        for (int dz = -range; dz <= range; dz++)
        {
            for (int dx = -range; dx <= range; dx++)
            {
                int cx = bx + dx;
                int cz = bz + dz;

                if (!InWorld(cx, cz))
                    continue;

                float ddx = cx + 0.5f - fx;
                float ddz = cz + 0.5f - fz;

                if (ddx * ddx + ddz * ddz > radius * radius)
                    continue;

                Column col = GetColumn(cx, cz);

                if (col == null || !col.meshed)
                    count++;
            }
        }

        return count;
    }

    // =====================================================================
    //  Zugriff (auch fuer Maschinen spaeter nutzbar)
    // =====================================================================

    // Hoehe der obersten festen Stelle einer Spalte (lokal, in Metern ueber der Weltecke).
    float SurfaceLocalY(Column col, int lx, int lz)
    {
        int b = (lz * PY) * ChunkCells + lx;

        for (int y = PY - 1; y >= 0; y--)
        {
            float d = col.d[b + y * ChunkCells];

            if (d <= 0f)
                continue;

            // Genaue Hoehe zwischen diesem festen und dem Luft-Punkt darueber.
            if (y == PY - 1)
                return y * cellSize;

            float above = col.d[b + (y + 1) * ChunkCells];
            float t = d / (d - above);

            return (y + t) * cellSize;
        }

        return 0f;
    }

    // Hoehe der Oberflaeche (Weltkoordinate) an einer Stelle.
    // Ist die Gegend nicht geladen, kommt die urspruengliche Gelaendehoehe zurueck.
    public float GetSurfaceWorldY(float worldX, float worldZ)
    {
        Vector3 local = new Vector3(worldX, 0f, worldZ) - transform.position;

        int px = Mathf.RoundToInt(local.x / cellSize);
        int pz = Mathf.RoundToInt(local.z / cellSize);

        if (!infiniteWorld)
        {
            px = Mathf.Clamp(px, 0, CellsX - 1);
            pz = Mathf.Clamp(pz, 0, CellsZ - 1);
        }

        Column col = GetColumn(px >> Shift, pz >> Shift);

        if (col == null)
            return GetNaturalHeight(worldX, worldZ);

        return transform.position.y + SurfaceLocalY(col, px & Mask, pz & Mask);
    }

    // Liest Dichte und Material eines Gitterpunkts (false = nicht geladen / ausserhalb).
    bool TryGetPoint(int x, int y, int z, out float density, out byte mat)
    {
        density = 0f;
        mat = 0;

        if (y < 0 || y >= PY || !PointInWorld(x, z))
            return false;

        Column col = GetColumn(x >> Shift, z >> Shift);

        if (col == null)
            return false;

        int index = ((z & Mask) * PY + y) * ChunkCells + (x & Mask);

        density = col.d[index];
        mat = col.m[index];

        return true;
    }

    // Welches Material liegt nahe dieser Stelle? (fuer die Abbau-Dauer)
    public BlockType GetMaterialNear(Vector3 worldPoint)
    {
        Vector3 l = (worldPoint - transform.position) / cellSize;

        int cx = Mathf.FloorToInt(l.x);
        int cy = Mathf.Clamp(Mathf.FloorToInt(l.y), 0, CellsY - 1);
        int cz = Mathf.FloorToInt(l.z);

        float best = float.MaxValue;
        byte bestMaterial = 0;
        bool found = false;

        // Naechster fester Eckpunkt der Zelle
        for (int i = 0; i < 8; i++)
        {
            int px = cx + (i & 1);
            int py = cy + ((i >> 1) & 1);
            int pz = cz + ((i >> 2) & 1);

            float d;
            byte m;

            if (!TryGetPoint(px, py, pz, out d, out m) || d <= 0f)
                continue;

            float dist = (new Vector3(px, py, pz) - l).sqrMagnitude;

            if (dist < best)
            {
                best = dist;
                bestMaterial = m;
                found = true;
            }
        }

        if (!found || bestMaterial == 0)
            return BlockType.Stone;

        return (BlockType)bestMaterial;
    }

    // Wie weit weicht das Gelaende unter einer Grundflaeche von der Hoehe
    // "baseWorldY" ab? (groesster Unterschied nach oben oder unten, in Metern)
    // Das Bausystem lehnt Bauplaetze ab, an denen zu viel eingeebnet werden muesste.
    public float MaxDeviation(Vector3 centerWorld, Vector2 halfExtents, float yaw, float baseWorldY)
    {
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        float worst = 0f;

        // 5 x 5 Messpunkte ueber die Grundflaeche
        for (int i = 0; i < 5; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                float fx = (i / 4f * 2f - 1f) * halfExtents.x;
                float fz = (j / 4f * 2f - 1f) * halfExtents.y;

                Vector3 p = centerWorld + rot * new Vector3(fx, 0f, fz);
                float h = GetSurfaceWorldY(p.x, p.z);

                worst = Mathf.Max(worst, Mathf.Abs(h - baseWorldY));
            }
        }

        return worst;
    }

    // Gehoert dieser Collider zur Voxel-Welt?
    public bool OwnsCollider(Collider col)
    {
        return col != null && col.transform.parent == transform;
    }

    // =====================================================================
    //  Veraenderungen (Op-Log)
    // =====================================================================

    // So tief versinkt die Unterkante einer Maschine im Boden (Meter).
    public const float FlattenSink = 0.06f;

    // Ebnet das Gelaende unter einer Grundflaeche ein (wie ein Fundament):
    //  - Innerhalb der Flaeche wird die Oberflaeche exakt auf "baseWorldY" gesetzt:
    //    Huegel werden abgetragen, Mulden aufgefuellt.
    //  - Ausserhalb geht es ueber "blend" Meter sanft in das alte Gelaende zurueck.
    //  - Oberhalb/unterhalb der Flaeche wirkt es nur wenige Meter weit, tiefer
    //    liegende Hoehlen bleiben unberuehrt.
    // "sink" laesst die Unterkante ein kleines Stueck im Boden versinken, damit
    // zwischen Maschine und Boden kein Spalt bleibt.
    public void FlattenArea(Vector3 centerWorld, Vector2 halfExtents, float yaw,
                            float baseWorldY, float blend = 1f, float sink = FlattenSink)
    {
        if (!IsReady)
            return;

        // Die Flaeche ein wenig vergroessern: Das Gitter ist grob und der Mesher glaettet
        // die Kante, sonst entsteht am Maschinenrand (v. a. bei runden Formen) ein Spalt
        // zwischen Boden und Maschine.
        float margin = cellSize * 0.75f;

        Op op = new Op();
        op.type = OpFlatten;
        op.c = centerWorld - transform.position;
        op.half = halfExtents + new Vector2(margin, margin);
        op.yaw = yaw;
        op.p0 = baseWorldY - transform.position.y - sink;
        op.p1 = blend;

        Commit(op, null);
        RebuildDirtyNow();
    }

    // Passt das Gelaende rund um eine Grundflaeche an die Umgebung an (Boeschung):
    // Am Rand der Flaeche liegt das Gelaende auf "levelWorldY" (der eingeebneten Hoehe).
    // Nach aussen folgt es der urspruenglichen Gelaendehoehe, darf sich aber pro Meter
    // Abstand nur um "slope" Meter von diesem Niveau entfernen (1 = 45 Grad).
    //  - Huegel neben der Flaeche: Die Erde steigt als Rampe bis zur Hoehe des Huegels an.
    //  - Senken neben der Flaeche: Das Gelaende faellt als Rampe zur Senke hin ab.
    //  - Ist die Umgebung ohnehin auf gleicher Hoehe, aendert sich nichts.
    // Nach "reach" Metern endet die Anpassung. Die Erde orientiert sich also immer an der
    // Hoehe des Gelaendes dort, nie an der Oberkante der Platte oder Wand.
    public void BackfillSlope(Vector3 centerWorld, Vector2 halfExtents, float yaw,
                              float levelWorldY, float reach, float slope)
    {
        if (!IsReady || reach <= 0f)
            return;

        // Gleiche Vergroesserung wie beim Einebnen, damit beide nahtlos aneinander anschliessen.
        float margin = cellSize * 0.75f;

        Op op = new Op();
        op.type = OpBackfill;
        op.c = centerWorld - transform.position;
        op.half = halfExtents + new Vector2(margin, margin);
        op.yaw = yaw;
        op.p0 = levelWorldY - transform.position.y - FlattenSink;
        op.p1 = reach;
        op.p2 = Mathf.Max(slope, 0.1f);

        Commit(op, null);
        RebuildDirtyNow();
    }

    // ---------- Schnittstelle fuer Generatoren (Doerfer) ----------

    // Liegt ein Punkt (mit Abstand "margin" zum Rand) innerhalb der Welt?
    public bool IsInsideWorld(Vector3 worldPos, float margin)
    {
        if (infiniteWorld)
            return true;

        Vector3 l = worldPos - transform.position;

        return l.x >= margin && l.z >= margin &&
               l.x <= CellsX * cellSize - margin &&
               l.z <= CellsZ * cellSize - margin;
    }

    // Ist das Gelaende rund um einen Punkt (Radius in Chunks) fertig mit Mesh und Collider?
    public bool IsMeshedAround(Vector3 worldPos, float radiusChunks)
    {
        return CountUnmeshedAround(worldPos, radiusChunks) == 0;
    }

    // Wie FlattenArea, gehoert aber zur Weltgenerierung: wird nicht gespeichert und bei jedem
    // Laden einer betroffenen Spalte automatisch wieder angewendet (vor den Spieler-Aenderungen).
    public void AddGeneratorFlatten(Vector3 centerWorld, Vector2 halfExtents, float yaw,
                                    float baseWorldY, float blend)
    {
        float margin = cellSize * 0.75f;

        Op op = new Op();
        op.type = OpFlatten;
        op.c = centerWorld - transform.position;
        op.half = halfExtents + new Vector2(margin, margin);
        op.yaw = yaw;
        op.p0 = baseWorldY - transform.position.y - FlattenSink;
        op.p1 = blend;

        AddGeneratorOp(op);
    }

    // Wie BackfillSlope, aber als Generator-Operation (siehe oben).
    public void AddGeneratorBackfill(Vector3 centerWorld, Vector2 halfExtents, float yaw,
                                     float levelWorldY, float reach, float slope)
    {
        float margin = cellSize * 0.75f;

        Op op = new Op();
        op.type = OpBackfill;
        op.c = centerWorld - transform.position;
        op.half = halfExtents + new Vector2(margin, margin);
        op.yaw = yaw;
        op.p0 = levelWorldY - transform.position.y - FlattenSink;
        op.p1 = reach;
        op.p2 = Mathf.Max(slope, 0.1f);

        AddGeneratorOp(op);
    }

    void AddGeneratorOp(Op op)
    {
        genOps.Add(op);
        int index = genOps.Count - 1;

        RegisterOpIn(genColumnOps, op, index);

        // Sicherheitshalber: schon geladene Spalten sofort anpassen (normalerweise meldet sich
        // ein Generator vor dem Laden an, dann passiert hier nichts).
        int x0, x1, z0, z1;
        OpPointRect(op, out x0, out x1, out z0, out z1);

        PointBox box = new PointBox();

        for (int cz = z0 >> Shift; cz <= (z1 >> Shift); cz++)
        {
            for (int cx = x0 >> Shift; cx <= (x1 >> Shift); cx++)
            {
                Column col = GetColumn(cx, cz);

                if (col != null)
                    ApplyOp(op, col, null, ref box);
            }
        }

        if (box.any)
            MarkDirtyRange(box);
    }

    // Verteilt das Gewicht eines Punktes: 0 = ganz Luft, 1 = ganz fest.
    // Wird fuer die Volumen-Schaetzung beim Graben benutzt.
    float Occupancy(float d)
    {
        return Mathf.Clamp01(d / cellSize + 0.5f);
    }

    // Graebt eine Kugel aus dem Gelaende.
    // Gibt das entfernte Volumen (in Kubikmetern) pro Material zurueck;
    // der Index ist die Zahl der BlockType-Sorte (z. B. (int)BlockType.Coal).
    public float[] DigSphere(Vector3 worldCenter, float radius)
    {
        float[] volumes = new float[7];

        if (!IsReady)
            return volumes;

        Op op = new Op();
        op.type = OpDig;
        op.c = worldCenter - transform.position;
        op.p0 = radius;

        Commit(op, volumes);

        return volumes;
    }

    // Fuehrt eine Operation jetzt aus (auf allen geladenen Spalten) und merkt sie im Log,
    // damit nicht geladene Spalten sie spaeter nachholen koennen.
    void Commit(Op op, float[] volumes)
    {
        PointBox box = new PointBox();
        bool complete = ApplyOp(op, null, volumes, ref box);

        // Hat die Operation nirgends etwas geaendert und waren alle Spalten geladen,
        // braucht sie auch nicht gespeichert zu werden.
        if (!box.any && complete)
            return;

        ops.Add(op);
        RegisterOp(ops.Count - 1);

        if (box.any)
            MarkDirtyRange(box);
    }

    // Traegt eine Operation bei allen Spalten ein, die sie beruehren kann.
    void RegisterOp(int index)
    {
        RegisterOpIn(columnOps, ops[index], index);
    }

    void RegisterOpIn(Dictionary<long, List<int>> table, Op op, int index)
    {
        int x0, x1, z0, z1;
        OpPointRect(op, out x0, out x1, out z0, out z1);

        for (int cz = z0 >> Shift; cz <= (z1 >> Shift); cz++)
        {
            for (int cx = x0 >> Shift; cx <= (x1 >> Shift); cx++)
            {
                long key = ColumnKey(cx, cz);
                List<int> list;

                if (!table.TryGetValue(key, out list))
                {
                    list = new List<int>();
                    table[key] = list;
                }

                list.Add(index);
            }
        }
    }

    // Der Bereich in X/Z (Gitterpunkte), den eine Operation beruehren kann.
    void OpPointRect(Op op, out int x0, out int x1, out int z0, out int z1)
    {
        float r;
        int pad;

        if (op.type == OpDig)
        {
            r = op.p0;
            pad = 1;
        }
        else
        {
            r = Mathf.Sqrt(op.half.x * op.half.x + op.half.y * op.half.y) + op.p1;
            pad = 0;
        }

        x0 = Mathf.FloorToInt((op.c.x - r) / cellSize) - pad;
        x1 = Mathf.CeilToInt((op.c.x + r) / cellSize) + pad;
        z0 = Mathf.FloorToInt((op.c.z - r) / cellSize) - pad;
        z1 = Mathf.CeilToInt((op.c.z + r) / cellSize) + pad;

        if (!infiniteWorld)
        {
            x0 = Mathf.Max(0, x0);
            z0 = Mathf.Max(0, z0);
            x1 = Mathf.Min(CellsX - 1, x1);
            z1 = Mathf.Min(CellsZ - 1, z1);
        }
    }

    // Findet die Spalte eines Punktes fuer eine Operation. null = ueberspringen.
    // "complete" wird false, wenn die Spalte zur Welt gehoert, aber gerade nicht geladen ist.
    // "only" gesetzt: Die Operation wird nur fuer diese eine Spalte ausgefuehrt (Nachholen beim Laden).
    Column OpColumn(int x, int z, Column only, ref bool complete)
    {
        if (!PointInWorld(x, z))
            return null;

        Column col = GetColumn(x >> Shift, z >> Shift);

        if (only != null)
            return col == only ? col : null;

        if (col == null)
        {
            complete = false;
            return null;
        }

        return col;
    }

    // Fuehrt eine Operation aus. Liefert true, wenn alle betroffenen Spalten geladen waren.
    bool ApplyOp(Op op, Column only, float[] volumes, ref PointBox box)
    {
        int x0, x1, z0, z1;
        OpPointRect(op, out x0, out x1, out z0, out z1);

        if (only != null)
        {
            x0 = Mathf.Max(x0, only.cx << Shift);
            x1 = Mathf.Min(x1, (only.cx << Shift) + Mask);
            z0 = Mathf.Max(z0, only.cz << Shift);
            z1 = Mathf.Min(z1, (only.cz << Shift) + Mask);
        }

        bool complete = true;

        switch (op.type)
        {
            case OpDig:
                ApplyDig(op, only, volumes, ref box, x0, x1, z0, z1, ref complete);
                break;

            case OpFlatten:
                ApplyFlatten(op, only, ref box, x0, x1, z0, z1, ref complete);
                break;

            case OpBackfill:
                ApplyBackfill(op, only, ref box, x0, x1, z0, z1, ref complete);
                break;
        }

        return complete;
    }

    void ApplyDig(Op op, Column only, float[] volumes, ref PointBox box,
                  int x0, int x1, int z0, int z1, ref bool complete)
    {
        Vector3 c = op.c;
        float radius = op.p0;

        // Die beiden untersten Ebenen (Weltboden) bleiben unberuehrt.
        int y0 = Mathf.Max(2, Mathf.FloorToInt((c.y - radius) / cellSize) - 1);
        int y1 = Mathf.Min(PY - 1, Mathf.CeilToInt((c.y + radius) / cellSize) + 1);

        float cellVolume = cellSize * cellSize * cellSize;

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                Column col = OpColumn(x, z, only, ref complete);

                if (col == null)
                    continue;

                int b = ((z & Mask) * PY) * ChunkCells + (x & Mask);

                for (int y = y0; y <= y1; y++)
                {
                    Vector3 p = new Vector3(x, y, z) * cellSize;

                    // Abstand zur Kugeloberflaeche: negativ = innerhalb der Kugel.
                    float cut = (p - c).magnitude - radius;

                    int index = b + y * ChunkCells;
                    float old = col.d[index];

                    // Neuer Wert = der kleinere von beiden (Gelaende MINUS Kugel).
                    if (cut >= old)
                        continue;

                    // Entfernte Menge dem Material dieses Punktes zuordnen.
                    if (volumes != null)
                    {
                        float removed = (Occupancy(old) - Occupancy(cut)) * cellVolume;

                        if (removed > 0f && old > 0f)
                            volumes[col.m[index]] += removed;
                    }

                    col.d[index] = cut;
                    box.Add(x, y, z);
                }
            }
        }
    }

    void ApplyFlatten(Op op, Column only, ref PointBox box,
                      int x0, int x1, int z0, int z1, ref bool complete)
    {
        Quaternion inverse = Quaternion.Euler(0f, -op.yaw, 0f);

        Vector3 c = op.c;
        Vector2 half = op.half;
        float baseY = op.p0;
        float blend = op.p1;

        // Hoehenbereich: 2 m darunter bis 3 m darueber (die Weltboden-Ebenen bleiben).
        int y0 = Mathf.Max(2, Mathf.FloorToInt((baseY - 2f) / cellSize));
        int y1 = Mathf.Min(PY - 1, Mathf.CeilToInt((baseY + 3f) / cellSize));

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                Column col = OpColumn(x, z, only, ref complete);

                if (col == null)
                    continue;

                // Abstand dieser Spalte zur (gedrehten) Rechteck-Grundflaeche.
                Vector3 off = inverse * new Vector3(x * cellSize - c.x, 0f, z * cellSize - c.z);

                float ex = Mathf.Max(Mathf.Abs(off.x) - half.x, 0f);
                float ez = Mathf.Max(Mathf.Abs(off.z) - half.y, 0f);
                float dist = Mathf.Sqrt(ex * ex + ez * ez);

                // 1 innerhalb der Flaeche, am Rand weich auf 0 abfallend.
                float t = 1f - Mathf.SmoothStep(0f, 1f, dist / Mathf.Max(blend, 0.01f));

                if (t <= 0.001f)
                    continue;

                int b = ((z & Mask) * PY) * ChunkCells + (x & Mask);

                for (int y = y0; y <= y1; y++)
                {
                    int index = b + y * ChunkCells;

                    float plane = baseY - y * cellSize;   // + unter der Ebene, - darueber
                    float old = col.d[index];
                    float now = Mathf.Lerp(old, plane, t);

                    if (Mathf.Approximately(now, old))
                        continue;

                    col.d[index] = now;

                    // Neu aufgefuellte oder oberste Punkte: Gras bzw. Erde.
                    // Aufgefuellte Punkte bekommen im Randbereich bewusst grosszuegig Gras
                    // (eine Zelle Toleranz), damit am Rand keine braunen Erdstreifen
                    // an der Oberflaeche sichtbar werden.
                    float grassLimit = grassDepthMeters + cellSize;

                    if (now > 0f &&
                        (old <= 0f || (t > 0.5f && plane < grassDepthMeters)))
                    {
                        col.m[index] = (byte)(plane < grassLimit
                            ? BlockType.Grass
                            : BlockType.Dirt);
                    }
                    else if (now <= 0f)
                    {
                        col.m[index] = 0;
                    }

                    box.Add(x, y, z);
                }
            }
        }
    }

    void ApplyBackfill(Op op, Column only, ref PointBox box,
                       int x0, int x1, int z0, int z1, ref bool complete)
    {
        Quaternion inverse = Quaternion.Euler(0f, -op.yaw, 0f);

        Vector3 c = op.c;
        Vector2 half = op.half;
        float level = op.p0;
        float reach = op.p1;
        float slope = op.p2;

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                Column col = OpColumn(x, z, only, ref complete);

                if (col == null)
                    continue;

                Vector3 off = inverse * new Vector3(x * cellSize - c.x, 0f, z * cellSize - c.z);

                float ex = Mathf.Max(Mathf.Abs(off.x) - half.x, 0f);
                float ez = Mathf.Max(Mathf.Abs(off.z) - half.y, 0f);
                float dist = Mathf.Sqrt(ex * ex + ez * ez);

                // Unter der Flaeche selbst hat das Einebnen schon gearbeitet.
                if (dist <= 0f || dist >= reach)
                    continue;

                int lx = x & Mask;
                int lz = z & Mask;

                // Urspruengliche Gelaendehoehe dieser Spalte (lokal).
                float natural = SurfaceLocalY(col, lx, lz);

                // Zielhoehe: dem Gelaende folgen, aber hoechstens "slope" pro Meter Abstand
                // vom Niveau der Flaeche entfernt.
                float allowed = slope * dist;
                float target = level + Mathf.Clamp(natural - level, -allowed, allowed);

                if (Mathf.Abs(target - natural) < 0.005f)
                    continue;

                bool fill = target > natural;

                int y0 = Mathf.Max(2, Mathf.FloorToInt((Mathf.Min(target, natural) - 1f) / cellSize));
                int y1 = Mathf.Min(PY - 1, Mathf.CeilToInt((Mathf.Max(target, natural) + 1f) / cellSize));

                int b = (lz * PY) * ChunkCells + lx;

                for (int y = y0; y <= y1; y++)
                {
                    int index = b + y * ChunkCells;

                    float plane = target - y * cellSize;   // + unter der Zielhoehe, - darueber
                    float old = col.d[index];

                    if (fill)
                    {
                        // Nur auffuellen.
                        if (plane <= old)
                            continue;
                    }
                    else
                    {
                        // Abtragen: nur ueber der Zielhoehe, darunter bleibt alles.
                        if (plane >= 0f || plane >= old)
                            continue;
                    }

                    col.d[index] = plane;

                    if (plane > 0f && old <= 0f)
                    {
                        col.m[index] = (byte)(plane < grassDepthMeters + cellSize
                            ? BlockType.Grass
                            : BlockType.Dirt);
                    }
                    else if (plane <= 0f)
                    {
                        col.m[index] = 0;
                    }

                    box.Add(x, y, z);
                }
            }
        }
    }

    // Merkt alle Chunks vor, die von geaenderten Punkten beeinflusst werden.
    // (Ein Punkt beeinflusst Zellen und Kanten bis einen Schritt ausserhalb.)
    void MarkDirtyRange(PointBox box)
    {
        int cx0 = (box.minX - 1) >> Shift;
        int cx1 = (box.maxX + 1) >> Shift;
        int cz0 = (box.minZ - 1) >> Shift;
        int cz1 = (box.maxZ + 1) >> Shift;

        int cy0 = Mathf.Max(0, (box.minY - 1) >> Shift);
        int cy1 = Mathf.Min(chunksY - 1, (box.maxY + 1) >> Shift);

        for (int cz = cz0; cz <= cz1; cz++)
        {
            for (int cx = cx0; cx <= cx1; cx++)
            {
                Column col = GetColumn(cx, cz);

                // Spalten ohne Mesh bekommen es spaeter ohnehin mit den neuen Werten.
                if (col == null || !col.meshed)
                    continue;

                for (int cy = cy0; cy <= cy1; cy++)
                    dirtyChunks.Add(col.chunks[cy]);
            }
        }
    }

    // ---------- Speichern und Laden ----------

    // Anzahl Chunks, die noch (neu) berechnet werden muessen: geaenderte Chunks plus die
    // noch fehlenden Spalten rund um den Spieler bzw. die Vorladepunkte.
    public int PendingChunks
    {
        get
        {
            int n = dirtyChunks.Count;

            if (player != null && player.gameObject.activeInHierarchy)
                n += CountUnmeshedAround(player.position, UrgentRadius);

            for (int i = 0; i < preloads.Count; i++)
                n += CountUnmeshedAround(preloads[i].pos, preloads[i].radius);

            return n;
        }
    }

    // Kennung der Welt: Ein Spielstand passt nur zur gleichen Welt.
    // ("s1" = Streaming-Format, die Veraenderungen werden als Op-Log gespeichert.)
    public string WorldSignature
    {
        get
        {
            string size = infiniteWorld ? "inf" : (chunksX + "x" + chunksY + "x" + chunksZ);

            if (infiniteWorld)
                size = "inf" + chunksY;

            return seed + ":" + size + ":"
                + cellSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":s1";
        }
    }

    const int OpLogMagic = 0x3130504F;   // "OP01"

    // Schreibt alle Veraenderungen (Graben, Einebnen, Boeschung) in der Reihenfolge, in der sie passiert sind.
    public void WriteChanges(System.IO.BinaryWriter w)
    {
        w.Write(OpLogMagic);
        w.Write(ops.Count);

        for (int i = 0; i < ops.Count; i++)
        {
            Op op = ops[i];

            w.Write(op.type);
            w.Write(op.c.x);
            w.Write(op.c.y);
            w.Write(op.c.z);
            w.Write(op.half.x);
            w.Write(op.half.y);
            w.Write(op.yaw);
            w.Write(op.p0);
            w.Write(op.p1);
            w.Write(op.p2);
        }
    }

    // Stellt die Welt aus dem Op-Log her: Alles wird verworfen und mit den gespeicherten
    // Veraenderungen neu aufgebaut (die Spalten entstehen dabei nach und nach neu).
    public bool ReadChanges(System.IO.BinaryReader r)
    {
        if (r.ReadInt32() != OpLogMagic)
            return false;

        int count = r.ReadInt32();

        if (count < 0 || count > 50000000)
            return false;

        List<Op> list = new List<Op>(Mathf.Min(count, 100000));

        for (int i = 0; i < count; i++)
        {
            Op op = new Op();

            op.type = r.ReadByte();
            op.c = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            op.half = new Vector2(r.ReadSingle(), r.ReadSingle());
            op.yaw = r.ReadSingle();
            op.p0 = r.ReadSingle();
            op.p1 = r.ReadSingle();
            op.p2 = r.ReadSingle();

            if (op.type < OpDig || op.type > OpBackfill)
                return false;

            list.Add(op);
        }

        ResetTerrain(list);

        return true;
    }

    // Wirft alle geladenen Spalten weg und uebernimmt ein neues Op-Log.
    void ResetTerrain(List<Op> newOps)
    {
        foreach (Column col in columns.Values)
            UnmeshColumn(col);

        columns.Clear();
        dirtyChunks.Clear();
        queue.Clear();
        queueIndex = 0;
        columnOps.Clear();

        ops = newOps;

        for (int i = 0; i < ops.Count; i++)
            RegisterOp(i);

        // Sofort neu planen.
        nextRefresh = 0f;
    }

    // Berechnet alle vorgemerkten Chunks sofort neu (z. B. direkt nach dem Graben).
    public void RebuildDirtyNow()
    {
        if (dirtyChunks.Count == 0)
            return;

        rebuildList.Clear();
        rebuildList.AddRange(dirtyChunks);
        dirtyChunks.Clear();

        foreach (Chunk c in rebuildList)
            RebuildChunk(c);
    }

    void Update()
    {
        // Nachladen und Entladen rund um den Spieler.
        float budget = (!IsReady || preloads.Count > 0) ? loadMillisecondsPerFrame : maxMillisecondsPerFrame;
        StreamStep(budget);

        if (dirtyChunks.Count == 0)
            return;

        // Rest, der nicht sofort neu berechnet wurde, kommt hier nach und nach dran.
        rebuildList.Clear();

        foreach (Chunk c in dirtyChunks)
        {
            rebuildList.Add(c);

            if (rebuildList.Count >= rebuildChunksPerFrame)
                break;
        }

        foreach (Chunk c in rebuildList)
        {
            dirtyChunks.Remove(c);
            RebuildChunk(c);
        }
    }

    // =====================================================================
    //  Mesh-Bau (Surface Nets)
    // =====================================================================

    static int ScratchIndex(int sx, int sy, int sz)
    {
        return sx + Sc * (sy + Sc * sz);
    }

    // Kopiert die 18 x 18 x 18 Punkte, die ein Chunk braucht, aus den Spalten in den Zwischenspeicher.
    // Punkte ausserhalb der Welt (oder der Hoehe) zaehlen als Luft.
    void FillScratch(Chunk c)
    {
        int x0 = c.cx * ChunkCells - 1;
        int y0 = c.cy * ChunkCells - 1;
        int z0 = c.cz * ChunkCells - 1;

        float air = -cellSize * 0.5f;

        for (int sz = 0; sz < Sc; sz++)
        {
            int gz = z0 + sz;

            for (int sx = 0; sx < Sc; sx++)
            {
                int gx = x0 + sx;

                Column col = PointInWorld(gx, gz) ? GetColumn(gx >> Shift, gz >> Shift) : null;

                int b = col != null ? ((gz & Mask) * PY) * ChunkCells + (gx & Mask) : 0;

                for (int sy = 0; sy < Sc; sy++)
                {
                    int gy = y0 + sy;
                    int i = ScratchIndex(sx, sy, sz);

                    if (col == null || gy < 0 || gy >= PY)
                    {
                        scratchDensity[i] = air;
                        scratchMaterial[i] = 0;
                    }
                    else
                    {
                        int index = b + gy * ChunkCells;

                        scratchDensity[i] = col.d[index];
                        scratchMaterial[i] = col.m[index];
                    }
                }
            }
        }
    }

    // Berechnet den Eckpunkt einer Zelle (falls die Oberflaeche sie schneidet).
    // (sx, sy, sz) = Zelle im Zwischenspeicher; der Punkt wird relativ zum Chunk abgelegt.
    Cell ComputeCell(int sx, int sy, int sz)
    {
        Cell cell = new Cell();

        // Dichte der 8 Ecken lesen und merken, welche fest sind.
        int mask = 0;

        for (int i = 0; i < 8; i++)
        {
            float d = scratchDensity[ScratchIndex(
                sx + (i & 1),
                sy + ((i >> 1) & 1),
                sz + ((i >> 2) & 1))];

            cornerDensity[i] = d;

            if (d > 0f)
                mask |= 1 << i;
        }

        // Alles fest oder alles Luft: die Oberflaeche geht nicht hindurch.
        if (mask == 0 || mask == 255)
            return cell;

        // Eckpunkt = Mittelwert aller Stellen, an denen die Oberflaeche eine
        // Kante der Zelle kreuzt.
        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int e = 0; e < 12; e++)
        {
            int a = EdgeCorners[e, 0];
            int b = EdgeCorners[e, 1];

            bool solidA = ((mask >> a) & 1) == 1;
            bool solidB = ((mask >> b) & 1) == 1;

            if (solidA == solidB)
                continue;

            // Wo zwischen den beiden Ecken wechselt der Wert das Vorzeichen?
            float t = cornerDensity[a] / (cornerDensity[a] - cornerDensity[b]);

            Vector3 pa = new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1);
            Vector3 pb = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);

            sum += Vector3.Lerp(pa, pb, t);
            count++;
        }

        Vector3 local = sum / count;

        // Der Zwischenspeicher beginnt eine Zelle vor dem Chunk (daher -1).
        cell.pos = new Vector3(sx - 1 + local.x, sy - 1 + local.y, sz - 1 + local.z) * cellSize;

        // Glatte Flaechenrichtung: zeigt dorthin, wo die Dichte abnimmt.
        float[] d8 = cornerDensity;

        Vector3 gradient = new Vector3(
            (d8[1] + d8[3] + d8[5] + d8[7]) - (d8[0] + d8[2] + d8[4] + d8[6]),
            (d8[2] + d8[3] + d8[6] + d8[7]) - (d8[0] + d8[1] + d8[4] + d8[5]),
            (d8[4] + d8[5] + d8[6] + d8[7]) - (d8[0] + d8[1] + d8[2] + d8[3]));

        Vector3 normal = -gradient;

        cell.normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;

        // Material-Gewichte: Jeder feste Eckpunkt bringt sein Material ein, und
        // zwar umso staerker, je naeher der Eckpunkt der Zelle am Eckpunkt
        // (trilineare Gewichtung). So geht ein Material weich ins naechste ueber.
        float wGrass = 0f, wDirt = 0f, wStone = 0f, wCoal = 0f, wIron = 0f;

        for (int i = 0; i < 8; i++)
        {
            if (((mask >> i) & 1) == 0)
                continue;

            float wx = (i & 1) == 1 ? local.x : 1f - local.x;
            float wy = ((i >> 1) & 1) == 1 ? local.y : 1f - local.y;
            float wz = ((i >> 2) & 1) == 1 ? local.z : 1f - local.z;

            float weight = wx * wy * wz + 0.0001f;

            byte m = scratchMaterial[ScratchIndex(
                sx + (i & 1),
                sy + ((i >> 1) & 1),
                sz + ((i >> 2) & 1))];

            switch ((BlockType)m)
            {
                case BlockType.Grass: wGrass += weight; break;
                case BlockType.Dirt:  wDirt += weight; break;
                case BlockType.Coal:  wCoal += weight; break;
                case BlockType.Iron:  wIron += weight; break;
                default:              wStone += weight; break;
            }
        }

        float sumWeights = wGrass + wDirt + wStone + wCoal + wIron;

        cell.weights = new Vector4(wGrass, wDirt, wStone, wCoal) / sumWeights;
        cell.iron = wIron / sumWeights;
        cell.vertex = -1;
        cell.valid = true;

        return cell;
    }

    // Erzeugt Mesh und Collider eines Chunks neu.
    void RebuildChunk(Chunk c)
    {
        verts.Clear();
        normals.Clear();
        colors.Clear();
        uvs.Clear();
        triangles.Clear();

        int x0 = c.cx * ChunkCells;
        int y0 = c.cy * ChunkCells;
        int z0 = c.cz * ChunkCells;

        int span = ChunkCells + 1;   // Zellen im Zwischenspeicher pro Achse

        FillScratch(c);

        // 1) Alle Zellen berechnen, die dieser Chunk braucht: auch eine Reihe
        //    ausserhalb (Index -1), weil Flaechen an den Chunk-Raendern die
        //    Eckpunkte der Nachbarzellen benutzen.
        for (int lz = 0; lz < span; lz++)
        {
            for (int ly = 0; ly < span; ly++)
            {
                for (int lx = 0; lx < span; lx++)
                {
                    int gx = x0 - 1 + lx;
                    int gy = y0 - 1 + ly;
                    int gz = z0 - 1 + lz;

                    int cacheIndex = (lz * span + ly) * span + lx;

                    // Ausserhalb der Welt gibt es keine Zellen.
                    if (gy < 0 || gy >= CellsY || !PointInWorld(gx, gz))
                    {
                        cellCache[cacheIndex] = new Cell();
                        continue;
                    }

                    cellCache[cacheIndex] = ComputeCell(lx, ly, lz);
                }
            }
        }

        // 2) Flaechen: Fuer jede Gitterkante, an der die Oberflaeche vorbeigeht
        //    (fester und leerer Punkt), entsteht ein Viereck aus den vier
        //    Zellen um die Kante. Dieser Chunk uebernimmt die Kanten, die in
        //    ihm starten.
        for (int lz = 0; lz < ChunkCells; lz++)
        {
            for (int ly = 0; ly < ChunkCells; ly++)
            {
                for (int lx = 0; lx < ChunkCells; lx++)
                {
                    bool solidHere = scratchDensity[ScratchIndex(lx + 1, ly + 1, lz + 1)] > 0f;

                    for (int axis = 0; axis < 3; axis++)
                    {
                        bool solidThere = scratchDensity[ScratchIndex(
                            lx + 1 + (axis == 0 ? 1 : 0),
                            ly + 1 + (axis == 1 ? 1 : 0),
                            lz + 1 + (axis == 2 ? 1 : 0))] > 0f;

                        if (solidHere == solidThere)
                            continue;

                        // Die vier Zellen um die Kante (Nummern im Zwischenspeicher).
                        int i0 = CachedIndex(lx, ly, lz, span, axis, 0);
                        int i1 = CachedIndex(lx, ly, lz, span, axis, 1);
                        int i2 = CachedIndex(lx, ly, lz, span, axis, 2);
                        int i3 = CachedIndex(lx, ly, lz, span, axis, 3);

                        if (!cellCache[i0].valid || !cellCache[i1].valid ||
                            !cellCache[i2].valid || !cellCache[i3].valid)
                            continue;

                        // Das Viereck aus den vier Zellen um die Kante ("aussen" zeigt vom
                        // festen zum leeren Punkt).
                        AddQuad(i0, i1, i2, i3, axis, solidHere);
                    }
                }
            }
        }

        // Chunk ist leer: nichts anzeigen.
        if (verts.Count == 0)
        {
            if (c.go != null)
            {
                c.collider.sharedMesh = null;
                c.filter.sharedMesh = null;
                c.go.SetActive(false);
            }

            return;
        }

        if (c.go == null)
            CreateChunkObject(c);

        c.go.SetActive(true);

        Mesh m = c.mesh;
        m.Clear();

        // Bei sehr vielen Ecken reicht das normale 16-Bit-Format nicht.
        m.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;

        m.SetVertices(verts);
        m.SetNormals(normals);
        m.SetColors(colors);
        m.SetUVs(1, uvs);
        m.SetTriangles(triangles, 0);
        m.RecalculateBounds();

        c.filter.sharedMesh = m;

        // Collider neu zuweisen, damit Unity ihn aktualisiert.
        c.collider.sharedMesh = null;
        c.collider.sharedMesh = m;
    }

    // Nummer einer der vier Zellen um eine Kante im Zwischenspeicher.
    int CachedIndex(int lx, int ly, int lz, int span, int axis, int which)
    {
        int ix = lx + 1 + QuadCells[axis, which, 0];
        int iy = ly + 1 + QuadCells[axis, which, 1];
        int iz = lz + 1 + QuadCells[axis, which, 2];

        return (iz * span + iy) * span + ix;
    }

    // Traegt den Eckpunkt einer Zelle ins Mesh ein (nur beim ersten Mal) und
    // gibt seine Nummer zurueck. So teilen sich benachbarte Dreiecke ihre
    // Eckpunkte, und die Flaeche wird glatt beleuchtet.
    int GetVertex(int cacheIndex)
    {
        if (cellCache[cacheIndex].vertex >= 0)
            return cellCache[cacheIndex].vertex;

        Cell cell = cellCache[cacheIndex];

        int index = verts.Count;

        verts.Add(cell.pos);
        normals.Add(cell.normal);
        colors.Add(new Color(cell.weights.x, cell.weights.y, cell.weights.z, cell.weights.w));
        uvs.Add(new Vector2(cell.iron, 0f));

        cellCache[cacheIndex].vertex = index;

        return index;
    }

    // Fuegt das Viereck um eine Gitterkante als zwei Dreiecke hinzu.
    //
    // WICHTIG: Die Umlaufrichtung wird EINMAL fuer das ganze Viereck aus dem idealen Gitter
    // bestimmt, nicht fuer jedes Dreieck einzeln aus den verschobenen Eckpunkten. An scharfen
    // Kanten (z. B. am Rand eines frisch gegrabenen Lochs) sind die vier Eckpunkte verdreht;
    // wuerde man jedes Dreieck einzeln ausrichten, bekaeme eines davon die falsche Richtung.
    // Solche umgedrehten Dreiecke sind unsichtbar (Rueckseite), werfen aber dunkle Schatten
    // und wirken wie durchsichtige Spitzen im Gelaende.
    void AddQuad(int i0, int i1, int i2, int i3, int axis, bool solidHere)
    {
        Vector3 p0 = new Vector3(QuadCells[axis, 0, 0], QuadCells[axis, 0, 1], QuadCells[axis, 0, 2]);
        Vector3 p1 = new Vector3(QuadCells[axis, 1, 0], QuadCells[axis, 1, 1], QuadCells[axis, 1, 2]);
        Vector3 p2 = new Vector3(QuadCells[axis, 2, 0], QuadCells[axis, 2, 1], QuadCells[axis, 2, 2]);

        // Sollrichtung (Unity: die Flaeche zeigt dorthin, wohin Cross(b - a, c - a) zeigt).
        Vector3 ideal = Vector3.Cross(p1 - p0, p2 - p0);
        float outward = solidHere ? 1f : -1f;

        int a = i0, b = i1, c = i2, d = i3;

        // Zeigt der Umlauf nach innen, wird er fuer das ganze Viereck umgekehrt.
        if (ideal[axis] * outward < 0f)
        {
            b = i3;
            d = i1;
        }

        // Die kuerzere Diagonale waehlen: ergibt weniger spitze, verdrehte Dreiecke.
        float diagonalAC = (cellCache[a].pos - cellCache[c].pos).sqrMagnitude;
        float diagonalBD = (cellCache[b].pos - cellCache[d].pos).sqrMagnitude;

        if (diagonalAC <= diagonalBD)
        {
            AddTriangle(a, b, c);
            AddTriangle(a, c, d);
        }
        else
        {
            AddTriangle(b, c, d);
            AddTriangle(b, d, a);
        }
    }

    // Fuegt ein Dreieck hinzu (die Richtung ist schon durch AddQuad festgelegt).
    void AddTriangle(int a, int b, int c)
    {
        Vector3 n = Vector3.Cross(
            cellCache[b].pos - cellCache[a].pos,
            cellCache[c].pos - cellCache[a].pos);

        // Entartetes Dreieck (keine Flaeche): ueberspringen.
        if (n.sqrMagnitude < 1e-10f)
            return;

        triangles.Add(GetVertex(a));
        triangles.Add(GetVertex(b));
        triangles.Add(GetVertex(c));
    }

    // Legt das Spielobjekt fuer einen Chunk an (erst wenn er wirklich etwas enthaelt).
    void CreateChunkObject(Chunk c)
    {
        GameObject go = new GameObject("Chunk_" + c.cx + "_" + c.cy + "_" + c.cz);
        go.transform.SetParent(transform, false);
        go.transform.localPosition =
            new Vector3(c.cx, c.cy, c.cz) * (ChunkCells * cellSize);

        c.filter = go.AddComponent<MeshFilter>();

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = terrainMaterial;

        c.collider = go.AddComponent<MeshCollider>();

        c.mesh = new Mesh();
        c.mesh.name = go.name;
        c.mesh.MarkDynamic();   // wird oft veraendert

        // Das Bausystem erkennt das Gelaende als Boden, auf dem man bauen darf.
        BuildSurface surface = go.AddComponent<BuildSurface>();
        surface.type = BuildSurfaceType.Ground;

        // Zusaetzlich den Tag "Ground" setzen (falls er im Projekt existiert).
        try { go.tag = "Ground"; }
        catch (UnityException) { }

        c.go = go;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (loadingCamera != null)
            Destroy(loadingCamera);

        if (terrainMaterial != null)
            Destroy(terrainMaterial);

        for (int i = 0; i < MaterialCount; i++)
        {
            if (textures[i] != null)
                Destroy(textures[i]);
        }
    }

    // =====================================================================
    //  Anzeige waehrend der Erzeugung
    // =====================================================================

    void OnGUI()
    {
        if (IsReady)
            return;

        // Ist das Hauptmenue offen, zeigt es den Balken selbst.
        if (MainMenu.IsOpen)
            return;

        // Ladebalken mit Prozentanzeige mitten auf dem Bildschirm.
        LoadingScreen.DrawFullscreen(
            Loc.T("world.loading", Mathf.RoundToInt(Progress * 100f)),
            Progress,
            1f);
    }
}
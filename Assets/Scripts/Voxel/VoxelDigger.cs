using UnityEngine;

// Abbauen von Hand: Linke Maustaste gedrueckt halten, waehrend das Fadenkreuz
// auf das Gelaende zeigt. Bei jedem Grabvorgang wird eine Kugel aus dem
// Gelaende geschnitten. Haengt am Spieler (oder an der Kamera).
public class VoxelDigger : MonoBehaviour
{
    [SerializeField] private VoxelWorld world;

    [Tooltip("Kamera des Spielers. Leer = Hauptkamera.")]
    [SerializeField] private Camera playerCamera;

    // Haerte eines Materials: So viele Sekunden dauert ein Grabvorgang (bei Tempo 1).
    [System.Serializable]
    public class MaterialHardness
    {
        public BlockType material = BlockType.Stone;
        [Tooltip("Sekunden pro Grabvorgang. Hoeher = haerter.")]
        public float seconds = 1f;
    }

    [Header("Haerte der Materialien")]
    [Tooltip("Wie lange das Graben pro Material dauert. Nicht eingetragene Materialien nehmen den Standardwert aus dem Code.")]
    [SerializeField] private MaterialHardness[] hardness =
    {
        new MaterialHardness { material = BlockType.Grass, seconds = 0.5f },
        new MaterialHardness { material = BlockType.Dirt,  seconds = 0.5f },
        new MaterialHardness { material = BlockType.Stone, seconds = 1.1f },
        new MaterialHardness { material = BlockType.Coal,  seconds = 0.9f },
        new MaterialHardness { material = BlockType.Iron,  seconds = 1.6f }
    };

    // Haerte eines Materials: Inspector-Eintrag, sonst der Standardwert.
    private float HardnessOf(BlockType type)
    {
        if (hardness != null)
        {
            foreach (MaterialHardness h in hardness)
            {
                if (h != null && h.material == type && h.seconds > 0.01f)
                    return h.seconds;
            }
        }

        return BlockInfo.Hardness(type);
    }

    [Header("Abbau")]
    [Tooltip("Reichweite in Metern.")]
    [SerializeField] private float reach = 4f;

    [Tooltip("Radius der ausgegrabenen Kugel in Metern.")]
    [SerializeField] private float digRadius = 0.45f;

    [Tooltip("Abbau-Tempo. 1 = Hand, groesser = schneller (spaeter Werkzeuge).")]
    [SerializeField] private float digSpeed = 1f;

    [Header("Beute (nach ausgegrabenem Volumen)")]
    [Tooltip("Wird fuer ausgegrabene Kohle erzeugt (dein CoalChunk-Prefab).")]
    [SerializeField] private GameObject coalDropPrefab;

    [Tooltip("Kubikmeter Kohle, die ein CoalChunk ergeben.")]
    [SerializeField] private float coalVolumePerPiece = 0.05f;

    [Tooltip("Optional: Beute fuer Eisen.")]
    [SerializeField] private GameObject ironDropPrefab;

    [Tooltip("Kubikmeter Eisen, die ein Stueck ergeben.")]
    [SerializeField] private float ironVolumePerPiece = 0.08f;

    [Tooltip("Hoechstens so viele Stuecke pro Grabvorgang.")]
    [SerializeField] private int maxDropsPerDig = 12;

    // Werkzeug in der Hand (oder null = blosse Hand) und die damit geltenden Werte.
    private DigTool tool;
    private float CurReach { get { return reach + (tool != null ? tool.reachBonus : 0f); } }
    private float CurRadius { get { return digRadius * (tool != null ? tool.radiusMultiplier : 1f); } }

    // Anvisierte Stelle
    private bool hasTarget;
    private Vector3 targetPoint;
    private Vector3 targetNormal;
    private BlockType targetMaterial;

    // Abbaufortschritt (0..1)
    private float progress;

    // Steht eine Maschine in der Grabstelle? Dann wird nicht gegraben
    // (sonst wuerde sie in der Luft haengen).
    private bool blocked;

    // Noch nicht ausgegebene Restmengen: Ein Grabvorgang ergibt oft nur einen
    // Bruchteil eines Stuecks, der Rest wird gesammelt.
    private float coalPieces;
    private float ironPieces;

    // Durchsichtige Kugel, die zeigt, was ausgegraben wird.
    private GameObject highlight;
    private Material highlightMaterial;

    void Start()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        CreateHighlight();
    }

    void OnDestroy()
    {
        if (highlightMaterial != null)
            Destroy(highlightMaterial);

        if (highlight != null)
            Destroy(highlight);
    }

    void Update()
    {
        tool = CoalPickup.LocalHeldItem as DigTool;

        // Ohne Welt, Kamera oder fertige Erzeugung gibt es nichts zu tun.
        if (world == null || playerCamera == null || !world.IsReady)
        {
            ResetTarget();
            return;
        }

        // Nicht graben, solange gebaut/abgerissen wird oder die Maus frei ist.
        if (BuildingSystem.IsBuildingActive ||
            Cursor.lockState != CursorLockMode.Locked)
        {
            ResetTarget();
            return;
        }

        if (!TryFindTarget())
        {
            ResetTarget();
            return;
        }

        blocked = IsMachineInTheWay();

        if (!blocked && GameInput.Held(GameAction.Primary) && BlockInfo.IsDiggable(targetMaterial))
        {
            // Je haerter das Material, desto laenger dauert ein Grabvorgang.
            float speed = digSpeed * (tool != null ? tool.SpeedFor(targetMaterial) : 1f);
            progress += Time.deltaTime * speed / HardnessOf(targetMaterial);

            if (tool != null)
                tool.SetSwing(progress);

            if (progress >= 1f)
            {
                Dig();
                progress = 0f;
            }
        }
        else
        {
            progress = 0f;
        }

        UpdateHighlight();
    }

    void ResetTarget()
    {
        hasTarget = false;
        progress = 0f;

        if (highlight != null)
            highlight.SetActive(false);
    }

    // Sucht die Stelle, auf die das Fadenkreuz zeigt.
    bool TryFindTarget()
    {
        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(Screen.width / 2f, Screen.height / 2f));

        if (!Physics.Raycast(
            ray,
            out RaycastHit hit,
            CurReach,
            ~0,
            QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        // Nur das Gelaende ist abbaubar, keine Maschinen o. ae.
        if (!world.OwnsCollider(hit.collider))
            return false;

        targetPoint = hit.point;
        targetNormal = hit.normal;
        hasTarget = true;

        // Material knapp unter der Oberflaeche bestimmt die Abbau-Dauer.
        targetMaterial = world.GetMaterialNear(
            hit.point - hit.normal * (world.cellSize * 0.3f));

        return true;
    }

    // Ueberschneidet die Grabkugel (mit etwas Spielraum) eine gebaute Maschine?
    bool IsMachineInTheWay()
    {
        Collider[] hits = Physics.OverlapSphere(
            DigCenter(),
            CurRadius + 0.15f,
            ~0,
            QueryTriggerInteraction.Ignore);

        foreach (Collider col in hits)
        {
            if (col.GetComponentInParent<PlacedMachine>() != null)
                return true;
        }

        return false;
    }

    // Mittelpunkt der Kugel: etwas hinter der Oberflaeche, damit wirklich
    // Material herausgeschnitten wird.
    Vector3 DigCenter()
    {
        return targetPoint - targetNormal * (CurRadius * 0.35f);
    }

    // Schneidet die Kugel aus dem Gelaende und gibt die Beute aus.
    void Dig()
    {
        // Graben laeuft als Kommando: Im Mehrspieler sehen alle dasselbe Loch.
        NetSync.Submit(NetCommand.Dig(DigCenter(), CurRadius));

        // Das Werkzeug nutzt sich ab. Ist es kaputt, verschwindet es aus der Hand.
        if (tool != null && tool.Wear(targetMaterial))
        {
            NetItems.Consume(tool.gameObject);
            tool = null;
        }
    }

    // Der Digger des lokalen Spielers (bekommt die Beute).
    public static VoxelDigger Local { get; private set; }

    void OnEnable()
    {
        Local = this;
    }

    // Wird aufgerufen, nachdem die Kugel ausgegraben wurde (nur beim Graber).
    // Rechnet das Volumen in Stuecke um und erzeugt sie.
    public void OnDug(float[] volumes, Vector3 center)
    {
        // Ausgegrabenes Volumen in Stuecke umrechnen.
        coalPieces += volumes[(int)BlockType.Coal] / coalVolumePerPiece;
        ironPieces += volumes[(int)BlockType.Iron] / ironVolumePerPiece;

        int coalCount = Mathf.Min(Mathf.FloorToInt(coalPieces), maxDropsPerDig);
        int ironCount = Mathf.Min(Mathf.FloorToInt(ironPieces), maxDropsPerDig);

        coalPieces -= coalCount;
        ironPieces -= ironCount;

        // Reste nicht unbegrenzt ansammeln.
        coalPieces = Mathf.Min(coalPieces, 2f);
        ironPieces = Mathf.Min(ironPieces, 2f);

        // Ohne Prefab gibt es nichts zu erzeugen (die Menge verfaellt).
        for (int i = 0; i < coalCount; i++)
            SpawnDrop(coalDropPrefab, center, ItemKind.Coal);

        for (int i = 0; i < ironCount; i++)
            SpawnDrop(ironDropPrefab, center, ItemKind.Ore);
    }

    // Erzeugt Kohle- oder Erzstuecke an einer Stelle (z. B. an der Verladestation einer Mine, nach dem Kaufen).
    // Gibt false zurueck, wenn dafuer kein Prefab eingetragen ist (Coal Drop Prefab / Iron Drop Prefab).
    public bool DropPieces(ItemKind kind, int count, Vector3 position, float spread)
    {
        GameObject prefab = kind == ItemKind.Coal ? coalDropPrefab : ironDropPrefab;

        if (prefab == null)
            return false;

        ItemTag prefabTag = prefab.GetComponent<ItemTag>();
        string metal = prefabTag != null ? prefabTag.metal : "";
        float mass = prefabTag != null ? prefabTag.massKg : 0f;

        if (kind == ItemKind.Ore && string.IsNullOrEmpty(metal))
            metal = "Iron";

        for (int i = 0; i < count; i++)
        {
            Vector3 p = position + new Vector3(Random.Range(-spread, spread), Random.Range(0f, spread), Random.Range(-spread, spread));
            Vector3 velocity = Random.insideUnitSphere * 0.5f;

            NetItems.Spawn(prefab, p, Random.rotation, velocity, kind, metal, mass, false);
        }

        return true;
    }

    // Erzeugt ein Beute-Objekt in der frisch ausgegrabenen Hoehle.
    void SpawnDrop(GameObject prefab, Vector3 center, ItemKind kind)
    {
        if (prefab == null)
            return;

        // Innerhalb der Kugel, damit nichts in der Erde steckt.
        Vector3 position = center + Random.insideUnitSphere * (CurRadius * 0.5f);

        // Metall und Gewicht aus dem Prefab; Erz ohne Angabe ist Eisenerz.
        ItemTag prefabTag = prefab.GetComponent<ItemTag>();
        string metal = prefabTag != null ? prefabTag.metal : "";
        float mass = prefabTag != null ? prefabTag.massKg : 0f;

        if (kind == ItemKind.Ore && string.IsNullOrEmpty(metal))
            metal = "Iron";

        Vector3 velocity = Random.insideUnitSphere * 0.8f + Vector3.up * 1.5f;

        // Erzeugt den Gegenstand (Kohle/Erz markiert). Im Mehrspieler bekommen ihn alle Spieler.
        NetItems.Spawn(prefab, position, Random.rotation, velocity, kind, metal, mass, false);
    }

    // ---------- Anzeige ----------

    void CreateHighlight()
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return;

        highlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        highlight.name = "DigHighlight";

        // Nur zur Anzeige: kein Collider (sonst wuerde der Strahl ihn treffen).
        Destroy(highlight.GetComponent<Collider>());

        highlightMaterial = new Material(shader);

        Renderer rend = highlight.GetComponent<Renderer>();
        rend.sharedMaterial = highlightMaterial;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        highlight.SetActive(false);
    }

    // Zeigt die Kugel an der Grabstelle; sie wird beim Graben deckender.
    void UpdateHighlight()
    {
        if (highlight == null || !hasTarget)
            return;

        highlight.transform.position = DigCenter();
        highlight.transform.localScale = Vector3.one * (CurRadius * 2f);

        // Rot, wenn eine Maschine im Weg ist.
        if (blocked)
            highlightMaterial.color = new Color(1f, 0.15f, 0.1f, 0.35f);
        else
            highlightMaterial.color = new Color(
                1f, 1f, 1f,
                BlockInfo.IsDiggable(targetMaterial) ? 0.10f + 0.35f * progress : 0.04f);

        highlight.SetActive(true);
    }
}
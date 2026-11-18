using System.Collections.Generic;
using UnityEngine;

// Foerderband fuer lose Gegenstaende (Erz, Kohle, Barren, Schlacke, Kanister ...).
// Alles, was auf dem Band liegt, wird in Laufrichtung transportiert. Spieler werden
// ebenfalls mitgenommen. Das Band kann waagerecht oder schraeg (Steigung) stehen;
// mehrere Baender lassen sich hintereinander setzen. Haengt das Ende ueber dem
// Einwurf eines Hochofens oder Kessels, fallen die Gegenstaende dort hinein.
//
// - Strom: Das Band ist ein Stromverbraucher. Je mehr darauf liegt, desto mehr
//   Leistung braucht es. Bei Unterversorgung wird es langsamer, ohne Strom steht es.
// - Schalter: Ein optionaler MachineSwitch unter dem Band schaltet es ein und aus.
//
// Einrichtung im Prefab (Wurzel mit diesem Skript):
//  1. Ein Kind-Objekt mit Modell und BOX COLLIDER als Bandflaeche (kein Trigger).
//     Dessen blaue Achse (Vorwaerts) zeigt in Laufrichtung. Bei einem schraegen Band
//     das Kind-Objekt entsprechend kippen.
//  2. Den Collider im Feld "Surface" eintragen (oder leer lassen: wird gesucht).
//  3. Empfohlen: Dem Collider ein Physic Material mit wenig Reibung geben (z. B. 0.1),
//     sonst bremst die Reibung die Gegenstaende ein wenig.
//  4. Optional: "Belt Renderer" fuer die Band-Animation (die Textur laeuft mit),
//     Seitenwaende als eigene Collider, Port "Power" fuer den Strom.
public class ConveyorBelt : MonoBehaviour, IPowerConsumer
{
    [Header("Aufbau")]
    [Tooltip("Box Collider der Bandflaeche. Leer = erster fester Box Collider unter dem Band.")]
    [SerializeField] private BoxCollider surface;

    [Tooltip("Optional: Objekt, dessen blaue Achse die Laufrichtung ist. " +
             "Leer = die Achse der Bandflaeche.")]
    [SerializeField] private Transform direction;

    [Header("Bewegung")]
    [Tooltip("Geschwindigkeit in Metern pro Sekunde.")]
    [SerializeField] private float speed = 1f;

    [Tooltip("Anlaufen und Auslaufen (Meter pro Sekunde zum Quadrat).")]
    [SerializeField] private float acceleration = 2f;

    [Tooltip("So hoch ueber der Bandflaeche werden Gegenstaende erfasst (Meter).")]
    [SerializeField] private float liftHeight = 0.4f;

    [Tooltip("Zieht Gegenstaende sanft zur Bandmitte, damit sie nicht seitlich herunterrutschen. " +
             "0 = aus.")]
    [SerializeField] private float centerPull = 1f;

    [Tooltip("Spieler auf dem Band werden mitgenommen.")]
    [SerializeField] private bool carryPlayers = true;

    [Header("Strom")]
    [Tooltip("Leistung bei vielen Gegenstaenden auf dem Band.")]
    [SerializeField] private float ratedPowerKW = 1.5f;
    [Tooltip("Leistung bei leerem Band.")]
    [SerializeField] private float idlePowerKW = 0.3f;
    [Tooltip("Bei so vielen Gegenstaenden gilt das Band als voll ausgelastet.")]
    [SerializeField] private int fullLoadItems = 8;
    [Tooltip("Hoeher = wichtiger. Unwichtige Verbraucher fallen zuerst aus.")]
    [SerializeField] private int importance = 5;

    [Header("Anzeige")]
    [Tooltip("Optional: Teil des Modells, dessen Textur mit dem Band mitlaeuft.")]
    [SerializeField] private Renderer beltRenderer;
    [Tooltip("In welche Richtung die Textur wandert (X/Y des Textur-Offsets).")]
    [SerializeField] private Vector2 scrollDirection = new Vector2(0f, 1f);
    [Tooltip("Wie oft sich die Textur pro Meter Bandlaenge wiederholt.")]
    [SerializeField] private float texturesPerMeter = 1f;
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [SerializeField] private float debugHeight = 1.2f;

    private MachineSwitch machineSwitch;
    private Material beltMaterial;
    private float currentSpeed;
    private float supply = 1f;
    private int itemCount;

    // Zwischenspeicher fuer die Suche (kein Speicher-Muell pro Bild).
    private static readonly Collider[] hitBuffer = new Collider[96];
    private static readonly HashSet<Rigidbody> bodies = new HashSet<Rigidbody>();
    private static readonly HashSet<CharacterController> players = new HashSet<CharacterController>();

    private bool IsOn => machineSwitch == null || machineSwitch.IsOn;

    // Wenig Verbrauch bei leerem Band, mehr mit Last.
    public float DemandKW =>
        IsOn
            ? Mathf.Lerp(idlePowerKW, ratedPowerKW,
                Mathf.Clamp01(itemCount / Mathf.Max(1f, fullLoadItems)))
            : 0f;

    public float RatedPowerKW => ratedPowerKW;
    public int Importance => importance;
    public int ItemCount => itemCount;

    public void SetSupply(float fraction)
    {
        supply = Mathf.Clamp01(fraction);
    }

    private void Awake()
    {
        if (surface == null)
        {
            foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>(true))
            {
                if (!box.isTrigger)
                {
                    surface = box;
                    break;
                }
            }
        }

        if (surface == null)
        {
            Debug.LogError("Foerderband: Kein Box Collider fuer die Bandflaeche gefunden!", this);
            enabled = false;
            return;
        }

        machineSwitch = GetComponentInChildren<MachineSwitch>(true);

        // Eigene Material-Kopie, damit nur dieses Band seine Textur verschiebt.
        if (beltRenderer != null)
            beltMaterial = beltRenderer.material;
    }

    private void OnEnable()
    {
        PowerGrid.Register(this);
    }

    private void OnDisable()
    {
        PowerGrid.Unregister(this);
    }

    private void OnDestroy()
    {
        if (beltMaterial != null)
            Destroy(beltMaterial);
    }

    // Laufrichtung des Bandes in der Welt (unter Umstaenden schraeg).
    private Vector3 BeltDirection()
    {
        Transform t = direction != null ? direction : surface.transform;
        return t.forward;
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Geschwindigkeit: anlaufen/auslaufen, abhaengig von Schalter und Strom.
        float target = IsOn ? speed * supply : 0f;
        currentSpeed = Mathf.MoveTowards(currentSpeed, target, acceleration * dt);

        // Gegenstaende zaehlen (auch bei stehendem Band, fuer den Stromverbrauch).
        Transform st = surface.transform;
        Vector3 scale = st.lossyScale;

        Vector3 half = new Vector3(
            surface.size.x * Mathf.Abs(scale.x) * 0.5f,
            liftHeight * 0.5f,
            surface.size.z * Mathf.Abs(scale.z) * 0.5f);

        // Mitte des Erfassungsbereichs: ueber der Oberflaeche der Bandflaeche.
        Vector3 localCenter = surface.center
            + new Vector3(0f, surface.size.y * 0.5f + liftHeight * 0.5f / Mathf.Max(0.0001f, Mathf.Abs(scale.y)), 0f);

        Vector3 center = st.TransformPoint(localCenter);

        bodies.Clear();
        players.Clear();

        int count = Physics.OverlapBoxNonAlloc(
            center, half, hitBuffer, st.rotation, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider col = hitBuffer[i];

            // Das Band selbst nicht mitnehmen.
            if (col.transform.IsChildOf(transform))
                continue;

            CharacterController controller = col as CharacterController;

            if (controller != null)
            {
                players.Add(controller);
                continue;
            }

            Rigidbody rb = col.attachedRigidbody;

            if (rb != null && !rb.isKinematic)
                bodies.Add(rb);
        }

        itemCount = bodies.Count;

        if (currentSpeed < 0.001f)
            return;

        Vector3 dir = BeltDirection();
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        float beltCenterSide = Vector3.Dot(st.TransformPoint(surface.center), side);

        foreach (Rigidbody rb in bodies)
        {
            Vector3 velocity = rb.velocity;

            // Nur die Geschwindigkeit entlang des Bandes wird auf die Bandgeschwindigkeit
            // gesetzt. Fallen, Springen und Seitwaertsbewegung bleiben unberuehrt.
            Vector3 along = Vector3.Project(velocity, dir);
            velocity += dir * currentSpeed - along;

            // Sanft zur Bandmitte ziehen.
            if (centerPull > 0f)
            {
                float offset = Vector3.Dot(rb.worldCenterOfMass, side) - beltCenterSide;
                velocity -= side * Mathf.Clamp(offset, -0.5f, 0.5f) * centerPull;
            }

            rb.velocity = velocity;
        }

        if (carryPlayers)
        {
            foreach (CharacterController controller in players)
            {
                if (controller != null && controller.enabled)
                    controller.Move(dir * currentSpeed * dt);
            }
        }
    }

    private void Update()
    {
        // Band-Animation: Die Textur wandert mit der Geschwindigkeit.
        if (beltMaterial == null || currentSpeed < 0.001f)
            return;

        Vector2 offset = beltMaterial.mainTextureOffset;
        offset += scrollDirection * (currentSpeed * Time.deltaTime * texturesPerMeter);
        offset.x -= Mathf.Floor(offset.x);
        offset.y -= Mathf.Floor(offset.y);
        beltMaterial.mainTextureOffset = offset;
    }

    // ---------- Anzeige ----------

    private void OnGUI()
    {
        if (!showDebugInfo)
            return;

        Camera cam = Camera.main;

        if (cam == null)
            return;

        Vector3 world = transform.position + Vector3.up * debugHeight;

        if ((world - cam.transform.position).sqrMagnitude
            > debugDistance * debugDistance)
            return;

        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return;

        string status = Loc.T(
            !IsOn ? "conveyor.status.off"
            : supply <= 0.05f ? "conveyor.status.nopower"
            : "conveyor.status.running");

        string text = Loc.T(
            "conveyor.info", status,
            currentSpeed.ToString("F1"), itemCount);

        GameSettings.DrawBoxAbove(screen, text);
    }
}
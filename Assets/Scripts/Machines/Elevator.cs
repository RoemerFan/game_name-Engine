using System.Collections.Generic;
using UnityEngine;

// Personen- und Warenaufzug. Eine Plattform faehrt zwischen mehreren Haltepunkten
// (Etagen). Spieler und lose Gegenstaende auf der Plattform fahren mit.
//
// - Strom: Der Aufzug ist ein Stromverbraucher (wie die Pumpe). Beim Fahren braucht
//   er die volle Leistung, im Stillstand fast nichts. Bei Unterversorgung faehrt er
//   langsamer, ohne Strom steht er (auch zwischen den Etagen).
// - Last: Ueber der Tragkraft ("Capacity Kg") faehrt er nicht los ("Ueberlast").
// - Bedienung: Mit ElevatorButton-Knoepfen (in der Kabine und an jeder Etage).
//   Die Knoepfe sind normale bedienbare Objekte, funktionieren also auch im Mehrspieler.
//
// Einrichtung im Prefab (Wurzel mit diesem Skript):
//  1. "Platform": Kind-Objekt mit Modell und festem Collider (kein Trigger), der Boden
//     der Kabine. Das Skript macht daraus selbst einen "kinematischen" Rigidbody.
//     Knoepfe in der Kabine koennen Kinder der Plattform sein; dann fahren sie mit.
//  2. "Stops": Fuer jede Etage ein leeres Kind-Objekt (NICHT unter der Plattform) an der
//     Position, an der die Plattform halten soll. Reihenfolge = Etagen-Nummer.
//  3. Pro Etage einen Knopf: leeres Kind-Objekt + Trigger-Collider + ElevatorButton.
//  4. Optional: Port "Power" fuer den Strom.
public class Elevator : MonoBehaviour, IPowerConsumer, ISaveable
{
    [Header("Aufbau")]
    [Tooltip("Die bewegliche Plattform (Kind-Objekt mit Collider).")]
    [SerializeField] private Transform platform;

    [Tooltip("Haltepunkte der Etagen (leere Objekte, nicht unter der Plattform).")]
    [SerializeField] private Transform[] stops;

    [Tooltip("Optional: Namen der Etagen, z. B. EG, 1. OG. Leer = 'Etage 1, 2, ...'. " +
             "Texte mit @ werden uebersetzt.")]
    [SerializeField] private string[] floorNames;

    [Header("Bewegung")]
    [Tooltip("Hoechstgeschwindigkeit in Metern pro Sekunde.")]
    [SerializeField] private float speed = 2f;

    [Tooltip("Anfahren und Bremsen (Meter pro Sekunde zum Quadrat).")]
    [SerializeField] private float acceleration = 1.5f;

    [Header("Last")]
    [Tooltip("Tragkraft in Kilogramm (Spieler + lose Gegenstaende auf der Plattform).")]
    [SerializeField] private float capacityKg = 1000f;

    [Tooltip("Gewicht eines Spielers.")]
    [SerializeField] private float playerMassKg = 80f;

    [Tooltip("So hoch ueber der Plattform werden Mitfahrer erfasst (Meter).")]
    [SerializeField] private float riderHeight = 2.2f;

    [Header("Strom")]
    [SerializeField] private float ratedPowerKW = 6f;
    [SerializeField] private float idlePowerKW = 0.2f;
    [Tooltip("Hoeher = wichtiger. Unwichtige Verbraucher fallen zuerst aus.")]
    [SerializeField] private int importance = 8;

    // ----- Zustand -----
    private Rigidbody body;
    private int targetFloor = -1;     // -1 = steht
    private int currentFloor;         // zuletzt erreichte Etage
    private float currentSpeed;
    private float supply = 1f;
    private bool moving;
    private bool overload;
    private float lastLoadKg;
    private bool stateLoadedOnce;

    // Bereich ueber der Plattform, in dem Mitfahrer erfasst werden.
    private Vector3 riderOffset;      // Mitte relativ zur Plattform-Position
    private Vector3 riderHalfExtents;

    // Zwischenspeicher fuer die Suche (kein Speicher-Muell pro Bild).
    private static readonly Collider[] hitBuffer = new Collider[64];
    private static readonly HashSet<Rigidbody> bodies = new HashSet<Rigidbody>();
    private static readonly HashSet<CharacterController> players = new HashSet<CharacterController>();

    // ----- Fuer Knoepfe, Anzeige und andere Skripte -----
    public int FloorCount => stops != null ? stops.Length : 0;
    public int CurrentFloor => currentFloor;
    public bool IsMoving => moving;

    public float DemandKW => moving ? ratedPowerKW : idlePowerKW;
    public float RatedPowerKW => ratedPowerKW;
    public int Importance => importance;

    public void SetSupply(float fraction)
    {
        supply = Mathf.Clamp01(fraction);
    }

    private void Awake()
    {
        if (platform == null || stops == null || stops.Length == 0)
        {
            Debug.LogError("Aufzug: Plattform oder Haltepunkte fehlen!", this);
            enabled = false;
            return;
        }

        // Die Plattform wird per Code bewegt (kinematisch), alles darauf nimmt Physik mit.
        body = platform.GetComponent<Rigidbody>();

        if (body == null)
            body = platform.gameObject.AddComponent<Rigidbody>();

        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        // Bereich fuer Mitfahrer aus den festen Collidern der Plattform bestimmen.
        Bounds bounds = new Bounds(platform.position, Vector3.zero);
        bool found = false;

        foreach (Collider col in platform.GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger)
                continue;

            if (!found)
            {
                bounds = col.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        if (!found)
        {
            Debug.LogWarning("Aufzug: Die Plattform hat keinen festen Collider.", this);
            bounds = new Bounds(platform.position, new Vector3(2f, 0.2f, 2f));
        }

        riderHalfExtents = new Vector3(bounds.extents.x, riderHeight * 0.5f, bounds.extents.z);
        riderOffset = (bounds.center - platform.position)
            + Vector3.up * (bounds.extents.y + riderHeight * 0.5f);

        // Start: an der naechsten Etage.
        currentFloor = NearestFloor(platform.position);
    }

    private void OnEnable()
    {
        PowerGrid.Register(this);
    }

    private void OnDisable()
    {
        PowerGrid.Unregister(this);
    }

    private int NearestFloor(Vector3 position)
    {
        int best = 0;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < stops.Length; i++)
        {
            if (stops[i] == null)
                continue;

            float d = (stops[i].position - position).sqrMagnitude;

            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    // ---------- Bedienung ----------

    // Schickt den Aufzug zu einer Etage (von den Knoepfen aufgerufen).
    public void GoTo(int floor)
    {
        if (stops == null || platform == null ||
            floor < 0 || floor >= stops.Length || stops[floor] == null)
            return;

        // Steht er schon dort, passiert nichts.
        if (targetFloor < 0 &&
            (stops[floor].position - platform.position).sqrMagnitude < 0.0025f)
        {
            currentFloor = floor;
            return;
        }

        targetFloor = floor;
    }

    public string FloorName(int floor)
    {
        if (floorNames != null && floor >= 0 && floor < floorNames.Length &&
            !string.IsNullOrEmpty(floorNames[floor]))
            return Loc.Resolve(floorNames[floor]);

        return Loc.T("elevator.floor", floor + 1);
    }

    // Kurzer Zustandstext (steht beim Anvisieren eines Knopfs).
    public string GetStatus()
    {
        if (overload)
            return Loc.T("elevator.status.overload",
                lastLoadKg.ToString("F0"), capacityKg.ToString("F0"));

        if (targetFloor >= 0 && supply <= 0.05f)
            return Loc.T("elevator.status.nopower");

        if (targetFloor >= 0)
            return Loc.T("elevator.status.moving", FloorName(targetFloor));

        return Loc.T("elevator.status.idle", FloorName(currentFloor));
    }

    // ---------- Fahren ----------

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        moving = false;

        if (targetFloor < 0 || stops[targetFloor] == null)
        {
            overload = false;
            currentSpeed = 0f;
            return;
        }

        Vector3 position = body.position;
        Vector3 goal = stops[targetFloor].position;
        Vector3 toGoal = goal - position;
        float distance = toGoal.magnitude;

        // Angekommen.
        if (distance < 0.005f)
        {
            Arrive(goal);
            return;
        }

        // Last auf der Plattform pruefen.
        float load = CollectRiders();
        lastLoadKg = load;
        overload = load > capacityKg;

        if (overload || supply <= 0.05f)
        {
            currentSpeed = 0f;
            return;
        }

        // Weich anfahren und rechtzeitig bremsen.
        float maxSpeed = speed * supply;
        float brakeSpeed = Mathf.Sqrt(2f * acceleration * distance);
        float wanted = Mathf.Min(maxSpeed, brakeSpeed);

        currentSpeed = Mathf.MoveTowards(currentSpeed, wanted, acceleration * dt);

        float step = Mathf.Min(distance, Mathf.Max(currentSpeed * dt, 0.002f));
        Vector3 delta = toGoal / distance * step;

        body.MovePosition(position + delta);
        CarryPlayers(delta);
        moving = true;

        if (distance - step <= 0.005f)
            Arrive(goal);
    }

    private void Arrive(Vector3 goal)
    {
        body.MovePosition(goal);
        currentFloor = targetFloor;
        targetFloor = -1;
        currentSpeed = 0f;
    }

    // Sucht alles, was auf der Plattform steht. Gibt das Gesamtgewicht zurueck und
    // merkt sich die Spieler, die mitgenommen werden muessen.
    private float CollectRiders()
    {
        bodies.Clear();
        players.Clear();

        Vector3 center = body.position + riderOffset;

        int count = Physics.OverlapBoxNonAlloc(
            center, riderHalfExtents, hitBuffer, Quaternion.identity,
            ~0, QueryTriggerInteraction.Ignore);

        float mass = 0f;

        for (int i = 0; i < count; i++)
        {
            Collider col = hitBuffer[i];

            // Teile der Plattform selbst (und Knoepfe darauf) zaehlen nicht.
            if (col.transform.IsChildOf(platform))
                continue;

            CharacterController controller = col as CharacterController;

            if (controller != null)
            {
                if (players.Add(controller))
                    mass += playerMassKg;

                continue;
            }

            Rigidbody rb = col.attachedRigidbody;

            if (rb != null && !rb.isKinematic && bodies.Add(rb))
                mass += rb.mass;
        }

        return mass;
    }

    // Spieler (CharacterController) werden nicht von der Physik mitgenommen,
    // deshalb schieben wir sie um dieselbe Strecke wie die Plattform.
    private void CarryPlayers(Vector3 delta)
    {
        foreach (CharacterController controller in players)
        {
            if (controller != null && controller.enabled)
                controller.Move(delta);
        }
    }

    // ---------- Speichern (und Abgleich im Mehrspieler) ----------

    public void SaveState(StateBag bag)
    {
        if (platform == null)
            return;

        Vector3 local = transform.InverseTransformPoint(platform.position);

        bag.SetFloat("x", local.x);
        bag.SetFloat("y", local.y);
        bag.SetFloat("z", local.z);
        bag.SetFloat("target", targetFloor);
        bag.SetFloat("floor", currentFloor);
    }

    public void LoadState(StateBag bag)
    {
        if (platform == null || body == null)
            return;

        Vector3 local = new Vector3(
            bag.GetFloat("x", 0f), bag.GetFloat("y", 0f), bag.GetFloat("z", 0f));

        Vector3 world = transform.TransformPoint(local);

        int target = Mathf.RoundToInt(bag.GetFloat("target", -1f));
        targetFloor = (target >= 0 && target < stops.Length) ? target : -1;
        currentFloor = Mathf.Clamp(Mathf.RoundToInt(bag.GetFloat("floor", currentFloor)), 0, stops.Length - 1);

        // Im Mehrspieler faehrt jeder Spieler selbst. Der Stand des Hosts korrigiert
        // nur grosse Abweichungen, sonst wuerde die Plattform bei jedem Abgleich ruckeln.
        bool smallDifference = (world - platform.position).sqrMagnitude < 0.75f * 0.75f;

        if (NetSync.Mode == NetMode.Client && stateLoadedOnce && smallDifference)
            return;

        platform.position = world;
        body.position = world;
        stateLoadedOnce = true;
    }
}
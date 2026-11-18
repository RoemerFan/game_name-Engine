using System.Collections.Generic;
using UnityEngine;

// Schubkarre. Der Spieler greift die Griffe (Taste F, siehe CartPusher) und schiebt sie vor sich her.
//
// Aufbau des Prefabs:
//  1. Wurzel mit diesem Skript, einem Rigidbody und festen Collidern (Mulde, Rad, Beine).
//     Welche Achse des Modells nach VORNE (zum Rad) zeigt, stellt man bei "Front Axis" ein.
//  2. "Handle Point": leeres Kind-Objekt in der Mitte der Griffe (dort fassen die Haende an).
//  3. "Wheel Point": leeres Kind-Objekt dort, wo das Rad den Boden beruehrt (unten am Rad).
//  4. Optional "Wheel": das Rad-Modell, es dreht sich beim Fahren.
//  5. Optional "Cargo Zone": ein Box Collider (Is Trigger) ueber der Mulde. Alles darin zaehlt als
//     Ladung. Je mehr Ladung, desto langsamer wird man.
//  6. Skript "SavedItem" an die Wurzel und das Prefab bei "Item Prefabs" am SaveSystem eintragen
//     (dann wird die Karre gespeichert und im Mehrspieler bei allen gezeigt).
//
// Lose Gegenstaende legt man mit E (fallen lassen) oder R (sanft ablegen, zielt auf die Mulde)
// in die Karre.
public enum WheelbarrowFront
{
    Auto,    // Vorne = Richtung von 'Handle Point' zu 'Wheel Point' (empfohlen, keine Achse noetig)
    PlusZ,   // blaue Achse zeigt zum Rad
    PlusX,   // rote Achse zeigt zum Rad
    MinusX,  // rote Achse zeigt vom Rad weg
    MinusZ   // blaue Achse zeigt vom Rad weg
}

[RequireComponent(typeof(Rigidbody))]
public class Wheelbarrow : MonoBehaviour
{
    [Header("Ausrichtung des Modells")]
    [Tooltip("Welche Achse des Modells zeigt nach VORNE (zum Rad)? Beim Auswaehlen der Karre im " +
             "Editor siehst du die Achsen: Rot = X, Blau = Z.")]
    public WheelbarrowFront frontAxis = WheelbarrowFront.Auto;

    [Tooltip("Feinkorrektur in Grad: positiv dreht die Karre nach RECHTS (aus Sicht des Spielers von oben), " +
             "negativ nach links.")]
    public float extraYaw = 0f;

    [Header("Griffe und Rad")]
    [Tooltip("Mitte der Griffe (leeres Kind-Objekt).")]
    public Transform handlePoint;
    [Tooltip("Stelle, an der das Rad den Boden beruehrt (leeres Kind-Objekt).")]
    public Transform wheelPoint;
    [Tooltip("Optional: Das Rad-Modell, dreht sich beim Fahren.")]
    public Transform wheel;
    [Tooltip("Drehachse des Rades im lokalen Raum des Rad-Objekts: die Achse, um die es sich beim " +
             "Fahren dreht (quer zur Fahrtrichtung). Bei Front = X meist (0, 0, 1), sonst oft (1, 0, 0).")]
    public Vector3 wheelAxis = Vector3.right;
    [Min(0.01f)] public float wheelRadius = 0.2f;

    [Header("Schieben")]
    [Tooltip("Wie schnell die Karre dem Blick folgt (Grad pro Sekunde). Kleiner = traeger.")]
    public float turnSpeed = 110f;
    [Tooltip("Hoehe der Haende ueber den Fuessen des Spielers (Meter).")]
    public float gripHeight = 0.85f;
    [Tooltip("Abstand der Griffe vor dem Spieler (Meter).")]
    public float gripDistance = 0.8f;
    [Tooltip("Groesste Neigung der Karre in Grad.")]
    public float maxPitch = 35f;

    [Header("Hindernisse")]
    [Tooltip("Die Karre stoppt an Waenden und festen Gegenstaenden (der Spieler wird zurueckgehalten). " +
             "Das Gelaende zaehlt nicht als Hindernis. Die Collider der Karre sollten Box-, Kugel- " +
             "oder Kapsel-Collider sein (oder konvexe Mesh-Collider).")]
    public bool blockedByWalls = true;
    [Tooltip("Kleiner Sicherheitsabstand zur Wand (Meter).")]
    public float blockMargin = 0.02f;

    [Header("Ladung")]
    [Tooltip("Optional: Trigger-Box ueber der Mulde. Alles darin zaehlt als Ladung.")]
    public BoxCollider cargoZone;
    [Tooltip("Bei dieser Ladung (kg) ist die Geschwindigkeit auf 'Loaded Speed Factor' gesunken.")]
    public float maxLoadKg = 80f;
    [Range(0.1f, 1f)] public float loadedSpeedFactor = 0.5f;
    [Tooltip("Geschwindigkeit der leeren Karre im Verhaeltnis zum Gehen.")]
    [Range(0.1f, 1f)] public float emptySpeedFactor = 0.85f;

    private class BodyCollider
    {
        public Collider col;
        public Vector3 rel;          // Lage relativ zur Karre (in Weltmassen, Karre unrotiert gedacht)
        public Quaternion relRot;
    }

    private readonly List<BodyCollider> bodyCols = new List<BodyCollider>();
    private Vector3 boundCenterLocal;
    private float boundRadius;
    private CharacterController pusherBody;

    private Rigidbody body;
    private Quaternion frontToModel;    // dreht "vorne = Z" in die Achsen des Modells
    private Vector3 localHandle;
    private Vector3 localWheel;

    private bool pushed;
    private float yaw;
    private float pitch;
    private Vector3 targetPos;
    private Quaternion targetRot;
    private Vector3 lastWheelPos;

    private float cargoMass;
    private float nextCargoCheck;

    private static readonly Collider[] overlapBuffer = new Collider[64];
    private readonly HashSet<Rigidbody> counted = new HashSet<Rigidbody>();

    public bool IsPushed { get { return pushed; } }
    public float CargoKg { get { return cargoMass; } }

    // Faktor fuer die Gehgeschwindigkeit (leer schneller, voll langsamer).
    public float SpeedFactor
    {
        get
        {
            float load = maxLoadKg > 0f ? Mathf.Clamp01(cargoMass / maxLoadKg) : 0f;
            return Mathf.Lerp(emptySpeedFactor, loadedSpeedFactor, load);
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();

        if (handlePoint == null || wheelPoint == null)
        {
            Debug.LogError("Wheelbarrow: 'Handle Point' und 'Wheel Point' muessen gesetzt sein!", this);
            enabled = false;
            return;
        }

        // Drehung, die ein Modell mit Front entlang X/Z auf "vorne = Z" bringt.
        float frontYaw = frontAxis == WheelbarrowFront.PlusX ? 90f
                       : frontAxis == WheelbarrowFront.MinusX ? -90f
                       : frontAxis == WheelbarrowFront.MinusZ ? 180f : 0f;

        if (frontAxis == WheelbarrowFront.Auto)
        {
            // Vorne ist die Richtung von den Griffen zum Rad (waagerecht, im Raum der Karre).
            Vector3 toWheel = transform.InverseTransformPoint(wheelPoint.position)
                            - transform.InverseTransformPoint(handlePoint.position);

            if (new Vector2(toWheel.x, toWheel.z).sqrMagnitude > 0.0001f)
                frontYaw = Mathf.Atan2(toWheel.x, toWheel.z) * Mathf.Rad2Deg;
        }

        // Feinkorrektur: positiv = nach rechts drehen.
        frontYaw -= extraYaw;

        frontToModel = Quaternion.Euler(0f, frontYaw, 0f);

        // Die festen Collider der Karre fuer die Hindernis-Pruefung vormerken.
        bool hasBounds = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        Quaternion inverse = Quaternion.Inverse(transform.rotation);

        foreach (Collider col in GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger)
                continue;

            MeshCollider mesh = col as MeshCollider;

            if (mesh != null && !mesh.convex)
                continue;

            BodyCollider bc = new BodyCollider();
            bc.col = col;
            bc.rel = inverse * (col.transform.position - transform.position);
            bc.relRot = inverse * col.transform.rotation;
            bodyCols.Add(bc);

            if (!hasBounds)
            {
                bounds = col.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        boundCenterLocal = inverse * (bounds.center - transform.position);
        boundRadius = bounds.extents.magnitude;

        // Lage der Punkte relativ zur Karre merken (die Karre steht im Editor in Ruhelage).
        localHandle = transform.InverseTransformPoint(handlePoint.position);
        localWheel = transform.InverseTransformPoint(wheelPoint.position);
    }

    // Der Spieler greift die Griffe.
    public void BeginPush(float playerYaw, CharacterController player = null)
    {
        pushed = true;

        // Der Spieler darf nicht an der eigenen Karre haengen bleiben.
        pusherBody = player;

        if (pusherBody != null)
        {
            foreach (BodyCollider bc in bodyCols)
                Physics.IgnoreCollision(bc.col, pusherBody, true);
        }

        // Die Karre startet mit der Blickrichtung des Spielers (sie springt in die Haende).
        yaw = playerYaw;
        pitch = 0f;

        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.isKinematic = true;

        targetPos = body.position;
        targetRot = body.rotation;
        lastWheelPos = wheelPoint.position;
    }

    // Der Spieler laesst die Griffe los: Die Karre steht wieder von allein.
    public void EndPush()
    {
        pushed = false;

        if (pusherBody != null)
        {
            foreach (BodyCollider bc in bodyCols)
            {
                if (bc.col != null)
                    Physics.IgnoreCollision(bc.col, pusherBody, false);
            }

            pusherBody = null;
        }

        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    // Wird jedes Bild vom Spieler aufgerufen, solange er schiebt.
    //  feet: Position der Fuesse, playerYaw: Blickrichtung (Grad).
    //  Rueckgabe: Verschiebung (waagerecht), um die der Spieler zurueckgesetzt werden soll,
    //  weil die Karre an einer Wand steht. Der Spieler wendet sie auf sich an.
    public Vector3 PushUpdate(Vector3 feet, float playerYaw, float dt)
    {
        if (!pushed)
            return Vector3.zero;

        // Die Karre folgt dem Blick traege (so lenkt man sie).
        yaw = Mathf.MoveTowardsAngle(yaw, playerYaw, turnSpeed * dt);
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);

        // Hier halten die Haende die Griffe.
        Vector3 forward = Quaternion.Euler(0f, playerYaw, 0f) * Vector3.forward;
        Vector3 handleWorld = feet + forward * gripDistance + Vector3.up * gripHeight;

        Vector3 scale = transform.lossyScale;
        Vector3 handleLocal = Vector3.Scale(localHandle, scale);
        Vector3 wheelLocal = Vector3.Scale(localWheel, scale);

        // Dieselben Punkte im Raster "vorne = Z" (unabhaengig von den Achsen des Modells).
        Quaternion modelToFront = Quaternion.Inverse(frontToModel);
        Vector3 handleF = modelToFront * handleLocal;
        Vector3 wheelF = modelToFront * wheelLocal;

        // Wo steht das Rad (waagerecht)? Dort wird die Bodenhoehe gemessen.
        Vector3 wheelXZ = handleWorld + yawRot * (wheelF - handleF);
        float groundY = SampleGround(wheelXZ, feet.y);

        // Neigung: Die Griffe sind in Handhoehe, das Rad steht auf dem Boden.
        // Gesucht ist der Winkel, bei dem der Hoehenunterschied zwischen Griff und Rad passt.
        float a = handleF.y - wheelF.y;               // Griff ueber Rad (in Ruhelage)
        float b = wheelF.z - handleF.z;               // Rad vor Griff
        float d = handleWorld.y - groundY;            // gewuenschter Hoehenunterschied
        float r = Mathf.Sqrt(a * a + b * b);

        float wanted = 0f;

        if (r > 0.01f)
        {
            float c = Mathf.Clamp(d / r, -1f, 1f);
            float phi = Mathf.Atan2(b, a);
            float acos = Mathf.Acos(c);

            float t1 = phi + acos;
            float t2 = phi - acos;

            wanted = Mathf.Abs(t1) < Mathf.Abs(t2) ? t1 : t2;
        }

        wanted = Mathf.Clamp(wanted * Mathf.Rad2Deg, -maxPitch, maxPitch);

        // Sanft nachfuehren, damit Huckel nicht ruckeln.
        float k = 1f - Mathf.Exp(-14f * dt);
        pitch = Mathf.Lerp(pitch, wanted, k);

        // Erst das Modell mit "vorne = Z" ausrichten, dann neigen und in Blickrichtung drehen.
        Quaternion rot = yawRot * Quaternion.Euler(pitch, 0f, 0f) * modelToFront;

        targetRot = rot;
        targetPos = handleWorld - rot * handleLocal;

        return ResolveWalls();
    }

    // Prueft, ob die Karre an der Zielposition in einer Wand steckt, schiebt sie heraus
    // und gibt die Verschiebung zurueck (damit der Spieler mit zurueckweicht).
    private Vector3 ResolveWalls()
    {
        if (!blockedByWalls || bodyCols.Count == 0)
            return Vector3.zero;

        Vector3 total = Vector3.zero;

        for (int iteration = 0; iteration < 2; iteration++)
        {
            Vector3 center = targetPos + targetRot * boundCenterLocal;
            int n = Physics.OverlapSphereNonAlloc(center, boundRadius + 0.1f, overlapBuffer,
                ~0, QueryTriggerInteraction.Ignore);

            Vector3 best = Vector3.zero;
            float bestLength = 0f;

            for (int i = 0; i < n; i++)
            {
                Collider other = overlapBuffer[i];

                if (!IsObstacle(other))
                    continue;

                foreach (BodyCollider bc in bodyCols)
                {
                    Vector3 pos = targetPos + targetRot * bc.rel;
                    Quaternion rotation = targetRot * bc.relRot;

                    Vector3 direction;
                    float distance;

                    if (!Physics.ComputePenetration(bc.col, pos, rotation,
                        other, other.transform.position, other.transform.rotation,
                        out direction, out distance))
                        continue;

                    // Boden und Decke zaehlen nicht, nur seitliche Hindernisse.
                    if (Mathf.Abs(direction.y) > 0.6f)
                        continue;

                    Vector3 push = direction * distance;
                    push.y = 0f;

                    if (push.magnitude > bestLength)
                    {
                        bestLength = push.magnitude;
                        best = push;
                    }
                }
            }

            if (bestLength < 0.002f)
                break;

            best += best.normalized * blockMargin;

            targetPos += best;
            total += best;
        }

        // Nie weiter als einen halben Meter pro Bild (z. B. wenn die Karre beim Greifen
        // schon in etwas steckt), sonst wuerde der Spieler weggeschleudert.
        return Vector3.ClampMagnitude(total, 0.5f);
    }

    // Ist dieser Collider ein Hindernis fuer die Karre?
    private bool IsObstacle(Collider other)
    {
        if (other.transform.IsChildOf(transform))
            return false;

        if (other.GetComponentInParent<CharacterController>() != null)
            return false;

        // Das Gelaende ist der Boden, kein Hindernis.
        VoxelWorld world = VoxelWorld.Instance;

        if (world != null && world.OwnsCollider(other))
            return false;

        // Lose, bewegliche Gegenstaende (Kohle, Barren ...) werden einfach weggeschoben.
        if (other.attachedRigidbody != null && !other.attachedRigidbody.isKinematic)
            return false;

        return true;
    }

    private void FixedUpdate()
    {
        // Eine bewegte kinematische Karre nimmt lose Gegenstaende in der Mulde mit.
        if (pushed)
        {
            body.MovePosition(targetPos);
            body.MoveRotation(targetRot);
        }
    }

    private void Update()
    {
        UpdateWheel();

        if (Time.time >= nextCargoCheck)
        {
            nextCargoCheck = Time.time + 0.25f;
            UpdateCargo();
        }
    }

    // Das Rad dreht sich mit der gefahrenen Strecke.
    private void UpdateWheel()
    {
        Vector3 now = wheelPoint.position;
        Vector3 delta = now - lastWheelPos;
        lastWheelPos = now;

        if (wheel == null || wheelRadius <= 0f)
            return;

        Vector3 frontWorld = transform.TransformDirection(frontToModel * Vector3.forward);
        float along = Vector3.Dot(delta, frontWorld);
        float degrees = along / wheelRadius * Mathf.Rad2Deg;

        wheel.Rotate(wheelAxis, degrees, Space.Self);
    }

    // Misst die Ladung (kg) in der Mulde.
    private void UpdateCargo()
    {
        cargoMass = 0f;

        if (cargoZone == null)
            return;

        Transform t = cargoZone.transform;
        Vector3 center = t.TransformPoint(cargoZone.center);
        Vector3 half = Vector3.Scale(cargoZone.size * 0.5f, t.lossyScale);

        int n = Physics.OverlapBoxNonAlloc(center, half, overlapBuffer, t.rotation,
            ~0, QueryTriggerInteraction.Ignore);

        counted.Clear();

        for (int i = 0; i < n; i++)
        {
            Rigidbody rb = overlapBuffer[i].attachedRigidbody;

            if (rb == null || rb == body || !counted.Add(rb))
                continue;

            ItemTag tag = rb.GetComponent<ItemTag>();
            cargoMass += (tag != null && tag.massKg > 0f) ? tag.massKg : rb.mass;
        }
    }

    // Bodenhoehe unter einem Punkt (ohne Karre, Spieler und lose Dinge).
    private float SampleGround(Vector3 point, float fallback)
    {
        Vector3 origin = new Vector3(point.x, Mathf.Max(fallback, point.y) + 1.5f, point.z);

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 5f, ~0,
            QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        float bestY = fallback;

        foreach (RaycastHit hit in hits)
        {
            Collider col = hit.collider;

            if (col.transform.IsChildOf(transform))
                continue;

            if (col.GetComponentInParent<CharacterController>() != null)
                continue;

            // Lose, bewegliche Gegenstaende zaehlen nicht als Boden.
            if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic)
                continue;

            if (hit.distance < best)
            {
                best = hit.distance;
                bestY = hit.point.y;
            }
        }

        return bestY;
    }
}
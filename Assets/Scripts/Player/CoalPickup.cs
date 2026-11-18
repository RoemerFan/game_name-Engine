using UnityEngine;

// Aufheben und Ablegen von Gegenstaenden (Kohle, Erz, Barren, Kanister ...).
// Bedienung: E (Tastatur) bzw. L2/LT (Controller) hebt auf und laesst fallen (mit leichtem Schwung).
// R (Tastatur) bzw. R1/RB (Controller) legt den Gegenstand SANFT dorthin, wohin man zielt
// (auf den Boden, einen Tisch, in eine Schubkarre) - ohne Schwung und aufrecht.
// Q (Tastatur) bzw. L1/LB (Controller) laesst den Gegenstand genau dort los, wo er in der Hand
// schwebt - ohne jede Kraft, er bleibt einfach liegen bzw. faellt senkrecht herunter.
public class CoalPickup : MonoBehaviour
{
    [Header("Referenzen")]
    [SerializeField] private Camera playerCamera;

    [Header("Halten")]
    [SerializeField] private float pickupDistance = 4f;
    [SerializeField] private float holdDistance = 1.2f;
    [SerializeField] private float holdHeight = -0.15f;

    [Header("Loslassen")]
    [SerializeField] private float dropDistance = 1.5f;
    [SerializeField] private float dropHeight = 0.2f;
    [SerializeField] private float dropForce = 1f;
    [SerializeField] private float dropWallMargin = 0.25f;

    [Header("Sanft ablegen")]
    [SerializeField] private KeyCode placeKey = KeyCode.R;
    [Tooltip("Loslassen an Ort und Stelle (ohne Schwung).")]
    [SerializeField] private KeyCode releaseKey = KeyCode.Q;
    [Tooltip("Kleiner Abstand zur Flaeche beim Ablegen (Meter), damit nichts im Boden steckt.")]
    [SerializeField] private float placeClearance = 0.02f;

    private GameObject heldCoal;
    private CollectibleCoal heldItem;   // enthaelt die Einstellungen fuer Haltewinkel und -position
    private Rigidbody heldRigidbody;
    private Collider[] heldColliders;

    // Mehrspieler: Auf die Bestaetigung des Hosts warten, dass wir den Gegenstand tragen duerfen.
    private NetItem pendingGrab;
    private float pendingTimer;

    private void Update()
    {
        // Nicht waehrend eines Menues (z. B. beim Tippen eines Namens).
        if (SettingsMenu.IsOpen)
            return;

        if (pendingGrab != null)
        {
            pendingTimer -= Time.deltaTime;

            if (pendingGrab.HeldByMe && heldCoal == null)
            {
                BeginHold(pendingGrab.GetComponent<CollectibleCoal>());
                pendingGrab = null;
            }
            else if (pendingTimer <= 0f)
            {
                pendingGrab = null; // abgelehnt oder zu lange gewartet
            }
        }

        // Sanft ablegen (R / R1): dorthin, wohin man zielt, ohne Schwung.
        if (heldCoal != null && GameInput.Pressed(GameAction.Place, placeKey))
        {
            DropCoal(true);
            return;
        }

        // Loslassen an Ort und Stelle (Q / L1): ohne Kraft, genau dort, wo er schwebt.
        if (heldCoal != null && GameInput.Pressed(GameAction.Release, releaseKey))
        {
            DropCoal(false, true);
            return;
        }

        // Ueber GameInput: E auf der Tastatur, L2/LT am Controller.
        if (!GameInput.Pressed(GameAction.Pickup, KeyCode.E))
            return;

        if (heldCoal == null)
        {
            if (pendingGrab == null)
                TryPickupCoal();
        }
        else
        {
            DropCoal();
        }
    }

    private void LateUpdate()
    {
        if (heldCoal == null || playerCamera == null)
            return;

        Transform cam = playerCamera.transform;

        heldCoal.transform.SetPositionAndRotation(
            HoldPosition(cam, heldItem),
            HoldRotation(cam, heldItem)
        );
    }

    // Position in der Hand: vor der Kamera plus die Verschiebung des Gegenstands ("Hold Offset").
    private Vector3 HoldPosition(Transform cam, CollectibleCoal item)
    {
        Vector3 offset = item != null ? item.holdOffset : Vector3.zero;

        return cam.TransformPoint(new Vector3(0f, holdHeight, holdDistance) + offset);
    }

    // Drehung in der Hand: Blickrichtung (oder nur die Links/Rechts-Drehung) plus "Hold Euler".
    private Quaternion HoldRotation(Transform cam, CollectibleCoal item)
    {
        if (item == null)
            return cam.rotation;

        Quaternion baseRotation = item.holdFollowsPitch
            ? cam.rotation
            : Quaternion.Euler(0f, cam.eulerAngles.y, 0f);

        return baseRotation * Quaternion.Euler(item.holdEuler);
    }

    private void TryPickupCoal()
    {
        if (playerCamera == null)
        {
            Debug.LogError("Player Camera ist nicht zugewiesen!", this);
            return;
        }

        Ray ray = playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        if (!Physics.Raycast(ray, out RaycastHit hit, pickupDistance,
            ~0, QueryTriggerInteraction.Ignore))
            return;

        CollectibleCoal coal =
            hit.collider.GetComponentInParent<CollectibleCoal>();

        if (coal == null)
            return;

        // Mehrspieler: Der Host entscheidet, wer den Gegenstand bekommt (kein doppeltes Aufheben).
        NetItem net = coal.GetComponent<NetItem>();

        if (net != null && NetSync.IsOnline)
        {
            // Hat ihn schon jemand anderes in der Hand?
            if (net.HeldBy >= 0 && net.HeldBy != NetSync.LocalId)
                return;

            if (!NetItems.BeginGrab(net))
            {
                pendingGrab = net;
                pendingTimer = 1.5f;
                return;
            }
        }

        BeginHold(coal);
    }

    // Nimmt den Gegenstand in die Hand (erst nach der Freigabe vom Host).
    private void BeginHold(CollectibleCoal coal)
    {
        if (coal == null || playerCamera == null)
            return;

        Rigidbody rb = coal.GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError("Kohle hat keinen Rigidbody!", coal);
            return;
        }

        heldCoal = coal.gameObject;
        heldItem = coal;
        heldRigidbody = rb;

        // Erst Bewegung stoppen, dann kinematisch machen.
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = false;
        rb.isKinematic = true;

        // Collider ausschalten, damit die Kohle nichts wegschiebt
        // und keine Raycasts blockiert.
        heldColliders = heldCoal.GetComponentsInChildren<Collider>();
        foreach (Collider col in heldColliders)
            col.enabled = false;

        Transform cam = playerCamera.transform;
        heldCoal.transform.SetPositionAndRotation(
            HoldPosition(cam, heldItem),
            HoldRotation(cam, heldItem)
        );
    }

    // gentle = true: sanft dorthin legen, wohin man zielt (ohne Schwung).
    // inPlace = true: genau dort loslassen, wo er in der Hand schwebt, ohne Schwung.
    private void DropCoal(bool gentle = false, bool inPlace = false)
    {
        if (heldCoal == null || playerCamera == null)
            return;

        GameObject coal = heldCoal;
        CollectibleCoal item = heldItem;
        Rigidbody rb = heldRigidbody;
        Collider[] colliders = heldColliders;

        heldCoal = null;
        heldItem = null;
        heldRigidbody = null;
        heldColliders = null;

        Transform cam = playerCamera.transform;

        if (gentle)
        {
            PlaceGently(coal, item, rb, colliders, cam);
            return;
        }

        if (inPlace)
        {
            ReleaseInPlace(coal, rb, colliders, cam);
            return;
        }

        // Zielposition prüfen, damit die Kohle nicht in Boden/Wand landet.
        Vector3 origin = cam.position;
        Vector3 target = cam.TransformPoint(new Vector3(0f, dropHeight, dropDistance));

        if (Physics.Linecast(origin, target, out RaycastHit hit,
            ~0, QueryTriggerInteraction.Ignore))
        {
            target = hit.point + hit.normal * dropWallMargin;
        }

        // Beim Ablegen die gleiche Ausrichtung wie in der Hand (eine Leiter bleibt aufrecht).
        coal.transform.SetPositionAndRotation(target, HoldRotation(cam, item));

        foreach (Collider col in colliders)
            col.enabled = true;

        // Erst Physik aktivieren, danach Geschwindigkeit setzen.
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.AddForce(cam.forward * dropForce, ForceMode.Impulse);

        // Mehrspieler: Wir rechnen die Physik weiter und melden, dass er losgelassen wurde.
        NetItem net = coal.GetComponent<NetItem>();

        if (net != null)
            NetItems.Release(net);
    }

    // Legt den Gegenstand dort ab, wohin das Fadenkreuz zeigt: Er liegt mit seiner Unterseite
    // auf der getroffenen Flaeche (Boden, Tisch, Mulde einer Schubkarre) und bekommt keinen Schwung.
    private void PlaceGently(GameObject coal, CollectibleCoal item, Rigidbody rb,
                             Collider[] colliders, Transform cam)
    {
        // Ausrichtung wie in der Hand (eine Leiter bleibt aufrecht).
        Quaternion rotation = HoldRotation(cam, item);
        coal.transform.rotation = rotation;

        // Die gehaltenen Collider sind aus: So blockiert der Gegenstand den Strahl nicht.
        Ray ray = new Ray(cam.position, cam.forward);
        Vector3 surfacePoint;
        Vector3 normal = Vector3.up;

        if (Physics.Raycast(ray, out RaycastHit hit, pickupDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            surfacePoint = hit.point;
            normal = hit.normal;
        }
        else
        {
            // Nichts getroffen: ein Stueck vor dem Spieler auf den Boden legen.
            Vector3 ahead = cam.position + cam.forward * dropDistance;

            if (Physics.Raycast(ahead + Vector3.up, Vector3.down, out RaycastHit ground, 4f,
                ~0, QueryTriggerInteraction.Ignore))
            {
                surfacePoint = ground.point;
                normal = ground.normal;
            }
            else
            {
                surfacePoint = ahead;
            }
        }

        // Collider an, damit die Groesse des Gegenstands gemessen werden kann.
        foreach (Collider col in colliders)
            col.enabled = true;

        Bounds bounds = new Bounds(coal.transform.position, Vector3.zero);
        bool has = false;

        foreach (Collider col in colliders)
        {
            if (col.isTrigger)
                continue;

            if (!has)
            {
                bounds = col.bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        // So weit ueber die Flaeche heben, wie der Gegenstand in Richtung der Flaeche reicht.
        Vector3 absNormal = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
        float reach = has ? Vector3.Dot(absNormal, bounds.extents) : 0f;

        // Mitte der Begrenzung auf den Zielpunkt, Pivot entsprechend mitverschieben.
        Vector3 centerOffset = has ? bounds.center - coal.transform.position : Vector3.zero;
        Vector3 center = surfacePoint + normal * (reach + placeClearance);
        coal.transform.position = center - centerOffset;

        // Physik an, ohne Schwung: Er setzt sich nur noch kurz.
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        NetItem net = coal.GetComponent<NetItem>();

        if (net != null)
            NetItems.Release(net);
    }

    // Laesst den Gegenstand genau an der Stelle los, an der er gerade schwebt: keine Kraft,
    // keine Bewegung. Nur wenn er dabei in einer Wand/im Boden stecken wuerde, wird er
    // ein Stueck davor gesetzt.
    private void ReleaseInPlace(GameObject coal, Rigidbody rb, Collider[] colliders, Transform cam)
    {
        Vector3 position = coal.transform.position;

        if (Physics.Linecast(cam.position, position, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            position = hit.point + hit.normal * dropWallMargin;

        coal.transform.position = position;

        foreach (Collider col in colliders)
            col.enabled = true;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        NetItem net = coal.GetComponent<NetItem>();

        if (net != null)
            NetItems.Release(net);
    }
}
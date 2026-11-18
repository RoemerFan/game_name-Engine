using UnityEngine;

// =====================================================================
//  TrainLever: ein Hebel (oder Zugseil) im Fuehrerstand, mit dem man die Lok bedienen kann.
//  OPTIONAL: Die Tasten (R/F/V/B/H) funktionieren weiter. Hebel und Tasten lassen sich mischen.
//
//  Einrichten: ein Objekt am Hebel-Ort mit einem BOX COLLIDER (Is Trigger) rund um den Hebel und diesem Skript.
//  Bei "Kind" waehlen, was der Hebel tut. Die Lok wird automatisch im Elternobjekt gefunden.
//  Der Hebelgriff (Feld "Handle") kippt um seinen Ursprung (Pivot) und zeigt den Zustand der Lok an,
//  auch bei Mitspielern.
//
//  Bedienen: mit dem Fadenkreuz auf den Hebel zeigen, dann
//    REGLER:        linke Maustaste gehalten = hoeher, rechte Maustaste gehalten = niedriger. Er behaelt seine Stellung.
//    FAHRTRICHTUNG: linke Maustaste = umlegen (nur im Stand).
//    BREMSE:        linke Maustaste gehalten = bremsen. Beim Loslassen federt der Hebel zurueck.
//    PFEIFE:        linke Maustaste = ziehen.
//
//  Im Mehrspieler geht die Bedienung wie bei der Tastatur an den Host.
// =====================================================================
public class TrainLever : MonoBehaviour
{
    public enum Kind
    {
        Throttle,   // Regler
        Reverser,   // Fahrtrichtung
        Brake,      // Bremse
        Horn        // Pfeife
    }

    [Header("Funktion")]
    public Kind kind = Kind.Throttle;

    [Tooltip("Die Lok (leer = im Elternobjekt gesucht).")]
    public Locomotive loco;

    [Header("Hebel-Modell (optional)")]
    [Tooltip("Der bewegliche Griff. Ursprung (Pivot) = Drehpunkt des Hebels.")]
    public Transform handle;

    [Tooltip("Um diese Achse kippt der Griff (im Raum des Griffs).")]
    public Vector3 handleAxis = Vector3.right;

    [Tooltip("Winkel bei Stellung 'aus' (Regler 0 %, Richtung rueckwaerts, Bremse offen, Pfeife in Ruhe).")]
    public float minAngle = -35f;

    [Tooltip("Winkel bei Stellung 'an' (Regler 100 %, Richtung vorwaerts, Bremse gezogen, Pfeife gezogen).")]
    public float maxAngle = 35f;

    [Tooltip("Wie schnell der Griff der Stellung folgt (Grad pro Sekunde).")]
    public float handleSpeed = 200f;

    [Header("Bedienung")]
    [Tooltip("Die Kamera des Spielers. Leer = die Kamera vom TrackManager (Player Camera), sonst 'MainCamera'.")]
    public Camera playerCamera;

    public float reach = 3.5f;

    [Tooltip("Feste Flaechen vor dem Hebel verdecken ihn, ausser sie sind nur so nah davor (Meter).")]
    public float tolerance = 0.3f;

    private Quaternion handleBase = Quaternion.identity;
    private float angle;
    private bool aimed;
    private float hornPull;

    void Reset()
    {
        loco = GetComponentInParent<Locomotive>();

        Collider c = GetComponent<Collider>();

        if (c != null)
            c.isTrigger = true;
    }

    void Awake()
    {
        if (loco == null)
            loco = GetComponentInParent<Locomotive>();

        if (handle != null)
            handleBase = handle.localRotation;

        // Der Griff steht zunaechst in der Stellung der Lok (kein "Hinschwingen" beim Start).
        angle = TargetAngle();
        ApplyHandle();
    }

    void Update()
    {
        aimed = false;

        if (loco != null && Cursor.lockState == CursorLockMode.Locked && !SettingsMenu.IsOpen)
        {
            aimed = IsAimed();

            if (aimed)
                Operate();
        }

        // Pfeife: der Zug-Griff faellt nach dem Ziehen zurueck.
        hornPull = Mathf.MoveTowards(hornPull, 0f, Time.deltaTime * 3f);

        if (handle != null)
        {
            angle = Mathf.MoveTowards(angle, TargetAngle(), handleSpeed * Time.deltaTime);
            ApplyHandle();
        }
    }

    private void Operate()
    {
        float dt = Time.deltaTime;

        switch (kind)
        {
            case Kind.Throttle:
                if (GameInput.Held(GameAction.Primary))
                    loco.LeverThrottle(1f, dt);

                if (GameInput.Held(GameAction.Secondary))
                    loco.LeverThrottle(-1f, dt);

                break;

            case Kind.Reverser:
                if (GameInput.Pressed(GameAction.Primary))
                    loco.LeverReverser();

                break;

            case Kind.Brake:
                if (GameInput.Held(GameAction.Primary))
                    loco.LeverBrake();

                break;

            case Kind.Horn:
                if (GameInput.Pressed(GameAction.Primary))
                {
                    loco.LeverHorn();
                    hornPull = 1f;
                }

                break;
        }
    }

    // Wo der Griff stehen soll, je nach Zustand der Lok.
    private float TargetAngle()
    {
        if (loco == null)
            return minAngle;

        float t;

        switch (kind)
        {
            case Kind.Throttle:
                t = loco.throttle;
                break;

            case Kind.Reverser:
                t = loco.reverser >= 0 ? 1f : 0f;
                break;

            case Kind.Brake:
                t = loco.braking ? 1f : 0f;
                break;

            default:
                t = hornPull;
                break;
        }

        return Mathf.Lerp(minAngle, maxAngle, t);
    }

    private void ApplyHandle()
    {
        if (handle == null)
            return;

        Vector3 axis = handleAxis.sqrMagnitude > 0.0001f ? handleAxis.normalized : Vector3.right;
        handle.localRotation = handleBase * Quaternion.AngleAxis(angle, axis);
    }

    // Zeigt das Fadenkreuz auf diesen Hebel?
    private bool IsAimed()
    {
        if (playerCamera == null)
            playerCamera = TrackManager.Instance != null ? TrackManager.Instance.PlayerCamera : Camera.main;

        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera == null)
            return false;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, reach, ~0, QueryTriggerInteraction.Collide);

        float leverDistance = float.MaxValue;
        float solidDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.GetComponentInParent<TrainLever>() == this)
            {
                leverDistance = Mathf.Min(leverDistance, hit.distance);
            }
            else if (!hit.collider.isTrigger && hit.collider.GetComponentInParent<CharacterController>() == null)
            {
                solidDistance = Mathf.Min(solidDistance, hit.distance);
            }
        }

        return leverDistance < float.MaxValue && leverDistance <= solidDistance + tolerance;
    }

    void OnGUI()
    {
        if (!aimed || loco == null || SettingsMenu.IsOpen)
            return;

        string text;

        switch (kind)
        {
            case Kind.Throttle:
                text = Loc.T("lever.throttle", Mathf.RoundToInt(loco.throttle * 100f));
                break;

            case Kind.Reverser:
                text = Loc.T("lever.reverser", Loc.T(loco.reverser >= 0 ? "train.dir.fwd" : "train.dir.back"));
                break;

            case Kind.Brake:
                text = Loc.T("lever.brake");
                break;

            default:
                text = Loc.T("lever.horn");
                break;
        }

        GameSettings.DrawBoxBelowCenter(GameInput.ReplaceTokens(text));
    }
}
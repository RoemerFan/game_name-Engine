using UnityEngine;

// =====================================================================
//  BoomLever: ein Hebel zum Steuern eines TelescopicBelt.
//
//  Einrichten: Leeres Objekt am Hebel-Ort, BOX COLLIDER (Is Trigger) rund um den Hebel,
//  dieses Skript dazu. Bei "Function" waehlen, was der Hebel steuert (Winkel oder Laenge).
//  Zwei Hebel anlegen: einen fuer "Angle", einen fuer "Extension".
//
//  Bedienen: Mit dem Fadenkreuz auf den Hebel zeigen und
//    Linke Maustaste GEHALTEN  = hoch bzw. ausfahren,
//    Rechte Maustaste GEHALTEN = runter bzw. einfahren.
//  Der Hebel kippt dabei und federt zurueck, sobald man loslaesst.
//
//  Im Mehrspieler wird die Bewegung als Kommando an alle geschickt.
//
//  Optional: "Handle" = das Modell des Hebelgriffs (kippt um "Handle Axis").
// =====================================================================
public class BoomLever : MonoBehaviour
{
    public enum Function
    {
        Angle,      // Neigung des Bandes
        Extension   // Laenge (Ausfahren)
    }

    [Header("Steuerung")]
    [Tooltip("Das Band, das gesteuert wird (leer = im Elternobjekt gesucht).")]
    public TelescopicBelt belt;
    public Function function = Function.Angle;

    [Header("Hebel-Modell (optional)")]
    [Tooltip("Der bewegliche Griff. Ursprung (Pivot) = Drehpunkt des Hebels.")]
    public Transform handle;
    [Tooltip("Um diese Achse kippt der Griff (im Raum des Griffs).")]
    public Vector3 handleAxis = Vector3.right;
    [Tooltip("Wie weit der Griff kippt (Grad).")]
    public float handleMaxAngle = 30f;
    [Tooltip("Wie schnell er zurueckfedert (Grad pro Sekunde).")]
    public float handleSpeed = 150f;

    [Header("Bedienung")]
    [Tooltip("Die Kamera des Spielers. Leer = die Kamera aus TrackManager > Player Camera, sonst die Kamera mit dem Tag 'MainCamera'. " +
             "(Ein Prefab kann keine Szenen-Kamera eintragen, deshalb gilt die Kamera vom TrackManager.)")]
    public Camera playerCamera;
    public float reach = 3.5f;
    [Tooltip("Feste Flaechen vor dem Hebel verdecken ihn, ausser sie sind nur so nah davor (Meter).")]
    public float tolerance = 0.3f;

    private Quaternion handleBase = Quaternion.identity;
    private float handleAngle;
    private bool aimed;
    private float pending;
    private float sendTimer;

    void Reset()
    {
        belt = GetComponentInParent<TelescopicBelt>();

        Collider c = GetComponent<Collider>();

        if (c != null)
            c.isTrigger = true;
    }

    void Awake()
    {
        if (belt == null)
            belt = GetComponentInParent<TelescopicBelt>();

        if (handle != null)
            handleBase = handle.localRotation;
    }

    void Update()
    {
        float input = 0f;
        aimed = false;

        if (belt != null && Cursor.lockState == CursorLockMode.Locked && !SettingsMenu.IsOpen)
        {
            aimed = IsAimed();

            if (aimed)
            {
                if (GameInput.Held(GameAction.Primary)) input += 1f;
                if (GameInput.Held(GameAction.Secondary)) input -= 1f;
            }
        }

        if (belt != null)
        {
            if (!NetSync.IsOnline)
            {
                if (input != 0f)
                {
                    if (function == Function.Angle)
                        belt.DriveAngle(input, Time.deltaTime);
                    else
                        belt.DriveExtension(input, Time.deltaTime);
                }
            }
            else
            {
                // Mehrspieler: Die Bewegung wird gesammelt und ca. alle 0,05 s als Kommando an
                // alle geschickt (Vorzeichen = Richtung, Betrag = Zeit in Sekunden).
                pending += input * Time.deltaTime;
                sendTimer -= Time.deltaTime;

                if (Mathf.Abs(pending) > 0.001f && (sendTimer <= 0f || input == 0f))
                {
                    NetSync.BoomDrive(belt, function == Function.Angle ? 0 : 1, pending);
                    pending = 0f;
                    sendTimer = 0.05f;
                }
            }
        }

        // Hebel kippen und zurueckfedern.
        if (handle != null)
        {
            handleAngle = Mathf.MoveTowards(handleAngle, input * handleMaxAngle, handleSpeed * Time.deltaTime);
            handle.localRotation = handleBase * Quaternion.AngleAxis(handleAngle, handleAxis);
        }
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
            if (hit.collider.GetComponentInParent<BoomLever>() == this)
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
        if (!aimed || belt == null || SettingsMenu.IsOpen)
            return;

        string key = function == Function.Angle ? "boom.prompt.angle" : "boom.prompt.extend";
        string value = function == Function.Angle
            ? Mathf.RoundToInt(belt.Angle).ToString()
            : (belt.Extension).ToString("0.0");

        GameSettings.DrawBoxBelowCenter(GameInput.ReplaceTokens(Loc.T(key, value)));
    }
}
using UnityEngine;

// Auf den Spieler haengen. Mit dem Fadenkreuz auf einen Schalter oder eine
// Sicherung zeigen und die Taste druecken.
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [SerializeField] private float reachDistance = 4f;

    [Tooltip("Schalter duerfen so weit hinter einer festen Flaeche liegen " +
             "(Schalter sitzen oft in der Huelle der Maschine).")]
    [SerializeField] private float tolerance = 0.3f;

    [SerializeField] private Camera playerCamera;

    private IInteractable current;

    private void Start()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera == null)
        {
            Debug.LogError("PlayerInteractor: Kamera fehlt!", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            current = null;
            return;
        }

        current = FindInteractable();

        if (current != null && GameInput.Pressed(GameAction.Interact, interactKey))
            NetSync.Interact(current);
    }

    private IInteractable FindInteractable()
    {
        Ray ray = playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            reachDistance,
            ~0,
            QueryTriggerInteraction.Collide
        );

        System.Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance)
        );

        IInteractable found = null;
        float foundDistance = 0f;
        float solidDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            IInteractable target =
                hit.collider.GetComponentInParent<IInteractable>();

            if (target != null)
            {
                if (found == null)
                {
                    found = target;
                    foundDistance = hit.distance;
                }

                continue;
            }

            // Feste Flaechen ohne Schalter verdecken alles dahinter.
            if (!hit.collider.isTrigger && hit.distance < solidDistance)
                solidDistance = hit.distance;
        }

        if (found != null && foundDistance > solidDistance + tolerance)
            return null;

        return found;
    }

    private void OnGUI()
    {
        if (current == null || Cursor.lockState != CursorLockMode.Locked)
            return;

        GameSettings.DrawBoxBelowCenter("[" + GameInput.Hint(GameAction.Interact, interactKey) + "]  " + current.GetPrompt());
    }
}
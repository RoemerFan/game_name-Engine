using UnityEngine;

// =====================================================================
//  HoseNozzle: die Duese am Ende des Schlauchs (siehe HoseReel). Wird von der Schlauchtrommel selbst angelegt.
//
//  Bedienen:
//   - Duese anvisieren + Bedientaste: in die Hand nehmen (bzw. abziehen, wenn sie angeschlossen ist).
//   - In der Hand: auf einen Wasser-Einfuellstutzen zeigen + Bedientaste = anschliessen.
//     Ohne Stutzen im Blick: Bedientaste = ablegen.
//
//  Das Kommando schickt dieses Skript selbst (ISelfNetworked).
// =====================================================================
public class HoseNozzle : MonoBehaviour, IInteractable, ISelfNetworked
{
    [HideInInspector] public HoseReel reel;
    [HideInInspector] public Port port;
    [HideInInspector] public BoxCollider box;

    [Tooltip("So weit (Meter) kann man mit der Duese in der Hand einen Stutzen anvisieren.")]
    public float reach = 4.5f;

    [Tooltip("Feste Flaechen vor dem Stutzen verdecken ihn, ausser sie sind nur so nah davor (Meter).")]
    public float tolerance = 0.3f;

    private Port aimedPort;
    private string hint = "";

    // Hier haengt der Schlauch an der Duese (hinten).
    public Vector3 AttachPos
    {
        get { return transform.position - transform.forward * 0.13f; }
    }

    public string GetPrompt()
    {
        if (reel == null)
            return "";

        switch (reel.State)
        {
            case HoseReel.HoseState.Plugged:
                return Loc.T("hose.unplug");

            case HoseReel.HoseState.Carried:
                return "";

            default:
                return Loc.T("hose.pickup");
        }
    }

    public void Interact()
    {
        if (reel == null)
            return;

        if (reel.State == HoseReel.HoseState.Plugged)
        {
            // Abziehen und gleich in die Hand nehmen.
            reel.RequestUnplug();
            reel.RequestPick();
        }
        else if (reel.State == HoseReel.HoseState.Hanging || reel.State == HoseReel.HoseState.Ground)
        {
            reel.RequestPick();
        }
    }

    void Update()
    {
        aimedPort = null;
        hint = "";

        if (reel == null || !reel.CarrierIsLocal || SettingsMenu.IsOpen || Cursor.lockState != CursorLockMode.Locked)
            return;

        Camera cam = TrackManager.Instance != null ? TrackManager.Instance.PlayerCamera : null;

        if (cam == null)
            cam = Camera.main;

        if (cam == null)
            return;

        aimedPort = FindAimedPort(cam);

        string key = GameInput.Hint(GameAction.Interact, KeyCode.F);

        if (aimedPort != null)
            hint = Loc.T("hose.carry.plug", key);
        else
            hint = Loc.T("hose.carry.drop", key);

        // Im selben Bild wie das Aufnehmen nicht gleich wieder reagieren.
        if (Time.frameCount <= reel.PickFrame + 1)
            return;

        if (!GameInput.Pressed(GameAction.Interact, KeyCode.F))
            return;

        if (aimedPort != null)
            reel.RequestPlug(aimedPort);
        else
            reel.RequestDrop(DropPoint(cam));
    }

    // Auf welchen freien Wasser-Stutzen zeigt das Fadenkreuz?
    private Port FindAimedPort(Camera cam)
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, reach, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        Port found = null;
        float foundDistance = 0f;
        float solidDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            // Eigene Duese und eigenen Schlauch ignorieren.
            if (hit.collider.transform.IsChildOf(transform))
                continue;

            Port candidate = hit.collider.GetComponentInParent<Port>();

            if (candidate == null)
                candidate = hit.collider.GetComponent<Port>();

            if (candidate != null)
            {
                if (found == null && candidate.Medium == PortMedium.Water && port.CanConnect(candidate))
                {
                    found = candidate;
                    foundDistance = hit.distance;
                }

                continue;
            }

            if (!hit.collider.isTrigger && hit.distance < solidDistance)
                solidDistance = hit.distance;
        }

        if (found != null && foundDistance > solidDistance + tolerance)
            return null;

        return found;
    }

    // Wo die Duese beim Ablegen hinkommt: etwas vor dem Spieler auf den Boden.
    private Vector3 DropPoint(Camera cam)
    {
        Vector3 origin = cam.transform.position + cam.transform.forward * 1.6f + Vector3.up * 0.8f;
        RaycastHit hit;

        if (Physics.Raycast(origin, Vector3.down, out hit, 5f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point;

        return cam.transform.position + cam.transform.forward * 1.6f - Vector3.up * 1.4f;
    }

    void OnGUI()
    {
        if (string.IsNullOrEmpty(hint))
            return;

        GameSettings.DrawBoxBelowCenter(hint);
    }
}
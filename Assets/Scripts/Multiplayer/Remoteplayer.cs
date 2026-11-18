using UnityEngine;

// Die Spielfigur eines Mitspielers: eine einfache Kapsel mit "Nase" (zeigt
// die Blickrichtung) und dem Namen darueber. Wird vom NetSync angelegt und
// bewegt sich weich zu den empfangenen Positionen.
public class RemotePlayer : MonoBehaviour
{
    private static readonly Color[] colors =
    {
        new Color(0.95f, 0.45f, 0.25f),
        new Color(0.3f, 0.7f, 0.95f),
        new Color(0.45f, 0.85f, 0.4f),
        new Color(0.9f, 0.8f, 0.3f)
    };

    public int PlayerId { get; private set; }
    public string PlayerName { get; set; }

    [Tooltip("So nah (in Metern) wird der Name angezeigt.")]
    public float nameDistance = 40f;

    private Vector3 targetFeet;
    private float targetYaw;
    private bool hasTarget;

    public static RemotePlayer Create(int id, string playerName)
    {
        GameObject root = new GameObject("RemotePlayer_" + playerName);
        RemotePlayer player = root.AddComponent<RemotePlayer>();
        player.PlayerId = id;
        player.PlayerName = playerName;

        Color color = colors[Mathf.Abs(id) % colors.Length];

        // Koerper: Kapsel (Fuesse am Ursprung der Figur).
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
        Tint(body, color);

        // Nase: kleiner Klotz vorn auf Kopfhoehe.
        GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Nose";
        Destroy(nose.GetComponent<Collider>());
        nose.transform.SetParent(root.transform, false);
        nose.transform.localPosition = new Vector3(0f, 1.5f, 0.3f);
        nose.transform.localScale = new Vector3(0.18f, 0.18f, 0.3f);
        Tint(nose, Color.Lerp(color, Color.white, 0.5f));

        return player;
    }

    private static void Tint(GameObject go, Color color)
    {
        Renderer renderer = go.GetComponent<Renderer>();
        Shader shader = Shader.Find("Standard");

        if (renderer == null || shader == null)
            return;

        Material material = new Material(shader);
        material.color = color;
        renderer.sharedMaterial = material;
    }

    // Neue Position vom Netzwerk.
    public void SetTarget(Vector3 feet, float yaw)
    {
        targetFeet = feet;
        targetYaw = yaw;

        if (!hasTarget)
        {
            // Erstes Mal: direkt dorthin.
            hasTarget = true;
            transform.position = feet;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }

    private void Update()
    {
        if (!hasTarget)
            return;

        float t = 1f - Mathf.Exp(-12f * Time.deltaTime);

        transform.position = Vector3.Lerp(transform.position, targetFeet, t);
        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), t);
    }

    private void OnGUI()
    {
        if (!hasTarget || string.IsNullOrEmpty(PlayerName))
            return;

        Camera cam = Camera.main;

        if (cam == null)
            return;

        Vector3 world = transform.position + Vector3.up * 2.2f;

        if ((world - cam.transform.position).sqrMagnitude > nameDistance * nameDistance)
            return;

        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return;

        GameSettings.DrawBoxAbove(screen, PlayerName);
    }
}
using UnityEngine;

// Schild eines Dorfes: Von jeder Seite ist nur der Text sichtbar, der zur Kamera zeigt.
// (Schrift wird sonst durch das Brett hindurch gezeichnet und ueberlagert sich.)
public class VillageSign : MonoBehaviour
{
    public GameObject front;   // Text auf der +X-Seite des Schildes
    public GameObject back;    // Text auf der -X-Seite

    private Camera cam;

    // Hauptkamera, sonst die Kamera am Spieler, sonst irgendeine aktive.
    private static Camera FindCamera()
    {
        Camera c = Camera.main;

        if (c != null)
            return c;

        VoxelWorld world = VoxelWorld.Instance;

        if (world != null && world.player != null)
            c = world.player.GetComponentInChildren<Camera>();

        if (c != null && c.isActiveAndEnabled)
            return c;

        Camera[] all = Camera.allCameras;

        return all.Length > 0 ? all[0] : null;
    }

    void LateUpdate()
    {
        if (cam == null || !cam.isActiveAndEnabled)
            cam = FindCamera();

        if (front == null || back == null)
            return;

        // Ohne Kamera wenigstens eine Seite zeigen.
        if (cam == null)
        {
            front.SetActive(true);
            back.SetActive(false);
            return;
        }

        // Auf welcher Seite des Schildes steht die Kamera?
        Vector3 toCamera = cam.transform.position - transform.position;
        bool frontSide = Vector3.Dot(toCamera, transform.right) >= 0f;

        if (front.activeSelf != frontSide)
            front.SetActive(frontSide);

        if (back.activeSelf == frontSide)
            back.SetActive(!frontSide);
    }
}
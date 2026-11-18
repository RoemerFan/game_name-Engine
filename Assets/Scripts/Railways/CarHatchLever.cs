using UnityEngine;

// =====================================================================
//  CarHatchLever: der Hebel fuer die Bodenklappe eines Wagens (siehe Wagon).
//
//  Bedienen: Fadenkreuz auf den Hebel, Bedientaste. Das Kommando schickt dieses Skript selbst
//  (ISelfNetworked), damit die Klappe bei allen Spielern gleichzeitig auf- und zugeht.
//
//  Einrichten: Kind-Objekt am Wagen mit Trigger-Collider und diesem Skript. Optional "Handle":
//  ein Objekt, das kippt, wenn die Klappe offen ist (reine Optik).
// =====================================================================
public class CarHatchLever : MonoBehaviour, IInteractable, ISelfNetworked
{
    [Tooltip("Der Wagen (leer = wird im Eltern-Objekt gesucht).")]
    public Wagon wagon;

    [Tooltip("Optional: Griff, der beim Oeffnen kippt (Drehung um die lokale X-Achse).")]
    public Transform handle;

    [Tooltip("Kippwinkel des Griffs bei offener Klappe (Grad).")]
    public float openTilt = 50f;

    private Quaternion baseRotation = Quaternion.identity;

    void Awake()
    {
        if (wagon == null)
            wagon = GetComponentInParent<Wagon>();

        if (handle != null)
            baseRotation = handle.localRotation;
    }

    void Update()
    {
        if (handle == null || wagon == null)
            return;

        Quaternion target = baseRotation * Quaternion.Euler(wagon.hatchOpen ? openTilt : 0f, 0f, 0f);
        handle.localRotation = Quaternion.RotateTowards(handle.localRotation, target, 300f * Time.deltaTime);
    }

    public string GetPrompt()
    {
        if (wagon == null)
            return "";

        return Loc.T(wagon.hatchOpen ? "wagon.hatch.close" : "wagon.hatch.open");
    }

    public void Interact()
    {
        if (wagon != null && wagon.NetId > 0)
            NetSync.Submit(NetCommand.CarFunction(wagon.NetId, 0));
    }
}
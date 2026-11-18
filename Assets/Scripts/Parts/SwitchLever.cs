using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =====================================================================
//  SwitchLever: der Hebel einer Weiche. Wird vom TrackManager automatisch neben jeder Weiche
//  aufgestellt (Gleis an ein bestehendes Gleis anschliessen = Weiche, oder das Parallelgleis-Werkzeug).
//
//  Bedienen: Mit dem Fadenkreuz auf den Hebel zielen und F druecken (Taste im TrackManager).
//  Das Umlegen laeuft ueber den TrackManager als Kommando, damit es im Mehrspieler bei allen
//  gleichzeitig passiert.
//
//  EIGENER HEBEL: Prefab in "Switch Lever Prefab" beim TrackManager eintragen. Ursprung = Fuss.
//  Dieses Skript muss am Prefab NICHT haengen - es wird automatisch hinzugefuegt (man kann es
//  aber auch selbst an das Prefab haengen, um die Felder dort einzustellen).
//  Der Hebel kann auf zwei Arten animiert werden (wie beim MachineSwitch):
//   A) "Handle": EIN Objekt (ein Kind namens "Handle" wird automatisch gefunden). Es kippt um
//      seinen Ursprung (Pivot) um die lokale Z-Achse.
//   B) "Lever Parts" + "Hinge": MEHRERE Teile werden gemeinsam um einen Gelenkpunkt gedreht.
//      Die Teile muessen nirgendwohin verschoben werden. Funktioniert auch mit Teilen aus
//      importierten Modellen. "Hinge" ist ein leeres Objekt am Gelenk (Position = Drehpunkt,
//      Achse = "Hinge Axis" im Raum des Gelenks). Bei gewaehltem Objekt zeigt eine gelbe Linie
//      die Achse. Die Teile stehen im Editor in der Mittelstellung (Winkel 0).
// =====================================================================
public class SwitchLever : MonoBehaviour, IInteractable, ISelfNetworked
{
    // Der Punkt im Gleisnetz, zu dem die Weiche gehoert (vom TrackManager gesetzt).
    [HideInInspector] public int nodeId;

    [Header("A) Hebel als ein Objekt")]
    [Tooltip("Der bewegliche Griff (leer = es kippt nichts bzw. Variante B).")]
    public Transform handle;

    [Header("B) Hebel aus mehreren Teilen")]
    [Tooltip("Alle beweglichen Teile. Sie muessen nicht unter dem Gelenk liegen.")]
    public Transform[] leverParts;
    [Tooltip("Leeres Objekt am Gelenk. Die Position ist der Drehpunkt, die Achse kommt aus " +
             "'Hinge Axis'. Ohne Eintrag gilt dieses Objekt.")]
    public Transform hinge;
    [Tooltip("Drehachse im lokalen Raum des Gelenks (X, Y oder Z). Bei gewaehltem Objekt zeigt " +
             "eine gelbe Linie die Achse.")]
    public Vector3 hingeAxis = Vector3.right;
    [Tooltip("Dreht die Richtung um (falls der Hebel in die falsche Richtung kippt).")]
    public bool invert;

    [Header("Beide Varianten")]
    [Tooltip("Wie schnell der Hebel kippt (Grad pro Sekunde).")]
    public float tiltSpeed = 240f;

    private Quaternion baseRotation = Quaternion.identity;
    private float current;      // aktueller Winkel
    private float target;       // Zielwinkel
    private float applied;      // Winkel, der bei den Teilen schon angewendet ist (Variante B)
    private bool placed;

    private bool UseHinge
    {
        get { return leverParts != null && leverParts.Length > 0; }
    }

    private Transform HingeTransform
    {
        get { return hinge != null ? hinge : transform; }
    }

    // Bedienen wie jeden anderen Schalter: der PlayerInteractor zeigt den Text und reagiert auf die
    // Bedientaste (F). Das Umlegen selbst laeuft als Kommando ueber den TrackManager.
    public string GetPrompt()
    {
        return TrackManager.Instance != null ? TrackManager.Instance.SwitchPrompt(nodeId) : "";
    }

    public void Interact()
    {
        if (TrackManager.Instance != null)
            TrackManager.Instance.ToggleSwitch(nodeId);
    }

    // Merkt sich die Ausgangsstellung des Griffs.
    public void Init()
    {
        if (handle != null)
            baseRotation = handle.localRotation;
    }

    // Hebel auf diesen Winkel kippen (Grad).
    public void SetTilt(float degrees)
    {
        if (invert)
            degrees = -degrees;

        if (!placed)
        {
            // Erste Stellung sofort einnehmen, nicht erst "hinschwingen".
            current = degrees;
            placed = true;
        }

        target = degrees;
        Apply();
    }

    void Update()
    {
        if (Mathf.Approximately(current, target))
            return;

        current = Mathf.MoveTowards(current, target, tiltSpeed * Time.deltaTime);
        Apply();
    }

    private void Apply()
    {
        if (UseHinge)
        {
            RotateParts(current - applied);
            applied = current;
            return;
        }

        if (handle != null)
            handle.localRotation = baseRotation * Quaternion.Euler(0f, 0f, current);
    }

    private Vector3 HingeAxisWorld()
    {
        Vector3 axis = hingeAxis.sqrMagnitude > 0.0001f ? hingeAxis.normalized : Vector3.right;
        return HingeTransform.TransformDirection(axis).normalized;
    }

    // Dreht alle Teile um den Gelenkpunkt.
    private void RotateParts(float deltaDegrees)
    {
        if (Mathf.Approximately(deltaDegrees, 0f))
            return;

        Vector3 pivot = HingeTransform.position;
        Vector3 axis = HingeAxisWorld();

        foreach (Transform part in leverParts)
        {
            if (part != null)
                part.RotateAround(pivot, axis, deltaDegrees);
        }
    }

    // Zeigt im Editor die Drehachse (gelb), auch durch das Modell hindurch.
    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        if (!UseHinge)
            return;

        Vector3 center = HingeTransform.position;
        Vector3 axis = HingeAxisWorld();

        UnityEngine.Rendering.CompareFunction oldZTest = Handles.zTest;
        Color oldColor = Handles.color;

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = new Color(1f, 0.9f, 0.1f, 1f);

        Handles.DrawLine(center - axis * 0.25f, center + axis * 0.25f);
        Handles.DrawWireDisc(center, axis, 0.05f);

        Handles.zTest = oldZTest;
        Handles.color = oldColor;
#endif
    }
}
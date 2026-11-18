using UnityEngine;

// =====================================================================
//  TelescopicBelt: macht ein Foerderband (ConveyorBelt) NEIGBAR und AUSFAHRBAR.
//
//  Neben das ConveyorBelt-Skript an die Wurzel des Foerderband-Prefabs haengen.
//  Gesteuert wird es von zwei Hebeln (BoomLever): einer fuer den Winkel, einer fuer die Laenge.
//
//  Der "Arm" dreht sich um sein Gelenk (hoch / runter). Die "Sections" (Teleskopteile)
//  schieben sich beim Ausfahren in Bandrichtung heraus, und die Bandflaeche (Collider)
//  wird dabei laenger - das ConveyorBelt transportiert also immer genau auf der
//  aktuellen Laenge.
//
//  AUFBAU (Details und Skizze in der Anleitung):
//   Wurzel (ConveyorBelt + TelescopicBelt)
//    ├─ Sockel (fest)
//    └─ Arm  (leeres Objekt am GELENK, +Z = Bandrichtung)   <- "Arm"
//        ├─ Basisband mit Box Collider (Bandflaeche)         <- "Surface"
//        ├─ Teleskopteil 1                                   <- "Sections"
//        └─ Teleskopteil 2                                   <- "Sections"
//
//  Mehrspieler: Die Hebel schicken ihre Bewegung als Kommando (NetCommand.BoomDrive) an alle.
//  Die Stellung wird mit dem Maschinen-Abgleich (SaveState/LoadState) nachgezogen.
// =====================================================================
public class TelescopicBelt : MonoBehaviour, ISaveable
{
    [Header("Aufbau")]
    [Tooltip("Der Teil, der sich neigt. Sein Ursprung (Pivot) ist das Gelenk, +Z (blaue Achse) = Bandrichtung. " +
             "Alles, was mitkippen soll (Band, Teleskopteile, Waende), muss Kind davon sein.")]
    public Transform arm;

    [Tooltip("Box Collider der Bandflaeche (Kind des Arms). Leer = wird beim ConveyorBelt gesucht.")]
    public BoxCollider surface;

    [Tooltip("Teleskopteile: Sie fahren beim Ausfahren in Bandrichtung heraus. Das erste faehrt am " +
             "wenigsten, das letzte am weitesten (Reihenfolge: innen nach aussen).")]
    public Transform[] sections;

    [Tooltip("Optional: Teile, die beim Ausfahren in Z gestreckt werden (z. B. das Band-Mesh). " +
             "Ihr Ursprung muss am HINTEREN Ende liegen.")]
    public Transform[] stretchParts;

    [Header("Neigung")]
    [Tooltip("Winkel relativ zur modellierten Stellung (Grad). Positiv = Spitze hoeher.")]
    public float minAngle = -5f;
    public float maxAngle = 40f;
    [Tooltip("Wie schnell sich der Winkel aendert (Grad pro Sekunde).")]
    public float angleSpeed = 12f;

    [Header("Ausfahren")]
    [Tooltip("So weit faehrt das Band maximal aus (Meter).")]
    public float extendDistance = 3f;
    [Tooltip("Wie schnell es aus- und einfaehrt (Meter pro Sekunde).")]
    public float extendSpeed = 0.6f;

    [Header("Sonstiges")]
    [Tooltip("Fuegt der Wurzel einen kinematischen Rigidbody hinzu. Bewegte Collider brauchen " +
             "das, sonst wird die Physik bei jeder Bewegung neu berechnet.")]
    public bool addKinematicBody = true;

    // Aktuelle Stellung.
    public float Angle { get { return angle; } }
    public float Extension { get { return extension; } }
    public float Angle01 { get { return Mathf.InverseLerp(minAngle, maxAngle, angle); } }
    public float Extension01 { get { return extendDistance > 0f ? extension / extendDistance : 0f; } }

    private float angle;
    private float extension;

    // Ausgangswerte (so, wie es modelliert wurde).
    private Quaternion baseRotation = Quaternion.identity;
    private Vector3 baseSize, baseCenter;
    private Vector3[] sectionBase;
    private Vector3[] stretchBase;
    private float baseLength = 1f;

    void Awake()
    {
        if (surface == null)
        {
            foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>(true))
            {
                if (!box.isTrigger)
                {
                    surface = box;
                    break;
                }
            }
        }

        if (arm == null)
            arm = surface != null ? surface.transform : transform;

        if (surface == null)
        {
            Debug.LogError("TelescopicBelt: Kein Box Collider fuer die Bandflaeche gefunden!", this);
            enabled = false;
            return;
        }

        baseRotation = arm.localRotation;
        baseSize = surface.size;
        baseCenter = surface.center;
        baseLength = baseSize.z * Mathf.Abs(surface.transform.lossyScale.z);

        sectionBase = new Vector3[sections != null ? sections.Length : 0];

        for (int i = 0; i < sectionBase.Length; i++)
            sectionBase[i] = sections[i] != null ? sections[i].localPosition : Vector3.zero;

        stretchBase = new Vector3[stretchParts != null ? stretchParts.Length : 0];

        for (int i = 0; i < stretchBase.Length; i++)
            stretchBase[i] = stretchParts[i] != null ? stretchParts[i].localScale : Vector3.one;

        if (addKinematicBody && GetComponentInParent<Rigidbody>() == null)
        {
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        Apply();
    }

    // ---------- Steuerung (von den Hebeln aufgerufen) ----------

    // input: -1 .. +1 (negativ = Spitze senken)
    public void DriveAngle(float input, float dt)
    {
        if (Mathf.Abs(input) < 0.001f)
            return;

        float old = angle;
        angle = Mathf.Clamp(angle + input * angleSpeed * dt, minAngle, maxAngle);

        if (!Mathf.Approximately(old, angle))
            Apply();
    }

    // input: -1 .. +1 (negativ = einfahren)
    public void DriveExtension(float input, float dt)
    {
        if (Mathf.Abs(input) < 0.001f)
            return;

        float old = extension;
        extension = Mathf.Clamp(extension + input * extendSpeed * dt, 0f, extendDistance);

        if (!Mathf.Approximately(old, extension))
            Apply();
    }

    // ---------- Stellung anwenden ----------

    private static float LocalLength(Transform t, float meters)
    {
        // Strecke in Metern -> Strecke im Raum von "t" (falls skaliert).
        float scale = t != null ? Mathf.Abs(t.lossyScale.z) : 1f;
        return scale > 0.0001f ? meters / scale : meters;
    }

    private void Apply()
    {
        // Neigen: Drehung um die X-Achse; negativ = Spitze nach oben.
        arm.localRotation = baseRotation * Quaternion.Euler(-angle, 0f, 0f);

        // Die Bandflaeche waechst nach vorne.
        float local = LocalLength(surface.transform, extension);

        surface.size = new Vector3(baseSize.x, baseSize.y, baseSize.z + local);
        surface.center = new Vector3(baseCenter.x, baseCenter.y, baseCenter.z + local * 0.5f);

        // Teleskopteile schieben sich heraus: jedes weiter als das vorige.
        int n = sectionBase.Length;

        for (int i = 0; i < n; i++)
        {
            if (sections[i] == null)
                continue;

            float share = extension * (i + 1) / n;
            sections[i].localPosition = sectionBase[i] + Vector3.forward * LocalLength(sections[i].parent, share);
        }

        // Gestreckte Teile.
        float factor = (baseLength + extension) / Mathf.Max(0.01f, baseLength);

        for (int i = 0; i < stretchBase.Length; i++)
        {
            if (stretchParts[i] == null)
                continue;

            Vector3 s = stretchBase[i];
            stretchParts[i].localScale = new Vector3(s.x, s.y, s.z * factor);
        }
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("angle", angle);
        bag.SetFloat("ext", extension);
    }

    public void LoadState(StateBag bag)
    {
        angle = Mathf.Clamp(bag.GetFloat("angle", angle), minAngle, maxAngle);
        extension = Mathf.Clamp(bag.GetFloat("ext", extension), 0f, extendDistance);

        if (surface != null && arm != null)
            Apply();
    }
}
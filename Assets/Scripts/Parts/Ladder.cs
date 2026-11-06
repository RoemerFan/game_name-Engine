using UnityEngine;

// Leiter. Man klettert, indem man auf die Leiter zulaeuft und "vorwaerts" haelt:
// Vorwaerts = hoch, rueckwaerts = runter, ohne Eingabe haengt man fest. Oben laeuft man
// einfach ueber die oberste Sprosse auf die Plattform, unten steht man wieder auf dem Boden.
// Von oben steigt man ein, indem man zur Kante und ueber sie hinaus laeuft. Mit Springen
// springt man ab. Das Klettern selbst steckt im FirstPersonController.
//
// So baust du eine Leiter (Prefab oder als Teil eines Geraets):
//
//  1. Modell: Die Leiter mit Sprossen. Sie braucht KEINE Collider vor den Sprossen (der Spieler
//     haengt davor, ein Collider wuerde ihn wegdruecken). Soll sie einen haben, dann nur einen
//     duennen Quader direkt an der Wand, hinter den Sprossen.
//
//  2. Markierung "Bottom": leeres Kind-Objekt UNTEN an den Sprossen, auf Bodenhoehe, genau auf
//     der Linie der Sprossen (nicht davor). Seine BLAUE Achse (Z) muss zur Wand zeigen, also in
//     die Richtung, in die man beim Klettern schaut. (Im Editor zeigt ein tuerkiser Strich
//     die Richtung, wenn die Zone ausgewaehlt ist.)
//
//  3. Markierung "Top": leeres Kind-Objekt OBEN auf der Hoehe der Plattform (Oberkante,
//     auf der man nach dem Klettern steht). Nur die Hoehe zaehlt; so weit wird geklettert.
//
//  4. Kletterzone: Leeres Kind-Objekt mit Box Collider (Is Trigger) und diesem Skript; Bottom
//     und Top eintragen. Der Quader umfasst:
//       - Breite: ca. 1 m (um die Leiter herum),
//       - Hoehe: vom Boden bis ca. 1 m ueber Top,
//       - Tiefe: von ca. 1 m VOR den Sprossen bis ca. 0,5 m HINTER die Sprossenlinie
//         (damit man auch oben an der Kante einsteigen kann).
//
//  5. Die Plattform oben muss hinter der Leiter (in Richtung der blauen Achse von Bottom) weitergehen,
//     denn dorthin steigt man nach dem Klettern.
[RequireComponent(typeof(Collider))]
public class Ladder : MonoBehaviour
{
    [Header("Markierungen")]
    [Tooltip("Unten an den Sprossen, auf Bodenhoehe. Blaue Achse zeigt zur Wand.")]
    [SerializeField] private Transform bottom;

    [Tooltip("Oben, auf der Hoehe der Plattform-Oberkante.")]
    [SerializeField] private Transform top;

    [Header("Klettern")]
    [Tooltip("Geschwindigkeit in Metern pro Sekunde.")]
    [SerializeField] private float climbSpeed = 2.5f;

    [Tooltip("Abstand zwischen Spieler (Kapselrand) und Sprossen.")]
    [SerializeField] private float clearance = 0.1f;

    public float ClimbSpeed => climbSpeed;
    public bool IsValid => bottom != null && top != null;
    public float BottomY => bottom.position.y;
    public float TopY => top.position.y;
    public Vector3 BottomPosition => bottom.position;
    public Vector3 TopPosition => top.position;

    // Richtung zur Wand (waagerecht).
    public Vector3 Facing
    {
        get
        {
            Vector3 f = bottom.forward;
            f.y = 0f;

            if (f.sqrMagnitude < 0.0001f)
                return Vector3.forward;

            return f.normalized;
        }
    }

    // Wo der Spieler (Mitte der Kapsel) beim Klettern steht: vor den Sprossen,
    // je nach Dicke der Kapsel weiter weg.
    public Vector3 StandPoint(float capsuleRadius)
    {
        return bottom.position - Facing * (capsuleRadius + clearance);
    }

    private void Reset()
    {
        Collider col = GetComponent<Collider>();

        if (col != null)
            col.isTrigger = true;
    }

    private void Awake()
    {
        if (!IsValid)
            Debug.LogError("Leiter: 'Bottom' und 'Top' muessen eingetragen sein!", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsValid)
            return;

        FirstPersonController player = other.GetComponentInParent<FirstPersonController>();

        if (player != null)
            player.EnterLadderZone(this);
    }

    private void OnTriggerExit(Collider other)
    {
        FirstPersonController player = other.GetComponentInParent<FirstPersonController>();

        if (player != null)
            player.ExitLadderZone(this);
    }

    // Hilfslinien im Editor: Weg nach oben und Richtung zur Wand.
    private void OnDrawGizmosSelected()
    {
        if (bottom == null || top == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(bottom.position, top.position);
        Gizmos.DrawSphere(bottom.position, 0.06f);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(top.position, 0.08f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(bottom.position, bottom.position + Facing * 0.5f);
    }
}
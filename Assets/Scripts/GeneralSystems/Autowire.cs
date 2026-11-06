using UnityEngine;

// An ein Lampen-Prefab (oder ein anderes kleines Geraet) haengen, das als Kind IN einem
// Maschinen-Prefab steckt. Sobald die Maschine gebaut wird, verbindet sich die Lampe
// selbststaendig mit einem Anschluss fuer duenne Kabel an der Maschine:
//  - elektrisch: Die Lampe haengt danach im Stromnetz der Maschine. Mehrere Lampen
//    duerfen sich denselben Anschluss teilen.
//  - optisch: Ein duennes Kabel fuehrt von der Lampe zum Anschluss (abschaltbar).
// Nichts muss von Hand verdrahtet werden, auch nicht nach dem Laden oder Umplatzieren.
//
// Einrichtung:
//  1. Die Maschine braucht einen Strom-Port, der zum Stromnetz gehoert. Das ist jeder
//     Strom-Port einer Maschine, die selbst am Netz haengt (Pumpe, Foerderband,
//     Giesserei, Hochofen, Aufzug, Generator, Batterie ...). Optional ein eigener Port
//     mit Cable Size = Small, der dann bevorzugt wird.
//     Maschinen OHNE Stromanschluss (z. B. ein Kessel): ein leeres Kind-Objekt mit dem
//     Skript PowerJunction und einem Power-Port anlegen und diesen Port einmal per
//     Kabel ans Netz anschliessen. Die Lampen haengen dann an diesem Kind.
//  2. Die Lampe (Kind der Maschine) bekommt dieses Skript.
//  3. "Socket": den Anschluss der Maschine hineinziehen. Leer = der naechste freie
//     duenne Strom-Port der Maschine wird automatisch gewaehlt.
//  4. "Lamp Port": Port der Lampe (leer = der erste Port im Lampen-Objekt).
public class AutoWire : MonoBehaviour
{
    [Tooltip("Anschluss der Maschine (Port, Typ Power, Cable Size Small). Leer = naechster passender.")]
    [SerializeField] private Port socket;

    [Tooltip("Anschluss der Lampe. Leer = der erste Port in diesem Objekt.")]
    [SerializeField] private Port lampPort;

    [Header("Kabel (nur Optik)")]
    [SerializeField] private bool showCable = true;
    [SerializeField, Min(0.001f)] private float cableRadius = 0.008f;
    [SerializeField] private Color cableColor = new Color(0.95f, 0.75f, 0.1f);
    [SerializeField, Min(0f)] private float stubLength = 0.1f;
    [Tooltip("Durchhang in Metern pro Meter Strecke.")]
    [SerializeField, Min(0f)] private float sagPerMeter = 0.06f;
    [SerializeField, Min(0f)] private float maxSag = 0.5f;

    private Pipe cable;
    private Port wiredA;
    private Port wiredB;

    private void Start()
    {
        Wire();
    }

    // Verbindet Lampe und Anschluss (kann auch spaeter von Hand aufgerufen werden).
    public void Wire()
    {
        Unwire();

        Port mine = lampPort != null ? lampPort : FindOwnPort();
        Port target = socket != null ? socket : FindNearestSocket(mine);

        if (mine == null || target == null)
        {
            Debug.LogWarning("AutoWire: Lampen-Port oder Anschluss der Maschine nicht gefunden.", this);
            return;
        }

        if (mine.Medium != PortMedium.Power || target.Medium != PortMedium.Power)
        {
            Debug.LogWarning("AutoWire: Beide Ports muessen vom Typ Power sein.", this);
            return;
        }

        PowerGrid.Link(mine, target);
        wiredA = mine;
        wiredB = target;

        StartCoroutine(CheckOwner());

        if (!showCable)
            return;

        GameObject cableObject = new GameObject("AutoCable");
        cable = cableObject.AddComponent<Pipe>();
        cable.Build(mine, target, cableRadius, stubLength, cableColor, 0.4f, sagPerMeter, maxSag);

        // Das Kabel haengt an der Lampe: Es bewegt sich mit, wenn die Maschine versetzt wird.
        cableObject.transform.SetParent(transform, true);
    }

    private void Unwire()
    {
        if (wiredA != null && wiredB != null)
            PowerGrid.Unlink(wiredA, wiredB);

        wiredA = null;
        wiredB = null;

        if (cable != null)
            Destroy(cable.gameObject);

        cable = null;
    }

    private void OnDestroy()
    {
        Unwire();
    }

    // Erster Port im eigenen Objekt (die Lampe selbst).
    private Port FindOwnPort()
    {
        return GetComponentInChildren<Port>(true);
    }

    // Passender Anschluss der Maschine (nicht in der Lampe selbst): bevorzugt der naechste
    // duenne Strom-Port, sonst der naechste Strom-Port ueberhaupt (z. B. der Hauptanschluss
    // einer Pumpe, eines Foerderbands oder einer Giesserei).
    private Port FindNearestSocket(Port mine)
    {
        Transform machine = transform.root;
        Vector3 from = mine != null ? mine.transform.position : transform.position;

        Port bestSmall = null;
        float bestSmallDistance = float.MaxValue;
        Port bestAny = null;
        float bestAnyDistance = float.MaxValue;

        foreach (Port p in machine.GetComponentsInChildren<Port>(true))
        {
            if (p == mine || p.transform.IsChildOf(transform))
                continue;

            if (p.RawMedium != PortMedium.Power)
                continue;

            float d = (p.transform.position - from).sqrMagnitude;

            if (d < bestAnyDistance)
            {
                bestAnyDistance = d;
                bestAny = p;
            }

            if (p.CableSize == PortCableSize.Small && d < bestSmallDistance)
            {
                bestSmallDistance = d;
                bestSmall = p;
            }
        }

        return bestSmall != null ? bestSmall : bestAny;
    }

    // Kurz nach dem Start pruefen: Gehoert der Anschluss einem Stromgeraet?
    private System.Collections.IEnumerator CheckOwner()
    {
        yield return new WaitForSeconds(1f);

        if (wiredB != null && !PowerGrid.HasOwner(wiredB))
        {
            Debug.LogWarning(
                "AutoWire: Der Anschluss '" + wiredB.name + "' gehoert keinem Stromgeraet. " +
                "Haenge an das Objekt mit dem Port ein PowerJunction (oder nutze einen Port " +
                "einer Maschine, die selbst ans Stromnetz gehoert).", this);
        }
    }
}
using UnityEngine;

// =====================================================================
//  CarCoupler: der Kupplungs-Griff an einem Ende eines Fahrzeugs (Lok oder Wagen).
//
//  Bedienen: Mit dem Fadenkreuz auf die Kupplung zielen und die Bedientaste druecken.
//   - Ist das Ende frei und steht ein anderes Fahrzeug mit freiem Ende in Reichweite (ca. 2 m),
//     wird angekuppelt. Die Wagen ruecken dabei auf den richtigen Pufferabstand zusammen.
//   - Ist das Ende schon gekuppelt, wird abgekuppelt.
//
//  Zusaetzlich kuppelt ein langsam (bis "Coupling Speed") anrollendes Fahrzeug automatisch an
//  (TrackManager > Wagen > Auto Couple). Fahrzeuge, die schneller anrollen, stossen an den Puffern an.
//
//  Die Griffe werden von TrainCar automatisch an beiden Enden angelegt. Eigene Modelle koennen
//  eigene mitbringen: Kind-Objekt mit Trigger-Collider und diesem Skript, "End" = 0 (vorne) oder 1 (hinten).
//  Das Kommando (Kuppeln/Abkuppeln) schickt dieses Skript selbst, deshalb ISelfNetworked.
// =====================================================================
public class CarCoupler : MonoBehaviour, IInteractable, ISelfNetworked
{
    [Tooltip("Das Fahrzeug (leer = wird im Eltern-Objekt gesucht).")]
    public TrainCar car;

    [Tooltip("0 = vorderes Ende, 1 = hinteres Ende.")]
    public int end;

    void Awake()
    {
        if (car == null)
            car = GetComponentInParent<TrainCar>();
    }

    public string GetPrompt()
    {
        if (car == null)
            return "";

        if (car.LinkAt(end) != null)
            return Loc.T("car.coupler.uncouple");

        int otherEnd;

        if (car.FindCouplePartner(end, out otherEnd) != null)
            return Loc.T("car.coupler.couple");

        return Loc.T("car.coupler.none");
    }

    public void Interact()
    {
        if (car == null || car.NetId <= 0)
            return;

        if (car.LinkAt(end) != null)
        {
            NetSync.Submit(NetCommand.Uncouple(car.NetId, end));
            return;
        }

        int otherEnd;
        TrainCar other = car.FindCouplePartner(end, out otherEnd);

        if (other != null)
            NetSync.Submit(NetCommand.Couple(car.NetId, end, other.NetId, otherEnd));
    }
}
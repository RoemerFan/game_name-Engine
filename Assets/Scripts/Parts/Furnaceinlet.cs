using UnityEngine;

// Beschickungsoeffnung oben am Hochofen. Leeres Kind-Objekt mit einem Collider
// ("Is Trigger"), der die Oeffnung ueberdeckt. Hineingeworfene Kohle und
// Eisenerz werden aufgenommen; Barren, Schlacke und Kanister bleiben liegen.
[RequireComponent(typeof(Collider))]
public class FurnaceInlet : MonoBehaviour
{
    [SerializeField] private BlastFurnace furnace;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
        furnace = GetComponentInParent<BlastFurnace>();
    }

    private void Awake()
    {
        if (furnace == null)
            furnace = GetComponentInParent<BlastFurnace>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (furnace == null)
            return;

        CollectibleCoal pickup = other.GetComponentInParent<CollectibleCoal>();
        ItemTag tag = other.GetComponentInParent<ItemTag>();

        // Dieselkanister gehoeren nicht in den Ofen.
        if (pickup is FuelCanister)
            return;

        // Ohne Markierung gilt ein Gegenstand als Kohle (nur wenn man ihn aufheben kann).
        ItemKind kind;
        GameObject itemObject;

        if (tag != null)
        {
            kind = tag.kind;
            itemObject = pickup != null ? pickup.gameObject : tag.gameObject;
        }
        else if (pickup != null)
        {
            kind = ItemKind.Coal;
            itemObject = pickup.gameObject;
        }
        else
        {
            return;
        }

        // Nur Kohle und Erz werden angenommen.
        if (kind != ItemKind.Coal && kind != ItemKind.Ore)
            return;

        // Im Mehrspieler fuehrt nur der Besitzer des Gegenstands den Einwurf aus.
        if (!NetItems.CanAct(itemObject))
            return;

        string metal = tag != null ? tag.metal : "";

        // Ofen voll (oder anderes Metall im Ofen): Der Gegenstand bleibt liegen.
        if (!furnace.CanAccept(kind, metal))
            return;

        // Der Gegenstand wird verbraucht, der Ofen nimmt ihn ueber ein Kommando auf
        // (im Mehrspieler sehen alle Spieler dasselbe).
        NetItems.Consume(itemObject);
        NetSync.AddCharge(furnace, kind, metal);
    }
}
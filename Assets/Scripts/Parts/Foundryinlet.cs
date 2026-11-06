using UnityEngine;

// Einwurf oben an der Giesserei (z. B. am Deckel des Zylinders). Leeres Kind-Objekt
// mit einem Collider ("Is Trigger"), der die Oeffnung ueberdeckt. Hineingeworfene
// Barren und Metallteile werden eingeschmolzen, sofern ihr Metall zum Inhalt des
// Tiegels passt (siehe Metals/MetalRegistry). Alles andere (Kohle, Erz, Schlacke, Kanister
// und Metall, das gerade nicht passt) bleibt liegen.
[RequireComponent(typeof(Collider))]
public class FoundryInlet : MonoBehaviour
{
    [SerializeField] private Foundry foundry;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
        foundry = GetComponentInParent<Foundry>();
    }

    private void Awake()
    {
        if (foundry == null)
            foundry = GetComponentInParent<Foundry>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (foundry == null)
            return;

        ItemTag tag = other.GetComponentInParent<ItemTag>();

        // Nur Gegenstaende, die aus Metall bestehen.
        if (tag == null || tag.kind != ItemKind.Ingot || string.IsNullOrEmpty(tag.metal))
            return;

        float mass = tag.massKg > 0f ? tag.massKg : foundry.DefaultSolidMassKg;

        // Im Mehrspieler fuehrt nur der Besitzer des Gegenstands den Einwurf aus.
        if (!NetItems.CanAct(tag.gameObject))
            return;

        // Passt das Metall und ist noch Platz?
        if (!foundry.CanAcceptSolid(tag.metal, mass))
            return;

        // Den Gegenstand entfernen (Wurzel des aufhebbaren Objekts) und das
        // Einschmelzen als Kommando ausfuehren, damit alle Spieler dasselbe sehen.
        CollectibleCoal pickup = other.GetComponentInParent<CollectibleCoal>();
        GameObject itemObject = pickup != null ? pickup.gameObject : tag.gameObject;
        string metal = tag.metal;

        NetItems.Consume(itemObject);
        NetSync.AddSolid(foundry, metal, mass);
    }
}
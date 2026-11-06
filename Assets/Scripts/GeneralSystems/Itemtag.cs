using UnityEngine;

// Welche Art von losem Gegenstand ist das? Bisher gab es nur Kohle; mit dem
// Hochofen kommen Eisenerz, Roheisen-Barren und Schlacke dazu.
//
// Gegenstaende ohne dieses Skript gelten als Kohle (wie bisher). Das Eisenerz vom
// VoxelDigger bekommt die Markierung automatisch. Barren und Schlacke bekommt der
// Hochofen bzw. die Giesserei beim Erzeugen. Man kann das Skript aber auch direkt in
// ein Prefab legen.
//
// Zum Aufheben und Tragen braucht ein Gegenstand weiterhin wie die Kohle das Skript
// "CollectibleCoal" (Prefab = Kopie vom CoalChunk mit anderem Modell).
public enum ItemKind
{
    Coal = 0,
    Ore = 1,    // Erz (welches Metall: siehe "metal")
    Ingot = 2,  // Barren / Platte aus Metall (Metall: siehe "metal")
    Slag = 3    // Schlacke
}

public class ItemTag : MonoBehaviour
{
    public ItemKind kind = ItemKind.Coal;

    [Tooltip("Welches Metall steckt im Erz bzw. im Barren? Muss in der MetalRegistry (Inspector) eingetragen " +
             "sein, z. B. 'Iron'. Leer bei Erz = Eisen.")]
    public string metal = "";

    [Tooltip("Bei Barren: Gewicht in Kilogramm (wird beim Einschmelzen zurueckgerechnet). " +
             "0 = unbekannt, dann zaehlt der Standardwert der Maschine.")]
    public float massKg = 0f;

    // Art eines Gegenstands (auch wenn die Markierung an der Wurzel oder einem Kind sitzt).
    public static ItemKind KindOf(Component item)
    {
        if (item == null)
            return ItemKind.Coal;

        ItemTag tag = item.GetComponentInParent<ItemTag>();
        return tag != null ? tag.kind : ItemKind.Coal;
    }
}
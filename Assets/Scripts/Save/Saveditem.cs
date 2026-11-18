using UnityEngine;

// An lose Gegenstaende haengen (Kohlestueck, Dieselkanister ...), die beim
// Speichern erhalten bleiben sollen. Das Prefab muss ausserdem in der Liste
// "Item Prefabs" am SaveSystem eingetragen sein, damit es beim Laden wieder
// erzeugt werden kann (Zuordnung ueber den Prefab-Namen).
//
// Gegenstaende ohne dieses Skript werden nicht gespeichert und beim Laden
// nicht angefasst.
public class SavedItem : MonoBehaviour, ISaveable
{
    // Im Mehrspieler wird der Gegenstand mit den anderen Spielern abgeglichen (siehe NetItems.cs).
    private void Awake()
    {
        if (GetComponent<NetItem>() == null)
            gameObject.AddComponent<NetItem>();
    }

    // Lose Gegenstaende halten das Gelaende um sich herum geladen (kleiner Radius),
    // damit sie nicht durch einen entladenen Boden fallen.
    private void OnEnable()
    {
        VoxelWorld.AddAnchor(transform, 1);
    }

    private void OnDisable()
    {
        VoxelWorld.RemoveAnchor(transform);
    }

    public void SaveState(StateBag bag)
    {
        // Dieselkanister: Fuellstand merken.
        FuelCanister canister = GetComponent<FuelCanister>();

        if (canister != null)
            bag.SetFloat("liters", canister.Liters);

        // Art des Gegenstands (Erz, Barren, Schlacke) merken.
        ItemTag tag = GetComponent<ItemTag>();

        if (tag != null)
        {
            bag.SetFloat("kind", (int)tag.kind);
            bag.SetString("metal", tag.metal ?? "");
            bag.SetFloat("mass", tag.massKg);
        }
    }

    public void LoadState(StateBag bag)
    {
        FuelCanister canister = GetComponent<FuelCanister>();

        if (canister != null)
            canister.Liters = bag.GetFloat("liters", canister.Liters);

        // Markierung wiederherstellen (das Prefab selbst hat sie evtl. nicht).
        float kind = bag.GetFloat("kind", -1f);

        if (kind >= 0f)
        {
            ItemTag tag = GetComponent<ItemTag>();

            if (tag == null)
                tag = gameObject.AddComponent<ItemTag>();

            tag.kind = (ItemKind)Mathf.RoundToInt(kind);
            tag.metal = bag.GetString("metal", tag.metal);
            tag.massKg = bag.GetFloat("mass", tag.massKg);
        }
    }
}
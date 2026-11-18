using UnityEngine;
 
// Dieselkanister. Funktioniert wie ein Kohlestueck: mit E aufheben und
// ablegen (leitet sich von CollectibleCoal ab, damit CoalPickup ihn kennt).
// Prefab wie CoalChunk aufbauen: Wurzel mit Rigidbody und FuelCanister,
// Modell mit Collider als Kind.
public class FuelCanister : CollectibleCoal
{
    [SerializeField] private float liters = 10f;
 
    public float Liters
    {
        get { return liters; }
        set { liters = Mathf.Max(0f, value); }
    }
}
 
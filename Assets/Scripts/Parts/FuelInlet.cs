using UnityEngine;

// Einfuellstutzen am Generator. Leeres Kind-Objekt mit Box Collider
// (Is Trigger) ueber der Tanköffnung. Ein hineingeworfener Kanister wird
// in den Tank entleert. Passt nicht alles hinein, bleibt der Rest im Kanister.
[RequireComponent(typeof(Collider))]
public class FuelInlet : MonoBehaviour
{
    [SerializeField] private DieselGenerator generator;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
        generator = GetComponentInParent<DieselGenerator>();
    }

    private void Awake()
    {
        if (generator == null)
            generator = GetComponentInParent<DieselGenerator>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (generator == null)
            return;

        FuelCanister canister = other.GetComponentInParent<FuelCanister>();

        if (canister == null)
            return;

        // Im Mehrspieler fuehrt nur der Besitzer des Kanisters das Entleeren aus.
        if (!NetItems.CanAct(canister.gameObject))
            return;

        // Wie viel passt noch in den Tank?
        float accepted = Mathf.Min(
            canister.Liters, generator.FuelCapacity - generator.Fuel);

        if (accepted <= 0.001f)
            return;

        canister.Liters -= accepted;

        // Der Tank wird ueber ein Kommando gefuellt (im Mehrspieler bei allen).
        NetSync.AddFuel(generator, accepted);

        if (canister.Liters <= 0.01f)
            NetItems.Consume(canister.gameObject);
    }
}
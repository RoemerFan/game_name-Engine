using UnityEngine;

// Einfuellstutzen am Dieseltank des Tenders. Leeres Kind-Objekt mit Box Collider (Is Trigger)
// ueber der Tankoeffnung. Ein hineingeworfener Kanister wird in den Tank entleert; passt nicht
// alles hinein, bleibt der Rest im Kanister. Im Mehrspieler fuehrt es nur der Besitzer des Kanisters aus
// und meldet die Menge als Kommando an alle.
[RequireComponent(typeof(Collider))]
public class TenderFuelInlet : MonoBehaviour
{
    [SerializeField] private TrainTender tender;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
        tender = GetComponentInParent<TrainTender>();
    }

    private void Awake()
    {
        if (tender == null)
            tender = GetComponentInParent<TrainTender>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (tender == null || tender.fuelType != FuelType.Diesel)
            return;

        FuelCanister canister = other.GetComponentInParent<FuelCanister>();

        if (canister == null || !NetItems.CanAct(canister.gameObject))
            return;

        float accepted;
        TrainCar car = tender.GetComponentInParent<TrainCar>();

        if (NetSync.IsOnline && car != null && car.NetId > 0)
        {
            // Online: Menge hier ausrechnen und als Kommando an alle schicken.
            accepted = Mathf.Clamp(canister.Liters, 0f, tender.Capacity - tender.Amount);

            if (accepted <= 0.001f)
                return;

            NetSync.Submit(NetCommand.LocoFuel(car.NetId, accepted));
        }
        else
        {
            accepted = tender.Add(canister.Liters);

            if (accepted <= 0.001f)
                return;
        }

        canister.Liters -= accepted;

        if (canister.Liters <= 0.01f)
            NetItems.Consume(canister.gameObject);
    }
}
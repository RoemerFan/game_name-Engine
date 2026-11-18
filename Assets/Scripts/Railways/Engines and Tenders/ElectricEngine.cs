using UnityEngine;

// =====================================================================
//  ElectricEngine: Elektroantrieb. Zieht den Strom aus der Tender-Batterie (Typ "Electric").
//  Verbrauch: kWh pro Sekunde bei vollem Regler (anteilig mit dem Regler).
//  Aufgeladen wird die Batterie spaeter an Ladestationen (TrainTender.Add).
// =====================================================================
public class ElectricEngine : TrainEngine
{
    [Tooltip("Stromverbrauch bei vollem Regler (kWh pro Sekunde). 0,01 = 36 kW.")]
    public float kWhPerSecondFull = 0.01f;

    private bool running;

    public override FuelType Fuel { get { return FuelType.Electric; } }

    public override float UseFuel(float throttle, float dt, TrainTender tender)
    {
        running = false;

        if (throttle <= 0.01f || tender == null || tender.fuelType != FuelType.Electric)
            return 0f;

        float need = throttle * kWhPerSecondFull * dt;

        if (need <= 0f)
            return 0f;

        float got = tender.Take(need);
        running = got > 0f;

        return Mathf.Clamp01(got / need);
    }

    public override string StatusText()
    {
        return Loc.T("train.electric.status", Loc.T(running ? "train.engine.on" : "train.engine.off"));
    }
}
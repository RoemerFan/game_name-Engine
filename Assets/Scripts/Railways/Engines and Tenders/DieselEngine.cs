using UnityEngine;

// =====================================================================
//  DieselEngine: Dieselantrieb. Zieht den Diesel aus dem Tender (Typ "Diesel").
//  Verbrauch: bei Leerlauf-Regler laeuft der Motor nicht und verbraucht nichts; sobald der
//  Regler offen ist, verbraucht er zwischen "Idle" und "Full" Litern pro Sekunde.
// =====================================================================
public class DieselEngine : TrainEngine
{
    [Tooltip("Verbrauch bei vollem Regler (Liter pro Sekunde).")]
    public float litersPerSecondFull = 0.04f;

    [Tooltip("Verbrauch bei kaum geoeffnetem Regler (Liter pro Sekunde).")]
    public float idleLitersPerSecond = 0.004f;

    private bool running;

    public override FuelType Fuel { get { return FuelType.Diesel; } }

    public override float UseFuel(float throttle, float dt, TrainTender tender)
    {
        running = false;

        if (throttle <= 0.01f || tender == null || tender.fuelType != FuelType.Diesel)
            return 0f;

        float need = Mathf.Lerp(idleLitersPerSecond, litersPerSecondFull, throttle) * dt;

        if (need <= 0f)
            return 0f;

        float got = tender.Take(need);
        running = got > 0f;

        return Mathf.Clamp01(got / need);
    }

    public override string StatusText()
    {
        return Loc.T("train.diesel.status", Loc.T(running ? "train.engine.on" : "train.engine.off"));
    }
}
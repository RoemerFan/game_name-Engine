using UnityEngine;

// =====================================================================
//  SteamEngine: Dampfantrieb. Braucht einen Boiler (Kessel) am selben Objekt
//  oder darunter. Der Kessel wird wie ueberall im Spiel mit Kohle gefeuert; die Lok
//  verbraucht Dampf aus dem Kessel.
//
//  Einstellungen am BOILER (nicht hier): wie lange ein Stueck Kohle brennt
//  ("Burn Seconds Per Coal"), wie viel Wasser pro Sekunde verdampft
//  ("Evaporation Per Second"), Kohle- und Wasserfassungsvermoegen, Dampfspeicher.
//  Einstellungen HIER: wie viel Dampf die Lok bei vollem Regler verbraucht.
// =====================================================================
public class SteamEngine : TrainEngine
{
    [Tooltip("Der Kessel (wird automatisch gefunden).")]
    public Boiler boiler;

    [Tooltip("Dampfverbrauch pro Sekunde bei vollem Regler. Zum Vergleich: Der Kessel fasst standardmaessig " +
             "100 Einheiten und erzeugt bei Feuer 2 pro Sekunde.")]
    public float steamPerSecondFull = 2.5f;

    [Tooltip("Ab diesem Druck (0-1) hat die Lok volle Leistung, darunter wird sie schwaecher.")]
    [Range(0.05f, 1f)] public float fullPowerPressure = 0.4f;

    public override FuelType Fuel { get { return FuelType.Coal; } }

    void Awake()
    {
        if (boiler == null)
            boiler = GetComponent<Boiler>();

        if (boiler == null)
            boiler = GetComponentInChildren<Boiler>();

        if (boiler == null)
            boiler = GetComponentInParent<Boiler>();
    }

    public override float UseFuel(float throttle, float dt, TrainTender tender)
    {
        if (boiler == null)
            return 0f;

        float demand = throttle * steamPerSecondFull * dt;

        if (demand <= 0f)
            return 0f;

        float pressureFactor = Mathf.Clamp01(boiler.Pressure01 / fullPowerPressure);
        float got = boiler.ConsumeSteam(demand);

        return (got / demand) * pressureFactor;
    }

    public override string StatusText()
    {
        if (boiler == null)
            return "";

        return Loc.T("train.hud2",
            Mathf.RoundToInt(boiler.Pressure01 * 100f),
            Mathf.RoundToInt(boiler.WaterLevel01 * 100f),
            boiler.CoalTotal);
    }
}
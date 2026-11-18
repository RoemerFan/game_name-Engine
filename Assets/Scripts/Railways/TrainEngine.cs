using UnityEngine;

// Welche Energie ein Antrieb braucht (und was ein Tender speichert).
public enum FuelType
{
    Coal,      // Kohle (Dampfkessel, Tender = Kohlelager)
    Diesel,    // Diesel (Tender = Dieseltank)
    Electric   // Strom (Tender = Batterie)
}

// =====================================================================
//  TrainEngine: Grundlage aller austauschbaren Antriebe einer Lok.
//
//  Ein Antrieb liegt als eigenes Prefab im Lok-Typ (TrackManager > Locomotives > Engine Prefab)
//  und wird an den "Engine Socket" der Lok gesetzt. Es gibt drei fertige Antriebe:
//    SteamEngine    - Dampfkessel, wird mit Kohle gefeuert
//    DieselEngine   - Dieselmotor, zieht Diesel aus dem Tender
//    ElectricEngine - Elektromotor, zieht Strom aus der Tender-Batterie
//
//  Eigene Antriebe: von dieser Klasse ableiten.
// =====================================================================
public abstract class TrainEngine : MonoBehaviour, ISaveable
{
    // Welchen Brennstoff der Antrieb braucht.
    public abstract FuelType Fuel { get; }

    // Wird jeden Physik-Schritt von der Lok aufgerufen. Verbraucht Brennstoff fuer diesen Schritt
    // und gibt die verfuegbare Leistung (0 = keine, 1 = voll) zurueck.
    public abstract float UseFuel(float throttle, float dt, TrainTender tender);

    // Kurzer Text fuer die Anzeige im Fuehrerstand (Zustand des Antriebs).
    public abstract string StatusText();

    public virtual void SaveState(StateBag bag) { }
    public virtual void LoadState(StateBag bag) { }
}
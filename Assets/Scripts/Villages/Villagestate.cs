using System.Collections.Generic;

// Der veraenderliche Zustand eines Dorfes: Lagerbestand und Ansehen beim Spieler.
// (Das Aussehen des Dorfes steht im VillagePlan und entsteht immer neu aus dem Seed.)
public class VillageState
{
    public int id;

    // Ansehen des Spielers in diesem Dorf (steigt durch Lieferungen).
    public float reputation;

    // Lagerbestand pro Ware (nur Waren, die das Dorf herstellt oder braucht).
    public Dictionary<string, float> stock = new Dictionary<string, float>();

    // Bis zu diesem Spielzeitpunkt (Sekunden) wurde das Lager nachgerechnet.
    public double lastSim;
}
using System;
using UnityEngine;

// Das Geld des Spielers (gilt fuer das ganze Spiel). Wird mit dem Spielstand gespeichert.
public static class PlayerWallet
{
    // Startkapital in einem neuen Spiel.
    public const float StartMoney = 100f;

    public static float Money { get; private set; }

    // Wird bei jeder Aenderung ausgeloest (fuer Anzeigen).
    public static event Action Changed;

    static PlayerWallet()
    {
        Money = StartMoney;
    }

    // Beim Start des Spiels (auch im Editor beim erneuten Spielen) wieder auf Anfang setzen.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnStart()
    {
        Money = StartMoney;
        Changed = null;
    }

    public static void Add(float amount)
    {
        Money += amount;

        if (Changed != null)
            Changed();
    }

    public static bool TrySpend(float amount)
    {
        if (amount > Money)
            return false;

        Add(-amount);
        return true;
    }

    public static void Save(StateBag bag)
    {
        bag.SetFloat("money", Money);
    }

    public static void Load(StateBag bag)
    {
        Money = bag != null ? bag.GetFloat("money", StartMoney) : StartMoney;

        if (Changed != null)
            Changed();
    }
}
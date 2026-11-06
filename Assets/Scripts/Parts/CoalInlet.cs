using UnityEngine;

// An ein Kind-Objekt des Kessels hängen (Collider mit "Is Trigger" an der Öffnung).
[RequireComponent(typeof(Collider))]
public class CoalInlet : MonoBehaviour
{
    [SerializeField] private Boiler boiler;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
        boiler = GetComponentInParent<Boiler>();
    }

    private void Awake()
    {
        if (boiler == null)
            boiler = GetComponentInParent<Boiler>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (boiler == null)
            return;

        CollectibleCoal coal = other.GetComponentInParent<CollectibleCoal>();

        if (coal == null)
            return;

        // Dieselkanister sind keine Kohle.
        if (coal is FuelCanister)
            return;

        // Eisenerz, Barren und Schlacke brennen nicht.
        if (ItemTag.KindOf(coal) != ItemKind.Coal)
            return;

        // Im Mehrspieler fuehrt nur der Besitzer des Gegenstands den Einwurf aus.
        if (!NetItems.CanAct(coal.gameObject))
            return;

        // Kessel voll: Kohle bleibt liegen bzw. fällt wieder heraus.
        if (!boiler.HasCoalSpace)
            return;

        // Die Kohle wird verbraucht (bei allen Spielern entfernt), der Kessel nimmt sie
        // ueber ein Kommando auf (im Mehrspieler sehen alle Spieler dasselbe).
        NetItems.Consume(coal.gameObject);
        NetSync.AddCoal(boiler);
    }
}
using UnityEngine;

// Feuerregler (Luftklappe) am Kessel: Mit der Interaktionstaste (F) wird
// zwischen den Stufen umgeschaltet:
//     Automatik -> 100 % -> 75 % -> 50 % -> 25 % -> 0 % (Klappe zu) -> Automatik ...
//
// Aufbau: Ein Objekt am Kessel mit diesem Skript und einem Collider
// (wie bei den Schaltern, damit man es anvisieren kann). Optional ein
// Hebel-Modell, das sich passend zur Stufe bewegt.
public class BoilerDamper : MonoBehaviour, IInteractable, ISaveable
{
    [SerializeField] private Boiler boiler;

    [Header("Hebel (optional)")]
    [Tooltip("Das Teil, das sich bewegen soll (muss ein einzelnes Objekt sein).")]
    [SerializeField] private Transform lever;

    [Tooltip("Lokale Drehung (Rotation im Inspector) des Hebels in der Stellung 'Automatik'.")]
    [SerializeField] private Vector3 leverAutoEuler;

    [Tooltip("Lokale Drehung bei 0 % (Klappe zu).")]
    [SerializeField] private Vector3 leverClosedEuler;

    [Tooltip("Lokale Drehung bei 100 % (voll offen). Zwischenstufen werden dazwischen eingestellt.")]
    [SerializeField] private Vector3 leverFullEuler;

    [Tooltip("Wie schnell sich der Hebel bewegt.")]
    [SerializeField] private float leverSpeed = 8f;

    // Handstufen in der Reihenfolge des Umschaltens (nach Automatik).
    private static readonly float[] Steps = { 1f, 0.75f, 0.5f, 0.25f, 0f };

    // -1 = Automatik, sonst Index in Steps.
    private int stepIndex = -1;

    private void Awake()
    {
        if (boiler == null)
            boiler = GetComponentInParent<Boiler>();

        // Startstellung vom Kessel uebernehmen.
        if (boiler != null && !boiler.AutoRegulation)
        {
            stepIndex = 0;
            float best = float.MaxValue;

            for (int i = 0; i < Steps.Length; i++)
            {
                float diff = Mathf.Abs(Steps[i] - boiler.ManualFire);

                if (diff < best)
                {
                    best = diff;
                    stepIndex = i;
                }
            }
        }

        // Hebel sofort in die richtige Stellung setzen.
        if (lever != null)
            lever.localRotation = TargetRotation();
    }

    public string GetPrompt()
    {
        if (boiler == null)
            return "-";

        if (stepIndex < 0)
        {
            return Loc.T("damper.prompt.auto",
                Mathf.RoundToInt(boiler.FireRate * 100f));
        }

        return Loc.T("damper.prompt.manual",
            Mathf.RoundToInt(Steps[stepIndex] * 100f));
    }

    public void Interact()
    {
        if (boiler == null)
            return;

        // Naechste Stufe; nach der letzten wieder Automatik.
        stepIndex++;

        if (stepIndex >= Steps.Length)
            stepIndex = -1;

        if (stepIndex < 0)
        {
            boiler.SetAutoRegulation(true);
        }
        else
        {
            boiler.SetAutoRegulation(false);
            boiler.SetManualFire(Steps[stepIndex]);
        }
    }

    // Soll-Drehung des Hebels fuer die aktuelle Stufe.
    private Quaternion TargetRotation()
    {
        if (stepIndex < 0)
            return Quaternion.Euler(leverAutoEuler);

        // 0 % = geschlossen, 100 % = voll; dazwischen gleichmaessig.
        return Quaternion.Slerp(
            Quaternion.Euler(leverClosedEuler),
            Quaternion.Euler(leverFullEuler),
            Steps[stepIndex]);
    }

    private void Update()
    {
        if (lever == null)
            return;

        // Weich zur Zielstellung drehen.
        float t = 1f - Mathf.Exp(-leverSpeed * Time.deltaTime);
        lever.localRotation = Quaternion.Slerp(lever.localRotation, TargetRotation(), t);
    }

    // ---------- Speichern ----------
    // Der Kessel speichert Automatik/Handstufe selbst; hier nur die Hebelstellung.

    public void SaveState(StateBag bag)
    {
        bag.SetInt("step", stepIndex);
    }

    public void LoadState(StateBag bag)
    {
        stepIndex = Mathf.Clamp(bag.GetInt("step", stepIndex), -1, Steps.Length - 1);
    }
}
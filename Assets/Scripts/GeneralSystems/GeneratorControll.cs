using UnityEngine;

public enum GeneratorControlType
{
    PullStart,
    HandCrank,
    StopEngine
}

// Einzelnes Bedienelement am Generator (z. B. Reissleine, Kurbel, Stopp-Knopf).
// Leeres Kind-Objekt an die Stelle im Modell setzen, kleinen Box Collider
// (Is Trigger) und dieses Skript dranhaengen, Typ waehlen.
public class GeneratorControl : MonoBehaviour, IInteractable
{
    [SerializeField] private DieselGenerator generator;
    [SerializeField] private GeneratorControlType type;

    private void Awake()
    {
        if (generator == null)
            generator = GetComponentInParent<DieselGenerator>();
    }

    public string GetPrompt()
    {
        if (generator == null)
            return Loc.T("control.nogenerator");

        switch (type)
        {
            case GeneratorControlType.PullStart:
                if (generator.EngineRunning)
                    return Loc.T("control.pull.running");

                if (generator.Fuel <= 0f)
                    return Loc.T("control.pull.empty");

                return Loc.T("control.pull.ready",
                    generator.PullCount, generator.PullsToStart);

            case GeneratorControlType.HandCrank:
                return Loc.T(generator.EngineRunning
                    ? "control.crank.running"
                    : "control.crank.ready");

            default:
                return Loc.T(generator.EngineRunning
                    ? "control.stop.running"
                    : "control.stop.off");
        }
    }

    public void Interact()
    {
        if (generator == null)
            return;

        switch (type)
        {
            case GeneratorControlType.PullStart:
                generator.PullStart();
                break;

            case GeneratorControlType.HandCrank:
                generator.Crank();
                break;

            default:
                generator.StopEngine();
                break;
        }
    }
}
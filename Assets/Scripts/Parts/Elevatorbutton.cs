using UnityEngine;

// Knopf fuer den Aufzug. Leeres Kind-Objekt mit Trigger-Collider (oder Box Collider)
// an die Knopfposition setzen, dieses Skript dazu und die Etage eintragen.
//   - An jeder Etage ein Knopf: "Floor" = Nummer dieser Etage (0 = erste).
//   - In der Kabine ein Knopf pro Etage (als Kinder der Plattform, dann fahren sie mit).
// Der Knopf wird wie ein Schalter mit der Interaktionstaste bedient. Im Mehrspieler
// laeuft das ueber dieselben Kommandos wie die Schalter, alle Spieler sehen denselben Aufzug.
public class ElevatorButton : MonoBehaviour, IInteractable
{
    [SerializeField] private Elevator elevator;

    [Tooltip("Zu dieser Etage faehrt der Aufzug (0 = erste Etage in der Liste 'Stops').")]
    [SerializeField] private int floor;

    private void Reset()
    {
        elevator = GetComponentInParent<Elevator>();

        Collider col = GetComponent<Collider>();

        if (col != null)
            col.isTrigger = true;
    }

    private void Awake()
    {
        if (elevator == null)
            elevator = GetComponentInParent<Elevator>();
    }

    public string GetPrompt()
    {
        if (elevator == null)
            return "?";

        return Loc.T("elevator.prompt", elevator.FloorName(floor), elevator.GetStatus());
    }

    public void Interact()
    {
        if (elevator != null)
            elevator.GoTo(floor);
    }
}
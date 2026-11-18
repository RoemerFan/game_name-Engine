using UnityEngine;
 
// Verteiler: Ein Gehaeuse mit mehreren Power-Ports (z. B. 4 Stueck),
// damit mehrere Geraete ans gleiche Netz koennen.
// Hat selbst keine Leistung, leitet nur weiter.
public class PowerJunction : MonoBehaviour
{
    private void OnEnable()
    {
        PowerGrid.Register(this);
    }
 
    private void OnDisable()
    {
        PowerGrid.Unregister(this);
    }
}
 
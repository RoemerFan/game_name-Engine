using UnityEngine;

// Wird vom BuildingSystem an jede gebaute Maschine gehaengt.
// Merkt sich, aus welchem Prefab sie stammt. Damit erkennen der Abriss- und
// der Umplatzier-Modus, welche Objekte gebaut wurden (und nicht fest zur
// Szene gehoeren), und wissen, welches Geisterbild beim Umplatzieren
// gezeigt werden soll.
public class PlacedMachine : MonoBehaviour
{
    // Das Prefab, aus dem diese Maschine gebaut wurde.
    public GameObject Prefab;

    // Nummer der Maschine im Mehrspieler (auf allen Rechnern gleich).
    // 0 = noch keine vergeben.
    [HideInInspector] public int NetId;

    // Um jede Maschine bleibt das Gelaende geladen (Streaming-Welt), auch wenn der
    // Spieler weit weg ist. Sonst wuerde die Maschine ihren Boden verlieren.
    private void OnEnable()
    {
        VoxelWorld.AddAnchor(transform);
    }

    private void OnDisable()
    {
        VoxelWorld.RemoveAnchor(transform);
    }

    private void OnDestroy()
    {
        NetSync.Unregister(this);
    }
}
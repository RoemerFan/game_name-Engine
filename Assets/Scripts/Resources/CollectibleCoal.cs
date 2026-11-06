using UnityEngine;

// Markiert einen Gegenstand als aufhebbar (Kohle, Erz, Barren, Kanister, Leiter ...).
// Die Werte darunter bestimmen, wie er in der Hand gehalten wird. Ohne Aenderung gilt
// das Verhalten wie bisher: Der Gegenstand haelt die Ausrichtung der Kamera.
public class CollectibleCoal : MonoBehaviour
{
    [Header("Halten")]
    [Tooltip("Zusaetzliche Drehung in der Hand (Grad, X/Y/Z), relativ zur Blickrichtung. " +
             "Beispiel: Eine Leiter, die waagerecht liegt, aber senkrecht gehalten werden soll: " +
             "(0, 0, 90) oder (90, 0, 0) ausprobieren, je nach Achsen des Modells.")]
    public Vector3 holdEuler = Vector3.zero;

    [Tooltip("Verschiebung in der Hand (Meter): X = rechts, Y = hoch, Z = weg vom Spieler. " +
             "Fuer lange Gegenstaende (Leiter): etwas nach unten und weiter weg.")]
    public Vector3 holdOffset = Vector3.zero;

    [Tooltip("Ein = Der Gegenstand kippt mit dem Blick nach oben/unten mit. " +
             "Aus = Er dreht sich nur mit dem Blick nach links/rechts und bleibt sonst aufrecht " +
             "(sinnvoll fuer Leitern und lange Teile).")]
    public bool holdFollowsPitch = true;
}
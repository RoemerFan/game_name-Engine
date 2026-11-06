using UnityEngine;
 
// Wo darf ein Teil gebaut werden?
public enum PlacementSurface
{
    Ground,       // nur auf dem Boden
    Wall,         // nur an einer Wand
    GroundOrWall  // beides
}
 
// Optional an die Wurzel eines Maschinen-Prefabs haengen.
// Ohne dieses Skript darf ein Teil nur auf den Boden (wie bisher).
//
// Bei Wandmontage zeigt die Vorderseite der Maschine (blaue Z-Achse) von der
// Wand weg. Die Rueckseite (kleinste Z-Position der festen Collider) liegt an
// der Wand.
public class PlacementRules : MonoBehaviour
{
    public PlacementSurface surface = PlacementSurface.Ground;
}
 
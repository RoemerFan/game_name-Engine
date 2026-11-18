using UnityEngine;
 
// Art einer Flaeche, auf der gebaut werden kann.
public enum BuildSurfaceType
{
    Ground,
    Wall
}
 
// An Waende (und bei Bedarf Boeden) haengen, damit das Bausystem weiss, wo
// Wandteile befestigt werden duerfen. Es genuegt das Skript an einem Objekt,
// das einen Collider hat (oder an dessen Eltern-Objekt).
// Der Boden wird weiterhin ueber den Tag "Ground" erkannt.
public class BuildSurface : MonoBehaviour
{
    public BuildSurfaceType type = BuildSurfaceType.Wall;
}
 
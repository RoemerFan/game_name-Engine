using UnityEngine;

// Hilfsklasse: dreht ein Modellteil (z.B. Turbinenlaeufer) um seine EIGENE
// Mitte statt um den Nullpunkt (Pivot) des Modells. Viele importierte Modelle
// haben ihren Pivot irgendwo anders, deshalb "eiert" das Teil sonst.
public class RotorSpin
{
    private readonly Transform rotor;
    private readonly Vector3 axis;      // Drehachse im Raum des Rotors
    private readonly Vector3 localCenter; // Mitte des Modells im Raum des Rotors
    private readonly Transform pivot;     // Optional: eigener Drehpunkt (Marker an der Achse)

    // Mit eigenem Drehpunkt: Ein leeres Objekt (am besten Kind des Teils) sitzt genau auf der
    // Drehachse. Gedreht wird um seine Position, und die Achse gilt im Raum dieses Markers.
    public RotorSpin(Transform rotor, Vector3 axis, Transform pivot) : this(rotor, axis)
    {
        this.pivot = pivot;
    }

    public RotorSpin(Transform rotor, Vector3 axis)
    {
        this.rotor = rotor;
        this.axis = axis;
        localCenter = Vector3.zero;

        if (rotor == null)
            return;

        // Umfang aller Mesh-Teile des Rotors (im Raum des Rotors) bestimmen.
        bool found = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);

        foreach (MeshFilter filter in rotor.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null)
                continue;

            Bounds mb = filter.sharedMesh.bounds;

            // Die acht Ecken der Bounds umrechnen: Mesh -> Welt -> Rotor.
            for (int i = 0; i < 8; i++)
            {
                Vector3 sign = new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f);

                Vector3 corner = mb.center + Vector3.Scale(mb.extents, sign);
                Vector3 p = rotor.InverseTransformPoint(
                    filter.transform.TransformPoint(corner));

                if (!found)
                {
                    result = new Bounds(p, Vector3.zero);
                    found = true;
                }
                else
                    result.Encapsulate(p);
            }
        }

        if (found)
            localCenter = result.center;
    }

    // Dreht um den berechneten Mittelpunkt, Achse in Rotor-Koordinaten.
    public void Rotate(float degrees)
    {
        if (rotor == null)
            return;

        // Eigener Drehpunkt hat Vorrang vor der automatischen Mitte.
        if (pivot != null)
        {
            rotor.RotateAround(pivot.position, pivot.TransformDirection(axis), degrees);
            return;
        }

        Vector3 worldCenter = rotor.TransformPoint(localCenter);
        Vector3 worldAxis = rotor.TransformDirection(axis);
        rotor.RotateAround(worldCenter, worldAxis, degrees);
    }
}
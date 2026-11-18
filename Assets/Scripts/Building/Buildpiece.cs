using System.Collections.Generic;
using UnityEngine;

// Art des Bauteils.
public enum BuildPieceKind
{
    Slab,   // Bodenplatte (Fundament): rastet an anderen Platten ein, Maschinen duerfen darauf stehen
    Wall,   // Mauer: rastet auf dem Rand einer Platte ein, Mauern lassen sich stapeln
    Floor   // Etagenboden / Dach: liegt auf dem Mauerkranz, rastet an anderen Etagenboeden ein
}

// An die Wurzel eines Prefabs haengen (Bodenplatte oder Mauer), damit das Bausystem
// weiss, wie das Teil einrasten soll.
//
// Annahmen zu den Modellen:
//  - Platte: beliebig gross, flach. Sie rastet buendig an die Seiten anderer Platten an.
//  - Mauer: die lange Seite liegt am Rand der Platte, die duenne Seite zeigt nach
//    aussen/innen. Die Laenge wird automatisch aus den festen Collidern gemessen
//    (egal ob die Mauer entlang X oder Z modelliert ist).
//  - Etagenboden/Dach (Floor): Gleiche Grundflaeche wie die Bodenplatte darunter. Er liegt
//    auf der Oberkante der obersten Mauer und deckt den Raum ab. Mauern, die danach auf
//    seinem Rand gebaut werden, stehen auf seiner Oberseite (naechste Etage).
//  - Die Teile werden nur um die Hochachse gedreht.
public class BuildPiece : MonoBehaviour
{
    public BuildPieceKind kind = BuildPieceKind.Slab;

    [Tooltip("Nur bei Mauern: Sie duerfen nur auf einer Platte gebaut werden, nicht frei im Gelaende.")]
    public bool needsSlab = false;

    // Alle aktiven Bauteile (fuer die Suche nach Nachbarn).
    public static readonly List<BuildPiece> All = new List<BuildPiece>();

    private bool measured;
    private Vector3 localCenter;    // Mitte des Umfangs, im (nicht skalierten) Welt-Mass relativ zum Ursprung
    private Vector3 localExtents;   // halbe Groesse

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    // Misst den Umfang aus allen festen Collidern. Das Teil darf dabei gedreht sein:
    // gerechnet wird im Koordinatensystem, das nur den Ursprung und die Drehung des Teils benutzt.
    private void Measure()
    {
        if (measured)
            return;

        measured = true;

        Quaternion inverse = Quaternion.Inverse(transform.rotation);

        bool has = false;
        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.zero;

        foreach (Collider col in GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger)
                continue;

            Vector3 c;
            Vector3 e;

            BoxCollider box = col as BoxCollider;
            MeshCollider mesh = col as MeshCollider;

            if (box != null)
            {
                c = box.center;
                e = box.size * 0.5f;
            }
            else if (mesh != null && mesh.sharedMesh != null)
            {
                c = mesh.sharedMesh.bounds.center;
                e = mesh.sharedMesh.bounds.extents;
            }
            else
            {
                // Sonstige Collider: grobe Naeherung ueber die Welt-Box.
                Bounds b = col.bounds;

                Encapsulate(b.min, inverse, ref has, ref min, ref max);
                Encapsulate(b.max, inverse, ref has, ref min, ref max);
                continue;
            }

            // Die 8 Ecken der Box in Weltkoordinaten, dann ins Teile-System.
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);

                Encapsulate(col.transform.TransformPoint(corner), inverse, ref has, ref min, ref max);
            }
        }

        if (!has)
        {
            localCenter = Vector3.zero;
            localExtents = Vector3.one * 0.5f;
            return;
        }

        localCenter = (min + max) * 0.5f;
        localExtents = (max - min) * 0.5f;
    }

    private void Encapsulate(Vector3 worldPoint, Quaternion inverse, ref bool has,
                             ref Vector3 min, ref Vector3 max)
    {
        Vector3 p = inverse * (worldPoint - transform.position);

        if (!has)
        {
            min = p;
            max = p;
            has = true;
            return;
        }

        min = Vector3.Min(min, p);
        max = Vector3.Max(max, p);
    }

    // Mitte des Umfangs in der Welt.
    public Vector3 WorldCenter
    {
        get
        {
            Measure();
            return transform.position + transform.rotation * localCenter;
        }
    }

    // Halbe Groesse im Koordinatensystem des Teils (X/Z gehoeren zur Drehung des Teils).
    public Vector3 Extents
    {
        get
        {
            Measure();
            return localExtents;
        }
    }

    public float TopY
    {
        get { return WorldCenter.y + Extents.y; }
    }

    public float BottomY
    {
        get { return WorldCenter.y - Extents.y; }
    }

    // Waagerechter Abstand eines Punktes zur Grundflaeche (0 = darueber/darin).
    public float HorizontalDistance(Vector3 worldPoint)
    {
        Vector3 p = Quaternion.Inverse(transform.rotation) * (worldPoint - WorldCenter);
        Vector3 e = Extents;

        float dx = Mathf.Max(Mathf.Abs(p.x) - e.x, 0f);
        float dz = Mathf.Max(Mathf.Abs(p.z) - e.z, 0f);

        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
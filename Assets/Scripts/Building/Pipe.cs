using System.Collections.Generic;
using UnityEngine;

// Eine sichtbare Leitung (Rohr oder Kabel) zwischen zwei Ports.
// Wird vom PipeBuilder erzeugt.
//
// Aufbau der Linie:
//   Port A -> kurzes gerades Stueck -> sanfte Kurve -> kurzes gerades Stueck -> Port B
// Die Kurve ist eine "Bezier-Kurve": Sie verlaesst jeden Port genau in dessen
// Blickrichtung (blaue Z-Achse) und biegt dann weich zum anderen Port.
// Aus der Linie wird ein einziges rundes Rohr-Mesh gebaut (statt vieler Zylinder).
//
// Strom-Kabel haben zusaetzlich eine Leistungsgrenze: Bei Ueberlast erhitzen
// sie sich (Farbe wird roter) und brennen irgendwann durch.
public class Pipe : MonoBehaviour
{
    // Wie rund der Querschnitt ist (mehr Ecken = runder, aber mehr Dreiecke).
    private const int PipeSides = 12;
    private const int CableSides = 8;

    public Port PortA { get; private set; }
    public Port PortB { get; private set; }

    // 0 = keine Grenze (normale Rohre).
    public float RatingKW { get; private set; }
    public float LoadKW { get; private set; }

    // 0 = kalt, 1 = durchgebrannt.
    public float Heat { get; private set; }

    private float tripSecondsAtDouble = 6f;
    private float coolSeconds = 10f;
    private float reportedFlow;
    private Color baseColor;
    private Material material;
    private Mesh mesh;

    // Standard-Material der Pipeline, einmal geliehen und fuer alle Leitungen genutzt.
    private static Material sharedBaseMaterial;

    // Baut die Leitung. "bendStrength" bestimmt, wie weit die Kurve ausholt
    // (klein = enge Kurve, gross = weiter Bogen).
    // "sagPerMeter" laesst die Leitung durchhaengen: 0 = starr, 0.08 = pro Meter
    // waagerechter Strecke haengt sie 8 cm durch. "maxSag" begrenzt das (in Metern).
    public void Build(
        Port a, Port b, float radius, float stubLength, Color color,
        float bendStrength = 0.4f, float sagPerMeter = 0f, float maxSag = 1.5f)
    {
        PortA = a;
        PortB = b;
        baseColor = color;

        // 1) Verlauf der Mittellinie berechnen.
        List<Vector3> path = BuildPath(
            a, b, stubLength, bendStrength, sagPerMeter, maxSag, radius);

        // 2) Aus der Mittellinie ein rundes Rohr formen.
        int sides = radius < 0.012f ? 6 : (radius < 0.03f ? CableSides : PipeSides);
        mesh = BuildTube(path, radius, sides);

        // 3) Mesh und Material anbringen.
        material = new Material(GetBaseMaterial());
        material.color = color;

        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
    }

    // Macht aus der Leitung ein Kabel mit Leistungsgrenze.
    public void ConfigureCable(float ratingKW, float tripSeconds)
    {
        RatingKW = ratingKW;
        tripSecondsAtDouble = tripSeconds;
    }

    // Das Stromnetz meldet hier die Leistung, die gerade durchfliesst.
    public void ReportFlow(float kw)
    {
        reportedFlow = kw;
    }

    private void Update()
    {
        // Wird eine der Maschinen entfernt, verschwindet die Leitung.
        if (PortA == null || PortB == null)
        {
            Destroy(gameObject);
            return;
        }

        // Rohre ohne Leistungsgrenze sind hier fertig.
        if (RatingKW <= 0f)
            return;

        // Gemeldete Last uebernehmen und fuer das naechste Bild zuruecksetzen.
        LoadKW = reportedFlow;
        reportedFlow = 0f;

        // Waermemodell: Ueber der Grenze heizt sich das Kabel auf, darunter kuehlt es ab.
        Heat = Overload.Step(
            Heat, LoadKW, RatingKW,
            tripSecondsAtDouble, coolSeconds, Time.deltaTime
        );

        // Je heisser, desto roter.
        if (material != null)
        {
            material.color = Color.Lerp(
                baseColor,
                new Color(1f, 0.15f, 0.05f),
                Heat
            );
        }

        // Voll aufgeheizt: Kabel brennt durch.
        if (Heat >= 1f)
        {
            // Im Mehrspieler entscheidet der Host. Mitspieler warten auf seinen Befehl.
            if (NetSync.Mode == NetMode.Client)
            {
                Heat = 1f;
                return;
            }

            Debug.LogWarning(
                "Kabel durchgebrannt! (" + LoadKW.ToString("F1")
                + " kW bei " + RatingKW.ToString("F0") + " kW)",
                this
            );

            PowerGrid.LogEvent(Loc.T("grid.event.cable",
                LoadKW.ToString("F1"), RatingKW.ToString("F0")), 2);

            // OnDestroy trennt die Ports, das Netz baut sich neu auf.
            NetSync.RemovePipe(this);
        }
    }

    // ---------- Verlauf ----------

    // Liefert die Punkte der Mittellinie von Port A nach Port B.
    private static List<Vector3> BuildPath(
        Port a, Port b, float stubLength, float bendStrength,
        float sagPerMeter, float maxSag, float radius)
    {
        Vector3 start = a.transform.position;
        Vector3 end = b.transform.position;

        // Richtung, in die jeder Port zeigt (blaue Z-Achse).
        Vector3 dirA = a.transform.forward;
        Vector3 dirB = b.transform.forward;

        // Kurzes gerades Stueck aus jedem Port heraus.
        Vector3 curveStart = start + dirA * stubLength;
        Vector3 curveEnd = end + dirB * stubLength;

        // Die beiden "Griffe" der Kurve: Sie ziehen die Linie aus den Ports
        // heraus in deren Blickrichtung. Je laenger, desto weiter der Bogen.
        float span = Vector3.Distance(curveStart, curveEnd);
        float handle = Mathf.Max(0.05f, span * Mathf.Clamp(bendStrength, 0.1f, 0.8f));
        Vector3 handleA = curveStart + dirA * handle;
        Vector3 handleB = curveEnd + dirB * handle;

        // Je laenger die Kurve, desto mehr Zwischenpunkte.
        int segments = Mathf.Clamp(Mathf.CeilToInt(span * 8f), 8, 48);

        // Wie tief die Leitung in der Mitte durchhaengt: Je weiter die waagerechte
        // Strecke, desto mehr. Senkrechte Leitungen haengen nicht durch.
        Vector3 flat = curveEnd - curveStart;
        flat.y = 0f;
        float sagDepth = Mathf.Min(flat.magnitude * sagPerMeter, maxSag);

        List<Vector3> path = new List<Vector3>();
        AddPoint(path, start);

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 point = Bezier(curveStart, handleA, handleB, curveEnd, t);

            if (sagDepth > 0.001f)
                point = ApplySag(point, t, sagDepth, radius);

            AddPoint(path, point);
        }

        AddPoint(path, end);
        return path;
    }

    // Senkt einen Punkt der Kurve nach unten ab. In der Mitte (t = 0.5) ist die
    // Absenkung am groessten, an den Enden null, dort sitzt die Leitung am Stecker.
    private static Vector3 ApplySag(Vector3 point, float t, float sagDepth, float radius)
    {
        // Mischung aus Parabel und weichem Sinus-Verlauf: haengt in der Mitte
        // durch und knickt am Stecker nicht sichtbar ab.
        float parabola = 4f * t * (1f - t);
        float s = Mathf.Sin(Mathf.PI * t);
        float weight = Mathf.Lerp(parabola, s * s, 0.5f);

        float drop = sagDepth * weight;

        if (drop <= 0.001f)
            return point;

        // Nicht in den Boden oder in Maschinen hineinhaengen: Die Leitung darf
        // hoechstens bis kurz ueber das Hindernis darunter absinken.
        float margin = radius + 0.03f;
        float free = FreeSpaceBelow(point, drop + margin) - margin;

        point.y -= Mathf.Clamp(free, 0f, drop);
        return point;
    }

    // Wie viel Platz ist unter dem Punkt bis zum naechsten festen Hindernis?
    // Spieler (und Mitspieler) zaehlen nicht, sonst wuerde die Leitung ueber
    // jemandem "haengen bleiben", der gerade darunter steht.
    private static float FreeSpaceBelow(Vector3 point, float maxDistance)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            point, Vector3.down, maxDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;

            if (col.GetComponentInParent<CharacterController>() != null ||
                col.GetComponentInParent<RemotePlayer>() != null)
                continue;

            if (hits[i].distance < nearest)
                nearest = hits[i].distance;
        }

        return nearest;
    }

    // Fuegt einen Punkt hinzu, aber nur wenn er nicht (fast) auf dem letzten liegt.
    private static void AddPoint(List<Vector3> path, Vector3 point)
    {
        if (path.Count > 0 &&
            (path[path.Count - 1] - point).sqrMagnitude < 0.000004f)
            return;

        path.Add(point);
    }

    // Punkt auf einer Bezier-Kurve (t = 0 am Anfang, 1 am Ende).
    private static Vector3 Bezier(
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;

        return u * u * u * p0
            + 3f * u * u * t * p1
            + 3f * u * t * t * p2
            + t * t * t * p3;
    }

    // ---------- Rohr-Mesh ----------

    // Baut ein rundes Rohr entlang der Punkte.
    // Pro Punkt entsteht ein Ring aus "sides" Eckpunkten. Zwischen zwei
    // Ringen werden Dreiecke gespannt.
    private static Mesh BuildTube(List<Vector3> path, float radius, int sides)
    {
        int rings = path.Count;
        int vertsPerRing = sides + 1; // letzte Spalte doppelt, damit die Textur-Naht sauber ist

        Vector3[] vertices = new Vector3[rings * vertsPerRing];
        Vector3[] normals = new Vector3[rings * vertsPerRing];
        Vector2[] uvs = new Vector2[rings * vertsPerRing];

        // Laufrichtung an jedem Punkt (Mittel aus Vorgaenger und Nachfolger).
        Vector3[] tangents = new Vector3[rings];

        for (int i = 0; i < rings; i++)
        {
            Vector3 before = path[Mathf.Max(i - 1, 0)];
            Vector3 after = path[Mathf.Min(i + 1, rings - 1)];
            tangents[i] = (after - before).normalized;
        }

        // Startrichtung fuer den Ring: irgendein Vektor quer zur Laufrichtung.
        Vector3 side = Vector3.Cross(tangents[0], Vector3.up);

        if (side.sqrMagnitude < 0.001f)
            side = Vector3.Cross(tangents[0], Vector3.right);

        side.Normalize();

        float length = 0f;

        for (int i = 0; i < rings; i++)
        {
            if (i > 0)
            {
                // Den Ring mit der Kurve mitdrehen, damit er sich nicht verdreht.
                side = Quaternion.FromToRotation(tangents[i - 1], tangents[i]) * side;
                length += Vector3.Distance(path[i - 1], path[i]);
            }

            Vector3 up = Vector3.Cross(tangents[i], side).normalized;

            for (int j = 0; j <= sides; j++)
            {
                float angle = (float)j / sides * Mathf.PI * 2f;

                // Richtung vom Mittelpunkt zum Rand des Rohrs.
                Vector3 offset = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);

                int index = i * vertsPerRing + j;
                vertices[index] = path[i] + offset * radius;
                normals[index] = offset;
                uvs[index] = new Vector2((float)j / sides, length / (radius * 4f));
            }
        }

        // Dreiecke zwischen je zwei Ringen (zwei pro Viereck).
        int[] triangles = new int[(rings - 1) * sides * 6];
        int t = 0;

        for (int i = 0; i < rings - 1; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int a = i * vertsPerRing + j;
                int b = a + 1;
                int c = a + vertsPerRing;
                int d = c + 1;

                triangles[t++] = a;
                triangles[t++] = b;
                triangles[t++] = c;

                triangles[t++] = b;
                triangles[t++] = d;
                triangles[t++] = c;
            }
        }

        Mesh result = new Mesh();
        result.name = "PipeMesh";
        result.vertices = vertices;
        result.normals = normals;
        result.uv = uvs;
        result.triangles = triangles;
        result.RecalculateBounds();
        return result;
    }

    // Leiht sich das Standard-Material (so passt es zu URP und zur normalen Pipeline).
    private static Material GetBaseMaterial()
    {
        if (sharedBaseMaterial == null)
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sharedBaseMaterial = temp.GetComponent<Renderer>().sharedMaterial;
            Destroy(temp);
        }

        return sharedBaseMaterial;
    }

    private void OnDestroy()
    {
        // Verbindungen loesen, damit die Ports wieder frei sind.
        if (PortA != null)
            PortA.Disconnect();

        if (PortB != null)
            PortB.Disconnect();

        // Selbst erzeugte Objekte aufraeumen.
        if (material != null)
            Destroy(material);

        if (mesh != null)
            Destroy(mesh);
    }
}
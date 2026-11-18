using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  TrackSpec: ALLE Masse der Gleise an einer Stelle.
//  Alles in Metern. Wer ein eigenes Gleisstueck modelliert, haelt sich an diese Werte
//  (siehe TrackManager.customPiece).
//
//  Querschnitt (Blick entlang des Gleises, Y = oben):
//
//            Schienenkopf-Oberkante   y = 0.39
//          ┌─┐                 ┌─┐
//          │ │    Spur 1,0 m   │ │     Schienen
//   ───────┴─┴─────────────────┴─┴───────  Schwelle: y = 0.20 .. 0.30, Laenge 1,5 m
//        ╱                           ╲     Schotter: Oberkante y = 0.20
//       ╱_____________________________╲    Schotter-Unterkante y = -0.10 (im Boden)
//
//  Y = 0 ist die Hoehe des eingeebneten Gelaendes.
// =====================================================================
public static class TrackSpec
{
    // Abstand der beiden Schienen-INNENkanten (Meter). 1,0 m passt gut zu kleinen Dampfloks.
    public const float Gauge = 1.0f;

    public const float RailHeadWidth = 0.07f;
    public const float RailHeight = 0.09f;

    public const float SleeperLength = 1.5f;
    public const float SleeperWidth = 0.16f;
    public const float SleeperHeight = 0.10f;
    public const float SleeperSpacing = 0.5f;

    public const float BallastHeight = 0.20f;     // Oberkante des Schotters ueber dem Boden
    public const float BallastSink = 0.10f;       // so tief reicht der Schotter in den Boden
    public const float BallastHalfTop = 0.85f;    // halbe Breite oben
    public const float BallastHalfBottom = 1.15f; // halbe Breite unten

    // Laenge eines eigenen Gleisstuecks (Meter). Es wird alle PieceLength Meter gesetzt.
    public const float PieceLength = 1.0f;

    // Obergrenze der Schienen ueber dem Boden.
    public static float RailTop { get { return BallastHeight + SleeperHeight + RailHeight; } }

    // Regeln fuer den Bau.
    public const float MinRadius = 8f;       // engste Kurve (Meter)
    public const float MaxGrade = 0.08f;     // groesste Steigung (0,08 = 8 %)
    public const float MinEdgeLength = 1.5f; // kuerzestes Stueck (auch der kleinste Abstand einer Weiche zum Ende eines Gleises)
    public const float MaxEdgeLength = 60f;  // laengstes Stueck
}

// Ein Punkt im Gleisnetz. Hier treffen sich ein, zwei oder drei Gleisstuecke.
//  1 Stueck  = freies Ende (Prellbock)
//  2 Stuecke = normale Verbindung
//  3 Stuecke = WEICHE: "stem" ist das Gleis auf der Einzelseite, die beiden anderen sind die
//              Abzweige. "active" ist der Abzweig, auf den die Weiche gestellt ist.
public class TrackNode
{
    public int id;
    public Vector3 pos;
    public int edgeA = -1;
    public int edgeB = -1;
    public int edgeC = -1;

    public int stem = -1;
    public int active = -1;

    // Drehscheibe: Ist dieser Punkt ein Ausgang am Grubenrand, steht hier die Nummer der Drehscheibe
    // (sonst -1) und die Richtung (Grad), in die ein Gleis von der Scheibe wegfuehrt.
    public int tt = -1;
    public float ttYaw;

    public int Degree
    {
        get { return (edgeA >= 0 ? 1 : 0) + (edgeB >= 0 ? 1 : 0) + (edgeC >= 0 ? 1 : 0); }
    }

    public bool IsSwitch { get { return Degree == 3; } }
}

// Ein Gleisstueck zwischen zwei Punkten: eine glatte Kurve (Bezier), die am Anfang und
// am Ende genau in der angegebenen Richtung verlaeuft.
public class TrackEdge
{
    public int id;
    public int a, b;                 // Anfangs- und Endpunkt (Node-Nummern)
    public float yawStart, yawEnd;   // Fahrtrichtung (Grad) am Anfang und Ende, von a nach b

    public float length;

    // Anlauf: So viele Meter (waagerecht) bleibt das Gleis am Anfang bzw. Ende auf der Hoehe des Punktes,
    // bevor es zur Hoehe des anderen Endes ansteigt oder faellt. Wird bei Weichen-Abzweigen gesetzt,
    // damit der Abzweig nicht sofort aus dem Hauptgleis heraus steigt und es schneidet.
    public float holdA, holdB;

    // Entladeschaechte: Bereiche (von Meter, bis Meter) entlang des Stuecks, in denen Schwellen und die Mitte des
    // Schotterbetts fehlen (darunter liegt ein Schacht fuer Foerderbaender). Nicht ueberlappend, nach Anfang sortiert.
    public List<Vector2> shafts = new List<Vector2>();

    // Liegt die Stelle (Meter) in einem Schacht (mit Rand)?
    public bool InShaft(float dist, float margin = 0f)
    {
        for (int i = 0; i < shafts.Count; i++)
        {
            if (dist > shafts[i].x - margin && dist < shafts[i].y + margin)
                return true;
        }

        return false;
    }

    // Abgetastete Kurve: Punkte, Richtungen und die bis dahin zurueckgelegte Strecke.
    public Vector3[] pts;
    public Vector3[] tans;
    public float[] cum;

    public static Vector3 YawToDir(float yaw)
    {
        float r = yaw * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
    }

    public static float DirToYaw(Vector3 d)
    {
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    // Berechnet die Kurve neu (nach dem Anlegen oder Laden).
    public void Build(Vector3 pa, Vector3 pb)
    {
        Vector3 ts = YawToDir(yawStart);
        Vector3 te = YawToDir(yawEnd);

        Vector3 flat = pb - pa;
        flat.y = 0f;
        float chord = flat.magnitude;

        // Winkel zwischen Start- und Endrichtung bestimmt, wie "rund" die Kurve wird.
        float theta = Mathf.Min(Vector3.Angle(ts, te), 170f) * Mathf.Deg2Rad;

        // Handle-Laenge, damit die Bezier-Kurve einen Kreisbogen sehr gut annaehert.
        float k = theta < 0.001f ? 1f / 3f
                : (4f / 3f) * Mathf.Tan(theta / 4f) / (2f * Mathf.Sin(theta / 2f));

        float h = chord * k;

        Vector3 p0 = pa;
        Vector3 p1 = pa + ts * h;  p1.y = pa.y;
        Vector3 p2 = pb - te * h;  p2.y = pb.y;
        Vector3 p3 = pb;

        const int steps = 160;

        pts = new Vector3[steps + 1];
        tans = new Vector3[steps + 1];
        cum = new float[steps + 1];

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float u = 1f - t;

            pts[i] = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;

            Vector3 d = 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
            tans[i] = d.sqrMagnitude > 1e-8f ? d.normalized : YawToDir(yawStart);

            cum[i] = i == 0 ? 0f : cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        }

        length = cum[steps];

        if (holdA > 0.01f || holdB > 0.01f)
            ApplyLevelRun(pa.y, pb.y);
    }

    // Formt das Hoehenprofil: erst eben (Anlauf), dann sanft zur Hoehe des anderen Endes.
    private void ApplyLevelRun(float ya, float yb)
    {
        int n = pts.Length;
        float[] sh = new float[n];

        for (int i = 1; i < n; i++)
        {
            float dx = pts[i].x - pts[i - 1].x, dz = pts[i].z - pts[i - 1].z;
            sh[i] = sh[i - 1] + Mathf.Sqrt(dx * dx + dz * dz);
        }

        float total = sh[n - 1];

        if (total < 1f)
            return;

        // Nie mehr als 40 % der Laenge pro Seite, damit fuer den Anstieg genug uebrig bleibt.
        float ha = Mathf.Min(holdA, total * 0.4f);
        float hb = Mathf.Min(holdB, total * 0.4f);
        float usable = Mathf.Max(1f, total - ha - hb);

        for (int i = 0; i < n; i++)
        {
            float t = Mathf.Clamp01((sh[i] - ha) / usable);
            float smooth = t * t * (3f - 2f * t);
            pts[i].y = Mathf.Lerp(ya, yb, smooth);
        }

        // Richtungen und Laengen passend zum neuen Profil.
        for (int i = 0; i < n; i++)
        {
            int lo = Mathf.Max(0, i - 1), hi = Mathf.Min(n - 1, i + 1);
            float run = sh[hi] - sh[lo];
            float slope = run > 1e-5f ? (pts[hi].y - pts[lo].y) / run : 0f;

            Vector3 h = new Vector3(tans[i].x, 0f, tans[i].z);
            h = h.sqrMagnitude > 1e-8f ? h.normalized : YawToDir(yawStart);

            tans[i] = (h + Vector3.up * slope).normalized;
            cum[i] = i == 0 ? 0f : cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        }

        length = cum[n - 1];
    }

    // Punkt und Richtung bei "dist" Metern vom Anfang.
    public void Evaluate(float dist, out Vector3 pos, out Vector3 tan)
    {
        dist = Mathf.Clamp(dist, 0f, length);

        int lo = 0;
        int hi = cum.Length - 1;

        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;

            if (cum[mid] <= dist) lo = mid; else hi = mid;
        }

        float span = cum[hi] - cum[lo];
        float f = span > 1e-6f ? (dist - cum[lo]) / span : 0f;

        pos = Vector3.Lerp(pts[lo], pts[hi], f);
        tan = Vector3.Slerp(tans[lo], tans[hi], f);
    }
}

// Position eines Wagens auf dem Gleis.
// dir: +1 = "vorne" des Wagens zeigt in Richtung wachsender dist, -1 = entgegengesetzt.
public struct TrackPos
{
    public int edge;
    public float dist;
    public int dir;
}

// Das Gleisnetz: Punkte, Stuecke und die Logik zum Fahren darauf.
public class TrackNetwork
{
    public readonly Dictionary<int, TrackNode> nodes = new Dictionary<int, TrackNode>();
    public readonly Dictionary<int, TrackEdge> edges = new Dictionary<int, TrackEdge>();

    // Zaehler fuer neue Nummern (werden mit gespeichert, damit alle Spieler dieselben Nummern vergeben).
    public int nextNode = 1;
    public int nextEdge = 1;

    // Die Bruecken der Drehscheiben (je ein Gleisstueck, das beim Drehen umgehaengt wird).
    // Sie haben keine eigene Gleis-Optik und gehoeren zur Drehscheibe.
    public readonly HashSet<int> bridgeEdges = new HashSet<int>();

    public void Clear()
    {
        nodes.Clear();
        edges.Clear();
        bridgeEdges.Clear();
        nextNode = 1;
        nextEdge = 1;
    }

    // ---------- Aufbau ----------

    public TrackNode AddNode(Vector3 pos, int forcedId = -1)
    {
        TrackNode n = new TrackNode();
        n.id = forcedId >= 0 ? forcedId : nextNode;
        n.pos = pos;

        nodes[n.id] = n;
        nextNode = Mathf.Max(nextNode, n.id + 1);

        return n;
    }

    public TrackEdge AddEdge(int a, int b, float yawStart, float yawEnd, int forcedId = -1)
    {
        TrackNode na, nb;

        if (!nodes.TryGetValue(a, out na) || !nodes.TryGetValue(b, out nb))
            return null;

        TrackEdge e = new TrackEdge();
        e.id = forcedId >= 0 ? forcedId : nextEdge;
        e.a = a;
        e.b = b;
        e.yawStart = yawStart;
        e.yawEnd = yawEnd;
        e.Build(na.pos, nb.pos);

        edges[e.id] = e;
        nextEdge = Mathf.Max(nextEdge, e.id + 1);

        Link(na, e.id);
        Link(nb, e.id);

        return e;
    }

    // Berechnet die Kurve eines Stuecks neu (z. B. nachdem ein Anlauf gesetzt wurde).
    public void RebuildEdge(TrackEdge e)
    {
        TrackNode na, nb;

        if (nodes.TryGetValue(e.a, out na) && nodes.TryGetValue(e.b, out nb))
            e.Build(na.pos, nb.pos);
    }

    private static void Link(TrackNode n, int edgeId)
    {
        if (n.edgeA < 0) n.edgeA = edgeId;
        else if (n.edgeB < 0) n.edgeB = edgeId;
        else n.edgeC = edgeId;
    }

    private static void Unlink(TrackNode n, int edgeId)
    {
        int a = n.edgeA, b = n.edgeB, c = n.edgeC;

        if (a == edgeId) a = -1;
        if (b == edgeId) b = -1;
        if (c == edgeId) c = -1;

        // Zusammenruecken: freie Plaetze nach hinten.
        List<int> rest = new List<int>(3);
        if (a >= 0) rest.Add(a);
        if (b >= 0) rest.Add(b);
        if (c >= 0) rest.Add(c);

        n.edgeA = rest.Count > 0 ? rest[0] : -1;
        n.edgeB = rest.Count > 1 ? rest[1] : -1;
        n.edgeC = rest.Count > 2 ? rest[2] : -1;

        // Keine Weiche mehr.
        if (n.Degree < 3)
        {
            n.stem = -1;
            n.active = -1;
        }
    }

    private static void Replace(TrackNode n, int oldEdge, int newEdge)
    {
        if (n.edgeA == oldEdge) n.edgeA = newEdge;
        else if (n.edgeB == oldEdge) n.edgeB = newEdge;
        else if (n.edgeC == oldEdge) n.edgeC = newEdge;

        if (n.stem == oldEdge) n.stem = newEdge;
        if (n.active == oldEdge) n.active = newEdge;
    }

    // Teilt ein Stueck bei "dist" Metern in zwei. Gibt den neuen Punkt zurueck (zwei Stuecke).
    // Die beiden neuen Stuecke haben dieselbe Richtung wie das alte an dieser Stelle.
    public TrackNode SplitEdge(int edgeId, float dist, out TrackEdge first, out TrackEdge second)
    {
        first = null;
        second = null;

        TrackEdge e;
        TrackNode na, nb;

        if (!edges.TryGetValue(edgeId, out e) ||
            !nodes.TryGetValue(e.a, out na) || !nodes.TryGetValue(e.b, out nb))
            return null;

        Vector3 pos, tan;
        e.Evaluate(dist, out pos, out tan);

        float yaw = TrackEdge.DirToYaw(tan);

        TrackNode p = AddNode(pos);

        first = new TrackEdge();
        first.id = nextEdge++;
        first.a = e.a;
        first.b = p.id;
        first.yawStart = e.yawStart;
        first.yawEnd = yaw;
        first.Build(na.pos, p.pos);

        second = new TrackEdge();
        second.id = nextEdge++;
        second.a = p.id;
        second.b = e.b;
        second.yawStart = yaw;
        second.yawEnd = e.yawEnd;
        second.Build(p.pos, nb.pos);

        // Schaechte mitnehmen (ein Schacht genau ueber der Teilstelle geht verloren).
        for (int i = 0; i < e.shafts.Count; i++)
        {
            Vector2 sh = e.shafts[i];

            if (sh.y <= dist)
                first.shafts.Add(sh);
            else if (sh.x >= dist)
                second.shafts.Add(new Vector2(sh.x - dist, sh.y - dist));
        }

        edges.Remove(edgeId);
        edges[first.id] = first;
        edges[second.id] = second;

        Replace(na, edgeId, first.id);
        Replace(nb, edgeId, second.id);

        p.edgeA = first.id;
        p.edgeB = second.id;

        return p;
    }

    // ---------- Weichen ----------

    // Das Gleis auf der Einzelseite der Weiche.
    public int StemOf(TrackNode n)
    {
        if (n.stem >= 0 && (n.edgeA == n.stem || n.edgeB == n.stem || n.edgeC == n.stem))
            return n.stem;

        return n.edgeA;
    }

    // Die zwei Abzweige einer Weiche.
    public void Branches(TrackNode n, out int first, out int second)
    {
        int stem = StemOf(n);

        first = -1;
        second = -1;

        int[] list = { n.edgeA, n.edgeB, n.edgeC };

        foreach (int id in list)
        {
            if (id < 0 || id == stem)
                continue;

            if (first < 0) first = id;
            else second = id;
        }
    }

    // Der Abzweig, auf den die Weiche gerade gestellt ist.
    public int ActiveBranch(TrackNode n)
    {
        int b0, b1;
        Branches(n, out b0, out b1);

        return n.active >= 0 && n.active == b1 ? b1 : b0;
    }

    // Stellt die Weiche um (edgeId < 0 = auf den anderen Abzweig).
    public bool SetSwitch(int nodeId, int edgeId)
    {
        TrackNode n;

        if (!nodes.TryGetValue(nodeId, out n) || !n.IsSwitch)
            return false;

        int b0, b1;
        Branches(n, out b0, out b1);

        int current = ActiveBranch(n);

        if (edgeId == b0 || edgeId == b1)
            n.active = edgeId;
        else
            n.active = current == b0 ? b1 : b0;

        return true;
    }

    // Richtung (Grad), in die ein Stueck von einem Punkt aus wegfuehrt.
    public float LeaveYaw(TrackNode n, TrackEdge e)
    {
        return e.a == n.id ? e.yawStart : e.yawEnd + 180f;
    }

    // Entfernt ein Stueck. Punkte ohne Gleis verschwinden mit.
    public void RemoveEdge(int id)
    {
        TrackEdge e;

        if (!edges.TryGetValue(id, out e))
            return;

        TrackNode na, nb;

        if (nodes.TryGetValue(e.a, out na)) Unlink(na, id);
        if (nodes.TryGetValue(e.b, out nb)) Unlink(nb, id);

        edges.Remove(id);

        // Ausgaenge einer Drehscheibe bleiben auch ohne Gleis bestehen.
        if (na != null && na.Degree == 0 && na.tt < 0) nodes.Remove(na.id);
        if (nb != null && nb.Degree == 0 && nb.tt < 0) nodes.Remove(nb.id);
    }

    // ---------- Drehscheibe ----------

    // Gleisstuecke (ausser der Bruecke) an einem Punkt.
    public int ExternalEdgeCount(TrackNode n)
    {
        int count = 0;

        if (n.edgeA >= 0 && !bridgeEdges.Contains(n.edgeA)) count++;
        if (n.edgeB >= 0 && !bridgeEdges.Contains(n.edgeB)) count++;
        if (n.edgeC >= 0 && !bridgeEdges.Contains(n.edgeC)) count++;

        return count;
    }

    // Legt die Bruecke an (gerade von Punkt a nach Punkt b) und haengt sie ein.
    public TrackEdge AddBridge(int a, int b, float yaw, int forcedId = -1)
    {
        TrackEdge e = AddEdge(a, b, yaw, yaw, forcedId);

        if (e != null)
            bridgeEdges.Add(e.id);

        return e;
    }

    // Loest die Bruecke von ihren Ausgaengen (beim Drehen). Gibt sie zurueck; sie bleibt eine Bruecke
    // und bleibt in "edges", damit Fahrzeuge darauf weiter ihren Platz haben.
    public TrackEdge DetachBridge(int edgeId)
    {
        TrackEdge e;

        if (!edges.TryGetValue(edgeId, out e))
            return null;

        TrackNode na, nb;

        if (nodes.TryGetValue(e.a, out na)) Unlink(na, edgeId);
        if (nodes.TryGetValue(e.b, out nb)) Unlink(nb, edgeId);

        // Die Bruecke bleibt im Gleisnetz (Fahrzeuge darauf drehen mit), ist aber an keinem Punkt mehr eingehaengt.
        return e;
    }

    // Haengt die Bruecke zwischen zwei anderen Ausgaengen wieder ein.
    public bool AttachBridge(TrackEdge e, int a, int b, float yaw)
    {
        TrackNode na, nb;

        if (e == null || !nodes.TryGetValue(a, out na) || !nodes.TryGetValue(b, out nb))
            return false;

        e.a = a;
        e.b = b;
        e.yawStart = yaw;
        e.yawEnd = yaw;
        e.Build(na.pos, nb.pos);

        edges[e.id] = e;
        Link(na, e.id);
        Link(nb, e.id);

        return true;
    }

    // Entfernt die Bruecke ganz (beim Abbau der Drehscheibe).
    public void RemoveBridge(int edgeId)
    {
        DetachBridge(edgeId);
        edges.Remove(edgeId);
        bridgeEdges.Remove(edgeId);
    }

    // ---------- Abfragen ----------

    // Das andere Stueck an einem Punkt (-1 = Gleisende).
    public int OtherEdge(int nodeId, int edgeId)
    {
        TrackNode n;

        if (!nodes.TryGetValue(nodeId, out n))
            return -1;

        // Ein Stueck, das an diesem Punkt gar nicht eingehaengt ist (Bruecke waehrend der Drehung),
        // endet hier: Fahrzeuge halten an, statt auf ein fremdes Gleis zu rutschen.
        if (n.edgeA != edgeId && n.edgeB != edgeId && n.edgeC != edgeId)
            return -1;

        // Weiche: Vom Einzelgleis geht es auf den gestellten Abzweig, aus einem Abzweig immer
        // zum Einzelgleis (auch wenn die Weiche anders steht).
        if (n.Degree == 3)
        {
            int stem = StemOf(n);
            return edgeId == stem ? ActiveBranch(n) : stem;
        }

        if (n.edgeA >= 0 && n.edgeA != edgeId) return n.edgeA;
        if (n.edgeB >= 0 && n.edgeB != edgeId) return n.edgeB;

        return -1;
    }

    // Naechster Punkt in der Naehe (nur freie Enden, wenn onlyOpen gesetzt ist).
    public TrackNode FindNode(Vector3 pos, float radius, bool onlyOpen)
    {
        TrackNode best = null;
        float bestDist = radius;

        foreach (TrackNode n in nodes.Values)
        {
            // Ausgang einer Drehscheibe: frei, solange noch kein Gleis (ausser der Bruecke) dran ist.
            if (onlyOpen && n.tt >= 0)
            {
                if (ExternalEdgeCount(n) > 0)
                    continue;
            }
            else if (onlyOpen && n.Degree >= 2)
                continue;

            Vector3 d = n.pos - pos;
            d.y = 0f;

            float dist = d.magnitude;

            if (dist < bestDist)
            {
                bestDist = dist;
                best = n;
            }
        }

        return best;
    }

    // Richtung (Grad), in die ein freies Ende weiterfuehren wuerde.
    // Gibt false zurueck, wenn der Punkt kein freies Ende mit genau einem Gleis ist.
    public bool OutwardYaw(TrackNode n, out float yaw)
    {
        yaw = 0f;

        // Ausgang einer Drehscheibe: Das Gleis fuehrt immer von der Scheibe weg.
        if (n.tt >= 0)
        {
            yaw = n.ttYaw;
            return ExternalEdgeCount(n) == 0;
        }

        if (n.Degree != 1)
            return false;

        TrackEdge e = edges[n.edgeA >= 0 ? n.edgeA : n.edgeB];

        yaw = e.a == n.id ? e.yawStart + 180f : e.yawEnd;
        return true;
    }

    // Naechster Gleispunkt zu einer Position: Stueck, Strecke und Abstand.
    public bool Project(Vector3 p, out int edgeId, out float dist, out float distance)
    {
        edgeId = -1;
        dist = 0f;
        distance = float.MaxValue;

        foreach (TrackEdge e in edges.Values)
        {
            for (int i = 0; i < e.pts.Length; i++)
            {
                float d = (e.pts[i] - p).sqrMagnitude;

                if (d < distance)
                {
                    distance = d;
                    edgeId = e.id;
                    dist = e.cum[i];
                }
            }
        }

        if (edgeId < 0)
            return false;

        distance = Mathf.Sqrt(distance);
        return true;
    }

    // ---------- Fahren ----------

    // Verschiebt eine Position um "amount" Meter nach vorne (negativ = rueckwaerts) und
    // folgt dabei dem Gleis ueber Punkte hinweg. Gibt false zurueck, wenn das Gleis
    // zu Ende ist (die Position bleibt dann am Ende stehen).
    public bool Move(ref TrackPos p, float amount)
    {
        for (int guard = 0; guard < 16; guard++)
        {
            TrackEdge e;

            if (!edges.TryGetValue(p.edge, out e))
                return false;

            int m = amount >= 0f ? 1 : -1;
            float s = amount * p.dir;
            float nd = p.dist + s;

            if (nd >= 0f && nd <= e.length)
            {
                p.dist = nd;
                return true;
            }

            bool atB = nd > e.length;
            float over = atB ? nd - e.length : -nd;
            int nodeId = atB ? e.b : e.a;
            int other = OtherEdge(nodeId, e.id);

            if (other < 0)
            {
                p.dist = atB ? e.length : 0f;
                return false;
            }

            TrackEdge e2 = edges[other];
            p.edge = other;

            if (e2.a == nodeId)
            {
                p.dist = 0f;
                p.dir = m;
            }
            else
            {
                p.dist = e2.length;
                p.dir = -m;
            }

            amount = m * over;
        }

        return false;
    }

    // Punkt und Fahrtrichtung (nach vorne) einer Position.
    public bool Pose(TrackPos p, out Vector3 pos, out Vector3 forward)
    {
        TrackEdge e;
        pos = Vector3.zero;
        forward = Vector3.forward;

        if (!edges.TryGetValue(p.edge, out e))
            return false;

        Vector3 tan;
        e.Evaluate(p.dist, out pos, out tan);
        forward = tan * p.dir;

        return true;
    }
}
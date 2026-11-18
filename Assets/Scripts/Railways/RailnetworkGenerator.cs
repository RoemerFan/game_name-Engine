using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  Vorgebaute Strecken zwischen den Bahnhoefen der Doerfer.
//
//  So funktioniert es:
//   - Nur der Host (bzw. das Einzelspiel) arbeitet hier. Alles, was gebaut wird, laeuft ueber die
//     normalen Gleisbau-Kommandos (NetCommand.TrackBuild). Mitspieler bekommen die Gleise also wie
//     vom Spieler gebaut; gespeichert wird es auch wie normale Gleise.
//   - Kommt ein Spieler in die Naehe eines Dorfes mit Bahnhof, wird dessen Stationsgleis gebaut
//     (VillagePlan.stopA .. stopB). Dann wird es mit den naechsten Nachbar-Bahnhoefen verbunden.
//   - Die Strecke wird vorher geplant: eine Wegsuche (A*) ueber das natuerliche Gelaende. Sie haelt die
//     Bauregeln ein (Steigung, engste Kurve, Stuecklaenge), meidet Doerfer und bestehende Gleise und
//     darf das Gelaende bis "Rail Max Cut Fill" einschneiden bzw. aufschuetten.
//   - Die Strecke besteht aus Kreisbogen-Stuecken, die tangential aneinander anschliessen
//     (genau wie wenn man von Hand immer vom freien Ende aus weiterbaut).
//   - Jedes Paar wird nur einmal versucht (auch wenn es keinen Weg gibt) und im Spielstand vermerkt.
//
//  Einstellungen: im Inspector beim TrackManager unter "Vorgebaute Strecken".
// =====================================================================
public class RailNetworkGenerator
{
    // ---------- Gemerkter Zustand (wird mit dem Spielstand gespeichert) ----------

    // Doerfer, deren Stationsgleis schon gebaut (oder bewusst uebersprungen) wurde.
    static readonly HashSet<int> stationsDone = new HashSet<int>();

    // Paare "a:b" (a < b), die schon gebaut oder verworfen wurden.
    static readonly HashSet<string> linksDone = new HashSet<string>();

    public static void Reset()
    {
        stationsDone.Clear();
        linksDone.Clear();
    }

    public static void Save(StateBag bag)
    {
        if (bag == null)
            return;

        bag.SetString("rg.st", string.Join(",", ToStrings(stationsDone)));

        List<string> links = new List<string>(linksDone);
        bag.SetString("rg.ln", string.Join(";", links.ToArray()));
    }

    public static void Load(StateBag bag)
    {
        Reset();

        if (bag == null)
            return;

        if (bag.Has("rg.st"))
        {
            foreach (string t in bag.GetString("rg.st").Split(','))
            {
                int id;

                if (int.TryParse(t, System.Globalization.NumberStyles.Integer,
                                 System.Globalization.CultureInfo.InvariantCulture, out id))
                    stationsDone.Add(id);
            }
        }

        if (bag.Has("rg.ln"))
        {
            foreach (string t in bag.GetString("rg.ln").Split(';'))
            {
                if (t.Length > 2)
                    linksDone.Add(t);
            }
        }
    }

    static string[] ToStrings(HashSet<int> set)
    {
        List<string> list = new List<string>();

        foreach (int i in set)
            list.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return list.ToArray();
    }

    static string LinkKey(int a, int b)
    {
        int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
        return lo.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
               hi.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------- Ablauf ----------

    // Kein eigenes Objekt: Der TrackManager ruft Tick() in seinem Update auf und startet die Coroutinen.
    // (So gibt es keine Probleme mit Dateinamen und Komponenten.)
    bool busy;
    float nextTick;
    float nextStatus;
    bool announced;

    public void Tick(TrackManager tm)
    {
        if (busy || Time.time < nextTick || tm == null)
            return;

        nextTick = Time.time + 1f;

        VillageSystem vs = VillageSystem.Instance;
        VoxelWorld world = VoxelWorld.Instance;

        if (vs == null || world == null || !world.IsReady || !tm.generateRailNetwork)
            return;

        // Mitspieler bauen nichts selbst: Der Host entscheidet und verteilt die Kommandos.
        if (NetSync.IsClient)
            return;

        List<Vector3> players = PlayerPositions(tm);

        if (!announced)
        {
            announced = true;
            Debug.Log("[Bahn] Streckenbauer ist aktiv.");
        }

        if (players.Count == 0)
        {
            if (Time.time >= nextStatus)
            {
                nextStatus = Time.time + 15f;
                Debug.LogWarning("[Bahn] Kein Spieler gefunden Beim TrackManager im Inspector die Kamera bei 'Player Camera' eintragen. Es wird nichts gebaut.");
            }

            return;
        }

        // Alle 15 Sekunden ein Lagebericht, damit man sieht, warum (noch) nichts passiert.
        if (Time.time >= nextStatus)
        {
            nextStatus = Time.time + 15f;

            int withStop = 0;
            float nearest = float.MaxValue;

            foreach (VillagePlan v in vs.KnownVillages)
            {
                if (!v.hasStop)
                    continue;

                withStop++;
                nearest = Mathf.Min(nearest, NearestPlayer(players, v.center));
            }

            Debug.Log("[Bahn] Doerfer bekannt: " + vs.KnownVillages.Count + ", davon mit Bahnhof: " + withStop +
                      (withStop > 0 ? ", naechster " + Mathf.RoundToInt(nearest) + " m entfernt (Ausloese-Abstand " +
                                      Mathf.RoundToInt(tm.railTriggerDistance) + " m)" : ""));
        }

        tm.StartCoroutine(Work(tm, vs, world, players));
    }

    static List<Vector3> PlayerPositions(TrackManager tm)
    {
        List<Vector3> list = new List<Vector3>();
        Camera cam = tm.PlayerCamera;

        if (cam != null)
            list.Add(cam.transform.position);

        foreach (RemotePlayer rp in Object.FindObjectsOfType<RemotePlayer>())
        {
            if (rp != null)
                list.Add(rp.transform.position);
        }

        return list;
    }

    static float NearestPlayer(List<Vector3> players, Vector3 p)
    {
        float best = float.MaxValue;

        foreach (Vector3 q in players)
        {
            float dx = q.x - p.x, dz = q.z - p.z;
            best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
        }

        return best;
    }

    // Sucht ein Dorf mit Bahnhof in der Naehe eines Spielers, dem noch Strecken fehlen, und baut sie.
    IEnumerator Work(TrackManager tm, VillageSystem vs, VoxelWorld world, List<Vector3> players)
    {
        busy = true;
        IEnumerator inner = WorkInner(tm, vs, world, players);

        while (true)
        {
            bool more;

            // Ein Fehler darf den Streckenbauer nicht dauerhaft blockieren.
            try
            {
                more = inner.MoveNext();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Bahn] Fehler beim Streckenbau: " + ex);
                break;
            }

            if (!more)
                break;

            yield return null;
        }

        busy = false;
    }

    IEnumerator WorkInner(TrackManager tm, VillageSystem vs, VoxelWorld world, List<Vector3> players)
    {
        List<VillagePlan> villages = new List<VillagePlan>(vs.KnownVillages);

        foreach (VillagePlan v in villages)
        {
            if (!v.hasStop || NearestPlayer(players, v.center) > tm.railTriggerDistance)
                continue;

            bool didWork = false;

            // 1) Das Stationsgleis.
            if (!stationsDone.Contains(v.id))
            {
                BuildStation(tm, v);
                didWork = true;
                yield return null;
            }

            // 2) Die Nachbarn: alle Doerfer in Reichweite planen lassen, dann die naechsten Bahnhoefe nehmen.
            List<VillagePlan> around = vs.FindVillagesAround(v.center, tm.railMaxLinkDistance);
            List<VillagePlan> candidates = new List<VillagePlan>();

            foreach (VillagePlan w in around)
            {
                if (w != v && w.hasStop)
                    candidates.Add(w);
            }

            // (FindVillagesAround sortiert schon nach Entfernung zur Mitte dieses Dorfes.)
            int wanted = Mathf.Clamp(tm.railLinksPerStation, 1, 2);
            // Schon belegte Enden des Stationsgleises zaehlen als Verbindung.
            int linked = (EndUsed(tm, v.stopA) ? 1 : 0) + (EndUsed(tm, v.stopB) ? 1 : 0);
            int attempts = 0;

            foreach (VillagePlan w in candidates)
            {
                if (linked >= wanted || attempts >= 6)
                    break;

                string key = LinkKey(v.id, w.id);

                if (linksDone.Contains(key))
                    continue;

                attempts++;
                didWork = true;
                linksDone.Add(key);

                if (!stationsDone.Contains(w.id))
                {
                    BuildStation(tm, w);
                    yield return null;
                }

                PathSearch search = null;
                string reason = PrepareSearch(tm, vs, world, v, w, out search);

                if (search == null)
                {
                    Debug.Log("[Bahn] Keine Strecke " + v.name + " - " + w.name + ": " + reason);
                    continue;
                }

                // Wegsuche in kleinen Haeppchen, damit das Spiel nicht ruckelt.
                while (search.Step(300) == PathSearch.Status.Running)
                    yield return null;

                if (search.status != PathSearch.Status.Found)
                {
                    Debug.Log("[Bahn] Kein Weg " + v.name + " - " + w.name + " gefunden (" + search.expanded +
                              " Zustaende geprueft).");
                    continue;
                }

                string error = ValidateChain(tm, search);

                if (error != null)
                {
                    Debug.Log("[Bahn] Strecke " + v.name + " - " + w.name + " verworfen: " + error);
                    continue;
                }

                // Bauen: wenige Stuecke pro Bild.
                IEnumerator build = BuildChain(tm, search, v, w);

                while (build.MoveNext())
                    yield return null;

                linked++;
            }

            // Pro Durchgang nur ein Dorf bearbeiten, an dem etwas zu tun war; dann wieder ruhig.
            if (didWork)
                break;
        }
    }

    static bool EndUsed(TrackManager tm, Vector3 pos)
    {
        TrackNode n = tm.Network.FindNode(pos, 0.6f, false);
        return n != null && n.Degree >= 2;
    }

    // ---------- Stationsgleis ----------

    void BuildStation(TrackManager tm, VillagePlan v)
    {
        stationsDone.Add(v.id);

        if (tm.Network.FindNode(v.stopA, 1f, false) != null || tm.Network.FindNode(v.stopB, 1f, false) != null)
            return;

        TrackManager.TrackPlan plan = tm.Plan(v.stopA, false, 0f, v.stopB, false, 0f);

        if (!plan.valid || tm.CrossesTracks(plan.curve, v.stopA, v.stopB))
        {
            Debug.Log("[Bahn] Stationsgleis in " + v.name + " nicht moeglich: " + plan.error);
            return;
        }

        NetSync.Submit(NetCommand.TrackBuild(0, v.stopA, 0, v.stopB));
    }

    // ---------- Wegsuche vorbereiten ----------

    // Waehlt die Enden und startet die Suche. Gibt null + Grund zurueck, wenn es nicht geht.
    static string PrepareSearch(TrackManager tm, VillageSystem vs, VoxelWorld world,
                                VillagePlan v, VillagePlan w, out PathSearch search)
    {
        search = null;

        Vector3 dirToW = w.center - v.center;
        dirToW.y = 0f;

        Vector3 posV, posW;
        float outV, outW;

        if (!PickEnd(tm, v, dirToW, out posV, out outV))
            return "kein freies Ende am Stationsgleis von " + v.name;

        if (!PickEnd(tm, w, -dirToW, out posW, out outW))
            return "kein freies Ende am Stationsgleis von " + w.name;

        PathSearch s = new PathSearch();
        s.tm = tm;
        s.world = world;
        s.start = posV;
        s.startYaw = outV;
        s.goal = posW;
        s.goalYaw = outW + 180f;         // Ankunft: entgegen der Richtung, in die das Ende zeigt
        s.villageA = v;
        s.villageB = w;
        s.villages = new List<VillagePlan>(vs.KnownVillages);
        s.Init();

        search = s;
        return null;
    }

    // Das Ende des Stationsgleises, das am besten zur Richtung "toward" passt und noch frei ist.
    static bool PickEnd(TrackManager tm, VillagePlan v, Vector3 toward, out Vector3 pos, out float outwardYaw)
    {
        pos = Vector3.zero;
        outwardYaw = 0f;

        Vector3[] ends = { v.stopA, v.stopB };
        float bestScore = float.MinValue;
        bool found = false;

        for (int i = 0; i < 2; i++)
        {
            TrackNode n = tm.Network.FindNode(ends[i], 0.6f, true);
            float yaw;

            if (n == null || !tm.Network.OutwardYaw(n, out yaw))
                continue;

            float score = Vector3.Dot(TrackEdge.YawToDir(yaw), toward.normalized);

            if (score > bestScore)
            {
                bestScore = score;
                pos = n.pos;
                outwardYaw = yaw;
                found = true;
            }
        }

        return found;
    }

    // ---------- Pruefen und Bauen ----------

    // Rechnet die ganze Strecke mit den echten Bauregeln durch, bevor das erste Stueck gebaut wird.
    static string ValidateChain(TrackManager tm, PathSearch s)
    {
        Vector3 a = s.start;
        float yaw = s.startYaw;

        for (int i = 0; i < s.waypoints.Count; i++)
        {
            Vector3 b = s.waypoints[i];
            TrackManager.TrackPlan plan = tm.Plan(a, true, yaw, b, false, 0f);

            if (!plan.valid)
                return plan.error;

            if (tm.CrossesTracks(plan.curve, a, b))
                return "track.err.occupied";

            yaw = plan.yawEnd;
            a = b;
        }

        TrackManager.TrackPlan last = tm.Plan(a, true, yaw, s.goal, true, s.goalYaw);

        if (!last.valid)
            return last.error;

        if (tm.CrossesTracks(last.curve, a, s.goal))
            return "track.err.occupied";

        return null;
    }

    IEnumerator BuildChain(TrackManager tm, PathSearch s, VillagePlan v, VillagePlan w)
    {
        List<Vector3> points = new List<Vector3>(s.waypoints);
        points.Add(s.goal);

        Vector3 a = s.start;
        float length = 0f;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 b = points[i];

            NetSync.Submit(NetCommand.TrackBuild(0, a, 0, b));

            length += Vector3.Distance(a, b);
            a = b;

            if (tm.Network.FindNode(b, 0.6f, false) == null)
            {
                Debug.LogWarning("[Bahn] Strecke " + v.name + " - " + w.name + " bricht bei Stueck " + (i + 1) +
                                 " ab (Bau abgelehnt).");
                yield break;
            }

            // Zwei Stuecke pro Bild (Einebnen und Mesh-Bau brauchen etwas Zeit).
            if (i % 2 == 1)
                yield return null;
        }

        Debug.Log("[Bahn] Strecke " + v.name + " - " + w.name + " gebaut (" + points.Count + " Stuecke, ca. " +
                  Mathf.RoundToInt(length) + " m).");
    }

    // =====================================================================
    //  Wegsuche (A*) ueber das natuerliche Gelaende
    //
    //  Ein Zustand ist Ort + Fahrtrichtung + Gleishoehe. Von dort geht es mit einem Bogenstueck
    //  (Sehne "step" Meter, leicht links oder rechts) weiter. Die Gleishoehe folgt dem Gelaende,
    //  darf aber pro Stueck nur so viel steigen, wie die Steigungsgrenze erlaubt, und muss das Ziel
    //  rechtzeitig erreichen koennen.
    // =====================================================================
    public class PathSearch
    {
        public enum Status { Running, Found, Failed }

        public TrackManager tm;
        public VoxelWorld world;
        public Vector3 start, goal;
        public float startYaw, goalYaw;
        public VillagePlan villageA, villageB;
        public List<VillagePlan> villages;

        public Status status = Status.Running;
        public int expanded;
        public List<Vector3> waypoints = new List<Vector3>();   // Zwischenpunkte (ohne Start und Ziel)

        class State
        {
            public float x, z, y, yaw, g, f;
            public int parent;
        }

        readonly List<State> states = new List<State>();
        readonly List<int> heap = new List<int>();
        readonly Dictionary<long, float> bestG = new Dictionary<long, float>();
        readonly HashSet<long> trackCells = new HashSet<long>();

        float step = 18f;                 // Sehne eines Bogenstuecks (Meter)
        float cell = 8f;                  // Raster fuer "schon besucht"
        float grade;                      // erlaubte Steigung (0,06 = 6 %)
        float maxCutFill;
        int budget;

        static readonly float[] turns = { -36f, -24f, -12f, 0f, 12f, 24f, 36f };

        public void Init()
        {
            grade = Mathf.Min(tm.railMaxGradePercent * 0.01f, TrackSpec.MaxGrade * 0.9f);
            maxCutFill = tm.railMaxCutFill;
            budget = Mathf.Max(500, tm.railSearchBudget);

            // Bestehende Gleise merken (4-m-Raster), damit die neue Strecke nicht hineinlaeuft.
            foreach (TrackEdge e in tm.Network.edges.Values)
            {
                if (e.pts == null || tm.Network.bridgeEdges.Contains(e.id))
                    continue;

                for (int i = 0; i < e.pts.Length; i += 3)
                    trackCells.Add(TrackCell(e.pts[i].x, e.pts[i].z));
            }

            State s = new State();
            s.x = start.x; s.z = start.z; s.y = start.y; s.yaw = startYaw;
            s.g = 0f; s.parent = -1;
            s.f = Heuristic(s);

            states.Add(s);
            Push(0);
            bestG[Key(s.x, s.z, s.yaw)] = 0f;
        }

        static long TrackCell(float x, float z)
        {
            int cx = Mathf.FloorToInt(x / 4f), cz = Mathf.FloorToInt(z / 4f);
            return ((long)cx << 32) | (uint)cz;
        }

        long Key(float x, float z, float yaw)
        {
            int cx = Mathf.RoundToInt(x / cell), cz = Mathf.RoundToInt(z / cell);
            int bin = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 15f) % 24;
            return (((long)cx & 0xFFFFF) << 40) | (((long)cz & 0xFFFFF) << 16) | (long)bin;
        }

        float Heuristic(State s)
        {
            float dx = goal.x - s.x, dz = goal.z - s.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            float climb = Mathf.Abs(s.y - goal.y) / Mathf.Max(0.01f, grade);

            // Etwas gierig (1,15), das beschleunigt die Suche deutlich.
            return Mathf.Max(dist, climb) * 1.15f;
        }

        // ---------- Halde (Binary Heap) ----------

        void Push(int i)
        {
            heap.Add(i);
            int c = heap.Count - 1;

            while (c > 0)
            {
                int p = (c - 1) / 2;

                if (states[heap[p]].f <= states[heap[c]].f)
                    break;

                int t = heap[p]; heap[p] = heap[c]; heap[c] = t;
                c = p;
            }
        }

        int Pop()
        {
            int top = heap[0];
            int last = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);

            if (heap.Count > 0)
            {
                heap[0] = last;
                int c = 0;

                while (true)
                {
                    int l = c * 2 + 1, r = l + 1, m = c;

                    if (l < heap.Count && states[heap[l]].f < states[heap[m]].f) m = l;
                    if (r < heap.Count && states[heap[r]].f < states[heap[m]].f) m = r;
                    if (m == c) break;

                    int t = heap[m]; heap[m] = heap[c]; heap[c] = t;
                    c = m;
                }
            }

            return top;
        }

        // ---------- Hindernisse ----------

        bool Blocked(float x, float z)
        {
            // Bestehende Gleise (ausser rund um Start und Ziel, dort schliesst die Strecke ja an).
            if (Flat(x - start.x, z - start.z) > 30f && Flat(x - goal.x, z - goal.z) > 30f)
            {
                int cx = Mathf.FloorToInt(x / 4f), cz = Mathf.FloorToInt(z / 4f);

                for (int i = -1; i <= 1; i++)
                    for (int j = -1; j <= 1; j++)
                    {
                        if (trackCells.Contains(((long)(cx + i) << 32) | (uint)(cz + j)))
                            return true;
                    }
            }

            foreach (VillagePlan v in villages)
            {
                if (v == villageA || v == villageB)
                {
                    // Das eigene Dorf: nicht durch Strassen und Gebaeude (Bahnsteig und Gleisbett sind frei).
                    if (Flat(x - v.center.x, z - v.center.z) < v.radius + 2f && InsideParts(v, x, z, 7f))
                        return true;
                }
                else if (Flat(x - v.center.x, z - v.center.z) < v.radius + 10f)
                {
                    return true;
                }
            }

            return false;
        }

        static float Flat(float dx, float dz)
        {
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Liegt der Punkt in einem Gebaeude oder auf einer Strasse des Dorfes (mit Abstand "inflate")?
        static bool InsideParts(VillagePlan v, float x, float z, float inflate)
        {
            foreach (VillagePart part in v.parts)
            {
                if (part.type == VillageSystem.PartSign || part.type == VillageSystem.PartPlatform ||
                    part.type == VillageSystem.PartRailBed)
                    continue;

                float r = part.yaw * Mathf.Deg2Rad;
                float c = Mathf.Cos(r), s = Mathf.Sin(r);

                // Laengsachse des Teils (Breite size.x): (cos, -sin); Querachse (size.y): (sin, cos).
                float dx = x - part.pos.x, dz = z - part.pos.z;
                float along = dx * c - dz * s;
                float across = dx * s + dz * c;

                if (Mathf.Abs(along) <= part.size.x * 0.5f + inflate && Mathf.Abs(across) <= part.size.y * 0.5f + inflate)
                    return true;
            }

            return false;
        }

        // ---------- Suche ----------

        // Macht bis zu "count" Schritte der Suche.
        public Status Step(int count)
        {
            if (status != Status.Running)
                return status;

            for (int n = 0; n < count; n++)
            {
                if (heap.Count == 0 || expanded >= budget)
                {
                    status = Status.Failed;
                    return status;
                }

                int index = Pop();
                State s = states[index];
                expanded++;

                // Ziel in Reichweite? Dann pruefen, ob das letzte Stueck mit beiden festen Richtungen geht.
                if (TryFinish(index, s))
                {
                    status = Status.Found;
                    return status;
                }

                Expand(index, s);
            }

            return Status.Running;
        }

        bool TryFinish(int index, State s)
        {
            float dist = Flat(goal.x - s.x, goal.z - s.z);

            if (dist > 58f || dist < TrackSpec.MinEdgeLength + 0.5f)
                return false;

            Vector3 from = new Vector3(s.x, s.y, s.z);
            Vector3 chord = goal - from;
            chord.y = 0f;
            float chordYaw = TrackEdge.DirToYaw(chord);

            if (Mathf.Abs(Mathf.DeltaAngle(s.yaw, chordYaw)) > 70f || Mathf.Abs(Mathf.DeltaAngle(chordYaw, goalYaw)) > 70f)
                return false;

            TrackManager.TrackPlan plan = tm.Plan(from, true, s.yaw, goal, true, goalYaw);

            if (!plan.valid)
                return false;

            // Weg zurueckverfolgen: alle Zustaende ausser dem Start sind Zwischenpunkte.
            List<Vector3> path = new List<Vector3>();
            int i = index;

            while (i > 0)
            {
                State t = states[i];
                path.Add(new Vector3(t.x, t.y, t.z));
                i = t.parent;
            }

            path.Reverse();
            waypoints = path;
            return true;
        }

        void Expand(int index, State s)
        {
            float goalDist = Flat(goal.x - s.x, goal.z - s.z);

            foreach (float turn in turns)
            {
                float bearing = s.yaw + turn;
                Vector3 dir = TrackEdge.YawToDir(bearing);

                float nx = s.x + dir.x * step;
                float nz = s.z + dir.z * step;
                float nyaw = s.yaw + 2f * turn;

                if (!world.IsInsideWorld(new Vector3(nx, 0f, nz), 15f))
                    continue;

                if (Blocked(nx, nz) || Blocked(s.x + dir.x * step * 0.5f, s.z + dir.z * step * 0.5f))
                    continue;

                // Gleishoehe: folgt dem Gelaende, begrenzt durch die Steigung und durch das Ziel.
                float natural = world.GetNaturalHeight(nx, nz);
                float maxDy = grade * step * 0.97f;
                float ny = Mathf.Clamp(natural, s.y - maxDy, s.y + maxDy);

                float dGoal = Flat(goal.x - nx, goal.z - nz);
                float reach = grade * Mathf.Max(0f, dGoal - 10f) * 0.95f;
                ny = Mathf.Clamp(ny, goal.y - reach, goal.y + reach);

                if (Mathf.Abs(ny - s.y) > maxDy + 0.001f)
                    continue;

                // Abweichung vom Gelaende: nicht nur in der Gleismitte, sondern auch 2,5 m daneben (dort liegen
                // Schotterbett und Uebergang). Quer zum Hang waere das Gleis sonst auf der Bergseite im Boden versenkt.
                Vector3 side = new Vector3(dir.z, 0f, -dir.x);
                float dev = 0f;

                for (int k = 1; k <= 2; k++)
                {
                    float f = k == 2 ? 1f : 0.5f;
                    float px = s.x + dir.x * step * f;
                    float pz = s.z + dir.z * step * f;
                    float py = Mathf.Lerp(s.y, ny, f);

                    dev = Mathf.Max(dev, Mathf.Abs(world.GetNaturalHeight(px, pz) - py));
                    dev = Mathf.Max(dev, Mathf.Abs(world.GetNaturalHeight(px + side.x * 2.5f, pz + side.z * 2.5f) - py));
                    dev = Mathf.Max(dev, Mathf.Abs(world.GetNaturalHeight(px - side.x * 2.5f, pz - side.z * 2.5f) - py));
                }

                if (dev > maxCutFill)
                    continue;

                // Kosten: Weg, Erdarbeiten, Hoehenaenderung, ein bisschen Abneigung gegen Kurven.
                float w = tm.railTerrainFollowing;
                float cost = step * (1f + w * dev + 0.5f * w * dev * dev) + Mathf.Abs(ny - s.y) * 4f + Mathf.Abs(turn) * 0.08f;
                float g = s.g + cost;

                long key = Key(nx, nz, nyaw);
                float old;

                if (bestG.TryGetValue(key, out old) && old <= g)
                    continue;

                bestG[key] = g;

                State t = new State();
                t.x = nx; t.z = nz; t.y = ny; t.yaw = nyaw;
                t.g = g; t.parent = index;
                t.f = g + Heuristic(t);

                states.Add(t);
                Push(states.Count - 1);
            }
        }
    }
}
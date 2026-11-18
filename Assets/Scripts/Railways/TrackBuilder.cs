using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  TrackBuilder: das Werkzeug zum Gleisbau. AN DEN SPIELER haengen (wie den PipeBuilder).
//
//  Bedienung:
//    K              Gleisbau ein/aus
//    Linksklick     1. Klick = Startpunkt, danach jeder Klick baut ein Stueck bis dorthin.
//                   Ein freies Gleisende (gruener Punkt) rastet ein: so setzt man Gleise fort.
//    Rechtsklick    Startpunkt verwerfen (zweiter Rechtsklick beendet den Gleisbau)
//                   Mitten auf ein Gleis klicken (blauer Punkt) = Weiche: Dort zweigt das neue
//                   Gleis ab. Den Hebel (F) neben der Weiche zum Umlegen benutzen.
//    J              Lok auf das anvisierte Gleis setzen
//    X              anvisiertes Gleis bzw. anvisierte Lok entfernen
//
//  Die Vorschau ist gruen, wenn man dort bauen darf, sonst rot (mit Begruendung).
// =====================================================================
public class TrackBuilder : MonoBehaviour
{
    [Header("Tasten")]
    public KeyCode toggleKey = KeyCode.K;
    public KeyCode locoKey = KeyCode.J;
    [Tooltip("Wechselt den Lok-Typ (Dampf, Diesel, Elektro ... aus der Liste im TrackManager).")]
    public KeyCode cycleLocoKey = KeyCode.N;
    public KeyCode removeKey = KeyCode.X;
    [Tooltip("Schaltet das Parallelgleis-Werkzeug ein/aus (Weichen und parallele Strecken).")]
    public KeyCode parallelKey = KeyCode.P;
    [Tooltip("Im Parallelgleis-Werkzeug: mit/ohne Weiche (Uebergang vom Hauptgleis).")]
    public KeyCode parallelSwitchKey = KeyCode.O;

    [Tooltip("Drehscheibe aufstellen (Platzhalter). Groesse und Ausgaenge: TrackManager, Bereich 'Drehscheibe'.")]
    public KeyCode turntableKey = KeyCode.U;
    public KeyCode shaftKey = KeyCode.M;

    [Header("Zielen")]
    public Camera playerCamera;
    public float maxDistance = 60f;
    [Tooltip("So nah muss man an einem freien Gleisende zielen, damit es einrastet (Meter).")]
    public float snapRadius = 2.5f;

    // Bau-Raster (Taste G im Bausystem): ist es an, rasten freie Punkte darauf ein.
    private BuildingSystem building;

    private bool active;
    private bool hasStart;
    private TrackNode startNode;
    private Vector3 startPoint;

    private LineRenderer line;

    // Vorschau des Gleises als echtes Gleis-Mesh (Schotter, Schwellen, Schienen), gruen bzw. rot.
    private float trackPreviewNext;
    private GameObject marker;
    private Material lineMaterial;

    // Parallelgleis-Werkzeug.
    private bool parallelMode;
    private bool parallelSwitch = true;
    private bool hasPStart;

    // Drehscheibe aufstellen.
    private bool ttMode;
    private bool shaftMode;

    // Rote Kugel in der Vorschau (zeigt, wo das geplante Gleis nicht geht).
    private GameObject problemMarker;
    private int pEdge;
    private float pDist;
    private int pSide;
    private int pAttach;          // > 0: das Parallelgleis wird an dieses freie Ende angeknuepft (Etappe)
    private readonly List<TrackManager.ParallelSeg> pSegs = new List<TrackManager.ParallelSeg>();
    private readonly List<Vector3> pPoints = new List<Vector3>();
    private readonly List<TrackEdge> pCurves = new List<TrackEdge>();
    private readonly List<bool> pValids = new List<bool>();

    private string message;
    private float messageUntil;

    // Ist irgendein Gleis-Werkzeug eingeschaltet? (Andere Skripte ignorieren dann das Steuerkreuz.)
    public static bool AnyActive { get; private set; }

    // Ist der Gleisbau gerade eingeschaltet?
    public bool IsActive { get { return active; } }

    void Start()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        building = FindObjectOfType<BuildingSystem>();
    }

    // Rastet einen Punkt am Bau-Raster ein (nur, wenn es an ist).
    private Vector3 GridSnap(TrackManager manager, Vector3 p)
    {
        if (building == null || !building.GridEnabled)
            return p;

        Vector3 snapped = building.SnapToGrid(p);

        if (manager.world != null)
            snapped.y = manager.world.GetSurfaceWorldY(snapped.x, snapped.z);

        return snapped;
    }

    void OnDisable()
    {
        SetActive(false);
    }

    private void SetActive(bool on)
    {
        active = on;
        AnyActive = on;
        hasStart = false;
        startNode = null;
        hasPStart = false;
        parallelMode = false;
        ttMode = false;
        shaftMode = false;

        if (line != null) line.enabled = false;
        HideTrackPreview();
        if (marker != null) marker.SetActive(false);
        ShowProblem(false, Vector3.zero);
    }

    // Schaltet den Gleisbau ein (z. B. aus dem Baumenue).
    public void Begin()
    {
        if (TrackManager.Instance == null)
        {
            Say(Loc.T("track.err.noManager"));
            return;
        }

        SetActive(true);
    }

    void Update()
    {
        if (SettingsMenu.IsOpen)
            return;

        // Baumenue geoeffnet: Gleisbau beenden.
        if (active && BuildingSystem.IsMenuOpen)
        {
            SetActive(false);
            return;
        }

        TrackManager manager = TrackManager.Instance;

        if (GameInput.Pressed(GameAction.TrackTool, toggleKey))
        {
            if (manager == null)
            {
                // Ohne TrackManager in der Szene kann man nicht bauen.
                Say(Loc.T("track.err.noManager"));
                Debug.LogWarning("TrackBuilder: Kein TrackManager in der Szene. " +
                                 "Bitte das Skript TrackManager an ein leeres GameObject haengen.", this);
            }
            else
            {
                SetActive(!active);
            }
        }

        if (!active || manager == null)
            return;

        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera == null)
            return;

        // Nur mit gefangener Maus bedienen (nicht in Menues).
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        Vector3 aim;
        bool hit = Aim(manager, out aim);

        // Mitspieler: Der Startpunkt kann erst nach dem Host-Kommando gefunden werden.
        if (hasStart && startNode == null)
            startNode = manager.Network.FindNode(startPoint, 0.3f, true);

        TrackNode snap = hit ? manager.Network.FindNode(aim, snapRadius, true) : null;

        // Bau-Raster: freie Punkte rasten am Raster ein. Freie Gleisenden und Stellen mitten
        // auf einem Gleis (Weiche) behalten Vorrang. Im Parallelgleis-Werkzeug bestimmt das Raster,
        // WO auf dem Gleis man klickt.
        if (hit)
        {
            if (parallelMode || ttMode)
                aim = GridSnap(manager, aim);
            else if (snap == null && manager.FindJoin(aim, 1.0f) == null)
                aim = GridSnap(manager, aim);
        }

        if (locked && GameInput.Pressed(GameAction.TrackParallel, parallelKey))
        {
            parallelMode = !parallelMode;
            ttMode = false;
            shaftMode = false;
            hasPStart = false;
            hasStart = false;
            startNode = null;
        }

        if (locked && parallelMode && GameInput.Pressed(GameAction.TrackParallelSwitch, parallelSwitchKey))
            parallelSwitch = !parallelSwitch;

        if (locked && GameInput.Pressed(GameAction.TrackTurntable, turntableKey))
        {
            ttMode = !ttMode;
            parallelMode = false;
            shaftMode = false;
            hasPStart = false;
            hasStart = false;
            startNode = null;
        }

        if (locked && GameInput.Pressed(GameAction.TrackShaft, shaftKey))
        {
            shaftMode = !shaftMode;
            ttMode = false;
            parallelMode = false;
            hasPStart = false;
            hasStart = false;
            startNode = null;
        }

        if (ttMode || parallelMode || shaftMode)
            HideTrackPreview();

        if (shaftMode)
            UpdateShaftPreview(manager, hit, aim);
        else if (ttMode)
            UpdateTurntablePreview(manager, hit, aim);
        else if (parallelMode)
            UpdateParallelPreview(manager, hit, aim);
        else
            UpdatePreview(manager, hit, aim, snap);

        // Fahrzeug-Typ wechseln: erst die Loks, dann die Wagen.
        if (locked && GameInput.Pressed(GameAction.TrackLocoType, cycleLocoKey) && manager.VehicleTypeCount > 0)
            manager.selectedLocomotive = (manager.selectedLocomotive + 1) % manager.VehicleTypeCount;

        if (!locked || !hit)
            return;

        if (GameInput.Pressed(GameAction.Secondary))
        {
            if (shaftMode)
            {
                shaftMode = false;
            }
            else if (ttMode)
            {
                ttMode = false;
            }
            else if (parallelMode)
            {
                // Erst die Auswahl verwerfen, dann das Parallelgleis-Werkzeug verlassen.
                if (hasPStart)
                    hasPStart = false;
                else
                    parallelMode = false;
            }
            else if (hasStart)
            {
                hasStart = false;
                startNode = null;
            }
            else
            {
                SetActive(false);
            }

            return;
        }

        if (GameInput.Pressed(GameAction.TrackLoco, locoKey))
            PlaceLoco(manager, aim);

        if (GameInput.Pressed(GameAction.Remove, removeKey))
            Remove(manager, aim);

        if (GameInput.Pressed(GameAction.Primary))
        {
            if (shaftMode)
                PlaceShaft(manager, aim);
            else if (ttMode)
                PlaceTurntable(manager, aim);
            else if (parallelMode)
                ClickParallel(manager, aim);
            else
                Click(manager, aim, snap);
        }
    }

    // ---------- Entladeschacht ----------

    private void PlaceShaft(TrackManager manager, Vector3 aim)
    {
        string error;

        if (!manager.RequestShaft(aim, out error))
            Say(Loc.T(error));
    }

    // Vorschau: eine Linie ueber die Laenge des Schachts (gruen = moeglich, rot = nicht moeglich).
    private void UpdateShaftPreview(TrackManager manager, bool hit, Vector3 aim)
    {
        EnsureVisuals();
        marker.SetActive(false);
        ShowProblem(false, Vector3.zero);

        int edge;
        float dist, distance;

        if (!hit || !manager.Network.Project(aim, out edge, out dist, out distance) || distance > 2.5f)
        {
            line.enabled = false;
            return;
        }

        string error;
        bool valid = manager.CheckShaft(edge, dist, out error);

        TrackEdge e = manager.Network.edges[edge];
        float half = Mathf.Clamp(manager.shaftLength, 2f, 12f) * 0.5f;
        float d0 = Mathf.Max(0f, dist - half), d1 = Mathf.Min(e.length, dist + half);

        const int segments = 12;

        line.enabled = true;
        line.positionCount = segments + 1;

        for (int i = 0; i <= segments; i++)
        {
            Vector3 pos, tan;
            e.Evaluate(Mathf.Lerp(d0, d1, (float)i / segments), out pos, out tan);
            line.SetPosition(i, pos + Vector3.up * 0.6f);
        }

        Color c = valid ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);
        line.startColor = c;
        line.endColor = c;

        if (!valid)
            Say(Loc.T(error));
    }

    // ---------- Drehscheibe ----------

    // Richtung des Ausgangs 0: Blickrichtung, auf 15 Grad gerundet.
    private float TurntableYaw()
    {
        Vector3 f = playerCamera.transform.forward;
        f.y = 0f;

        float yaw = f.sqrMagnitude > 1e-4f ? TrackEdge.DirToYaw(f) : 0f;
        return Mathf.Round(yaw / 15f) * 15f;
    }

    private void PlaceTurntable(TrackManager manager, Vector3 aim)
    {
        string error;

        if (manager.RequestTurntable(aim, TurntableYaw(), out error))
            ttMode = false;
        else
            Say(Loc.T(error));
    }

    // Vorschau: Kreis der Grube und die Bruecke in Richtung von Ausgang 0.
    private void UpdateTurntablePreview(TrackManager manager, bool hit, Vector3 aim)
    {
        EnsureVisuals();
        marker.SetActive(false);
        ShowProblem(false, Vector3.zero);

        if (!hit)
        {
            line.enabled = false;
            return;
        }

        float radius = Mathf.Clamp(manager.turntableRadius, 5f, 25f);
        float yaw = TurntableYaw();

        string error;
        bool valid = manager.CheckTurntable(aim, radius, out error);

        const int segments = 48;

        line.enabled = true;
        line.positionCount = segments + 2;

        for (int i = 0; i <= segments; i++)
        {
            Vector3 dir = TrackEdge.YawToDir(yaw + i * (360f / segments));
            line.SetPosition(i, aim + dir * radius + Vector3.up * 0.5f);
        }

        // Von Ausgang 0 quer durch die Mitte zum gegenueberliegenden Ausgang.
        line.SetPosition(segments + 1, aim - TrackEdge.YawToDir(yaw) * radius + Vector3.up * 0.5f);

        Color c = valid ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);
        line.startColor = c;
        line.endColor = c;

        if (!valid)
            Say(Loc.T(error));
    }

    // ---------- Parallelgleis / Weiche ----------

    // 1. Klick: Gleis anklicken (Startpunkt, die Seite ergibt sich aus der Zielrichtung).
    // 2. Klick: weiter vorne auf demselben Gleis (Ende). Dazwischen entsteht das Parallelgleis,
    //           mit Weiche zusaetzlich eine Uebergangskurve vom Hauptgleis.
    private void ClickParallel(TrackManager manager, Vector3 aim)
    {
        int edge = 0, side = 1;
        float dist = 0f;

        // Erster Klick auf ein freies Gleisende zaehlt auch ohne Gleis in 4 m Naehe (Anknuepfen).
        TrackNode probe;
        int pe, ps;
        float pd;
        bool attachable = !hasPStart && manager.FindParallelAttach(aim, out probe, out pe, out pd, out ps);

        if (!attachable && !manager.PickTrack(aim, 4f, out edge, out dist, out side))
        {
            Say(Loc.T("track.err.noTrack"));
            return;
        }

        if (!hasPStart)
        {
            hasPStart = true;
            pEdge = edge;
            pDist = dist;
            pSide = side;
            pAttach = 0;

            // Freies Ende eines Parallelgleises angeklickt? Dann dort weiterbauen (Etappe).
            TrackNode end;
            int mainEdge, mainSide;
            float mainDist;

            if (manager.FindParallelAttach(aim, out end, out mainEdge, out mainDist, out mainSide))
            {
                pEdge = mainEdge;
                pDist = mainDist;
                pSide = mainSide;
                pAttach = end.id;
            }

            return;
        }

        float dist2;
        string error;

        if (!manager.ResolveParallelEnd(pEdge, pDist, aim, out dist2, out error))
        {
            Say(Loc.T(error));
            return;
        }

        bool ok;

        if (pAttach > 0)
            ok = manager.RequestParallelExtend(manager.GetNode(pAttach), pEdge, pDist, dist2, pSide, out error);
        else
            ok = manager.RequestParallel(pEdge, pDist, dist2, pSide, parallelSwitch, out error);

        if (ok)
            hasPStart = false;
        else
            Say(Loc.T(error));
    }

    private void UpdateParallelPreview(TrackManager manager, bool hit, Vector3 aim)
    {
        EnsureVisuals();
        ShowProblem(false, Vector3.zero);

        int edge = 0, side = 1;
        float dist = 0f;

        // Freies Ende zum Anknuepfen? Gelb markieren.
        TrackNode attachNode = null;
        int attachEdge, attachSide;
        float attachDist;
        bool attach = hit && !hasPStart &&
                      manager.FindParallelAttach(aim, out attachNode, out attachEdge, out attachDist, out attachSide);

        bool pick = hit && manager.PickTrack(aim, 4f, out edge, out dist, out side);

        marker.SetActive(pick || attach);

        if (attach)
        {
            marker.transform.position = attachNode.pos + Vector3.up * 0.3f;
            SetColor(marker.GetComponent<Renderer>().sharedMaterial, Color.yellow);
        }
        else if (pick)
        {
            // Die Markierung zeigt den Punkt NEBEN dem Gleis, an dem das Parallelgleis liegt.
            float yaw;
            Vector3 shown = hasPStart
                ? manager.ParallelPoint(pEdge, dist2Safe(manager, aim), pSide, 1, out yaw)
                : manager.ParallelPoint(edge, dist, side, 1, out yaw);

            marker.transform.position = shown + Vector3.up * 0.3f;
            SetColor(marker.GetComponent<Renderer>().sharedMaterial, Color.cyan);
        }

        if (!hasPStart || !hit)
        {
            line.enabled = false;
            HideTrackPreview();
            ShowProblem(false, Vector3.zero);
            return;
        }

        float d2;
        string error;

        if (!manager.ResolveParallelEnd(pEdge, pDist, aim, out d2, out error))
        {
            line.enabled = false;
            HideTrackPreview();
            ShowProblem(false, Vector3.zero);
            Say(Loc.T(error));
            return;
        }

        // Genau wie beim echten Bau pruefen und jedes Stueck als Gleis zeigen (gruen/rot).
        bool hasProblem;
        Vector3 problem;

        bool valid = manager.PreviewParallel(pAttach > 0 ? manager.GetNode(pAttach) : null, pEdge, pDist, d2, pSide,
                                             parallelSwitch, pCurves, pValids, out error, out hasProblem, out problem);

        line.enabled = false;

        if (pCurves.Count == 0)
        {
            HideTrackPreview();

            // Nichts zu zeichnen (z. B. Uebergang zu kurz): Wenigstens den Zielpunkt rot markieren.
            float yaw;
            ShowProblem(!valid, manager.ParallelPoint(pEdge, d2, pSide, 1, out yaw));

            if (!valid)
                Say(Loc.T(error));

            return;
        }

        ShowTrackPreview(pCurves, pValids);

        // Die rote Kugel dort, wo es hakt.
        ShowProblem(!valid && hasProblem, problem);

        if (!valid)
            Say(Loc.T(error));
    }

    // Strecke (auf dem ersten Stueck) des Punktes, auf den gerade gezielt wird.
    private float dist2Safe(TrackManager manager, Vector3 aim)
    {
        float d;
        string error;

        return manager.ResolveParallelEnd(pEdge, pDist, aim, out d, out error) ? d : pDist;
    }

    // Wohin zeigt das Fadenkreuz? Gibt einen Punkt auf dem Gelaende zurueck.
    private bool Aim(TrackManager manager, out Vector3 point)
    {
        point = Vector3.zero;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit info;

        if (!Physics.Raycast(ray, out info, maxDistance, ~0, QueryTriggerInteraction.Ignore))
            return false;

        point = info.point;

        // Auf Gelaende zielen wir direkt. Trifft der Strahl etwas anderes (Gleis, Maschine),
        // nehmen wir die Gelaendehoehe an dieser Stelle.
        bool terrain = manager.world != null && manager.world.OwnsCollider(info.collider);

        if (!terrain && manager.world != null)
            point.y = manager.world.GetSurfaceWorldY(point.x, point.z);

        return true;
    }

    private void Click(TrackManager manager, Vector3 aim, TrackNode snap)
    {
        if (!hasStart)
        {
            // Startpunkt: ein freies Ende oder ein neuer Punkt im Gelaende.
            hasStart = true;
            startNode = snap;

            if (snap != null)
            {
                startPoint = snap.pos;
            }
            else
            {
                // Mitten auf einem Gleis: dort entsteht eine Weiche.
                TrackManager.TrackJoin join = manager.FindJoin(aim, 1.0f);
                startPoint = join != null ? join.pos : aim;
            }

            return;
        }

        // Im Mehrspieler entsteht der Punkt erst, wenn der Host das Kommando ausgefuehrt hat:
        // Dann suchen wir das Ende anhand seines Ortes.
        if (startNode == null)
            startNode = manager.Network.FindNode(startPoint, 0.3f, true);

        string error;
        Vector3 lastPos;

        if (manager.RequestBuild(startNode, startPoint, snap, aim, out error, out lastPos))
        {
            // Weiterbauen: das neue Ende ist der naechste Startpunkt.
            startPoint = lastPos;
            startNode = manager.Network.FindNode(lastPos, 0.3f, true);

            // Endete das Gleis an einer Weiche oder an einem schon verbundenen Punkt, gibt es kein
            // freies Ende zum Weiterbauen: Startpunkt verwerfen.
            if (startNode == null && manager.Network.FindNode(lastPos, 0.3f, false) != null)
                hasStart = false;
        }
        else
        {
            Say(Loc.T(error));
        }
    }

    private void PlaceLoco(TrackManager manager, Vector3 aim)
    {
        int edge;
        float dist, distance;

        if (!manager.Network.Project(aim, out edge, out dist, out distance) || distance > 2.5f)
        {
            Say(Loc.T("track.err.noTrack"));
            return;
        }

        // Blickrichtung des Spielers bestimmt, wohin die Lok zeigt.
        Vector3 pos, tan;
        manager.Network.edges[edge].Evaluate(dist, out pos, out tan);

        Vector3 look = playerCamera.transform.forward;
        look.y = 0f;
        tan.y = 0f;

        int dir = Vector3.Dot(look, tan) >= 0f ? 1 : -1;

        manager.RequestSpawnLoco(edge, dist, dir);
    }

    private void Remove(TrackManager manager, Vector3 aim)
    {
        // Im Schacht-Werkzeug entfernt Entfernen nur den Schacht (das Gleis bleibt).
        if (shaftMode)
        {
            if (manager.RequestShaftRemove(aim))
                Say(Loc.T("track.removed"));

            return;
        }

        // Innerhalb einer Drehscheibe: die ganze Drehscheibe abbauen.
        TurntableData tt = manager.TurntableAt(aim);

        if (tt != null)
        {
            string ttError;

            if (manager.RequestTurntableRemove(tt.id, out ttError))
                Say(Loc.T("track.removed"));
            else
                Say(Loc.T(ttError));

            return;
        }

        // Zuerst Fahrzeuge, dann Gleise.
        TrainCar car = TrainCar.FindNear(aim, 3f);

        if (car != null)
        {
            manager.RequestRemoveCar(car);
            Say(Loc.T("track.removed"));
            return;
        }

        int edge;
        float dist, distance;

        if (!manager.Network.Project(aim, out edge, out dist, out distance) || distance > 2.5f)
            return;

        string error;

        if (manager.RequestRemove(edge, out error))
            Say(Loc.T("track.removed"));
        else
            Say(Loc.T(error));
    }

    // ---------- Vorschau ----------

    private void UpdatePreview(TrackManager manager, bool hit, Vector3 aim, TrackNode snap)
    {
        EnsureVisuals();

        // Markierung am Zielpunkt (gruen = freies Ende zum Anschliessen).
        marker.SetActive(hit);

        // Mitten auf einem Gleis (blau): hier entsteht eine Weiche.
        TrackManager.TrackJoin join = (hit && snap == null) ? manager.FindJoin(aim, 1.0f) : null;

        if (hit)
        {
            Vector3 shown = snap != null ? snap.pos : (join != null ? join.pos : aim);

            marker.transform.position = shown + Vector3.up * 0.3f;
            SetColor(marker.GetComponent<Renderer>().sharedMaterial,
                     snap != null ? Color.green : (join != null ? Color.cyan : Color.white));
        }

        if (!hasStart || !hit)
        {
            line.enabled = false;
            HideTrackPreview();
            ShowProblem(false, Vector3.zero);
            return;
        }

        Vector3 realEnd;
        TrackNode endSnap = snap != startNode ? snap : null;
        TrackManager.TrackPlan plan = manager.PreviewBuild(startNode, startPoint, endSnap, aim, out realEnd);

        if (plan.curve == null)
        {
            line.enabled = false;
            HideTrackPreview();
            ShowProblem(false, Vector3.zero);
            Say(plan.error != null ? Loc.T(plan.error) : null);
            return;
        }

        // Rote Kugel dort, wo es hakt (Gleis im Weg, engste Stelle der Kurve, falsche Richtung ...).
        ShowProblem(!plan.valid && plan.hasProblem, plan.problem);

        // Statt einer Linie: das Gleis selbst, gruen = geht, rot = geht nicht.
        line.enabled = false;
        ShowTrackPreview(plan.curve, plan.valid);

        if (!plan.valid)
            Say(Loc.T(plan.error));
    }

    // Ein Stueck der Gleisvorschau.
    private class PreviewPiece
    {
        public GameObject go;
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Vector3 keyA, keyB;
        public float keyLength = -1f;
        public int valid = -1;     // 1 gruen, 0 rot
    }

    private readonly List<PreviewPiece> previewPieces = new List<PreviewPiece>();
    private Material[] previewGreen, previewRed;

    private void EnsurePreviewMaterials()
    {
        if (previewGreen != null)
            return;

        Shader shader = Shader.Find("Sprites/Default");
        previewGreen = new Material[] { new Material(shader), new Material(shader), new Material(shader) };
        previewRed = new Material[] { new Material(shader), new Material(shader), new Material(shader) };

        // Schotter hell und durchsichtig, Schwellen dunkler, Schienen kraeftig (Sprites/Default rechnet
        // mit vormultiplizierter Farbe).
        Color g = new Color(0.25f, 1f, 0.3f), r = new Color(1f, 0.25f, 0.2f);

        SetPreviewColor(previewGreen[0], g * 0.85f, 0.45f);
        SetPreviewColor(previewGreen[1], g * 0.55f, 0.7f);
        SetPreviewColor(previewGreen[2], g, 0.95f);
        SetPreviewColor(previewRed[0], r * 0.85f, 0.45f);
        SetPreviewColor(previewRed[1], r * 0.55f, 0.7f);
        SetPreviewColor(previewRed[2], r, 0.95f);
    }

    private PreviewPiece NewPreviewPiece()
    {
        PreviewPiece piece = new PreviewPiece();

        // Bewusst OHNE Elternobjekt: Der Gleisbauer haengt am Spieler und wuerde die Vorschau sonst mitnehmen.
        // Das Mesh liegt in Weltkoordinaten, das Objekt steht deshalb fest im Nullpunkt.
        piece.go = new GameObject("GleisVorschauMesh");
        piece.go.transform.position = Vector3.up * 0.08f;   // etwas anheben: sonst sinkt der Schotter in den Boden
        piece.go.transform.rotation = Quaternion.identity;
        piece.go.transform.localScale = Vector3.one;

        piece.filter = piece.go.AddComponent<MeshFilter>();
        piece.renderer = piece.go.AddComponent<MeshRenderer>();
        piece.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        piece.renderer.receiveShadows = false;

        return piece;
    }

    private void ShowTrackPreview(TrackEdge curve, bool valid)
    {
        previewOne.Clear();
        previewOneValid.Clear();
        previewOne.Add(curve);
        previewOneValid.Add(valid);
        ShowTrackPreview(previewOne, previewOneValid);
    }

    private readonly List<TrackEdge> previewOne = new List<TrackEdge>();
    private readonly List<bool> previewOneValid = new List<bool>();

    // Zeigt geplante Gleise als halbdurchsichtige Gleis-Meshes (gruen = geht, rot = geht nicht). Ein Mesh wird
    // nur neu berechnet, wenn sich seine Kurve aendert (und hoechstens alle 0,04 s).
    private void ShowTrackPreview(List<TrackEdge> curves, List<bool> valids)
    {
        EnsurePreviewMaterials();

        while (previewPieces.Count < curves.Count)
            previewPieces.Add(NewPreviewPiece());

        bool mayRebuild = Time.time >= trackPreviewNext;
        bool rebuilt = false;

        for (int i = 0; i < previewPieces.Count; i++)
        {
            PreviewPiece piece = previewPieces[i];

            if (i >= curves.Count || curves[i] == null)
            {
                piece.go.SetActive(false);
                piece.keyLength = -1f;
                continue;
            }

            piece.go.SetActive(true);

            int v = valids[i] ? 1 : 0;

            if (piece.valid != v)
            {
                piece.valid = v;
                piece.renderer.sharedMaterials = v == 1 ? previewGreen : previewRed;
            }

            TrackEdge curve = curves[i];
            Vector3 a = curve.pts[0];
            Vector3 b = curve.pts[curve.pts.Length - 1];

            bool changed = (a - piece.keyA).sqrMagnitude > 0.0004f ||
                           (b - piece.keyB).sqrMagnitude > 0.0004f ||
                           Mathf.Abs(curve.length - piece.keyLength) > 0.01f;

            if (!changed || !mayRebuild)
                continue;

            piece.keyA = a;
            piece.keyB = b;
            piece.keyLength = curve.length;

            Mesh old = piece.filter.sharedMesh;
            piece.filter.sharedMesh = TrackMesh.FullMesh(curve);
            rebuilt = true;

            if (old != null)
                Destroy(old);
        }

        if (rebuilt)
            trackPreviewNext = Time.time + 0.04f;
    }

    private static void SetPreviewColor(Material m, Color c, float alpha)
    {
        m.color = new Color(c.r * alpha, c.g * alpha, c.b * alpha, alpha);
    }

    void OnDestroy()
    {
        foreach (PreviewPiece piece in previewPieces)
        {
            if (piece.go != null)
                Destroy(piece.go);
        }
    }

    private void HideTrackPreview()
    {
        foreach (PreviewPiece piece in previewPieces)
        {
            if (piece.go != null)
                piece.go.SetActive(false);

            piece.keyLength = -1f;
        }
    }

    // Rote Kugel in der Vorschau an der Stelle, die den Bau verhindert.
    private void ShowProblem(bool show, Vector3 pos)
    {
        if (problemMarker == null)
            return;

        problemMarker.SetActive(show);

        if (show)
            problemMarker.transform.position = pos + Vector3.up * 0.5f;
    }

    private void EnsureVisuals()
    {
        if (line != null)
            return;

        Shader shader = Shader.Find("Sprites/Default");
        lineMaterial = new Material(shader);

        GameObject lineObject = new GameObject("GleisVorschau");
        lineObject.transform.SetParent(transform, false);

        line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.12f;
        line.sharedMaterial = lineMaterial;
        line.enabled = false;

        marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "GleisMarker";
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = Vector3.one * 0.4f;
        Destroy(marker.GetComponent<Collider>());

        Material m = new Material(shader);
        marker.GetComponent<Renderer>().sharedMaterial = m;
        marker.SetActive(false);

        problemMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        problemMarker.name = "GleisProblem";
        problemMarker.transform.SetParent(transform, false);
        problemMarker.transform.localScale = Vector3.one * 0.9f;
        Destroy(problemMarker.GetComponent<Collider>());

        Material pm = new Material(shader);
        pm.color = new Color(1f, 0.2f, 0.15f, 0.9f);
        problemMarker.GetComponent<Renderer>().sharedMaterial = pm;
        problemMarker.SetActive(false);
    }

    private static void SetColor(Material m, Color c)
    {
        m.color = c;
    }

    // ---------- Anzeige ----------

    private void Say(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        message = text;
        messageUntil = Time.time + 2.5f;
    }

    // Die Texte benutzen die Hinweis-Boxen des Spiels, damit sie mit der Textgroesse
    // aus den Einstellungen mitwachsen.
    void OnGUI()
    {
        if (SettingsMenu.IsOpen)
            return;

        bool showMessage = Time.time < messageUntil && !string.IsNullOrEmpty(message);

        if (active)
        {
            string hint;

            if (shaftMode)
            {
                hint = GameInput.ReplaceTokens(Loc.T("track.hint.shaft",
                    GameInput.Hint(GameAction.TrackShaft, shaftKey),
                    GameInput.Hint(GameAction.Remove, removeKey)));
            }
            else if (ttMode)
            {
                hint = GameInput.ReplaceTokens(Loc.T("track.hint.tt.place",
                    GameInput.Hint(GameAction.TrackTurntable, turntableKey)));
            }
            else if (parallelMode)
            {
                hint = Loc.T(hasPStart ? "track.hint.parallel.end" : "track.hint.parallel.start");
                hint += "\n" + GameInput.ReplaceTokens(Loc.T("track.hint.parallel",
                    GameInput.Hint(GameAction.TrackParallel, parallelKey),
                    GameInput.Hint(GameAction.TrackParallelSwitch, parallelSwitchKey),
                    Loc.T(parallelSwitch ? "track.parallel.on" : "track.parallel.off")));
            }
            else
            {
                hint = Loc.T(hasStart ? "track.hint.end" : "track.hint.start");
                hint += "\n" + GameInput.ReplaceTokens(Loc.T("track.hint",
                    GameInput.Hint(GameAction.TrackLoco, locoKey),
                    GameInput.Hint(GameAction.Remove, removeKey),
                    GameInput.UsingGamepad ? GameInput.Hint(GameAction.Secondary) : GameInput.Hint(GameAction.TrackTool, toggleKey),
                    GameInput.Hint(GameAction.TrackParallel, parallelKey)));

                TrackManager tm = TrackManager.Instance;

                hint += "\n" + Loc.T("track.hint.tt",
                    GameInput.Hint(GameAction.TrackTurntable, turntableKey),
                    GameInput.Hint(GameAction.Interact, tm != null ? tm.switchKey : KeyCode.F));
            }

            TrackManager m = TrackManager.Instance;

            if (m != null && m.VehicleTypeCount > 0)
            {
                int typeIndex = Mathf.Clamp(m.selectedLocomotive, 0, m.VehicleTypeCount - 1);
                hint += "\n" + Loc.T("track.loco.select", m.VehicleTypeName(typeIndex), GameInput.Hint(GameAction.TrackLocoType, cycleLocoKey));
            }

            GameSettings.DrawBoxBottomCenter(hint);
        }

        if (showMessage)
            GameSettings.DrawBoxBelowCenter(message);
    }
}
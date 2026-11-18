using System;
using UnityEngine;

// =====================================================================
//  HoseReel: eine Wasserstelle mit biegsamem Schlauch (Schlauchtrommel, Wasserkran, Wassersaeule ...).
//
//  So benutzt man ihn im Spiel:
//   - Die Duese haengt am Halter. Mit der Bedientaste auf die Duese zeigen = sie in die Hand nehmen.
//     Der Schlauch rollt sich dabei ab (bis "Hose Length").
//   - Mit der Duese in der Hand auf einen Wasser-Einfuellstutzen zeigen (z. B. am Tender einer Dampflok)
//     und die Bedientaste druecken = anschliessen. Das Wasser fliesst, bis der Tank voll ist.
//   - Ohne Stutzen im Blick: Bedientaste = ablegen (nahe an der Trommel wird der Schlauch eingeholt).
//   - Auf die angeschlossene Duese zeigen und Bedientaste = abziehen und wieder in die Hand nehmen.
//   - Wird der Schlauch zu kurz (die Lok faehrt weg, man geht zu weit), laesst er los und faellt zu Boden.
//
//  EINRICHTEN (Prefab): Dieses Skript auf die Wurzel. Optional:
//   - "Hose Outlet": leeres Objekt, wo der Schlauch herauskommt (sonst 1 m ueber dem Ursprung).
//   - "Nozzle Holder": leeres Objekt, wo die Duese haengt (sonst am Auslass).
//   - "Nozzle Prefab": eigenes Modell der Duese (nur Optik; vorne = +Z, Schlauch haengt hinten).
//   - Wasser: "Infinite Water" = unendlich (zum Testen). Sonst Zulauf ueber einen Port (Wasser, Eingang) am
//     Prefab, der ueber eine normale Leitung gespeist wird, und ein Vorrat (Buffer Capacity).
//
//  Mehrspieler: Nehmen, Ablegen, Anschliessen und Abziehen laufen als Kommando bei allen gleich. Der Zustand der
//  Duese wird nicht gespeichert (nach dem Laden haengt sie wieder am Halter).
//
//  Die Schlauch-Optik (Seil-Simulation plus Rohr-Mesh) macht dieses Skript selbst.
// =====================================================================
public class HoseReel : MonoBehaviour, IPortOwner
{
    public enum HoseState
    {
        Hanging,   // am Halter, Schlauch eingerollt
        Carried,   // in der Hand eines Spielers
        Ground,    // liegt am Boden
        Plugged    // an einem Stutzen angeschlossen
    }

    [Header("Schlauch")]
    [Tooltip("Laenge des Schlauchs in Metern. Weiter kommt man von der Trommel nicht weg.")]
    public float hoseLength = 15f;

    [Tooltip("Durchmesser des Schlauchs in Zentimetern.")]
    public float hoseThicknessCm = 4f;

    [Tooltip("Steifigkeit des Schlauchs (0 = schlaff wie ein Seil, 1 = fast gerade wie ein Gartenschlauch unter Druck).")]
    [Range(0f, 1f)]
    public float hoseStiffness = 0.75f;

    [Tooltip("Farbe des Schlauchs.")]
    public Color hoseColor = new Color(0.14f, 0.15f, 0.17f);

    [Header("Anschluesse am Modell")]
    [Tooltip("Hier kommt der Schlauch heraus (leer = 1 m ueber dem Ursprung).")]
    public Transform hoseOutlet;

    [Tooltip("Hier haengt die Duese (leer = am Auslass).")]
    public Transform nozzleHolder;

    [Tooltip("Optional: eigenes Modell der Duese (nur Optik, vorne = +Z).")]
    public GameObject nozzlePrefab;

    [Header("Wasser")]
    [Tooltip("Unendliche Wasserquelle (zum Testen). Aus = das Wasser kommt ueber einen Zulauf-Port.")]
    public bool infiniteWater = true;

    [Tooltip("Vorrat bei Zulauf ueber Leitung.")]
    public float bufferCapacity = 100f;

    [Tooltip("Wasser pro Sekunde, das die Duese abgibt.")]
    public float flowPerSecond = 8f;

    // ---------- Zustand ----------

    public HoseState State { get; private set; }
    public int CarrierId { get; private set; }
    public bool CarrierIsLocal { get { return State == HoseState.Carried && CarrierId == NetSync.LocalId; } }
    public int PickFrame { get; private set; }
    public HoseNozzle Nozzle { get { return nozzle; } }
    public Vector3 OutletPos
    {
        get { return hoseOutlet != null ? hoseOutlet.position : transform.position + Vector3.up * 1f; }
    }

    private HoseNozzle nozzle;
    private Port nozzlePort;
    private Port plugPort;
    private Vector3 groundPos;
    private float groundYaw;
    private float buffer;
    private HoseRope rope;
    private float limitCooldown;
    private float smoothPaid;

    void Awake()
    {
        buffer = infiniteWater ? 0f : 0f;
        BuildNozzle();

        rope = new HoseRope(18, hoseThicknessCm * 0.005f, TrackManager.MakeMaterial(hoseColor, 0.35f));
        rope.SetVisible(false);
    }

    void OnDestroy()
    {
        if (nozzle != null)
            Destroy(nozzle.gameObject);

        if (rope != null)
            rope.Destroy();
    }

    // Duese bauen: eigenes Objekt (ohne Eltern, damit es frei herumfliegen kann), Port als Ausgang.
    private void BuildNozzle()
    {
        GameObject n = new GameObject("Schlauchduese");
        n.transform.SetParent(transform, false);   // erst als Kind: der Port findet so seinen Besitzer (diese Trommel)

        // Optik
        if (nozzlePrefab != null)
        {
            GameObject model = Instantiate(nozzlePrefab, n.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
        }
        else
        {
            Material brass = TrackManager.MakeMaterial(new Color(0.78f, 0.6f, 0.22f), 0.6f);
            Material rubber = TrackManager.MakeMaterial(new Color(0.1f, 0.1f, 0.12f), 0.2f);
            Material band = TrackManager.MakeMaterial(new Color(0.15f, 0.4f, 0.8f), 0.4f);

            // Von hinten (Schlauch) nach vorne: Knickschutz, Ueberwurfmutter, Koerper, Farbring, Spitze.
            // Zylinder liegen entlang Z (Laenge = 2 x Skalierung Y, Durchmesser = Skalierung X).
            MakePart(n.transform, PrimitiveType.Cylinder, "Knickschutz1", new Vector3(0f, 0f, -0.115f), new Vector3(90f, 0f, 0f),
                     new Vector3(0.05f, 0.03f, 0.05f), rubber);
            MakePart(n.transform, PrimitiveType.Cylinder, "Knickschutz2", new Vector3(0f, 0f, -0.075f), new Vector3(90f, 0f, 0f),
                     new Vector3(0.066f, 0.02f, 0.066f), rubber);
            MakePart(n.transform, PrimitiveType.Cylinder, "Mutter", new Vector3(0f, 0f, -0.035f), new Vector3(90f, 0f, 0f),
                     new Vector3(0.082f, 0.02f, 0.082f), brass);
            MakePart(n.transform, PrimitiveType.Cylinder, "Koerper", new Vector3(0f, 0f, 0.04f), new Vector3(90f, 0f, 0f),
                     new Vector3(0.06f, 0.06f, 0.06f), brass);
            MakePart(n.transform, PrimitiveType.Cylinder, "Farbring", new Vector3(0f, 0f, 0.1f), new Vector3(90f, 0f, 0f),
                     new Vector3(0.076f, 0.01f, 0.076f), band);
            MakePart(n.transform, PrimitiveType.Cylinder, "Spitze", new Vector3(0f, 0f, 0.15f), new Vector3(90f, 0f, 0f),
                     new Vector3(0.04f, 0.04f, 0.04f), brass);

            // Griff und Abzug
            MakePart(n.transform, PrimitiveType.Cube, "Griff", new Vector3(0f, -0.075f, 0f), new Vector3(-12f, 0f, 0f),
                     new Vector3(0.035f, 0.11f, 0.05f), rubber);
            MakePart(n.transform, PrimitiveType.Cube, "Abzug", new Vector3(0f, -0.035f, 0.045f), new Vector3(20f, 0f, 0f),
                     new Vector3(0.015f, 0.04f, 0.02f), brass);
        }

        // Anvisieren / Anfassen
        BoxCollider box = n.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(0.3f, 0.3f, 0.45f);
        box.center = new Vector3(0f, 0f, 0.05f);

        GameObject portObject = new GameObject("DuesenPort");
        portObject.transform.SetParent(n.transform, false);
        portObject.transform.localPosition = new Vector3(0f, 0f, 0.17f);

        nozzlePort = portObject.AddComponent<Port>();
        nozzlePort.Configure(PortMedium.Water, PortDirection.Output, flowPerSecond);

        nozzle = n.AddComponent<HoseNozzle>();
        nozzle.reel = this;
        nozzle.port = nozzlePort;
        nozzle.box = box;

        // Jetzt frei machen: Die Duese gehoert zur Trommel, haengt aber nicht an ihr.
        n.transform.SetParent(null, true);
        n.hideFlags = HideFlags.None;

        PlaceAtHolder();
    }

    private static void MakePart(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler,
                                 Vector3 scale, Material material)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = material;
        Destroy(g.GetComponent<Collider>());
    }

    private void PlaceAtHolder()
    {
        Transform holder = nozzleHolder != null ? nozzleHolder : hoseOutlet;

        if (holder != null)
            nozzle.transform.SetPositionAndRotation(holder.position, holder.rotation);
        else
            nozzle.transform.SetPositionAndRotation(OutletPos, transform.rotation);
    }

    // ---------- Von der Duese aufgerufen (Anfragen als Kommando) ----------

    public void RequestPick()
    {
        Send(0, 0, 0, 0, nozzle.transform.position);
    }

    public void RequestDrop(Vector3 where)
    {
        Send(1, 0, 0, 0, where);
    }

    public void RequestUnplug()
    {
        Send(3, 0, 0, 0, nozzle.transform.position);
    }

    public void RequestPlug(Port target)
    {
        int kind, id, index;

        if (!DescribeTarget(target, out kind, out id, out index))
        {
            // Ziel hat keine Netz-Nummer (z. B. im Editor ohne Netz): direkt anschliessen.
            Apply(2, target, Vector3.zero, NetSync.LocalId);
            return;
        }

        Send(2, kind, id, index, target.transform.position);
    }

    private void Send(int action, int kind, int targetId, int portIndex, Vector3 where)
    {
        int id = NetSync.IdOf(this);

        if (id == 0)
        {
            // Ohne Netz-Nummer (nicht gebaut, nicht in der Szene gefunden): direkt ausfuehren.
            Port target = action == 2 ? ResolveTarget(kind, targetId, portIndex) : null;
            Apply(action, target, where, NetSync.LocalId);
            return;
        }

        NetSync.Submit(NetCommand.HoseAction(id, action, kind, targetId, portIndex, where));
    }

    // ---------- Kommando ausfuehren (bei allen Spielern) ----------

    public static void ExecuteAction(int reelId, int action, int kind, int targetId, int portIndex, Vector3 where, int sender)
    {
        Transform root = NetSync.ResolveRoot(reelId);
        HoseReel reel = root != null ? root.GetComponentInChildren<HoseReel>(true) : null;

        if (reel == null)
            return;

        Port target = action == 2 ? ResolveTarget(kind, targetId, portIndex) : null;
        reel.Apply(action, target, where, sender);
    }

    private void Apply(int action, Port target, Vector3 where, int sender)
    {
        switch (action)
        {
            case 0:     // nehmen
                Unplug();
                State = HoseState.Carried;
                CarrierId = sender;
                PickFrame = Time.frameCount;
                break;

            case 1:     // ablegen
                Unplug();

                if ((where - OutletPos).sqrMagnitude < 1.5f * 1.5f)
                {
                    State = HoseState.Hanging;     // nahe an der Trommel: wird eingeholt
                }
                else
                {
                    State = HoseState.Ground;
                    groundPos = where;
                    groundYaw = nozzle.transform.eulerAngles.y;
                }

                break;

            case 2:     // anschliessen
                Unplug();

                if (target != null && Port.TryConnect(nozzlePort, target))
                {
                    State = HoseState.Plugged;
                    plugPort = target;
                }
                else
                {
                    State = HoseState.Ground;
                    groundPos = where;
                    groundYaw = nozzle.transform.eulerAngles.y;
                }

                break;

            case 3:     // abziehen
                Unplug();
                State = HoseState.Ground;
                groundPos = where;
                groundYaw = nozzle.transform.eulerAngles.y;
                break;
        }
    }

    private void Unplug()
    {
        if (nozzlePort != null && nozzlePort.IsConnected)
            nozzlePort.Disconnect();

        plugPort = null;
    }

    // ---------- Ziele beschreiben / finden ----------

    // Fahrzeuge (kind 1) werden ueber ihre Zugnummer gefunden, alles andere ueber die Netz-Nummer.
    public static bool DescribeTarget(Port port, out int kind, out int id, out int index)
    {
        kind = 0;
        id = 0;
        index = -1;

        if (port == null)
            return false;

        TrainCar car = port.GetComponentInParent<TrainCar>();

        if (car != null)
        {
            kind = 1;
            id = car.NetId;
            index = Array.IndexOf(car.GetComponentsInChildren<Port>(true), port);
            return id > 0 && index >= 0;
        }

        return NetSync.DescribePort(port, out id, out index);
    }

    private static Port ResolveTarget(int kind, int id, int index)
    {
        if (kind == 1)
        {
            TrainCar car = TrackManager.Instance != null ? TrackManager.Instance.FindCar(id) : null;

            if (car == null)
                return null;

            Port[] ports = car.GetComponentsInChildren<Port>(true);
            return index >= 0 && index < ports.Length ? ports[index] : null;
        }

        return NetSync.ResolvePort(id, index);
    }

    // ---------- Jedes Bild: Duese, Schlauch ----------

    void LateUpdate()
    {
        if (nozzle == null)
            return;

        // Angeschlossen, aber der Stutzen ist weg (Fahrzeug abgebaut): Duese faellt zu Boden.
        if (State == HoseState.Plugged && (plugPort == null || !nozzlePort.IsConnected))
        {
            groundPos = nozzle.transform.position;
            groundYaw = nozzle.transform.eulerAngles.y;
            Unplug();
            State = HoseState.Ground;
        }

        Vector3 outlet = OutletPos;
        Vector3 target;
        Quaternion rotation;

        switch (State)
        {
            case HoseState.Carried:
                if (!HandPose(out target, out rotation))
                {
                    target = nozzle.transform.position;
                    rotation = nozzle.transform.rotation;
                }

                // Die Hand kommt nie weiter als die Schlauchlaenge von der Trommel weg.
                Vector3 toHand = target - outlet;

                if (toHand.magnitude > hoseLength)
                    target = outlet + toHand.normalized * hoseLength;

                break;

            case HoseState.Plugged:
                target = plugPort.transform.position;
                rotation = Quaternion.LookRotation(-plugPort.transform.forward, Vector3.up);
                break;

            case HoseState.Ground:
                target = groundPos + Vector3.up * 0.06f;
                rotation = Quaternion.Euler(0f, groundYaw, 0f);
                break;

            default:
                Transform holder = nozzleHolder != null ? nozzleHolder : hoseOutlet;
                target = holder != null ? holder.position : outlet;
                rotation = holder != null ? holder.rotation : transform.rotation;
                break;
        }

        nozzle.transform.SetPositionAndRotation(target, rotation);

        // Beim Tragen schaltet sich der Griff-Collider ab (sonst wuerde man die Duese in der Hand anvisieren).
        if (nozzle.box != null)
            nozzle.box.enabled = State != HoseState.Carried;

        // Schlauch: am Halter eingerollt (unsichtbar), sonst Seil-Simulation zwischen Auslass und Duese.
        if (State == HoseState.Hanging)
        {
            rope.SetVisible(false);
            return;
        }

        Vector3 end = nozzle.AttachPos;
        float distance = Vector3.Distance(outlet, end);

        // Ausgerollte Laenge: etwas mehr als der Abstand, damit der Schlauch leicht durchhaengt.
        float paid = Mathf.Min(hoseLength, distance * 1.02f + 0.2f);

        if (!rope.Visible)
        {
            rope.SetVisible(true);
            smoothPaid = paid;
            rope.Reset(outlet, end);
        }

        // Laenge wird weich nachgefuehrt, damit der Schlauch nicht ruckartig peitscht.
        smoothPaid = Mathf.MoveTowards(smoothPaid, paid, 4f * Time.deltaTime);

        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        rope.Stiffness = hoseStiffness;
        rope.Step(outlet, end, dt, smoothPaid);

        // Zu weit weg (Lok faehrt los, Spieler geht zu weit): nur der Host entscheidet, damit alle gleich handeln.
        limitCooldown -= Time.deltaTime;

        if (!NetSync.IsClient && limitCooldown <= 0f && distance > hoseLength + 0.4f &&
            (State == HoseState.Plugged || State == HoseState.Carried))
        {
            limitCooldown = 0.6f;

            if (State == HoseState.Plugged)
                RequestUnplug();
            else
                RequestDrop(end);
        }
    }

    // Wo die Hand des Traegers ist (lokaler Spieler: an der Kamera; Mitspieler: an der Figur).
    private bool HandPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (CarrierId == NetSync.LocalId)
        {
            Camera cam = TrackManager.Instance != null ? TrackManager.Instance.PlayerCamera : null;

            if (cam == null)
                cam = Camera.main;

            if (cam == null)
                return false;

            Transform c = cam.transform;
            position = c.position + c.forward * 0.8f + c.right * 0.25f - c.up * 0.3f;
            rotation = Quaternion.LookRotation(c.forward, Vector3.up);
            return true;
        }

        RemotePlayer remote = RemotePlayer.Find(CarrierId);

        if (remote == null)
            return false;

        position = remote.transform.position + Vector3.up * 1.1f + remote.transform.forward * 0.6f;
        rotation = remote.transform.rotation;
        return true;
    }

    // Von aussen Wasser in den Vorrat fuellen (z. B. aus dem Tank des Lokschuppens). Nur bei "Infinite Water" aus.
    // Gibt zurueck, wie viel hineinpasste.
    public float AddToBuffer(float amount)
    {
        if (infiniteWater || amount <= 0f)
            return 0f;

        float accepted = Mathf.Clamp(amount, 0f, bufferCapacity - buffer);
        buffer += accepted;
        return accepted;
    }

    // ---------- IPortOwner ----------
    // Die Duese gibt Wasser ab; ein optionaler Zulauf-Port (Wasser, Eingang) fuellt den Vorrat.

    public float GetAvailable(Port port)
    {
        if (port != nozzlePort || port.Medium != PortMedium.Water)
            return 0f;

        return infiniteWater ? float.MaxValue : buffer;
    }

    public float GetFreeSpace(Port port)
    {
        if (port == nozzlePort || infiniteWater || port.Medium != PortMedium.Water)
            return 0f;

        return Mathf.Max(0f, bufferCapacity - buffer);
    }

    public float TakeFrom(Port port, float amount)
    {
        if (port != nozzlePort || port.Medium != PortMedium.Water)
            return 0f;

        if (infiniteWater)
            return amount;

        float taken = Mathf.Clamp(amount, 0f, buffer);
        buffer -= taken;
        return taken;
    }

    public float GiveTo(Port port, float amount)
    {
        if (port == nozzlePort || infiniteWater || port.Medium != PortMedium.Water)
            return 0f;

        float accepted = Mathf.Clamp(amount, 0f, bufferCapacity - buffer);
        buffer += accepted;
        return accepted;
    }
}

// =====================================================================
//  HoseRope: das Seil hinter dem Schlauch. Eine Kette aus Punkten (Verlet), die zwischen zwei Enden haengt,
//  durchhaengt und am Boden liegen bleibt. Daraus wird jedes Bild ein Rohr-Mesh gebaut.
// =====================================================================
public class HoseRope
{
    private const float FixedStep = 1f / 60f;

    private readonly int count;
    private readonly int sides = 8;
    private readonly float radius;
    private readonly Vector3[] p;
    private readonly Vector3[] prev;
    private readonly Vector3[] vertices;
    private readonly Vector3[] normals;
    private readonly Mesh mesh;
    private readonly GameObject go;
    private float accumulator;

    public bool Visible { get; private set; }

    // 0 = schlaff, 1 = fast gerade (wird von der Schlauchtrommel gesetzt).
    public float Stiffness = 0.75f;

    public HoseRope(int points, float tubeRadius, Material material)
    {
        count = Mathf.Max(4, points);
        radius = tubeRadius;

        p = new Vector3[count];
        prev = new Vector3[count];

        // Ringe plus je ein Mittelpunkt fuer die Endkappen.
        vertices = new Vector3[count * sides + 2];
        normals = new Vector3[count * sides + 2];

        int capStart = count * sides;
        int[] tris = new int[(count - 1) * sides * 6 + sides * 6];
        int t = 0;

        for (int i = 0; i < count - 1; i++)
        {
            for (int s = 0; s < sides; s++)
            {
                int a = i * sides + s;
                int b = i * sides + (s + 1) % sides;
                int c = (i + 1) * sides + s;
                int d = (i + 1) * sides + (s + 1) % sides;

                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        }

        // Endkappen (vorne und hinten), damit das offene Rohrende nie zu sehen ist.
        int last = (count - 1) * sides;

        for (int s = 0; s < sides; s++)
        {
            int n = (s + 1) % sides;

            tris[t++] = capStart; tris[t++] = n; tris[t++] = s;
            tris[t++] = capStart + 1; tris[t++] = last + s; tris[t++] = last + n;
        }

        mesh = new Mesh();
        mesh.name = "Schlauch";
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.triangles = tris;

        go = new GameObject("Schlauch");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    public void SetVisible(bool visible)
    {
        Visible = visible;
        go.SetActive(visible);
    }

    public void Destroy()
    {
        UnityEngine.Object.Destroy(go);
        UnityEngine.Object.Destroy(mesh);
    }

    // Gerade Kette zwischen den Enden.
    public void Reset(Vector3 a, Vector3 b)
    {
        for (int i = 0; i < count; i++)
        {
            p[i] = Vector3.Lerp(a, b, (float)i / (count - 1));
            prev[i] = p[i];
        }

        accumulator = 0f;
    }

    // Rechnet in festen Zeitschritten (gleichmaessig, egal wie schnell das Bild kommt).
    // "length" ist die ausgerollte Schlauchlaenge.
    public void Step(Vector3 a, Vector3 b, float dt, float length)
    {
        accumulator = Mathf.Min(accumulator + dt, FixedStep * 3f);

        while (accumulator >= FixedStep)
        {
            accumulator -= FixedStep;
            StepFixed(a, b, length);
        }

        p[0] = a;
        p[count - 1] = b;
        BuildMesh();
    }

    private void StepFixed(Vector3 a, Vector3 b, float length)
    {
        float seg = length / (count - 1);
        float g = 9.81f * FixedStep * FixedStep;

        p[0] = a;
        p[count - 1] = b;

        for (int i = 1; i < count - 1; i++)
        {
            // Starke Daempfung: Der Schlauch ist schwer und traege, er schwingt nicht nach.
            Vector3 v = (p[i] - prev[i]) * Mathf.Lerp(0.88f, 0.6f, Stiffness);
            prev[i] = p[i];
            p[i] += v + Vector3.down * g;
        }

        // Laengenbedingung (Glieder) plus Biegesteifigkeit (jeder Punkt will von seinem uebernaechsten Abstand halten).
        float minSkip = seg * Mathf.Lerp(1.85f, 1.985f, Stiffness);
        float bendPush = Mathf.Lerp(0.1f, 0.5f, Stiffness);

        for (int iter = 0; iter < 20; iter++)
        {
            for (int i = 0; i < count - 1; i++)
            {
                Vector3 d = p[i + 1] - p[i];
                float len = d.magnitude;

                if (len < 1e-5f)
                    continue;

                Vector3 correction = d * ((len - seg) / len);

                if (i == 0)
                {
                    p[1] -= correction;
                }
                else if (i == count - 2)
                {
                    p[i] += correction;
                }
                else
                {
                    p[i] += correction * 0.5f;
                    p[i + 1] -= correction * 0.5f;
                }
            }

            // Biegesteifigkeit: scharfe Knicke werden aufgeweitet.
            for (int i = 1; i < count - 1; i++)
            {
                Vector3 d = p[i + 1] - p[i - 1];
                float len = d.magnitude;

                if (len < 1e-5f || len >= minSkip)
                    continue;

                Vector3 push = d * ((minSkip - len) / len) * bendPush;

                if (i - 1 > 0)
                    p[i - 1] -= push;

                if (i + 1 < count - 1)
                    p[i + 1] += push;
            }

            p[0] = a;
            p[count - 1] = b;
        }

        // Boden: Punkte bleiben darueber liegen.
        for (int i = 1; i < count - 1; i++)
        {
            RaycastHit hit;

            if (Physics.Raycast(p[i] + Vector3.up * 0.6f, Vector3.down, out hit, 2.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                float minY = hit.point.y + radius;

                if (p[i].y < minY)
                {
                    p[i].y = minY;
                    prev[i] = Vector3.Lerp(prev[i], p[i], 0.8f);   // viel Reibung am Boden
                }
            }

            if (float.IsNaN(p[i].x) || float.IsNaN(p[i].y) || float.IsNaN(p[i].z))
                Reset(a, b);
        }
    }

    private void BuildMesh()
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 tangent = i == 0 ? p[1] - p[0] : (i == count - 1 ? p[i] - p[i - 1] : p[i + 1] - p[i - 1]);

            if (tangent.sqrMagnitude < 1e-8f)
                tangent = Vector3.forward;

            tangent.Normalize();

            Vector3 up = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            Vector3 side = Vector3.Cross(tangent, up).normalized;
            Vector3 normal = Vector3.Cross(side, tangent).normalized;

            for (int s = 0; s < sides; s++)
            {
                float angle = s * Mathf.PI * 2f / sides;
                Vector3 dir = side * Mathf.Cos(angle) + normal * Mathf.Sin(angle);

                vertices[i * sides + s] = p[i] + dir * radius;
                normals[i * sides + s] = dir;
            }
        }

        int capStart = count * sides;
        vertices[capStart] = p[0];
        normals[capStart] = (p[0] - p[1]).normalized;
        vertices[capStart + 1] = p[count - 1];
        normals[capStart + 1] = (p[count - 1] - p[count - 2]).normalized;

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.RecalculateBounds();
    }
}
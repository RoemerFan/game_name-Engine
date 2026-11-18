using UnityEngine;

// =====================================================================
//  Drehscheibe (Platzhalter-Modell).
//
//  So funktioniert sie:
//   - Am Rand der runden Grube liegen "Ausgaenge" (Gleispunkte, gleichmaessig verteilt).
//     Dort schliesst man ganz normal Gleise an (Gleisbau, freies Ende anklicken).
//   - Die Bruecke ist ein gerades Gleisstueck quer durch die Mitte. Sie verbindet immer zwei
//     gegenueberliegende Ausgaenge. Beim Drehen wird sie von diesen Ausgaengen geloest und nach der
//     Drehung an zwei andere gehaengt. Fahrzeuge, die auf der Bruecke stehen, drehen mit.
//     Waehrend der Drehung kann keine Lok auf- oder abfahren, Zuege halten vor dem Ausgang.
//   - Bedient wird sie am Pfosten neben jedem Ausgang: darauf zielen und F druecken.
//
//  Das Aussehen ist ein einfaches Platzhalter-Modell aus Quadern. Eigenes Modell: Prefab beim
//  TrackManager unter "Turntable Prefab" eintragen (siehe dort).
// =====================================================================

// Die Daten einer Drehscheibe (werden mit gespeichert).
public class TurntableData
{
    public int id;
    public Vector3 center;
    public float baseYaw;        // Richtung (Grad), in die Ausgang 0 zeigt
    public int slots;            // Anzahl der Ausgaenge (gerade)
    public float radius;         // Halbmesser der Grube (Meter)
    public int[] nodeIds;        // die Gleispunkte der Ausgaenge
    public int bridgeEdge;       // Nummer des Brueckenstuecks
    public int slot;             // Das Brueckenende "a" steht auf Ausgang "slot" (0 .. slots-1), das Ende "b" gegenueber

    public float StepAngle { get { return 360f / slots; } }

    // Richtung (Grad), in die Ausgang k von der Mitte wegzeigt.
    public float SlotYaw(int k)
    {
        return baseYaw + k * StepAngle;
    }

    // Ort von Ausgang k am Grubenrand.
    public Vector3 SlotPos(int k)
    {
        return center + TrackEdge.YawToDir(SlotYaw(k)) * radius;
    }
}

// Der Pfosten neben einem Ausgang, zum Anzielen.
public class TurntableMarker : MonoBehaviour, IInteractable, ISelfNetworked
{
    [HideInInspector] public int ttId;
    [HideInInspector] public int slot;
    [HideInInspector] public Renderer lamp;

    // Bedienen wie jeden Schalter (F): Bruecke auf diesen Ausgang drehen.
    public string GetPrompt()
    {
        return TrackManager.Instance != null ? TrackManager.Instance.TurntablePrompt(ttId, slot) : "";
    }

    public void Interact()
    {
        if (TrackManager.Instance != null)
            TrackManager.Instance.RequestTurntableRotate(ttId, slot);
    }
}

// Optik und Drehbewegung einer Drehscheibe.
public class TurntableView : MonoBehaviour
{
    public TurntableData data;

    // Die Bruecke, solange sie ausgehaengt ist (waehrend der Drehung).
    [System.NonSerialized] public TrackEdge detached;
    [System.NonSerialized] public int finishSlot;   // Ausgang, auf dem das Ende "a" nach der Drehung steht

    private Transform bridge;
    private TurntableMarker[] markers;
    private Material lampOn, lampOff;

    private float angle;          // aktuelle Richtung der Bruecke (Grad)
    private float targetAngle;
    private float speed = 20f;
    private bool moving;

    public bool IsMoving { get { return moving; } }

    // Baut die Optik. prefab darf null sein (dann Platzhalter aus Quadern).
    public void Build(TurntableData d, GameObject prefab, float turnSpeed)
    {
        data = d;
        speed = Mathf.Max(1f, turnSpeed);

        transform.position = d.center;
        transform.rotation = Quaternion.identity;

        // Das Objekt liegt in Weltkoordinaten (wie die Gleise): Groesse des Elternobjekts ausgleichen.
        if (transform.parent != null)
        {
            Vector3 ps = transform.parent.lossyScale;
            transform.localScale = new Vector3(
                ps.x != 0f ? 1f / ps.x : 1f, ps.y != 0f ? 1f / ps.y : 1f, ps.z != 0f ? 1f / ps.z : 1f);
        }

        lampOn = TrackManager.MakeMaterial(new Color(0.2f, 0.9f, 0.25f), 0f);
        lampOff = TrackManager.MakeMaterial(new Color(0.5f, 0.5f, 0.5f), 0f);

        if (prefab != null)
        {
            GameObject model = Instantiate(prefab, transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            bridge = model.transform.Find("Bridge");
        }
        else
        {
            bridge = BuildPlaceholder(d.radius);
        }

        BuildMarkers();

        angle = d.SlotYaw(d.slot);
        targetAngle = angle;
        ApplyAngle();
        RefreshMarkers();
    }

    // ---------- Platzhalter ----------

    private Transform BuildPlaceholder(float radius)
    {
        Color concrete = new Color(0.36f, 0.36f, 0.38f);
        Color rim = new Color(0.55f, 0.55f, 0.57f);
        Color steel = new Color(0.22f, 0.22f, 0.25f);
        Color wood = new Color(0.33f, 0.22f, 0.13f);
        Color rail = new Color(0.30f, 0.30f, 0.33f);

        // Grube: flache Scheibe auf dem Boden (Zylinder, 4 cm hoch) und ein hellerer Rand.
        Disc("Rand", new Vector3(0f, 0.01f, 0f), (radius + 0.45f) * 2f, 0.02f, rim);
        Disc("Grube", new Vector3(0f, 0.02f, 0f), radius * 2f, 0.04f, concrete);

        // Begehbarer Boden (flache Box), damit man auf der Grube nicht einsinkt.
        GameObject floor = new GameObject("Boden");
        floor.transform.SetParent(transform, false);
        BoxCollider box = floor.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.0f, 0f);
        box.size = new Vector3(radius * 2f, 0.1f, radius * 2f);

        // Bruecke: Traeger, Schwellen und zwei Schienen. Laenge entlang Z, Oberkante wie beim Schotterbett.
        GameObject root = new GameObject("Bridge");
        root.transform.SetParent(transform, false);

        float length = radius * 2f;

        Part(root.transform, "Traeger", new Vector3(0f, 0.11f, 0f),
             new Vector3(TrackSpec.BallastHalfTop * 2f, 0.18f, length), steel);
        Part(root.transform, "Schwellen", new Vector3(0f, 0.25f, 0f),
             new Vector3(TrackSpec.SleeperLength, TrackSpec.SleeperHeight, length - 0.2f), wood);

        float railX = TrackSpec.Gauge * 0.5f + TrackSpec.RailHeadWidth * 0.5f;
        float railY = TrackSpec.BallastHeight + TrackSpec.SleeperHeight + TrackSpec.RailHeight * 0.5f;

        Part(root.transform, "SchieneL", new Vector3(-railX, railY, 0f),
             new Vector3(TrackSpec.RailHeadWidth, TrackSpec.RailHeight, length), rail);
        Part(root.transform, "SchieneR", new Vector3(railX, railY, 0f),
             new Vector3(TrackSpec.RailHeadWidth, TrackSpec.RailHeight, length), rail);

        return root.transform;
    }

    private void Disc(string name, Vector3 pos, float diameter, float height, Color color)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = name;
        g.transform.SetParent(transform, false);
        g.transform.localPosition = pos;

        // Der Unity-Zylinder ist 2 Einheiten hoch.
        g.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
        g.GetComponent<Renderer>().sharedMaterial = TrackManager.MakeMaterial(color, 0f);
        Destroy(g.GetComponent<Collider>());
    }

    private static void Part(Transform parent, string name, Vector3 pos, Vector3 size, Color color)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = TrackManager.MakeMaterial(color, 0.2f);
        Destroy(g.GetComponent<Collider>());
    }

    // ---------- Pfosten an den Ausgaengen ----------

    private void BuildMarkers()
    {
        markers = new TurntableMarker[data.slots];

        for (int k = 0; k < data.slots; k++)
        {
            float yaw = data.SlotYaw(k);
            Vector3 dir = TrackEdge.YawToDir(yaw);
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);

            // Neben dem Gleis, knapp hinter dem Grubenrand.
            Vector3 pos = data.center + dir * (data.radius + 0.6f) + right * 1.7f;

            GameObject post = new GameObject("Ausgang_" + k);
            post.transform.SetParent(transform, true);
            post.transform.position = pos;
            post.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            Part(post.transform, "Pfosten", new Vector3(0f, 0.45f, 0f), new Vector3(0.18f, 0.9f, 0.18f),
                 new Color(0.2f, 0.2f, 0.22f));

            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = "Lampe";
            lamp.transform.SetParent(post.transform, false);
            lamp.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            lamp.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
            Destroy(lamp.GetComponent<Collider>());

            // Zum Zielen: ein Trigger-Collider.
            BoxCollider trigger = post.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.7f, 0f);
            trigger.size = new Vector3(0.8f, 1.4f, 0.8f);

            TurntableMarker marker = post.AddComponent<TurntableMarker>();
            marker.ttId = data.id;
            marker.slot = k;
            marker.lamp = lamp.GetComponent<Renderer>();
            markers[k] = marker;
        }
    }

    // Die Lampen der beiden Ausgaenge, an denen die Bruecke steht, leuchten gruen
    // (waehrend der Drehung sind alle grau).
    public void RefreshMarkers()
    {
        if (markers == null)
            return;

        int half = data.slots / 2;

        for (int k = 0; k < markers.Length; k++)
        {
            bool aligned = !moving && (k % half) == (data.slot % half);

            if (markers[k] != null && markers[k].lamp != null)
                markers[k].lamp.sharedMaterial = aligned ? lampOn : lampOff;
        }
    }

    // ---------- Drehen ----------

    // Dreht die Bruecke so, dass ihr Ende "a" auf Ausgang "newSlot" (0 .. slots-1) steht.
    // Der Weg ist der kuerzeste (hoechstens 90 Grad), die Bruecke ist ja symmetrisch.
    public void TurnTo(int newSlot)
    {
        finishSlot = newSlot;
        targetAngle = angle + Mathf.DeltaAngle(angle, data.SlotYaw(newSlot));
        moving = true;
        RefreshMarkers();
    }

    void Update()
    {
        if (!moving)
            return;

        angle = Mathf.MoveTowards(angle, targetAngle, speed * Time.deltaTime);

        if (Mathf.Approximately(angle, targetAngle))
        {
            moving = false;
            data.slot = finishSlot;
            angle = data.SlotYaw(finishSlot);
            targetAngle = angle;
            ApplyAngle();

            // Jetzt wird die Bruecke an die neuen Ausgaenge gehaengt.
            if (TrackManager.Instance != null)
                TrackManager.Instance.FinishTurntable(data.id);

            RefreshMarkers();
            return;
        }

        ApplyAngle();
    }

    // Stellt die Bruecke (Optik und Gleisstueck) auf die aktuelle Richtung ein.
    // Das Gleisstueck wird jeden Schritt neu berechnet, damit Fahrzeuge darauf mitdrehen.
    private void ApplyBridgeEdge()
    {
        if (detached == null)
            return;

        Vector3 dir = TrackEdge.YawToDir(angle);
        Vector3 pa = data.center + dir * data.radius;
        Vector3 pb = data.center - dir * data.radius;

        detached.yawStart = angle + 180f;
        detached.yawEnd = angle + 180f;
        detached.Build(pa, pb);
    }

    private void ApplyAngle()
    {
        if (bridge != null)
            bridge.rotation = Quaternion.Euler(0f, angle, 0f);

        ApplyBridgeEdge();
    }
}
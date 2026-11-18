using System.Collections.Generic;
using System.IO;
using UnityEngine;

// =====================================================================
//  TrainCar: Grundlage fuer alles, was auf Gleisen faehrt (Lok, spaeter Wagen).
//
//  Der Wagen steht mit seiner Mitte auf dem Gleis ("pos"). Vorderachse und Hinterachse
//  liegen "wheelBase" Meter auseinander; aus beiden Punkten ergibt sich, wohin der
//  Wagen zeigt - dadurch fuehrt er Kurven ganz natuerlich aus.
//
//  Spieler und Gegenstaende, die auf dem Wagen stehen, werden mitgenommen
//  (wie beim Aufzug).
//
//  Eigene Modelle: Das Modell kann ein beliebiges Prefab sein. Es muss nur so gebaut sein:
//   - Ursprung (Pivot) in der Mitte des Wagens, auf Hoehe der Schienen-OBERKANTE,
//   - Vorne = +Z,
//   - mindestens ein Collider als "Boden" (damit man darauf stehen kann).
// =====================================================================
// Frueh ausfuehren: Die Mitfahrer werden vor der Kamera mitbewegt (sonst ruckelt das Bild).
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Rigidbody))]
public abstract class TrainCar : MonoBehaviour
{
    // Alle Wagen der Welt.
    public static readonly List<TrainCar> All = new List<TrainCar>();

    [Header("Fahrwerk")]
    [Tooltip("Abstand zwischen Vorder- und Hinterachse (Meter).")]
    public float wheelBase = 2f;
    [Tooltip("Hoehe des Ursprungs ueber dem Gleisbett. 0,39 = Schienen-Oberkante.")]
    public float heightOffset = 0.39f;

    [Tooltip("Wie weit der Wagen ueber die VORDERachse hinausragt: so nah faehrt er ans Gleisende (Prellbock).")]
    public float frontClearance = 1f;
    [Tooltip("Wie weit der Wagen ueber die HINTERachse hinausragt (die Test-Lok hat hinten den Tender).")]
    public float rearClearance = 2.5f;

    [Header("Mitfahrer")]
    [Tooltip("Bereich (Mitte relativ zum Wagen), in dem Spieler und Gegenstaende mitgenommen werden.")]
    public Vector3 riderOffset = new Vector3(0f, 1.2f, -0.7f);
    public Vector3 riderHalfExtents = new Vector3(0.9f, 1.2f, 2.8f);

    // Position auf dem Gleis und Geschwindigkeit (m/s, positiv = nach vorne).
    [HideInInspector] public TrackPos pos;
    [HideInInspector] public float speed;

    // Nummer des Fahrzeugs (bei allen Spielern gleich; 0 = keine). Wird vom TrackManager vergeben.
    [HideInInspector] public int NetId;

    // Mitspieler: so viele Meter liegt die eigene Position gegenueber dem Host zurueck/vorn
    // (wird weich ausgeglichen).
    private float netCorrection;

    // Steht ein Spieler auf dem Wagen?
    public bool PlayerOnBoard { get; private set; }

    protected Rigidbody body;

    // ---------- Masse ----------
    // Gewicht dieses Fahrzeugs in Tonnen (Leergewicht plus Ladung/Vorraete). Lok und Wagen ueberschreiben das.
    public virtual float MassTons { get { return 10f; } }

    // Wie schwer ein Zug sein darf, bevor diese Lok langsamer beschleunigt (Tonnen). Nur Loks haben einen Wert.
    public virtual float RatedMassTons { get { return 0f; } }

    private static readonly List<TrainCar> massBuf = new List<TrainCar>();

    // Gesamtgewicht und Gesamt-Zugkraft (Summe der Nennlasten aller Loks) des Zuges, zu dem dieses Fahrzeug gehoert.
    public void GetConsistMass(out float totalTons, out float ratedTons)
    {
        totalTons = 0f;
        ratedTons = 0f;

        CollectConsist(massBuf);

        for (int i = 0; i < massBuf.Count; i++)
        {
            totalTons += massBuf[i].MassTons;
            ratedTons += massBuf[i].RatedMassTons;
        }
    }

    // ---------- Kupplungen ----------
    // Abstand der Puffer zweier gekuppelter Wagen (Meter).
    public const float CouplerGap = 0.5f;

    // Einstellungen (kommen vom TrackManager > Wagen).
    public static bool AutoCouple = true;
    public static float CouplingSpeed = 2f;

    // Nachbarn: Ende 0 = vorne, Ende 1 = hinten. "...End" sagt, welches Ende des Nachbarn dort haengt.
    [HideInInspector] public TrainCar frontLink;
    [HideInInspector] public TrainCar rearLink;
    [HideInInspector] public int frontLinkEnd;
    [HideInInspector] public int rearLinkEnd;

    // Nach dem Abkuppeln kuppelt der Wagen bis zu dieser Zeit nicht von selbst wieder an.
    [HideInInspector] public float noCoupleUntil;

    private bool couplersBuilt;

    private readonly List<CharacterController> riders = new List<CharacterController>();
    private static readonly Collider[] hitBuffer = new Collider[64];

    // Pose im letzten Bild (fuer das gleichmaessige Mitnehmen der Spieler).
    private Vector3 lastPos;
    private Quaternion lastRot;
    private bool hasLast;

    protected virtual void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    protected virtual void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);

        // Das Gelaende rund um den Wagen geladen halten.
        VoxelWorld.AddAnchor(transform, 2);
    }

    protected virtual void OnDisable()
    {
        All.Remove(this);
        VoxelWorld.RemoveAnchor(transform);
    }

    protected virtual void OnDestroy()
    {
        // Beim Entfernen von den Nachbarn abkuppeln.
        Uncouple(this, 0);
        Uncouple(this, 1);
    }

    // Stellt den Wagen sofort auf das Gleis.
    public void Place(TrackPos p)
    {
        pos = p;
        speed = 0f;
        hasLast = false;

        EnsureCouplers();

        TrackManager m = TrackManager.Instance;

        if (m == null)
            return;

        Vector3 center;
        Quaternion rot;

        // Das Ergebnis ist egal (auch bei zu kurzem Gleis wird der Wagen hingestellt).
        TryPose(m.Network, pos, out center, out rot);

        transform.SetPositionAndRotation(center, rot);

        if (body == null)
            body = GetComponent<Rigidbody>();

        body.position = center;
        body.rotation = rot;
    }

    // Wo steht der Wagen, wenn seine Mitte bei "at" ist? false, wenn eine Achse ueber das Gleisende ragt.
    private bool TryPose(TrackNetwork net, TrackPos at, out Vector3 center, out Quaternion rot)
    {
        center = transform.position;
        rot = transform.rotation;

        TrackPos front = at, rear = at;

        bool okFront = net.Move(ref front, wheelBase * 0.5f);
        bool okRear = net.Move(ref rear, -wheelBase * 0.5f);

        // Der Wagen ragt vorne und hinten ueber die Achsen hinaus: Er soll vor dem Gleisende
        // (Prellbock) anhalten und nicht hineinfahren.
        TrackPos frontEnd = at, rearEnd = at;

        okFront &= net.Move(ref frontEnd, wheelBase * 0.5f + frontClearance);
        okRear &= net.Move(ref rearEnd, -(wheelBase * 0.5f + rearClearance));

        Vector3 pf, pr, ff, fr;

        if (!net.Pose(front, out pf, out ff) || !net.Pose(rear, out pr, out fr))
            return false;

        Vector3 forward = pf - pr;

        if (forward.sqrMagnitude < 1e-6f)
            forward = ff;

        center = (pf + pr) * 0.5f + Vector3.up * heightOffset;
        rot = Quaternion.LookRotation(forward.normalized, Vector3.up);

        return okFront && okRear;
    }

    // Wird jeden Physik-Schritt aufgerufen: hier legt die Lok ihre Geschwindigkeit fest.
    protected virtual void StepTrain(float dt)
    {
    }

    void FixedUpdate()
    {
        TrackManager m = TrackManager.Instance;

        if (m == null)
            return;

        float dt = Time.fixedDeltaTime;
        TrackNetwork net = m.Network;

        Vector3 oldPos = body.position;
        Quaternion oldRot = body.rotation;

        CollectRiders(oldPos, oldRot);

        // Mitspieler rechnen die Fahrt nicht selbst, sondern folgen dem Host (siehe TrainNet).
        bool follower = NetSync.IsClient && NetId > 0;

        // Gekuppelte Fahrzeuge bewegt der "Fuehrer" des Zuges (die Lok, sonst der Wagen mit der
        // kleinsten Nummer). Alle anderen werden von ihm mitgenommen.
        if (!follower && (frontLink != null || rearLink != null) && !IsConsistRoot())
            return;

        if (!follower)
            StepTrain(dt);

        float step = speed * dt;

        if (follower)
        {
            // Den Abstand zum Host nach und nach ausgleichen.
            float k = Mathf.Min(1f, dt * 4f);
            float part = netCorrection * k;

            netCorrection -= part;
            step += part;
        }

        TrainCar coupleA = null, coupleB = null;
        int coupleEndA = 0, coupleEndB = 0;

        if (Mathf.Abs(step) > 0.00001f)
        {
            TrackPos before = pos;
            bool ok = net.Move(ref pos, step);

            Vector3 center;
            Quaternion rot;

            bool moved = ok && TryPose(net, pos, out center, out rot);

            // Ganzen Zug mitnehmen. 0 = frei, 1 = blockiert (Puffer), 2 = ankuppeln.
            int result = 0;

            if (moved && !follower)
                result = StepConsist(net, out coupleA, out coupleEndA, out coupleEndB, out coupleB);

            if (!moved || result != 0)
            {
                // Gleisende oder Hindernis erreicht: stehen bleiben.
                pos = before;

                if (!follower && result != 2)
                    speed = 0f;
            }
            else
            {
                coupleA = null;
            }
        }

        Vector3 newPos;
        Quaternion newRot;

        if (TryPose(net, pos, out newPos, out newRot))
        {
            body.MovePosition(newPos);
            body.MoveRotation(newRot);
        }

        // Langsam angefahren und Puffer beruehren sich: ankuppeln (Kommando, damit alle es sehen).
        if (coupleA != null && coupleB != null)
            NetSync.Submit(NetCommand.Couple(coupleA.NetId, coupleEndA, coupleB.NetId, coupleEndB));
    }

    // =====================================================================
    //  Zuege: Kupplungen
    // =====================================================================

    private static readonly List<TrainCar> consistBuf = new List<TrainCar>();

    private struct Slot
    {
        public TrainCar car;
        public TrackPos pos;
        public Vector3 center;
        public Quaternion rot;
        public bool flip;      // steht verkehrt herum zum Fuehrer
    }

    private static readonly List<Slot> slotBuf = new List<Slot>();

    // Alle Fahrzeuge, die mit diesem verbunden sind (inklusive diesem).
    public void CollectConsist(List<TrainCar> list)
    {
        list.Clear();
        list.Add(this);

        for (int i = 0; i < list.Count; i++)
        {
            TrainCar c = list[i];

            if (c.frontLink != null && !list.Contains(c.frontLink))
                list.Add(c.frontLink);

            if (c.rearLink != null && !list.Contains(c.rearLink))
                list.Add(c.rearLink);
        }
    }

    // Der Fuehrer eines Zuges: die Lok mit der kleinsten Nummer, sonst das Fahrzeug mit der kleinsten Nummer.
    private static TrainCar PickRoot(List<TrainCar> list)
    {
        TrainCar best = null;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is Locomotive && (best == null || list[i].NetId < best.NetId))
                best = list[i];
        }

        if (best != null)
            return best;

        best = list[0];

        for (int i = 1; i < list.Count; i++)
        {
            if (list[i].NetId > 0 && (best.NetId <= 0 || list[i].NetId < best.NetId))
                best = list[i];
        }

        return best;
    }

    private bool IsConsistRoot()
    {
        CollectConsist(consistBuf);
        return PickRoot(consistBuf) == this;
    }

    // Abstand von der Wagenmitte bis zum Ende (0 = vorne, 1 = hinten).
    public float EndDist(int end)
    {
        return wheelBase * 0.5f + (end == 0 ? frontClearance : rearClearance);
    }

    public TrainCar LinkAt(int end)
    {
        return end == 0 ? frontLink : rearLink;
    }

    public int LinkEndAt(int end)
    {
        return end == 0 ? frontLinkEnd : rearLinkEnd;
    }

    private void SetLink(int end, TrainCar other, int otherEnd)
    {
        if (end == 0)
        {
            frontLink = other;
            frontLinkEnd = otherEnd;
        }
        else
        {
            rearLink = other;
            rearLinkEnd = otherEnd;
        }
    }

    // Weltposition eines Endes (Pufferhoehe).
    public Vector3 EndPoint(int end)
    {
        return transform.position + transform.forward * (end == 0 ? EndDist(0) : -EndDist(1));
    }

    // Kuppelt zwei Enden zusammen. Gibt false zurueck, wenn es nicht geht
    // (Ende belegt oder beide gehoeren schon zum selben Zug).
    public static bool Couple(TrainCar a, int ea, TrainCar b, int eb)
    {
        if (a == null || b == null || a == b)
            return false;

        if (a.LinkAt(ea) != null || b.LinkAt(eb) != null)
            return false;

        List<TrainCar> tmp = new List<TrainCar>();
        a.CollectConsist(tmp);

        if (tmp.Contains(b))
            return false;

        a.SetLink(ea, b, eb);
        b.SetLink(eb, a, ea);
        return true;
    }

    public static void Uncouple(TrainCar a, int ea)
    {
        if (a == null)
            return;

        TrainCar b = a.LinkAt(ea);

        if (b == null)
            return;

        int eb = a.LinkEndAt(ea);

        a.SetLink(ea, null, 0);
        b.SetLink(eb, null, 0);

        a.noCoupleUntil = Time.time + 4f;
        b.noCoupleUntil = Time.time + 4f;
    }

    // Stellt nach dem Ankuppeln alle Wagen des Zuges richtig hinter den Fuehrer.
    public void SnapConsist()
    {
        TrackManager m = TrackManager.Instance;

        if (m == null)
            return;

        CollectConsist(consistBuf);
        TrainCar root = PickRoot(consistBuf);

        TrainCar a, b;
        int ea, eb;
        root.StepConsist(m.Network, out a, out ea, out eb, out b, true);
    }

    private int SlotIndex(TrainCar car)
    {
        for (int i = 0; i < slotBuf.Count; i++)
        {
            if (slotBuf[i].car == car)
                return i;
        }

        return -1;
    }

    // Berechnet, wo alle Wagen des Zuges stehen, wenn der Fuehrer (dieses Fahrzeug) bei "pos" steht, und
    // stellt sie dorthin. Rueckgabe: 0 = ok, 1 = blockiert (Puffer stossen an), 2 = Ankuppeln gewuenscht
    // (dann sind cA/eA/cB/eB gesetzt). Bei 1 und 2 wird NICHTS bewegt.
    private int StepConsist(TrackNetwork net, out TrainCar cA, out int eA, out int eB, out TrainCar cB,
                            bool forceApply = false)
    {
        cA = null;
        cB = null;
        eA = 0;
        eB = 0;

        slotBuf.Clear();

        Vector3 c;
        Quaternion r;

        if (!TryPose(net, pos, out c, out r))
            return 1;

        Slot rootSlot = new Slot();
        rootSlot.car = this;
        rootSlot.pos = pos;
        rootSlot.center = c;
        rootSlot.rot = r;
        slotBuf.Add(rootSlot);

        // Nachbarn der Reihe nach hinter bzw. vor den Fuehrer stellen.
        for (int i = 0; i < slotBuf.Count; i++)
        {
            Slot s = slotBuf[i];

            for (int end = 0; end < 2; end++)
            {
                TrainCar n = s.car.LinkAt(end);

                if (n == null || SlotIndex(n) >= 0)
                    continue;

                int ne = s.car.LinkEndAt(end);
                float d = s.car.EndDist(end) + n.EndDist(ne) + CouplerGap;

                TrackPos p = s.pos;

                if (!net.Move(ref p, end == 0 ? d : -d))
                    return 1;

                // Front an Front bzw. Heck an Heck: der Nachbar steht verkehrt herum.
                bool flipped = end == ne;

                if (flipped)
                    p.dir = -p.dir;

                Vector3 nc;
                Quaternion nr;

                if (!n.TryPose(net, p, out nc, out nr))
                    return 1;

                Slot ns = new Slot();
                ns.car = n;
                ns.pos = p;
                ns.center = nc;
                ns.rot = nr;
                ns.flip = s.flip ^ flipped;
                slotBuf.Add(ns);
            }
        }

        // Beruehren die freien Enden dieses Zuges ein fremdes Fahrzeug?
        for (int i = 0; i < slotBuf.Count; i++)
        {
            Slot s = slotBuf[i];

            for (int end = 0; end < 2; end++)
            {
                if (s.car.LinkAt(end) != null)
                    continue;

                Vector3 mine = s.center + s.rot * Vector3.forward * (end == 0 ? s.car.EndDist(0) : -s.car.EndDist(1));
                Vector3 motion = s.center - s.car.transform.position;

                for (int k = 0; k < All.Count; k++)
                {
                    TrainCar o = All[k];

                    if (o == null || SlotIndex(o) >= 0)
                        continue;

                    for (int oe = 0; oe < 2; oe++)
                    {
                        Vector3 delta = o.EndPoint(oe) - mine;

                        if (Mathf.Abs(delta.y) > 1.2f)
                            continue;

                        delta.y = 0f;

                        if (delta.magnitude > CouplerGap + 0.15f)
                            continue;

                        // Nur wenn man sich aufeinander zu bewegt (sonst darf man wegfahren).
                        if (Vector3.Dot(motion, delta) <= 0f)
                            continue;

                        bool canCouple = AutoCouple && o.LinkAt(oe) == null &&
                                         Mathf.Abs(speed) <= CouplingSpeed &&
                                         Time.time >= s.car.noCoupleUntil && Time.time >= o.noCoupleUntil &&
                                         s.car.NetId > 0 && o.NetId > 0;

                        if (canCouple)
                        {
                            cA = s.car;
                            eA = end;
                            cB = o;
                            eB = oe;
                            return 2;
                        }

                        return 1;
                    }
                }
            }
        }

        // Alles frei: die Nachbarn hinstellen.
        for (int i = 1; i < slotBuf.Count; i++)
        {
            Slot s = slotBuf[i];

            s.car.pos = s.pos;
            s.car.speed = speed * (s.flip ? -1f : 1f);

            if (s.car.body != null)
            {
                if (forceApply)
                    s.car.body.position = s.center;

                s.car.body.MovePosition(s.center);
                s.car.body.MoveRotation(s.rot);
            }
        }

        return 0;
    }

    // ---------- Kupplungs-Griffe ----------

    // Legt an beiden Enden einen Griff zum Kuppeln an (falls das Modell keine eigenen mitbringt).
    private void EnsureCouplers()
    {
        if (couplersBuilt)
            return;

        couplersBuilt = true;

        CarCoupler[] existing = GetComponentsInChildren<CarCoupler>(true);

        for (int i = 0; i < existing.Length; i++)
            existing[i].car = this;

        if (existing.Length > 0)
            return;

        for (int end = 0; end < 2; end++)
        {
            GameObject g = new GameObject(end == 0 ? "KupplungVorn" : "KupplungHinten");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(0f, 0.55f, end == 0 ? EndDist(0) - 0.15f : -(EndDist(1) - 0.15f));

            BoxCollider box = g.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.7f, 0.5f, 0.7f);

            CarCoupler coupler = g.AddComponent<CarCoupler>();
            coupler.car = this;
            coupler.end = end;

            // Platzhalter: kleiner Kupplungshaken.
            GameObject hook = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hook.name = "Haken";
            hook.transform.SetParent(g.transform, false);
            hook.transform.localPosition = new Vector3(0f, 0f, end == 0 ? 0.2f : -0.2f);
            hook.transform.localScale = new Vector3(0.12f, 0.12f, 0.4f);
            hook.GetComponent<Renderer>().sharedMaterial = TrackManager.MakeMaterial(new Color(0.2f, 0.2f, 0.22f), 0.2f);
            Destroy(hook.GetComponent<Collider>());
        }
    }

    // Anderes Fahrzeug mit freiem Ende in Reichweite des Endes "end" (oder null).
    public TrainCar FindCouplePartner(int end, out int partnerEnd)
    {
        partnerEnd = 0;

        List<TrainCar> mine = new List<TrainCar>();
        CollectConsist(mine);

        Vector3 p = EndPoint(end);
        TrainCar best = null;
        float bestDist = 2f;

        foreach (TrainCar o in All)
        {
            if (o == null || o.NetId <= 0 || mine.Contains(o))
                continue;

            for (int oe = 0; oe < 2; oe++)
            {
                if (o.LinkAt(oe) != null)
                    continue;

                float d = Vector3.Distance(o.EndPoint(oe), p);

                if (d < bestDist)
                {
                    bestDist = d;
                    best = o;
                    partnerEnd = oe;
                }
            }
        }

        return best;
    }

    // Die Mitfahrer werden JEDES BILD mit der (geglaetteten) Bewegung des Wagens mitgenommen,
    // nicht nur bei jedem Physik-Schritt. Sonst bewegt sich der Wagen glatt, der Spieler aber
    // ruckartig - das sah wie Ruckeln aus.
    void LateUpdate()
    {
        Vector3 p = transform.position;
        Quaternion r = transform.rotation;

        if (hasLast && riders.Count > 0)
            CarryRiders(lastPos, p, lastRot, r);

        lastPos = p;
        lastRot = r;
        hasLast = true;
    }

    // Sucht Spieler im Mitfahr-Bereich.
    private void CollectRiders(Vector3 position, Quaternion rotation)
    {
        riders.Clear();
        PlayerOnBoard = false;

        int count = Physics.OverlapBoxNonAlloc(
            position + rotation * riderOffset, riderHalfExtents, hitBuffer, rotation,
            ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            CharacterController controller = hitBuffer[i] as CharacterController;

            if (controller != null && !riders.Contains(controller))
            {
                riders.Add(controller);
                PlayerOnBoard = true;
            }
        }
    }

    // Nimmt die Spieler mit: Verschiebung PLUS Drehung um die Wagenmitte (Kurven).
    private void CarryRiders(Vector3 oldPos, Vector3 newPos, Quaternion oldRot, Quaternion newRot)
    {
        if (riders.Count == 0)
            return;

        float yawDelta = Mathf.DeltaAngle(oldRot.eulerAngles.y, newRot.eulerAngles.y);
        Quaternion turn = Quaternion.Euler(0f, yawDelta, 0f);

        foreach (CharacterController controller in riders)
        {
            if (controller == null || !controller.enabled)
                continue;

            Vector3 p = controller.transform.position;
            Vector3 target = newPos + turn * (p - oldPos);

            controller.Move(target - p);
        }
    }

    // ---------- Speichern ----------

    // Text fuer den Spielstand. Das erste Feld sagt, um welche Art Wagen es sich handelt.
    public abstract string Serialize();

    // Liest die Werte zurueck (s = Text an den ";" aufgeteilt).
    public abstract void Deserialize(string[] s);

    // ---------- Mehrspieler ----------

    // Schreibt den Fahrzustand (Host -> Mitspieler). Unterklassen haengen eigene Werte an.
    public virtual void WriteNetState(BinaryWriter w)
    {
        w.Write(pos.edge);
        w.Write(pos.dist);
        w.Write(pos.dir);
        w.Write(speed);
    }

    // Liest den Fahrzustand des Hosts. Ist der Abstand klein, wird er weich ausgeglichen,
    // sonst springt der Wagen sofort hin.
    public virtual void ReadNetState(BinaryReader r)
    {
        TrackPos host = new TrackPos();
        host.edge = r.ReadInt32();
        host.dist = r.ReadSingle();
        host.dir = r.ReadInt32();
        speed = r.ReadSingle();

        if (host.edge == pos.edge && host.dir == pos.dir)
        {
            float error = (host.dist - pos.dist) * pos.dir;

            if (Mathf.Abs(error) > 4f)
            {
                pos = host;
                netCorrection = 0f;
            }
            else
            {
                netCorrection = error;
            }
        }
        else
        {
            pos = host;
            netCorrection = 0f;
        }
    }

    // Naechster Wagen in der Naehe eines Punktes (oder null).
    public static TrainCar FindNear(Vector3 point, float radius)
    {
        TrainCar best = null;
        float bestDist = radius;

        foreach (TrainCar car in All)
        {
            if (car == null)
                continue;

            float d = Vector3.Distance(car.transform.position, point);

            if (d < bestDist)
            {
                bestDist = d;
                best = car;
            }
        }

        return best;
    }
}
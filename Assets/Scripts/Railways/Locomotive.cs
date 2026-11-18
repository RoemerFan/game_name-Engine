using System.Globalization;
using System.IO;
using UnityEngine;

// =====================================================================
//  Locomotive: das Fahrverhalten und die Bedienung einer Lok.
//
//  Eine Lok besteht aus drei austauschbaren Teilen (siehe TrackManager > Locomotives):
//    - KOERPER   (dieses Skript + Plattform, Fuehrerstand, Raeder)
//    - ANTRIEB   (TrainEngine: Dampf, Diesel oder Elektro) am "Engine Socket"
//    - TENDER    (TrainTender: Kohlelager, Dieseltank oder Batterie) am "Tender Socket"
//
//  Bedienung, solange man AUF der Lok steht (alle Tasten stehen im Inspektor):
//    R / F  = Regler hoeher / niedriger (haelt seine Stellung, auch wenn man absteigt)
//    V      = Fahrtrichtung umschalten (nur im Stand)
//    B      = Bremse (gedrueckt halten)
//    H      = Pfeife
//
//  Im Mehrspieler rechnet der Host die Fahrt; Mitspieler koennen die Lok bedienen (siehe TrainNet).
//
//  SPAETER (geplant): Regler, Fahrtrichtung und Bremse werden echte Hebel im Fuehrerstand.
//
//  EIGENER KOERPER: Prefab mit diesem Skript, Vorne = +Z, Ursprung = Mitte auf Schienen-Oberkante
//  (siehe TrainCar). Optional "Engine Socket" und "Tender Socket" setzen: leere Objekte, an
//  denen Antrieb bzw. Tender eingesetzt werden (leer = Mitte der Lok).
// =====================================================================
public class Locomotive : TrainCar
{
    [Header("Fahrverhalten")]
    [Tooltip("Hoechstgeschwindigkeit in m/s (6 = ca. 22 km/h).")]
    public float maxSpeed = 6f;
    [Tooltip("Beschleunigung bei vollem Regler (m/s^2).")]
    public float acceleration = 0.8f;
    public float brakeDeceleration = 2f;
    [Tooltip("Ausrollen ohne Regler (m/s^2).")]
    public float rollingResistance = 0.2f;

    [Header("Austauschbare Teile")]
    [Tooltip("Hier wird der Antrieb eingesetzt (leer = Mitte der Lok).")]
    public Transform engineSocket;
    [Tooltip("Hier wird der Tender eingesetzt (leer = Mitte der Lok).")]
    public Transform tenderSocket;

    // Wird vom TrackManager gesetzt (fuer das Speichern).
    [HideInInspector] public string recipeId = "";

    // Die eingebauten Teile (werden automatisch gefunden).
    public TrainEngine engine;
    public TrainTender tender;

    [Header("Bedienung")]
    public KeyCode throttleUpKey = KeyCode.R;
    public KeyCode throttleDownKey = KeyCode.F;
    public KeyCode reverserKey = KeyCode.V;
    public KeyCode brakeKey = KeyCode.B;
    public KeyCode hornKey = KeyCode.H;
    [Tooltip("Wie schnell sich der Regler bewegt (pro Sekunde).")]
    public float throttleRate = 0.4f;
    public AudioClip hornClip;

    [Header("Gewicht")]
    [Tooltip("Gewicht der Lok selbst ohne Tender-Vorraete (Tonnen).")]
    public float bodyMassTons = 35f;

    [Tooltip("So viel Zug (Tonnen, inklusive der Lok selbst) zieht die Lok mit voller Beschleunigung. " +
             "Ist der Zug schwerer, beschleunigt und bremst er traeger. Mehrere Loks im Zug addieren sich.")]
    public float ratedMassTons = 100f;

    [Tooltip("Der Zug wird nie traeger als dieser Anteil (0.1 = hoechstens 10 Mal so traege).")]
    [Range(0.05f, 1f)] public float minMassFactor = 0.15f;

    public override float MassTons
    {
        get { return bodyMassTons + (tender != null ? tender.MassTons : 0f); }
    }

    public override float RatedMassTons { get { return ratedMassTons; } }

    // Zustand.
    [Range(0f, 1f)] public float throttle;
    public int reverser = 1;     // +1 = vorwaerts, -1 = rueckwaerts
    public bool braking;

    private AudioSource hornSource;

    protected override void Awake()
    {
        base.Awake();
        RefreshParts();
    }

    // Sucht Antrieb und Tender neu (nach dem Einsetzen oder Austauschen).
    public void RefreshParts()
    {
        engine = GetComponentInChildren<TrainEngine>();
        tender = GetComponentInChildren<TrainTender>();
    }

    // Verbraucht Brennstoff fuer diesen Schritt und gibt die verfuegbare Leistung (0..1) zurueck.
    // Ohne Antrieb: immer volle Leistung (zum Testen).
    protected virtual float UseFuel(float dt)
    {
        if (engine == null)
            return 1f;

        return engine.UseFuel(throttle, dt, tender);
    }

    // ---------- Bedienung ----------

    // Mehrspieler: Bedienung eines Mitspielers (nur der Host wertet sie aus).
    private float remoteUntil;
    private float remoteThrottle;
    private int remoteReverser = 1;
    private bool remoteBraking;

    // Zum Senden der eigenen Bedienung an den Host.
    private float inputTimer;
    private bool wasOnBoard;
    private float sentThrottle = -1f;
    private int sentReverser;
    private bool sentBraking;

    // Der Host bekommt die Bedienung eines Mitspielers (siehe TrainNet).
    public void ApplyRemoteInput(float newThrottle, int newReverser, bool newBraking)
    {
        remoteThrottle = Mathf.Clamp01(newThrottle);
        remoteReverser = newReverser < 0 ? -1 : 1;
        remoteBraking = newBraking;

        // Der Mitspieler schickt alle 0,25 s seine Bedienung, solange er auf der Lok steht.
        remoteUntil = Time.time + 0.8f;
    }

    // Steht der lokale Spieler gerade auf einer Lok und fuehrt sie? (Fuer die Controller-Belegung:
    // Steuerkreuz und Trigger gelten dann fuer die Lok.)
    private static int driveFrame = -10;
    public static bool IsDriving { get { return Time.frameCount - driveFrame <= 1; } }

    void Update()
    {
        // Bedienung per Tastatur nur auf der Lok. Hebel im Fuehrerstand (siehe TrainLever) gehen auch von aussen.
        bool onBoard = PlayerOnBoard && !SettingsMenu.IsOpen;
        bool lever = Time.time < leverUntil;
        bool local = onBoard || lever;

        if (onBoard)
            driveFrame = Time.frameCount;

        if (!local)
        {
            // Mitspieler (nicht Host) uebernehmen die Werte aus dem Zustand des Hosts.
            if (!NetSync.IsClient && Time.time < remoteUntil)
            {
                throttle = remoteThrottle;
                reverser = remoteReverser;
                braking = remoteBraking;
            }
            else if (!NetSync.IsClient)
            {
                braking = false;
            }

            // Gerade abgestiegen: dem Host noch "keine Bremse" melden.
            if (wasOnBoard && NetSync.IsClient)
            {
                braking = false;
                TrainNet.SendInput(this);
            }

            wasOnBoard = PlayerOnBoard;
            return;
        }

        wasOnBoard = true;
        braking = false;

        float dt = Time.deltaTime;

        if (onBoard)
        {
            if (GameInput.Held(GameAction.ThrottleUp, throttleUpKey))
                throttle = Mathf.Min(1f, throttle + throttleRate * dt);

            if (GameInput.Held(GameAction.ThrottleDown, throttleDownKey))
                throttle = Mathf.Max(0f, throttle - throttleRate * dt);

            if (GameInput.Pressed(GameAction.Reverser, reverserKey) && Mathf.Abs(speed) < 0.3f)
                reverser = -reverser;

            braking = GameInput.Held(GameAction.Brake, brakeKey);

            if (GameInput.Pressed(GameAction.Horn, hornKey))
            {
                PlayHorn();
                TrainNet.SendHorn(this);
            }
        }

        // Bremshebel gehalten?
        if (Time.time < leverBrakeUntil)
            braking = true;

        // Mitspieler: Bedienung an den Host schicken (bei Aenderung und alle 0,25 s).
        if (NetSync.IsClient)
        {
            inputTimer -= dt;

            bool changed = !Mathf.Approximately(sentThrottle, throttle) ||
                           sentReverser != reverser || sentBraking != braking;

            if (changed || inputTimer <= 0f)
            {
                inputTimer = 0.25f;
                sentThrottle = throttle;
                sentReverser = reverser;
                sentBraking = braking;
                TrainNet.SendInput(this);
            }
        }
    }

    // ---------- Hebel im Fuehrerstand (siehe TrainLever) ----------

    // Solange ein Hebel bedient wird, gilt der lokale Spieler als Fuehrer der Lok (und schickt seine Bedienung an den Host).
    private float leverUntil;
    private float leverBrakeUntil;

    private void MarkLever()
    {
        leverUntil = Time.time + 0.8f;
    }

    // Regler bewegen: direction +1 hoch, -1 runter.
    public void LeverThrottle(float direction, float dt)
    {
        throttle = Mathf.Clamp01(throttle + direction * throttleRate * dt);
        MarkLever();
    }

    // Fahrtrichtung umschalten (nur im Stand).
    public void LeverReverser()
    {
        if (Mathf.Abs(speed) < 0.3f)
            reverser = -reverser;

        MarkLever();
    }

    // Bremshebel gezogen (wird jedes Bild aufgerufen, solange er gehalten wird).
    public void LeverBrake()
    {
        leverBrakeUntil = Time.time + 0.15f;
        MarkLever();
    }

    public void LeverHorn()
    {
        PlayHorn();
        TrainNet.SendHorn(this);
    }

    // Spielt die Pfeife (auch fuer Mitspieler).
    public void PlayHorn()
    {
        if (hornClip == null)
            return;

        if (hornSource == null)
        {
            hornSource = gameObject.AddComponent<AudioSource>();
            hornSource.spatialBlend = 1f;
            hornSource.maxDistance = 120f;
        }

        hornSource.PlayOneShot(hornClip);
    }

    // ---------- Mehrspieler: Zustand ----------

    public override void WriteNetState(BinaryWriter w)
    {
        base.WriteNetState(w);

        w.Write(throttle);
        w.Write(reverser);
        w.Write(braking);
    }

    public override void ReadNetState(BinaryReader r)
    {
        base.ReadNetState(r);

        float t = r.ReadSingle();
        int rev = r.ReadInt32();
        bool brake = r.ReadBoolean();

        // Wer die Lok gerade selbst bedient (auch per Hebel), behaelt seine eigenen Werte.
        if (PlayerOnBoard || Time.time < leverUntil)
            return;

        throttle = t;
        reverser = rev < 0 ? -1 : 1;
        braking = brake;
    }

    // ---------- Fahren ----------

    // Wie traege der Zug ist: 1 = leicht (volle Beschleunigung), klein = schwerer Zug.
    public float MassFactor
    {
        get
        {
            float total, rated;
            GetConsistMass(out total, out rated);

            if (rated <= 0f)
                rated = ratedMassTons;

            return Mathf.Clamp(rated / Mathf.Max(total, 1f), minMassFactor, 1f);
        }
    }

    protected override void StepTrain(float dt)
    {
        float power = UseFuel(dt);
        float target = throttle * power * reverser * maxSpeed;
        float massFactor = MassFactor;

        if (braking)
        {
            // Bremskraft haengt weniger stark vom Gewicht ab als der Antrieb (Wagenbremsen helfen mit).
            speed = Mathf.MoveTowards(speed, 0f, brakeDeceleration * Mathf.Lerp(1f, massFactor, 0.6f) * dt);
        }
        else if (throttle > 0.02f)
        {
            speed = Mathf.MoveTowards(speed, target, acceleration * massFactor * Mathf.Max(throttle, 0.2f) * dt);
        }
        else
        {
            speed = Mathf.MoveTowards(speed, 0f, rollingResistance * dt);
        }
    }

    // ---------- Anzeige ----------

    // Anzeige mit den Hinweis-Boxen des Spiels (wachsen mit der Textgroesse aus den Einstellungen).
    void OnGUI()
    {
        if (!PlayerOnBoard || SettingsMenu.IsOpen)
            return;

        string dir = Loc.T(reverser >= 0 ? "train.dir.fwd" : "train.dir.back");
        float kmh = Mathf.Abs(speed) * 3.6f;

        string text = Loc.T("train.hud", Mathf.RoundToInt(throttle * 100f), dir,
                            kmh.ToString("0.0", CultureInfo.InvariantCulture));

        // Zuggewicht anzeigen, sobald Wagen dran haengen.
        if (frontLink != null || rearLink != null)
        {
            float total, rated;
            GetConsistMass(out total, out rated);
            text = Loc.T("train.mass", Mathf.RoundToInt(total), Mathf.RoundToInt(MassFactor * 100f)) + "\n" + text;
        }

        if (tender != null)
            text = tender.StatusText() + "\n" + text;

        if (engine != null)
        {
            string status = engine.StatusText();

            if (!string.IsNullOrEmpty(status))
                text = status + "\n" + text;
        }

        text += "\n" + Loc.T("train.hint",
            GameInput.Hint(GameAction.ThrottleUp, throttleUpKey), GameInput.Hint(GameAction.ThrottleDown, throttleDownKey),
            GameInput.Hint(GameAction.Reverser, reverserKey), GameInput.Hint(GameAction.Brake, brakeKey),
            GameInput.Hint(GameAction.Horn, hornKey));

        GameSettings.DrawBoxBottomCenter(text);
    }

    // ---------- Speichern ----------

    private static string F(float v)
    {
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    private static float P(string s)
    {
        float v;
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        return v;
    }

    public override string Serialize()
    {
        return "loco;" + pos.edge + ";" + F(pos.dist) + ";" + pos.dir + ";" +
               F(speed) + ";" + F(throttle) + ";" + reverser + ";" + recipeId + ";" + NetId;
    }

    public override void Deserialize(string[] s)
    {
        if (s.Length >= 7)
        {
            speed = P(s[4]);
            throttle = Mathf.Clamp01(P(s[5]));
            reverser = P(s[6]) < 0f ? -1 : 1;
        }
    }
}
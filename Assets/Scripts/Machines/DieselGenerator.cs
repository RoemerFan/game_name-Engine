using UnityEngine;
using UnityEngine.Events;

// Kleiner Notstrom-Generator.
//  - Diesel: Reissleine ziehen (mehrmals F), dann laeuft der Motor selbst,
//    solange Diesel im Tank ist. Volle Leistung.
//  - Handkurbel: Auch ohne Diesel. Jedes Druecken dreht den Laeufer kurz an,
//    es gibt nur wenig Leistung, solange man kurbelt.
//
// Diesel kommt ueber FuelCanister (Kanister aufheben und in den Einfuellstutzen
// "FuelInlet" werfen).
//
// Prefab: Modell mit festem Collider, DieselGenerator an der Wurzel, ein
// Port (Power), ein Kind mit FuelInlet. Optional: GeneratorControl-Kinder fuer
// Reissleine, Kurbel und Stopp-Knopf. Ohne diese bedient man alles direkt am
// Gehaeuse (F).
public class DieselGenerator : MonoBehaviour, IPowerProducer, IInteractable, ISaveable
{
    [Header("Motor")]
    [SerializeField] private float ratedPowerKW = 12f;
    [SerializeField] private float tankCapacityLiters = 20f;
    [SerializeField] private float startingFuel = 0f;
    [Tooltip("Dieselverbrauch pro Sekunde bei Nennleistung.")]
    [SerializeField] private float litersPerSecondAtRated = 0.05f;
    [Tooltip("Dieselverbrauch pro Sekunde im Leerlauf.")]
    [SerializeField] private float litersPerSecondIdle = 0.008f;

    [Header("Anlassen (Reissleine)")]
    [Tooltip("Aus = Das Modell hat keine Reissleine. Dann bleibt am Gehaeuse (F) nur die Kurbel, " +
             "und eine PullStart-Bedienung wird ignoriert.")]
    [SerializeField] private bool hasPullCord = true;
    [SerializeField] private int pullsToStart = 3;
    [SerializeField] private float pullWindowSeconds = 2.5f;

    [Header("Handkurbel")]
    [SerializeField] private float handPowerKW = 1.5f;
    [SerializeField] private float crankSecondsPerPress = 1.2f;
    [SerializeField] private float crankSpinUpRpmPerSecond = 1500f;

    [Header("Läufer")]
    [SerializeField] private float nominalRpm = 3000f;
    [SerializeField] private float spinUpRpmPerSecond = 800f;
    [SerializeField] private float spinDownRpmPerSecond = 200f;
    [SerializeField] private float nominalFrequencyHz = 50f;

    [Header("Überlast")]
    [SerializeField] private float overloadDroop = 0.08f;
    [SerializeField] private float droopRpmPerSecond = 60f;

    [Header("Schalter (optional, trennt vom Netz)")]
    [SerializeField] private MachineSwitch machineSwitch;

    // Optionaler zweiter Schalter: Motor AN/AUS (zusaetzlich oder statt Reissleine).
    // An = Motor startet (wenn Sprit im Tank), Aus = Motor stoppt.
    // Der andere Schalter (machineSwitch) bleibt fuers An-/Abkoppeln vom Netz.
    [SerializeField] private MachineSwitch engineSwitch;

    [Header("Optik (optional)")]
    [SerializeField] private Transform rotor;
    private RotorSpin spin; // berechnet die Drehung um die Modellmitte
    [SerializeField] private Vector3 rotorAxis = Vector3.up;
    [SerializeField] private float rotorVisualFactor = 0.05f;
    [Tooltip("Die Kurbel (Modell-Teil). Dreht sich, solange gekurbelt wird.")]
    [SerializeField] private Transform crankHandle;
    [Tooltip("Optional: Leeres Objekt genau auf der Drehachse der Kurbel (Kind der Kurbel). " +
             "Leer = Drehung um die Mitte des Kurbel-Teils.")]
    [SerializeField] private Transform crankPivot;
    [Tooltip("Drehachse (z. B. (0,0,1) = blaue Achse). Mit Pivot im Raum des Pivots, " +
             "sonst im Raum des Kurbel-Objekts.")]
    [SerializeField] private Vector3 crankAxis = Vector3.forward;
    [Tooltip("Volle Umdrehungen der Kurbel pro Druck auf F.")]
    [SerializeField] private float crankTurnsPerPress = 1f;
    private RotorSpin crankSpin;
    [SerializeField] private UnityEvent<bool> onEngineChanged;

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [Tooltip("Optional: Leeres Objekt, an dessen Position der Info-Text erscheint. " +
             "Leer = Hoehe ueber dem Nullpunkt der Maschine (siehe 'Debug Height').")]
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 2f;

    private float fuel;
    private bool engineRunning;
    private float rpm;
    private float requestedPowerKW;
    private float deliveredPowerKW;
    private int pullCount;
    private float pullTimer;
    private float crankTimer;

    public bool EngineRunning => engineRunning;
    public bool IsCranking => crankTimer > 0f;
    public float Fuel => fuel;
    public float FuelCapacity => tankCapacityLiters;
    public int PullCount => pullCount;
    public int PullsToStart => pullsToStart;
    public float Rpm => rpm;

    private bool IsOn => machineSwitch == null || machineSwitch.IsOn;

    // Maximale Leistung im aktuellen Betrieb.
    private float CurrentMaxKW =>
        engineRunning ? ratedPowerKW
        : crankTimer > 0f ? handPowerKW
        : 0f;

    // 0 = zu langsam, 1 = bei mindestens 90 % der Nenndrehzahl.
    public float RpmFactor =>
        Mathf.Clamp01((rpm / nominalRpm - 0.5f) / 0.4f);

    public float AvailablePowerKW =>
        IsOn ? CurrentMaxKW * RpmFactor : 0f;

    public float FrequencyHz => nominalFrequencyHz * rpm / nominalRpm;

    public void SetRequestedPower(float kw)
    {
        requestedPowerKW = Mathf.Max(0f, kw);
    }

    private void Awake()
    {
        fuel = Mathf.Clamp(startingFuel, 0f, tankCapacityLiters);

        // Netz-Schalter automatisch suchen, aber nie den Motor-Schalter nehmen.
        if (machineSwitch == null)
        {
            foreach (MachineSwitch sw in GetComponentsInChildren<MachineSwitch>())
            {
                if (sw != engineSwitch)
                {
                    machineSwitch = sw;
                    break;
                }
            }
        }
    }

    private void OnEnable()
    {
        PowerGrid.Register(this);

        // Auf Umlegen des Motor-Schalters reagieren.
        if (engineSwitch != null)
            engineSwitch.Changed += OnEngineSwitchChanged;
    }

    private void OnDisable()
    {
        PowerGrid.Unregister(this);

        if (engineSwitch != null)
            engineSwitch.Changed -= OnEngineSwitchChanged;
    }

    // Motor-Schalter wurde umgelegt: Motor starten oder stoppen.
    private void OnEngineSwitchChanged(bool on)
    {
        if (on)
        {
            // Ohne Sprit springt der Schalter gleich wieder auf AUS.
            if (fuel <= 0f)
                engineSwitch.SetOn(false);
            else
                SetEngine(true);
        }
        else
        {
            SetEngine(false);
        }
    }

    // ---------- Bedienung ----------

    public void PullStart()
    {
        if (!hasPullCord || engineRunning || fuel <= 0f)
            return;

        pullCount++;
        pullTimer = pullWindowSeconds;

        if (pullCount >= pullsToStart)
        {
            pullCount = 0;
            SetEngine(true);
        }
    }

    public void Crank()
    {
        if (engineRunning)
            return;

        crankTimer = crankSecondsPerPress;
    }

    public void StopEngine()
    {
        SetEngine(false);
    }

    // Gibt die tatsaechlich eingefuellte Menge zurueck.
    public float AddFuel(float liters)
    {
        float accepted = Mathf.Clamp(
            liters, 0f, tankCapacityLiters - fuel
        );

        fuel += accepted;
        return accepted;
    }

    private void SetEngine(bool on)
    {
        if (engineRunning == on)
            return;

        engineRunning = on;
        pullCount = 0;

        // Motor-Schalter anpassen, falls der Motor anders gestartet/gestoppt
        // wurde (Reissleine, Kurbel, Tank leer).
        if (engineSwitch != null && engineSwitch.IsOn != on)
            engineSwitch.SetOn(on);

        if (onEngineChanged != null)
            onEngineChanged.Invoke(on);
    }

    // Direkt am Gehaeuse (wenn keine eigenen Bedienelemente da sind).
    public string GetPrompt()
    {
        if (engineRunning)
            return Loc.T("diesel.prompt.running");

        // Mit Reissleine und Sprit: erst reissen. Sonst (keine Leine oder kein Sprit): kurbeln.
        if (hasPullCord && fuel > 0f)
            return Loc.T("diesel.prompt.pull", pullCount, pullsToStart);

        // Keine Leine, aber Sprit im Tank: nur den Kurbel-Hinweis zeigen (nicht "Tank leer").
        if (fuel > 0f)
            return Loc.T("control.crank.ready");

        return Loc.T("diesel.prompt.empty");
    }

    public void Interact()
    {
        if (engineRunning)
            StopEngine();
        else if (hasPullCord && fuel > 0f)
            PullStart();
        else
            Crank();
    }

    // ---------- Simulation ----------

    private void Update()
    {
        float dt = Time.deltaTime;

        if (pullCount > 0)
        {
            pullTimer -= dt;

            if (pullTimer <= 0f)
                pullCount = 0;
        }

        if (crankTimer > 0f)
            crankTimer -= dt;

        float max = CurrentMaxKW;
        float load = IsOn ? Mathf.Min(requestedPowerKW, max) : 0f;
        float loadFraction = max > 0f ? load / max : 0f;

        if (engineRunning)
        {
            float rate = Mathf.Lerp(
                litersPerSecondIdle,
                litersPerSecondAtRated,
                loadFraction
            );

            fuel = Mathf.Max(0f, fuel - rate * dt);

            if (fuel <= 0f)
            {
                Debug.Log("Diesel-Generator: Tank leer, Motor aus.", this);
                SetEngine(false);
            }
        }

        // Ueberlast bremst den Laeufer, die Frequenz sinkt.
        float overload = max > 0f
            ? Mathf.Max(0f, requestedPowerKW / max - 1f)
            : 0f;

        float droop = overloadDroop * Mathf.Clamp01(overload / 0.5f);
        bool driven = engineRunning || crankTimer > 0f;
        float targetRpm = driven ? nominalRpm * (1f - droop) : 0f;

        float change;

        if (rpm < targetRpm)
            change = engineRunning ? spinUpRpmPerSecond : crankSpinUpRpmPerSecond;
        else if (!driven)
            change = spinDownRpmPerSecond;
        else
            change = droopRpmPerSecond;

        rpm = Mathf.MoveTowards(rpm, targetRpm, change * dt);

        deliveredPowerKW = load * RpmFactor;

        if (rotor != null)
        {
            // Dreht um die eigene Mitte des Modells (siehe RotorSpin).
            if (spin == null)
                spin = new RotorSpin(rotor, rotorAxis);

            spin.Rotate(rpm * 6f * rotorVisualFactor * dt);
        }

        // Kurbel: dreht sich nur waehrend des Ankurbelns (crankTimer laeuft).
        if (crankHandle != null && crankTimer > 0f)
        {
            if (crankSpin == null)
                crankSpin = new RotorSpin(crankHandle, crankAxis, crankPivot);

            float degreesPerSecond = 360f * crankTurnsPerPress / Mathf.Max(0.1f, crankSecondsPerPress);
            crankSpin.Rotate(degreesPerSecond * dt);
        }
    }

    // ---------- Testhilfe ----------

    [ContextMenu("Debug: Diesel +10")]
    private void DebugAddFuel()
    {
        AddFuel(10f);
    }

    private void OnGUI()
    {
        if (!showDebugInfo)
            return;

        Camera cam = Camera.main;

        if (cam == null && Camera.allCamerasCount > 0)
            cam = Camera.allCameras[0];

        if (cam == null)
            return;

        // Position des Info-Textes: am Anker, sonst ueber der Maschine.
        Vector3 world = infoAnchor != null
            ? infoAnchor.position
            : transform.position + Vector3.up * debugHeight;

        if ((world - cam.transform.position).sqrMagnitude
            > debugDistance * debugDistance)
            return;

        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return;

        string status = Loc.T(
            !IsOn ? "diesel.status.decoupled"
            : engineRunning ? "diesel.status.engine"
            : crankTimer > 0f ? "diesel.status.crank"
            : rpm > 1f ? "diesel.status.coast"
            : "diesel.status.stopped");

        string text = Loc.T(
            "diesel.info",
            status,
            fuel.ToString("F1"), tankCapacityLiters,
            rpm.ToString("F0"), FrequencyHz.ToString("F1"),
            deliveredPowerKW.ToString("F1"), CurrentMaxKW.ToString("F1"));

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("fuel", fuel);
        bag.SetFloat("rpm", rpm);
        bag.SetBool("running", engineRunning);
    }

    public void LoadState(StateBag bag)
    {
        fuel = Mathf.Clamp(bag.GetFloat("fuel", fuel), 0f, tankCapacityLiters);
        rpm = Mathf.Max(0f, bag.GetFloat("rpm", rpm));

        // SetEngine stellt auch den Motor-Schalter passend.
        SetEngine(bag.GetBool("running", engineRunning));
    }
}
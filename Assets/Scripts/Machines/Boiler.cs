using UnityEngine;

public class Boiler : MonoBehaviour, IPortOwner, ISaveable
{
    [Header("Kohle")]
    [SerializeField] private int maxCoalStock = 20;
    [SerializeField] private float burnSecondsPerCoal = 10f;

    [Header("Ruß")]
    [SerializeField] private float sootPerCoal = 1f;
    [SerializeField] private float maxSoot = 50f;

    [Header("Wasser")]
    [SerializeField] private float waterCapacity = 100f;
    [SerializeField] private float startingWater = 0f;

    [Header("Dampf")]
    [SerializeField] private float steamCapacity = 100f;
    [Tooltip("Wasser, das pro Sekunde verdampft, solange der Kessel brennt.")]
    [SerializeField] private float evaporationPerSecond = 2f;
    [SerializeField] private float steamPerWater = 1f;

    [Header("Feuerregelung")]
    [Tooltip("An: Der Kessel drosselt sein Feuer selbst, wenn der Druck steigt. " +
             "Aus: Es gilt 'Manual Fire' (z. B. per Feuerregler am Kessel).")]
    [SerializeField] private bool autoRegulation = true;

    [Tooltip("Feuerstaerke im Handbetrieb (0 = Klappe zu, 1 = voll).")]
    [Range(0f, 1f)]
    [SerializeField] private float manualFire = 1f;

    [Tooltip("Automatik: Ab diesem Druck (0-1) wird das Feuer schwaecher ...")]
    [Range(0f, 1f)]
    [SerializeField] private float throttleStartPressure = 0.6f;

    [Tooltip("... und ab diesem Druck ist es ganz heruntergeregelt.")]
    [Range(0f, 1f)]
    [SerializeField] private float throttleEndPressure = 0.9f;

    [Tooltip("Wie schnell das Feuer auf Aenderungen reagiert (pro Sekunde, 1 = sofort).")]
    [SerializeField] private float fireChangePerSecond = 0.4f;

    [Header("Schalter (optional)")]
    [SerializeField] private MachineSwitch machineSwitch;

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [Tooltip("Optional: Leeres Objekt, an dessen Position der Info-Text erscheint. " +
             "Leer = Hoehe ueber dem Nullpunkt der Maschine (siehe 'Debug Height').")]
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 3f;

    private int coalStock;
    private float burnTimer;
    private float soot;
    private float waterLevel;
    private float steam;
    private bool isVenting;

    // Aktuelle Feuerstaerke (0..1): bestimmt Brenntempo, Hitze und Dampfmenge.
    private float fire = 1f;

    public int CoalStock => coalStock;
    public int SootCount => Mathf.FloorToInt(soot);
    public float WaterLevel => waterLevel;
    public float Steam => steam;
    public float Pressure01 => steamCapacity > 0f ? steam / steamCapacity : 0f;
    public bool IsBurning => burnTimer > 0f;

    // Feuerregelung
    public float FireRate => fire;
    public bool AutoRegulation => autoRegulation;
    public float ManualFire => manualFire;

    public void SetAutoRegulation(bool on)
    {
        autoRegulation = on;
    }

    public void SetManualFire(float value)
    {
        manualFire = Mathf.Clamp01(value);
    }

    // Fuer die Optik (BoilerVisuals).
    public float WaterLevel01 =>
        waterCapacity > 0f ? waterLevel / waterCapacity : 0f;
    public float CoalStock01 =>
        maxCoalStock > 0 ? (float)coalStock / maxCoalStock : 0f;
    public bool IsVenting => isVenting;

    // Vorrat plus das Stueck, das gerade brennt (fuer die Optik).
    public int CoalTotal => coalStock + (IsBurning ? 1 : 0);
    public float CoalTotal01 =>
        maxCoalStock > 0
            ? Mathf.Clamp01((float)CoalTotal / (maxCoalStock + 1))
            : 0f;
    public bool IsRunning => machineSwitch == null || machineSwitch.IsOn;
    public bool IsFireLit => IsRunning && IsBurning;
    public bool IsDry => IsBurning && waterLevel <= 0f;

    private void Awake()
    {
        waterLevel = Mathf.Clamp(startingWater, 0f, waterCapacity);

        if (machineSwitch == null)
            machineSwitch = GetComponentInChildren<MachineSwitch>();
    }

    private void Update()
    {
        // Ausgeschaltet: Feuer und Verdampfung ruhen.
        if (machineSwitch != null && !machineSwitch.IsOn)
        {
            isVenting = false;
            return;
        }

        UpdateFire();
        UpdateBurning();
        UpdateSteam();
    }

    // Feuerstaerke: Im Automatikbetrieb sinkt sie mit steigendem Druck
    // (voll bis "Throttle Start", aus ab "Throttle End"). Im Handbetrieb
    // gilt der Wert des Reglers. Das Feuer folgt nur langsam (Traegheit).
    private void UpdateFire()
    {
        float target = autoRegulation
            ? Mathf.InverseLerp(throttleEndPressure, throttleStartPressure, Pressure01)
            : manualFire;

        fire = Mathf.MoveTowards(
            fire, target, fireChangePerSecond * Time.deltaTime);
    }

    private void UpdateBurning()
    {
        // Bei schwachem Feuer brennt das Stueck langsamer ab.
        if (burnTimer > 0f)
            burnTimer -= Time.deltaTime * fire;

        // Nächstes Kohlestück verbrennen, sobald das vorige aufgebraucht ist.
        // Ist der Rußspeicher voll, bleibt der Kessel stehen.
        // Bei (fast) geschlossener Klappe wird nichts Neues angezuendet.
        if (burnTimer <= 0f && coalStock > 0 && soot < maxSoot && fire > 0.05f)
        {
            coalStock--;
            burnTimer += burnSecondsPerCoal;
            soot = Mathf.Min(maxSoot, soot + sootPerCoal);
        }
    }

    private void UpdateSteam()
    {
        isVenting = false;

        // Dampf entsteht nur bei Feuer und vorhandenem Wasser.
        if (!IsBurning || waterLevel <= 0f)
            return;

        float water = Mathf.Min(
            waterLevel,
            evaporationPerSecond * fire * Time.deltaTime
        );

        waterLevel -= water;
        steam += water * steamPerWater;

        // Sicherheitsventil: Überschüssiger Dampf geht verloren.
        if (steam > steamCapacity)
        {
            steam = steamCapacity;
            isVenting = true;
        }
    }

    // Wird vom Einwurf aufgerufen. true = Kohle wurde angenommen.
    // Ist im Kessel noch Platz fuer Kohle?
    public bool HasCoalSpace => coalStock < maxCoalStock;

    public bool TryAddCoal()
    {
        if (coalStock >= maxCoalStock)
            return false;

        coalStock++;
        return true;
    }

    // Manuelle Entnahme von Ruß (ganze Stücke).
    public int TakeSoot(int amount)
    {
        int taken = Mathf.Clamp(amount, 0, SootCount);
        soot -= taken;
        return taken;
    }

    // ---------- IPortOwner ----------

    public float GetAvailable(Port port)
    {
        switch (port.Medium)
        {
            case PortMedium.Steam: return steam;
            case PortMedium.Soot: return soot;
            default: return 0f;
        }
    }

    public float GetFreeSpace(Port port)
    {
        if (port.Medium == PortMedium.Water)
            return waterCapacity - waterLevel;

        return 0f;
    }

    public float TakeFrom(Port port, float amount)
    {
        float taken;

        switch (port.Medium)
        {
            case PortMedium.Steam:
                taken = Mathf.Clamp(amount, 0f, steam);
                steam -= taken;
                return taken;

            case PortMedium.Soot:
                taken = Mathf.Clamp(amount, 0f, soot);
                soot -= taken;
                return taken;

            default:
                return 0f;
        }
    }

    public float GiveTo(Port port, float amount)
    {
        if (port.Medium != PortMedium.Water)
            return 0f;

        float accepted = Mathf.Clamp(
            amount, 0f, waterCapacity - waterLevel
        );

        waterLevel += accepted;
        return accepted;
    }

    // ---------- Testhilfen (Rechtsklick auf die Komponente) ----------

    [ContextMenu("Debug: Wasser +50")]
    private void DebugAddWater()
    {
        waterLevel = Mathf.Min(waterCapacity, waterLevel + 50f);
    }

    [ContextMenu("Debug: Kohle +5")]
    private void DebugAddCoal()
    {
        coalStock = Mathf.Min(maxCoalStock, coalStock + 5);
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
            (machineSwitch != null && !machineSwitch.IsOn) ? "boiler.status.off"
            : IsDry ? "boiler.status.dry"
            : isVenting ? "boiler.status.venting"
            : IsBurning ? "boiler.status.burning"
            : "boiler.status.idle");

        string text = Loc.T(
            "boiler.info",
            status,
            coalStock, maxCoalStock,
            waterLevel.ToString("F1"), waterCapacity,
            steam.ToString("F1"), steamCapacity,
            (Pressure01 * 100f).ToString("F0"),
            SootCount, maxSoot,
            Mathf.RoundToInt(fire * 100f),
            Loc.T(autoRegulation ? "boiler.fire.auto" : "boiler.fire.manual"));

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetInt("coal", coalStock);
        bag.SetFloat("burn", burnTimer);
        bag.SetFloat("soot", soot);
        bag.SetFloat("water", waterLevel);
        bag.SetFloat("steam", steam);
        bag.SetFloat("fire", fire);
        bag.SetBool("auto", autoRegulation);
        bag.SetFloat("manual", manualFire);
    }

    public void LoadState(StateBag bag)
    {
        coalStock = Mathf.Clamp(bag.GetInt("coal", coalStock), 0, maxCoalStock);
        burnTimer = Mathf.Max(0f, bag.GetFloat("burn", burnTimer));
        soot = Mathf.Clamp(bag.GetFloat("soot", soot), 0f, maxSoot);
        waterLevel = Mathf.Clamp(bag.GetFloat("water", waterLevel), 0f, waterCapacity);
        steam = Mathf.Clamp(bag.GetFloat("steam", steam), 0f, steamCapacity);
        fire = Mathf.Clamp01(bag.GetFloat("fire", fire));
        autoRegulation = bag.GetBool("auto", autoRegulation);
        manualFire = Mathf.Clamp01(bag.GetFloat("manual", manualFire));
    }
}
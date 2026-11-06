using UnityEngine;

// Batterie (Stromspeicher) mit Wechselrichter.
//
// Was simuliert wird:
//  - Ladung (kWh) mit Reserve: Das Batteriemanagement laesst die Zelle nie ganz
//    leer laufen (Reserve), geladen wird bis voll.
//  - Wirkungsgrad: Je mehr Leistung, desto mehr Verlust (Strom-Waerme). Beim
//    Laden und Entladen gehen so zusammen etwa 15-20 % verloren.
//  - Ladeleistung sinkt, wenn die Batterie fast voll ist ("Konstantspannung"),
//    Entladeleistung sinkt, wenn sie fast leer ist (Spannungseinbruch).
//  - Temperatur: Die Verluste heizen die Batterie. Ab dem Beginn der Drosselung
//    sinkt die Leistung, bei Ueberhitzung schaltet sie ab, bis sie abgekuehlt ist.
//  - Selbstentladung und Alterung: Jeder Ladezyklus (besonders heiss) nimmt
//    dauerhaft etwas Kapazitaet weg ("Gesundheit").
//  - Netzbetrieb: Normalerweise laedt sie nur aus Ueberschuss der Erzeuger und
//    springt bei Unterversorgung ein. Faellt das Netz aus (Blackout), trennt sie
//    sich - ausser im Inselbetrieb. Im Inselbetrieb (zweiter Schalter) kann sie das
//    Netz allein aufbauen, solange kein Erzeuger laeuft: der "Schwarzstart".
//
// Der Strom fliesst wie bei allen Geraeten ueber Kabel an Power-Ports (Kinder
// mit dem Port-Skript, Stoff "Power").
public class Battery : MonoBehaviour, IPowerStorage, ISaveable
{
    [Header("Speicher")]
    [Tooltip("Nenn-Kapazitaet in kWh (neu).")]
    [SerializeField] private float capacityKWh = 20f;

    [Tooltip("Ladung beim Start (0-1).")]
    [Range(0f, 1f)]
    [SerializeField] private float startingCharge = 0.5f;

    [Tooltip("Reserve: Darunter wird nicht entladen (Tiefentladeschutz).")]
    [Range(0f, 0.3f)]
    [SerializeField] private float reserveCharge = 0.1f;

    [Header("Leistung")]
    [SerializeField] private float maxChargeKW = 10f;
    [SerializeField] private float maxDischargeKW = 15f;

    [Tooltip("Ab dieser Ladung (0-1) sinkt die Ladeleistung.")]
    [Range(0.5f, 0.99f)]
    [SerializeField] private float taperStartCharge = 0.8f;

    [Tooltip("So viel der Ladeleistung bleibt bei voller Batterie (Anteil).")]
    [Range(0.02f, 0.5f)]
    [SerializeField] private float taperMinFraction = 0.1f;

    [Tooltip("Unter dieser Ladung (0-1) bricht die Entladeleistung ein.")]
    [Range(0.1f, 0.6f)]
    [SerializeField] private float lowChargeBelow = 0.25f;

    [Tooltip("So viel der Entladeleistung bleibt an der Reserve-Grenze (Anteil).")]
    [Range(0.05f, 1f)]
    [SerializeField] private float lowChargeMinFraction = 0.3f;

    [Header("Wirkungsgrad")]
    [Tooltip("Wirkungsgrad bei kleiner Leistung (0-1).")]
    [Range(0.5f, 1f)]
    [SerializeField] private float efficiencyLowPower = 0.97f;

    [Tooltip("Wirkungsgrad bei voller Leistung (0-1).")]
    [Range(0.5f, 1f)]
    [SerializeField] private float efficiencyFullPower = 0.88f;

    [Tooltip("Selbstentladung pro Stunde Spielzeit (Anteil der Kapazitaet).")]
    [SerializeField] private float selfDischargePerHour = 0.02f;

    [Header("Temperatur")]
    [SerializeField] private float ambientC = 20f;

    [Tooltip("Waermekapazitaet in kJ pro Grad: Kleiner = heizt sich schneller auf.")]
    [SerializeField] private float thermalMassKJPerC = 8f;

    [Tooltip("Abkuehlung pro Sekunde (Anteil des Temperaturunterschieds zur Umgebung).")]
    [SerializeField] private float coolingPerSecond = 0.005f;

    [Tooltip("Ab hier wird die Leistung gedrosselt.")]
    [SerializeField] private float derateStartC = 45f;

    [Tooltip("Ab hier schaltet die Batterie ab (Ueberhitzung).")]
    [SerializeField] private float shutdownC = 60f;

    [Tooltip("Nach dem Abschalten geht sie erst unter dieser Temperatur wieder in Betrieb.")]
    [SerializeField] private float resumeC = 40f;

    [Header("Alterung")]
    [Tooltip("So viel Kapazitaet (Anteil) geht pro Vollzyklus dauerhaft verloren.")]
    [SerializeField] private float wearPerFullCycle = 0.004f;

    [Range(0.1f, 1f)]
    [SerializeField] private float minHealth = 0.5f;

    [Header("Schalter (optional)")]
    [Tooltip("Hauptschalter: AUS = Batterie vom Netz getrennt.")]
    [SerializeField] private MachineSwitch machineSwitch;

    [Tooltip("Inselbetrieb / Schwarzstart: AN = baut das Netz allein auf, wenn kein Erzeuger laeuft.")]
    [SerializeField] private MachineSwitch islandSwitch;

    [Header("Anzeige")]
    [Tooltip("Optional: Farbanzeige (Ladestand gruen -> rot, blinkt bei Ueberhitzung).")]
    [SerializeField] private Renderer indicator;
    [Tooltip("Helligkeit des Leuchtens der Lampe (Emission). 0 = leuchtet nicht, 1,5 = wie bisher, " +
             "hoehere Werte = heller (bei Bloom/HDR gluehend).")]
    [SerializeField, Min(0f)] private float indicatorBrightness = 1.5f;
    [Tooltip("Optional: Echtes Licht (Point Light), das die Umgebung in der Farbe der Lampe beleuchtet.")]
    [SerializeField] private Light indicatorLight;
    [Tooltip("Helligkeit des echten Lichts (Intensity) bei voller Anzeige.")]
    [SerializeField, Min(0f)] private float indicatorLightIntensity = 1f;
    [Tooltip("Reichweite des echten Lichts in Metern.")]
    [SerializeField, Min(0f)] private float indicatorLightRange = 3f;
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [Tooltip("Optional: Leeres Objekt, an dessen Position der Info-Text erscheint.")]
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 2f;

    // ----- Zustand -----
    private float storedKWh;
    private float health = 1f;          // 1 = neu, sinkt mit den Zyklen
    private float temperatureC;
    private bool thermalShutdown;
    private float requestedKW;          // Vorgabe des Netzes (+ abgeben, - laden)
    private float actualKW;             // tatsaechliche Leistung
    private float lossKW;               // Verlustleistung (Waerme)
    private MaterialPropertyBlock block;

    // ----- Werte fuer Netz und Anzeige -----
    private bool SwitchOn => machineSwitch == null || machineSwitch.IsOn;

    public bool IsOnline => SwitchOn && !thermalShutdown;
    public bool IsGridForming =>
        IsOnline && islandSwitch != null && islandSwitch.IsOn;

    public float CapacityKWh => capacityKWh * health;
    public float StoredKWh => storedKWh;
    public float Charge01 => CapacityKWh > 0f ? storedKWh / CapacityKWh : 0f;
    public float PowerKW => actualKW;
    public float TemperatureC => temperatureC;
    public float Health => health;

    // Drosselung bei hoher Temperatur: 1 = volle Leistung, 0 = nichts.
    private float ThermalFactor =>
        temperatureC <= derateStartC
            ? 1f
            : 1f - Mathf.InverseLerp(derateStartC, shutdownC, temperatureC);

    // Energie ueber der Reserve, die man entnehmen darf.
    private float UsableKWh => Mathf.Max(0f, storedKWh - reserveCharge * CapacityKWh);

    public float MaxDischargeKW
    {
        get
        {
            if (!IsOnline || UsableKWh <= 0.0001f)
                return 0f;

            // Fast leer: Die Spannung bricht ein, es geht nur noch wenig.
            float low = Charge01 >= lowChargeBelow
                ? 1f
                : Mathf.Lerp(lowChargeMinFraction, 1f,
                    Mathf.InverseLerp(reserveCharge, lowChargeBelow, Charge01));

            return maxDischargeKW * low * ThermalFactor;
        }
    }

    public float MaxChargeKW
    {
        get
        {
            if (!IsOnline || storedKWh >= CapacityKWh - 0.0001f)
                return 0f;

            // Fast voll: Die Ladeleistung faellt ab.
            float taper = Charge01 <= taperStartCharge
                ? 1f
                : Mathf.Lerp(1f, taperMinFraction,
                    Mathf.InverseLerp(taperStartCharge, 1f, Charge01));

            return maxChargeKW * taper * ThermalFactor;
        }
    }

    // ---------- Anbindung ans Netz ----------

    // Das Stromnetz gibt vor: positiv = ins Netz abgeben, negativ = laden.
    public void SetPower(float kw)
    {
        requestedKW = kw;
    }

    private void Awake()
    {
        storedKWh = Mathf.Clamp01(startingCharge) * capacityKWh;
        temperatureC = ambientC;

        // Damit die Emission der Lampe sicher leuchtet, auch wenn "Emission" im Material
        // nicht angehakt ist (eigene Materialkopie, aendert nicht das Asset).
        if (indicator != null)
            indicator.material.EnableKeyword("_EMISSION");

        // Schalter automatisch suchen, falls nicht eingetragen: der erste ist der
        // Hauptschalter, der zweite der Inselbetrieb-Schalter.
        if (machineSwitch == null || islandSwitch == null)
        {
            foreach (MachineSwitch sw in GetComponentsInChildren<MachineSwitch>())
            {
                if (sw == machineSwitch || sw == islandSwitch)
                    continue;

                if (machineSwitch == null)
                    machineSwitch = sw;
                else if (islandSwitch == null)
                    islandSwitch = sw;
            }
        }
    }

    private void OnEnable()
    {
        PowerGrid.Register(this);
    }

    private void OnDisable()
    {
        PowerGrid.Unregister(this);
    }

    // ---------- Simulation ----------

    private void Update()
    {
        float dt = Time.deltaTime;

        // Energie wird im Zeitraffer gerechnet (siehe PowerGrid "Energy Time Scale"),
        // sonst waere eine Batterie erst nach Stunden leer.
        float hours = dt * PowerGrid.EnergyTimeScale / 3600f;

        float cycleKWh = 0f;   // entnommene Energie, fuer die Alterung
        lossKW = 0f;
        actualKW = 0f;

        if (IsOnline)
        {
            if (requestedKW > 0.0001f)
                Discharge(hours, out cycleKWh);
            else if (requestedKW < -0.0001f)
                Charge(hours);
        }

        // Selbstentladung
        storedKWh = Mathf.Max(
            0f,
            storedKWh - CapacityKWh * selfDischargePerHour * hours);

        UpdateTemperature(dt);
        UpdateWear(cycleKWh);

        // Passt nach der Alterung nicht mehr in die Zelle?
        storedKWh = Mathf.Min(storedKWh, CapacityKWh);

        UpdateIndicator();
    }

    // Wirkungsgrad: bei kleiner Leistung gut, bei voller schlechter.
    private float Efficiency(float power, float maxPower)
    {
        float load = maxPower > 0f ? Mathf.Clamp01(power / maxPower) : 0f;
        return Mathf.Lerp(efficiencyLowPower, efficiencyFullPower, load);
    }

    // Ins Netz abgeben: Dem Speicher wird mehr entnommen, als ankommt.
    private void Discharge(float hours, out float drawnKWh)
    {
        float power = Mathf.Min(requestedKW, MaxDischargeKW);
        float eff = Efficiency(power, maxDischargeKW);

        float draw = power / eff * hours;   // aus der Zelle
        float usable = UsableKWh;

        // Nicht mehr entnehmen als ueber der Reserve vorhanden.
        if (draw > usable && draw > 0f)
        {
            power *= usable / draw;
            draw = usable;
        }

        storedKWh -= draw;
        drawnKWh = draw;

        actualKW = power;
        lossKW = power / eff - power;
    }

    // Aus dem Netz laden: Nur ein Teil kommt in der Zelle an.
    private void Charge(float hours)
    {
        float power = Mathf.Min(-requestedKW, MaxChargeKW);
        float eff = Efficiency(power, maxChargeKW);

        float stored = power * eff * hours;
        float space = CapacityKWh - storedKWh;

        if (stored > space && stored > 0f)
        {
            power *= space / stored;
            stored = space;
        }

        storedKWh += stored;

        actualKW = -power;
        lossKW = power * (1f - eff);
    }

    // Verluste heizen, die Umgebung kuehlt.
    private void UpdateTemperature(float dt)
    {
        temperatureC +=
            lossKW / Mathf.Max(0.1f, thermalMassKJPerC) * dt
            - coolingPerSecond * (temperatureC - ambientC) * dt;

        if (!thermalShutdown && temperatureC >= shutdownC)
            thermalShutdown = true;
        else if (thermalShutdown && temperatureC <= resumeC)
            thermalShutdown = false;
    }

    // Jeder Vollzyklus (Entladung = Kapazitaet) kostet etwas Kapazitaet;
    // heiss und bei hoher Leistung mehr.
    private void UpdateWear(float drawnKWh)
    {
        if (drawnKWh <= 0f || capacityKWh <= 0f)
            return;

        float heatStress = Mathf.Clamp01((temperatureC - 35f) / 25f);
        float powerStress = maxDischargeKW > 0f
            ? Mathf.Clamp01(actualKW / maxDischargeKW)
            : 0f;

        float stress = 1f + heatStress + 0.5f * powerStress;

        health = Mathf.Max(
            minHealth,
            health - wearPerFullCycle * (drawnKWh / capacityKWh) * stress);
    }

    // ---------- Optik ----------

    private void UpdateIndicator()
    {
        if (indicator == null)
            return;

        Color color;

        if (thermalShutdown)
        {
            // Ueberhitzt: rot blinkend.
            color = Mathf.Repeat(Time.time * 3f, 1f) < 0.5f ? Color.red : Color.black;
        }
        else if (!SwitchOn)
        {
            color = Color.gray;
        }
        else
        {
            // Voll = gruen, leer = rot.
            color = Color.Lerp(Color.red, Color.green, Charge01);

            // Waehrend des Ladens leicht pulsieren.
            if (actualKW < -0.01f)
                color *= 0.7f + 0.3f * Mathf.Sin(Time.time * 4f);
        }

        if (block == null)
            block = new MaterialPropertyBlock();

        indicator.GetPropertyBlock(block);
        block.SetColor("_Color", color);
        block.SetColor("_BaseColor", color);
        block.SetColor("_EmissionColor", color * indicatorBrightness);
        indicator.SetPropertyBlock(block);

        // Echtes Licht (optional): gleiche Farbe, Helligkeit folgt dem Blinken/Pulsieren.
        if (indicatorLight != null)
        {
            float level = Mathf.Max(color.r, Mathf.Max(color.g, color.b));

            indicatorLight.color = level > 0.001f ? color / level : Color.black;
            indicatorLight.intensity = indicatorLightIntensity * level;
            indicatorLight.range = indicatorLightRange;
            indicatorLight.enabled = level > 0.01f && SwitchOn; // Schalter aus = Licht aus
        }
    }

    // ---------- Anzeige ----------

    private void OnGUI()
    {
        if (!showDebugInfo)
            return;

        Camera cam = Camera.main;

        if (cam == null && Camera.allCamerasCount > 0)
            cam = Camera.allCameras[0];

        if (cam == null)
            return;

        Vector3 world = infoAnchor != null
            ? infoAnchor.position
            : transform.position + Vector3.up * debugHeight;

        if ((world - cam.transform.position).sqrMagnitude
            > debugDistance * debugDistance)
            return;

        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return;

        string status =
            !SwitchOn ? "battery.status.off"
            : thermalShutdown ? "battery.status.overheat"
            : actualKW > 0.05f ? "battery.status.discharging"
            : actualKW < -0.05f ? "battery.status.charging"
            : IsGridForming ? "battery.status.island"
            : "battery.status.idle";

        string text = Loc.T(
            "battery.info",
            Loc.T(status),
            storedKWh.ToString("F1"),
            CapacityKWh.ToString("F1"),
            Mathf.RoundToInt(Charge01 * 100f),
            actualKW.ToString("F1"),
            Mathf.RoundToInt(temperatureC),
            Mathf.RoundToInt(health * 100f));

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Testhilfen (Rechtsklick auf die Komponente) ----------

    [ContextMenu("Debug: Batterie voll")]
    private void DebugFull()
    {
        storedKWh = CapacityKWh;
    }

    [ContextMenu("Debug: Batterie leer")]
    private void DebugEmpty()
    {
        storedKWh = reserveCharge * CapacityKWh;
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("stored", storedKWh);
        bag.SetFloat("health", health);
        bag.SetFloat("temp", temperatureC);
        bag.SetBool("hot", thermalShutdown);
    }

    public void LoadState(StateBag bag)
    {
        health = Mathf.Clamp(bag.GetFloat("health", health), minHealth, 1f);
        storedKWh = Mathf.Clamp(bag.GetFloat("stored", storedKWh), 0f, CapacityKWh);
        temperatureC = bag.GetFloat("temp", temperatureC);
        thermalShutdown = bag.GetBool("hot", thermalShutdown);
    }
}
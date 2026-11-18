using System.Collections.Generic;
using UnityEngine;

// Lampe als Stromverbraucher. Sie haengt am Stromnetz und passt sich an die ankommende
// Leistung an:
//  - voll versorgt: volle Helligkeit,
//  - zu wenig Strom: dunkler und ab etwa 80 % Versorgung flackernd,
//  - fast kein Strom: aus.
// Mit einem optionalen Schalter (MachineSwitch) laesst sie sich ein- und ausschalten.
// Eine ausgeschaltete Lampe braucht keinen Strom.
//
// Aufbau (Prefab):
//  1. Lampen-Modell mit dem Skript "Lamp" an der Wurzel.
//  2. Ein Port (Typ Power) als Kind, an dem das Kabel angesteckt wird.
//  3. "Lamp Renderer": der Teil des Modells, der leuchten soll (z. B. Glaskolben).
//  4. Optional: ein Light (Point oder Spot) als Kind: leuchtet die Umgebung an.
//  5. Optional: ein MachineSwitch (an der Lampe oder als Kind) zum An-/Ausschalten.
//
// Leistung: Echte Lichter sind teuer. Deshalb leuchten nur die naechsten Lampen
// ("Max Active Lights", im Inspector, Standard 8) und nur innerhalb von "Light Cull Distance". Alle
// anderen leuchten weiter mit ihrem Modell (Emission), werfen aber kein echtes Licht.
public class Lamp : MonoBehaviour, IPowerConsumer
{
    [Header("Strom")]
    [Tooltip("Leistungsaufnahme bei Betrieb in Kilowatt (0,06 = 60 Watt).")]
    [SerializeField] private float ratedPowerKW = 0.06f;
    [Tooltip("Hoeher = wichtiger. Lampen sind meist unwichtig und werden bei Mangel zuerst abgeworfen.")]
    [SerializeField] private int importance = 1;
    [Tooltip("Optional: Schalter. Leer = Lampe ist immer an (ein Schalter im Kind wird automatisch gefunden).")]
    [SerializeField] private MachineSwitch machineSwitch;

    [Header("Anpassung an die Versorgung")]
    [Tooltip("Unter diesem Anteil der Versorgung (0 bis 1) bleibt die Lampe aus.")]
    [SerializeField, Range(0f, 0.9f)] private float minSupply = 0.2f;
    [Tooltip("Unter diesem Anteil beginnt die Lampe zu flackern (staerker, je weniger Strom).")]
    [SerializeField, Range(0f, 1f)] private float flickerBelow = 0.8f;
    [Tooltip("Wie schnell die Helligkeit auf Aenderungen reagiert (Sekunden bis voll).")]
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.25f;

    [Header("Leuchten (Modell)")]
    [Tooltip("Der Teil des Modells, der leuchtet (z. B. Glaskolben).")]
    [SerializeField] private Renderer lampRenderer;
    [SerializeField] private Color lampColor = new Color(1f, 0.92f, 0.75f);
    [Tooltip("Helligkeit des Leuchtens (Emission) bei voller Versorgung.")]
    [SerializeField, Min(0f)] private float emissionBrightness = 2f;

    [Header("Echtes Licht (optional)")]
    [Tooltip("Leer = automatisch ein Light im Kind suchen.")]
    [SerializeField] private Light lampLight;
    [SerializeField, Min(0f)] private float lightIntensity = 1.5f;
    [SerializeField, Min(0f)] private float lightRange = 8f;
    [Tooltip("Weiter als diese Entfernung zum Spieler (Meter) wird das echte Licht abgeschaltet.")]
    [SerializeField, Min(1f)] private float lightCullDistance = 25f;

    [Tooltip("Maximal so viele Lampen werfen gleichzeitig echtes Licht (die naechsten zum Spieler). " +
             "Gilt fuer ALLE Lampen gemeinsam; stehen bei Lampen verschiedene Werte, zaehlt der kleinste. " +
             "Am einfachsten: in jedem Lampen-Prefab denselben Wert lassen.")]
    [SerializeField, Min(0)] private int maxActiveLights = 8;

    private float supply = 1f;       // vom Netz gemeldet (0 bis 1)
    private float level;             // aktuelle Helligkeit (0 bis 1), weich nachgefuehrt
    private float flickerSeed;
    private bool allowLight = true;  // vom Verteiler: darf echtes Licht werfen?
    private float distanceSq;
    private MaterialPropertyBlock block;

    private bool IsOn => machineSwitch == null || machineSwitch.IsOn;

    // Zielhelligkeit aus der Versorgung (0 bis 1), ohne Flackern.
    private float TargetLevel
    {
        get
        {
            if (!IsOn || supply <= minSupply)
                return 0f;

            return Mathf.Clamp01(Mathf.InverseLerp(minSupply, 1f, supply));
        }
    }

    // ----- IPowerConsumer -----
    public float DemandKW => IsOn ? ratedPowerKW : 0f;
    public float RatedPowerKW => ratedPowerKW;
    public int Importance => importance;

    public void SetSupply(float fraction)
    {
        supply = Mathf.Clamp01(fraction);
    }

    // ---------- Start ----------

    private void Awake()
    {
        flickerSeed = Random.value * 100f;

        if (machineSwitch == null)
            machineSwitch = GetComponentInChildren<MachineSwitch>();

        if (lampLight == null)
            lampLight = GetComponentInChildren<Light>();

        // Damit die Emission sicher leuchtet, auch wenn "Emission" im Material nicht angehakt ist
        // (eigene Materialkopie, aendert nicht das Asset).
        if (lampRenderer != null)
            lampRenderer.material.EnableKeyword("_EMISSION");

        // Anfangs aus; die Lampe blendet beim Start weich auf.
        level = 0f;
        ApplyVisuals(0f);
    }

    private void OnEnable()
    {
        PowerGrid.Register(this);
        all.Add(this);
    }

    private void OnDisable()
    {
        PowerGrid.Unregister(this);
        all.Remove(this);
    }

    // ---------- Laufender Betrieb ----------

    private void Update()
    {
        float dt = Time.deltaTime;

        // Helligkeit weich dem Ziel nachfuehren.
        level = Mathf.MoveTowards(level, TargetLevel, dt / fadeSeconds);

        // Flackern bei knapper Versorgung: je weniger Strom, desto staerker.
        float shown = level;

        if (IsOn && supply > minSupply && supply < flickerBelow && flickerBelow > 0f)
        {
            float amount = 1f - supply / flickerBelow;
            float noise = Mathf.PerlinNoise(Time.time * 18f, flickerSeed);

            shown *= Mathf.Lerp(1f, noise, amount);
        }

        // Nur eine Lampe pro Zeitraum stoesst die Verteilung der echten Lichter an.
        if (Time.unscaledTime >= nextRebalance)
            Rebalance();

        ApplyVisuals(shown);
    }

    private void ApplyVisuals(float brightness)
    {
        // Leuchtendes Modell (Emission).
        if (lampRenderer != null)
        {
            if (block == null)
                block = new MaterialPropertyBlock();

            lampRenderer.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", lampColor * (emissionBrightness * brightness));
            lampRenderer.SetPropertyBlock(block);
        }

        // Echtes Licht, nur wenn die Lampe nah genug ist und unter den hellsten Lichtern liegt.
        if (lampLight != null)
        {
            bool lit = allowLight && brightness > 0.01f;

            lampLight.enabled = lit;

            if (lit)
            {
                lampLight.color = lampColor;
                lampLight.intensity = lightIntensity * brightness;
                lampLight.range = lightRange;
            }
        }
    }

    // ---------- Verteilung der echten Lichter ----------

    private static readonly List<Lamp> all = new List<Lamp>();
    private static readonly List<Lamp> candidates = new List<Lamp>();
    private static float nextRebalance;

    // Bestimmt, welche Lampen echtes Licht werfen duerfen: die naechsten zum Spieler,
    // hoechstens MaxActiveLights, nur innerhalb ihrer Cull-Entfernung.
    private static void Rebalance()
    {
        nextRebalance = Time.unscaledTime + 0.3f;

        Camera cam = Camera.main;

        if (cam == null && Camera.allCamerasCount > 0)
            cam = Camera.allCameras[0];

        candidates.Clear();

        foreach (Lamp lamp in all)
        {
            lamp.allowLight = false;

            if (cam == null)
            {
                lamp.allowLight = true;
                continue;
            }

            if (lamp.lampLight == null || lamp.TargetLevel <= 0f)
                continue;

            lamp.distanceSq = (lamp.transform.position - cam.transform.position).sqrMagnitude;

            if (lamp.distanceSq <= lamp.lightCullDistance * lamp.lightCullDistance)
                candidates.Add(lamp);
        }

        if (cam == null)
            return;

        candidates.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

        // Der kleinste eingestellte Wert aller Lampen gilt.
        int limit = int.MaxValue;

        foreach (Lamp lamp in all)
            limit = Mathf.Min(limit, lamp.maxActiveLights);

        int count = Mathf.Min(limit, candidates.Count);

        for (int i = 0; i < count; i++)
            candidates[i].allowLight = true;
    }

    // Hilfslinie im Editor: Reichweite des echten Lichts.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, lightRange);
    }
}
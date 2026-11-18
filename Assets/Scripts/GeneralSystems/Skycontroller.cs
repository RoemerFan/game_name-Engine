using UnityEngine;
using UnityEngine.Rendering;

// Himmel, Sonne und Tageszeit in einem Skript.
//
// Was es macht:
//  - Die Sonne ist das Directional Light. Es wird ueber den Tag gedreht
//    (Sonnenaufgang -> Mittag -> Sonnenuntergang -> Nacht). Die Position des
//    Lichts ist bei einem Directional Light egal, nur die Drehung zaehlt.
//  - Der Himmel ist ein "Skybox/Procedural"-Material: Die Sonnenscheibe sitzt
//    automatisch dort, wo das Licht herkommt.
//  - Farbe und Staerke des Lichts, Umgebungslicht und Nebel passen sich an:
//    morgens/abends orange und flach, mittags hell und weiss, nachts dunkel.
//  - Nachts gibt es ein schwaches blaues Mondlicht (zweites Licht).
//
// Einrichtung: dieses Skript auf ein leeres Objekt in der Szene legen.
// Das vorhandene Directional Light wird automatisch gefunden.
public class SkyController : MonoBehaviour
{
    [Header("Zeit")]
    [Tooltip("Aktuelle Uhrzeit in Stunden (0-24).")]
    [Range(0f, 24f)]
    [SerializeField] private float timeOfDay = 9f;

    [Tooltip("Dauer eines ganzen Tages in Echtzeit-Minuten.")]
    [SerializeField] private float dayLengthMinutes = 24f;

    [Tooltip("Zeit laeuft weiter. Aus = Tageszeit bleibt stehen.")]
    [SerializeField] private bool runTime = true;

    [Tooltip("Taste: gedrueckt halten spult die Zeit schnell vor.")]
    [SerializeField] private KeyCode fastForwardKey = KeyCode.Y;

    [SerializeField] private float fastForwardFactor = 30f;

    [Header("Sonne")]
    [Tooltip("Das Directional Light. Leer = wird automatisch gesucht.")]
    [SerializeField] private Light sun;

    [Tooltip("Himmelsrichtung, in der die Sonne aufgeht (Grad um die Hochachse).")]
    [SerializeField] private float sunYaw = 90f;

    [Tooltip("Neigung der Sonnenbahn (Jahreszeit/Breitengrad). 0 = Sonne zieht ueber den Kopf.")]
    [Range(-60f, 60f)]
    [SerializeField] private float sunTilt = 25f;

    [SerializeField] private float maxSunIntensity = 1.2f;
    [SerializeField] private Color sunHighColor = new Color(1f, 0.96f, 0.88f);
    [SerializeField] private Color sunLowColor = new Color(1f, 0.5f, 0.25f);

    [Tooltip("Schatten der Sonne (Soft Shadows sehen am besten aus).")]
    [SerializeField] private LightShadows sunShadows = LightShadows.Soft;

    [Header("Mondlicht")]
    [SerializeField] private bool useMoon = true;
    [SerializeField] private float moonIntensity = 0.18f;
    [SerializeField] private Color moonColor = new Color(0.55f, 0.65f, 1f);

    [Header("Himmel")]
    [Tooltip("Shader 'Custom/GameSky' (GameSky.shader). Leer = wird gesucht.")]
    [SerializeField] private Shader skyShader;

    [Tooltip("Optional: fertiges Material. Leer = wird aus dem Shader erzeugt.")]
    [SerializeField] private Material skyMaterial;

    [Tooltip("Groesse der Sonnenscheibe in Grad (real 0.5, hier groesser).")]
    [Range(0.5f, 8f)]
    [SerializeField] private float sunSizeDegrees = 3f;

    [SerializeField] private Color zenithDay = new Color(0.18f, 0.42f, 0.85f);
    [SerializeField] private Color horizonDay = new Color(0.62f, 0.77f, 0.92f);
    [SerializeField] private Color zenithNight = new Color(0.008f, 0.012f, 0.035f);
    [SerializeField] private Color horizonNight = new Color(0.03f, 0.04f, 0.09f);
    [SerializeField] private Color sunsetColor = new Color(1f, 0.5f, 0.25f);
    [SerializeField] private Color groundColor = new Color(0.32f, 0.31f, 0.3f);

    [Header("Mond")]
    [Tooltip("Groesse der Mondscheibe in Grad.")]
    [Range(0.5f, 8f)]
    [SerializeField] private float moonSizeDegrees = 3.5f;

    [Tooltip("0 = Neumond, 0.25 = Halbmond, 0.5 = Vollmond, 0.75 = Halbmond.")]
    [Range(0f, 1f)]
    [SerializeField] private float moonPhase = 0.5f;

    [Tooltip("Der Mond steht der Sonne gegenueber, um diese Grad zur Seite versetzt.")]
    [SerializeField] private float moonOffsetDegrees = 20f;

    [Header("Sterne")]
    [Range(0f, 2f)]
    [SerializeField] private float starBrightness = 1f;

    [Header("Wolken")]
    [Range(0f, 1f)]
    [SerializeField] private float cloudCoverage = 0.45f;

    [Tooltip("Kleiner = groessere Wolken.")]
    [Range(0.4f, 4f)]
    [SerializeField] private float cloudScale = 1.3f;

    [Tooltip("Windgeschwindigkeit der Wolken.")]
    [SerializeField] private float windSpeed = 0.01f;

    [Tooltip("Windrichtung in Grad.")]
    [SerializeField] private float windAngle = 30f;

    [Header("Umgebungslicht")]
    [SerializeField] private Color ambientSkyDay = new Color(0.5f, 0.62f, 0.8f);
    [SerializeField] private Color ambientEquatorDay = new Color(0.5f, 0.52f, 0.55f);
    [SerializeField] private Color ambientGroundDay = new Color(0.3f, 0.28f, 0.25f);
    [SerializeField] private Color ambientNight = new Color(0.03f, 0.04f, 0.08f);

    [Header("Nebel")]
    [SerializeField] private bool useFog = false;
    [SerializeField] private float fogDensity = 0.004f;

    // ---------- Oeffentlich ----------

    public static float Hour { get; private set; }

    // Hoehe der Sonne: 1 = Zenit, 0 = Horizont, negativ = unter dem Horizont.
    public static float SunHeight { get; private set; }

    public static string TimeText
    {
        get
        {
            int h = Mathf.FloorToInt(Hour) % 24;
            int m = Mathf.FloorToInt((Hour - Mathf.Floor(Hour)) * 60f);
            return h.ToString("00") + ":" + m.ToString("00");
        }
    }

    private Light moon;
    private Vector2 cloudOffset;

    // Zugriff fuer Spielstaende.
    public static SkyController Instance { get; private set; }

    public float TimeOfDay
    {
        get { return timeOfDay; }
        set { timeOfDay = Mathf.Repeat(value, 24f); }
    }

    public Vector2 CloudOffset
    {
        get { return cloudOffset; }
        set { cloudOffset = value; }
    }
    private Material runtimeSky;

    private void Awake()
    {
        Instance = this;

        FindSun();
        SetupSky();
        SetupMoon();

        Apply();
    }

    private void FindSun()
    {
        if (sun == null)
            sun = RenderSettings.sun;

        if (sun == null)
        {
            foreach (Light l in FindObjectsOfType<Light>())
            {
                if (l.type == LightType.Directional)
                {
                    sun = l;
                    break;
                }
            }
        }

        // Kein Licht gefunden: eines anlegen.
        if (sun == null)
        {
            GameObject go = new GameObject("Sun");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
        }

        sun.shadows = sunShadows;
        RenderSettings.sun = sun;
    }

    private void SetupSky()
    {
        Material mat = skyMaterial;

        if (mat == null)
        {
            Shader shader = skyShader != null
                ? skyShader
                : Shader.Find("Custom/GameSky");

            if (shader == null)
            {
                Debug.LogWarning(
                    "SkyController: Shader 'Custom/GameSky' nicht gefunden. "
                    + "Bitte GameSky.shader ins Projekt legen und im Feld 'Sky Shader' eintragen.",
                    this);
                return;
            }

            runtimeSky = new Material(shader);
            runtimeSky.name = "Runtime Sky";
            mat = runtimeSky;
        }

        RenderSettings.skybox = mat;
        skyMaterial = mat;

        // Umgebungslicht kommt aus den Farben unten, nicht aus dem Himmel
        // (spart die teure Neuberechnung bei jeder Aenderung).
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
    }

    private void SetupMoon()
    {
        if (!useMoon)
            return;

        GameObject go = new GameObject("Moon");
        go.transform.SetParent(transform, false);

        moon = go.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.shadows = LightShadows.None;
        moon.color = moonColor;
        moon.intensity = 0f;
    }

    private void Update()
    {
        if (runTime && dayLengthMinutes > 0.01f)
        {
            float speed = 24f / (dayLengthMinutes * 60f);

            if (GameInput.KeyHeld(fastForwardKey))
                speed *= fastForwardFactor;

            timeOfDay = Mathf.Repeat(timeOfDay + speed * Time.deltaTime, 24f);
        }

        // Wolken ziehen mit dem Wind.
        float rad = windAngle * Mathf.Deg2Rad;
        cloudOffset += new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * windSpeed * Time.deltaTime;

        Apply();
    }

    private void OnValidate()
    {
        // Im Editor sofort sichtbar, wenn man die Uhrzeit verschiebt.
        if (Application.isPlaying && sun != null)
            Apply();
    }

    // Wendet die Tageszeit auf Licht, Himmel, Umgebung und Nebel an.
    private void Apply()
    {
        if (sun == null)
            return;

        Hour = timeOfDay;

        // 6 Uhr = Aufgang, 12 Uhr = Mittag, 18 Uhr = Untergang.
        float angle = timeOfDay * 15f - 90f;

        Quaternion rot =
            Quaternion.Euler(0f, sunYaw, 0f)
            * Quaternion.Euler(0f, 0f, sunTilt)
            * Quaternion.Euler(angle, 0f, 0f);

        sun.transform.rotation = rot;

        float height = Vector3.Dot(-sun.transform.forward, Vector3.up);
        SunHeight = height;

        // 0 = Nacht, 1 = voller Tag. Dazwischen die Daemmerung.
        float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.25f, height));

        // Naehe zum Horizont: 1 = genau am Horizont (orange).
        float horizon = 1f - Mathf.Clamp01(height / 0.35f);
        horizon = Mathf.Clamp01(horizon) * Mathf.Clamp01(height + 0.25f) * 4f;
        horizon = Mathf.Clamp01(horizon);

        sun.color = Color.Lerp(sunHighColor, sunLowColor, horizon);
        sun.intensity = maxSunIntensity * day;
        sun.enabled = day > 0.001f;

        // Richtung zur Sonne und zum Mond (Mond: gegenueber, seitlich versetzt).
        Vector3 sunDir = -sun.transform.forward;
        Vector3 moonDir = Quaternion.AngleAxis(moonOffsetDegrees, Vector3.up) * -sunDir;

        // Das Mondlicht kommt aus der Richtung der sichtbaren Mondscheibe
        // und nur, solange der Mond ueber dem Horizont steht.
        if (moon != null)
        {
            moon.transform.rotation = Quaternion.LookRotation(-moonDir);
            float night = 1f - day;
            float up = Mathf.Clamp01(moonDir.y * 4f);
            moon.intensity = moonIntensity * night * up;
            moon.enabled = moon.intensity > 0.001f;
        }

        // Mit der Tageszeit gedrehte Sterne: Drehung um die Achse der Sonnenbahn.
        Quaternion pathRot =
            Quaternion.Euler(0f, sunYaw, 0f) * Quaternion.Euler(0f, 0f, sunTilt);
        Vector3 axis = pathRot * Vector3.right;
        Matrix4x4 starRot = Matrix4x4.Rotate(
            Quaternion.Inverse(Quaternion.AngleAxis(timeOfDay * 15f, axis)));

        // Himmelsfarben: Tag/Nacht, am Horizont bei Sonnenauf-/untergang orange.
        Color zenith = Color.Lerp(zenithNight, zenithDay, day);
        Color horizonCol = Color.Lerp(horizonNight, horizonDay, day);
        horizonCol = Color.Lerp(horizonCol, sunsetColor, horizon * 0.75f * Mathf.Clamp01(day * 1.5f + 0.2f));
        zenith = Color.Lerp(zenith, new Color(0.22f, 0.22f, 0.45f), horizon * 0.35f * day);

        // Wolken: tagsueber weiss (bei Sonnenuntergang getoent), nachts dunkel.
        Color cloudDay = Color.Lerp(Color.white, sunLowColor, horizon * 0.8f);
        Color cloudNight = new Color(0.05f, 0.06f, 0.1f);
        Color cloudLight = Color.Lerp(cloudNight, cloudDay, day);

        // Bei viel Bewoelkung wird die Sonne schwaecher.
        sun.intensity *= Mathf.Lerp(1f, 0.55f, cloudCoverage);

        if (skyMaterial != null)
        {
            skyMaterial.SetVector("_SunDir", sunDir);
            skyMaterial.SetVector("_MoonDir", moonDir);
            skyMaterial.SetColor("_SunColor", sun.color);
            skyMaterial.SetFloat("_SunSize", sunSizeDegrees * Mathf.Deg2Rad);
            skyMaterial.SetColor("_MoonColor", new Color(0.92f, 0.94f, 1f));
            skyMaterial.SetFloat("_MoonSize", moonSizeDegrees * Mathf.Deg2Rad);
            skyMaterial.SetFloat("_MoonPhase", moonPhase);
            skyMaterial.SetVector("_MoonAxis", axis);

            skyMaterial.SetColor("_ZenithColor", zenith);
            skyMaterial.SetColor("_HorizonColor", horizonCol);
            skyMaterial.SetColor("_GroundColor", groundColor * Mathf.Lerp(0.08f, 1f, day));
            skyMaterial.SetColor("_GlowColor", Color.Lerp(sunHighColor, sunLowColor, horizon));
            skyMaterial.SetFloat("_GlowStrength", Mathf.Lerp(0.12f, 0.6f, horizon) * day);
            skyMaterial.SetFloat("_Day", day);

            skyMaterial.SetFloat("_StarBrightness", starBrightness);
            skyMaterial.SetVector("_StarR0", starRot.GetRow(0));
            skyMaterial.SetVector("_StarR1", starRot.GetRow(1));
            skyMaterial.SetVector("_StarR2", starRot.GetRow(2));

            skyMaterial.SetFloat("_CloudCoverage", cloudCoverage);
            skyMaterial.SetFloat("_CloudScale", cloudScale);
            skyMaterial.SetVector("_CloudOffset", cloudOffset);
            skyMaterial.SetColor("_CloudColor", cloudLight);
            skyMaterial.SetColor("_CloudShade", cloudLight * 0.6f);
        }

        // Umgebungslicht
        Color sunsetTint = Color.Lerp(Color.white, new Color(1f, 0.8f, 0.7f), horizon);

        RenderSettings.ambientSkyColor =
            Color.Lerp(ambientNight, ambientSkyDay * sunsetTint, day);
        RenderSettings.ambientEquatorColor =
            Color.Lerp(ambientNight * 0.8f, ambientEquatorDay * sunsetTint, day);
        RenderSettings.ambientGroundColor =
            Color.Lerp(ambientNight * 0.5f, ambientGroundDay, day);

        // Nebel
        RenderSettings.fog = useFog;

        if (useFog)
        {
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = fogDensity;

            Color dayFog = Color.Lerp(
                new Color(0.7f, 0.78f, 0.88f), new Color(1f, 0.7f, 0.5f), horizon);

            RenderSettings.fogColor = Color.Lerp(ambientNight, dayFog, day);
        }
    }

    private void OnDestroy()
    {
        if (runtimeSky != null)
            Destroy(runtimeSky);
    }
}
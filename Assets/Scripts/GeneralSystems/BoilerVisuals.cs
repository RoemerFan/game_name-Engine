using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Optik des Kessels: liest nur den Zustand des Boilers und steuert Effekte.
// Die Simulation wird nicht veraendert. Alle Felder sind optional.
//
// Einrichtung:
//  - Auf die Wurzel des Kessel-Prefabs legen (neben Boiler).
//  - Effekte als Kind-Objekte an die richtigen Stellen setzen und hier eintragen.
//  - Bei allen eingetragenen Partikelsystemen "Play On Awake" ausschalten und
//    die Menge ueber "Emission > Rate over Time" einstellen (das ist der
//    Maximalwert bei voller Staerke). Bursts werden nicht skaliert.
//  - Fuer die Glut muss am Material "Emission" aktiviert sein.
public class BoilerVisuals : MonoBehaviour
{
    [Header("Quelle")]
    [SerializeField] private Boiler boiler;

    [Header("Feuer")]
    [Tooltip("Flammen, Funken usw. Jedes Partikelsystem einzeln eintragen.")]
    [SerializeField] private ParticleSystem[] flames;
    [SerializeField] private Light fireLight;
    [SerializeField] private float fireLightIntensity = 2.5f;
    [Range(0f, 1f)]
    [SerializeField] private float flickerAmount = 0.35f;
    [SerializeField] private float flickerSpeed = 8f;
    [SerializeField] private float fireRiseSpeed = 1.5f;
    [SerializeField] private float fireFallSpeed = 0.6f;
    [Tooltip("Loop-Sound (Knistern). Loop wird automatisch eingeschaltet.")]
    [SerializeField] private AudioSource fireAudio;
    [Range(0f, 1f)]
    [SerializeField] private float fireVolume = 0.6f;

    [Header("Glut (leuchtendes Material)")]
    [SerializeField] private Renderer emberRenderer;
    [SerializeField, ColorUsage(false, true)]
    private Color emberColor = new Color(2f, 0.7f, 0.1f);
    [Tooltip("Wie schnell die Glut nach dem Erloeschen abkuehlt (pro Sekunde).")]
    [SerializeField] private float emberFallSpeed = 0.08f;
    [SerializeField] private string emissionProperty = "_EmissionColor";

    [Header("Kohle im Feuerraum")]
    [Tooltip("Rein optisches Kohlestueck, z. B. CoalChunk_Visual. " +
             "Colliders und Rigidbody werden entfernt.")]
    [SerializeField] private GameObject coalPiecePrefab;
    [Tooltip("Leeres Kind-Objekt auf dem Rost (direkt unter der Wurzel, " +
             "Scale 1). Ohne Eintrag gilt die Wurzel selbst.")]
    [SerializeField] private Transform coalPileCenter;
    [Tooltip("Bodenflaeche (X/Z) und Stapelhoehe (Y) im lokalen Raum " +
             "des Mittelpunkts. Im Editor als orange Rahmen sichtbar.")]
    [SerializeField] private Vector3 coalPileSize = new Vector3(0.5f, 0.12f, 0.4f);
    [Tooltip("So viele Stuecke sind bei vollem Kohlevorrat zu sehen.")]
    [SerializeField] private int maxCoalPieces = 14;
    [SerializeField] private float coalPieceScale = 1f;
    [SerializeField, ColorUsage(false, true)]
    private Color coalGlowColor = new Color(2.2f, 0.6f, 0.08f);
    [Tooltip("Farbe der abkuehlenden Glut (dunkles Rot).")]
    [SerializeField, ColorUsage(false, true)]
    private Color coalCoolColor = new Color(0.8f, 0.1f, 0.02f);
    [Range(0f, 1f)]
    [SerializeField] private float coalFlickerAmount = 0.3f;
    [SerializeField] private float coalFlickerSpeed = 1.5f;

    [Header("Rauch (Schornstein, folgt dem Feuer)")]
    [SerializeField] private ParticleSystem[] chimneySmoke;

    [Header("Dampf (folgt dem Druck)")]
    [SerializeField] private ParticleSystem[] steam;
    [Range(0f, 0.5f)]
    [SerializeField] private float steamMinPressure = 0.05f;

    [Header("Sicherheitsventil (blaest bei vollem Druck ab)")]
    [SerializeField] private ParticleSystem[] ventSteam;
    [SerializeField] private AudioSource ventAudio;
    [Range(0f, 1f)]
    [SerializeField] private float ventVolume = 0.8f;

    [Header("Wasserstandsglas")]
    [Tooltip("Zylinder (oder Quader) im Glas. Die Hoehe folgt der lokalen Y-Achse.")]
    [SerializeField] private Transform waterFill;
    [Tooltip("An: Pivot liegt unten. Aus: Pivot liegt in der Mitte " +
             "(wie beim Unity-Zylinder), die Unterkante wird nachgefuehrt.")]
    [SerializeField] private bool pivotAtBottom = true;
    [Tooltip("Wie schnell die Anzeige dem echten Wasserstand folgt (pro Sekunde).")]
    [SerializeField] private float waterChangeSpeed = 0.4f;

    [Header("Manometer (Druckanzeige)")]
    [SerializeField] private Transform needle;
    [SerializeField] private Vector3 needleAxis = Vector3.forward;
    [SerializeField] private float needleMinAngle = -120f;
    [SerializeField] private float needleMaxAngle = 120f;
    [SerializeField] private float needleSmoothTime = 0.4f;

    [Header("Leistung")]
    [Tooltip("Ab dieser Entfernung zur Kamera werden Partikel, Licht und Ton " +
             "abgeschaltet. 0 = immer an.")]
    [SerializeField] private float maxEffectDistance = 40f;
    [SerializeField] private float cullCheckInterval = 0.3f;

    // Steuert ein Partikelsystem ueber einen Wert von 0 bis 1.
    private class Fx
    {
        private readonly ParticleSystem ps;
        private readonly float baseRate;
        private readonly float baseSize;
        private float last = -1f;

        public Fx(ParticleSystem ps)
        {
            this.ps = ps;
            baseRate = ps.emission.rateOverTimeMultiplier;
            baseSize = ps.main.startSizeMultiplier;
        }

        public void Set(float level)
        {
            if (level > 0.01f)
            {
                if (Mathf.Abs(level - last) > 0.005f)
                {
                    ParticleSystem.EmissionModule em = ps.emission;
                    em.rateOverTimeMultiplier = baseRate * level;

                    ParticleSystem.MainModule main = ps.main;
                    main.startSizeMultiplier =
                        baseSize * Mathf.Lerp(0.6f, 1f, level);

                    last = level;
                }

                if (!ps.isEmitting)
                    ps.Play(true);
            }
            else
            {
                if (ps.isEmitting)
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

                last = 0f;
            }
        }

        public void Clear()
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            last = 0f;
        }
    }

    private Fx[] flameFx;
    private Fx[] smokeFx;
    private Fx[] steamFx;
    private Fx[] ventFx;

    private float fireLevel;
    private float emberLevel;
    private float steamLevel;
    private float ventLevel;
    private float waterShown;
    private float needleAngle;
    private float needleVelocity;
    private float flickerSeed;

    private MaterialPropertyBlock emberBlock;
    private float emberApplied = -1f;

    private Vector3 waterBaseScale;
    private Vector3 waterBasePosition;
    private float waterFullHeight;
    private Renderer waterRenderer;
    private Quaternion needleBaseRotation;

    private GameObject[] coalPieces;
    private Renderer[][] coalRenderers;
    private float[] coalPhase;
    private Material coalMaterial;
    private MaterialPropertyBlock coalBlock;
    private int coalShown = -1;
    private bool coalGlowing;

    private Camera cam;
    private bool nearby = true;
    private float nextCullCheck;

    private void Awake()
    {
        if (boiler == null)
            boiler = GetComponentInParent<Boiler>();

        if (boiler == null)
        {
            Debug.LogError("BoilerVisuals: Kein Boiler gefunden!", this);
            enabled = false;
        }
    }

    private void Start()
    {
        flameFx = Build(flames);
        smokeFx = Build(chimneySmoke);
        steamFx = Build(steam);
        ventFx = Build(ventSteam);

        ClearAll();

        flickerSeed = Random.value * 100f;
        emberBlock = new MaterialPropertyBlock();

        if (fireAudio != null)
        {
            fireAudio.loop = true;
            fireAudio.playOnAwake = false;
        }

        if (ventAudio != null)
        {
            ventAudio.loop = true;
            ventAudio.playOnAwake = false;
        }

        if (waterFill != null)
        {
            waterBaseScale = waterFill.localScale;
            waterBasePosition = waterFill.localPosition;
            waterRenderer = waterFill.GetComponent<Renderer>();

            MeshFilter mf = waterFill.GetComponent<MeshFilter>();
            float meshHeight =
                (mf != null && mf.sharedMesh != null)
                    ? mf.sharedMesh.bounds.size.y
                    : 1f;

            waterFullHeight = meshHeight * waterBaseScale.y;
            waterShown = boiler.WaterLevel01;
        }

        if (needle != null)
            needleBaseRotation = needle.localRotation;

        BuildCoalPieces();
    }

    private static Fx[] Build(ParticleSystem[] systems)
    {
        if (systems == null)
            return new Fx[0];

        System.Collections.Generic.List<Fx> list =
            new System.Collections.Generic.List<Fx>();

        foreach (ParticleSystem ps in systems)
        {
            if (ps != null)
                list.Add(new Fx(ps));
        }

        return list.ToArray();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        UpdateNearby();

        // ----- Zustaende gleiten weich auf ihre Zielwerte -----

        // Flamme waechst mit der Feuerstaerke (bei Klappe zu bleibt ein kleines Feuer).
        float fireTarget = boiler.IsFireLit ? Mathf.Lerp(0.3f, 1f, boiler.FireRate) : 0f;
        float fireRate = fireTarget > fireLevel
            ? fireRiseSpeed
            : fireFallSpeed;

        fireLevel = Mathf.MoveTowards(fireLevel, fireTarget, fireRate * dt);

        // Glut steigt mit dem Feuer und kuehlt danach langsam ab.
        emberLevel = Mathf.Max(
            fireLevel,
            Mathf.MoveTowards(emberLevel, 0f, emberFallSpeed * dt)
        );

        float pressure = boiler.Pressure01;
        float steamTarget = pressure > steamMinPressure ? pressure : 0f;
        steamLevel = Mathf.MoveTowards(steamLevel, steamTarget, 1f * dt);

        float ventTarget = boiler.IsVenting ? 1f : 0f;
        float ventRate = ventTarget > ventLevel ? 4f : 2f;
        ventLevel = Mathf.MoveTowards(ventLevel, ventTarget, ventRate * dt);

        // ----- Guenstige Teile: immer aktiv -----

        UpdateEmber();
        UpdateWaterGlass(dt);
        UpdateNeedle(pressure);
        UpdateCoalCount();

        // ----- Teure Teile: nur in der Naehe -----

        if (!nearby)
            return;

        Apply(flameFx, fireLevel);
        Apply(smokeFx, fireLevel);
        Apply(steamFx, steamLevel);
        Apply(ventFx, ventLevel);

        UpdateCoalGlow();
        UpdateLight();

        Drive(fireAudio, fireLevel, fireVolume);
        Drive(ventAudio, ventLevel, ventVolume);
    }

    private static void Apply(Fx[] systems, float level)
    {
        for (int i = 0; i < systems.Length; i++)
            systems[i].Set(level);
    }

    private static void Drive(AudioSource source, float level, float volume)
    {
        if (source == null)
            return;

        if (level > 0.01f)
        {
            source.volume = level * volume;

            if (!source.isPlaying)
                source.Play();
        }
        else if (source.isPlaying)
        {
            source.Stop();
        }
    }

    private void UpdateLight()
    {
        if (fireLight == null)
            return;

        float glow = Mathf.Clamp01(fireLevel + emberLevel * 0.2f);

        // Flackern nur bei echtem Feuer, nicht bei Glut.
        float noise =
            Mathf.PerlinNoise(Time.time * flickerSpeed, flickerSeed) * 2f - 1f;

        float flicker = 1f + noise * flickerAmount * fireLevel;
        float intensity = fireLightIntensity * glow * flicker;

        fireLight.intensity = intensity;
        fireLight.enabled = intensity > 0.01f;
    }

    private void UpdateEmber()
    {
        if (emberRenderer == null)
            return;

        if (Mathf.Abs(emberLevel - emberApplied) < 0.002f)
            return;

        emberApplied = emberLevel;

        emberRenderer.GetPropertyBlock(emberBlock);
        emberBlock.SetColor(emissionProperty, emberColor * emberLevel);
        emberRenderer.SetPropertyBlock(emberBlock);
    }

    private void UpdateWaterGlass(float dt)
    {
        if (waterFill == null)
            return;

        waterShown = Mathf.MoveTowards(
            waterShown, boiler.WaterLevel01, waterChangeSpeed * dt
        );

        float level = Mathf.Clamp01(waterShown);

        if (waterRenderer != null)
            waterRenderer.enabled = level > 0.005f;

        Vector3 scale = waterBaseScale;
        scale.y = waterBaseScale.y * Mathf.Max(0.0001f, level);
        waterFill.localScale = scale;

        if (!pivotAtBottom)
        {
            // Unterkante bleibt stehen, die Oberkante sinkt.
            Vector3 shift = waterFill.localRotation
                * (Vector3.down * (1f - level) * waterFullHeight * 0.5f);

            waterFill.localPosition = waterBasePosition + shift;
        }
    }

    private void UpdateNeedle(float pressure)
    {
        if (needle == null)
            return;

        float target = Mathf.Lerp(needleMinAngle, needleMaxAngle, pressure);

        needleAngle = Mathf.SmoothDamp(
            needleAngle, target, ref needleVelocity, needleSmoothTime
        );

        needle.localRotation =
            needleBaseRotation * Quaternion.AngleAxis(needleAngle, needleAxis);
    }

    // ---------- Kohle im Feuerraum ----------

    private static float Rnd(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    // Erzeugt alle Kohlestuecke einmal vorab (inaktiv).
    // Die unteren Stuecke liegen auf dem Rost, die oberen auf dem Haufen.
    private void BuildCoalPieces()
    {
        if (coalPiecePrefab == null || maxCoalPieces <= 0)
            return;

        Transform center = coalPileCenter != null ? coalPileCenter : transform;

        // Gemeinsames Material mit aktivierter Emission fuer alle Stuecke.
        Material source = null;

        foreach (Renderer r in
            coalPiecePrefab.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterial != null)
            {
                source = r.sharedMaterial;
                break;
            }
        }

        if (source != null)
        {
            coalMaterial = new Material(source);
            coalMaterial.EnableKeyword("_EMISSION");
            coalMaterial.globalIlluminationFlags =
                MaterialGlobalIlluminationFlags.RealtimeEmissive;
            coalMaterial.SetColor(emissionProperty, Color.black);
        }

        coalBlock = new MaterialPropertyBlock();
        coalPieces = new GameObject[maxCoalPieces];
        coalRenderers = new Renderer[maxCoalPieces][];
        coalPhase = new float[maxCoalPieces];

        System.Random rng = new System.Random(Mathf.Abs(GetInstanceID()));
        int groundCount = Mathf.CeilToInt(maxCoalPieces * 0.6f);

        for (int i = 0; i < maxCoalPieces; i++)
        {
            bool ground = i < groundCount;
            float maxRadius = ground ? 1f : 0.65f;

            float angle = Rnd(rng, 0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Rnd(rng, 0f, 1f)) * maxRadius;

            Vector3 position = new Vector3(
                Mathf.Cos(angle) * radius * coalPileSize.x * 0.5f,
                ground ? 0f : coalPileSize.y * Rnd(rng, 0.5f, 1f),
                Mathf.Sin(angle) * radius * coalPileSize.z * 0.5f
            );

            GameObject piece = Instantiate(coalPiecePrefab, center);
            piece.name = "CoalPiece";

            Transform t = piece.transform;
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(
                Rnd(rng, 0f, 360f), Rnd(rng, 0f, 360f), Rnd(rng, 0f, 360f)
            );
            t.localScale = coalPiecePrefab.transform.localScale
                * coalPieceScale * Rnd(rng, 0.85f, 1.15f);

            // Nur Optik: keine Physik, keine Raycast-Treffer.
            foreach (Collider c in piece.GetComponentsInChildren<Collider>(true))
                Destroy(c);

            foreach (Rigidbody rb in piece.GetComponentsInChildren<Rigidbody>(true))
                Destroy(rb);

            foreach (Transform tr in piece.GetComponentsInChildren<Transform>(true))
                tr.gameObject.layer = gameObject.layer;

            Renderer[] rends = piece.GetComponentsInChildren<Renderer>(true);

            if (coalMaterial != null)
            {
                foreach (Renderer r in rends)
                {
                    Material[] mats = new Material[r.sharedMaterials.Length];

                    for (int m = 0; m < mats.Length; m++)
                        mats[m] = coalMaterial;

                    r.sharedMaterials = mats;
                }
            }

            piece.SetActive(false);

            coalPieces[i] = piece;
            coalRenderers[i] = rends;
            coalPhase[i] = Rnd(rng, 0f, 100f);
        }
    }

    // Zeigt so viele Stuecke, wie dem Kohlevorrat entsprechen.
    private void UpdateCoalCount()
    {
        if (coalPieces == null)
            return;

        int target = 0;

        // Das Stueck, das gerade brennt, liegt noch im Feuer.
        if (boiler.CoalTotal > 0)
        {
            target = Mathf.Clamp(
                Mathf.CeilToInt(boiler.CoalTotal01 * coalPieces.Length),
                1,
                coalPieces.Length
            );
        }

        if (target == coalShown)
            return;

        for (int i = 0; i < coalPieces.Length; i++)
            coalPieces[i].SetActive(i < target);

        coalShown = target;
    }

    // Die Kohle gluehet mit dem Feuer und kuehlt danach rot ab.
    private void UpdateCoalGlow()
    {
        if (coalPieces == null || coalMaterial == null)
            return;

        bool glowing = emberLevel > 0.002f;

        // Schon dunkel und bleibt dunkel: nichts zu tun.
        if (!glowing && !coalGlowing)
            return;

        coalGlowing = glowing;

        float time = Time.time * coalFlickerSpeed;

        Color hot = Color.Lerp(
            coalCoolColor,
            coalGlowColor,
            Mathf.Clamp01(emberLevel * 1.3f)
        );

        for (int i = 0; i < coalPieces.Length; i++)
        {
            Color color = Color.black;

            if (glowing)
            {
                float noise = Mathf.PerlinNoise(time, coalPhase[i]);
                float k = emberLevel
                    * Mathf.Lerp(1f - coalFlickerAmount, 1f, noise);

                color = hot * k;
            }

            foreach (Renderer r in coalRenderers[i])
            {
                r.GetPropertyBlock(coalBlock);
                coalBlock.SetColor(emissionProperty, color);
                r.SetPropertyBlock(coalBlock);
            }
        }
    }

    private void OnDestroy()
    {
        if (coalMaterial != null)
            Destroy(coalMaterial);
    }

    // Zeigt im Editor, wo die Kohle liegt (BoilerVisuals markieren).
    // Der Rahmen ist auch durch das Modell hindurch sichtbar.
    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        Transform center = coalPileCenter != null ? coalPileCenter : transform;

        Matrix4x4 oldMatrix = Handles.matrix;
        Color oldColor = Handles.color;
        UnityEngine.Rendering.CompareFunction oldZTest = Handles.zTest;

        Handles.matrix = center.localToWorldMatrix;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = new Color(1f, 0.5f, 0.1f, 1f);

        // Untere Schicht (auf dem Rost).
        Handles.DrawWireCube(
            Vector3.zero,
            new Vector3(coalPileSize.x, 0.01f, coalPileSize.z)
        );

        // Obere Schicht (auf dem Haufen).
        Handles.DrawWireCube(
            Vector3.up * coalPileSize.y * 0.75f,
            new Vector3(
                coalPileSize.x * 0.65f,
                coalPileSize.y * 0.5f,
                coalPileSize.z * 0.65f
            )
        );

        Handles.matrix = oldMatrix;
        Handles.color = oldColor;
        Handles.zTest = oldZTest;
#endif
    }

    // ---------- Entfernung ----------

    private void UpdateNearby()
    {
        if (Time.time < nextCullCheck)
            return;

        nextCullCheck = Time.time + cullCheckInterval;

        if (maxEffectDistance <= 0f)
        {
            nearby = true;
            return;
        }

        if (cam == null)
            cam = Camera.main;

        if (cam == null && Camera.allCamerasCount > 0)
            cam = Camera.allCameras[0];

        if (cam == null)
        {
            nearby = true;
            return;
        }

        float sq = (cam.transform.position - transform.position).sqrMagnitude;

        // Kleine Hysterese, damit es an der Grenze nicht flackert.
        float limit = nearby ? maxEffectDistance : maxEffectDistance * 0.9f;
        bool now = sq < limit * limit;

        if (now == nearby)
            return;

        nearby = now;

        if (!nearby)
            ClearAll();
    }

    private void ClearAll()
    {
        Clear(flameFx);
        Clear(smokeFx);
        Clear(steamFx);
        Clear(ventFx);

        if (fireLight != null)
            fireLight.enabled = false;

        if (fireAudio != null)
            fireAudio.Stop();

        if (ventAudio != null)
            ventAudio.Stop();
    }

    private static void Clear(Fx[] systems)
    {
        if (systems == null)
            return;

        for (int i = 0; i < systems.Length; i++)
            systems[i].Clear();
    }
}
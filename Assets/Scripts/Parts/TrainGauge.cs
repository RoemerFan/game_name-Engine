using UnityEngine;

// =====================================================================
//  TrainGauge: eine mechanische Anzeige an der Lok (Zeiger-Instrument oder Fuellstands-Anzeige).
//  OPTIONAL: Kein Muss; die Textanzeige am Bildschirmrand bleibt.
//
//  Einrichten: das Skript an ein Objekt im Fuehrerstand (oder am Kessel, am Tender ...). Die Lok wird automatisch
//  im Elternobjekt gefunden. Dann bei "Source" waehlen, WAS angezeigt wird, und bei "Style", WIE:
//    NEEDLE = Zeiger: das Feld "Needle" (Objekt, das sich um seinen Ursprung dreht) schwenkt zwischen Min und Max Winkel.
//    FILL   = Fuellstand: das Feld "Fill" (Zylinder oder Quader) waechst in der lokalen Y-Achse, wie beim
//             Wasserstandsglas des Kessels. Mit "Pivot At Bottom" liegt der Ursprung unten, sonst in der Mitte.
//
//  Der Kessel hat sein eigenes Wasserstandsglas und Manometer (BoilerVisuals). Dieses Skript ist fuer alles
//  andere: Geschwindigkeit, Regler, Tender-Vorraete, Wasser im Tender, zusaetzliche Anzeigen im Fuehrerstand.
// =====================================================================
public class TrainGauge : MonoBehaviour
{
    public enum Source
    {
        Speed,            // Geschwindigkeit (km/h, Skala bis "Max Value")
        Throttle,         // Reglerstellung
        BoilerPressure,   // Dampfdruck im Kessel
        BoilerWater,      // Wasserstand im Kessel
        BoilerCoal,       // Kohle im Feuerraum
        TenderFuel,       // Brennstoff im Tender (Kohle/Diesel/Batterie)
        TenderWater,      // Wasser im Tender
        TrainMass         // Zuggewicht (Tonnen, Skala bis "Max Value")
    }

    public enum Style
    {
        Needle,
        Fill
    }

    [Header("Anzeige")]
    public Source source = Source.BoilerPressure;
    public Style style = Style.Needle;

    [Tooltip("Die Lok (leer = im Elternobjekt gesucht).")]
    public Locomotive loco;

    [Tooltip("Nur fuer Geschwindigkeit (km/h) und Zuggewicht (t): Wert, bei dem der Zeiger ganz oben steht.")]
    public float maxValue = 30f;

    [Header("Zeiger (Style = Needle)")]
    [Tooltip("Das Objekt, das sich dreht. Ursprung (Pivot) = Drehpunkt.")]
    public Transform needle;

    public Vector3 needleAxis = Vector3.forward;
    public float needleMinAngle = -120f;
    public float needleMaxAngle = 120f;

    [Tooltip("Wie traege der Zeiger folgt (Sekunden). Kleiner = schneller.")]
    public float smoothTime = 0.4f;

    [Header("Fuellstand (Style = Fill)")]
    [Tooltip("Zylinder oder Quader. Die Hoehe folgt der lokalen Y-Achse.")]
    public Transform fill;

    [Tooltip("An: Ursprung liegt unten. Aus: Ursprung liegt in der Mitte (wie beim Unity-Zylinder).")]
    public bool pivotAtBottom = true;

    [Tooltip("Hoehe des vollen Zylinders in lokalen Einheiten (nur bei 'Pivot At Bottom' aus, Unity-Zylinder = 2).")]
    public float fullHeight = 2f;

    [Tooltip("Wie schnell die Anzeige folgt (pro Sekunde).")]
    public float fillSpeed = 0.4f;

    private Quaternion needleBase = Quaternion.identity;
    private Vector3 fillBaseScale = Vector3.one;
    private Vector3 fillBasePosition;
    private float shown;
    private float velocity;
    private float refreshTimer;

    private Boiler boiler;
    private TrainTender tender;

    // Aktueller Wert von 0 bis 1.
    public float Value01
    {
        get
        {
            if (loco == null)
                return 0f;

            switch (source)
            {
                case Source.Speed:
                    return Mathf.Clamp01(Mathf.Abs(loco.speed) * 3.6f / Mathf.Max(0.1f, maxValue));

                case Source.Throttle:
                    return Mathf.Clamp01(loco.throttle);

                case Source.BoilerPressure:
                    return boiler != null ? Mathf.Clamp01(boiler.Pressure01) : 0f;

                case Source.BoilerWater:
                    return boiler != null ? Mathf.Clamp01(boiler.WaterLevel01) : 0f;

                case Source.BoilerCoal:
                    return boiler != null ? Mathf.Clamp01(boiler.CoalTotal01) : 0f;

                case Source.TenderFuel:
                    return tender != null ? tender.Fraction : 0f;

                case Source.TenderWater:
                    return tender != null ? tender.WaterFraction : 0f;

                default:
                    float total, rated;
                    loco.GetConsistMass(out total, out rated);
                    return Mathf.Clamp01(total / Mathf.Max(0.1f, maxValue));
            }
        }
    }

    void Awake()
    {
        if (loco == null)
            loco = GetComponentInParent<Locomotive>();

        if (needle != null)
            needleBase = needle.localRotation;

        if (fill != null)
        {
            fillBaseScale = fill.localScale;
            fillBasePosition = fill.localPosition;
        }

        FindParts();
        shown = Value01;
        Apply(0.0001f, true);
    }

    // Kessel und Tender der Lok suchen (sie koennen ausgetauscht werden).
    private void FindParts()
    {
        if (loco == null)
            return;

        boiler = loco.GetComponentInChildren<Boiler>(true);
        tender = loco.tender != null ? loco.tender : loco.GetComponentInChildren<TrainTender>(true);
    }

    void Update()
    {
        refreshTimer -= Time.deltaTime;

        if (refreshTimer <= 0f)
        {
            refreshTimer = 1f;
            FindParts();
        }

        Apply(Time.deltaTime, false);
    }

    private void Apply(float dt, bool instant)
    {
        float target = Value01;

        if (style == Style.Needle)
        {
            if (needle == null)
                return;

            shown = instant ? target : Mathf.SmoothDamp(shown, target, ref velocity, Mathf.Max(0.01f, smoothTime));

            Vector3 axis = needleAxis.sqrMagnitude > 0.0001f ? needleAxis.normalized : Vector3.forward;
            float angle = Mathf.Lerp(needleMinAngle, needleMaxAngle, shown);
            needle.localRotation = needleBase * Quaternion.AngleAxis(angle, axis);
        }
        else
        {
            if (fill == null)
                return;

            shown = instant ? target : Mathf.MoveTowards(shown, target, fillSpeed * dt);
            float level = Mathf.Clamp01(shown);

            Vector3 scale = fillBaseScale;
            scale.y = fillBaseScale.y * Mathf.Max(0.0001f, level);
            fill.localScale = scale;

            // Fuellung unsichtbar machen, wenn leer.
            Renderer r = fill.GetComponent<Renderer>();

            if (r != null)
                r.enabled = level > 0.005f;

            if (!pivotAtBottom)
            {
                // Unterkante bleibt stehen, die Oberkante sinkt.
                Vector3 shift = fill.localRotation * (Vector3.down * (1f - level) * fullHeight * fillBaseScale.y * 0.5f);
                fill.localPosition = fillBasePosition + shift;
            }
        }
    }
}
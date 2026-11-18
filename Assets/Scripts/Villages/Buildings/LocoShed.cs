using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  LocoShed: der LOKSCHUPPEN. Ein Gebaeude, das ueber dem Gleis steht. Loks, die darin halten, werden
//  automatisch mit Wasser und Diesel aus den Vorraeten des Schuppens versorgt.
//
//  VORRAETE:
//   - Wasser: Der Schuppen hat einen Wassertank mit Wasser-Port (Eingang). Mit Pumpe/Rohr/Schlauch fuellst du
//     ihn wie jeden anderen Tank.
//   - Diesel: Wirf Dieselkanister (FuelCanister) in den Schuppen, ihr Inhalt landet im Dieselvorrat.
//   (Kohle fuer Dampfloks schaufelst du weiterhin selbst in den Tender; Strom-Loks laden spaeter.)
//
//  FUER DEN SPIELER: Am Tank haengt eine Schlauchtrommel (HoseReel), die aus dem Tank des Schuppens gespeist
//  wird. Duese nehmen, an den Wasser-Einfuellstutzen des Tenders anschliessen, fertig - genau wie bei jedem
//  anderen Wasserkran. Spielerloks werden NICHT automatisch betankt.
//
//  AUTOMATISCH: Nur Zuege, die als "IAutoServiced" markiert sind (kommen spaeter mit den NPC-Zuegen), werden
//  im Schuppen von selbst mit Wasser und Diesel versorgt, wenn sie dort fast still stehen ("Max Service Speed").
//  Pro Sekunde fliessen "Water Per Second" / "Diesel Per Second". Wer andere Loks automatisch versorgen will,
//  schaltet "Auto Service All Trains" ein oder ruft ServiceLocomotive() selbst auf.
//
//  EINRICHTEN (wie die anderen Maschinen, ohne Editor-Skript):
//   1. Leeres GameObject, dieses Skript draufziehen, als Prefab speichern, beim BuildMenuController eintragen.
//   2. Die Gleisrichtung ist die Z-Achse des Objekts: Der Schuppen steht so, dass das Gleis in Z-Richtung
//      mittig hindurchlaeuft. Beide Enden sind offen.
//   3. Ohne eigenes Modell baut das Skript einen einfachen Platzhalter (Halle mit Dach, Wassertank, Dieselfass).
//      Eigenes Modell: bei "Model Prefab" eintragen, dann wird nichts gebaut.
//   4. Zone, Wasser-Port und Wassertank-/Fass-Lage lassen sich im Inspector aendern. Ein eigener Wasser-Port
//      (Port-Objekt im Prefab, Medium Water, Eingang) kann bei "Water Port" eingetragen werden.
// =====================================================================
// Marker fuer Zuege, die der Schuppen automatisch versorgt (spaeter NPC-Zuege): Eine Komponente an der Lok
// (oder einem Elternobjekt) implementiert dieses Interface. Ohne Methoden, nur eine Markierung.
public interface IAutoServiced { }

public class LocoShed : MonoBehaviour, IPortOwner, ISaveable
{
    [Header("Halle")]
    [Tooltip("Innenmasse der Halle (Meter): X = Breite, Y = Hoehe, Z = Laenge (Gleisrichtung).")]
    [SerializeField] private Vector3 hallSize = new Vector3(6f, 5f, 18f);

    [Tooltip("Loks innerhalb dieser Box (am Mittelpunkt gemessen) zaehlen als 'im Schuppen'. Leer = automatisch " +
             "aus der Hallengroesse. Optional eigene Trigger-Box (nimmt auch die Dieselkanister an).")]
    [SerializeField] private BoxCollider zone;

    [Header("Versorgung")]
    [SerializeField] private float waterCapacity = 2000f;
    [SerializeField] private float waterStart = 500f;
    [SerializeField] private float waterPerSecond = 30f;

    [SerializeField] private float dieselCapacity = 600f;
    [SerializeField] private float dieselStart = 0f;
    [SerializeField] private float dieselPerSecond = 8f;

    [Tooltip("Aus (Standard): Nur markierte Zuege (IAutoServiced, spaeter NPC-Zuege) werden automatisch versorgt, " +
             "Spielerloks nutzen den Schlauch. Ein: wirklich alle Loks im Schuppen (zum Testen).")]
    [SerializeField] private bool autoServiceAllTrains = false;

    [Tooltip("Hoechstes Tempo (m/s), bei dem eine Lok noch versorgt wird.")]
    [SerializeField] private float maxServiceSpeed = 0.3f;

    [Header("Wasseranschluss")]
    [Tooltip("Optional: eigener Wasser-Port (Eingang). Leer = wird am Wassertank automatisch angelegt.")]
    [SerializeField] private Port waterPort;
    [Tooltip("Lage des Wassertanks (und des Ports) relativ zum Schuppen.")]
    [SerializeField] private Vector3 tankPosition = new Vector3(4.2f, 0f, -6f);
    [SerializeField] private float portFlowPerSecond = 40f;

    [Header("Schlauch fuer den Spieler")]
    [Tooltip("Optional: eigene Schlauchtrommel im Prefab (Infinite Water aus). Leer = wird am Tank automatisch angelegt.")]
    [SerializeField] private HoseReel hoseReel;
    [Tooltip("Lage der automatisch angelegten Schlauchtrommel relativ zum Schuppen.")]
    [SerializeField] private Vector3 hosePosition = new Vector3(4.2f, 0f, -4.2f);
    [SerializeField] private float hoseLength = 20f;
    [Tooltip("Wie viel Wasser die Trommel vorhaelt (Liter) und wie schnell die Duese abgibt (pro Sekunde).")]
    [SerializeField] private float hoseBuffer = 60f;
    [SerializeField] private float hoseFlowPerSecond = 12f;

    [Header("Aussehen")]
    [Tooltip("Optional: eigenes Modell. Gesetzt = der Platzhalter wird nicht gebaut.")]
    [SerializeField] private GameObject modelPrefab;

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showInfo = true;
    [SerializeField] private float infoDistance = 14f;
    [SerializeField] private float infoHeight = 6f;

    private float water;
    private float diesel;
    private int trainsInside;
    private int servedCount;
    private bool waterRestored;

    private readonly List<Material> materials = new List<Material>();

    public float Water { get { return water; } }
    public float Diesel { get { return diesel; } }

    private void Awake()
    {
        // Eigene Optik: entweder steckt das Modell schon als Kind im Prefab (dann wird nichts gebaut), oder es
        // steht im Feld "Model Prefab" (wird als Kind erzeugt), sonst entsteht der Platzhalter.
        bool ownVisuals = HasOwnVisuals();

        if (ownVisuals)
        {
            // nichts zu tun
        }
        else if (modelPrefab != null && modelPrefab.GetComponentInChildren<LocoShed>(true) == null)
        {
            GameObject model = Instantiate(modelPrefab, transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
        }
        else
        {
            if (modelPrefab != null)
                Debug.LogWarning("LocoShed: 'Model Prefab' darf nicht das Schuppen-Prefab selbst sein (es enthaelt das Skript). Platzhalter wird gebaut.", this);

            BuildPlaceholder();
        }

        // Trigger fuer Dieselkanister: eigene Box oder automatisch aus der Hallengroesse.
        if (zone == null)
        {
            GameObject z = new GameObject("Schuppenbereich");
            z.transform.SetParent(transform, false);
            z.transform.localPosition = new Vector3(0f, hallSize.y * 0.5f, 0f);
            zone = z.AddComponent<BoxCollider>();
            zone.size = hallSize;
        }

        zone.isTrigger = true;

        if (zone.gameObject != gameObject && zone.GetComponent<ShedRelay>() == null)
            zone.gameObject.AddComponent<ShedRelay>().owner = this;

        EnsureWaterPort();
        EnsureHoseReel();
    }

    // Schlauchtrommel am Tank: wird aus dem Wasser des Schuppens gespeist.
    private void EnsureHoseReel()
    {
        if (hoseReel == null)
            hoseReel = GetComponentInChildren<HoseReel>(true);

        if (hoseReel != null)
        {
            hoseReel.infiniteWater = false;
            return;
        }

        // Inaktiv anlegen, einstellen, dann aktivieren: so laeuft Awake der Trommel mit den richtigen Werten.
        GameObject g = new GameObject("Schlauchtrommel");
        g.SetActive(false);
        g.transform.SetParent(transform, false);
        g.transform.localPosition = hosePosition;

        // Optik: ein kleiner Kasten als Halterung (Platzhalter).
        if (modelPrefab == null)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Halterung";
            box.transform.SetParent(g.transform, false);
            box.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            box.transform.localScale = new Vector3(0.4f, 1f, 0.4f);
            box.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.2f, 0.3f, 0.5f));
        }

        hoseReel = g.AddComponent<HoseReel>();
        hoseReel.infiniteWater = false;
        hoseReel.bufferCapacity = hoseBuffer;
        hoseReel.flowPerSecond = hoseFlowPerSecond;
        hoseReel.hoseLength = hoseLength;

        g.SetActive(true);
    }

    private void Start()
    {
        if (!waterRestored)
        {
            water = Mathf.Clamp(waterStart, 0f, waterCapacity);
            diesel = Mathf.Clamp(dieselStart, 0f, dieselCapacity);
        }
    }

    private void OnDestroy()
    {
        foreach (Material m in materials)
        {
            if (m != null)
                Destroy(m);
        }
    }

    // Hat das Prefab schon eigene Modell-Teile (Renderer) als Kinder? Dann wird kein Platzhalter gebaut und nichts
    // zusaetzlich erzeugt. Die Schlauchtrommel und der Port zaehlen nicht.
    private bool HasOwnVisuals()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.GetComponentInParent<HoseReel>() == null)
                return true;
        }

        return false;
    }

    // ---------- Wasseranschluss ----------

    private void EnsureWaterPort()
    {
        if (waterPort != null)
            return;

        foreach (Port p in GetComponentsInChildren<Port>(true))
        {
            if (p.RawMedium == PortMedium.Water)
            {
                waterPort = p;
                return;
            }
        }

        GameObject portObject = new GameObject("WasserPort");
        portObject.transform.SetParent(transform, false);
        portObject.transform.localPosition = tankPosition + new Vector3(0.8f, 0.7f, 0f);
        portObject.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);   // Blick nach aussen (+X)

        waterPort = portObject.AddComponent<Port>();
        waterPort.Configure(PortMedium.Water, PortDirection.Input, portFlowPerSecond);
    }

    // IPortOwner: Der Schuppen nimmt Wasser an.
    public float GetAvailable(Port port) { return 0f; }

    public float GetFreeSpace(Port port)
    {
        return port.Medium == PortMedium.Water ? Mathf.Max(0f, waterCapacity - water) : 0f;
    }

    public float TakeFrom(Port port, float amountWanted) { return 0f; }

    public float GiveTo(Port port, float amountGiven)
    {
        if (port.Medium != PortMedium.Water)
            return 0f;

        float accepted = Mathf.Clamp(amountGiven, 0f, waterCapacity - water);
        water += accepted;
        return accepted;
    }

    // ---------- Dieselkanister ----------

    internal void OfferItem(Collider other)
    {
        FuelCanister canister = other.GetComponentInParent<FuelCanister>();

        if (canister == null || CoalPickup.LocalHeldItem == canister)
            return;

        if (dieselCapacity - diesel < 0.01f || canister.Liters <= 0f)
            return;

        if (!NetItems.CanAct(canister.gameObject))
            return;

        float accepted = Mathf.Min(canister.Liters, dieselCapacity - diesel);
        diesel += accepted;
        canister.Liters -= accepted;

        // Leere Kanister bleiben als Gegenstand erhalten, nur der Inhalt ist weg.
    }

    private void OnTriggerEnter(Collider other)
    {
        OfferItem(other);
    }

    private class ShedRelay : MonoBehaviour
    {
        public LocoShed owner;

        private void OnTriggerEnter(Collider other)
        {
            if (owner != null)
                owner.OfferItem(other);
        }
    }

    // ---------- Versorgung der Loks ----------

    private bool IsInside(Vector3 worldPoint)
    {
        Vector3 local = zone.transform.InverseTransformPoint(worldPoint) - zone.center;
        Vector3 half = Vector3.Scale(zone.size, zone.transform.lossyScale) * 0.5f;
        Vector3 scale = zone.transform.lossyScale;

        // lokale Koordinaten sind unskaliert, die Halbmasse in Weltmassen: umrechnen.
        Vector3 localHalf = new Vector3(
            scale.x != 0f ? half.x / scale.x : 0f,
            scale.y != 0f ? half.y / scale.y : 0f,
            scale.z != 0f ? half.z / scale.z : 0f);

        return Mathf.Abs(local.x) <= localHalf.x && Mathf.Abs(local.y) <= localHalf.y + 1f && Mathf.Abs(local.z) <= localHalf.z;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        trainsInside = 0;
        servedCount = 0;

        // Schlauchtrommel aus dem Tank nachfuellen.
        if (hoseReel != null && water > 0f)
            water -= hoseReel.AddToBuffer(Mathf.Min(water, hoseFlowPerSecond * 2f * dt));

        // In Mehrspielerspielen rechnet jeder Rechner den Vorrat selbst; die Zuege kommen ueber das Netz.
        for (int i = 0; i < TrainCar.All.Count; i++)
        {
            Locomotive loco = TrainCar.All[i] as Locomotive;

            if (loco == null || !IsInside(loco.transform.position))
                continue;

            trainsInside++;

            // Spielerloks werden nicht automatisch versorgt (Schlauch benutzen), nur markierte Zuege.
            if (!autoServiceAllTrains && loco.GetComponentInParent<IAutoServiced>() == null)
                continue;

            if (ServiceLocomotive(loco, dt))
                servedCount++;
        }
    }

    // Versorgt eine Lok fuer einen Zeitschritt mit Wasser und Diesel (aus den Vorraeten). Gibt zurueck, ob etwas
    // geflossen ist. Wird vom Schuppen selbst fuer markierte Zuege aufgerufen, sonst von NPC-Zuegen.
    public bool ServiceLocomotive(Locomotive loco, float dt)
    {
        {
            if (loco == null || Mathf.Abs(loco.speed) > maxServiceSpeed || loco.tender == null)
                return false;

            TrainTender tender = loco.tender;
            bool served = false;

            if (tender.HasWaterTank && water > 0f && tender.WaterFraction < 0.999f)
            {
                float given = tender.AddWater(Mathf.Min(waterPerSecond * dt, water));
                water -= given;
                served |= given > 0f;
            }

            if (tender.fuelType == FuelType.Diesel && diesel > 0f && tender.Fraction < 0.999f)
            {
                float given = tender.Add(Mathf.Min(dieselPerSecond * dt, diesel));
                diesel -= given;
                served |= given > 0f;
            }

            return served;
        }
    }

    // ---------- Platzhalter ----------

    private void BuildPlaceholder()
    {
        Material wall = Mat(new Color(0.55f, 0.33f, 0.26f));
        Material roof = Mat(new Color(0.3f, 0.3f, 0.33f));
        Material steel = Mat(new Color(0.5f, 0.55f, 0.6f));
        Material dark = Mat(new Color(0.15f, 0.15f, 0.17f));

        float w = hallSize.x;
        float h = hallSize.y;
        float l = hallSize.z;
        float t = 0.3f;

        // Zwei Seitenwaende mit Luecke fuer Fenster (nur Optik: ein dunkles Band), beide Enden offen.
        Part("WandLinks", PrimitiveType.Cube, new Vector3(-w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, l), wall, true);
        Part("WandRechts", PrimitiveType.Cube, new Vector3(w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, l), wall, true);
        Part("FensterLinks", PrimitiveType.Cube, new Vector3(-w * 0.5f + 0.02f, h * 0.65f, 0f), new Vector3(0.05f, 0.9f, l * 0.7f), dark, false);
        Part("FensterRechts", PrimitiveType.Cube, new Vector3(w * 0.5f - 0.02f, h * 0.65f, 0f), new Vector3(0.05f, 0.9f, l * 0.7f), dark, false);

        // Satteldach aus zwei geneigten Platten.
        float slope = 18f;
        float half = (w + 2f * t) * 0.5f;
        float plate = half / Mathf.Cos(slope * Mathf.Deg2Rad) + 0.2f;

        GameObject left = Part("DachLinks", PrimitiveType.Cube, new Vector3(-half * 0.5f, h + half * 0.5f * Mathf.Tan(slope * Mathf.Deg2Rad), 0f),
                               new Vector3(plate, 0.2f, l + 0.6f), roof, false);
        left.transform.localRotation = Quaternion.Euler(0f, 0f, -slope);

        GameObject right = Part("DachRechts", PrimitiveType.Cube, new Vector3(half * 0.5f, h + half * 0.5f * Mathf.Tan(slope * Mathf.Deg2Rad), 0f),
                                new Vector3(plate, 0.2f, l + 0.6f), roof, false);
        right.transform.localRotation = Quaternion.Euler(0f, 0f, slope);

        // Wassertank (stehender Zylinder auf Stuetzen) und Dieselfass neben der Halle.
        Vector3 tp = tankPosition;
        Part("TankStuetze1", PrimitiveType.Cube, tp + new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.12f, 1.0f, 0.12f), steel, false);
        Part("TankStuetze2", PrimitiveType.Cube, tp + new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.12f, 1.0f, 0.12f), steel, false);
        Part("TankStuetze3", PrimitiveType.Cube, tp + new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.12f, 1.0f, 0.12f), steel, false);
        Part("TankStuetze4", PrimitiveType.Cube, tp + new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.12f, 1.0f, 0.12f), steel, false);
        Part("Wassertank", PrimitiveType.Cylinder, tp + new Vector3(0f, 1.9f, 0f), new Vector3(1.8f, 0.9f, 1.8f), steel, true);

        Part("Dieselfass", PrimitiveType.Cylinder, tp + new Vector3(0f, 0.55f, 2.6f), new Vector3(0.9f, 0.55f, 0.9f), Mat(new Color(0.75f, 0.55f, 0.1f)), true);
    }

    private Material Mat(Color color)
    {
        Material m = TrackManager.MakeMaterial(color, 0.1f);
        materials.Add(m);
        return m;
    }

    private GameObject Part(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material m, bool collider)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(transform, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;

        if (!collider)
            Destroy(g.GetComponent<Collider>());

        return g;
    }

    // ---------- Anzeige ----------

    private void OnGUI()
    {
        if (!showInfo)
            return;

        Camera cam = TrackManager.Instance != null ? TrackManager.Instance.PlayerCamera : null;

        if (cam == null)
            cam = Camera.main;

        if (cam == null)
            return;

        Vector3 world = transform.position + Vector3.up * infoHeight;

        if ((world - cam.transform.position).sqrMagnitude > infoDistance * infoDistance)
            return;

        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return;

        string text = Loc.T("shed.info",
            Mathf.RoundToInt(water), Mathf.RoundToInt(waterCapacity),
            Mathf.RoundToInt(diesel), Mathf.RoundToInt(dieselCapacity),
            trainsInside);

        if (servedCount > 0)
            text += "\n" + Loc.T("shed.serving");

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("water", water);
        bag.SetFloat("diesel", diesel);
    }

    public void LoadState(StateBag bag)
    {
        waterRestored = true;
        water = Mathf.Clamp(bag.GetFloat("water", water), 0f, waterCapacity);
        diesel = Mathf.Clamp(bag.GetFloat("diesel", diesel), 0f, dieselCapacity);
    }
}
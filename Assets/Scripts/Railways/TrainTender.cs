using System.Globalization;
using UnityEngine;

// =====================================================================
//  TrainTender: der Vorrat der Lok. Je nach "Fuel Type":
//    Coal     - Kohlelager. Dafuer braucht der Tender ein ItemStorage (ganze Kohlestuecke,
//               werden per E herausgenommen und von Hand in die Feuerbuechse geworfen).
//    Diesel   - Dieseltank (Liter). Wird mit Kanistern gefuellt (TenderFuelInlet).
//    Electric - Batterie (kWh).
//  Der Tender liegt als eigenes Prefab im Lok-Typ (TrackManager > Locomotives > Tender Prefab).
// =====================================================================
public class TrainTender : MonoBehaviour, ISaveable, IPortOwner
{
    public FuelType fuelType = FuelType.Coal;

    [Tooltip("Fassungsvermoegen: Liter (Diesel) oder kWh (Batterie). Beim Kohlelager gilt 'Max Items' des ItemStorage.")]
    public float capacity = 100f;

    [Tooltip("Fuellung beim Aufstellen (Liter bzw. kWh).")]
    public float startAmount = 50f;

    [Tooltip("Nur Kohle: das Lager mit den Stuecken (wird automatisch gefunden).")]
    public ItemStorage coalStorage;

    [Header("Wassertank (nur Dampflok mit Kohle-Tender)")]
    [Tooltip("Fassungsvermoegen des Wassertanks. 0 = kein Tank (der Kessel haelt dann nur sein eigenes Wasser). " +
             "Zum Vergleich: Der Kessel fasst standardmaessig 100.")]
    public float waterCapacity = 0f;

    [Tooltip("Wasser im Tank beim Aufstellen.")]
    public float waterStart = 150f;

    [Tooltip("So viel Wasser (pro Sekunde) pumpt der Tender in den Kessel, solange der Kessel nicht voll ist.")]
    public float feedPerSecond = 3f;

    [Tooltip("Der Kessel wird nachgespeist, wenn er unter diesen Fuellstand (0-1) faellt.")]
    [Range(0.1f, 1f)] public float feedBelowFraction = 0.9f;

    [Tooltip("Gewicht pro Wassereinheit (Tonnen). 1 Einheit = 10 Liter = 0,01 t.")]
    public float tonsPerWaterUnit = 0.01f;

    [Tooltip("Optional: eigenes Modell fuer den Wassertank. Leer = Platzhalter. Das Prefab darf einen " +
             "Port (Wasser, Eingang) enthalten, sonst wird am Wassertank-Socket einer angelegt.")]
    public GameObject waterTankPrefab;

    [Tooltip("Hier sitzt der Wassertank (leeres Objekt am Tender; leer = Platzhalter-Lage oben auf dem Tender).")]
    public Transform waterTankSocket;

    [Tooltip("Der Einfuellstutzen: Port (Wasser, Eingang). Leer = wird automatisch gefunden oder angelegt.")]
    public Port waterPort;

    private float amount;
    private float water;
    private bool tankBuilt;
    private bool waterRestored;
    private Boiler boiler;

    // Aktuelle Menge (bei Kohle: Anzahl Stuecke).
    public float Amount
    {
        get { return fuelType == FuelType.Coal ? (coalStorage != null ? coalStorage.Count : 0f) : amount; }
    }

    public float Capacity
    {
        get { return fuelType == FuelType.Coal ? (coalStorage != null ? coalStorage.maxItems : 0f) : capacity; }
    }

    public float Fraction { get { return Capacity > 0f ? Mathf.Clamp01(Amount / Capacity) : 0f; } }

    void Awake()
    {
        if (fuelType == FuelType.Coal && coalStorage == null)
            coalStorage = GetComponentInChildren<ItemStorage>();

        amount = Mathf.Clamp(startAmount, 0f, capacity);
    }

    void Start()
    {
        // Startfuellung des Wassers erst hier: Platzhalter setzen ihre Werte nach dem Anlegen. Wurde der
        // Stand schon aus einem Spielstand geladen, bleibt er.
        if (!waterRestored)
            water = Mathf.Clamp(waterStart, 0f, waterCapacity);

        EnsureWaterTank();
    }

    // ---------- Wasser ----------

    public bool HasWaterTank { get { return waterCapacity > 0f; } }
    public float Water { get { return water; } }
    public float WaterFraction { get { return waterCapacity > 0f ? Mathf.Clamp01(water / waterCapacity) : 0f; } }

    // Gewicht des Tenders mit Vorraeten (Tonnen).
    public float MassTons
    {
        get
        {
            float t = 4f;   // leerer Tender

            if (fuelType == FuelType.Coal)
                t += Amount * 0.02f;
            else if (fuelType == FuelType.Diesel)
                t += amount * 0.00085f;
            else
                t += amount * 0.02f;

            return t + water * tonsPerWaterUnit;
        }
    }

    // Fuellt Wasser nach (Pumpe, Schlauch). Gibt zurueck, wie viel hineinpasste.
    public float AddWater(float value)
    {
        float accepted = Mathf.Clamp(value, 0f, waterCapacity - water);
        water += accepted;
        return accepted;
    }

    // Legt den Wassertank an: eigenes Prefab, sonst Platzhalter. Es gibt immer einen Einfuell-Port.
    public void EnsureWaterTank()
    {
        if (tankBuilt || waterCapacity <= 0f)
            return;

        tankBuilt = true;

        // Hat der Tender schon einen Wasser-Port (selbst gebautes Modell), ist nichts zu tun.
        if (waterPort == null)
        {
            foreach (Port p in GetComponentsInChildren<Port>(true))
            {
                if (p.RawMedium == PortMedium.Water)
                {
                    waterPort = p;
                    break;
                }
            }
        }

        if (waterPort != null)
            return;

        Transform parent = waterTankSocket != null ? waterTankSocket : transform;

        if (waterTankPrefab != null)
        {
            GameObject model = Instantiate(waterTankPrefab, parent);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            foreach (Port p in model.GetComponentsInChildren<Port>(true))
            {
                if (p.RawMedium == PortMedium.Water)
                {
                    waterPort = p;
                    break;
                }
            }

            if (waterPort != null)
                return;

            // Prefab ohne Port: der Stutzen kommt an den Socket.
            waterPort = CreatePort(parent, Vector3.zero, Quaternion.LookRotation(Vector3.right));
            return;
        }

        // Platzhalter: liegender Zylinder oben auf dem Tender mit Stutzen an der rechten Seite.
        Transform t = waterTankSocket != null ? waterTankSocket : transform;
        Vector3 center = waterTankSocket != null ? Vector3.zero : new Vector3(0f, 1.55f, -2.7f);

        GameObject tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tank.name = "WassertankPlatzhalter";
        tank.transform.SetParent(t, false);
        tank.transform.localPosition = center;
        tank.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
        tank.transform.localScale = new Vector3(0.9f, 0.65f, 0.9f);
        tank.GetComponent<Renderer>().sharedMaterial = TrackManager.MakeMaterial(new Color(0.25f, 0.3f, 0.38f), 0.3f);
        Destroy(tank.GetComponent<Collider>());

        GameObject spout = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        spout.name = "Einfuellstutzen";
        spout.transform.SetParent(t, false);
        spout.transform.localPosition = center + new Vector3(0.5f, 0f, 0f);
        spout.transform.localEulerAngles = new Vector3(0f, 0f, 90f);
        spout.transform.localScale = new Vector3(0.14f, 0.08f, 0.14f);
        spout.GetComponent<Renderer>().sharedMaterial = TrackManager.MakeMaterial(new Color(0.7f, 0.55f, 0.2f), 0.4f);
        Destroy(spout.GetComponent<Collider>());

        waterPort = CreatePort(t, center + new Vector3(0.58f, 0f, 0f), Quaternion.LookRotation(Vector3.right));
    }

    private Port CreatePort(Transform parent, Vector3 localPos, Quaternion localRot)
    {
        GameObject go = new GameObject("WasserPort");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;

        SphereCollider col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.22f;

        Port port = go.AddComponent<Port>();
        port.Configure(PortMedium.Water, PortDirection.Input, 5f);
        return port;
    }

    void Update()
    {
        // Speisewasser: nur der Host rechnet, die Mitspieler bekommen den Stand per Abgleich.
        if (waterCapacity <= 0f || water <= 0f || NetSync.IsClient)
            return;

        if (boiler == null)
        {
            Locomotive loco = GetComponentInParent<Locomotive>();

            if (loco != null)
                boiler = loco.GetComponentInChildren<Boiler>();

            if (boiler == null)
                return;
        }

        if (boiler.WaterLevel01 >= feedBelowFraction)
            return;

        float wanted = Mathf.Min(feedPerSecond * Time.deltaTime, water);
        water -= boiler.AddWater(wanted);
    }

    // ---------- IPortOwner (Einfuellstutzen) ----------

    public float GetAvailable(Port port) { return 0f; }

    public float GetFreeSpace(Port port)
    {
        return port.Medium == PortMedium.Water ? Mathf.Max(0f, waterCapacity - water) : 0f;
    }

    public float TakeFrom(Port port, float amountWanted) { return 0f; }

    public float GiveTo(Port port, float amountGiven)
    {
        return port.Medium == PortMedium.Water ? AddWater(amountGiven) : 0f;
    }

    // Entnimmt bis zu "wanted" (Liter / kWh). Gibt zurueck, wie viel wirklich da war.
    public float Take(float wanted)
    {
        if (fuelType == FuelType.Coal)
            return 0f;

        float taken = Mathf.Clamp(wanted, 0f, amount);
        amount -= taken;
        return taken;
    }

    // Fuellt nach (Liter / kWh). Gibt zurueck, wie viel hineinpasste.
    public float Add(float value)
    {
        if (fuelType == FuelType.Coal)
            return 0f;

        float accepted = Mathf.Clamp(value, 0f, capacity - amount);
        amount += accepted;
        return accepted;
    }

    public string StatusText()
    {
        string text = FuelStatusText();

        if (waterCapacity > 0f)
            text += "\n" + Loc.T("train.tender.water", Mathf.RoundToInt(water), Mathf.RoundToInt(waterCapacity));

        return text;
    }

    private string FuelStatusText()
    {
        int percent = Mathf.RoundToInt(Fraction * 100f);

        switch (fuelType)
        {
            case FuelType.Diesel:
                return Loc.T("train.tender.diesel", amount.ToString("0.0", CultureInfo.InvariantCulture), percent);

            case FuelType.Electric:
                return Loc.T("train.tender.electric", amount.ToString("0.0", CultureInfo.InvariantCulture), percent);

            default:
                return Loc.T("train.tender.coal", Mathf.RoundToInt(Amount), Mathf.RoundToInt(Capacity));
        }
    }

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("amount", amount);
        bag.SetFloat("water", water);
    }

    public void LoadState(StateBag bag)
    {
        amount = Mathf.Clamp(bag.GetFloat("amount", amount), 0f, capacity);

        float savedWater = bag.GetFloat("water", -1f);

        if (savedWater >= 0f)
        {
            water = Mathf.Clamp(savedWater, 0f, waterCapacity);
            waterRestored = true;
        }
    }
}
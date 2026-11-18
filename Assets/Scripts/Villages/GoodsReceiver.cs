using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  GoodsReceiver: die WARENANNAHME. Ein eigenes Gebaeude, in das man Waren legt (oder per Foerderband
//  schickt). Die Annahme verkauft sie an den naechsten Markt und zahlt dir das Geld aus.
//
//  Bisher gibt es als Markt nur das naechste DORF (Preise, Lager und Ansehen kommen aus dem VillageSystem:
//  Je leerer das Lager des Dorfes, desto mehr zahlt es; es nimmt nur Waren, die es braucht).
//  Spaeter koennen NPC-Fabriken ebenfalls ein "IGoodsMarket" sein (siehe unten), dann waehlt die Annahme
//  den besten Markt in Reichweite.
//
//  EINRICHTEN (wie die anderen Maschinen, ganz ohne Editor-Skript):
//   1. Leeres GameObject, dieses Skript draufziehen, als Prefab speichern.
//   2. Prefab beim BuildMenuController > Machine Prefabs eintragen (Kategorie siehe "Build Category" am Objekt,
//      sonst kommt es unter "other").
//   3. Fertig! Ohne eigenes Modell baut das Skript beim Start einen einfachen Platzhalter (kleine Halle mit
//      Rampe und Schild). Eigenes Modell: bei "Model Prefab" eintragen, dann wird der Platzhalter nicht gebaut.
//   4. Optional "Zone": eine eigene Trigger-Box fuer den Annahmebereich. Leer = das Skript legt selbst eine an
//      (Groesse und Lage siehe "Zone Size / Zone Center").
//
//  Was angenommen wird: Kohle, Erz, Barren (jeweils nach Metall). Schlacke, Werkzeuge und Kanister nicht.
//  Der Warenname fuer das Dorf entsteht so: Kohle = "Coal", Erz = Metall + "Ore" (Eisen = "IronOre"),
//  Barren = Metall + "Ingots" (Eisen = "IronIngots"). Diese Namen stehen im VillageSystem > Goods.
//
//  Mehrspieler: Das Stueck verkauft der Rechner, dem es gehoert; das Geld geht auf dessen Konto.
// =====================================================================

// Ein Abnehmer fuer Waren (Dorf, spaeter NPC-Fabrik ...). Preise duerfen sich jederzeit aendern.
public interface IGoodsMarket
{
    // Name zur Anzeige.
    string MarketName { get; }

    // Nimmt dieser Markt die Ware gerade an?
    bool Wants(string good);

    // Was zahlt er jetzt pro Stueck?
    float PayoutPerUnit(string good);

    // Ware abliefern. Gibt das gezahlte Geld zurueck (-1 = abgelehnt). Der Markt schreibt das Geld selbst gut.
    float Deliver(string good, float amount);
}

// Hilfen rund um Warennamen.
public static class GoodsNames
{
    // Warenname eines Gegenstands ("" = keine handelbare Ware).
    public static string Of(Component item)
    {
        if (item == null)
            return "";

        // Dieselkanister und Werkzeuge sind keine Ware.
        if (item is FuelCanister || item is DigTool)
            return "";

        ItemTag tag = item.GetComponentInParent<ItemTag>();
        ItemKind kind = tag != null ? tag.kind : ItemKind.Coal;
        string metal = tag != null ? tag.metal : "";

        if (string.IsNullOrEmpty(metal))
            metal = "Iron";

        switch (kind)
        {
            case ItemKind.Coal: return "Coal";
            case ItemKind.Ore: return metal + "Ore";
            case ItemKind.Ingot: return metal + "Ingots";
            default: return "";
        }
    }

    // Lesbarer Name: Textschluessel "good.<Name>", sonst der Name selbst.
    public static string Display(string good)
    {
        string key = "good." + good;
        return Loc.Has(key) ? Loc.T(key) : good;
    }
}

public class GoodsReceiver : MonoBehaviour
{
    [Header("Markt")]
    [Tooltip("Wie weit darf das naechste Dorf entfernt sein (Meter, ab Dorfrand)? Weiter weg gibt es keinen Abnehmer.")]
    [SerializeField] private float maxVillageDistance = 120f;

    [Header("Annahmebereich")]
    [Tooltip("Optional: eigene Trigger-Box. Leer = wird automatisch angelegt.")]
    [SerializeField] private BoxCollider zone;
    [SerializeField] private Vector3 zoneSize = new Vector3(2.4f, 1.2f, 2.4f);
    [SerializeField] private Vector3 zoneCenter = new Vector3(0f, 0.9f, 0f);

    [Tooltip("Wie oft (Sekunden) abgelehnte Stuecke im Bereich erneut angeboten werden (das Dorf braucht vielleicht inzwischen mehr).")]
    [SerializeField] private float retrySeconds = 2f;

    [Header("Aussehen")]
    [Tooltip("Optional: eigenes Modell. Gesetzt = der Platzhalter wird nicht gebaut.")]
    [SerializeField] private GameObject modelPrefab;
    [SerializeField] private Vector3 placeholderSize = new Vector3(4f, 3f, 4f);

    [Header("Debug-Anzeige")]
    [SerializeField] private bool showInfo = true;
    [SerializeField] private float infoDistance = 10f;
    [SerializeField] private float infoHeight = 3.6f;

    private readonly List<GameObject> placeholderParts = new List<GameObject>();
    private readonly List<Material> materials = new List<Material>();
    private float retryTimer;

    // Letzter Verkauf (fuer die Anzeige).
    private string lastGood = "";
    private float lastPayout;
    private float lastTime = -100f;
    private float totalEarned;

    // Markt-Suche nur gelegentlich.
    private IGoodsMarket market;
    private float marketTimer;

    private void Awake()
    {
        // Eigene Optik: entweder steckt das Modell schon als Kind im Prefab (dann wird nichts gebaut), oder es
        // steht im Feld "Model Prefab", sonst entsteht der Platzhalter.
        bool ownVisuals = GetComponentInChildren<Renderer>(true) != null;

        if (ownVisuals)
        {
            // nichts zu tun
        }
        else if (modelPrefab != null && modelPrefab.GetComponentInChildren<GoodsReceiver>(true) == null)
        {
            GameObject model = Instantiate(modelPrefab, transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
        }
        else
        {
            if (modelPrefab != null)
                Debug.LogWarning("GoodsReceiver: 'Model Prefab' darf nicht das Annahme-Prefab selbst sein (es enthaelt das Skript). Platzhalter wird gebaut.", this);

            BuildPlaceholder();
        }

        if (zone == null)
        {
            GameObject z = new GameObject("Annahmebereich");
            z.transform.SetParent(transform, false);
            z.transform.localPosition = zoneCenter;
            zone = z.AddComponent<BoxCollider>();
            zone.size = zoneSize;
        }

        zone.isTrigger = true;

        // Ein Trigger meldet nur, wenn er den Weg zu uns kennt: Weiterleiter an die Box haengen,
        // falls sie nicht auf diesem Objekt sitzt.
        if (zone.gameObject != gameObject && zone.GetComponent<ReceiverRelay>() == null)
            zone.gameObject.AddComponent<ReceiverRelay>().owner = this;
    }

    private void OnDestroy()
    {
        foreach (Material m in materials)
        {
            if (m != null)
                Destroy(m);
        }
    }

    // ---------- Platzhalter ----------

    private void BuildPlaceholder()
    {
        Material concrete = Mat(new Color(0.55f, 0.55f, 0.57f));
        Material wood = Mat(new Color(0.45f, 0.32f, 0.22f));
        Material roof = Mat(new Color(0.5f, 0.2f, 0.18f));
        Material sign = Mat(new Color(0.9f, 0.75f, 0.2f));

        Vector3 s = placeholderSize;
        float w = 0.2f;

        // Boden, Rueckwand und zwei Seitenwaende; die Vorderseite (+Z) bleibt offen zum Einwerfen.
        Part("Boden", new Vector3(0f, 0.1f, 0f), new Vector3(s.x, 0.2f, s.z), concrete, true);
        Part("Rueckwand", new Vector3(0f, s.y * 0.5f, -s.z * 0.5f + w * 0.5f), new Vector3(s.x, s.y, w), wood, true);
        Part("WandLinks", new Vector3(-s.x * 0.5f + w * 0.5f, s.y * 0.5f, 0f), new Vector3(w, s.y, s.z), wood, true);
        Part("WandRechts", new Vector3(s.x * 0.5f - w * 0.5f, s.y * 0.5f, 0f), new Vector3(w, s.y, s.z), wood, true);
        Part("Dach", new Vector3(0f, s.y + 0.1f, 0f), new Vector3(s.x + 0.4f, 0.2f, s.z + 0.4f), roof, false);
        Part("Schild", new Vector3(0f, s.y - 0.4f, s.z * 0.5f + 0.25f), new Vector3(1.6f, 0.5f, 0.1f), sign, false);
        Part("Rampe", new Vector3(0f, 0.15f, s.z * 0.5f + 0.9f), new Vector3(s.x * 0.6f, 0.3f, 1.8f), concrete, true);
    }

    private Material Mat(Color color)
    {
        Material m = TrackManager.MakeMaterial(color, 0.1f);
        materials.Add(m);
        return m;
    }

    private void Part(string name, Vector3 pos, Vector3 scale, Material m, bool collider)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(transform, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;

        if (!collider)
            Destroy(g.GetComponent<Collider>());

        placeholderParts.Add(g);
    }

    // ---------- Markt ----------

    // Der Markt in Reichweite (aktuell das naechste Dorf, das noch in Reichweite liegt).
    private IGoodsMarket CurrentMarket()
    {
        if (Time.time < marketTimer)
            return market;

        marketTimer = Time.time + 2f;
        market = null;

        VillageSystem vs = VillageSystem.Instance;

        if (vs == null)
            return null;

        List<VillagePlan> near = vs.FindVillagesAround(transform.position, maxVillageDistance + 80f);

        foreach (VillagePlan plan in near)
        {
            Vector3 d = plan.center - transform.position;
            d.y = 0f;

            if (d.magnitude <= plan.radius + maxVillageDistance)
            {
                market = new VillageMarket(vs, plan);
                break;
            }
        }

        return market;
    }

    private class VillageMarket : IGoodsMarket
    {
        private readonly VillageSystem system;
        private readonly VillagePlan plan;

        public VillageMarket(VillageSystem system, VillagePlan plan)
        {
            this.system = system;
            this.plan = plan;
        }

        public string MarketName { get { return plan.name; } }
        public bool Wants(string good) { return system.Needs(plan, good); }
        public float PayoutPerUnit(string good) { return system.PayoutPerUnit(plan, good); }
        public float Deliver(string good, float amount) { return system.Deliver(plan, good, amount); }
    }

    // ---------- Annehmen ----------

    // Vom Trigger aufgerufen (auch vom Weiterleiter).
    internal void Offer(Collider other)
    {
        CollectibleCoal item = other.GetComponentInParent<CollectibleCoal>();

        if (item == null)
            return;

        // Was in der Hand liegt, wird nicht verkauft.
        if (CoalPickup.LocalHeldItem == item)
            return;

        string good = GoodsNames.Of(item);

        if (good.Length == 0)
            return;

        IGoodsMarket m = CurrentMarket();

        if (m == null || !m.Wants(good))
            return;

        // Im Mehrspieler verkauft nur der Besitzer des Stuecks.
        if (!NetItems.CanAct(item.gameObject))
            return;

        float payout = m.Deliver(good, 1f);

        if (payout < 0f)
            return;

        NetItems.Consume(item.gameObject);

        lastGood = good;
        lastPayout = payout;
        lastTime = Time.time;
        totalEarned += payout;
    }

    private void OnTriggerEnter(Collider other)
    {
        Offer(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (Time.time < retryTimer)
            return;

        Offer(other);
    }

    private void LateUpdate()
    {
        if (Time.time >= retryTimer)
            retryTimer = Time.time + Mathf.Max(0.2f, retrySeconds);
    }

    // Weiterleiter: Sitzt die Zonen-Box auf einem Kind, meldet sie ihre Treffer an die Annahme.
    private class ReceiverRelay : MonoBehaviour
    {
        public GoodsReceiver owner;

        private void OnTriggerEnter(Collider other)
        {
            if (owner != null)
                owner.Offer(other);
        }

        private void OnTriggerStay(Collider other)
        {
            if (owner != null && Time.time >= owner.retryTimer)
                owner.Offer(other);
        }
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

        IGoodsMarket m = CurrentMarket();
        string text;

        if (m == null)
        {
            text = Loc.T("receiver.nomarket");
        }
        else
        {
            text = Loc.T("receiver.market", m.MarketName);

            // Preise der wichtigsten Waren, die der Markt gerade will.
            foreach (string good in new[] { "Coal", "IronOre", "IronIngots" })
            {
                if (m.Wants(good))
                    text += "\n" + Loc.T("receiver.price", GoodsNames.Display(good), m.PayoutPerUnit(good).ToString("0.0"));
            }
        }

        if (Time.time - lastTime < 6f)
            text += "\n" + Loc.T("receiver.sold", GoodsNames.Display(lastGood), lastPayout.ToString("0.0"));

        text += "\n" + Loc.T("receiver.total", totalEarned.ToString("0.0"));

        GameSettings.DrawBoxAbove(screen, text);
    }
}
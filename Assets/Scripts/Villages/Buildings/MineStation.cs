using UnityEngine;

// =====================================================================
//  MineStation: die VERLADESTATION einer Dorfmine (Tagebau).
//
//  Das Dorf foerdert Kohle bzw. Erz aus einem Vorkommen in der Naehe. Die Station steht am Rand der Grube
//  und wird vom VillageSystem automatisch dort gebaut (nicht von Hand platziert). Wenn du auf das Pult
//  zielst, kaufst du dem Dorf Ware aus dem Lager ab; sie rutscht als echte Stuecke aus der Rutsche.
//
//  Eigenes Modell: Im VillageSystem bei "Mine Station Prefab" eintragen. Das Prefab braucht kein MineStation-
//  Skript (wird ergaenzt). Optionale Kinder, die das Skript findet:
//    - "Pult"     : Objekt mit Collider, auf das man zielt (fehlt = es wird eine Box angelegt)
//    - "Rutsche"  : Punkt, an dem die gekauften Stuecke erscheinen
//  Ohne Prefab baut das Skript einen einfachen Platzhalter (Baracke, Pult, Rutsche, Haufen).
//
//  Aus dem Vorkommen geforderte Ware = Menge im Dorflager (Waren-Preis steigt, wenn es leer wird).
// =====================================================================
public class MineStation : MonoBehaviour, IInteractable
{
    [Header("Kaufen")]
    [Tooltip("So viele Stuecke bekommst du pro Druck auf das Pult.")]
    [SerializeField] private int piecesPerPress = 5;

    [Tooltip("Wie weit (Meter) die Stuecke an der Rutsche gestreut werden.")]
    [SerializeField] private float dropSpread = 0.25f;

    [Header("Anzeige")]
    [SerializeField] private bool showInfo = true;
    [SerializeField] private float infoDistance = 16f;
    [SerializeField] private float infoHeight = 4.5f;

    private VillagePlan plan;
    private string good = "Coal";
    private Transform chute;
    private BoxCollider terminal;
    private string message = "";
    private float messageUntil;

    private readonly System.Collections.Generic.List<Material> materials = new System.Collections.Generic.List<Material>();

    // Vom VillageSystem aufgerufen, gleich nach dem Erzeugen. buildPlaceholder = kein eigenes Prefab.
    public void Setup(VillagePlan plan, string good, bool buildPlaceholder, Vector2 footprint)
    {
        this.plan = plan;
        this.good = good;

        if (buildPlaceholder)
            BuildPlaceholder(footprint);

        // Pult und Rutsche aus dem Prefab (oder dem Platzhalter) finden, sonst anlegen.
        Transform pult = transform.Find("Pult");

        if (pult == null)
        {
            GameObject p = new GameObject("Pult");
            p.transform.SetParent(transform, false);
            p.transform.localPosition = new Vector3(-footprint.x * 0.25f, 0.6f, footprint.y * 0.5f - 0.6f);
            pult = p.transform;
        }

        terminal = pult.GetComponent<BoxCollider>();

        if (terminal == null)
        {
            terminal = pult.gameObject.AddComponent<BoxCollider>();
            terminal.size = new Vector3(1.4f, 1.2f, 0.8f);
        }

        chute = transform.Find("Rutsche");

        if (chute == null)
        {
            GameObject c = new GameObject("Rutsche");
            c.transform.SetParent(transform, false);
            c.transform.localPosition = new Vector3(footprint.x * 0.25f, 0.9f, footprint.y * 0.5f + 0.8f);
            chute = c.transform;
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

    // ---------- Platzhalter ----------

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

    private void BuildPlaceholder(Vector2 fp)
    {
        Material hut = Mat(new Color(0.55f, 0.42f, 0.30f));
        Material roof = Mat(new Color(0.28f, 0.28f, 0.30f));
        Material steel = Mat(new Color(0.45f, 0.48f, 0.52f));
        Material pile = Mat(good == "Coal" ? new Color(0.08f, 0.08f, 0.09f) : new Color(0.45f, 0.30f, 0.22f));
        Material desk = Mat(new Color(0.20f, 0.30f, 0.50f));

        float w = fp.x;
        float d = fp.y;

        // Baracke an der Rueckseite (die Vorderseite +Z zeigt zur Grube).
        Part("Baracke", PrimitiveType.Cube, new Vector3(0f, 1.5f, -d * 0.25f), new Vector3(w * 0.7f, 3f, d * 0.4f), hut, true);
        Part("Dach", PrimitiveType.Cube, new Vector3(0f, 3.15f, -d * 0.25f), new Vector3(w * 0.7f + 0.6f, 0.3f, d * 0.4f + 0.6f), roof, false);

        // Fuellrumpf und Rutsche (schraeger Block).
        Part("Rumpf", PrimitiveType.Cube, new Vector3(w * 0.25f, 2.2f, d * 0.05f), new Vector3(2f, 2.4f, 2f), steel, true);
        GameObject ramp = Part("Rutschenblech", PrimitiveType.Cube, new Vector3(w * 0.25f, 1.3f, d * 0.32f), new Vector3(1.4f, 0.15f, 2.4f), steel, false);
        ramp.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);

        // Haufen der Ware vor dem Rumpf (nur Optik).
        Part("Haufen", PrimitiveType.Sphere, new Vector3(-w * 0.28f, 0.5f, d * 0.05f), new Vector3(2.6f, 1.3f, 2.2f), pile, false);

        // Pult
        Part("Pult", PrimitiveType.Cube, new Vector3(-w * 0.25f, 0.6f, d * 0.5f - 0.6f), new Vector3(1.4f, 1.2f, 0.8f), desk, true);

        // Rutschenausgang als Marke
        GameObject c = new GameObject("Rutsche");
        c.transform.SetParent(transform, false);
        c.transform.localPosition = new Vector3(w * 0.25f, 0.9f, d * 0.5f + 0.8f);
    }

    // ---------- Pult ----------

    private bool Ready
    {
        get { return plan != null && VillageSystem.Instance != null; }
    }

    public string GetPrompt()
    {
        if (!Ready)
            return "";

        float price = VillageSystem.Instance.PricePerUnit(plan, good) * piecesPerPress;

        return Loc.T("mine.prompt", piecesPerPress, GoodsNames.Display(good), price.ToString("0.0"));
    }

    public void Interact()
    {
        if (!Ready)
            return;

        VillageSystem vs = VillageSystem.Instance;

        if (VoxelDigger.Local == null)
        {
            Say(Loc.T("mine.nodigger"));
            return;
        }

        float stock = vs.Stock(plan, good);

        if (stock < piecesPerPress)
        {
            Say(Loc.T("mine.nostock"));
            return;
        }

        if (!vs.Buy(plan, good, piecesPerPress))
        {
            Say(Loc.T("mine.nomoney"));
            return;
        }

        ItemKind kind = good == "Coal" ? ItemKind.Coal : ItemKind.Ore;

        if (!VoxelDigger.Local.DropPieces(kind, piecesPerPress, chute.position, dropSpread))
        {
            Say(Loc.T("mine.noprefab"));
            return;
        }

        Say(Loc.T("mine.bought", piecesPerPress, GoodsNames.Display(good)));
    }

    private void Say(string text)
    {
        message = text;
        messageUntil = Time.time + 3f;
    }

    // ---------- Anzeige ----------

    private void OnGUI()
    {
        if (!showInfo || !Ready)
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

        VillageSystem vs = VillageSystem.Instance;

        float remaining, total, depth;
        string text = Loc.T("mine.title", plan.name, GoodsNames.Display(good));

        text += "\n" + Loc.T("mine.stock", Mathf.RoundToInt(vs.Stock(plan, good)), Mathf.RoundToInt(vs.stockCapacity));

        if (vs.DescribeMine(plan, good, out remaining, out total, out depth))
            text += "\n" + Loc.T("mine.deposit", Mathf.RoundToInt(remaining), Mathf.RoundToInt(total), depth.ToString("0.0"));
        else
            text += "\n" + Loc.T("mine.exhausted");

        if (Time.time < messageUntil)
            text += "\n" + message;

        GameSettings.DrawBoxAbove(screen, text);
    }
}
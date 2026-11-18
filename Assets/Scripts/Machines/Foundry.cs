using UnityEngine;

// Giesserei: Schmilzt und giesst jedes Metall, das ankommt.
//
// So funktioniert sie:
//  1. Der Tiegel (der Zylinder) nimmt Metall auf. Es kommt auf zwei Wegen an:
//     - fluessig ueber den Schmelze-Anschluss oben (Port, Medium "Melt"), z. B. vom
//       Hochofen. Das Rohr ist geschlossen, es geht nichts verloren.
//     - fest ueber den Einwurf oben (FoundryInlet): Barren/Schrott werden eingeschmolzen.
//     Welches Metall es ist, steht im Gegenstand bzw. in der Schmelze. Es gibt keine eigene
//     Giesserei pro Metall: Alles, was in der MetalRegistry (Inspector) steht, wird genommen. Ein anderes
//     Metall wird erst angenommen, wenn der Tiegel leer ist (oder Metals.TryMix eine
//     Legierung daraus macht).
//  2. Strom: Eine Induktionsheizung haelt die Schmelze fluessig. Sie ist ein normaler
//     Stromverbraucher. Ohne Strom kuehlt die Schmelze ab und erstarrt irgendwann; mit
//     Strom wird sie wieder fluessig. Eingeworfenes festes Metall kuehlt die Schmelze erst
//     einmal ab (die Heizung muss es erwaermen).
//  3. Giessen: Ist die Schmelze fluessig (und der Giess-Schalter an), laeuft sie ueber
//     das geschlossene Rohr in die Formen auf dem Gusstisch, eine nach der anderen.
//  4. Abkuehlen: Eine volle Form braucht einige Sekunden, bis die Platte fest ist.
//  5. Entnehmen: Danach liegt die fertige Platte (Barren) in der Form und kann einfach
//     herausgehoben werden. Erst wenn sie weg ist, wird die Form wieder befuellt.
//
// Einrichtung im Prefab (Wurzel mit diesem Skript):
//  - Kind-Port "Melt / Input" oben am Zylinder und Kind-Port "Power".
//  - Kind "Einwurf": Collider (Is Trigger) + FoundryInlet, z. B. am Deckel.
//  - Fuer jede Form ein leeres Kind-Objekt auf dem Boden der Form, in der Mitte (die gruene
//    Y-Achse zeigt nach oben, die Platte liegt in dessen Ausrichtung). Alle in "Molds"
//    eintragen. Dort erscheint auch die Fuellstandsanzeige: ein gluehender Block, der mit
//    dem Fuellstand waechst und beim Abkuehlen grau wird.
//  - Optional: ein MachineSwitch (Giess-Schalter) unter der Giesserei. Ohne Schalter
//    giesst sie immer, sobald fluessiges Metall da ist.
//  - Prefabs der Platten: fuer jedes Metall ein Eintrag in "Ingot Prefabs" (oder nur
//    "Default Ingot Prefab", dann sehen alle Metalle gleich aus). Das Prefab braucht
//    CollectibleCoal (zum Aufheben), Rigidbody und Collider wie der CoalChunk.
public class Foundry : MonoBehaviour, IMeltOwner, IPowerConsumer, ISaveable
{
    [System.Serializable]
    public class MetalPrefab
    {
        [Tooltip("Metall-ID wie in der MetalRegistry (Inspector), z. B. Iron.")]
        public string metal = "Iron";
        public GameObject prefab;
    }

    [Header("Strom (Induktionsheizung)")]
    [SerializeField] private float ratedPowerKW = 20f;
    [Tooltip("Verbrauch bei leerem Tiegel.")]
    [SerializeField] private float idlePowerKW = 0.3f;
    [Tooltip("Hoeher = wichtiger. Unwichtige Verbraucher fallen zuerst aus.")]
    [SerializeField] private int importance = 12;

    [Header("Tiegel")]
    [SerializeField] private float crucibleCapacityKg = 150f;
    [SerializeField] private float ambientC = 20f;
    [Tooltip("Die Heizung haelt die Schmelze so viele Grad ueber ihrer Schmelztemperatur.")]
    [SerializeField] private float holdMarginC = 100f;
    [Tooltip("Aufheizung in Grad pro Sekunde bei Vollleistung und 'Heat Mass Reference Kg' Inhalt.")]
    [SerializeField] private float heatCPerSecond = 12f;
    [Tooltip("Bei dieser Fuellmenge schafft die Heizung die oben genannte Aufheizung. " +
             "Mehr Inhalt = langsamer, weniger = schneller.")]
    [SerializeField] private float heatMassReferenceKg = 100f;
    [Tooltip("Waermeverlust an die Umgebung (Anteil des Temperaturunterschieds pro Sekunde).")]
    [SerializeField] private float lossPerSecond = 0.002f;

    [Header("Giessen")]
    [Tooltip("Giess-Schalter. Leer = der erste MachineSwitch unter der Giesserei, " +
             "und gibt es keinen, wird immer gegossen.")]
    [SerializeField] private MachineSwitch pourSwitch;
    [Tooltip("Metallmenge, die in eine Form passt (= Gewicht einer Platte).")]
    [SerializeField] private float ingotMassKg = 10f;
    [SerializeField] private float pourKgPerSecond = 4f;
    [Tooltip("So lange braucht eine volle Form, bis die Platte fest ist.")]
    [SerializeField] private float coolSeconds = 25f;
    [Tooltip("Mitte jeder Form auf dem Gusstisch. Leer = alles landet an 'Output Point'.")]
    [SerializeField] private Transform[] molds;
    [Tooltip("Ersatz, wenn keine Formen eingetragen sind (z. B. die Schublade vorne).")]
    [SerializeField] private Transform outputPoint;

    [Header("Platten (Prefabs)")]
    [SerializeField] private MetalPrefab[] ingotPrefabs;
    [Tooltip("Wird benutzt, wenn fuer ein Metall kein eigenes Prefab eingetragen ist.")]
    [SerializeField] private GameObject defaultIngotPrefab;

    [Header("Anzeige der Formen")]
    [Tooltip("Zeigt in jeder Form, wie voll sie ist: ein Block aus fluessigem Metall, " +
             "der waechst und beim Abkuehlen grau wird.")]
    [SerializeField] private bool showFill = true;
    [Tooltip("Innenmasse der Form in Metern (Breite, Hoehe, Tiefe). Die Hoehe ist der Stand " +
             "bei voller Form.")]
    [SerializeField] private Vector3 moldSize = new Vector3(0.3f, 0.04f, 0.15f);
    [Tooltip("Verschiebung der Anzeige gegenueber dem Form-Punkt (in dessen Richtungen).")]
    [SerializeField] private Vector3 fillOffset = Vector3.zero;
    [Tooltip("Farbe des heissen Metalls beim Giessen.")]
    [SerializeField] private Color moltenColor = new Color(1f, 0.5f, 0.1f);

    [Header("Anzeige")]
    [Tooltip("Optional: Teil des Modells (z. B. der Tiegel), das mit der Hitze gluehend wird.")]
    [SerializeField] private Renderer glowRenderer;
    [SerializeField] private Color coldColor = new Color(0.4f, 0.4f, 0.42f);
    [SerializeField] private Color hotColor = new Color(1f, 0.45f, 0.1f);
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 2.5f;

    // ----- Zustand -----
    private string metal = "";          // Metall im Tiegel (ID aus der MetalRegistry)
    private float kg;                   // Inhalt des Tiegels
    private float temp;                 // Temperatur der Schmelze

    private float[] moldFill;           // Metall in der Form (kg)
    private float[] moldTimer;          // Rest-Abkuehlzeit, kleiner 0 = nicht am Abkuehlen
    private string[] moldMetal;
    private Transform[] moldItem;       // fertige Platte, die noch in der Form liegt
    private float[] moldSearchUntil;    // Mitspieler: bis dahin wird auf die Platte vom Host gewartet
    private Transform[] fillObjects;    // Fuellstandsanzeige pro Form
    private Renderer[] fillRenderers;
    private MaterialPropertyBlock fillBlock;

    private float supply = 1f;
    private bool pouring;
    private float dryTimer;
    private bool stateLoadedOnce;
    private float glowTimer;
    private MaterialPropertyBlock glowBlock;

    // ----- Werte fuer Anzeige und andere Skripte -----
    public float ContentKg => kg;
    public float TemperatureC => temp;
    public string Metal => metal;
    public float DefaultSolidMassKg => ingotMassKg;

    private bool PourOpen => pourSwitch == null || pourSwitch.IsOn;
    private int MoldCount => moldFill != null ? moldFill.Length : 0;

    private float HoldTemp
    {
        get
        {
            MetalDef def = Metals.Get(metal);
            return (def != null ? def.meltTempC : 1200f) + holdMarginC;
        }
    }

    private bool HasContent => kg > 0.001f;

    private bool IsLiquid
    {
        get
        {
            MetalDef def = Metals.Get(metal);
            return HasContent && def != null && temp >= def.meltTempC;
        }
    }

    // Beim Aufheizen volle Leistung, beim Halten nur ein Teil, leer fast nichts.
    public float DemandKW =>
        !HasContent ? idlePowerKW
        : temp < HoldTemp ? ratedPowerKW
        : ratedPowerKW * 0.25f;

    public float RatedPowerKW => ratedPowerKW;
    public int Importance => importance;

    public void SetSupply(float fraction)
    {
        supply = Mathf.Clamp01(fraction);
    }

    private void Awake()
    {
        temp = ambientC;

        // Formen vorbereiten. Ohne eingetragene Formen gibt es eine einzige "Form"
        // am Ausgabepunkt.
        int count = (molds != null && molds.Length > 0) ? molds.Length : 1;

        moldFill = new float[count];
        moldTimer = new float[count];
        moldMetal = new string[count];
        moldItem = new Transform[count];
        moldSearchUntil = new float[count];

        for (int i = 0; i < count; i++)
        {
            moldTimer[i] = -1f;
            moldMetal[i] = "";
        }

        if (pourSwitch == null)
            pourSwitch = GetComponentInChildren<MachineSwitch>(true);

        if (showFill)
            CreateFillVisuals();
    }

    // Erzeugt pro Form einen einfachen Block (ohne Collider) als Fuellstandsanzeige.
    private void CreateFillVisuals()
    {
        fillObjects = new Transform[MoldCount];
        fillRenderers = new Renderer[MoldCount];
        fillBlock = new MaterialPropertyBlock();

        for (int i = 0; i < MoldCount; i++)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "MoldFill" + i;

            Collider col = block.GetComponent<Collider>();

            if (col != null)
                Destroy(col);

            block.transform.SetParent(MoldPoint(i), false);

            Renderer rend = block.GetComponent<Renderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            block.SetActive(false);

            fillObjects[i] = block.transform;
            fillRenderers[i] = rend;
        }
    }

    // Passt die Anzeige jeder Form an: Hoehe = Fuellstand, Farbe = Temperatur.
    private void UpdateFillVisuals()
    {
        if (fillObjects == null)
            return;

        for (int i = 0; i < fillObjects.Length; i++)
        {
            Transform block = fillObjects[i];

            if (block == null)
                continue;

            float fill = Mathf.Clamp01(moldFill[i] / Mathf.Max(0.001f, ingotMassKg));
            bool visible = fill > 0.001f;

            if (block.gameObject.activeSelf != visible)
                block.gameObject.SetActive(visible);

            if (!visible)
                continue;

            // Groesse in Metern -> lokale Skalierung (der Form-Punkt kann skaliert sein).
            Vector3 parentScale = block.parent != null ? block.parent.lossyScale : Vector3.one;
            float sx = Mathf.Max(0.0001f, Mathf.Abs(parentScale.x));
            float sy = Mathf.Max(0.0001f, Mathf.Abs(parentScale.y));
            float sz = Mathf.Max(0.0001f, Mathf.Abs(parentScale.z));

            float height = Mathf.Max(0.002f, moldSize.y * fill);

            block.localScale = new Vector3(moldSize.x / sx, height / sy, moldSize.z / sz);
            block.localPosition = fillOffset + new Vector3(0f, height * 0.5f / sy, 0f);

            // Beim Fuellen heiss; waehrend des Abkuehlens wird es zur Farbe des Metalls.
            float cooled = moldTimer[i] >= 0f
                ? 1f - Mathf.Clamp01(moldTimer[i] / Mathf.Max(0.1f, coolSeconds))
                : 0f;

            MetalDef def = Metals.Get(moldMetal[i]);
            Color solid = def != null ? def.color : coldColor;

            fillRenderers[i].GetPropertyBlock(fillBlock);
            fillBlock.SetColor("_Color", Color.Lerp(moltenColor, solid, cooled));
            fillRenderers[i].SetPropertyBlock(fillBlock);
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

    private Transform MoldPoint(int i)
    {
        if (molds != null && i >= 0 && i < molds.Length && molds[i] != null)
            return molds[i];

        return outputPoint != null ? outputPoint : transform;
    }

    // ---------- Metall aufnehmen ----------

    // Mischt "add" kg mit der Temperatur "addTemp" in den Tiegel.
    private void Mix(string addMetal, float add, float addTemp)
    {
        string result;

        if (!HasContent)
            result = addMetal;
        else if (!Metals.TryMix(metal, addMetal, out result))
            return;

        // Die Tiegelwand speichert auch etwas Waerme: Sie zaehlt wie 5 kg Metall.
        float oldMass = Mathf.Max(kg, 5f);

        temp = (temp * oldMass + addTemp * add) / (oldMass + add);
        kg += add;
        metal = result;
    }

    private bool CanTake(string addMetal, float add)
    {
        if (string.IsNullOrEmpty(addMetal) || !Metals.IsKnown(addMetal))
            return false;

        if (kg + add > crucibleCapacityKg + 0.001f)
            return false;

        string result;
        return !HasContent || Metals.TryMix(metal, addMetal, out result);
    }

    // Festes Metall (z. B. ein Barren) passt noch hinein? Masse 0 = Standardgewicht.
    public bool CanAcceptSolid(string addMetal, float mass)
    {
        return CanTake(addMetal, mass > 0f ? mass : ingotMassKg);
    }

    // Wird ueber ein Kommando aufgerufen (NetSync.AddSolid), damit alle Spieler
    // dasselbe sehen. Das feste Metall ist kalt und kuehlt die Schmelze ab.
    public bool AddSolid(string addMetal, float mass)
    {
        float add = mass > 0f ? mass : ingotMassKg;

        if (!CanTake(addMetal, add))
            return false;

        Mix(addMetal, add, ambientC);
        return true;
    }

    // ---------- Simulation ----------

    private void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.25f);

        if (dt <= 0f)
            return;

        // 1) Heizung und Abkuehlung.
        float heat = 0f;

        if (HasContent && supply > 0.01f && temp < HoldTemp)
        {
            heat = heatCPerSecond * supply
                * heatMassReferenceKg / (heatMassReferenceKg + kg);
        }

        float loss = (temp - ambientC) * lossPerSecond;

        temp += (heat - loss) * dt;
        temp = Mathf.Max(temp, ambientC);

        // 2) Giessen in die Formen.
        pouring = false;

        if (IsLiquid && PourOpen)
            Pour(dt);

        // 3) Formen: Abkuehlen, Platte erzeugen, Platte entnommen?
        UpdateMolds(dt);

        // 4) Fuellstand der Formen sichtbar machen.
        UpdateFillVisuals();

        // 5) Glueh-Anzeige (nicht jedes Bild).
        glowTimer -= dt;

        if (glowTimer <= 0f)
        {
            glowTimer = 0.2f;
            UpdateGlow();
        }
    }

    // Die erste freie Form, die dieses Metall aufnehmen kann.
    private int FindFreeMold(string forMetal)
    {
        for (int i = 0; i < MoldCount; i++)
        {
            if (moldTimer[i] >= 0f || moldItem[i] != null || moldSearchUntil[i] > Time.time)
                continue;

            if (moldFill[i] >= ingotMassKg - 0.001f)
                continue;

            if (moldFill[i] > 0.001f && moldMetal[i] != forMetal)
                continue;

            return i;
        }

        return -1;
    }

    private void Pour(float dt)
    {
        int i = FindFreeMold(metal);

        if (i < 0)
            return;

        float amount = Mathf.Min(pourKgPerSecond * dt, kg);
        amount = Mathf.Min(amount, ingotMassKg - moldFill[i]);

        if (amount <= 0f)
            return;

        moldFill[i] += amount;
        moldMetal[i] = metal;
        kg -= amount;
        pouring = true;
        dryTimer = 0f;

        // Voll: Die Platte kuehlt ab.
        if (moldFill[i] >= ingotMassKg - 0.001f)
            moldTimer[i] = coolSeconds;

        if (kg <= 0.001f)
        {
            kg = 0f;
            metal = "";
        }
    }

    private void UpdateMolds(float dt)
    {
        // Ist der Tiegel leer, bevor eine Form voll wurde, wird die Platte eben kleiner.
        bool crucibleDry = !HasContent || !IsLiquid || !PourOpen;

        if (!pouring && crucibleDry)
            dryTimer += dt;

        for (int i = 0; i < MoldCount; i++)
        {
            // Teilweise gefuellte Form, es kommt nichts mehr nach: abkuehlen lassen.
            if (moldTimer[i] < 0f && moldFill[i] > 0.1f && !HasContent && dryTimer > 3f)
                moldTimer[i] = coolSeconds;

            // Abkuehlen und fertige Platte erzeugen.
            if (moldTimer[i] >= 0f)
            {
                moldTimer[i] -= dt;

                if (moldTimer[i] <= 0f)
                {
                    moldItem[i] = SpawnIngot(i);
                    moldTimer[i] = -1f;
                    moldFill[i] = 0f;
                }
            }

            // Mitspieler: Die Platte erzeugt der Host. Sobald sie da ist, in der Form suchen.
            if (moldItem[i] == null && moldSearchUntil[i] > Time.time)
            {
                moldItem[i] = NetItems.FindNear(MoldPoint(i).position, 0.35f, ItemKind.Ingot);

                if (moldItem[i] != null)
                    moldSearchUntil[i] = 0f;
            }

            // Platte wurde herausgehoben (oder ist verschwunden): Form wieder frei.
            if (moldItem[i] != null &&
                (moldItem[i].position - MoldPoint(i).position).sqrMagnitude > 0.16f)
                moldItem[i] = null;
        }
    }

    private GameObject FindIngotPrefab(string forMetal)
    {
        if (ingotPrefabs != null)
        {
            foreach (MetalPrefab entry in ingotPrefabs)
            {
                if (entry != null && entry.metal == forMetal && entry.prefab != null)
                    return entry.prefab;
            }
        }

        return defaultIngotPrefab;
    }

    // Erzeugt die fertige Platte in der Form. Gibt sie zurueck (null ohne Prefab).
    private Transform SpawnIngot(int i)
    {
        GameObject prefab = FindIngotPrefab(moldMetal[i]);

        if (prefab == null)
        {
            Debug.LogWarning("Giesserei: Kein Platten-Prefab fuer '" + moldMetal[i] +
                "' (und kein Default Ingot Prefab).", this);
            return null;
        }

        Transform point = MoldPoint(i);

        // Erzeugt wird nur beim Host (Markierung: Barren aus diesem Metall, kann wieder
        // eingeschmolzen werden). Mitspieler bekommen die Platte von ihm.
        GameObject item = NetItems.Spawn(
            prefab, point.position + Vector3.up * 0.03f, point.rotation,
            Vector3.zero, ItemKind.Ingot, moldMetal[i], moldFill[i], true);

        if (item != null)
            return item.transform;

        // Mitspieler: kurz auf die Platte vom Host warten (Form gilt solange als belegt).
        if (NetSync.IsClient)
            moldSearchUntil[i] = Time.time + 3f;

        return null;
    }

    private void UpdateGlow()
    {
        if (glowRenderer == null)
            return;

        if (glowBlock == null)
            glowBlock = new MaterialPropertyBlock();

        float heatFraction = Mathf.Clamp01((temp - ambientC) / 1500f);

        glowRenderer.GetPropertyBlock(glowBlock);
        glowBlock.SetColor("_Color", Color.Lerp(coldColor, hotColor, heatFraction));
        glowRenderer.SetPropertyBlock(glowBlock);
    }

    // ---------- IPortOwner / IMeltOwner (Schmelze-Eingang oben) ----------

    // Die Giesserei gibt keine Schmelze ab.
    public float GetAvailable(Port port)
    {
        return 0f;
    }

    public float GetFreeSpace(Port port)
    {
        if (port.Medium != PortMedium.Melt)
            return 0f;

        return Mathf.Max(0f, crucibleCapacityKg - kg);
    }

    public float TakeFrom(Port port, float amount)
    {
        return 0f;
    }

    // Die normale Mengen-Aufnahme gilt nicht: Schmelze braucht Metallart (GiveMelt).
    public float GiveTo(Port port, float amount)
    {
        return 0f;
    }

    public string GetMelt(Port port)
    {
        return "";
    }

    public float GetMeltTemp(Port port)
    {
        return temp;
    }

    public bool CanAcceptMelt(Port port, string addMetal)
    {
        return port.Medium == PortMedium.Melt && CanTake(addMetal, 0.01f);
    }

    public float GiveMelt(Port port, string addMetal, float amount, float tempC)
    {
        if (port.Medium != PortMedium.Melt || !CanTake(addMetal, 0.01f))
            return 0f;

        float accepted = Mathf.Min(amount, crucibleCapacityKg - kg);

        if (accepted <= 0f)
            return 0f;

        Mix(addMetal, accepted, tempC);
        return accepted;
    }

    // ---------- Anzeige ----------

    private int UsedMolds()
    {
        int used = 0;

        for (int i = 0; i < MoldCount; i++)
        {
            if (moldFill[i] > 0.001f || moldTimer[i] >= 0f || moldItem[i] != null)
                used++;
        }

        return used;
    }

    private string StatusKey()
    {
        if (HasContent && supply <= 0.01f && temp < HoldTemp)
            return "foundry.status.nopower";

        if (pouring)
            return "foundry.status.pouring";

        if (HasContent && !IsLiquid)
            return temp < ambientC + 100f ? "foundry.status.frozen" : "foundry.status.heating";

        if (UsedMolds() >= MoldCount && MoldCount > 0)
            return "foundry.status.moldsfull";

        for (int i = 0; i < MoldCount; i++)
        {
            if (moldTimer[i] >= 0f)
                return "foundry.status.cooling";
        }

        if (IsLiquid && !PourOpen)
            return "foundry.status.ready";

        return HasContent ? "foundry.status.ready" : "foundry.status.empty";
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

        Vector3 world = infoAnchor != null
            ? infoAnchor.position
            : transform.position + Vector3.up * debugHeight;

        if ((world - cam.transform.position).sqrMagnitude
            > debugDistance * debugDistance)
            return;

        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return;

        string text = Loc.T(
            "foundry.info",
            Loc.T(StatusKey()),
            HasContent ? Metals.Name(metal) : "-",
            kg.ToString("F0"), crucibleCapacityKg.ToString("F0"),
            temp.ToString("F0"),
            UsedMolds(), MoldCount);

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern (und Abgleich im Mehrspieler) ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("kg", kg);
        bag.SetFloat("temp", temp);
        bag.SetString("metal", metal);

        for (int i = 0; i < MoldCount; i++)
        {
            bag.SetFloat("fill" + i, moldFill[i]);
            bag.SetFloat("timer" + i, moldTimer[i]);
            bag.SetString("mmetal" + i, moldMetal[i] ?? "");
        }
    }

    public void LoadState(StateBag bag)
    {
        float newKg = Mathf.Clamp(bag.GetFloat("kg", kg), 0f, crucibleCapacityKg);

        temp = Mathf.Max(ambientC, bag.GetFloat("temp", temp));
        metal = bag.GetString("metal", metal);

        // Im Mehrspieler schickt der Host regelmaessig seinen Stand. Platten erzeugt jeder
        // Spieler bei sich selbst. Damit der verspaetete Stand nicht noch einmal
        // dasselbe Metall "auffuellt" (und so doppelte Platten entstehen), wird der
        // Tiegel dort nie hoeher gesetzt und die Formen bleiben unberuehrt.
        // Beim ersten Laden (Spielstand, Beitritt) gilt der Stand unveraendert.
        if (NetSync.Mode == NetMode.Client && stateLoadedOnce)
        {
            kg = Mathf.Min(kg, newKg);
            return;
        }

        kg = newKg;

        for (int i = 0; i < MoldCount; i++)
        {
            moldFill[i] = Mathf.Max(0f, bag.GetFloat("fill" + i, 0f));
            moldTimer[i] = bag.GetFloat("timer" + i, -1f);
            moldMetal[i] = bag.GetString("mmetal" + i, "");
            moldItem[i] = null;
        }

        stateLoadedOnce = true;
    }
}
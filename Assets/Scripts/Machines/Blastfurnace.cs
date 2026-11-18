using UnityEngine;

// Hochofen: Schmilzt Eisenerz zu Roheisen.
//
// So funktioniert er:
//  1. Beschickung: Kohle und Eisenerz werden oben in die Oeffnung geworfen
//     (FurnaceInlet). Beides wird im Ofen zwischengelagert.
//  2. Geblaese: Ein elektrisches Geblaese blaest Luft ein. Ohne Strom, ohne
//     Kohle oder bei ausgeschaltetem Geblaese brennt kein Feuer. Das Geblaese
//     ist ein normaler Stromverbraucher im Stromnetz.
//  3. Temperatur: Das Feuer heizt den Ofen langsam auf (einige Minuten).
//     Er kuehlt von selbst wieder ab. Geschmolzen wird erst ueber
//     "Min Smelt Temp C", und je heisser, desto schneller.
//  4. Kuehlwasser: Ein Wasseranschluss kuehlt die Waende. Ohne Wasser wird der
//     Ofen zu heiss und verschleisst. Bei 100 % Verschleiss ist er defekt.
//  5. Schmelze: Das Erz wird mit Kohle zu fluessigem Roheisen und Schlacke.
//     Beides sammelt sich unten im Herd. Ist der Herd voll, stoppt das Schmelzen.
//  6. Abstich: Mit dem Abstich-Schalter laeuft der Herd leer. Die Schlacke faellt als
//     lose Brocken am Abstich-Punkt heraus. Das fluessige Metall laeuft
//     - ueber einen angeschlossenen Schmelze-Anschluss (Medium "Melt") in eine
//       Giesserei (Foundry), oder
//     - ohne angeschlossenen Anschluss als lose Barren am Abstich-Punkt.
//     Zu kaltes Metall laesst sich nicht abstechen.
//
// Jedes Erz, das in der MetalRegistry (Inspector) eingetragen ist, wird geschmolzen (Schmelztemperatur,
// Ausbeute und Kohlebedarf stehen dort). Es wird immer nur ein Metall auf einmal
// geschmolzen; ein anderes Erz wird erst angenommen, wenn der Ofen leer ist.
//
// Einrichtung im Prefab (Wurzel mit diesem Skript):
//  - Kind "Beschickung": Collider (Is Trigger) + FurnaceInlet.
//  - Zwei Kind-Objekte mit MachineSwitch: Der ERSTE in der Hierarchy ist der
//    Geblaese-Schalter, der ZWEITE der Abstich-Schalter. Oder die Felder unten belegen.
//  - Kind-Port "Water / Input" (Kuehlwasser) und Kind-Port "Power".
//  - Kind "TapPoint": Leeres Objekt dort, wo Barren herauskommen (blaue Achse =
//    Richtung, in die sie rollen).
//  - Prefabs fuer Barren und Schlacke (Kopie des CoalChunk-Prefabs mit anderem Modell,
//    mit CollectibleCoal und Rigidbody) in die Felder eintragen.
public class BlastFurnace : MonoBehaviour, IMeltOwner, IPowerConsumer, ISaveable
{
    [Header("Strom (Geblaese)")]
    [Tooltip("Leistung des Geblaeses bei voller Drehzahl.")]
    [SerializeField] private float ratedPowerKW = 12f;
    [Tooltip("Leistung, wenn das Geblaese gerade erst anlaeuft.")]
    [SerializeField] private float idlePowerKW = 1.5f;
    [Tooltip("Hoeher = wichtiger. Unwichtige Verbraucher fallen zuerst aus.")]
    [SerializeField] private int importance = 15;
    [Tooltip("Wie schnell das Geblaese hochlaeuft (Anteil pro Sekunde).")]
    [SerializeField] private float blastSpinUpPerSecond = 0.25f;

    [Header("Beschickung")]
    [Tooltip("Wie viele Erzstuecke der Ofen aufnehmen kann.")]
    [SerializeField] private float oreCapacity = 30f;
    [Tooltip("Wie viele Kohlestuecke der Ofen aufnehmen kann.")]
    [SerializeField] private float coalCapacity = 30f;

    [Header("Schmelzen")]
    [Tooltip("Schmelztemperatur, Ausbeute und Kohlebedarf stehen pro Metall in der MetalRegistry (Inspector). " +
             "So viele Grad ueber der Schmelztemperatur schmilzt das Erz mit voller Geschwindigkeit.")]
    [SerializeField] private float smeltRangeC = 200f;
    [Tooltip("Erzstuecke pro Sekunde bei voller Temperatur und vollem Wind.")]
    [SerializeField] private float maxOrePerSecond = 0.2f;
    [Tooltip("Fassungsvermoegen des Herds fuer fluessiges Metall (kg).")]
    [SerializeField] private float hearthIronCapacityKg = 60f;
    [SerializeField] private float hearthSlagCapacityKg = 40f;

    [Header("Hitze")]
    [SerializeField] private float ambientC = 20f;
    [Tooltip("Aufheizung in Grad pro Sekunde bei vollem Feuer.")]
    [SerializeField] private float heatMaxCPerSecond = 10f;
    [Tooltip("Waermeverlust an die Umgebung (Anteil des Temperaturunterschieds pro Sekunde).")]
    [SerializeField] private float lossPerSecond = 0.004f;
    [Tooltip("Zusaetzlicher Verlust durch Kuehlwasser (gleiche Einheit).")]
    [SerializeField] private float waterLossPerSecond = 0.0025f;
    [Tooltip("So viel Grad pro Sekunde kostet das Schmelzen an Waerme.")]
    [SerializeField] private float smeltAbsorbCPerSecond = 1f;
    [Tooltip("Kohlestuecke pro Sekunde, die bei vollem Wind verbrennen (Feuer halten).")]
    [SerializeField] private float coalBurnPerSecond = 0.05f;

    [Header("Kuehlwasser")]
    [SerializeField] private float waterCapacity = 20f;
    [Tooltip("Liter pro Sekunde bei sehr heissem Ofen.")]
    [SerializeField] private float coolWaterLitersPerSecond = 0.8f;

    [Header("Verschleiss")]
    [Tooltip("Ueber dieser Temperatur nimmt der Ofen Schaden.")]
    [SerializeField] private float overheatTempC = 1650f;
    [Tooltip("Verschleiss (0..1) pro Sekunde je 100 Grad ueber der Grenze.")]
    [SerializeField] private float overheatWearPerSecond = 0.01f;

    [Header("Abstich")]
    [Tooltip("Geblaese-Schalter. Leer = der erste MachineSwitch unter dem Ofen.")]
    [SerializeField] private MachineSwitch blowerSwitch;
    [Tooltip("Abstich-Schalter. Leer = der zweite MachineSwitch unter dem Ofen.")]
    [SerializeField] private MachineSwitch tapSwitch;
    [Tooltip("Unter dieser Temperatur ist das Eisen zu zaeh zum Abstechen.")]
    [SerializeField] private float tapMinTempC = 1150f;
    [SerializeField] private float tapIronKgPerSecond = 6f;
    [SerializeField] private float tapSlagKgPerSecond = 4f;
    [SerializeField] private float ingotMassKg = 10f;
    [SerializeField] private float slagChunkMassKg = 10f;
    [Tooltip("Prefab eines Roheisen-Barrens (CollectibleCoal + Rigidbody + Collider).")]
    [SerializeField] private GameObject ingotPrefab;
    [Tooltip("Prefab eines Schlacke-Brockens.")]
    [SerializeField] private GameObject slagPrefab;
    [Tooltip("Hier erscheinen Barren und Schlacke; die blaue Achse zeigt, wohin sie rollen. " +
             "Leer = Mitte des Ofens.")]
    [SerializeField] private Transform tapPoint;
    [Tooltip("Optional: Partikel (Funken, Dampf), laufen waehrend des Abstichs.")]
    [SerializeField] private ParticleSystem tapEffect;

    [Header("Anzeige")]
    [Tooltip("Optional: Teil des Modells, das mit steigender Temperatur gluehend wird.")]
    [SerializeField] private Renderer glowRenderer;
    [SerializeField] private Color coldColor = new Color(0.25f, 0.25f, 0.27f);
    [SerializeField] private Color hotColor = new Color(1f, 0.45f, 0.1f);
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float debugDistance = 8f;
    [Tooltip("Optional: Leeres Objekt, an dessen Position der Info-Text erscheint.")]
    [SerializeField] private Transform infoAnchor;
    [SerializeField] private float debugHeight = 3f;

    // ----- Zustand -----
    private float ore;              // Erzstuecke im Schacht
    private float coal;             // Kohlestuecke im Schacht
    private float temp;             // Temperatur in Grad Celsius
    private float blast;            // Geblaese-Drehzahl 0..1
    private float water;            // Kuehlwasser in Litern
    private float hearthIron;       // fluessiges Eisen im Herd (kg)
    private float hearthSlag;       // Schlacke im Herd (kg)
    private float wear;             // 0 = neu, 1 = defekt
    private string oreMetal = "";   // Metall des Erzes im Schacht (ID aus der MetalRegistry)
    private string hearthMetal = "";// Metall im Herd
    private Port[] meltPorts;       // Schmelze-Anschluesse (zur Giesserei)
    private float ingotAccum;       // abgelassenes Metall, aus dem noch kein Barren wurde (kg)
    private float slagAccum;        // dasselbe fuer Schlacke (kg)

    private float supply = 1f;      // Stromversorgung 0..1 (vom Stromnetz)
    private bool smelting;
    private bool tapping;
    private bool frozen;            // Abstich offen, aber Eisen zu kalt
    private bool stateLoadedOnce;
    private bool warnedPrefab;
    private float glowTimer;
    private MaterialPropertyBlock glowBlock;

    // ----- Werte fuer Anzeige und andere Skripte -----
    public float TemperatureC => temp;
    public float Wear => wear;
    public bool IsBroken => wear >= 1f;
    private bool BlowerOn => blowerSwitch == null || blowerSwitch.IsOn;
    private bool TapOpen => tapSwitch != null && tapSwitch.IsOn;

    // Das Geblaese braucht Strom; im Leerlauf (aus oder defekt) nichts.
    public float DemandKW =>
        (IsBroken || !BlowerOn)
            ? 0f
            : Mathf.Lerp(idlePowerKW, ratedPowerKW, blast);

    public float RatedPowerKW => ratedPowerKW;
    public int Importance => importance;

    public void SetSupply(float fraction)
    {
        supply = Mathf.Clamp01(fraction);
    }

    private void Awake()
    {
        temp = ambientC;

        // Schmelze-Anschluesse merken (Ausgang zur Giesserei).
        System.Collections.Generic.List<Port> melts = new System.Collections.Generic.List<Port>();

        foreach (Port p in GetComponentsInChildren<Port>(true))
        {
            if (p.RawMedium == PortMedium.Melt)
                melts.Add(p);
        }

        meltPorts = melts.ToArray();

        // Schalter automatisch zuordnen: erster = Geblaese, zweiter = Abstich.
        // Gibt es nur einen, ist es der Abstich-Schalter (das Geblaese laeuft dann immer).
        if (blowerSwitch == null || tapSwitch == null)
        {
            MachineSwitch[] all = GetComponentsInChildren<MachineSwitch>(true);

            foreach (MachineSwitch sw in all)
            {
                if (sw == blowerSwitch || sw == tapSwitch)
                    continue;

                if (all.Length == 1)
                {
                    if (tapSwitch == null)
                        tapSwitch = sw;
                }
                else if (blowerSwitch == null)
                {
                    blowerSwitch = sw;
                }
                else if (tapSwitch == null)
                {
                    tapSwitch = sw;
                }
            }
        }

        if (tapEffect != null)
            tapEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnEnable()
    {
        PowerGrid.Register(this);
    }

    private void OnDisable()
    {
        PowerGrid.Unregister(this);
    }

    // ---------- Beschickung ----------

    // Passt noch ein Stueck dieser Art hinein?
    // Ist mindestens ein Schmelze-Anschluss mit einer Giesserei verbunden?
    private bool MeltOutputConnected
    {
        get
        {
            foreach (Port p in meltPorts)
            {
                if (p != null && p.IsConnected)
                    return true;
            }

            return false;
        }
    }

    // Passt noch ein Stueck dieser Art hinein? Bei Erz gehoert die Metall-ID dazu
    // (leer = Eisen).
    public bool CanAccept(ItemKind kind, string metal = "")
    {
        if (IsBroken)
            return false;

        if (kind == ItemKind.Ore)
        {
            string m = string.IsNullOrEmpty(metal) ? "Iron" : metal;

            if (!Metals.IsKnown(m) || ore + 1f > oreCapacity)
                return false;

            // Es wird immer nur ein Metall auf einmal geschmolzen: Entweder ist es
            // dasselbe wie im Schacht, oder der Schacht ist leer und im Herd liegt
            // nichts Anderes.
            bool sameOre = oreMetal == m && ore > 0.01f;
            bool freeToSwitch = ore <= 0.01f && (hearthIron <= 0.01f || hearthMetal == m);

            return sameOre || freeToSwitch;
        }

        if (kind == ItemKind.Coal)
            return coal + 1f <= coalCapacity;

        return false;
    }

    // Wird ueber ein Kommando aufgerufen (NetSync.AddCharge), damit alle Spieler
    // dieselbe Beschickung sehen.
    public bool TryAdd(ItemKind kind, string metal = "")
    {
        if (!CanAccept(kind, metal))
            return false;

        if (kind == ItemKind.Ore)
        {
            if (ore <= 0.01f)
                oreMetal = string.IsNullOrEmpty(metal) ? "Iron" : metal;

            ore += 1f;
        }
        else
        {
            coal += 1f;
        }

        return true;
    }

    // ---------- Simulation ----------

    private void Update()
    {
        // Sehr lange Bilder (Ruckler) nicht in einem Schritt rechnen.
        float dt = Mathf.Min(Time.deltaTime, 0.25f);

        if (dt <= 0f)
            return;

        bool broken = IsBroken;

        // 1) Geblaese laeuft hoch oder aus.
        float targetBlast = (!broken && BlowerOn) ? supply : 0f;
        blast = Mathf.MoveTowards(blast, targetBlast, blastSpinUpPerSecond * dt);

        // 2) Das Feuer: Es brennt nur mit Wind und Kohle und verbraucht Kohle.
        float burn = 0f;

        if (!broken && blast > 0.01f && coal > 0f)
        {
            coal = Mathf.Max(0f, coal - coalBurnPerSecond * blast * dt);
            burn = blast;
        }

        // 3) Schmelzen: Erz + Kohle -> Eisen + Schlacke (nur heiss genug).
        MetalDef def = Metals.Get(oreMetal) ?? Metals.Default;

        float smeltFactor = Mathf.Clamp01(
            (temp - def.meltTempC) / Mathf.Max(1f, smeltRangeC));

        smelting = false;

        if (!broken && burn > 0f && smeltFactor > 0f && ore > 0f && coal > 0f &&
            hearthIron < hearthIronCapacityKg && hearthSlag < hearthSlagCapacityKg &&
            (hearthIron <= 0.01f || hearthMetal == oreMetal))
        {
            float melt = maxOrePerSecond * smeltFactor * blast * dt;

            // Hoechstens so viel, wie Erz, Kohle und Platz im Herd erlauben.
            melt = Mathf.Min(melt, ore);
            melt = Mathf.Min(melt, coal / Mathf.Max(0.0001f, def.coalPerOre));
            melt = Mathf.Min(melt, (hearthIronCapacityKg - hearthIron) / Mathf.Max(0.0001f, def.oreYieldKg));
            melt = Mathf.Min(melt, (hearthSlagCapacityKg - hearthSlag) / Mathf.Max(0.0001f, def.slagPerOreKg));

            if (melt > 0f)
            {
                ore -= melt;
                coal -= melt * def.coalPerOre;
                hearthIron += melt * def.oreYieldKg;
                hearthSlag += melt * def.slagPerOreKg;
                hearthMetal = oreMetal;
                smelting = true;
            }
        }

        // 4) Temperatur: Heizung minus Verlust minus Schmelzwaerme.
        float waterFraction = Mathf.Clamp01(water / 2f);
        float heatIn = heatMaxCPerSecond * burn;
        float absorb = smelting ? smeltAbsorbCPerSecond * smeltFactor : 0f;
        float loss = (lossPerSecond + waterLossPerSecond * waterFraction) * (temp - ambientC);

        temp += (heatIn - absorb - loss) * dt;
        temp = Mathf.Max(temp, ambientC);

        // 5) Kuehlwasser wird verbraucht, je heisser, desto mehr.
        if (water > 0f && temp > ambientC + 50f)
        {
            float use = coolWaterLitersPerSecond * Mathf.Clamp01((temp - ambientC) / 1500f) * dt;
            water = Mathf.Max(0f, water - use);
        }

        // 6) Ueberhitzung: Zu heiss verschleisst den Ofen.
        if (temp > overheatTempC)
        {
            wear += overheatWearPerSecond * (temp - overheatTempC) / 100f * dt;
            wear = Mathf.Clamp01(wear);
        }

        // 7) Abstich.
        UpdateTapping(dt);

        // 8) Glueh-Anzeige (nicht jedes Bild).
        glowTimer -= dt;

        if (glowTimer <= 0f)
        {
            glowTimer = 0.2f;
            UpdateGlow();
        }
    }

    private void UpdateTapping(float dt)
    {
        tapping = false;
        frozen = false;

        if (IsBroken || !TapOpen || (hearthIron <= 0f && hearthSlag <= 0f))
        {
            UpdateTapEffect();
            return;
        }

        if (temp < tapMinTempC)
        {
            // Das Eisen ist zu zaeh, es kommt nichts heraus.
            frozen = hearthIron > 0f;
            UpdateTapEffect();
            return;
        }

        tapping = true;

        // Ist eine Giesserei angeschlossen, fliesst das Metall ueber den Schmelze-Anschluss
        // (siehe TakeFrom). Sonst wird es hier zu losen Barren.
        float iron = MeltOutputConnected ? 0f : Mathf.Min(hearthIron, tapIronKgPerSecond * dt);
        float slag = Mathf.Min(hearthSlag, tapSlagKgPerSecond * dt);

        hearthIron -= iron;
        hearthSlag -= slag;
        ingotAccum += iron;
        slagAccum += slag;

        // Aus der abgelassenen Menge entstehen ganze Barren / Brocken
        // (hoechstens ein paar pro Bild, damit nichts auf einmal herausplatzt).
        for (int i = 0; i < 3 && ingotAccum >= ingotMassKg; i++)
        {
            ingotAccum -= ingotMassKg;
            SpawnItem(ingotPrefab, ItemKind.Ingot,
                string.IsNullOrEmpty(hearthMetal) ? "Iron" : hearthMetal, ingotMassKg);
        }

        for (int i = 0; i < 3 && slagAccum >= slagChunkMassKg; i++)
        {
            slagAccum -= slagChunkMassKg;
            SpawnItem(slagPrefab, ItemKind.Slag, "", slagChunkMassKg);
        }

        UpdateTapEffect();
    }

    // Erzeugt einen losen Gegenstand am Abstich-Punkt.
    private void SpawnItem(GameObject prefab, ItemKind kind, string metal, float mass)
    {
        if (prefab == null)
        {
            if (!warnedPrefab)
            {
                warnedPrefab = true;
                Debug.LogWarning(
                    "Hochofen: Barren-/Schlacke-Prefab fehlt, die Menge geht verloren.", this);
            }

            return;
        }

        Transform point = tapPoint != null ? tapPoint : transform;

        Vector3 position = point.position
            + Random.insideUnitSphere * 0.05f;

        // Leichter Schubs in Richtung des Abstich-Punkts.
        Vector3 velocity = point.forward * 1.2f + Vector3.up * 0.4f;

        // Erzeugt wird nur beim Host (alle anderen bekommen den Gegenstand von ihm, siehe NetItems).
        // Die Markierung (Art, Metall, Gewicht) setzt NetItems.Spawn.
        NetItems.Spawn(prefab, position, Random.rotation, velocity, kind, metal, mass, true);
    }

    private void UpdateTapEffect()
    {
        if (tapEffect == null)
            return;

        if (tapping && !tapEffect.isEmitting)
            tapEffect.Play(true);
        else if (!tapping && tapEffect.isEmitting)
            tapEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void UpdateGlow()
    {
        if (glowRenderer == null)
            return;

        if (glowBlock == null)
            glowBlock = new MaterialPropertyBlock();

        float heat = Mathf.Clamp01((temp - ambientC) / 1500f);

        glowRenderer.GetPropertyBlock(glowBlock);
        glowBlock.SetColor("_Color", Color.Lerp(coldColor, hotColor, heat));
        glowRenderer.SetPropertyBlock(glowBlock);
    }

    // ---------- IPortOwner (Kuehlwasser-Eingang) ----------

    // Schmelze-Ausgang: So viel Metall kann gerade zur Giesserei fliessen.
    public float GetAvailable(Port port)
    {
        if (port.Medium != PortMedium.Melt || IsBroken || !TapOpen || temp < tapMinTempC)
            return 0f;

        return Mathf.Min(hearthIron, tapIronKgPerSecond * Time.deltaTime);
    }

    public float GetFreeSpace(Port port)
    {
        if (port.Medium != PortMedium.Water)
            return 0f;

        return Mathf.Max(0f, waterCapacity - water);
    }

    public float TakeFrom(Port port, float amount)
    {
        if (port.Medium != PortMedium.Melt)
            return 0f;

        float taken = Mathf.Clamp(amount, 0f, hearthIron);
        hearthIron -= taken;
        return taken;
    }

    // ---------- IMeltOwner ----------

    public string GetMelt(Port port)
    {
        return (port.Medium == PortMedium.Melt && hearthIron > 0.001f) ? hearthMetal : "";
    }

    public float GetMeltTemp(Port port)
    {
        return temp;
    }

    // Der Hochofen gibt nur ab, er nimmt keine Schmelze an.
    public bool CanAcceptMelt(Port port, string metal)
    {
        return false;
    }

    public float GiveMelt(Port port, string metal, float kg, float tempC)
    {
        return 0f;
    }

    public float GiveTo(Port port, float amount)
    {
        if (port.Medium != PortMedium.Water)
            return 0f;

        float accepted = Mathf.Clamp(amount, 0f, Mathf.Max(0f, waterCapacity - water));
        water += accepted;
        return accepted;
    }

    // ---------- Anzeige ----------

    private string StatusKey()
    {
        if (IsBroken)
            return "furnace.status.broken";

        if (tapping)
            return "furnace.status.tapping";

        if (frozen)
            return "furnace.status.frozen";

        if (!BlowerOn)
            return "furnace.status.off";

        if (supply <= 0.01f)
            return "furnace.status.nopower";

        if (hearthIron >= hearthIronCapacityKg - 0.01f)
            return "furnace.status.hearthfull";

        if (hearthSlag >= hearthSlagCapacityKg - 0.01f)
            return "furnace.status.slagfull";

        if (smelting)
            return "furnace.status.smelting";

        if (coal <= 0.01f)
            return "furnace.status.nocoal";

        if (temp < (Metals.Get(oreMetal) ?? Metals.Default).meltTempC)
            return "furnace.status.heating";

        if (ore <= 0.01f)
            return "furnace.status.noore";

        return "furnace.status.ready";
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

        // Position des Info-Textes: am Anker, sonst ueber der Maschine.
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
            "furnace.info",
            Loc.T(StatusKey()),
            temp.ToString("F0"),
            (blast * 100f).ToString("F0"),
            ore.ToString("F0"), oreCapacity.ToString("F0"),
            coal.ToString("F0"), coalCapacity.ToString("F0"),
            Metals.Name(string.IsNullOrEmpty(hearthMetal) ? oreMetal : hearthMetal),
            hearthIron.ToString("F0"), hearthSlag.ToString("F0"),
            water.ToString("F1"),
            (wear * 100f).ToString("F0"));

        GameSettings.DrawBoxAbove(screen, text);
    }

    // ---------- Speichern (und Abgleich im Mehrspieler) ----------

    public void SaveState(StateBag bag)
    {
        bag.SetFloat("ore", ore);
        bag.SetFloat("coal", coal);
        bag.SetFloat("temp", temp);
        bag.SetFloat("blast", blast);
        bag.SetFloat("water", water);
        bag.SetFloat("iron", hearthIron);
        bag.SetFloat("slag", hearthSlag);
        bag.SetFloat("wear", wear);
        bag.SetFloat("ingotAccum", ingotAccum);
        bag.SetFloat("slagAccum", slagAccum);
        bag.SetString("oreMetal", oreMetal);
        bag.SetString("hearthMetal", hearthMetal);
    }

    public void LoadState(StateBag bag)
    {
        ore = Mathf.Clamp(bag.GetFloat("ore", ore), 0f, oreCapacity);
        coal = Mathf.Clamp(bag.GetFloat("coal", coal), 0f, coalCapacity);
        temp = Mathf.Max(ambientC, bag.GetFloat("temp", temp));
        blast = Mathf.Clamp01(bag.GetFloat("blast", blast));
        water = Mathf.Clamp(bag.GetFloat("water", water), 0f, waterCapacity);
        wear = Mathf.Clamp01(bag.GetFloat("wear", wear));
        oreMetal = bag.GetString("oreMetal", oreMetal);
        hearthMetal = bag.GetString("hearthMetal", hearthMetal);

        float iron = Mathf.Clamp(bag.GetFloat("iron", hearthIron), 0f, hearthIronCapacityKg);
        float slag = Mathf.Clamp(bag.GetFloat("slag", hearthSlag), 0f, hearthSlagCapacityKg);

        // Im Mehrspieler schickt der Host regelmaessig seinen Stand. Barren und
        // Schlacke entstehen bei jedem Spieler selbst. Damit der verspaetete Stand
        // des Hosts beim Abstich nicht noch einmal dasselbe Eisen "auffuellt" (und
        // so doppelte Barren entstehen), wird der Herd dort nie hoeher gesetzt.
        // Beim ersten Laden (Spielstand, Beitritt) gilt der Stand unveraendert.
        if (NetSync.Mode == NetMode.Client && stateLoadedOnce)
        {
            hearthIron = Mathf.Min(hearthIron, iron);
            hearthSlag = Mathf.Min(hearthSlag, slag);
        }
        else
        {
            hearthIron = iron;
            hearthSlag = slag;
            ingotAccum = Mathf.Max(0f, bag.GetFloat("ingotAccum", ingotAccum));
            slagAccum = Mathf.Max(0f, bag.GetFloat("slagAccum", slagAccum));
        }

        stateLoadedOnce = true;
    }
}
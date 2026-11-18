using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  OreDeposits: die VORKOMMEN (Kohle, Eisen ...) im Untergrund der Voxel-Welt.
//
//  Statt ueberall gleichmaessiger Erzklumpen gibt es echte Vorkommen mit Ort, Form und Groesse:
//   - Die Welt ist in grosse Zellen geteilt (pro Vorkommens-Art eigene Groesse). Aus dem Seed wuerfelt jede
//     Zelle, ob sie ein Vorkommen hat, und wo, wie gross und wie tief.
//   - Die Groessen folgen einer Potenzverteilung: viele kleine, einige mittlere, wenige riesige.
//   - Eine Gegend ("Region", sehr langsames Rauschen) macht Vorkommen dort haeufiger oder seltener:
//     es gibt reiche Reviere und karge Landstriche.
//   - Form: ein verdrehtes Ellipsoid. "Nester" sind rundlich und dick, "Floeze" sind riesige, hauchduenne, leicht
//     geneigte Schichten. Der Rand franst aus, im Kern ist das Erz dicht.
//   - Abbau: Doerfer bauen im TAGEBAU von oben nach unten ab (bis zur maximalen Grubentiefe). Die abgebaute Menge
//     legt fest, wie tief die Grube ist; das Gelaende darueber wird abgetragen (siehe VoxelWorld.MineDeposit).
//   - Oberflaeche: Ein Vorkommen zeigt sich oben nur, wenn es bis zur Oberflaeche reicht (Chance "Outcrop Chance"
//     bzw. wenn ein Floez am Hang austritt). Offene Kohle ist also ein ehrlicher Hinweis auf ein Vorkommen darunter.
//
//  Alles ist aus dem Seed berechnet (kein Speichern noetig) und laesst sich abfragen, ohne dass das Gelaende
//  geladen ist (Dorf-Produktion, NPC-Fabriken, Karte ...): siehe VoxelWorld.FindDeposits.
//
//  Alle Zahlen sind im Inspector von VoxelWorld unter "Vorkommen" einstellbar. Eigene Arten (auch mehrere
//  Vorkommen desselben Erzes) legst du als weiteren Eintrag in der Liste an.
//
//  Einheiten: alles in Metern, in Weltkoordinaten relativ zur Ecke der Welt (wie die Voxel-Welt selbst).
// =====================================================================

// Eine Art von Vorkommen (z. B. "Kohlenest", "Kohlefloez", "Eisenlinse").
[Serializable]
public class DepositType
{
    public string name = "Kohlenest";
    [Tooltip("Welches Material das Vorkommen fuellt (aktuell sinnvoll: Coal oder Iron).")]
    public BlockType block = BlockType.Coal;
    [Tooltip("An: Das Erz ersetzt nur Stein (Gras und Erde bleiben, das Vorkommen zeigt sich nie an der Oberflaeche).")]
    public bool onlyReplaceStone = false;

    [Header("Haeufigkeit")]
    [Tooltip("Zellengroesse in Metern. Pro Zelle gibt es hoechstens ein Vorkommen dieser Art. Groesser = seltener.")]
    public float cellMeters = 100f;
    [Tooltip("Wahrscheinlichkeit, dass eine Zelle ein Vorkommen hat (0 bis 1). Wird von der Region verstaerkt oder gedaempft.")]
    [Range(0f, 1f)] public float chancePerCell = 0.6f;
    [Tooltip("Wie stark die Region (reiche/arme Gegend) wirkt: 0 = gar nicht, 1 = voll.")]
    [Range(0f, 1f)] public float regionStrength = 1f;

    [Header("Groesse (Halbmesser in Metern)")]
    public float radiusMin = 4f;
    public float radiusMax = 40f;
    [Tooltip("Form der Verteilung: klein = viele grosse Vorkommen, gross (3 und mehr) = fast alle klein.")]
    public float sizeExponent = 1.3f;
    [Tooltip("Wie laenglich es werden darf (1 = rund, 3 = dreimal so lang wie breit).")]
    public float aspectMax = 2f;
    [Tooltip("Halbe Dicke (Hoehe) in Metern: kleine Vorkommen sind duenner, grosse dicker (zwischen Min und Max).")]
    public float verticalMin = 1.5f;
    public float verticalMax = 4f;

    [Header("Lage")]
    [Tooltip("Obere Kante des Vorkommens liegt so tief unter der Oberflaeche (Meter, zwischen Min und Max).")]
    public float topDepthMin = 0.8f;
    public float topDepthMax = 6f;
    [Tooltip("Wahrscheinlichkeit, dass das Vorkommen bis an die Oberflaeche reicht (offene Kohle als Hinweis).")]
    [Range(0f, 1f)] public float outcropChance = 0.2f;
    [Tooltip("Hoechste Neigung in Grad (bei Floezen 1 bis 3 sinnvoll, grosse Floeze reichen dann ueber Hang und Tal).")]
    public float dipMaxDegrees = 0f;
    [Tooltip("Wie ausgefranst der Rand ist (0 = glatt, 1 = sehr zerfetzt, mit Einschluessen).")]
    [Range(0f, 1f)] public float ragged = 0.35f;

    [Header("Startvorkommen")]
    [Tooltip("Ein garantiertes Vorkommen dieser Art in dieser Entfernung (Meter) vom Startpunkt. 0 = keines. " +
             "Praktisch zum Testen, damit man nicht kilometerweit laufen muss.")]
    public float startDistance = 0f;
    public float startRadius = 8f;
}

public enum PitRampMode
{
    None,      // keine Rampe (nur Terrassen)
    Linear     // gerade Zufahrtsrampe, waechst mit der Grube mit
}

[Serializable]
public class DepositSettings
{
    [Header("Regionen")]
    [Tooltip("Groesse der Gegenden in Metern (Periode des langsamen Rauschens). Groesser = weitlaeufigere Reviere.")]
    public float regionMeters = 700f;
    [Tooltip("0 = ueberall gleich viel, 1 = ganze Gegenden ohne Vorkommen und andere sehr reich.")]
    [Range(0f, 1f)] public float regionContrast = 0.85f;

    [Header("Tagebau (Abbau durch Doerfer)")]
    [Tooltip("So tief (Meter unter der Oberflaeche) wird im Tagebau hoechstens abgebaut. Was tiefer liegt, bleibt Reserve " +
             "(der Spieler kann es mit Tunneln selbst abbauen).")]
    public float maxPitDepthMeters = 8f;

    [Tooltip("Die Grube wird in Stufen von so vielen Metern tiefer (Terrassen). Kleiner = glatter, aber mehr Rechenarbeit.")]
    public float pitStepMeters = 0.75f;

    [Tooltip("Zufahrtsrampe in die Grube. Sie wird mit jeder neuen Stufe nachgearbeitet und verlaengert, damit man " +
             "nicht ueber Terrassen fahren muss. Sie liegt an der Seite der Grube (90 Grad zur Verladestation).")]
    public PitRampMode rampMode = PitRampMode.Linear;

    [Tooltip("Steigung der Rampe in Prozent (12 = 12 m Hoehe auf 100 m Laenge). Kleiner = laenger und flacher.")]
    [Range(3f, 30f)]
    public float rampGradientPercent = 12f;

    [Tooltip("Breite der Rampe in Metern (Fahrbahn, ohne Boeschung).")]
    public float rampWidthMeters = 7f;

    [Header("Arten")]
    public List<DepositType> types = DefaultTypes();

    // Voreinstellung: Kohlenester, Kohlefloeze, Eisenlinsen.
    public static List<DepositType> DefaultTypes()
    {
        List<DepositType> list = new List<DepositType>();

        list.Add(new DepositType
        {
            name = "Kohlenest",
            block = BlockType.Coal,
            cellMeters = 90f,
            chancePerCell = 0.6f,
            radiusMin = 4f,
            radiusMax = 40f,
            sizeExponent = 1.3f,
            aspectMax = 2f,
            verticalMin = 1.5f,
            verticalMax = 4f,
            topDepthMin = 0.8f,
            topDepthMax = 6f,
            outcropChance = 0.2f,
            startDistance = 35f,
            startRadius = 8f
        });

        // Floez: riesig, duenn, leicht geneigt. Tritt am Hang aus, wo das Gelaende den Floez schneidet.
        list.Add(new DepositType
        {
            name = "Kohlefloez",
            block = BlockType.Coal,
            cellMeters = 220f,
            chancePerCell = 0.5f,
            radiusMin = 25f,
            radiusMax = 110f,
            sizeExponent = 1.6f,
            aspectMax = 3f,
            verticalMin = 0.5f,
            verticalMax = 1.4f,
            topDepthMin = 2f,
            topDepthMax = 7f,
            outcropChance = 0.35f,
            dipMaxDegrees = 2.5f,
            ragged = 0.2f
        });

        list.Add(new DepositType
        {
            name = "Eisenlinse",
            block = BlockType.Iron,
            onlyReplaceStone = true,
            cellMeters = 160f,
            chancePerCell = 0.5f,
            radiusMin = 6f,
            radiusMax = 55f,
            sizeExponent = 1.4f,
            aspectMax = 2.2f,
            verticalMin = 2f,
            verticalMax = 5f,
            topDepthMin = 4f,
            topDepthMax = 8f,
            outcropChance = 0f,
            ragged = 0.4f,
            startDistance = 90f,
            startRadius = 12f
        });

        return list;
    }
}

// Ein einzelnes Vorkommen (berechnet, nicht gespeichert).
public struct Deposit
{
    public string typeName;
    public BlockType block;
    public bool onlyStone;

    // Mitte (Meter, Weltkoordinaten relativ zur Weltecke).
    public Vector3 center;

    // Halbachsen in Metern: Hauptrichtung, Querrichtung, Hoehe.
    public float radiusMajor;
    public float radiusMinor;
    public float radiusVertical;

    // Dreht Weltrichtungen ins Eigensystem des Vorkommens.
    public Quaternion inverse;

    public float boundHorizontal;
    public float boundVertical;
    public float ragged;

    // Drehung der Hauptachse um die Hochachse (Grad).
    public float yawDeg;

    // Abbau von oben: Oberhalb dieser Hoehe (Meter) ist das Erz weg. float.MaxValue = noch nichts abgebaut.
    public float cutY;

    // So viel (Kubikmeter) kann ein Dorf insgesamt im Tagebau foerdern (bis zur maximalen Grubentiefe).
    public float reachableM3;

    // Oberflaechenhoehe in der Mitte des Vorkommens.
    public float surfaceY;

    // Reicht es bis (fast) an die Oberflaeche?
    public bool outcrops;

    // Grobe Menge in Kubikmetern Gestein am Anfang (Ellipsoid, Rand ausgefranst: etwa die Haelfte).
    public float volumeM3;

    // Wie viel davon noch uebrig ist (nach Abbau durch Doerfer und Spieler).
    public float remainingM3;

    // Kennung (aus dem Seed, immer gleich): Damit findet man das Vorkommen wieder.
    public long id;
}

public class OreDeposits
{
    private readonly DepositSettings settings;
    private readonly int seed;
    private readonly Func<float, float, float> surfaceAt;   // Oberflaechenhoehe (Meter) an (x, z)
    private readonly float[] offsets = new float[64];
    private readonly List<Deposit> fixedDeposits = new List<Deposit>();

    // Abbau: Von Doerfern gefoerdert (laesst das Vorkommen von aussen nach innen schrumpfen) und vom Spieler
    // ausgegraben (die Loecher sind schon echt im Gelaende, es zaehlt nur gegen den Vorrat).
    private readonly Dictionary<long, float> villageMined = new Dictionary<long, float>();
    private readonly Dictionary<long, float> playerDug = new Dictionary<long, float>();

    // Untergrenze: so viel Fels (Meter) bleibt mindestens unter dem Vorkommen.
    private const float FloorMargin = 1.2f;

    public OreDeposits(DepositSettings settings, int seed, Func<float, float, float> surfaceAt)
    {
        this.settings = settings;
        this.seed = seed;
        this.surfaceAt = surfaceAt;

        Rng r = new Rng(Hash(seed, 9001, 1, 2));

        for (int i = 0; i < offsets.Length; i++)
            offsets[i] = r.Next() * 1000f;
    }

    // ---------- Zufall (immer gleich zum selben Seed) ----------

    private struct Rng
    {
        private uint s;

        public Rng(uint seed)
        {
            s = seed == 0 ? 0x9E3779B9u : seed;
            Next();
            Next();
        }

        // 0 bis knapp unter 1.
        public float Next()
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return (s & 0xFFFFFFu) / 16777216f;
        }
    }

    private static uint Hash(int a, int b, int c, int d)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)a) * 16777619u;
            h ^= h >> 15;
            h = (h ^ (uint)b) * 16777619u;
            h ^= h >> 13;
            h = (h ^ (uint)c) * 16777619u;
            h ^= h >> 16;
            h = (h ^ (uint)d) * 16777619u;
            h ^= h >> 15;
            return h;
        }
    }

    // ---------- Regionen ----------

    // Faktor 1 - Kontrast ... 1 + Kontrast: wie reich ist die Gegend an dieser Stelle fuer diese Art?
    private float RegionFactor(int typeIndex, DepositType t, float x, float z)
    {
        if (t.regionStrength <= 0f || settings.regionContrast <= 0f)
            return 1f;

        float size = Mathf.Max(10f, settings.regionMeters);
        int o = (typeIndex * 2) % (offsets.Length - 1);

        float n = Mathf.PerlinNoise(x / size + offsets[o], z / size + offsets[o + 1]);

        // Perlin liegt meist um 0,5: den mittleren Bereich spreizen, damit echte Unterschiede entstehen.
        float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.72f, n));
        float c = settings.regionContrast;
        float f = Mathf.Lerp(1f - c, 1f + c, s);

        return Mathf.Lerp(1f, f, t.regionStrength);
    }

    // ---------- Vorkommen erzeugen ----------

    private bool Make(int typeIndex, DepositType t, int cx, int cz, out Deposit d)
    {
        d = new Deposit();

        float cell = Mathf.Max(10f, t.cellMeters);
        Rng r = new Rng(Hash(seed, typeIndex, cx, cz));

        float chanceRoll = r.Next();
        float px = (cx + r.Next()) * cell;
        float pz = (cz + r.Next()) * cell;

        float regionF = RegionFactor(typeIndex, t, (cx + 0.5f) * cell, (cz + 0.5f) * cell);

        if (chanceRoll > t.chancePerCell * regionF)
            return false;

        float sizeRoll = r.Next();
        float aspectRoll = r.Next();
        float yawRoll = r.Next();
        float heightRoll = r.Next();
        float dipRoll = r.Next();
        float outcropRoll = r.Next();
        float topRoll = r.Next();

        // Pareto-Verteilung: viele kleine, wenige grosse. In reichen Gegenden etwas groesser.
        float radius = t.radiusMin * Mathf.Pow(1f - sizeRoll * 0.999f, -1f / Mathf.Max(0.2f, t.sizeExponent));
        float c = Mathf.Max(0.0001f, settings.regionContrast);
        radius *= Mathf.Lerp(1f, Mathf.Lerp(0.8f, 1.25f, Mathf.InverseLerp(1f - c, 1f + c, regionF)), t.regionStrength);
        radius = Mathf.Clamp(radius, t.radiusMin * 0.8f, t.radiusMax * 1.25f);

        float sizeNorm = Mathf.InverseLerp(t.radiusMin, t.radiusMax, radius);
        float aspect = Mathf.Lerp(1f, Mathf.Max(1f, t.aspectMax), aspectRoll);
        float vertical = Mathf.Lerp(t.verticalMin, t.verticalMax, Mathf.Clamp01(sizeNorm * 0.6f + heightRoll * 0.4f));
        float dip = (dipRoll * 2f - 1f) * t.dipMaxDegrees;
        bool outcrop = outcropRoll < t.outcropChance;
        float topDepth = outcrop
            ? Mathf.Lerp(-0.4f, 0.4f, topRoll)
            : Mathf.Lerp(t.topDepthMin, t.topDepthMax, topRoll);

        Build(ref d, t, px, pz, radius, radius / aspect, vertical, yawRoll * 180f, dip, topDepth, outcrop);
        d.id = ((long)typeIndex << 52) ^ ((long)Hash(seed, typeIndex, cx, cz) << 8) ^ 1L;
        return true;
    }

    // Eigenschaften eines Vorkommens zusammensetzen (auch fuer die Startvorkommen).
    private void Build(ref Deposit d, DepositType t, float x, float z, float rMajor, float rMinor, float rVertical,
                       float yawDeg, float dipDeg, float topDepth, bool outcrop)
    {
        float surface = surfaceAt(x, z);
        float y = surface - topDepth - rVertical;

        // Nicht unter den Weltboden.
        y = Mathf.Max(y, rVertical + FloorMargin);

        d.typeName = t.name;
        d.block = t.block;
        d.onlyStone = t.onlyReplaceStone;
        d.center = new Vector3(x, y, z);
        d.radiusMajor = rMajor;
        d.radiusMinor = rMinor;
        d.radiusVertical = rVertical;
        d.inverse = Quaternion.Inverse(Quaternion.Euler(dipDeg, yawDeg, 0f));
        d.ragged = t.ragged;
        d.outcrops = outcrop;

        float expand = 1f + 0.5f * t.ragged;
        d.boundHorizontal = rMajor * expand;
        d.boundVertical = (rVertical + rMajor * Mathf.Sin(Mathf.Abs(dipDeg) * Mathf.Deg2Rad)) * expand;
        d.volumeM3 = 4f / 3f * Mathf.PI * rMajor * rMinor * rVertical * 0.5f;
        d.remainingM3 = d.volumeM3;
        d.yawDeg = yawDeg;
        d.cutY = float.MaxValue;
        d.surfaceY = surface;

        // Wie viel liegt oberhalb der maximalen Grubentiefe? (Anteil der Ellipsoid-Kappe ueber dieser Ebene.)
        float limitY = surface - Mathf.Max(0f, settings.maxPitDepthMeters);
        float tLimit = Mathf.Clamp((limitY - y) / Mathf.Max(0.01f, rVertical), -1f, 1f);
        d.reachableM3 = d.volumeM3 * FractionAbove(tLimit);
    }

    // Anteil des Ellipsoid-Volumens oberhalb einer Ebene. t = -1 (ganz unten) ... 0 (Mitte) ... 1 (ganz oben).
    private static float FractionAbove(float t)
    {
        return Mathf.Clamp01((2f - 3f * t + t * t * t) * 0.25f);
    }

    // Umkehrung: In welcher Hoehe liegt die Ebene, ueber der noch der Anteil f des Volumens liegt?
    private static float PlaneForFractionAbove(float f)
    {
        float lo = -1f;
        float hi = 1f;

        for (int i = 0; i < 30; i++)
        {
            float mid = (lo + hi) * 0.5f;

            // FractionAbove faellt mit t: zu viel darueber = Ebene muss hoeher.
            if (FractionAbove(mid) > f)
                lo = mid;
            else
                hi = mid;
        }

        return (lo + hi) * 0.5f;
    }

    // Grundflaeche der Grube auf der aktuellen Abbauhoehe (Halbachsen in Metern, Drehung = yawDeg).
    // false, wenn noch nichts abgebaut ist.
    public static bool PitFootprint(Deposit d, out Vector2 half, out float cutY)
    {
        cutY = d.cutY;
        half = Vector2.zero;

        if (d.cutY == float.MaxValue)
            return false;

        float t = Mathf.Clamp((d.cutY - d.center.y) / Mathf.Max(0.01f, d.radiusVertical), -1f, 1f);
        float k = Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
        float expand = 1f + 0.3f * d.ragged;

        // x = entlang der Hauptachse, y = quer dazu.
        half = new Vector2(d.radiusMajor * k * expand, d.radiusMinor * k * expand);
        return true;
    }

    // ---------- Abbau ----------

    // Wie viel ist noch uebrig? (Anfangsmenge minus Dorfabbau minus Spieler.)
    private float RemainingOf(long id, float total)
    {
        float m, p;
        villageMined.TryGetValue(id, out m);
        playerDug.TryGetValue(id, out p);

        return Mathf.Max(0f, total - m - p);
    }

    // Wendet den Abbau auf ein frisch berechnetes Vorkommen an: Der Dorfabbau traegt es von oben ab (Tagebau).
    // Die abgebaute Menge bestimmt die Hoehe, ueber der kein Erz mehr liegt. Gibt false zurueck, wenn nichts mehr da ist.
    private bool Deplete(ref Deposit d)
    {
        float m;
        villageMined.TryGetValue(d.id, out m);

        d.remainingM3 = RemainingOf(d.id, d.volumeM3);

        if (m <= 0f)
        {
            d.cutY = float.MaxValue;
            return true;
        }

        float f = Mathf.Clamp01(m / Mathf.Max(0.001f, d.volumeM3));

        if (f >= 0.995f)
            return false;

        d.cutY = d.center.y + PlaneForFractionAbove(f) * d.radiusVertical;
        return true;
    }

    // Ein Dorf foerdert aus einem Vorkommen. Gibt zurueck, wie viel wirklich gefoerdert wurde (kleiner, wenn
    // fast nichts mehr da ist). totalM3 = Anfangsmenge (Deposit.volumeM3).
    public float Mine(long id, float totalM3, float reachableM3, float wantedM3)
    {
        float m;
        villageMined.TryGetValue(id, out m);

        // Mehr als bis zur maximalen Grubentiefe gibt es nicht, und nicht mehr als noch da ist.
        float capacity = Mathf.Min(reachableM3 - m, RemainingOf(id, totalM3));
        float take = Mathf.Clamp(wantedM3, 0f, Mathf.Max(0f, capacity));

        if (take <= 0f)
            return 0f;

        villageMined[id] = m + take;

        return take;
    }

    // Wie viel hat das Dorf insgesamt schon gefoerdert (Kubikmeter)?
    public float MinedByVillages(long id)
    {
        float m;
        villageMined.TryGetValue(id, out m);
        return m;
    }

    // Der Spieler hat an dieser Stelle ausgegraben (Kubikmeter Erz): zaehlt gegen den Vorrat des Vorkommens.
    public void NotifyDug(float mx, float my, float mz, BlockType block, float volumeM3)
    {
        if (volumeM3 <= 0f)
            return;

        List<Deposit> found = new List<Deposit>();
        Collect(mx - 1f, mz - 1f, mx + 1f, mz + 1f, found);

        foreach (Deposit d in found)
        {
            if (d.block != block)
                continue;

            float dx = mx - d.center.x;
            float dz = mz - d.center.z;
            float dy = Mathf.Abs(my - d.center.y);

            // Der Grabpunkt liegt (grob) im Vorkommen, inklusive etwas Rand fuer die Grabkugel.
            if (dx * dx + dz * dz > (d.boundHorizontal + 2f) * (d.boundHorizontal + 2f) || dy > d.boundVertical + 2f)
                continue;

            float p;
            playerDug.TryGetValue(d.id, out p);
            playerDug[d.id] = p + volumeM3;
            return;
        }
    }

    // ---------- Speichern ----------

    public void Write(System.IO.BinaryWriter w)
    {
        w.Write(villageMined.Count);

        foreach (KeyValuePair<long, float> pair in villageMined)
        {
            w.Write(pair.Key);
            w.Write(pair.Value);
        }

        w.Write(playerDug.Count);

        foreach (KeyValuePair<long, float> pair in playerDug)
        {
            w.Write(pair.Key);
            w.Write(pair.Value);
        }
    }

    // Zustand verwerfen (neues Spiel / Laden ohne Abbau).
    public void Clear()
    {
        villageMined.Clear();
        playerDug.Clear();
    }

    // Gespeicherten Zustand ueberlesen (wenn die Vorkommen gerade abgeschaltet sind).
    public static void SkipState(System.IO.BinaryReader r)
    {
        for (int part = 0; part < 2; part++)
        {
            int n = r.ReadInt32();

            for (int i = 0; i < n && i < 1000000; i++)
            {
                r.ReadInt64();
                r.ReadSingle();
            }
        }
    }

    public void Read(System.IO.BinaryReader r)
    {
        villageMined.Clear();
        playerDug.Clear();

        int n = r.ReadInt32();

        for (int i = 0; i < n && i < 1000000; i++)
            villageMined[r.ReadInt64()] = r.ReadSingle();

        n = r.ReadInt32();

        for (int i = 0; i < n && i < 1000000; i++)
            playerDug[r.ReadInt64()] = r.ReadSingle();
    }

    // Garantierte Vorkommen in der Naehe des Startpunkts (Meter in Weltkoordinaten).
    public void AddStartDeposits(float spawnX, float spawnZ)
    {
        fixedDeposits.Clear();

        for (int i = 0; i < settings.types.Count; i++)
        {
            DepositType t = settings.types[i];

            if (t == null || t.startDistance <= 0f)
                continue;

            Rng r = new Rng(Hash(seed, 777, i, 3));
            float angle = r.Next() * Mathf.PI * 2f;

            float x = spawnX + Mathf.Cos(angle) * t.startDistance;
            float z = spawnZ + Mathf.Sin(angle) * t.startDistance;

            float rMajor = Mathf.Max(1f, t.startRadius);
            float vertical = Mathf.Lerp(t.verticalMin, t.verticalMax, 0.6f);

            Deposit d = new Deposit();

            // Startvorkommen reichen bis an die Oberflaeche, damit man sie findet (offene Kohle als Hinweis).
            bool show = !t.onlyReplaceStone;
            Build(ref d, t, x, z, rMajor, rMajor / 1.3f, vertical, r.Next() * 180f, 0f,
                  show ? -0.2f : (t.topDepthMin + t.topDepthMax) * 0.5f, show);

            d.id = ((long)i << 52) ^ ((long)Hash(seed, 777, i, 3) << 8) ^ 2L;
            fixedDeposits.Add(d);
        }
    }

    // ---------- Abfragen ----------

    // Alle Vorkommen, die das Rechteck (Meter) beruehren koennten. Ergebnis ersetzt den Inhalt von result.
    public void Collect(float minX, float minZ, float maxX, float maxZ, List<Deposit> result)
    {
        result.Clear();

        for (int i = 0; i < settings.types.Count; i++)
        {
            DepositType t = settings.types[i];

            if (t == null || t.chancePerCell <= 0f)
                continue;

            float cell = Mathf.Max(10f, t.cellMeters);
            float reach = t.radiusMax * 1.25f * (1f + 0.5f * t.ragged);

            int x0 = Mathf.FloorToInt((minX - reach) / cell);
            int x1 = Mathf.FloorToInt((maxX + reach) / cell);
            int z0 = Mathf.FloorToInt((minZ - reach) / cell);
            int z1 = Mathf.FloorToInt((maxZ + reach) / cell);

            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    Deposit d;

                    if (Make(i, t, cx, cz, out d) && Touches(d, minX, minZ, maxX, maxZ) && Deplete(ref d))
                        result.Add(d);
                }
            }
        }

        for (int i = 0; i < fixedDeposits.Count; i++)
        {
            Deposit d = fixedDeposits[i];

            if (Touches(d, minX, minZ, maxX, maxZ) && Deplete(ref d))
                result.Add(d);
        }
    }

    private static bool Touches(Deposit d, float minX, float minZ, float maxX, float maxZ)
    {
        float nx = Mathf.Clamp(d.center.x, minX, maxX);
        float nz = Mathf.Clamp(d.center.z, minZ, maxZ);
        float dx = d.center.x - nx;
        float dz = d.center.z - nz;

        return dx * dx + dz * dz <= d.boundHorizontal * d.boundHorizontal;
    }

    // Vorkommen im Umkreis (Meter) um einen Punkt, nach Entfernung sortiert. block = Any, wenn egal.
    public List<Deposit> Query(float x, float z, float radius, BlockType? block)
    {
        List<Deposit> found = new List<Deposit>();
        Collect(x - radius, z - radius, x + radius, z + radius, found);

        found.RemoveAll(d =>
        {
            float dx = d.center.x - x;
            float dz = d.center.z - z;

            return dx * dx + dz * dz > radius * radius || (block.HasValue && d.block != block.Value);
        });

        found.Sort((a, b) =>
        {
            float da = (a.center.x - x) * (a.center.x - x) + (a.center.z - z) * (a.center.z - z);
            float db = (b.center.x - x) * (b.center.x - x) + (b.center.z - z) * (b.center.z - z);
            return da.CompareTo(db);
        });

        return found;
    }

    // ---------- Material an einem Punkt ----------

    // Welches Erz liegt an diesem Punkt? Gibt -1 zurueck, wenn keines (dann bleibt das Gelaendematerial).
    // "current" ist das bisherige Material (Gras, Erde, Stein): Manche Erze ersetzen nur Stein.
    public int BlockAt(List<Deposit> candidates, float mx, float my, float mz, BlockType current)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            Deposit d = candidates[i];

            float dx = mx - d.center.x;
            float dz = mz - d.center.z;

            if (dx * dx + dz * dz > d.boundHorizontal * d.boundHorizontal)
                continue;

            float dy = my - d.center.y;

            if (dy > d.boundVertical || dy < -d.boundVertical)
                continue;

            // Von oben abgebaut (Tagebau): darueber gibt es kein Erz mehr.
            if (my > d.cutY)
                continue;

            Vector3 l = d.inverse * new Vector3(dx, dy, dz);

            float qx = l.x / d.radiusMajor;
            float qy = l.y / d.radiusVertical;
            float qz = l.z / d.radiusMinor;

            float q = Mathf.Sqrt(qx * qx + qy * qy + qz * qz);

            if (q >= 1f + 0.5f * d.ragged)
                continue;

            // Kern dicht, Rand ausgefranst (Rauschen mit etwa 3 m Muster).
            float v = 1f - q + (Noise3(mx, my, mz, 0.35f, 40) - 0.5f) * d.ragged;

            if (v <= 0f)
                continue;

            if (d.onlyStone && current != BlockType.Stone)
                continue;

            return (int)d.block;
        }

        return -1;
    }

    private float Noise3(float mx, float my, float mz, float scale, int o)
    {
        float a = Mathf.PerlinNoise(mx * scale + offsets[o], my * scale + offsets[o + 1]);
        float b = Mathf.PerlinNoise(my * scale + offsets[o + 2], mz * scale + offsets[o + 3]);
        float c = Mathf.PerlinNoise(mz * scale + offsets[o + 4], mx * scale + offsets[o + 5]);

        return (a + b + c) / 3f;
    }
}
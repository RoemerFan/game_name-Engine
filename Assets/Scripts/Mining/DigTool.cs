using UnityEngine;

// =====================================================================
//  DigTool: ein Grabwerkzeug (Schaufel, Spitzhacke ...). Es ist ein ganz normaler Gegenstand zum Aufheben:
//   E = aufheben / fallen lassen, R = sanft ablegen, Q = loslassen (siehe CoalPickup).
//  Haelt man es in der Hand und gräbt mit der linken Maustaste, dann gelten seine Werte statt denen der blossen Hand
//  (der VoxelDigger liest sie aus) und es schwingt beim Graben.
//
//  EIGENSCHAFTEN (alles im Inspector einstellbar):
//   - Tempo, Radius der Grabkugel und Reichweite
//   - Material-Vorlieben: je Material ein eigenes Tempo und eine eigene Abnutzung
//   - Haltbarkeit: so viele Grabvorgaenge haelt es aus, dann geht es kaputt und verschwindet
//
//  PREFAB aufbauen (wie der CoalChunk): Wurzel mit Rigidbody, Collider und diesem Skript, dazu "SavedItem" damit es
//  gespeichert und im Mehrspieler abgeglichen wird. Das Prefab muss in der Liste "Item Prefabs" am SaveSystem stehen.
//  Das Modell hängst du unter ein leeres Objekt ("Swing Pivot"): Dieses Objekt schwingt. Sein Ursprung ist der Drehpunkt
//  (zum Beispiel das Griffende). Vorne = +Z.
//
//  Die Schwing-Animation sieht nur der Spieler, der das Werkzeug haelt.
//  Beispiele zum Ausprobieren: Menue Factocreative > Platzhalter > Werkzeuge erzeugen.
// =====================================================================
public class DigTool : CollectibleCoal
{
    [System.Serializable]
    public class MaterialEffect
    {
        public BlockType material = BlockType.Stone;

        [Tooltip("Tempo in diesem Material (1 = normal, 2 = doppelt so schnell, 0.5 = halb so schnell).")]
        public float speedFactor = 1f;

        [Tooltip("Abnutzung in diesem Material (1 = normal, 2 = nutzt sich doppelt so schnell ab).")]
        public float wearFactor = 1f;
    }

    [Header("Wirkung")]
    [Tooltip("Grundtempo gegenueber der blossen Hand (1 = wie die Hand).")]
    public float speedMultiplier = 2f;

    [Tooltip("Radius der Grabkugel gegenueber der Hand (1 = wie die Hand, 1.3 = groesseres Loch).")]
    public float radiusMultiplier = 1f;

    [Tooltip("Zusaetzliche Reichweite in Metern.")]
    public float reachBonus = 0f;

    [Tooltip("Material-Vorlieben. Nicht eingetragene Materialien: Tempo 1, Abnutzung 1.")]
    public MaterialEffect[] materials = new MaterialEffect[0];

    [Header("Haltbarkeit")]
    [Tooltip("So viele Grabvorgaenge haelt das Werkzeug aus. 0 = unzerstoerbar.")]
    public float maxDurability = 200f;

    [Tooltip("Abnutzung pro Grabvorgang (wird mit dem Faktor des Materials multipliziert).")]
    public float wearPerDig = 1f;

    [Header("Schwingen")]
    [Tooltip("Der Teil, der beim Graben schwingt (leer = keine Animation).")]
    public Transform swingPivot;

    [Tooltip("Drehachse des Schwungs im Raum des Pivots. X = nach vorne/oben kippen.")]
    public Vector3 swingAxis = Vector3.right;

    [Tooltip("So weit (Grad) wird ausgeholt.")]
    public float raiseAngle = 55f;

    [Tooltip("So weit (Grad) schlaegt es durch.")]
    public float strikeAngle = 25f;

    [Tooltip("Wie schnell die Bewegung nachlaeuft (Grad pro Sekunde).")]
    public float swingSpeed = 500f;

    // Rest der Haltbarkeit. -1 = noch nicht gesetzt (wird beim ersten Zugriff auf das Maximum gesetzt).
    private float remaining = -1f;

    private Quaternion pivotBase = Quaternion.identity;
    private bool pivotReady;
    private float angle;
    private float targetAngle;
    private float lastSwingTime = -10f;

    // Aktuelle Haltbarkeit (wird gespeichert und im Mehrspieler uebertragen).
    public float Durability
    {
        get
        {
            if (remaining < 0f)
                remaining = maxDurability;

            return remaining;
        }
        set { remaining = Mathf.Clamp(value, 0f, Mathf.Max(maxDurability, 0f)); }
    }

    // Zustand von 0 (kaputt) bis 1 (neu). Unzerstoerbare Werkzeuge sind immer 1.
    public float Condition01
    {
        get { return maxDurability <= 0f ? 1f : Mathf.Clamp01(Durability / maxDurability); }
    }

    public bool IsBroken
    {
        get { return maxDurability > 0f && Durability <= 0f; }
    }

    private MaterialEffect EffectOf(BlockType type)
    {
        if (materials != null)
        {
            foreach (MaterialEffect e in materials)
            {
                if (e != null && e.material == type)
                    return e;
            }
        }

        return null;
    }

    // Gesamttempo gegenueber der Hand in diesem Material.
    public float SpeedFor(BlockType type)
    {
        MaterialEffect e = EffectOf(type);
        return Mathf.Max(0.05f, speedMultiplier * (e != null ? e.speedFactor : 1f));
    }

    // Ein Grabvorgang nutzt das Werkzeug ab. Gibt true zurueck, wenn es jetzt kaputt ist.
    public bool Wear(BlockType type)
    {
        if (maxDurability <= 0f)
            return false;

        MaterialEffect e = EffectOf(type);
        Durability = Durability - wearPerDig * (e != null ? e.wearFactor : 1f);
        return IsBroken;
    }

    // Vom VoxelDigger jedes Bild waehrend des Grabens: Fortschritt des Grabvorgangs (0..1) steuert den Schwung.
    public void SetSwing(float progress01)
    {
        lastSwingTime = Time.time;
        progress01 = Mathf.Clamp01(progress01);

        // Erst ausholen (0 bis 70 %), dann zuschlagen (70 bis 100 %): Der Treffer sitzt am Ende des Vorgangs.
        if (progress01 < 0.7f)
            targetAngle = -raiseAngle * (progress01 / 0.7f);
        else
            targetAngle = Mathf.Lerp(-raiseAngle, strikeAngle, (progress01 - 0.7f) / 0.3f);
    }

    private void Update()
    {
        if (swingPivot == null)
            return;

        if (!pivotReady)
        {
            pivotBase = swingPivot.localRotation;
            pivotReady = true;
        }

        // Kein Schwung mehr angefordert: zurueck in die Ruhestellung.
        if (Time.time - lastSwingTime > 0.15f)
            targetAngle = 0f;

        angle = Mathf.MoveTowards(angle, targetAngle, swingSpeed * Time.deltaTime);

        Vector3 axis = swingAxis.sqrMagnitude > 0.0001f ? swingAxis.normalized : Vector3.right;
        swingPivot.localRotation = pivotBase * Quaternion.AngleAxis(angle, axis);
    }
}
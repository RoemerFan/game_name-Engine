using System;
using System.Globalization;
using UnityEngine;

// =====================================================================
//  Wagon: ein Gueterwagen (ohne eigenen Antrieb). Wird mit Kupplungen an eine Lok gehaengt
//  (siehe TrainCar / CarCoupler) und faehrt dann im Zug mit.
//
//  LADUNG: Der Wagen hat ein ItemStorage (Haufen aus ganzen Stuecken). Man belaedt ihn, indem man
//  Stuecke in die Ladezone wirft - zum Beispiel mit einem (Teleskop-)Foerderband, dessen Ende
//  ueber dem Wagen haengt. Mit E auf den Haufen nimmt man das oberste Stueck heraus.
//
//  BODENKLAPPE: Ein Hebel (CarHatchLever) oeffnet die Klappe im Boden. Solange sie offen ist, faellt
//  etwa alle "Release Interval" Sekunden ein Stueck heraus (nur der Host erzeugt sie, bei allen
//  Spielern verschwindet dasselbe Stueck aus dem Haufen). Darunter kann ein Foerderband stehen
//  (Entladegrube zwischen den Schienen oder ein Band neben dem Gleis).
//
//  EIGENES MODELL: Prefab mit diesem Skript (+ ItemStorage mit Zone), Ursprung = Mitte auf Schienen-
//  Oberkante, vorne = +Z. Fuer die Klappe: "Hatch Plate" (kippt um ihren Ursprung = Gelenk, lokale
//  X-Achse, positiver Winkel = +Z-Seite nach unten), "Hatch Collider" (wird beim Oeffnen
//  abgeschaltet) und "Hatch Drop" (Punkt unter der Klappe, an dem die Stuecke erscheinen).
// =====================================================================
public class Wagon : TrainCar, ISaveable
{
    [Header("Wagen")]
    [Tooltip("Kennwort des Wagen-Typs (wird im Spielstand gespeichert; vom TrackManager gesetzt).")]
    public string recipeId = "";

    [Tooltip("Ausrollen (Bremsen) in m/s^2, wenn der Wagen allein steht und noch rollt.")]
    public float rollingResistance = 1f;

    [Header("Gewicht")]
    [Tooltip("Gewicht des leeren Wagens (Tonnen).")]
    public float emptyMassTons = 12f;

    [Tooltip("Gewicht pro geladenem Stueck (Tonnen). 120 Stueck x 0,1 = 12 t Ladung.")]
    public float tonsPerItem = 0.1f;

    public override float MassTons
    {
        get { return emptyMassTons + (storage != null ? storage.Count * tonsPerItem : 0f); }
    }

    [Header("Ladung")]
    [Tooltip("Das Lager mit den Stuecken (leer = wird automatisch gefunden).")]
    public ItemStorage storage;

    [Header("Bodenklappe")]
    [Tooltip("Die Klappenplatte. Ihr Ursprung ist das Gelenk.")]
    public Transform hatchPlate;
    [Tooltip("Collider der Platte: wird beim Oeffnen abgeschaltet, damit die Stuecke hindurchfallen.")]
    public Collider hatchCollider;
    [Tooltip("Hier erscheinen die herausfallenden Stuecke (unter der Klappe).")]
    public Transform hatchDrop;
    [Tooltip("Wie weit die Klappe aufschwingt (Grad).")]
    public float hatchOpenAngle = 80f;
    [Tooltip("Wie schnell sie aufschwingt (Grad pro Sekunde).")]
    public float hatchSpeed = 150f;
    [Tooltip("Abstand zwischen zwei herausfallenden Stuecken (Sekunden).")]
    public float releaseInterval = 0.35f;

    // Ist die Klappe offen?
    public bool hatchOpen;

    private float angle;
    private float releaseTimer;
    private Quaternion plateBase = Quaternion.identity;
    private bool plateInit;

    protected override void Awake()
    {
        base.Awake();
        RefreshParts();
    }

    public void RefreshParts()
    {
        if (storage == null)
            storage = GetComponentInChildren<ItemStorage>(true);

        if (hatchPlate != null && !plateInit)
        {
            plateBase = hatchPlate.localRotation;
            plateInit = true;
        }
    }

    // Allein stehende Wagen rollen aus (gekuppelte werden vom Zug bewegt).
    protected override void StepTrain(float dt)
    {
        speed = Mathf.MoveTowards(speed, 0f, rollingResistance * dt);
    }

    // ---------- Bodenklappe ----------

    // AUS DEM KOMMANDO.
    public void ToggleHatch()
    {
        hatchOpen = !hatchOpen;
        releaseTimer = 0f;
    }

    void Update()
    {
        RefreshParts();

        // Klappe animieren.
        float target = hatchOpen ? hatchOpenAngle : 0f;

        if (!Mathf.Approximately(angle, target))
        {
            angle = Mathf.MoveTowards(angle, target, hatchSpeed * Time.deltaTime);

            if (hatchPlate != null)
                hatchPlate.localRotation = plateBase * Quaternion.Euler(angle, 0f, 0f);

            if (hatchCollider != null)
                hatchCollider.enabled = angle < 8f;
        }

        // Stuecke herausfallen lassen: nur der Host entscheidet (das Kommando wirkt bei allen).
        if (NetSync.IsClient || !hatchOpen || angle < hatchOpenAngle * 0.7f || storage == null || NetId <= 0)
            return;

        if (storage.Count <= 0)
            return;

        releaseTimer -= Time.deltaTime;

        if (releaseTimer > 0f)
            return;

        releaseTimer = releaseInterval;

        int index = Array.IndexOf(GetComponentsInChildren<ItemStorage>(true), storage);

        if (index < 0)
            return;

        Vector3 at = hatchDrop != null ? hatchDrop.position : transform.position + Vector3.up * 0.2f;
        at += new Vector3(UnityEngine.Random.Range(-0.15f, 0.15f), 0f, UnityEngine.Random.Range(-0.15f, 0.15f));

        NetSync.Submit(NetCommand.StoreTake(2, NetId, index, at, UnityEngine.Random.rotation));
    }

    // ---------- Speichern / Mehrspieler ----------

    // Die Klappe steht als Teil des Zustands im Spielstand und im regelmaessigen Abgleich.
    public void SaveState(StateBag bag)
    {
        bag.SetInt("hatch", hatchOpen ? 1 : 0);
    }

    public void LoadState(StateBag bag)
    {
        hatchOpen = bag.GetInt("hatch", 0) == 1;
    }

    private static string F(float v)
    {
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    // Format: wagon;Kante;Strecke;Richtung;Tempo;Typ;Nummer
    public override string Serialize()
    {
        return "wagon;" + pos.edge + ";" + F(pos.dist) + ";" + pos.dir + ";" + F(speed) + ";" + recipeId + ";" + NetId;
    }

    public override void Deserialize(string[] s)
    {
        if (s.Length >= 5)
        {
            float v;
            float.TryParse(s[4], NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            speed = v;
        }
    }
}
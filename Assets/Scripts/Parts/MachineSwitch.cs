using System;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Etwas, das man anschauen und per Taste bedienen kann.
public interface IInteractable
{
    string GetPrompt();
    void Interact();
}

// Allgemeiner Ein/Aus-Schalter. Kann an Maschinen (Pumpe, Generator, Kessel)
// oder an Sicherungen haengen.
//
// Einrichtung: Leeres Kind-Objekt an die Schalterposition setzen,
// MachineSwitch + Box Collider (Is Trigger) dranhaengen.
// Die Maschine findet den Schalter automatisch.
//
// Der Hebel kann auf zwei Arten animiert werden:
//  A) Ein Objekt (Feld "Lever"): Seine lokale Rotation wird zwischen den
//     Euler-Werten fuer AN und AUS bewegt. Der Drehpunkt ist der Ursprung
//     dieses Objekts.
//  B) Mehrere Teile (Feld "Lever Parts") plus ein Gelenkpunkt (Feld "Hinge"):
//     Alle Teile werden gemeinsam um die Achse des Gelenks gedreht. Die Teile
//     muessen dafuer nirgendwohin verschoben werden. Funktioniert auch mit
//     Teilen aus importierten Modellen.
public class MachineSwitch : MonoBehaviour, IInteractable, ISaveable
{
    [SerializeField] private string label = "@switch.label";
    [SerializeField] private bool isOn = true;

    [Header("A) Hebel als ein Objekt")]
    [SerializeField] private Transform lever;
    [SerializeField] private Vector3 leverEulerOn = new Vector3(-30f, 0f, 0f);
    [SerializeField] private Vector3 leverEulerOff = new Vector3(30f, 0f, 0f);

    [Header("B) Hebel aus mehreren Teilen")]
    [Tooltip("Alle beweglichen Teile. Sie muessen nicht unter dem Gelenk liegen.")]
    [SerializeField] private Transform[] leverParts;
    [Tooltip("Leeres Objekt am Gelenk. Die Position ist der Drehpunkt, " +
             "die Achse kommt aus 'Hinge Axis'. Ohne Eintrag gilt dieses Objekt.")]
    [SerializeField] private Transform hinge;
    [Tooltip("Drehachse im lokalen Raum des Gelenks (X, Y oder Z). " +
             "Bei gewaehltem Schalter zeigt eine gelbe Linie die Achse.")]
    [SerializeField] private Vector3 hingeAxis = Vector3.right;
    [Tooltip("Winkel relativ zur Stellung, in der die Teile im Editor stehen. " +
             "Stehen sie dort in AN-Stellung, ist AN = 0.")]
    [SerializeField] private float hingeAngleOn = -30f;
    [SerializeField] private float hingeAngleOff = 30f;

    [Header("C) Taster / Knopf (versenkt sich, Farbe je Stellung)")]
    [Tooltip("Der bewegliche Knopf. Er wird beim Umschalten nach unten (bzw. in 'Press Offset'-Richtung) versenkt. Leer = keine Bewegung.")]
    [SerializeField] private Transform pressPart;
    [Tooltip("Weg des Knopfes beim Versenken, im lokalen Raum des Knopfes (Meter). " +
             "Zeigt der Knopf nach vorne (Z), ist (0, 0, -0.01) richtig.")]
    [SerializeField] private Vector3 pressOffset = new Vector3(0f, 0f, -0.01f);
    [Tooltip("Aktiv = Knopf ist bei AN versenkt. Aus = bei AUS versenkt.")]
    [SerializeField] private bool sunkWhenOn = true;
    [Tooltip("Dauer der Bewegung/Farbaenderung in Sekunden.")]
    [SerializeField, Min(0.01f)] private float pressSeconds = 0.08f;
    [Tooltip("Teil des Modells, das die Farbe der Stellung zeigt (z. B. Knopf oder Anzeige). Leer = keine Farbe.")]
    [SerializeField] private Renderer colorRenderer;
    [SerializeField] private Color colorOn = new Color(0.2f, 0.85f, 0.25f);
    [SerializeField] private Color colorOff = new Color(0.85f, 0.2f, 0.15f);
    [Tooltip("Farbe zusaetzlich leuchten lassen (Emission).")]
    [SerializeField] private bool glow = true;
    [SerializeField, Min(0f)] private float glowBrightness = 1.5f;

    [Header("Beide Varianten")]
    [SerializeField] private float leverSpeed = 360f;

    [Header("Ereignis (z. B. fuer Sound oder Animation)")]
    [SerializeField] private UnityEvent<bool> onChanged;

    public event Action<bool> Changed;

    public bool IsOn => isOn;

    public string Label
    {
        get { return label; }
        set { label = value; }
    }

    // Zusatztext fuer die Anzeige (z. B. Last einer Sicherung).
    public string Info { get; set; }

    private float hingeCurrent;

    // Taster: 0 = AUS-Stellung, 1 = AN-Stellung (weich nachgefuehrt).
    private float buttonBlend;
    private Vector3 pressRest;
    private MaterialPropertyBlock colorBlock;

    private bool HasButton
    {
        get { return pressPart != null || colorRenderer != null; }
    }

    private bool UseHinge
    {
        get { return leverParts != null && leverParts.Length > 0; }
    }

    private Transform HingeTransform
    {
        get { return hinge != null ? hinge : transform; }
    }

    private void Awake()
    {
        if (pressPart != null)
            pressRest = pressPart.localPosition;

        if (colorRenderer != null && glow)
            colorRenderer.material.EnableKeyword("_EMISSION");

        buttonBlend = isOn ? 1f : 0f;
        ApplyButton();

        if (UseHinge)
        {
            // Die Teile stehen im Editor in der Ausgangsstellung (Winkel 0).
            hingeCurrent = 0f;
            RotateParts(GetHingeTarget());
            hingeCurrent = GetHingeTarget();
        }
        else if (lever != null)
        {
            lever.localRotation = Quaternion.Euler(GetLeverTarget());
        }
    }

    private void Update()
    {
        UpdateButton();

        if (UseHinge)
        {
            float target = GetHingeTarget();

            if (Mathf.Approximately(hingeCurrent, target))
                return;

            float next = Mathf.MoveTowards(
                hingeCurrent, target, leverSpeed * Time.deltaTime
            );

            RotateParts(next - hingeCurrent);
            hingeCurrent = next;
            return;
        }

        if (lever == null)
            return;

        lever.localRotation = Quaternion.RotateTowards(
            lever.localRotation,
            Quaternion.Euler(GetLeverTarget()),
            leverSpeed * Time.deltaTime
        );
    }

    // Knopf und Farbe weich in die aktuelle Stellung bewegen.
    private void UpdateButton()
    {
        if (!HasButton)
            return;

        float target = isOn ? 1f : 0f;

        if (Mathf.Approximately(buttonBlend, target))
            return;

        buttonBlend = Mathf.MoveTowards(buttonBlend, target, Time.deltaTime / pressSeconds);
        ApplyButton();
    }

    private void ApplyButton()
    {
        // Position: je nach Einstellung ist AN oder AUS die versenkte Stellung.
        if (pressPart != null)
        {
            float sunk = sunkWhenOn ? buttonBlend : 1f - buttonBlend;
            pressPart.localPosition = pressRest + pressOffset * sunk;
        }

        // Farbe: stufenlos zwischen den beiden Farben.
        if (colorRenderer != null)
        {
            Color color = Color.Lerp(colorOff, colorOn, buttonBlend);

            if (colorBlock == null)
                colorBlock = new MaterialPropertyBlock();

            colorRenderer.GetPropertyBlock(colorBlock);
            colorBlock.SetColor("_Color", color);
            colorBlock.SetColor("_BaseColor", color);

            if (glow)
                colorBlock.SetColor("_EmissionColor", color * glowBrightness);

            colorRenderer.SetPropertyBlock(colorBlock);
        }
    }

    private Vector3 GetLeverTarget()
    {
        return isOn ? leverEulerOn : leverEulerOff;
    }

    private float GetHingeTarget()
    {
        return isOn ? hingeAngleOn : hingeAngleOff;
    }

    private Vector3 GetHingeAxisWorld()
    {
        Vector3 axis = hingeAxis.sqrMagnitude > 0.0001f
            ? hingeAxis.normalized
            : Vector3.right;

        return HingeTransform.TransformDirection(axis).normalized;
    }

    // Dreht alle Teile um den Gelenkpunkt.
    private void RotateParts(float deltaDegrees)
    {
        if (Mathf.Approximately(deltaDegrees, 0f))
            return;

        Vector3 pivot = HingeTransform.position;
        Vector3 axis = GetHingeAxisWorld();

        foreach (Transform part in leverParts)
        {
            if (part != null)
                part.RotateAround(pivot, axis, deltaDegrees);
        }
    }

    public void SetOn(bool on)
    {
        if (isOn == on)
            return;

        isOn = on;

        if (Changed != null)
            Changed(isOn);

        if (onChanged != null)
            onChanged.Invoke(isOn);
    }

    public void Toggle()
    {
        SetOn(!isOn);
    }

    public string GetPrompt()
    {
        string text = Loc.T("switch.prompt", Loc.Resolve(label),
            Loc.T(isOn ? "common.on" : "common.off"));

        if (!string.IsNullOrEmpty(Info))
            text += "   |   " + Info;

        return text;
    }

    public void Interact()
    {
        Toggle();
    }

    // Zeigt im Editor die Drehachse (gelb), auch durch das Modell hindurch.
    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        if (!UseHinge)
            return;

        Vector3 center = HingeTransform.position;
        Vector3 axis = GetHingeAxisWorld();

        UnityEngine.Rendering.CompareFunction oldZTest = Handles.zTest;
        Color oldColor = Handles.color;

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = new Color(1f, 0.9f, 0.1f, 1f);

        Handles.DrawLine(center - axis * 0.25f, center + axis * 0.25f);
        Handles.DrawWireDisc(center, axis, 0.05f);

        Handles.zTest = oldZTest;
        Handles.color = oldColor;
#endif
    }

    // ---------- Speichern ----------

    public void SaveState(StateBag bag)
    {
        bag.SetBool("on", isOn);
    }

    public void LoadState(StateBag bag)
    {
        SetOn(bag.GetBool("on", isOn));
    }
}
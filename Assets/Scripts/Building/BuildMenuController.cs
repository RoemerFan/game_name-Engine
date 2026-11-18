using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Das Baumenue: Eine Liste von Buttons.
//   - oben die Werkzeuge "Abreissen" und "Umplatzieren"
//   - darunter ein Button pro Maschine aus der Prefab-Liste
// Das BuildingSystem traegt die Prefabs und die Aktionen ein und ruft Initialize() auf.
public class BuildMenuController : MonoBehaviour
{
    public GameObject buildPanel;
    public GameObject[] machinePrefabs;

    // Eine Kategorie im Baumenue.
    [Serializable]
    public class CategoryDef
    {
        [Tooltip("Kennwort (klein, ohne Leerzeichen). Wird im BuildCategory-Skript des Prefabs eingetragen.")]
        public string id = "other";
        [Tooltip("Anzeigename, falls es keinen Textschluessel gibt.")]
        public string displayName = "Sonstiges";
        [Tooltip("Optional: Textschluessel aus den Sprachdateien (z. B. build.cat.power). Hat Vorrang vor dem Anzeigenamen.")]
        public string nameKey = "";
    }

    [Tooltip("Die Kategorien im Baumenue, in dieser Reihenfolge. Eigene Kategorien einfach anhaengen. " +
             "Prefabs mit unbekannter Kategorie landen in 'other'.")]
    public CategoryDef[] categories =
    {
        new CategoryDef { id = "power",      displayName = "Strom & Licht",   nameKey = "build.cat.power" },
        new CategoryDef { id = "production", displayName = "Produktion",      nameKey = "build.cat.production" },
        new CategoryDef { id = "fluids",     displayName = "Wasser & Dampf",  nameKey = "build.cat.fluids" },
        new CategoryDef { id = "transport",  displayName = "Transport",       nameKey = "build.cat.transport" },
        new CategoryDef { id = "structures", displayName = "Bauteile",        nameKey = "build.cat.structures" },
        new CategoryDef { id = "other",      displayName = "Sonstiges",       nameKey = "build.cat.other" }
    };

    // Aktionen, die beim Klick ausgeloest werden (setzt das BuildingSystem).
    public Action<GameObject> onMachineSelected;
    public Action onDemolishSelected;
    public Action onMoveSelected;

    private Font uiFont;

    // Farben der Buttons.
    private readonly Color machineColor = new Color(0.22f, 0.25f, 0.29f);
    private readonly Color demolishColor = new Color(0.55f, 0.20f, 0.18f);
    private readonly Color moveColor = new Color(0.18f, 0.36f, 0.55f);

    private bool initialized;

    // Ist gerade eine Kategorie geoeffnet? (false = Uebersicht mit Werkzeugen und Kategorien)
    private bool inCategory;
    private string openCategory = "other";

    // Farbe der Kategorie-Buttons.
    private readonly Color categoryColor = new Color(0.27f, 0.30f, 0.20f);
    private readonly Color backColor = new Color(0.30f, 0.30f, 0.30f);

    // Steuerung mit Controller: die Buttons in Reihenfolge, ihre Grundfarben
    // und der gerade ausgewaehlte.
    private readonly List<Button> navButtons = new List<Button>();
    private readonly List<Image> navImages = new List<Image>();
    private readonly List<Color> navColors = new List<Color>();
    private int navIndex = -1;
    private bool navMoved;

    void Awake()
    {
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // Bei Sprachwechsel das Menue mit den neuen Texten neu aufbauen.
    void OnEnable()
    {
        GameSettings.Changed += OnSettingsChanged;
    }

    void OnDisable()
    {
        GameSettings.Changed -= OnSettingsChanged;
    }

    void OnSettingsChanged()
    {
        if (initialized)
            Rebuild();
    }

    // Zurueck aus einer Kategorie in die Uebersicht. Liefert true, wenn dabei etwas
    // zu tun war (dann soll das Menue offen bleiben).
    public bool GoBack()
    {
        if (!initialized || !inCategory)
            return false;

        inCategory = false;
        Rebuild();
        return true;
    }

    // Beim Oeffnen des Menues immer mit der Uebersicht beginnen.
    public void ShowOverview()
    {
        if (!initialized)
            return;

        if (inCategory)
        {
            inCategory = false;
            Rebuild();
        }
    }

    // Zu welcher Kategorie gehoert das Prefab? Unbekannte Kennwoerter landen in "other".
    string CategoryOf(GameObject prefab)
    {
        string id = BuildCategory.IdOf(prefab);

        if (categories != null)
        {
            foreach (CategoryDef c in categories)
            {
                if (c != null && c.id != null && c.id.Trim().ToLowerInvariant() == id)
                    return id;
            }
        }

        return "other";
    }

    // Name einer Kategorie (Sprachdatei vor Inspector-Name).
    string CategoryName(CategoryDef c)
    {
        if (!string.IsNullOrEmpty(c.nameKey) && Loc.Has(c.nameKey))
            return Loc.T(c.nameKey);

        return string.IsNullOrEmpty(c.displayName) ? c.id : c.displayName;
    }

    // Steuerung mit Steuerkreuz/Stick und Bestaetigen-Knopf (Kreuz / A / Enter).
    void Update()
    {
        if (!initialized || buildPanel == null || !buildPanel.activeInHierarchy
            || navButtons.Count == 0)
        {
            navIndex = -1;
            navMoved = false;
            return;
        }

        if (navIndex < 0)
            navIndex = 0;

        int step = MenuNav.MoveY != 0 ? -MenuNav.MoveY : MenuNav.MoveX;

        if (step != 0)
        {
            navIndex = Mathf.Clamp(navIndex + step, 0, navButtons.Count - 1);
            navMoved = true;
        }

        bool showSelection = GameInput.UsingGamepad || navMoved;

        // Auswahl heller darstellen.
        for (int i = 0; i < navImages.Count; i++)
        {
            if (navImages[i] == null)
                continue;

            navImages[i].color = (showSelection && i == navIndex)
                ? Color.Lerp(navColors[i], Color.white, 0.4f)
                : navColors[i];
        }

        if (showSelection && MenuNav.Confirm && navButtons[navIndex] != null)
            navButtons[navIndex].onClick.Invoke();
    }

    // Wird vom BuildingSystem aufgerufen: baut das Menue (Uebersicht) neu auf.
    public void Initialize()
    {
        // Namen aus dem Inspector (BuildCategory > Display Name) bekannt machen.
        if (machinePrefabs != null)
        {
            foreach (GameObject prefab in machinePrefabs)
            {
                if (prefab != null)
                    Loc.RegisterMachineName(prefab.name, BuildCategory.DisplayNameOf(prefab));
            }
        }

        inCategory = false;
        initialized = true;
        Rebuild();
    }

    // Baut das gesamte Menue neu auf (Uebersicht oder geoeffnete Kategorie).
    void Rebuild()
    {
        if (buildPanel == null)
        {
            Debug.LogError("BuildMenuController: BuildPanel fehlt!");
            return;
        }

        navButtons.Clear();
        navImages.Clear();
        navColors.Clear();
        navIndex = -1;

        // Vorhandene Menueeintraege entfernen. Zuerst vom Panel loesen, damit sie
        // sofort aus dem Layout verschwinden (Destroy wirkt erst am Bildende).
        for (int i = buildPanel.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = buildPanel.transform.GetChild(i);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }

        // Das Panel ordnet seine Eintraege untereinander an.
        VerticalLayoutGroup layout =
            buildPanel.GetComponent<VerticalLayoutGroup>();

        if (layout == null)
            layout = buildPanel.AddComponent<VerticalLayoutGroup>();

        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Ueberschrift
        CreateLabel(Loc.T("build.title"), 24, FontStyle.Bold, Color.white, 42);

        if (inCategory)
            BuildCategoryPage();
        else
            BuildOverviewPage();
    }

    // Seite 1: Werkzeuge und die Kategorien.
    void BuildOverviewPage()
    {
        CreateLabel(Loc.T("build.tools"), 14, FontStyle.Normal, new Color(0.7f, 0.7f, 0.7f), 22);

        CreateButton(Loc.T("build.demolish"), demolishColor, () =>
        {
            if (onDemolishSelected != null)
                onDemolishSelected();
        });

        CreateButton(Loc.T("build.move"), moveColor, () =>
        {
            if (onMoveSelected != null)
                onMoveSelected();
        });

        if (machinePrefabs == null)
            return;

        CreateLabel(Loc.T("build.categories"), 14, FontStyle.Normal, new Color(0.7f, 0.7f, 0.7f), 22);

        // Fuer jede Kategorie zaehlen, wie viele Teile sie enthaelt (leere werden ausgeblendet).
        if (categories == null)
            return;

        foreach (CategoryDef def in categories)
        {
            if (def == null || string.IsNullOrEmpty(def.id))
                continue;

            string id = def.id.Trim().ToLowerInvariant();
            int count = 0;

            foreach (GameObject prefab in machinePrefabs)
            {
                if (prefab != null && CategoryOf(prefab) == id)
                    count++;
            }

            if (count == 0)
                continue;

            CreateButton(CategoryName(def) + "  (" + count + ")", categoryColor, () =>
            {
                openCategory = id;
                inCategory = true;
                Rebuild();
            });
        }
    }

    // Seite 2: die Teile einer Kategorie.
    void BuildCategoryPage()
    {
        string title = openCategory;

        if (categories != null)
        {
            foreach (CategoryDef def in categories)
            {
                if (def != null && def.id != null && def.id.Trim().ToLowerInvariant() == openCategory)
                    title = CategoryName(def);
            }
        }

        CreateLabel(title, 16, FontStyle.Normal, new Color(0.85f, 0.85f, 0.85f), 26);

        CreateButton("< " + Loc.T("build.back"), backColor, () => GoBack());

        if (machinePrefabs == null)
            return;

        foreach (GameObject prefab in machinePrefabs)
        {
            if (prefab == null || CategoryOf(prefab) != openCategory)
                continue;

            // "prefab" wird fuer den Klick festgehalten.
            GameObject selected = prefab;

            CreateButton(Loc.MachineName(prefab.name), machineColor, () =>
            {
                if (onMachineSelected != null)
                    onMachineSelected(selected);
            });
        }
    }

    // Erstellt eine Textzeile (Ueberschrift oder Abschnittstitel).
    void CreateLabel(string text, int size, FontStyle style, Color color, float height)
    {
        GameObject labelObject = new GameObject(
            "Label_" + text, typeof(RectTransform), typeof(Text)
        );

        labelObject.transform.SetParent(buildPanel.transform, false);

        Text label = labelObject.GetComponent<Text>();
        label.text = text;
        label.font = uiFont;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;

        // Hoehe im Layout festlegen.
        LayoutElement element = labelObject.AddComponent<LayoutElement>();
        element.preferredHeight = height;
    }

    // Erstellt einen Button mit Beschriftung und Klick-Aktion.
    void CreateButton(string text, Color color, Action onClick)
    {
        GameObject buttonObject = new GameObject(
            "Button_" + text,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement)
        );

        buttonObject.transform.SetParent(buildPanel.transform, false);

        // Hintergrundfarbe
        Image image = buttonObject.GetComponent<Image>();
        image.color = color;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        // Feste Hoehe im Layout
        LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
        layout.preferredHeight = 48;
        layout.minHeight = 48;
        layout.flexibleHeight = 0;

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(296, 48);

        // Beschriftung
        GameObject labelObject = new GameObject(
            "Label", typeof(RectTransform), typeof(Text)
        );

        labelObject.transform.SetParent(buttonObject.transform, false);

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8, 4);
        labelRect.offsetMax = new Vector2(-8, -4);

        Text label = labelObject.GetComponent<Text>();
        label.text = text;
        label.font = uiFont;
        label.fontSize = 17;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;

        // Klick: die uebergebene Aktion ausfuehren.
        button.onClick.AddListener(() => onClick());

        navButtons.Add(button);
        navImages.Add(image);
        navColors.Add(color);
    }
}
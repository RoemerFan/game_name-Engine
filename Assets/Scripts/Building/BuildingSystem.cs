using UnityEngine;

public class BuildingSystem : MonoBehaviour
{
    [Header("Building")]
    public GameObject[] machinePrefabs;
    public float rotationStep = 15f;
    public float maxBuildDistance = 20f;

    [Header("Optional Grid")]
    public KeyCode gridToggleKey = KeyCode.G;
    public float gridSize = 1f;
    public int gridRadius = 10;
    public float gridLineHeight = 0.02f;
    public float gridLineWidth = 0.025f;
    public Color gridColor = new Color(0.2f, 0.8f, 1f, 0.65f);
    public KeyCode gridAnchorKey = KeyCode.T;
    public Color anchoredGridColor = new Color(1f, 0.6f, 0.1f, 0.75f);

    [Header("References")]
    public Camera playerCamera;
    public FirstPersonController playerController;
    public BuildMenuController menuController;

    [Header("Bauteile (Platten & Waende)")]
    [Tooltip("Bodenplatten und Mauern (Komponente BuildPiece) rasten an Nachbarn ein.")]
    public bool pieceSnapping = true;
    [Tooltip("Bis zu diesem Abstand (Meter) zur naechsten Platte rastet ein Teil ein.")]
    public float pieceSnapDistance = 2.5f;
    [Tooltip("Platten lassen sich seitlich entlang der Kante in diesen Schritten verschieben (Meter).")]
    public float pieceSnapStep = 0.5f;
    [Tooltip("So viel Hoehenunterschied (Meter) zwischen Zielpunkt und Platte ist fuer das Einrasten erlaubt.")]
    public float pieceSnapHeight = 3f;

    [Tooltip("Gelaende rund um Platten/Waende an die Umgebung anpassen: Rampe zu Huegeln und Senken (folgt der Hoehe des Gelaendes). Im Spiel mit der Taste umschaltbar.")]
    public bool backfillTerrain = false;
    public KeyCode backfillToggleKey = KeyCode.H;
    [Tooltip("So weit (Meter) reicht die Anpassung nach aussen.")]
    public float backfillReach = 4f;
    [Tooltip("Gefaelle der Boeschung: Meter Hoehenverlust pro Meter Abstand (1 = 45 Grad).")]
    public float backfillSlope = 1f;

    private GameObject currentPrefab;

    // Steht die Mauer-Vorschau gerade auf einer Platte? Dann merkt sich das Bausystem
    // die Unterkante dieser Platte (= Hoehe des eingeebneten Bodens).
    private bool previewWallOnSlab;
    private float previewSlabBottomY;

    // Bauteil-Regeln des gewaehlten Prefabs (null = normale Maschine).
    private BuildPiece currentPiece;

    // Regeln des gewaehlten Teils (Boden oder Wand), null = nur Boden.
    private PlacementRules currentRules;
    private GameObject preview;
    private Renderer[] previewRenderers;
    private Material previewMaterial;

    // Umfang der Maschine (ohne Trigger), relativ zum Prefab-Ursprung.
    private Vector3 localBoundsCenter;
    private Vector3 localBoundsExtents;

    private float placementOffset;
    private float currentRotation;
    private bool canPlace;
    private bool gridEnabled;

    private int machinesLayer;

    private GameObject gridObject;
    private Material gridMaterial;

    // Objekt, an dem sich das Gitter ausrichtet (null = Weltgitter).
    private Transform gridAnchor;
    private float gridAnchorY;

    // ----- Abriss- und Umplatzier-Modus -----
    private bool demolishMode;   // true: Linksklick reisst Maschinen ab
    private bool moveMode;       // true: Linksklick nimmt Maschinen zum Umplatzieren auf

    // Maschine, die gerade umgesetzt wird (ist waehrenddessen unsichtbar).
    private GameObject movingMachine;

    // Maschine, auf die gerade gezielt wird, und ihre Hervorhebung.
    private PlacedMachine targetMachine;
    private GameObject highlightObject;
    private Material highlightMaterial;

    private readonly Color demolishColor = new Color(1f, 0.15f, 0.1f, 0.35f);
    private readonly Color moveColor = new Color(0.2f, 0.7f, 1f, 0.35f);

    private readonly Color validColor =
        new Color(0.2f, 1f, 0.2f, 0.5f);

    private readonly Color invalidColor =
        new Color(1f, 0.2f, 0.2f, 0.5f);

    void Start()
    {
        if (playerCamera == null ||
            playerController == null ||
            menuController == null ||
            menuController.buildPanel == null)
        {
            Debug.LogError(
                "BuildingSystem: Referenzen im Inspector fehlen!"
            );

            enabled = false;
            return;
        }

        machinesLayer = LayerMask.NameToLayer("Machines");

        if (machinesLayer < 0)
        {
            Debug.LogError(
                "Bitte zuerst den Layer 'Machines' erstellen!"
            );

            enabled = false;
            return;
        }

        if (gridSize <= 0f)
            gridSize = 1f;

        menuController.machinePrefabs = machinePrefabs;
        menuController.onMachineSelected = SelectMachine;
        menuController.onDemolishSelected = StartDemolish;
        menuController.onMoveSelected = StartMove;
        menuController.Initialize();
        menuController.buildPanel.SetActive(false);

        CreateGrid();
        CreateHighlight();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // Solange das Einstellungsmenue offen ist, wird nichts gebaut.
        if (SettingsMenu.IsOpen)
            return;

        // G schaltet das optionale Raster um.
        if (GameInput.Pressed(GameAction.GridToggle, gridToggleKey) && !menuController.buildPanel.activeSelf)
        {
            gridEnabled = !gridEnabled;

            if (gridObject != null)
                gridObject.SetActive(gridEnabled);
        }

        UpdateGridPosition();

        if (GameInput.Pressed(GameAction.BuildMenu, KeyCode.B))
        {
            if (menuController.buildPanel.activeSelf)
                CloseMenu();
            else
                OpenMenu();
        }

        if (menuController.buildPanel.activeSelf)
        {
            // Zurueck-Knopf am Controller schliesst das Baumenue.
            // Zuerst aus einer Kategorie zurueck, dann das Menue schliessen.
            if (GameInput.Pressed(GameAction.Back) && !menuController.GoBack())
                CloseMenu();

            return;
        }

        HandleGridAnchorSelection();

        // Abriss-Modus: auf Maschinen zielen und abreissen.
        if (demolishMode)
        {
            UpdateDemolish();
            return;
        }

        // Umplatzieren, solange noch keine Maschine aufgenommen wurde.
        if (moveMode && preview == null)
        {
            UpdateMoveSelect();
            return;
        }

        if (preview == null)
            return;

        if (GameInput.Pressed(GameAction.Secondary))
        {
            // Eine aufgenommene Maschine geht zurueck, der Modus bleibt aktiv.
            if (movingMachine != null)
                ClearPreview();
            else
                CancelPlacement();

            return;
        }

        // H schaltet das Anschuetten von Erde um (nur bei Platten und Waenden).
        if (currentPiece != null && GameInput.KeyDown(backfillToggleKey))
            backfillTerrain = !backfillTerrain;

        // E dreht die Maschine nach rechts, Q nach links.
        // Bei gedrueckter Taste wird nach kurzer Pause weitergedreht.
        currentRotation += rotationStep * GetRotationSteps();

        UpdatePreview();

        if (GameInput.Pressed(GameAction.Primary) &&
            Cursor.lockState == CursorLockMode.Locked &&
            canPlace)
        {
            PlaceMachine();
        }
    }

    void OpenMenu()
    {
        CancelPlacement();

        IsMenuOpen = true;

        menuController.ShowOverview();
        menuController.buildPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        playerController.enabled = false;
    }

    void CloseMenu()
    {
        IsMenuOpen = false;

        menuController.buildPanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        playerController.enabled = true;
    }

    void SelectMachine(GameObject prefab)
    {
        currentPrefab = prefab;

        // Normales Bauen: Abriss- und Umplatzier-Modus beenden.
        demolishMode = false;
        moveMode = false;
        HideHighlight();

        CloseMenu();
        CreatePreview();
    }

    void CreatePreview()
    {
        // Nur die alte Vorschau wegraeumen (die Modi bleiben, wie sie sind).
        ClearPreview();

        if (currentPrefab == null)
            return;

        currentRotation = 0f;

        // Merken, ob das Teil an die Wand darf.
        currentRules = currentPrefab.GetComponent<PlacementRules>();
        currentPiece = currentPrefab.GetComponent<BuildPiece>();

        // Am Ursprung ohne Drehung erzeugen, damit die Maﬂe stimmen.
        preview = Instantiate(
            currentPrefab,
            Vector3.zero,
            Quaternion.identity
        );

        preview.name = "MachinePreview";

        // Die Vorschau soll nichts tun (kein Kessel-Update usw.).
        foreach (MonoBehaviour script in
            preview.GetComponentsInChildren<MonoBehaviour>())
        {
            script.enabled = false;
        }

        // Auch Effekte (Licht, Partikel, Ton) ruhen in der Vorschau.
        foreach (Light effectLight in
            preview.GetComponentsInChildren<Light>())
        {
            effectLight.enabled = false;
        }

        foreach (ParticleSystem effectParticles in
            preview.GetComponentsInChildren<ParticleSystem>())
        {
            effectParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );
        }

        foreach (AudioSource effectAudio in
            preview.GetComponentsInChildren<AudioSource>())
        {
            effectAudio.playOnAwake = false;
            effectAudio.Stop();
        }

        // Umfang aus allen festen Collidern berechnen.
        // Trigger (z. B. Kohle-Einwurf) z‰hlen nicht mit.
        bool hasBounds = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

        foreach (Collider col in
            preview.GetComponentsInChildren<Collider>())
        {
            if (!col.isTrigger)
            {
                if (!hasBounds)
                {
                    bounds = col.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(col.bounds);
                }
            }

            // Erst nach dem Auslesen ausschalten.
            col.enabled = false;
        }

        // Nur echte Modell-Renderer einfaerben (keine Partikel oder Linien).
        System.Collections.Generic.List<Renderer> modelRenderers =
            new System.Collections.Generic.List<Renderer>();

        foreach (Renderer rend in
            preview.GetComponentsInChildren<Renderer>())
        {
            if (rend is MeshRenderer || rend is SkinnedMeshRenderer)
                modelRenderers.Add(rend);
        }

        previewRenderers = modelRenderers.ToArray();

        // Ersatz: Wenn es keinen festen Collider gibt, Renderer nutzen.
        if (!hasBounds)
        {
            foreach (Renderer rend in previewRenderers)
            {
                if (!hasBounds)
                {
                    bounds = rend.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rend.bounds);
                }
            }

            Debug.LogWarning(
                "Das Maschinen-Prefab hat keinen festen Collider. " +
                "Es werden die Renderer-Maﬂe verwendet."
            );
        }

        if (hasBounds)
        {
            placementOffset =
                preview.transform.position.y - bounds.min.y;

            localBoundsCenter =
                bounds.center - preview.transform.position;

            localBoundsExtents = bounds.extents;
        }
        else
        {
            placementOffset = 0f;
            localBoundsCenter = Vector3.zero;
            localBoundsExtents = Vector3.one * 0.5f;
        }

        // Alle Teile der Vorschau mit einem Geister-Material versehen.
        if (previewRenderers.Length > 0)
        {
            previewMaterial = new Material(
                previewRenderers[0].sharedMaterial
            );

            previewMaterial.color = validColor;

            foreach (Renderer rend in previewRenderers)
            {
                Material[] mats =
                    new Material[rend.sharedMaterials.Length];

                for (int i = 0; i < mats.Length; i++)
                    mats[i] = previewMaterial;

                rend.sharedMaterials = mats;
            }
        }
        else
        {
            Debug.LogWarning(
                "Das Maschinen-Prefab hat keinen Renderer."
            );
        }
    }

    // Ist der Baumodus gerade aktiv (Menue, Vorschau, Abriss, Umplatzieren)?
    // Andere Skripte (z. B. der Gelaende-Abbau) lesen das, damit sich
    // Mausklicks nicht ueberschneiden.
    public static bool IsBuildingActive;

    // Ist das Baumenue (Auswahlliste) gerade offen?
    public static bool IsMenuOpen;

    // ----- Anpassung ans Gelaende (Voxel-Welt) -----
    [Header("Gelaende")]
    [Tooltip("Unter dem Fundament wird das Gelaende eingeebnet.")]
    public bool flattenTerrain = true;
    [Tooltip("So viel Hoehenunterschied (Meter) darf unter einer Maschine hoechstens eingeebnet werden. Mehr = Bauplatz gesperrt.")]
    public float maxTerrainFlatten = 1.2f;
    [Tooltip("So breit (Meter) geht die Einebnung weich ins Gelaende zurueck.")]
    public float flattenBlend = 1f;

    // Steht die Vorschau gerade auf dem Voxel-Gelaende?
    private bool previewOnTerrain;

    // Wird nach allen Update-Aufrufen gesetzt.
    void LateUpdate()
    {
        IsBuildingActive =
            SettingsMenu.IsOpen ||
            preview != null ||
            demolishMode ||
            moveMode ||
            (menuController != null &&
             menuController.buildPanel != null &&
             menuController.buildPanel.activeSelf);
    }

    void UpdatePreview()
    {
        previewOnTerrain = false;
        previewWallOnSlab = false;

        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(
                Screen.width / 2f,
                Screen.height / 2f
            )
        );

        if (!Physics.Raycast(
            ray,
            out RaycastHit hit,
            maxBuildDistance,
            ~0,
            QueryTriggerInteraction.Ignore))
        {
            canPlace = false;
            SetPreviewColor(invalidColor);
            return;
        }

        // Welche Art Flaeche wurde getroffen, und wo darf das Teil hin?
        BuildSurface surface = hit.collider.GetComponentInParent<BuildSurface>();

        bool wallAllowed = currentRules != null
            && currentRules.surface != PlacementSurface.Ground;

        bool groundAllowed = currentRules == null
            || currentRules.surface != PlacementSurface.Wall;

        // Wandmontage: Das Fadenkreuz zeigt auf eine (senkrechte) Wand.
        if (wallAllowed &&
            surface != null &&
            surface.type == BuildSurfaceType.Wall &&
            Mathf.Abs(hit.normal.y) < 0.3f)
        {
            UpdateWallPreview(hit);
            return;
        }

        // Bodenplatten und Mauern: an Nachbarplatten einrasten.
        if (currentPiece != null && pieceSnapping && groundAllowed && TrySnapPiece(hit))
            return;

        // Mauern, die nur auf Platten duerfen, sind sonst gesperrt.
        if (currentPiece != null &&
            (currentPiece.needsSlab || currentPiece.kind == BuildPieceKind.Floor))
        {
            canPlace = false;
            SetPreviewColor(invalidColor);
            return;
        }

        Vector3 targetPosition =
            hit.point + Vector3.up * placementOffset;

        // Nur im Rastermodus werden Positionen eingerastet.
        if (gridEnabled)
            targetPosition = SnapToGrid(targetPosition);

        // Mit gewaehltem Ankerobjekt dreht sich die Maschine relativ dazu.
        Quaternion previewRotation =
            GetGridRotation()
            * Quaternion.Euler(0f, currentRotation, 0f);

        // Die MITTE der Maschine (nicht ihr Modell-Nullpunkt) kommt auf den
        // Zielpunkt. So liegt sie direkt vor dem Spieler, auch wenn das
        // Modell einen verschobenen Pivot hat.
        Vector3 pivotShift = previewRotation *
            new Vector3(localBoundsCenter.x, 0f, localBoundsCenter.z);

        preview.transform.SetPositionAndRotation(
            targetPosition - pivotShift,
            previewRotation
        );

        // Boden nur, wenn das Teil auf den Boden darf.
        canPlace = groundAllowed && IsGround(hit.collider, surface, hit.normal);

        if (canPlace)
        {
            // Box passend zur Maschine, mitgedreht.
            Vector3 center =
                preview.transform.position
                + preview.transform.rotation * localBoundsCenter;

            Collider[] overlaps = Physics.OverlapBox(
                center,
                localBoundsExtents * 0.95f,
                preview.transform.rotation,
                1 << machinesLayer,
                QueryTriggerInteraction.Ignore
            );

            canPlace = overlaps.Length == 0;
        }

        // Auf dem Voxel-Gelaende: Nur bauen, wenn das Einebnen nicht zu viel waere.
        VoxelWorld terrain = VoxelWorld.Instance;

        if (terrain != null && terrain.OwnsCollider(hit.collider))
        {
            previewOnTerrain = true;

            if (canPlace && flattenTerrain)
            {
                Vector3 center;
                Vector2 half;
                float yaw, baseY;

                GetFootprint(out center, out half, out yaw, out baseY);

                if (terrain.MaxDeviation(center, half, yaw, baseY) > maxTerrainFlatten)
                    canPlace = false;
            }
        }

        SetPreviewColor(
            canPlace ? validColor : invalidColor
        );
    }

    // Grundflaeche der Vorschau: Mitte, halbe Breite/Tiefe, Drehung, Unterkante.
    void GetFootprint(out Vector3 center, out Vector2 half, out float yaw, out float baseY)
    {
        center = preview.transform.position
               + preview.transform.rotation * localBoundsCenter;

        half = new Vector2(localBoundsExtents.x, localBoundsExtents.z);
        yaw = preview.transform.eulerAngles.y;
        baseY = center.y - localBoundsExtents.y;
    }

    // Ebnet das Gelaende unter der Vorschau ein (falls sie auf dem Gelaende steht).
    void FlattenUnderPreview()
    {
        VoxelWorld terrain = VoxelWorld.Instance;

        if (!flattenTerrain || !previewOnTerrain || terrain == null)
            return;

        Vector3 center;
        Vector2 half;
        float yaw, baseY;

        GetFootprint(out center, out half, out yaw, out baseY);

        terrain.FlattenArea(center, half, yaw, baseY, flattenBlend);
    }

    // Boden ist alles mit dem Tag "Ground" oder einer BuildSurface vom Typ Ground.
    bool IsGround(Collider col, BuildSurface surface, Vector3 normal)
    {
        if (col.CompareTag("Ground") ||
            (surface != null && surface.type == BuildSurfaceType.Ground))
            return true;

        // Die Oberseite einer Bodenplatte gilt ebenfalls als Boden.
        BuildPiece piece = col.GetComponentInParent<BuildPiece>();

        return piece != null
            && piece.kind != BuildPieceKind.Wall
            && normal.y > 0.7f;
    }

    // Rastet Platten an Platten und Mauern auf Plattenraendern ein.
    // Liefert true, wenn eingerastet wurde (Vorschau und canPlace sind dann gesetzt).
    bool TrySnapPiece(RaycastHit hit)
    {
        BuildPiece hitPiece = hit.collider.GetComponentInParent<BuildPiece>();

        // Mauer auf Mauer: stapeln (naechste Lage).
        if (currentPiece.kind == BuildPieceKind.Wall &&
            hitPiece != null && hitPiece.kind == BuildPieceKind.Wall)
            return SnapWallOnWall(hitPiece);

        // Etagenboden/Dach: Zielt man auf eine Mauer, liegt er auf deren Oberkante.
        if (currentPiece.kind == BuildPieceKind.Floor &&
            hitPiece != null && hitPiece.kind == BuildPieceKind.Wall)
            return SnapFloorOnWalls(hit, hitPiece);

        // Naechste Platte in Reichweite suchen.
        BuildPiece best = null;
        float bestDistance = pieceSnapDistance;

        foreach (BuildPiece other in BuildPiece.All)
        {
            if (other == null || !IsSnapTarget(other))
                continue;

            if (movingMachine != null && other.gameObject == movingMachine)
                continue;

            if (Mathf.Abs(hit.point.y - other.TopY) > pieceSnapHeight)
                continue;

            float distance = other.HorizontalDistance(hit.point);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = other;
            }
        }

        if (best == null)
            return false;

        // Platte im eigenen Koordinatensystem betrachten (nur die Drehung um die Hochachse zaehlt).
        Quaternion otherRot = Quaternion.Euler(0f, best.transform.eulerAngles.y, 0f);
        Vector3 oc = best.WorldCenter;
        Vector3 oe = best.Extents;
        Vector3 p = Quaternion.Inverse(otherRot) * (hit.point - oc);

        // Naechste Seite: die, ueber die der Zielpunkt am weitesten hinausragt.
        bool xSide = (Mathf.Abs(p.x) - oe.x) > (Mathf.Abs(p.z) - oe.z);
        float sign = xSide ? Mathf.Sign(p.x) : Mathf.Sign(p.z);

        if (sign == 0f)
            sign = 1f;

        Quaternion rotation;
        Vector3 worldCenter;

        if (currentPiece.kind != BuildPieceKind.Wall)
        {
            // Drehung in 90-Grad-Schritten relativ zur Nachbarplatte.
            int quarter = Mathf.RoundToInt(currentRotation / 90f);
            rotation = otherRot * Quaternion.Euler(0f, quarter * 90f, 0f);

            bool swapped = (Mathf.Abs(quarter) % 2) == 1;
            float myX = swapped ? localBoundsExtents.z : localBoundsExtents.x;
            float myZ = swapped ? localBoundsExtents.x : localBoundsExtents.z;

            float step = Mathf.Max(pieceSnapStep, 0.05f);
            Vector3 local;

            if (xSide)
            {
                // Seitlich entlang der Kante verschieben, Kanten bleiben im Raster.
                float z = -oe.z + Mathf.Round((p.z - myZ + oe.z) / step) * step + myZ;
                local = new Vector3(sign * (oe.x + myX), 0f, z);
            }
            else
            {
                float x = -oe.x + Mathf.Round((p.x - myX + oe.x) / step) * step + myX;
                local = new Vector3(x, 0f, sign * (oe.z + myZ));
            }

            // Gleiche Unterkante wie die Nachbarplatte.
            Vector3 c = oc + otherRot * local;
            c.y = best.BottomY + localBoundsExtents.y;
            worldCenter = c;
        }
        else
        {
            // Mauer: lange Seite entlang der Plattenkante, aussen buendig mit der Kante.
            bool longZ = localBoundsExtents.z > localBoundsExtents.x;
            float length = 2f * Mathf.Max(localBoundsExtents.x, localBoundsExtents.z);
            float thickness = 2f * Mathf.Min(localBoundsExtents.x, localBoundsExtents.z);

            Vector3 outward = otherRot * (xSide ? new Vector3(sign, 0f, 0f) : new Vector3(0f, 0f, sign));

            float edgeHalf = xSide ? oe.z : oe.x;
            float along = xSide ? p.z : p.x;

            // Mauerstuecke reihen sich ab der Ecke aneinander.
            float a = 0f;

            if (2f * edgeHalf >= length)
            {
                int count = Mathf.Max(1, Mathf.FloorToInt(2f * edgeHalf / length + 0.01f));
                int k = Mathf.Clamp(Mathf.RoundToInt((along + edgeHalf - length * 0.5f) / length), 0, count - 1);
                a = -edgeHalf + length * 0.5f + k * length;
            }

            float across = (xSide ? oe.x : oe.z) - thickness * 0.5f;

            Vector3 local = xSide
                ? new Vector3(sign * across, 0f, a)
                : new Vector3(a, 0f, sign * across);

            // Die Drehtaste dreht die Mauer um 180 Grad (innen/aussen vertauschen).
            bool flip = (Mathf.Abs(Mathf.RoundToInt(currentRotation / 180f)) % 2) == 1;

            rotation = Quaternion.LookRotation(outward, Vector3.up)
                * Quaternion.Euler(0f, (longZ ? 90f : 0f) + (flip ? 180f : 0f), 0f);

            Vector3 c = oc + otherRot * local;
            c.y = best.TopY + localBoundsExtents.y;
            worldCenter = c;
        }

        // Vorschau so setzen, dass die MITTE des Umfangs auf worldCenter liegt.
        preview.transform.SetPositionAndRotation(
            worldCenter - rotation * localBoundsCenter,
            rotation);

        Vector3 center = preview.transform.position + rotation * localBoundsCenter;
        bool isWall = currentPiece.kind == BuildPieceKind.Wall;

        canPlace = !OverlapsOthers(center, rotation, isWall);

        // Nur Mauern auf einer Bodenplatte am Boden passen das Gelaende an
        // (nicht Mauern auf Etagenboeden in der Luft).
        if (isWall && best.kind == BuildPieceKind.Slab)
        {
            previewWallOnSlab = true;
            previewSlabBottomY = best.BottomY;
        }

        // Platten stehen auf dem Gelaende: dort wird eingeebnet (Mauern stehen auf der Platte).
        VoxelWorld terrain = VoxelWorld.Instance;
        previewOnTerrain = currentPiece.kind == BuildPieceKind.Slab && terrain != null && terrain.IsReady;

        if (canPlace && previewOnTerrain && flattenTerrain)
        {
            Vector3 fc;
            Vector2 half;
            float yaw, baseY;

            GetFootprint(out fc, out half, out yaw, out baseY);

            if (terrain.MaxDeviation(fc, half, yaw, baseY) > maxTerrainFlatten)
                canPlace = false;
        }

        SetPreviewColor(canPlace ? validColor : invalidColor);
        return true;
    }

    // Welche Bauteile dienen als Anschluss fuer das gewaehlte Teil?
    //  - Platte: andere Platten (seitlich)
    //  - Etagenboden: andere Etagenboeden (seitlich)
    //  - Mauer: Platten und Etagenboeden (auf deren Rand)
    bool IsSnapTarget(BuildPiece other)
    {
        if (movingMachine != null && other.gameObject == movingMachine)
            return false;

        switch (currentPiece.kind)
        {
            case BuildPieceKind.Slab:
                return other.kind == BuildPieceKind.Slab;

            case BuildPieceKind.Floor:
                return other.kind == BuildPieceKind.Floor;

            default:
                return other.kind == BuildPieceKind.Slab || other.kind == BuildPieceKind.Floor;
        }
    }

    // Setzt die Vorschau so, dass die MITTE des Umfangs auf worldCenter liegt.
    void PlacePreviewByCenter(Vector3 worldCenter, Quaternion rotation)
    {
        preview.transform.SetPositionAndRotation(
            worldCenter - rotation * localBoundsCenter,
            rotation);
    }

    // Ist die Mauer bereits "belegt"? Sucht die oberste Mauer, die an derselben Stelle
    // (gleiche Mitte, gleiche Richtung) steht wie "wall".
    BuildPiece TopOfWallStack(BuildPiece wall)
    {
        BuildPiece top = wall;
        Vector3 c = wall.WorldCenter;

        foreach (BuildPiece other in BuildPiece.All)
        {
            if (other == null || other.kind != BuildPieceKind.Wall || other == wall)
                continue;

            if (movingMachine != null && other.gameObject == movingMachine)
                continue;

            Vector3 d = other.WorldCenter - c;
            d.y = 0f;

            float yawDelta = Mathf.Abs(Mathf.DeltaAngle(
                other.transform.eulerAngles.y, wall.transform.eulerAngles.y)) % 180f;

            bool sameDirection = yawDelta < 5f || yawDelta > 175f;

            if (d.magnitude < 0.25f && sameDirection && other.TopY > top.TopY)
                top = other;
        }

        return top;
    }

    // Mauer auf Mauer: gleiche Stelle, eine Lage hoeher.
    bool SnapWallOnWall(BuildPiece hitWall)
    {
        BuildPiece below = TopOfWallStack(hitWall);

        // Lange Seite der Mauer darunter und der neuen Mauer vergleichen (X oder Z).
        Vector3 be = below.Extents;
        bool belowLongZ = be.z > be.x;
        bool myLongZ = localBoundsExtents.z > localBoundsExtents.x;

        bool flip = (Mathf.Abs(Mathf.RoundToInt(currentRotation / 180f)) % 2) == 1;

        Quaternion rotation = Quaternion.Euler(0f,
            below.transform.eulerAngles.y
            + (belowLongZ != myLongZ ? 90f : 0f)
            + (flip ? 180f : 0f), 0f);

        Vector3 c = below.WorldCenter;
        c.y = below.TopY + localBoundsExtents.y;

        PlacePreviewByCenter(c, rotation);

        canPlace = !OverlapsOthers(c, rotation, true);
        SetPreviewColor(canPlace ? validColor : invalidColor);
        return true;
    }

    // Etagenboden/Dach auf einem Mauerkranz: gleiche Grundflaeche wie die Platte darunter,
    // Unterkante auf der Oberkante der obersten Mauer. Es ragt nichts in die Mauern, und das
    // Raster stimmt, weil der Boden an der Platte darunter ausgerichtet wird.
    bool SnapFloorOnWalls(RaycastHit hit, BuildPiece hitWall)
    {
        BuildPiece wall = TopOfWallStack(hitWall);
        Vector3 wc = wall.WorldCenter;

        // Tragende Platte/Etagenboden: Die Mauer steht (mit ihrer Mitte) auf ihr.
        BuildPiece support = null;

        foreach (BuildPiece other in BuildPiece.All)
        {
            if (other == null || other == wall)
                continue;

            if (other.kind == BuildPieceKind.Wall)
                continue;

            if (movingMachine != null && other.gameObject == movingMachine)
                continue;

            if (other.HorizontalDistance(wc) > 0.01f)
                continue;

            // Muss unter der Mauer liegen.
            if (other.TopY > wall.BottomY + 0.1f)
                continue;

            if (support == null || other.TopY > support.TopY)
                support = other;
        }

        if (support == null)
            return false;

        Quaternion supportRot = Quaternion.Euler(0f, support.transform.eulerAngles.y, 0f);
        Vector3 sc = support.WorldCenter;
        Vector3 se = support.Extents;
        Vector3 p = Quaternion.Inverse(supportRot) * (hit.point - sc);

        int quarter = Mathf.RoundToInt(currentRotation / 90f);
        Quaternion rotation = supportRot * Quaternion.Euler(0f, quarter * 90f, 0f);

        bool swapped = (Mathf.Abs(quarter) % 2) == 1;
        float myX = swapped ? localBoundsExtents.z : localBoundsExtents.x;
        float myZ = swapped ? localBoundsExtents.x : localBoundsExtents.z;

        float step = Mathf.Max(pieceSnapStep, 0.05f);

        // Innerhalb der Grundflaeche im Raster verschieben (gleich grosser Boden: Mitte).
        float cx = SnapInside(p.x, se.x, myX, step);
        float cz = SnapInside(p.z, se.z, myZ, step);

        Vector3 c = sc + supportRot * new Vector3(cx, 0f, cz);
        c.y = wall.TopY + localBoundsExtents.y;

        PlacePreviewByCenter(c, rotation);

        canPlace = !OverlapsOthers(c, rotation, false);
        previewOnTerrain = false;

        SetPreviewColor(canPlace ? validColor : invalidColor);
        return true;
    }

    // Mitte eines Teils (halbe Groesse "mine") innerhalb einer Flaeche (halbe Groesse "outer"),
    // eingerastet im Raster ab der Kante. Ist das Teil gleich gross oder groesser: Mitte.
    float SnapInside(float aim, float outer, float mine, float step)
    {
        if (mine >= outer - 0.001f)
            return 0f;

        float lo = -outer + mine;
        float hi = outer - mine;

        float snapped = lo + Mathf.Round((aim - lo) / step) * step;
        return Mathf.Clamp(snapped, lo, hi);
    }

    // Ueberschneidet die Box eine andere Maschine? Mauern duerfen sich an Ecken
    // ueberlappen, aber nicht genau aufeinander stehen.
    bool OverlapsOthers(Vector3 center, Quaternion rotation, bool isWall)
    {
        Collider[] overlaps = Physics.OverlapBox(
            center,
            localBoundsExtents * 0.95f,
            rotation,
            1 << machinesLayer,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider col in overlaps)
        {
            if (isWall)
            {
                BuildPiece piece = col.GetComponentInParent<BuildPiece>();

                if (piece != null && piece.kind == BuildPieceKind.Wall &&
                    Vector3.Distance(piece.WorldCenter, center) > 0.1f)
                    continue;
            }

            return true;
        }

        return false;
    }

    // Setzt die Vorschau an eine Wand: Die Rueckseite der Maschine liegt an
    // der Wand, die Vorderseite zeigt von der Wand weg.
    void UpdateWallPreview(RaycastHit hit)
    {
        // Wandrichtung waagerecht machen, damit die Maschine nicht kippt.
        Vector3 normal = hit.normal;
        normal.y = 0f;
        normal.Normalize();

        Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);

        // Der Punkt der Maschine, der auf dem Zielpunkt an der Wand liegen soll:
        // seitlich und in der Hoehe die Mitte, in der Tiefe die Rueckseite.
        Vector3 anchor = new Vector3(
            localBoundsCenter.x,
            localBoundsCenter.y,
            localBoundsCenter.z - localBoundsExtents.z
        );

        preview.transform.rotation = rotation;
        preview.transform.position = hit.point - rotation * anchor;

        // Platz-Pruefung wie am Boden: Ueberlappt eine andere Maschine?
        Vector3 center =
            preview.transform.position + rotation * localBoundsCenter;

        Collider[] overlaps = Physics.OverlapBox(
            center,
            localBoundsExtents * 0.95f,
            rotation,
            1 << machinesLayer,
            QueryTriggerInteraction.Ignore
        );

        canPlace = overlaps.Length == 0;

        SetPreviewColor(canPlace ? validColor : invalidColor);
    }

    void SetPreviewColor(Color color)
    {
        if (previewMaterial != null)
            previewMaterial.color = color;
    }

    // True, solange eine Maschine zum Umplatzieren getragen wird.
    public bool IsCarrying
    {
        get { return movingMachine != null; }
    }

    // Sucht ein Maschinen-Prefab anhand seines Namens (fuer Spielstaende).
    public GameObject FindPrefab(string prefabName)
    {
        if (machinePrefabs == null)
            return null;

        foreach (GameObject prefab in machinePrefabs)
        {
            if (prefab != null && prefab.name == prefabName)
                return prefab;
        }

        return null;
    }

    // Baut eine Maschine aus einem Spielstand: wie PlaceMachine, aber ohne
    // Vorschau und ohne das Gelaende einzuebnen (das ist schon gespeichert).
    public GameObject SpawnSavedMachine(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        GameObject machine = Instantiate(prefab, position, rotation);
        machine.name = prefab.name;

        SetLayerRecursively(machine, machinesLayer);

        PlacedMachine placed = machine.AddComponent<PlacedMachine>();
        placed.Prefab = prefab;

        return machine;
    }

    void PlaceMachine()
    {
        // Umplatzieren: Die vorhandene Maschine wird versetzt statt neu
        // gebaut, damit ihr Zustand (Inhalt, Schalter ...) erhalten bleibt.
        if (movingMachine != null)
        {
            FinishMove();
            return;
        }

        // Bauen laeuft als Kommando: Im Mehrspieler entsteht die Maschine bei
        // allen Spielern (SpawnSavedMachine erledigt Ebene, PlacedMachine ...).
        NetCommand cmd = NetCommand.Place(
            currentPrefab.name,
            preview.transform.position,
            preview.transform.rotation);

        AttachFlatten(cmd);
        NetSync.Submit(cmd);
    }

    // Traegt ein, wie das Gelaende unter der Vorschau eingeebnet werden soll
    // (auf allen Rechnern gleich).
    void AttachFlatten(NetCommand cmd)
    {
        VoxelWorld terrain = VoxelWorld.Instance;

        if (terrain == null)
            return;

        Vector3 center;
        Vector2 half;
        float yaw, baseY;

        GetFootprint(out center, out half, out yaw, out baseY);

        // Anpassung an die Umgebung (Taste H): nur fuer Platten/Waende, die auf dem Gelaende stehen.
        // Mauern auf einer Platte zaehlen mit: Die Erde wird auch an ihnen angeschuettet.
        bool shape = backfillTerrain && currentPiece != null && backfillReach > 0f
            && (previewOnTerrain || previewWallOnSlab);

        if (flattenTerrain && previewOnTerrain)
        {
            cmd.flatten = true;
            cmd.flatCenter = center;
            cmd.flatHalf = half;
            cmd.flatYaw = yaw;
            cmd.flatBase = baseY;

            // Mit Anpassung uebernimmt die Boeschung den weichen Uebergang.
            cmd.flatBlend = shape ? 0.01f : flattenBlend;
        }

        if (shape)
        {
            cmd.flatCenter = center;
            cmd.flatHalf = half;
            cmd.flatYaw = yaw;

            // Niveau am Rand = eingeebnete Hoehe der Flaeche (bei Mauern auf einer Platte
            // die Hoehe des Bodens unter der Platte).
            cmd.fillTop = previewWallOnSlab ? previewSlabBottomY : baseY;
            cmd.fillReach = backfillReach;
            cmd.fillSlope = backfillSlope;
        }
    }

    void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;

        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    // ----- Dauerdrehen bei gehaltener Taste -----
    public float rotateRepeatDelay = 0.35f;    // Pause bis das Wiederholen startet
    public float rotateRepeatInterval = 0.07f; // Zeit zwischen den weiteren Schritten
    private float rotateHoldTimer;

    // Gibt zurueck, wie viele Schritte JETZT gedreht werden:
    // +1 (rechts, E), -1 (links, Q) oder 0. Beim ersten Druck sofort,
    // danach erst nach der Pause und dann im festen Takt.
    int GetRotationSteps()
    {
        int direction = 0;

        if (GameInput.Held(GameAction.RotateRight, KeyCode.E)) direction += 1;
        if (GameInput.Held(GameAction.RotateLeft, KeyCode.Q)) direction -= 1;

        // Keine Taste (oder beide gleichzeitig): Zaehler zuruecksetzen.
        if (direction == 0)
        {
            rotateHoldTimer = 0f;
            return 0;
        }

        // Frisch gedrueckt: sofort ein Schritt, dann Pause abwarten.
        if (GameInput.Pressed(GameAction.RotateRight, KeyCode.E) || GameInput.Pressed(GameAction.RotateLeft, KeyCode.Q))
        {
            rotateHoldTimer = -rotateRepeatDelay;
            return direction;
        }

        // Gehalten: Zeit sammeln, pro Intervall ein weiterer Schritt.
        rotateHoldTimer += Time.deltaTime;

        int steps = 0;

        while (rotateHoldTimer >= rotateRepeatInterval)
        {
            rotateHoldTimer -= rotateRepeatInterval;
            steps++;
        }

        return steps * direction;
    }

    // Beendet die laufende Vorschau. Eine aufgenommene Maschine wird
    // dabei an ihren alten Platz zurueckgestellt.
    void ClearPreview()
    {
        if (movingMachine != null)
        {
            movingMachine.SetActive(true);
            movingMachine = null;
        }

        if (preview != null)
            Destroy(preview);

        preview = null;
        previewRenderers = null;

        if (previewMaterial != null)
            Destroy(previewMaterial);

        previewMaterial = null;
        canPlace = false;
    }

    // Beendet alles: Vorschau, Abriss- und Umplatzier-Modus.
    // (Wird auch vom PipeBuilder aufgerufen.)
    public void CancelPlacement()
    {
        demolishMode = false;
        moveMode = false;
        HideHighlight();
        ClearPreview();
    }

    // ---------- Abriss- und Umplatzier-Modus ----------

    // Wird vom Baumenue-Button "Abreissen" aufgerufen.
    void StartDemolish()
    {
        CancelPlacement();
        demolishMode = true;
        CloseMenu();
    }

    // Wird vom Baumenue-Button "Umplatzieren" aufgerufen.
    void StartMove()
    {
        CancelPlacement();
        moveMode = true;
        CloseMenu();
    }

    // Abriss-Modus: Ziel rot markieren, per Linksklick abreissen.
    void UpdateDemolish()
    {
        // Rechtsklick beendet den Modus.
        if (GameInput.Pressed(GameAction.Secondary))
        {
            CancelPlacement();
            return;
        }

        PlacedMachine machine = FindTargetMachine();
        SetTarget(machine, demolishColor);

        if (machine != null &&
            GameInput.Pressed(GameAction.Primary) &&
            Cursor.lockState == CursorLockMode.Locked)
        {
            Demolish(machine);
        }
    }

    // Umplatzieren, Schritt 1: Ziel blau markieren, per Linksklick aufnehmen.
    void UpdateMoveSelect()
    {
        if (GameInput.Pressed(GameAction.Secondary))
        {
            CancelPlacement();
            return;
        }

        PlacedMachine machine = FindTargetMachine();
        SetTarget(machine, moveColor);

        if (machine != null &&
            GameInput.Pressed(GameAction.Primary) &&
            Cursor.lockState == CursorLockMode.Locked)
        {
            StartCarry(machine);
        }
    }

    // Sucht die gebaute Maschine, auf die das Fadenkreuz zeigt.
    // Trigger (Schalter, Einwuerfe, Ports) werden dabei ignoriert.
    PlacedMachine FindTargetMachine()
    {
        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(Screen.width / 2f, Screen.height / 2f)
        );

        if (!Physics.Raycast(
            ray,
            out RaycastHit hit,
            maxBuildDistance,
            ~0,
            QueryTriggerInteraction.Ignore))
        {
            return null;
        }

        return hit.collider.GetComponentInParent<PlacedMachine>();
    }

    // Reisst eine Maschine ab.
    void Demolish(PlacedMachine machine)
    {
        // Abreissen laeuft als Kommando (alle Spieler sehen es).
        NetSync.Submit(NetCommand.Demolish(machine.NetId));
    }

    // Fuehrt den Abriss aus (wird vom NetSync auf allen Rechnern aufgerufen).
    public void ExecuteDemolish(PlacedMachine machine)
    {
        // Zuerst alle angeschlossenen Leitungen und Kabel entfernen.
        RemoveConnectedPipes(machine.gameObject);

        Debug.Log("Abgerissen: " + machine.name);

        HideHighlight();
        Destroy(machine.gameObject);
    }

    // Entfernt alle Leitungen und Kabel, die an Ports der Maschine haengen.
    void RemoveConnectedPipes(GameObject machine)
    {
        foreach (Port port in machine.GetComponentsInChildren<Port>(true))
        {
            if (port.Cable != null)
                Destroy(port.Cable.gameObject);
        }
    }

    // Umplatzieren, Schritt 2: Die Maschine wird aufgenommen. Ein Geisterbild
    // folgt dem Fadenkreuz, die echte Maschine ist solange unsichtbar.
    void StartCarry(PlacedMachine machine)
    {
        if (machine.Prefab == null)
            return;

        HideHighlight();

        // Das Geisterbild entsteht aus dem Prefab der Maschine.
        currentPrefab = machine.Prefab;
        CreatePreview();

        if (preview == null)
            return;

        // Das Geisterbild startet mit der Drehung der Maschine.
        currentRotation = Mathf.DeltaAngle(
            GetGridRotation().eulerAngles.y,
            machine.transform.eulerAngles.y
        );

        // Original verstecken. Beim Absetzen wird es versetzt, bei einem
        // Abbruch (Rechtsklick) wieder an seinen Platz zurueckgestellt.
        movingMachine = machine.gameObject;
        movingMachine.SetActive(false);
    }

    // Umplatzieren, Schritt 3: Maschine an der Stelle des Geisterbilds absetzen.
    void FinishMove()
    {
        GameObject machine = movingMachine;

        // Auf null setzen, damit ClearPreview sie nicht zurueckstellt.
        movingMachine = null;

        // Umplatzieren laeuft als Kommando (alle Spieler sehen es). Die alten
        // Leitungen entfernt ExecuteMove, die Maschine wird dort wieder sichtbar.
        PlacedMachine placed = machine.GetComponent<PlacedMachine>();

        NetCommand cmd = NetCommand.Move(
            placed != null ? placed.NetId : 0,
            preview.transform.position,
            preview.transform.rotation);

        AttachFlatten(cmd);

        // Maschine ohne Nummer (sollte nicht vorkommen): lokal versetzen.
        if (placed == null || placed.NetId <= 0)
            ExecuteMove(placed, preview.transform.position, preview.transform.rotation);
        else
            NetSync.Submit(cmd);

        // Geisterbild weg. Der Umplatzier-Modus bleibt aktiv.
        ClearPreview();
    }

    // Fuehrt das Umplatzieren aus (wird vom NetSync auf allen Rechnern aufgerufen).
    public void ExecuteMove(PlacedMachine machine, Vector3 position, Quaternion rotation)
    {
        if (machine == null)
            return;

        // Die alten Leitungen passen nicht mehr zur neuen Position.
        RemoveConnectedPipes(machine.gameObject);

        machine.transform.SetPositionAndRotation(position, rotation);
        machine.gameObject.SetActive(true);
    }

    // ---------- Hervorhebung ----------

    // Merkt sich das Ziel und zeigt (oder versteckt) die Hervorhebung.
    void SetTarget(PlacedMachine machine, Color color)
    {
        if (machine == targetMachine)
            return;

        if (machine == null)
        {
            HideHighlight();
            return;
        }

        targetMachine = machine;
        ShowHighlight(machine, color);
    }

    // Erstellt das (zunaechst versteckte) durchsichtige Hervorhebungs-Kaestchen.
    void CreateHighlight()
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return;

        highlightObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        highlightObject.name = "MachineHighlight";

        // Nur zur Anzeige: kein Collider, kein Schatten.
        Destroy(highlightObject.GetComponent<Collider>());

        highlightMaterial = new Material(shader);

        Renderer rend = highlightObject.GetComponent<Renderer>();
        rend.sharedMaterial = highlightMaterial;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        highlightObject.SetActive(false);
    }

    // Legt das Kaestchen passend um die Maschine.
    void ShowHighlight(PlacedMachine machine, Color color)
    {
        if (highlightObject == null)
            return;

        Transform t = machine.transform;

        Bounds local;

        // Notloesung, falls die Maschine kein Modell hat.
        if (!TryGetLocalBounds(t, out local))
            local = new Bounds(Vector3.zero, Vector3.one);

        // Etwas groesser als die Maschine, damit es sichtbar umschliesst.
        Vector3 size = local.size + Vector3.one * 0.06f;

        highlightObject.transform.SetPositionAndRotation(
            t.TransformPoint(local.center),
            t.rotation
        );

        highlightObject.transform.localScale =
            Vector3.Scale(size, t.lossyScale);

        highlightMaterial.color = color;
        highlightObject.SetActive(true);
    }

    void HideHighlight()
    {
        targetMachine = null;

        if (highlightObject != null)
            highlightObject.SetActive(false);
    }

    // Berechnet den Umfang aller Modell-Teile im Koordinatensystem der Maschine.
    // Dazu werden die acht Ecken der Bounds jedes Mesh-Teils umgerechnet.
    bool TryGetLocalBounds(Transform machine, out Bounds result)
    {
        bool found = false;
        result = new Bounds(Vector3.zero, Vector3.zero);

        foreach (MeshFilter filter in machine.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null)
                continue;

            Bounds mb = filter.sharedMesh.bounds;

            for (int i = 0; i < 8; i++)
            {
                // Jede Ecke der Bounds: (+/-1, +/-1, +/-1) mal die halbe Groesse.
                Vector3 sign = new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f
                );

                Vector3 corner = mb.center + Vector3.Scale(mb.extents, sign);

                // Mesh-Raum -> Welt -> Raum der Maschine.
                Vector3 inMachine = machine.InverseTransformPoint(
                    filter.transform.TransformPoint(corner)
                );

                if (!found)
                {
                    result = new Bounds(inMachine, Vector3.zero);
                    found = true;
                }
                else
                {
                    result.Encapsulate(inMachine);
                }
            }
        }

        return found;
    }

    // ---------- Hinweis-Anzeige ----------

    void OnGUI()
    {
        // Waehrend das Menue offen ist, wird nichts angezeigt.
        if (menuController == null ||
            menuController.buildPanel == null ||
            menuController.buildPanel.activeSelf)
            return;

        string text = null;

        if (demolishMode)
        {
            text = Loc.T("build.hint.demolish");

            if (targetMachine != null)
                text += "\n" + Loc.T("build.target", Loc.MachineName(targetMachine.name));
        }
        else if (moveMode && movingMachine == null)
        {
            text = Loc.T("build.hint.moveselect");

            if (targetMachine != null)
                text += "\n" + Loc.T("build.target", Loc.MachineName(targetMachine.name));
        }
        else if (moveMode)
        {
            text = Loc.T("build.hint.movecarry");
        }

        // Hinweis fuer Platten/Waende: Erde anschuetten an/aus.
        if (text == null && preview != null && currentPiece != null && movingMachine == null)
        {
            text = Loc.T(backfillTerrain ? "build.backfill.on" : "build.backfill.off",
                         GameInput.Hint(GameAction.None, backfillToggleKey));
        }

        if (text == null)
            return;

        GameSettings.DrawBoxBottomCenter(text);
    }

    // Erstellt das sichtbare Bodengitter.
    void CreateGrid()
    {
        gridObject = new GameObject("BuildGrid");
        gridObject.transform.SetParent(transform);
        gridObject.SetActive(false);

        Shader gridShader = Shader.Find("Sprites/Default");

        if (gridShader == null)
        {
            Debug.LogError(
                "Raster-Shader wurde nicht gefunden."
            );
            return;
        }

        gridMaterial = new Material(gridShader);
        gridMaterial.color = gridColor;

        int lineCount = gridRadius * 2 + 1;
        float extent = gridRadius * gridSize;

        for (int i = 0; i < lineCount; i++)
        {
            float offset = (i - gridRadius) * gridSize;

            // Linien parallel zur Z-Achse.
            CreateGridLine(
                new Vector3(offset, 0f, -extent),
                new Vector3(offset, 0f, extent)
            );

            // Linien parallel zur X-Achse.
            CreateGridLine(
                new Vector3(-extent, 0f, offset),
                new Vector3(extent, 0f, offset)
            );
        }
    }

    void CreateGridLine(Vector3 start, Vector3 end)
    {
        GameObject lineObject =
            new GameObject("GridLine");

        lineObject.transform.SetParent(
            gridObject.transform,
            false
        );

        LineRenderer line =
            lineObject.AddComponent<LineRenderer>();

        line.useWorldSpace = false;
        line.positionCount = 2;

        line.SetPosition(0, start);
        line.SetPosition(1, end);

        line.startWidth = gridLineWidth;
        line.endWidth = gridLineWidth;

        line.sharedMaterial = gridMaterial;
        line.startColor = gridColor;
        line.endColor = gridColor;

        line.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;

        line.receiveShadows = false;
    }

    // Das Gitter folgt dem Spieler und bleibt am Weltraster ausgerichtet.
    void UpdateGridPosition()
    {
        if (gridObject == null || playerCamera == null)
            return;

        if (!gridEnabled)
            return;

        Vector3 origin = GetGridOrigin();
        Quaternion rotation = GetGridRotation();

        // Spielerposition im Koordinatensystem des Gitters.
        Vector3 local =
            Quaternion.Inverse(rotation)
            * (playerCamera.transform.position - origin);

        local.x = Mathf.Floor(local.x / gridSize) * gridSize;
        local.y = 0f;
        local.z = Mathf.Floor(local.z / gridSize) * gridSize;

        Vector3 position = origin + rotation * local;
        position.y = origin.y + gridLineHeight;

        gridObject.transform.SetPositionAndRotation(
            position,
            rotation
        );
    }

    // Ist ein Ankerobjekt gewaehlt (und das Gitter an), gilt dessen
    // Position und Drehung um die Y-Achse als Gitterursprung.
    bool UseAnchor
    {
        get { return gridEnabled && gridAnchor != null; }
    }

    Vector3 GetGridOrigin()
    {
        if (!UseAnchor)
            return Vector3.zero;

        // X/Z vom Pivot, Hoehe von der Unterkante der Maschine.
        return new Vector3(
            gridAnchor.position.x,
            gridAnchorY,
            gridAnchor.position.z
        );
    }

    Quaternion GetGridRotation()
    {
        return UseAnchor
            ? Quaternion.Euler(0f, gridAnchor.eulerAngles.y, 0f)
            : Quaternion.identity;
    }

    // Rastet eine Weltposition am (ggf. ausgerichteten) Gitter ein.
    Vector3 SnapToGrid(Vector3 position)
    {
        Vector3 origin = GetGridOrigin();
        Quaternion rotation = GetGridRotation();

        Vector3 local =
            Quaternion.Inverse(rotation) * (position - origin);

        local.x = Mathf.Round(local.x / gridSize) * gridSize;
        local.z = Mathf.Round(local.z / gridSize) * gridSize;

        return origin + rotation * local;
    }

    // Taste T: auf eine platzierte Maschine schauen = Gitter daran
    // ausrichten. Auf etwas anderes schauen = zurueck zum Weltgitter.
    void HandleGridAnchorSelection()
    {
        if (!gridEnabled || !GameInput.Pressed(GameAction.GridAnchor, gridAnchorKey))
            return;

        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(
                Screen.width / 2f,
                Screen.height / 2f
            )
        );

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            maxBuildDistance,
            ~0,
            QueryTriggerInteraction.Ignore))
        {
            Transform root = hit.collider.transform.root;

            if (root.gameObject.layer == machinesLayer)
            {
                SetGridAnchor(root);
                return;
            }
        }

        SetGridAnchor(null);
    }

    void SetGridAnchor(Transform anchor)
    {
        gridAnchor = anchor;

        if (anchor != null)
            gridAnchorY = GetBottomY(anchor);

        SetGridColor(
            anchor != null ? anchoredGridColor : gridColor
        );

        Debug.Log(
            anchor != null
                ? "Gitter ausgerichtet an: " + anchor.name
                : "Gitter: Weltausrichtung"
        );
    }

    // Tiefster Punkt aller festen Collider (Trigger zaehlen nicht).
    float GetBottomY(Transform machine)
    {
        bool found = false;
        float minY = machine.position.y;

        foreach (Collider col in
            machine.GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger || !col.enabled)
                continue;

            if (!found || col.bounds.min.y < minY)
                minY = col.bounds.min.y;

            found = true;
        }

        return minY;
    }

    void SetGridColor(Color color)
    {
        if (gridObject == null)
            return;

        foreach (LineRenderer line in
            gridObject.GetComponentsInChildren<LineRenderer>(true))
        {
            line.startColor = color;
            line.endColor = color;
        }
    }

    void OnDestroy()
    {
        if (highlightMaterial != null)
            Destroy(highlightMaterial);

        if (highlightObject != null)
            Destroy(highlightObject);

        if (previewMaterial != null)
            Destroy(previewMaterial);

        if (gridMaterial != null)
            Destroy(gridMaterial);

        if (gridObject != null)
            Destroy(gridObject);
    }
}
using System.Collections.Generic;
using UnityEngine;

// Auf den Spieler (oder das BuildingSystem-Objekt) haengen.
public class PipeBuilder : MonoBehaviour
{
    [Header("Bedienung")]
    [SerializeField] private KeyCode toggleKey = KeyCode.L;
    [SerializeField] private KeyCode removeKey = KeyCode.X;
    [SerializeField] private float reachDistance = 6f;

    [Tooltip("Wie weit ein Port hinter einer festen Flaeche noch " +
             "anvisierbar ist (Ports sitzen oft direkt an der Huelle).")]
    [SerializeField] private float portTolerance = 0.3f;

    [Header("Leitung")]
    [SerializeField] private float pipeRadius = 0.05f;
    [SerializeField] private float cableRadius = 0.02f;
    [Tooltip("Maximale Leistung eines Kabels in kW (gilt fuer neu gebaute Kabel).")]
    [SerializeField] private float cableRatingKW = 50f;
    [Tooltip("Nach wie vielen Sekunden brennt ein Kabel bei doppelter Last durch?")]
    [SerializeField] private float cableTripSecondsAtDouble = 6f;
    [Tooltip("Wie stark Stromkabel durchhaengen: Meter Durchhang pro Meter waagerechter " +
             "Strecke. 0 = starr gespannt.")]
    [SerializeField] private float cableSagPerMeter = 0.08f;
    [Tooltip("Groesster Durchhang eines Kabels in Metern.")]
    [SerializeField] private float cableMaxSag = 1.5f;
    [Header("Duenne Kabel (kleine Geraete, z. B. Lampen)")]
    [SerializeField] private float smallCableRadius = 0.008f;
    [Tooltip("Maximale Leistung eines duennen Kabels in kW.")]
    [SerializeField] private float smallCableRatingKW = 2f;
    [SerializeField] private float smallStubLength = 0.1f;
    [Tooltip("Durchhang duenner Kabel (Meter pro Meter Strecke).")]
    [SerializeField] private float smallCableSagPerMeter = 0.06f;

    [Header("Leitung (Form)")]
    [SerializeField] private float stubLength = 0.3f;
    [Tooltip("Wie weit die Kurven ausholen: klein = enge Kurve, gross = weiter Bogen.")]
    [Range(0.1f, 0.8f)]
    [SerializeField] private float bendStrength = 0.4f;

    [Header("Referenzen")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private BuildingSystem buildingSystem;

    private bool pipeMode;
    private Port firstPort;
    private Port hoveredPort;

    private LineRenderer previewLine;
    private Material previewMaterial;

    private readonly List<Pipe> pipes = new List<Pipe>();

    private void Start()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (buildingSystem == null)
            buildingSystem = FindObjectOfType<BuildingSystem>();

        if (playerCamera == null)
        {
            Debug.LogError("PipeBuilder: Kamera fehlt!", this);
            enabled = false;
            return;
        }

        CreatePreviewLine();
    }

    private void Update()
    {
        // Im Einstellungsmenue steuert das Steuerkreuz das Menue.
        if (SettingsMenu.IsOpen)
            return;

        if (GameInput.Pressed(GameAction.PipeMode, toggleKey) && !IsBuildMenuOpen())
            SetPipeMode(!pipeMode);

        if (!pipeMode)
            return;

        // Wird das Baumenue geoeffnet, endet der Leitungsmodus.
        if (IsBuildMenuOpen())
        {
            SetPipeMode(false);
            return;
        }

        Vector3 aimPoint;
        hoveredPort = FindPortUnderCrosshair(out aimPoint);

        if (GameInput.Pressed(GameAction.PipeRemove, removeKey) &&
            hoveredPort != null && hoveredPort.IsConnected)
        {
            int removeId, removeIndex;

            if (NetSync.DescribePort(hoveredPort, out removeId, out removeIndex))
                NetSync.Submit(NetCommand.PipeRemove(removeId, removeIndex));
            else
                TryRemovePipe(hoveredPort);
        }

        if (GameInput.Pressed(GameAction.Secondary))
        {
            // Erst Auswahl abbrechen, danach den Modus verlassen.
            if (firstPort != null)
                firstPort = null;
            else
                SetPipeMode(false);

            return;
        }

        if (GameInput.Pressed(GameAction.Primary) &&
            Cursor.lockState == CursorLockMode.Locked)
        {
            HandleClick();
        }

        UpdatePreviewLine(aimPoint);
    }

    private bool IsBuildMenuOpen()
    {
        return buildingSystem != null
            && buildingSystem.menuController != null
            && buildingSystem.menuController.buildPanel != null
            && buildingSystem.menuController.buildPanel.activeSelf;
    }

    private void SetPipeMode(bool on)
    {
        pipeMode = on;
        firstPort = null;
        hoveredPort = null;

        if (previewLine != null)
            previewLine.gameObject.SetActive(false);

        // Eine laufende Maschinen-Vorschau waere im Weg (Linksklick).
        if (on && buildingSystem != null)
            buildingSystem.CancelPlacement();
    }

    // Sucht den Port, auf den das Fadenkreuz zeigt.
    private Port FindPortUnderCrosshair(out Vector3 aimPoint)
    {
        Ray ray = playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            reachDistance,
            ~0,
            QueryTriggerInteraction.Collide
        );

        System.Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance)
        );

        Port found = null;
        float portDistance = 0f;
        float solidDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            Port port = hit.collider.GetComponent<Port>();

            if (port != null)
            {
                if (found == null)
                {
                    found = port;
                    portDistance = hit.distance;
                }

                continue;
            }

            // Feste Flaechen (keine Trigger) verdecken Ports dahinter.
            if (!hit.collider.isTrigger && hit.distance < solidDistance)
                solidDistance = hit.distance;
        }

        if (found != null && portDistance > solidDistance + portTolerance)
            found = null;

        if (found != null)
            aimPoint = found.transform.position;
        else if (solidDistance < float.MaxValue)
            aimPoint = ray.GetPoint(solidDistance);
        else
            aimPoint = ray.GetPoint(reachDistance);

        return found;
    }

    private void HandleClick()
    {
        if (hoveredPort == null)
            return;

        // Erster Port.
        if (firstPort == null)
        {
            if (!hoveredPort.IsConnected)
                firstPort = hoveredPort;

            return;
        }

        // Nochmal derselbe Port = Auswahl aufheben.
        if (hoveredPort == firstPort)
        {
            firstPort = null;
            return;
        }

        // Zweiter Port: Verbinden laeuft als Kommando (alle Spieler sehen die Leitung).
        if (firstPort.CanConnect(hoveredPort))
        {
            int idA, portA, idB, portB;

            if (NetSync.DescribePort(firstPort, out idA, out portA) &&
                NetSync.DescribePort(hoveredPort, out idB, out portB))
            {
                NetSync.Submit(NetCommand.PipeAdd(idA, portA, idB, portB));
            }
            else if (Port.TryConnect(firstPort, hoveredPort))
            {
                // Nicht zuzuordnen: nur lokal verlegen.
                CreatePipe(firstPort, hoveredPort);
            }

            firstPort = null;
        }
    }

    // Stellt eine Leitung aus einem Spielstand wieder her.
    public bool RestorePipe(Port a, Port b)
    {
        if (!Port.TryConnect(a, b))
        {
            // Zwei noch unbestimmte Universal-Ports: Die Leitung zwischen ihnen
            // kann erst gebaut werden, wenn einer ueber eine andere gespeicherte
            // Leitung seinen Stoff bekommen hat. Sie wartet deshalb kurz.
            if (a != null && b != null &&
                a.Medium == PortMedium.Any && b.Medium == PortMedium.Any &&
                !a.IsConnected && !b.IsConnected)
            {
                deferred.Add(new Port[] { a, b });
                deferredTime = Time.unscaledTime;
                return true;
            }

            return false;
        }

        CreatePipe(a, b);
        RetryDeferred(false);
        return true;
    }

    // Wartende Leitungen (siehe RestorePipe).
    private readonly List<Port[]> deferred = new List<Port[]>();
    private float deferredTime;

    private void RetryDeferred(bool force)
    {
        bool progress = true;

        while (progress && deferred.Count > 0)
        {
            progress = false;

            for (int i = deferred.Count - 1; i >= 0; i--)
            {
                Port x = deferred[i][0];
                Port y = deferred[i][1];

                if (x == null || y == null || x.IsConnected || y.IsConnected)
                {
                    deferred.RemoveAt(i);
                    continue;
                }

                if (Port.TryConnect(x, y, force))
                {
                    CreatePipe(x, y);
                    deferred.RemoveAt(i);
                    progress = true;
                }
            }
        }
    }

    private void LateUpdate()
    {
        // Wurde der Stoff nach einer Sekunde immer noch nicht bestimmt (z. B. eine
        // Verteilerkette ohne Quelle), wird die Leitung trotzdem wiederhergestellt.
        if (deferred.Count > 0 && Time.unscaledTime - deferredTime > 1f)
            RetryDeferred(true);
    }

    private void CreatePipe(Port a, Port b)
    {
        GameObject pipeObject = new GameObject("Pipe_" + a.Medium);
        Pipe pipe = pipeObject.AddComponent<Pipe>();

        bool small = a.Medium == PortMedium.Power && a.CableSize == PortCableSize.Small;

        float radius =
            small ? smallCableRadius :
            a.Medium == PortMedium.Power ? cableRadius : pipeRadius;

        // Die Leitung wird als weich gebogenes Rohr gebaut.
        // Nur Kabel haengen durch, Rohre bleiben starr.
        float sag =
            small ? smallCableSagPerMeter :
            a.Medium == PortMedium.Power ? cableSagPerMeter : 0f;

        pipe.Build(a, b, radius, small ? smallStubLength : stubLength,
            GetColor(a.Medium), bendStrength, sag, cableMaxSag);

        // Jeder Port merkt sich seine Leitung, damit sie beim Abriss oder
        // Umplatzieren der Maschine mit entfernt werden kann.
        a.Cable = pipe;
        b.Cable = pipe;

        // Nur Strom-Kabel haben eine Leistungsgrenze.
        if (a.Medium == PortMedium.Power)
            pipe.ConfigureCable(small ? smallCableRatingKW : cableRatingKW, cableTripSecondsAtDouble);

        pipes.Add(pipe);
    }

    // Entfernt die Leitung an diesem Port (vom NetSync auf allen Rechnern aufgerufen).
    public void RemovePipeAt(Port port)
    {
        TryRemovePipe(port);
    }

    private void TryRemovePipe(Port port)
    {
        if (port == null || !port.IsConnected)
            return;

        pipes.RemoveAll(p => p == null);

        for (int i = 0; i < pipes.Count; i++)
        {
            Pipe pipe = pipes[i];

            if (pipe.PortA == port || pipe.PortB == port)
            {
                // Sofort trennen, die Leitung selbst verschwindet danach.
                port.Disconnect();
                Destroy(pipe.gameObject);
                pipes.RemoveAt(i);
                return;
            }
        }
    }

    private static Color GetColor(PortMedium medium)
    {
        switch (medium)
        {
            case PortMedium.Water:
                return new Color(0.2f, 0.45f, 0.9f);
            case PortMedium.Steam:
                return new Color(0.82f, 0.82f, 0.86f);
            case PortMedium.Power:
                return new Color(0.95f, 0.75f, 0.1f);
            case PortMedium.Melt:
                return new Color(1f, 0.45f, 0.1f);
            default:
                return new Color(0.2f, 0.18f, 0.17f);
        }
    }

    // ---------- Vorschau-Linie ----------

    private void CreatePreviewLine()
    {
        GameObject lineObject = new GameObject("PipePreviewLine");
        lineObject.transform.SetParent(transform, false);

        previewLine = lineObject.AddComponent<LineRenderer>();
        previewLine.useWorldSpace = true;
        previewLine.positionCount = 2;
        previewLine.startWidth = 0.03f;
        previewLine.endWidth = 0.03f;

        previewLine.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;

        previewLine.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            previewMaterial = new Material(shader);
            previewLine.sharedMaterial = previewMaterial;
        }

        lineObject.SetActive(false);
    }

    private void UpdatePreviewLine(Vector3 aimPoint)
    {
        if (previewLine == null)
            return;

        if (firstPort == null)
        {
            previewLine.gameObject.SetActive(false);
            return;
        }

        previewLine.gameObject.SetActive(true);

        Color color = Color.white;

        if (hoveredPort != null && hoveredPort != firstPort)
        {
            color = firstPort.CanConnect(hoveredPort)
                ? new Color(0.2f, 1f, 0.2f)
                : new Color(1f, 0.2f, 0.2f);
        }

        previewLine.startColor = color;
        previewLine.endColor = color;

        previewLine.SetPosition(0, firstPort.transform.position);
        previewLine.SetPosition(1, aimPoint);
    }

    // ---------- Anzeige ----------

    private void OnGUI()
    {
        if (!pipeMode)
            return;

        string text =
            Loc.T("pipe.hint", GameInput.Hint(GameAction.PipeMode, toggleKey), GameInput.Hint(GameAction.PipeRemove, removeKey)) + "\n" +
            Loc.T(firstPort == null ? "pipe.hint.first" : "pipe.hint.second");

        if (hoveredPort != null)
        {
            text += "\n" + Describe(hoveredPort);
        }

        GameSettings.DrawBoxBottomCenter(text);
    }

    private static string Describe(Port port)
    {
        string state = Loc.T(port.IsConnected ? "port.occupied" : "port.free");

        // Universal-Port, an dem noch nichts angesteckt ist.
        if (port.Medium == PortMedium.Any)
            return Loc.T("pipe.port.any", state);

        string medium = Loc.T(
            port.Medium == PortMedium.Water ? "medium.water" :
            port.Medium == PortMedium.Steam ? "medium.steam" :
            port.Medium == PortMedium.Power ? "medium.power" :
            port.Medium == PortMedium.Melt ? "medium.melt" : "medium.soot");

        string direction = Loc.T(
            port.Direction == PortDirection.Input ? "port.input" :
            port.Direction == PortDirection.Output ? "port.output" : "port.universal");

        if (port.Medium == PortMedium.Power)
        {
            // Strom-Anschluesse sind Stecker: Ein- und Ausgang zugleich.
            string info = Loc.T("pipe.port.power",
                medium + " - " + Loc.T("port.both")
                + (port.CableSize == PortCableSize.Small ? " (" + Loc.T("port.cable.small") + ")" : ""),
                state);

            if (port.Cable != null && port.Cable.RatingKW > 0f)
            {
                info = Loc.T(
                    "pipe.port.cable",
                    info,
                    port.Cable.LoadKW.ToString("F1"),
                    port.Cable.RatingKW.ToString("F0"));
            }

            return info;
        }

        return Loc.T("pipe.port.flow", medium, direction, state);
    }

    private void OnDestroy()
    {
        if (previewMaterial != null)
            Destroy(previewMaterial);
    }
}
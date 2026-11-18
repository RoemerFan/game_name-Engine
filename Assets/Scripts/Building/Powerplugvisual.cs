using UnityEngine;

// Zeigt an jedem Strom-Anschluss (Power-Port) eine Steckdose bzw. einen Stecker.
//
// Wird vom Port beim Start automatisch hinzugefuegt. Du musst also nichts an
// den Prefabs aendern.
//
//  - Anschluss frei:    dunkle Steckdose mit zwei Schlitzen.
//  - Anschluss belegt:  ein Stecker steckt drin (Gehaeuse mit gelbem Ring),
//                       das Kabel kommt hinten heraus.
//
// Es werden bewusst KEINE Collider erzeugt, damit weder das Anvisieren der
// Ports noch das Platzieren der Maschinen beeinflusst wird.
[DisallowMultipleComponent]
public class PowerPlugVisual : MonoBehaviour
{
    // Masse in Metern (Richtung "vorne" = blaue Z-Achse des Ports).
    private const float SocketRadius = 0.055f;
    private const float SocketHeight = 0.012f;

    private const float PlugRadius = 0.04f;
    private const float PlugLength = 0.10f;

    // Das schwarze Gehaeuse endet etwas vor dem Ring (statt buendig),
    // damit es nicht durch den gelben Ring hindurchschaut.
    private const float BodyLength = 0.085f;
    private const float CollarRadius = 0.046f;
    private const float CollarLength = 0.02f;

    private Port port;
    private GameObject socketRoot;
    private GameObject plugRoot;

    // Merkt sich, bei welchem Verbindungsstand die Anzeige zuletzt stimmte.
    private int shownVersion = -1;

    // Haengt die Anzeige an einen Strom-Port (falls noch nicht vorhanden).
    public static void Attach(Port p)
    {
        // Strom-Ports, und universelle Ports (die erst spaeter Strom werden koennen).
        if (p == null ||
            (p.RawMedium != PortMedium.Power && p.RawMedium != PortMedium.Any))
            return;

        if (p.GetComponent<PowerPlugVisual>() == null)
            p.gameObject.AddComponent<PowerPlugVisual>();
    }

    private void Awake()
    {
        port = GetComponent<Port>();

        BuildSocket();
        BuildPlug();

        // Duenne Kabel: kleinere Steckdose und kleiner Stecker.
        if (port != null && port.CableSize == PortCableSize.Small)
        {
            socketRoot.transform.localScale = Vector3.one * 0.45f;
            plugRoot.transform.localScale = Vector3.one * 0.45f;
        }

        Refresh();
    }

    private void Update()
    {
        // Port.ConnectionVersion zaehlt hoch, sobald irgendwo ein Kabel
        // gesteckt oder gezogen wird. Nur dann muss neu entschieden werden.
        if (shownVersion != Port.ConnectionVersion)
            Refresh();
    }

    // Zeigt je nach Zustand die Steckdose oder den Stecker.
    private void Refresh()
    {
        shownVersion = Port.ConnectionVersion;

        bool connected = port != null && port.IsConnected;

        // Universelle Ports zeigen nur dann Steckdose/Stecker, wenn sie gerade Strom sind.
        bool isPower = port != null && port.Medium == PortMedium.Power;

        if (socketRoot != null)
            socketRoot.SetActive(isPower && !connected);

        if (plugRoot != null)
            plugRoot.SetActive(isPower && connected);
    }

    // ---------- Steckdose (Anschluss frei) ----------

    private void BuildSocket()
    {
        socketRoot = new GameObject("PowerSocket");
        socketRoot.transform.SetParent(transform, false);

        // Runde dunkle Platte, leicht vor der Huelle.
        CreatePart(
            socketRoot.transform, PrimitiveType.Cylinder, "Plate",
            new Vector3(0f, 0f, SocketHeight * 0.5f),
            new Vector3(SocketRadius * 2f, SocketHeight * 0.5f, SocketRadius * 2f),
            true,
            new Color(0.12f, 0.12f, 0.13f));

        // Zwei helle Schlitze, in die spaeter die Stifte des Steckers kommen.
        for (int i = -1; i <= 1; i += 2)
        {
            CreatePart(
                socketRoot.transform, PrimitiveType.Cube, "Slot",
                new Vector3(i * 0.02f, 0f, SocketHeight + 0.001f),
                new Vector3(0.008f, 0.03f, 0.002f),
                false,
                new Color(0.75f, 0.75f, 0.78f));
        }
    }

    // ---------- Stecker (Anschluss belegt) ----------

    private void BuildPlug()
    {
        plugRoot = new GameObject("PowerPlug");
        plugRoot.transform.SetParent(transform, false);

        // Schwarzes Steckergehaeuse.
        CreatePart(
            plugRoot.transform, PrimitiveType.Cylinder, "Body",
            new Vector3(0f, 0f, BodyLength * 0.5f),
            new Vector3(PlugRadius * 2f, BodyLength * 0.5f, PlugRadius * 2f),
            true,
            new Color(0.10f, 0.10f, 0.11f));

        // Gelber Ring (Farbe der Stromkabel), damit man den Stecker gut sieht.
        CreatePart(
            plugRoot.transform, PrimitiveType.Cylinder, "Collar",
            new Vector3(0f, 0f, PlugLength - CollarLength * 0.5f),
            new Vector3(CollarRadius * 2f, CollarLength * 0.5f, CollarRadius * 2f),
            true,
            new Color(0.95f, 0.75f, 0.10f));
    }

    // Erstellt ein Teil aus einem Standard-Koerper.
    //  localPos / size:  Position und Groesse relativ zum Port.
    //  alongForward:     true = Zylinder wird so gedreht, dass seine Laenge
    //                    entlang der Port-Richtung (Z) zeigt.
    private static void CreatePart(
        Transform parent, PrimitiveType type, string name,
        Vector3 localPos, Vector3 size, bool alongForward, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;

        // Collider sofort entfernen (siehe Kommentar oben).
        Collider col = part.GetComponent<Collider>();

        if (col != null)
            Destroy(col);

        Transform t = part.transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;

        // Ein Unity-Zylinder liegt standardmaessig entlang der Y-Achse.
        // Mit 90 Grad um X zeigt er stattdessen entlang Z (nach vorne).
        t.localRotation = alongForward
            ? Quaternion.Euler(90f, 0f, 0f)
            : Quaternion.identity;

        t.localScale = size;

        // Farbe ueber einen PropertyBlock setzen: So brauchen wir kein
        // eigenes Material (und keinen Shader, der im Build fehlen koennte).
        Renderer rend = part.GetComponent<Renderer>();

        if (rend != null)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            rend.SetPropertyBlock(block);
            rend.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
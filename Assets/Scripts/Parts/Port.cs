using UnityEngine;

public enum PortMedium
{
    Water,
    Steam,
    Soot,
    Power,

    // Universell: Der Port nimmt den Stoff an, der an einer Seite des Bauteils
    // angesteckt wird (siehe UniversalJunction). Steht am Ende der Liste,
    // damit die Werte bestehender Prefabs gleich bleiben.
    Any,

    // Fluessiges Metall (Schmelze). Anders als Wasser hat sie eine Metallart und eine
    // Temperatur, deshalb gibt es dafuer die Schnittstelle IMeltOwner (siehe Metals/MetalRegistry).
    // Schmelze-Ports passen nicht an universelle Ports.
    Melt
}

public enum PortDirection
{
    Input,
    Output,

    // Beidseitig: Anschluss ist Ein- UND Ausgang zugleich, wie ein Stecker.
    // Gilt automatisch fuer alle Strom-Ports (siehe Port.Direction).
    // Steht am Ende der Liste, damit gespeicherte Prefab-Werte gleich bleiben.
    Both
}

// Kabelstaerke eines Strom-Ports. Duenne Kabel sind fuer kleine Geraete gedacht (Lampen,
// Schalter): Sie sind duenner, haben eine kleine Leistungsgrenze und passen nur an
// Ports derselben Staerke.
public enum PortCableSize
{
    Normal,
    Small
}

// Wird von Maschinen implementiert, die Stoffe (Wasser, Dampf, Ruß) austauschen.
public interface IPortOwner
{
    // Ausgang: Wie viel kann gerade abgegeben werden?
    float GetAvailable(Port port);

    // Eingang: Wie viel Platz ist noch frei?
    float GetFreeSpace(Port port);

    // Entnimmt bis zu "amount" und gibt die tatsaechliche Menge zurueck.
    float TakeFrom(Port port, float amount);

    // Nimmt bis zu "amount" auf und gibt die angenommene Menge zurueck.
    float GiveTo(Port port, float amount);
}

// An ein leeres Kind-Objekt der Maschine haengen (Anschlussstelle).
// Die blaue Z-Achse (forward) zeigt in die Richtung, in die spaeter
// die Leitung abgeht.
//
// Power-Ports sind Steckverbindungen: Sie sind immer Ein- und Ausgang
// zugleich (PortDirection.Both), egal was im Inspector steht. Zwei beliebige
// Stecker passen also zusammen, wie bei einem echten Kabel. Es fliesst nichts
// ueber den Port selbst: Das Stromnetz (PowerGrid) wertet nur aus,
// welche Geraete ueber Kabel zusammenhaengen.
// An jedem Strom-Port erscheint automatisch eine Steckdose bzw. ein Stecker
// (siehe PowerPlugVisual).
public class Port : MonoBehaviour
{
    [SerializeField] private PortMedium medium = PortMedium.Water;
    [SerializeField] private PortDirection direction = PortDirection.Input;

    [Tooltip("Nur fuer Strom: Normal = dickes Kabel fuer Maschinen, Small = duennes Kabel fuer " +
             "kleine Geraete (Lampen). Nur gleiche Staerken lassen sich verbinden.")]
    [SerializeField] private PortCableSize cableSize = PortCableSize.Normal;

    [Tooltip("Nur fuer Ausgaenge: maximale Menge pro Sekunde.")]
    [SerializeField] private float maxFlowPerSecond = 5f;

    private IPortOwner owner;
    private UniversalJunction junction;

    // Der Stoff dieses Ports. Bei universellen Ports (Any) ist es der Stoff, der am
    // Bauteil angesteckt ist, oder "Any", solange noch nichts angesteckt wurde.
    public PortMedium Medium =>
        medium != PortMedium.Any
            ? medium
            : (junction != null ? junction.CurrentMedium : PortMedium.Any);

    // Der im Inspector eingestellte Stoff (bei universellen Ports immer "Any").
    public PortMedium RawMedium => medium;
    public bool IsAdaptive => medium == PortMedium.Any;
    public UniversalJunction Junction => junction;
    // Strom-Ports sind immer beidseitig; bei Wasser/Dampf/Russ gilt der Inspector-Wert.
    public PortDirection Direction =>
        (medium == PortMedium.Power || medium == PortMedium.Any)
            ? PortDirection.Both
            : direction;
    // Kabelstaerke (nur bei Strom-Ports von Bedeutung).
    public PortCableSize CableSize => cableSize;
    public IPortOwner Owner => owner;
    public Port Connected { get; private set; }
    public bool IsConnected => Connected != null;

    // Das Kabel, das an diesem Power-Port haengt (vom PipeBuilder gesetzt).
    public Pipe Cable { get; set; }

    // Zaehlt hoch, sobald irgendwo eine Verbindung entsteht oder getrennt wird.
    public static int ConnectionVersion { get; private set; }

    private void Reset()
    {
        // Kleiner Trigger, damit man den Port spaeter anvisieren kann.
        if (GetComponent<Collider>() == null)
        {
            SphereCollider col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.15f;
        }
    }

    private void Awake()
    {
        owner = GetComponentInParent<IPortOwner>();

        if (medium == PortMedium.Any)
            junction = GetComponentInParent<UniversalJunction>();

        // Strom-Ports (und universelle Ports, die Strom werden koennen)
        // bekommen eine sichtbare Steckdose / einen Stecker.
        if (medium == PortMedium.Power || medium == PortMedium.Any)
            PowerPlugVisual.Attach(this);

        if (owner == null && medium != PortMedium.Power && medium != PortMedium.Any)
        {
            Debug.LogWarning(
                "Port hat keine Maschine als Besitzer!",
                this
            );
        }
    }

    private void Update()
    {
        // Strom fliesst nicht ueber Ports, das erledigt das PowerGrid.
        PortMedium current = Medium;

        if (current == PortMedium.Melt)
        {
            PushMelt();
            return;
        }

        if (current == PortMedium.Power || current == PortMedium.Any)
            return;

        // Nur Ausgaenge (und universelle Ports) schieben Material in die
        // verbundene Leitung/Maschine.
        bool pushes = direction == PortDirection.Output || medium == PortMedium.Any;

        if (!pushes ||
            Connected == null ||
            owner == null)
            return;

        Port target = Connected;

        if (target.owner == null)
            return;

        float amount = maxFlowPerSecond * Time.deltaTime;
        amount = Mathf.Min(amount, owner.GetAvailable(this));
        amount = Mathf.Min(amount, target.owner.GetFreeSpace(target));

        if (amount <= 0f)
            return;

        float taken = owner.TakeFrom(this, amount);
        target.owner.GiveTo(target, taken);
    }

    // Schmelze: Ein Ausgang schiebt fluessiges Metall (mit Metallart und Temperatur)
    // in den verbundenen Eingang. Beide Maschinen muessen IMeltOwner sein.
    private void PushMelt()
    {
        if (direction != PortDirection.Output || Connected == null || owner == null)
            return;

        IMeltOwner source = owner as IMeltOwner;
        Port target = Connected;
        IMeltOwner sink = target.owner as IMeltOwner;

        if (source == null || sink == null)
            return;

        string metal = source.GetMelt(this);

        if (string.IsNullOrEmpty(metal) || !sink.CanAcceptMelt(target, metal))
            return;

        float amount = maxFlowPerSecond * Time.deltaTime;
        amount = Mathf.Min(amount, source.GetAvailable(this));
        amount = Mathf.Min(amount, sink.GetFreeSpace(target));

        if (amount <= 0f)
            return;

        float temperature = source.GetMeltTemp(this);
        float taken = source.TakeFrom(this, amount);

        if (taken > 0f)
            sink.GiveMelt(target, metal, taken, temperature);
    }

    // Pruefen, ob zwei Ports verbunden werden duerfen.
    public bool CanConnect(Port other)
    {
        if (other == null
            || other == this
            || IsConnected
            || other.IsConnected
            || transform.root == other.transform.root)
            return false;

        PortMedium mine = Medium;
        PortMedium theirs = other.Medium;

        // Zwei noch unbestimmte Universal-Ports: Es ist unklar, was das fuer eine
        // Leitung werden soll. Erst muss eine Seite einen festen Stoff haben.
        if (mine == PortMedium.Any && theirs == PortMedium.Any)
            return false;

        // Zwei feste Stoffe muessen gleich sein. Ein unbestimmter Port nimmt
        // den Stoff der Gegenseite an.
        if (mine != PortMedium.Any && theirs != PortMedium.Any && mine != theirs)
            return false;

        PortMedium result = mine != PortMedium.Any ? mine : theirs;

        // Schmelze passt nur an Schmelze (ein universeller Port kennt keine Metallart).
        if (result == PortMedium.Melt && (mine == PortMedium.Any || theirs == PortMedium.Any))
            return false;

        // Strom ist immer beidseitig. Bei Wasser/Dampf/Russ muss Ein- an Ausgang
        // passen; beidseitige (universelle) Ports passen an alles.
        if (result == PortMedium.Power)
            return cableSize == other.cableSize;

        return Direction == PortDirection.Both
            || other.Direction == PortDirection.Both
            || Direction != other.Direction;
    }

    // "force" = Regeln ignorieren (nur fuer das Wiederherstellen gespeicherter Leitungen).
    public static bool TryConnect(Port a, Port b, bool force = false)
    {
        if (a == null)
            return false;

        if (force)
        {
            if (b == null || a == b || a.IsConnected || b.IsConnected)
                return false;
        }
        else if (!a.CanConnect(b))
        {
            return false;
        }

        a.Connected = b;
        b.Connected = a;
        ConnectionVersion++;
        return true;
    }

    public void Disconnect()
    {
        if (Connected == null)
            return;

        Port other = Connected;
        Connected = null;
        Cable = null;
        other.Cable = null;

        if (other.Connected == this)
            other.Connected = null;

        ConnectionVersion++;
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private void OnDrawGizmos()
    {
        switch (medium)
        {
            case PortMedium.Water:
                Gizmos.color = new Color(0.2f, 0.5f, 1f);
                break;
            case PortMedium.Steam:
                Gizmos.color = new Color(0.9f, 0.9f, 0.95f);
                break;
            case PortMedium.Power:
                Gizmos.color = new Color(1f, 0.85f, 0.1f);
                break;
            default:
                Gizmos.color = new Color(0.45f, 0.35f, 0.3f);
                break;
        }

        Gizmos.DrawSphere(transform.position, 0.08f);

        Gizmos.DrawLine(
            transform.position,
            transform.position + transform.forward * 0.3f
        );
    }
}
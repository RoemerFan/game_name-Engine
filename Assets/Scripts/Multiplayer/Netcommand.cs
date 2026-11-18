using System.IO;
using UnityEngine;

// Marker fuer Bedienstellen, deren Interact() selbst schon ein Netzwerk-Kommando schickt
// (z. B. Weichenhebel, Drehscheibe). NetSync.Interact ruft sie nur lokal auf, damit das Kommando
// nicht bei jedem Mitspieler noch einmal ausgeloest wird.
public interface ISelfNetworked { }

// Alle Aktionen, die die gemeinsame Welt veraendern, laufen als Kommando.
// Im Einzelspiel wird es sofort ausgefuehrt. Im Mehrspieler schickt ein
// Mitspieler es an den Host, der es ausfuehrt und an alle verteilt. So sehen
// alle Spieler dieselbe Welt.
public enum NetCommandType : byte
{
    None = 0,
    Place = 1,        // Maschine bauen
    Demolish = 2,     // Maschine abreissen
    Move = 3,         // Maschine umplatzieren
    PipeAdd = 4,      // Rohr/Kabel verlegen
    PipeRemove = 5,   // Rohr/Kabel entfernen
    Dig = 6,          // Gelaende abbauen
    Interact = 7,     // Schalter, Hebel ... bedienen
    AddCoal = 8,      // Kohle in einen Kessel werfen
    AddFuel = 9,      // Diesel in einen Generator fuellen
    AddCharge = 10,   // Kohle/Erz in einen Hochofen oder festes Metall in eine Giesserei werfen

    // Eisenbahn
    TrackBuild = 11,  // Gleisstueck bauen
    TrackRemove = 12, // Gleisstueck entfernen
    LocoSpawn = 13,   // Lok aufs Gleis setzen
    LocoRemove = 14,  // Lok entfernen
    LocoCoal = 15,    // Kohle in die Feuerbuechse einer Lok werfen
    LocoFuel = 16,    // Diesel in den Tender einer Lok fuellen
    StoreAdd = 17,    // Stueck in ein Lager (Schubkarre, Tender ...) aufnehmen
    StoreTake = 18,   // Stueck aus einem Lager herausnehmen
    BoomDrive = 19,   // Teleskop-Foerderband neigen / ausfahren
    SwitchSet = 20,   // Weiche umlegen
    TurntablePlace = 21,   // Drehscheibe aufstellen
    TurntableRotate = 22,  // Bruecke auf einen Ausgang drehen
    TurntableRemove = 23,  // Drehscheibe abbauen
    Couple = 24,           // zwei Fahrzeuge zusammenkuppeln
    Uncouple = 25,         // Fahrzeug abkuppeln
    CarFunction = 26,      // Funktion an einem Fahrzeug (0 = Bodenklappe umschalten)
    TrackShaft = 27,       // Entladeschacht in ein Gleis bauen
    TrackShaftRemove = 28, // Entladeschacht entfernen
    HoseAction = 29        // Schlauch: nehmen, ablegen, anschliessen, abziehen
}

// Ein Kommando. Es werden immer alle Felder uebertragen (klein genug);
// welche gelten, haengt vom Typ ab.
public class NetCommand
{
    public NetCommandType type;
    public int sender;          // wer es ausgeloest hat (0 = Host)
    public int netId;           // betroffene Maschine
    public string prefab = "";
    public Vector3 position;    // Ort (Bauen, Umplatzieren, Graben)
    public Quaternion rotation = Quaternion.identity;

    // Gelaende einebnen (beim Bauen und Umplatzieren)
    public bool flatten;
    public Vector3 flatCenter;
    public Vector2 flatHalf;
    public float flatYaw;
    public float flatBase;
    public float flatBlend;

    // Angeschuettete Erde rund um die Flaeche (fillReach 0 = aus).
    public float fillTop;
    public float fillReach;
    public float fillSlope;

    // Rohre: beide Anschluesse als Maschine + Nummer des Ports
    public int idA, portA, idB, portB;

    public float radius;        // Grabradius
    public int index;           // Nummer der Bedienstelle (Interact)
    public float amount;        // Menge (Diesel)

    // ---------- Erzeugen ----------

    public static NetCommand Place(string prefab, Vector3 position, Quaternion rotation)
    {
        return new NetCommand
        {
            type = NetCommandType.Place,
            prefab = prefab,
            position = position,
            rotation = rotation
        };
    }

    public static NetCommand Demolish(int netId)
    {
        return new NetCommand { type = NetCommandType.Demolish, netId = netId };
    }

    public static NetCommand Move(int netId, Vector3 position, Quaternion rotation)
    {
        return new NetCommand
        {
            type = NetCommandType.Move,
            netId = netId,
            position = position,
            rotation = rotation
        };
    }

    public static NetCommand PipeAdd(int idA, int portA, int idB, int portB)
    {
        return new NetCommand
        {
            type = NetCommandType.PipeAdd,
            idA = idA, portA = portA, idB = idB, portB = portB
        };
    }

    public static NetCommand PipeRemove(int id, int port)
    {
        return new NetCommand { type = NetCommandType.PipeRemove, idA = id, portA = port };
    }

    public static NetCommand Dig(Vector3 center, float radius)
    {
        return new NetCommand { type = NetCommandType.Dig, position = center, radius = radius };
    }

    public static NetCommand Interact(int netId, int index)
    {
        return new NetCommand { type = NetCommandType.Interact, netId = netId, index = index };
    }

    public static NetCommand AddCoal(int netId)
    {
        return new NetCommand { type = NetCommandType.AddCoal, netId = netId };
    }

    public static NetCommand AddFuel(int netId, float liters)
    {
        return new NetCommand { type = NetCommandType.AddFuel, netId = netId, amount = liters };
    }

    // kind = ItemKind als Zahl (im Feld "index"), metal = Metall-ID (im Feld "prefab"),
    // mass = Gewicht in kg (im Feld "amount", nur fuer Metall in der Giesserei).
    public static NetCommand AddCharge(int netId, int kind, string metal, float mass)
    {
        return new NetCommand
        {
            type = NetCommandType.AddCharge,
            netId = netId,
            index = kind,
            prefab = metal ?? "",
            amount = mass
        };
    }

    // ---------- Eisenbahn ----------

    // Gleisstueck: Start (Punkt-Nummer oder 0 = neuer Punkt an startPos) bis Ende (ebenso).
    // startYaw / endYaw (NaN = keine): feste Richtung an einem freien Punkt (Parallelgleise, Weichen).
    // Sie stehen in flatYaw / flatBase; "index" sagt, welche gesetzt sind (1 = Start, 2 = Ende).
    public static NetCommand TrackBuild(int startId, Vector3 startPos, int endId, Vector3 endPos,
                                        float startYaw = float.NaN, float endYaw = float.NaN)
    {
        int flags = (float.IsNaN(startYaw) ? 0 : 1) | (float.IsNaN(endYaw) ? 0 : 2);

        return new NetCommand
        {
            type = NetCommandType.TrackBuild,
            idA = startId, position = startPos,
            idB = endId, flatCenter = endPos,
            flatYaw = float.IsNaN(startYaw) ? 0f : startYaw,
            flatBase = float.IsNaN(endYaw) ? 0f : endYaw,
            index = flags
        };
    }

    // Weiche umlegen: nodeId = Punkt der Weiche, edgeId = Abzweig (kleiner als 0 = auf den anderen).
    public static NetCommand SwitchSet(int nodeId, int edgeId)
    {
        return new NetCommand { type = NetCommandType.SwitchSet, idA = nodeId, idB = edgeId };
    }

    // Drehscheibe: Mitte, Richtung des Ausgangs 0 (Grad), Anzahl Ausgaenge, Halbmesser (Meter).
    public static NetCommand TurntablePlace(Vector3 center, float baseYaw, int slots, float radius)
    {
        return new NetCommand
        {
            type = NetCommandType.TurntablePlace,
            position = center, flatYaw = baseYaw, idA = slots, radius = radius
        };
    }

    // Bruecke auf Ausgang "slot" drehen.
    public static NetCommand TurntableRotate(int ttId, int slot)
    {
        return new NetCommand { type = NetCommandType.TurntableRotate, idA = ttId, idB = slot };
    }

    public static NetCommand TurntableRemove(int ttId)
    {
        return new NetCommand { type = NetCommandType.TurntableRemove, idA = ttId };
    }

    public static NetCommand TrackRemove(int edgeId)
    {
        return new NetCommand { type = NetCommandType.TrackRemove, idA = edgeId };
    }

    public static NetCommand LocoSpawn(int edge, float dist, int dir, int recipe)
    {
        return new NetCommand
        {
            type = NetCommandType.LocoSpawn,
            idA = edge, amount = dist, portA = dir, index = recipe
        };
    }

    // Kuppeln: Fahrzeug A (Ende ea: 0 vorne, 1 hinten) mit Fahrzeug B (Ende eb).
    public static NetCommand Couple(int carA, int endA, int carB, int endB)
    {
        return new NetCommand
        {
            type = NetCommandType.Couple,
            idA = carA, portA = endA, idB = carB, portB = endB
        };
    }

    public static NetCommand Uncouple(int car, int end)
    {
        return new NetCommand { type = NetCommandType.Uncouple, idA = car, portA = end };
    }

    // Entladeschacht: Gleisstueck, Mitte (Meter) und Laenge (Meter, im Feld "radius").
    public static NetCommand TrackShaft(int edge, float dist, float length)
    {
        return new NetCommand { type = NetCommandType.TrackShaft, idA = edge, amount = dist, radius = length };
    }

    public static NetCommand TrackShaftRemove(int edge, float dist)
    {
        return new NetCommand { type = NetCommandType.TrackShaftRemove, idA = edge, amount = dist };
    }

    // Schlauch: Trommel (idA), Aktion (portA: 0 nehmen, 1 ablegen, 2 anschliessen, 3 abziehen),
    // Ziel beim Anschliessen (netId: 0 = Maschine, 1 = Fahrzeug; idB = Nummer; portB = Port-Nummer),
    // Position beim Ablegen. Wer den Schlauch traegt, steht im Absender.
    public static NetCommand HoseAction(int reel, int action, int targetKind, int targetId, int portIndex, Vector3 where)
    {
        return new NetCommand
        {
            type = NetCommandType.HoseAction,
            idA = reel, portA = action, netId = targetKind, idB = targetId, portB = portIndex, position = where
        };
    }

    public static NetCommand CarFunction(int car, int function)
    {
        return new NetCommand { type = NetCommandType.CarFunction, netId = car, index = function };
    }

    public static NetCommand LocoRemove(int carId)
    {
        return new NetCommand { type = NetCommandType.LocoRemove, netId = carId };
    }

    public static NetCommand LocoCoal(int carId)
    {
        return new NetCommand { type = NetCommandType.LocoCoal, netId = carId };
    }

    public static NetCommand LocoFuel(int carId, float liters)
    {
        return new NetCommand { type = NetCommandType.LocoFuel, netId = carId, amount = liters };
    }

    // Lager: domain 0 = Maschine/Szenenobjekt, 1 = loser Gegenstand (Schubkarre), 2 = Fahrzeug.
    // id = Nummer des Besitzers, storageIndex = Nummer des Lagers darin.
    // Stueck: "prefab|metall", kind = ItemKind, mass = Gewicht.
    public static NetCommand StoreAdd(int domain, int id, int storageIndex,
                                      string prefab, string metal, int kind, float mass)
    {
        return new NetCommand
        {
            type = NetCommandType.StoreAdd,
            portA = domain, netId = id, idA = storageIndex,
            prefab = prefab + "|" + (metal ?? ""), idB = kind, amount = mass
        };
    }

    public static NetCommand StoreTake(int domain, int id, int storageIndex,
                                       Vector3 position, Quaternion rotation)
    {
        return new NetCommand
        {
            type = NetCommandType.StoreTake,
            portA = domain, netId = id, idA = storageIndex,
            position = position, rotation = rotation
        };
    }

    // function: 0 = Neigung, 1 = Laenge. delta: Wirkung des Hebels (Vorzeichen = Richtung, Betrag = Zeit in s).
    public static NetCommand BoomDrive(int id, int beltIndex, int function, float delta)
    {
        return new NetCommand
        {
            type = NetCommandType.BoomDrive,
            netId = id, idA = beltIndex, portA = function, amount = delta
        };
    }

    // ---------- Uebertragen ----------

    public void Write(BinaryWriter w)
    {
        w.Write((byte)type);
        w.Write(sender);
        w.Write(netId);
        w.Write(prefab ?? "");
        NetIO.Write(w, position);
        NetIO.Write(w, rotation);

        w.Write(flatten);
        NetIO.Write(w, flatCenter);
        w.Write(flatHalf.x);
        w.Write(flatHalf.y);
        w.Write(flatYaw);
        w.Write(flatBase);
        w.Write(flatBlend);
        w.Write(fillTop);
        w.Write(fillReach);
        w.Write(fillSlope);

        w.Write(idA);
        w.Write(portA);
        w.Write(idB);
        w.Write(portB);

        w.Write(radius);
        w.Write(index);
        w.Write(amount);
    }

    public static NetCommand Read(BinaryReader r)
    {
        NetCommand c = new NetCommand();

        c.type = (NetCommandType)r.ReadByte();
        c.sender = r.ReadInt32();
        c.netId = r.ReadInt32();
        c.prefab = r.ReadString();
        c.position = NetIO.ReadVector3(r);
        c.rotation = NetIO.ReadQuaternion(r);

        c.flatten = r.ReadBoolean();
        c.flatCenter = NetIO.ReadVector3(r);
        c.flatHalf = new Vector2(r.ReadSingle(), r.ReadSingle());
        c.flatYaw = r.ReadSingle();
        c.flatBase = r.ReadSingle();
        c.flatBlend = r.ReadSingle();
        c.fillTop = r.ReadSingle();
        c.fillReach = r.ReadSingle();
        c.fillSlope = r.ReadSingle();

        c.idA = r.ReadInt32();
        c.portA = r.ReadInt32();
        c.idB = r.ReadInt32();
        c.portB = r.ReadInt32();

        c.radius = r.ReadSingle();
        c.index = r.ReadInt32();
        c.amount = r.ReadSingle();

        return c;
    }

    public byte[] ToBytes()
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            Write(w);
            w.Flush();
            return ms.ToArray();
        }
    }

    public static NetCommand FromBytes(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader r = new BinaryReader(ms))
        {
            return Read(r);
        }
    }
}

// Kleine Hilfen zum Schreiben und Lesen von Unity-Typen.
public static class NetIO
{
    public static void Write(BinaryWriter w, Vector3 v)
    {
        w.Write(v.x);
        w.Write(v.y);
        w.Write(v.z);
    }

    public static Vector3 ReadVector3(BinaryReader r)
    {
        return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    public static void Write(BinaryWriter w, Quaternion q)
    {
        w.Write(q.x);
        w.Write(q.y);
        w.Write(q.z);
        w.Write(q.w);
    }

    public static Quaternion ReadQuaternion(BinaryReader r)
    {
        return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
}
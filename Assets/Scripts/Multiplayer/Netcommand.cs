using System.IO;
using UnityEngine;

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
    AddCharge = 10    // Kohle/Erz in einen Hochofen oder festes Metall in eine Giesserei werfen
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
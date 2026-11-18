using System.Collections.Generic;
using System.IO;
using UnityEngine;

// =====================================================================
//  TrainNet: Synchronisation der Loks im Mehrspieler.
//
//  Prinzip:
//   - Der HOST rechnet die Fahrt der Loks (Geschwindigkeit, Verbrauch ...).
//   - Etwa 2,5 Mal pro Sekunde schickt der Host an alle Mitspieler den Zustand jeder Lok:
//     Position auf dem Gleis, Geschwindigkeit, Regler, Fahrtrichtung, Bremse sowie den
//     Zustand der Teile (Kessel, Tender, Kohlelager ...).
//   - Mitspieler rechnen die Bewegung zwischendurch selbst weiter (Geschwindigkeit mal Zeit)
//     und gleichen sich weich mit dem Host ab. Dadurch faehrt die Lok bei allen glatt.
//   - Steht ein Mitspieler auf einer Lok und bedient sie, schickt er seine Bedienung
//     (Regler, Richtung, Bremse) an den Host. Der Host faehrt dann danach.
//   - Die Pfeife wird ebenfalls weitergereicht.
//
//  Gleise bauen, Loks aufstellen/entfernen, Kohle/Diesel einfuellen und das Lager laufen
//  als Kommandos (siehe NetCommand / NetSync).
// =====================================================================
public static class TrainNet
{
    public const byte MsgState = 50;   // Host -> alle: Zustand aller Loks
    public const byte MsgInput = 51;   // Mitspieler -> Host: Bedienung einer Lok
    public const byte MsgHorn = 52;    // Pfeife (Mitspieler -> Host -> alle anderen)

    // So oft (Sekunden) schickt der Host den Zustand.
    private const float StateInterval = 0.4f;

    private static float timer;

    public static bool IsTrainMessage(byte type)
    {
        return type >= MsgState && type <= MsgHorn;
    }

    // ---------- Host: Zustand verschicken ----------

    // Wird jedes Bild vom TrackManager aufgerufen.
    public static void Tick(float dt)
    {
        if (NetSync.Mode != NetMode.Host)
            return;

        timer -= dt;

        if (timer > 0f)
            return;

        timer = StateInterval;

        List<TrainCar> cars = new List<TrainCar>();

        foreach (TrainCar car in TrainCar.All)
        {
            if (car != null && car.NetId > 0)
                cars.Add(car);
        }

        if (cars.Count == 0)
            return;

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(cars.Count);

            foreach (TrainCar car in cars)
            {
                byte[] block = WriteCar(car);

                w.Write(car.NetId);
                w.Write(block.Length);
                w.Write(block);
            }

            w.Flush();
            NetSync.SendItemMessage(MsgState, ms.ToArray(), -1);
        }
    }

    private static byte[] WriteCar(TrainCar car)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            car.WriteNetState(w);

            ISaveable[] parts = car.GetComponentsInChildren<ISaveable>(true);
            w.Write(parts.Length);

            for (int i = 0; i < parts.Length; i++)
            {
                StateBag bag = new StateBag();
                parts[i].SaveState(bag);

                w.Write(bag.keys.Count);

                for (int k = 0; k < bag.keys.Count; k++)
                {
                    w.Write(bag.keys[k]);
                    w.Write(bag.values[k]);
                }
            }

            w.Flush();
            return ms.ToArray();
        }
    }

    // ---------- Mitspieler: Zustand lesen ----------

    public static void HandleClient(byte type, byte[] payload)
    {
        try
        {
            if (type == MsgState)
                ReadStates(payload);
            else if (type == MsgHorn)
                ReadHorn(payload);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("TrainNet: Nachricht fehlerhaft: " + e.Message);
        }
    }

    private static void ReadStates(byte[] payload)
    {
        TrackManager manager = TrackManager.Instance;

        if (manager == null)
            return;

        using (MemoryStream ms = new MemoryStream(payload))
        using (BinaryReader r = new BinaryReader(ms))
        {
            int count = r.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                int length = r.ReadInt32();
                byte[] block = r.ReadBytes(length);

                TrainCar car = manager.FindCar(id);

                if (car != null)
                    ReadCar(car, block);
            }
        }
    }

    private static void ReadCar(TrainCar car, byte[] block)
    {
        using (MemoryStream ms = new MemoryStream(block))
        using (BinaryReader r = new BinaryReader(ms))
        {
            car.ReadNetState(r);

            ISaveable[] parts = car.GetComponentsInChildren<ISaveable>(true);
            int count = r.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                StateBag bag = new StateBag();
                int keys = r.ReadInt32();

                for (int k = 0; k < keys; k++)
                {
                    bag.keys.Add(r.ReadString());
                    bag.values.Add(r.ReadString());
                }

                // Nur uebernehmen, wenn die Teile bei allen gleich aufgebaut sind.
                if (count == parts.Length && parts[i] != null)
                    parts[i].LoadState(bag);
            }
        }
    }

    // ---------- Bedienung ----------

    // Mitspieler -> Host: aktuelle Bedienung der Lok.
    public static void SendInput(Locomotive loco)
    {
        if (!NetSync.IsClient || loco == null || loco.NetId <= 0)
            return;

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(loco.NetId);
            w.Write(loco.throttle);
            w.Write(loco.reverser);
            w.Write(loco.braking);
            w.Flush();

            NetSync.SendItemMessage(MsgInput, ms.ToArray(), -1);
        }
    }

    // Pfeife: Mitspieler -> Host, Host -> alle anderen.
    public static void SendHorn(Locomotive loco)
    {
        if (!NetSync.IsOnline || loco == null || loco.NetId <= 0)
            return;

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(loco.NetId);
            w.Flush();

            NetSync.SendItemMessage(MsgHorn, ms.ToArray(), -1);
        }
    }

    private static void ReadHorn(byte[] payload)
    {
        using (MemoryStream ms = new MemoryStream(payload))
        using (BinaryReader r = new BinaryReader(ms))
        {
            int id = r.ReadInt32();
            TrackManager m = TrackManager.Instance;
            Locomotive loco = m != null ? m.FindCar(id) as Locomotive : null;

            if (loco != null)
                loco.PlayHorn();
        }
    }

    // ---------- Host: Nachrichten der Mitspieler ----------

    public static void HandleHost(int sender, byte type, byte[] payload)
    {
        TrackManager manager = TrackManager.Instance;

        if (manager == null)
            return;

        using (MemoryStream ms = new MemoryStream(payload))
        using (BinaryReader r = new BinaryReader(ms))
        {
            if (type == MsgInput)
            {
                int id = r.ReadInt32();
                float throttle = r.ReadSingle();
                int reverser = r.ReadInt32();
                bool braking = r.ReadBoolean();

                Locomotive loco = manager.FindCar(id) as Locomotive;

                if (loco != null)
                    loco.ApplyRemoteInput(throttle, reverser, braking);
            }
            else if (type == MsgHorn)
            {
                int id = r.ReadInt32();
                Locomotive loco = manager.FindCar(id) as Locomotive;

                if (loco != null)
                    loco.PlayHorn();

                // An alle anderen Mitspieler weiterreichen.
                NetSync.SendItemMessage(MsgHorn, payload, sender);
            }
        }
    }
}
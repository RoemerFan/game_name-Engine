using System;
using System.Collections.Generic;
using System.Net.Sockets;

// Eine TCP-Verbindung mit Nachrichten-Rahmen.
//   Rahmen: [Laenge (4 Byte)] [Typ (1 Byte)] [Daten ...]
// Alles laeuft ohne Threads: Pump() wird einmal pro Bild aufgerufen, sendet
// Wartendes und liest Eingehendes. Fertige Nachrichten holt man mit TryRead().
public class NetConnection
{
    private const int MaxFrame = 64 * 1024 * 1024;

    private readonly Socket socket;
    private byte[] inBuffer = new byte[64 * 1024];
    private int inLength;

    private readonly List<byte[]> outQueue = new List<byte[]>();
    private int outOffset;

    public bool Alive { get; private set; }

    public NetConnection(Socket socket)
    {
        this.socket = socket;
        Alive = true;
    }

    // Stellt eine Nachricht in die Warteschlange.
    public void Send(byte type, byte[] payload)
    {
        if (!Alive)
            return;

        int length = (payload != null ? payload.Length : 0) + 1;
        byte[] frame = new byte[4 + length];

        BitConverter.GetBytes(length).CopyTo(frame, 0);
        frame[4] = type;

        if (payload != null && payload.Length > 0)
            Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);

        outQueue.Add(frame);
    }

    // Einmal pro Bild: senden und empfangen.
    public void Pump()
    {
        if (!Alive)
            return;

        try
        {
            // Senden (nicht blockierend: was nicht geht, bleibt fuer spaeter).
            while (outQueue.Count > 0)
            {
                byte[] frame = outQueue[0];
                SocketError error;

                int sent = socket.Send(
                    frame, outOffset, frame.Length - outOffset, SocketFlags.None, out error);

                if (error == SocketError.WouldBlock)
                    break;

                if (error != SocketError.Success)
                {
                    Close();
                    return;
                }

                outOffset += sent;

                if (outOffset >= frame.Length)
                {
                    outQueue.RemoveAt(0);
                    outOffset = 0;
                }
            }

            // Empfangen
            while (socket.Available > 0)
            {
                if (inLength == inBuffer.Length)
                {
                    if (inBuffer.Length >= MaxFrame + 16)
                    {
                        Close();
                        return;
                    }

                    Array.Resize(ref inBuffer, inBuffer.Length * 2);
                }

                SocketError error;
                int n = socket.Receive(
                    inBuffer, inLength, inBuffer.Length - inLength, SocketFlags.None, out error);

                if (error == SocketError.WouldBlock)
                    break;

                if (error != SocketError.Success || n <= 0)
                {
                    Close();
                    return;
                }

                inLength += n;
            }

            // Gegenseite hat getrennt: lesbar, aber nichts da.
            if (socket.Available == 0 && socket.Poll(0, SelectMode.SelectRead))
                Close();
        }
        catch (Exception)
        {
            Close();
        }
    }

    // Holt die naechste vollstaendige Nachricht, falls vorhanden.
    public bool TryRead(out byte type, out byte[] payload)
    {
        type = 0;
        payload = null;

        if (inLength < 4)
            return false;

        int length = BitConverter.ToInt32(inBuffer, 0);

        if (length < 1 || length > MaxFrame)
        {
            Close();
            return false;
        }

        if (inLength < 4 + length)
            return false;

        type = inBuffer[4];
        payload = new byte[length - 1];

        if (payload.Length > 0)
            Buffer.BlockCopy(inBuffer, 5, payload, 0, payload.Length);

        int rest = inLength - (4 + length);

        if (rest > 0)
            Buffer.BlockCopy(inBuffer, 4 + length, inBuffer, 0, rest);

        inLength = rest;
        return true;
    }

    // Wartet noch Gesendetes?
    public bool HasPendingOutput
    {
        get { return outQueue.Count > 0; }
    }

    public void Close()
    {
        if (!Alive)
            return;

        Alive = false;

        try { socket.Shutdown(SocketShutdown.Both); } catch (Exception) { }
        try { socket.Close(); } catch (Exception) { }
    }
}
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Alles, was gespeichert werden soll und einen eigenen Zustand hat (Kessel,
// Schalter, Batterie ...), bekommt dieses Interface.
// SaveState schreibt die Werte in den "Beutel" (StateBag), LoadState liest
// sie wieder heraus. Fehlt ein Wert (z. B. in einem alten Spielstand), gilt
// der Standardwert, der beim Lesen angegeben wird.
public interface ISaveable
{
    void SaveState(StateBag bag);
    void LoadState(StateBag bag);
}

// Ein einfacher Beutel mit Schluessel/Wert-Paaren. Kann von JsonUtility
// gespeichert werden (Woerterbuecher koennte es nicht).
[Serializable]
public class StateBag
{
    public List<string> keys = new List<string>();
    public List<string> values = new List<string>();

    private void Set(string key, string value)
    {
        int i = keys.IndexOf(key);

        if (i >= 0)
        {
            values[i] = value;
            return;
        }

        keys.Add(key);
        values.Add(value);
    }

    private bool TryGet(string key, out string value)
    {
        int i = keys.IndexOf(key);

        if (i >= 0)
        {
            value = values[i];
            return true;
        }

        value = null;
        return false;
    }

    public bool Has(string key)
    {
        return keys.Contains(key);
    }

    public void SetFloat(string key, float value)
    {
        Set(key, value.ToString("R", CultureInfo.InvariantCulture));
    }

    public float GetFloat(string key, float fallback = 0f)
    {
        string text;
        float result;

        if (TryGet(key, out text) &&
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return result;

        return fallback;
    }

    public void SetInt(string key, int value)
    {
        Set(key, value.ToString(CultureInfo.InvariantCulture));
    }

    public int GetInt(string key, int fallback = 0)
    {
        string text;
        int result;

        if (TryGet(key, out text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            return result;

        return fallback;
    }

    public void SetBool(string key, bool value)
    {
        Set(key, value ? "1" : "0");
    }

    public bool GetBool(string key, bool fallback = false)
    {
        string text;

        if (TryGet(key, out text))
            return text == "1";

        return fallback;
    }

    public void SetString(string key, string value)
    {
        Set(key, value ?? "");
    }

    public string GetString(string key, string fallback = "")
    {
        string text;
        return TryGet(key, out text) ? text : fallback;
    }
}

// ---------- Aufbau der Spielstand-Datei (wird als JSON gespeichert) ----------

[Serializable]
public class MachineRecord
{
    public string prefab;
    public Vector3 position;
    public Quaternion rotation;

    // Nummer im Mehrspieler (0 = in aelteren Spielstaenden nicht vorhanden).
    public int netId;

    // Ein Beutel pro ISaveable-Komponente, in der Reihenfolge im Prefab.
    public List<StateBag> states = new List<StateBag>();
}

[Serializable]
public class PipeRecord
{
    // Maschine = Nummer in der Maschinenliste, Port = Nummer unter den
    // Ports dieser Maschine.
    public int machineA;
    public int portA;
    public int machineB;
    public int portB;
}

[Serializable]
public class ItemRecord
{
    public string prefab;
    public Vector3 position;
    public Quaternion rotation;
    public List<StateBag> states = new List<StateBag>();

    // Nummer im Mehrspieler (0 = keine). Beim Beitritt uebernimmt der Mitspieler sie.
    public int netId;
}

[Serializable]
public class SaveFileData
{
    public int version;
    public string savedAt;
    public float playSeconds;

    // Seed und Weltgroesse: Ein Spielstand passt nur zur gleichen Welt.
    public string world;

    public float timeOfDay;
    public Vector2 cloudOffset;

    public Vector3 playerPosition;
    public float playerYaw;

    public List<MachineRecord> machines = new List<MachineRecord>();
    public List<PipeRecord> pipes = new List<PipeRecord>();
    public List<ItemRecord> items = new List<ItemRecord>();
}
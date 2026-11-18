#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// =====================================================================
//  CabPanelPlaceholderCreator: erzeugt das Bedienpult (Instrumentenbrett und Hebel) als Prefab (nur Editor).
//
//  WICHTIG: Diese Datei gehoert in einen Ordner namens "Editor" (z. B. Assets/Editor/), NICHT nach Assets/Scripts.
//
//  Menue: Factocreative > Platzhalter > Lok-Bedienpult erzeugen
//  Es entsteht Assets/Prefabs/Placeholders/CabPanelPlaceholder. Ziehe es als KIND in dein Lok-Koerper-Prefab
//  (das mit dem Skript "Locomotive"). Die Lok wird von Hebeln und Anzeigen automatisch im Elternobjekt gefunden.
//  Danach kannst du die Teile verschieben oder durch eigene Modelle ersetzen (siehe TrainLever und TrainGauge).
// =====================================================================
public static class CabPanelPlaceholderCreator
{
    private const string Folder = "Assets/Prefabs/Placeholders";

    [MenuItem("Factocreative/Platzhalter/Lok-Bedienpult erzeugen")]
    public static void Create()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        GameObject root = new GameObject("CabPanelPlaceholder");
        CabPanelBuilder.Build(root.transform, MaterialFor);

        string path = Folder + "/CabPanelPlaceholder.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Bedienpult-Platzhalter erzeugt: " + path);
    }

    // Gespeichertes Material pro Farbe (damit das Prefab seine Materialien behaelt).
    private static Material MaterialFor(Color color)
    {
        string path = Folder + "/Cab_" + ColorUtility.ToHtmlStringRGB(color) + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
            return existing;

        Material m = new Material(Shader.Find("Standard"));
        m.color = color;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
#endif
using System.Collections.Generic;
using UnityEngine;

// Hier pflegst du alle Metalle des Spiels im Inspector.
//
// EINRICHTUNG: Dieses Skript an ein beliebiges Objekt in der Szene haengen (nur einmal).
//  - "Metals": Liste der Metalle (Eisen, Kupfer, Zinn ...). Neue Metalle immer unten anhaengen.
//  - "Alloys": Legierungs-Regeln (Kupfer + Zinn = Bronze). Das Ergebnis muss auch in der
//    Metall-Liste stehen.
// Dem Erz- bzw. Barren-Prefab gibst du im ItemTag die Metall-ID (Feld "Metal").
[DefaultExecutionOrder(-200)]
public class MetalRegistry : MonoBehaviour
{
    public List<MetalDef> metals = new List<MetalDef>
    {
        new MetalDef { nameKey = "metal.iron" }
    };

    public List<AlloyRule> alloys = new List<AlloyRule>();

    void Awake()
    {
        Metals.SetAll(metals, alloys);
    }
}
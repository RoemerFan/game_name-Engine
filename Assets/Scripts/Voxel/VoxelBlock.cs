using UnityEngine;

// Materialien des Gelaendes. Jeder Gitterpunkt der Welt hat eine dieser
// Sorten (0 = Luft). Die Zahl wird als ein Byte gespeichert.
public enum BlockType : byte
{
    Air = 0,
    Grass = 1,
    Dirt = 2,
    Stone = 3,
    Coal = 4,
    Iron = 5,
    Bedrock = 6   // reserviert (aktuell wird die unterste Schicht als Stein dargestellt)
}

// Eigenschaften der Materialien und ihre automatisch erzeugten Texturen.
public static class BlockInfo
{
    // So viele Sekunden dauert EIN Grabvorgang an dieser Materialsorte
    // (mit Hand-Tempo 1). Haerter = laenger.
    public static float Hardness(BlockType type)
    {
        switch (type)
        {
            case BlockType.Grass: return 0.5f;
            case BlockType.Dirt:  return 0.5f;
            case BlockType.Stone: return 1.1f;
            case BlockType.Coal:  return 0.9f;
            case BlockType.Iron:  return 1.6f;
            default:              return float.PositiveInfinity;
        }
    }

    public static bool IsDiggable(BlockType type)
    {
        return type != BlockType.Air && type != BlockType.Bedrock;
    }

    // ---------- Texturen ----------
    // Jedes Material bekommt eine eigene, nahtlos wiederholbare Textur
    // (64x64 Pixel), die per Code aus Rauschen gemalt wird.

    const int TexSize = 64;

    // Nahtloses "Value Noise": Zufallswerte auf einem Gitter, die weich
    // ineinander uebergehen. "period" ist die Gitterweite, am Rand wird
    // wieder von vorn begonnen (damit die Textur kachelt).
    static float[] MakeGrid(int period, System.Random rnd)
    {
        float[] grid = new float[period * period];

        for (int i = 0; i < grid.Length; i++)
            grid[i] = (float)rnd.NextDouble();

        return grid;
    }

    // u und v liegen zwischen 0 und 1.
    static float SampleGrid(float[] grid, int period, float u, float v)
    {
        float x = u * period;
        float y = v * period;

        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);

        float fx = x - x0;
        float fy = y - y0;

        // Weiche Uebergaenge
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);

        int x1 = (x0 + 1) % period;
        int y1 = (y0 + 1) % period;

        x0 %= period;
        y0 %= period;

        float a = grid[y0 * period + x0];
        float b = grid[y0 * period + x1];
        float c = grid[y1 * period + x0];
        float d = grid[y1 * period + x1];

        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    public static Texture2D CreateTexture(BlockType type, int seed)
    {
        System.Random rnd = new System.Random(seed * 31 + (int)type * 977);

        // Rauschen in vier Groessen, grob bis fein.
        float[] g4 = MakeGrid(4, rnd);
        float[] g8 = MakeGrid(8, rnd);
        float[] g16 = MakeGrid(16, rnd);
        float[] g32 = MakeGrid(32, rnd);

        // Zweite Schicht fuer Flecken (Erz-Splitter, Rost).
        float[] spots = MakeGrid(16, rnd);
        float[] rustGrid = MakeGrid(8, rnd);

        Color[] pixels = new Color[TexSize * TexSize];

        for (int py = 0; py < TexSize; py++)
        {
            for (int px = 0; px < TexSize; px++)
            {
                float u = px / (float)TexSize;
                float v = py / (float)TexSize;

                // Mischung aller Groessen: Grundmuster des Materials.
                float f =
                    SampleGrid(g4, 4, u, v) * 0.35f +
                    SampleGrid(g8, 8, u, v) * 0.30f +
                    SampleGrid(g16, 16, u, v) * 0.22f +
                    SampleGrid(g32, 32, u, v) * 0.13f;

                Color c;

                switch (type)
                {
                    case BlockType.Grass:
                        c = Color.Lerp(
                            new Color(0.24f, 0.50f, 0.15f),
                            new Color(0.38f, 0.68f, 0.22f), f);
                        break;

                    case BlockType.Dirt:
                        c = Color.Lerp(
                            new Color(0.36f, 0.24f, 0.14f),
                            new Color(0.52f, 0.36f, 0.22f), f);
                        break;

                    case BlockType.Coal:
                    {
                        // Tiefschwarzer Grund ...
                        c = Color.Lerp(
                            new Color(0.04f, 0.04f, 0.05f),
                            new Color(0.12f, 0.12f, 0.14f), f);

                        // ... mit helleren Bruchflaechen ...
                        float chip = SampleGrid(spots, 16, u, v);

                        if (chip > 0.62f)
                        {
                            float k = Mathf.Clamp01((chip - 0.62f) * 6f);
                            c = Color.Lerp(c, new Color(0.26f, 0.26f, 0.29f), k * 0.8f);
                        }

                        // ... und einzelnen Glanzpunkten.
                        if (rnd.NextDouble() < 0.006)
                            c = new Color(0.45f, 0.45f, 0.50f);

                        break;
                    }

                    case BlockType.Iron:
                    {
                        // Stein mit rostroten Einschluessen
                        c = Color.Lerp(
                            new Color(0.40f, 0.40f, 0.42f),
                            new Color(0.58f, 0.58f, 0.60f), f);

                        float rust = SampleGrid(rustGrid, 8, u, v);

                        if (rust > 0.5f)
                        {
                            float k = Mathf.Clamp01((rust - 0.5f) * 5f);
                            c = Color.Lerp(c, new Color(0.66f, 0.36f, 0.22f), k);
                        }

                        break;
                    }

                    default: // Stein
                        c = Color.Lerp(
                            new Color(0.40f, 0.40f, 0.42f),
                            new Color(0.60f, 0.60f, 0.62f), f);
                        break;
                }

                // Feines Koernen pro Pixel
                float grain = 0.93f + (float)rnd.NextDouble() * 0.14f;
                c = new Color(c.r * grain, c.g * grain, c.b * grain, 1f);

                pixels[py * TexSize + px] = c;
            }
        }

        Texture2D tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true);
        tex.SetPixels(pixels);
        tex.wrapMode = TextureWrapMode.Repeat;    // wiederholt sich ueber die Flaeche
        tex.filterMode = FilterMode.Trilinear;
        tex.anisoLevel = 4;
        tex.Apply(true);

        return tex;
    }
}
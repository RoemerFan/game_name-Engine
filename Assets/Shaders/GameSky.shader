// Eigener Himmel (Built-in Render Pipeline) mit Farbverlauf, Sonne, Mond,
// Sternen und Wolken. Alle Werte setzt das Skript SkyController.
Shader "Custom/GameSky"
{
    Properties
    {
        _SunDir ("Sonnenrichtung", Vector) = (0,1,0,0)
        _MoonDir ("Mondrichtung", Vector) = (0,-1,0,0)
        _SunColor ("Sonnenfarbe", Color) = (1,0.95,0.85,1)
        _SunSize ("Sonnenradius (Bogenmass)", Float) = 0.05
        _MoonColor ("Mondfarbe", Color) = (0.9,0.92,1,1)
        _MoonSize ("Mondradius (Bogenmass)", Float) = 0.06
        _MoonPhase ("Mondphase 0..1", Float) = 0.5
        _MoonAxis ("Himmelsachse (Mond-Ausrichtung)", Vector) = (1,0,0,0)

        _ZenithColor ("Himmel oben", Color) = (0.2,0.45,0.85,1)
        _HorizonColor ("Himmel am Horizont", Color) = (0.6,0.75,0.9,1)
        _GroundColor ("Boden unter dem Horizont", Color) = (0.3,0.3,0.3,1)
        _GlowColor ("Sonnenschein-Glanz", Color) = (1,0.6,0.3,1)
        _GlowStrength ("Glanzstaerke", Float) = 0.3
        _Day ("Tag 0..1", Float) = 1

        _StarBrightness ("Sternhelligkeit", Float) = 1
        _StarR0 ("Sternendrehung Zeile 0", Vector) = (1,0,0,0)
        _StarR1 ("Sternendrehung Zeile 1", Vector) = (0,1,0,0)
        _StarR2 ("Sternendrehung Zeile 2", Vector) = (0,0,1,0)

        _CloudCoverage ("Wolkenmenge 0..1", Float) = 0.45
        _CloudScale ("Wolkengroesse", Float) = 1.3
        _CloudOffset ("Wolkenverschiebung", Vector) = (0,0,0,0)
        _CloudColor ("Wolke hell", Color) = (1,1,1,1)
        _CloudShade ("Wolke Schatten", Color) = (0.6,0.65,0.75,1)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _SunDir, _MoonDir, _SunColor, _MoonColor, _MoonAxis;
            float _SunSize, _MoonSize, _MoonPhase;
            float4 _ZenithColor, _HorizonColor, _GroundColor, _GlowColor;
            float _GlowStrength, _Day, _StarBrightness;
            float4 _StarR0, _StarR1, _StarR2;
            float _CloudCoverage, _CloudScale;
            float4 _CloudOffset, _CloudColor, _CloudShade;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // ---------- Zufall und Rauschen ----------

            float hash2(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float hash3(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash2(i);
                float b = hash2(i + float2(1, 0));
                float c = hash2(i + float2(0, 1));
                float d = hash2(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;

                for (int i = 0; i < 5; i++)
                {
                    v += a * vnoise(p);
                    p = p * 2.03 + float2(17.1, 9.2);
                    a *= 0.5;
                }

                return v;
            }

            // ---------- Sterne ----------

            // Eine Ebene Sterne: Der Himmel wird in Zellen geteilt, einige
            // Zellen enthalten einen Stern an zufaelliger Stelle.
            float3 StarLayer(float3 d, float scale, float threshold, float size)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float3 f = p - cell;

                float h = hash3(cell);

                if (h < threshold)
                    return float3(0, 0, 0);

                float3 offset = float3(
                    hash3(cell + 7.1),
                    hash3(cell + 13.7),
                    hash3(cell + 19.3));

                float dist = length(f - (0.2 + offset * 0.6));
                float star = smoothstep(size, 0.0, dist);

                float bright = (h - threshold) / (1.0 - threshold);
                float twinkle = 0.75 + 0.25 * sin(_Time.y * 3.0 + h * 100.0);

                float3 tint = lerp(float3(1.0, 0.85, 0.7), float3(0.7, 0.82, 1.0), frac(h * 53.0));

                return tint * star * (0.4 + 0.6 * bright) * twinkle;
            }

            // ---------- Bild ----------

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float h = dir.y;
                float day = _Day;

                // Farbverlauf: oben Zenit, unten Horizont.
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(h), 0.45));

                // Glanz um die Sonne (orange bei Auf- und Untergang).
                float sunAng = acos(clamp(dot(dir, _SunDir.xyz), -1.0, 1.0));
                col += _GlowColor.rgb * exp(-sunAng * 3.5) * _GlowStrength;

                // Unter dem Horizont: Bodenfarbe.
                col = lerp(col, _GroundColor.rgb, smoothstep(0.0, 0.08, -h));

                // Sonne und Mond nur ueber dem Horizont zeichnen.
                float horizonMask = smoothstep(-0.01, 0.03, h);

                // Sterne
                float starVis = saturate(1.0 - day * 2.5) * _StarBrightness * horizonMask * saturate(h * 4.0);

                if (starVis > 0.001)
                {
                    float3 sd = float3(
                        dot(_StarR0.xyz, dir),
                        dot(_StarR1.xyz, dir),
                        dot(_StarR2.xyz, dir));

                    float3 stars = StarLayer(sd, 60.0, 0.975, 0.14)
                                 + StarLayer(sd, 120.0, 0.985, 0.12) * 0.7;

                    col += stars * starVis * 1.6;
                }

                // Mond mit Phase und einfacher Oberflaeche.
                float moonDot = dot(dir, _MoonDir.xyz);

                if (moonDot > 0.95)
                {
                    float ang = acos(clamp(moonDot, -1.0, 1.0));
                    float night = 1.0 - saturate(day * 2.0);

                    // Leichter Hof um den Mond.
                    // Der Hof wird zum Rand des Bereichs (ang = 0.318) exakt null,
                    // damit es keine sichtbare Kante gibt.
                    float halo = max(0.0, exp(-ang * 8.0) - exp(-0.318 * 8.0));
                    halo = halo * halo * 4.0;
                    col += _MoonColor.rgb * halo * 0.1 * night * horizonMask;

                    if (ang < _MoonSize)
                    {
                        float3 m = _MoonDir.xyz;
                        // Feste Achse der Himmelsdrehung: Der Mond dreht sich so ruhig mit dem Himmel.
                        float3 refUp = normalize(_MoonAxis.xyz);
                        float3 T = normalize(cross(m, refUp));
                        float3 B = cross(T, m);

                        float2 uv = float2(dot(dir, T), dot(dir, B)) / max(0.0001, sin(_MoonSize));
                        float r2 = dot(uv, uv);
                        float3 n = float3(uv, sqrt(max(0.0, 1.0 - r2)));

                        // Dunkle "Meere" und kleine Krater.
                        float maria = smoothstep(0.42, 0.62, fbm(uv * 2.2 + 3.1));
                        float albedo = lerp(0.95, 0.55, maria) + (vnoise(uv * 14.0) - 0.5) * 0.12;

                        // Beleuchtung entsprechend der Mondphase.
                        float phi = _MoonPhase * 6.28318;
                        float3 lightDir = float3(sin(phi), 0.0, -cos(phi));
                        float lit = smoothstep(-0.02, 0.12, dot(n, lightDir));

                        float3 moonCol = _MoonColor.rgb * albedo * (lit + 0.04) * 1.5;
                        float edge = 1.0 - smoothstep(_MoonSize * 0.94, _MoonSize, ang);

                        col = lerp(col, moonCol, edge * horizonMask);
                    }
                }

                // Sonnenscheibe mit kleinem Strahlenkranz.
                float sunDisk = 1.0 - smoothstep(_SunSize * 0.85, _SunSize, sunAng);
                col += _SunColor.rgb * exp(-sunAng * 18.0) * 0.35 * horizonMask;
                col = lerp(col, saturate(_SunColor.rgb * 1.6 + 0.3), sunDisk * horizonMask);

                // Wolken: Ebene ueber dem Betrachter, nach oben projiziert.
                if (h > 0.0 && _CloudCoverage > 0.01)
                {
                    float2 uv = dir.xz / (h + 0.15) * _CloudScale + _CloudOffset.xy;

                    float d1 = fbm(uv);
                    float thr = lerp(0.85, 0.38, saturate(_CloudCoverage));
                    float dens = smoothstep(thr, thr + 0.22, d1);

                    if (dens > 0.002)
                    {
                        // Seite zur Sonne ist heller (zweite Probe in Sonnenrichtung).
                        float d2 = fbm(uv + _SunDir.xz * 0.12);
                        float lit = saturate(0.55 + (d1 - d2) * 5.0);

                        float3 cloudCol = lerp(_CloudShade.rgb, _CloudColor.rgb, lit);
                        cloudCol = lerp(cloudCol, _CloudShade.rgb, saturate((d1 - thr) * 1.5) * 0.35);

                        // Heller Rand, wenn die Sonne dahinter steht.
                        cloudCol += _SunColor.rgb * exp(-sunAng * 6.0) * (1.0 - dens) * 0.25 * day;

                        float a = dens * smoothstep(0.0, 0.2, h);
                        col = lerp(col, cloudCol, a * 0.95);
                    }
                }

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}

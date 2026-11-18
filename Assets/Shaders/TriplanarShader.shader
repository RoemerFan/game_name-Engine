// =====================================================================
//  Triplanar-Shader (Built-in Render Pipeline)
//
//  Legt eine kachelbare Textur nicht ueber die UVs des Modells, sondern ueber die Position im Raum:
//  von allen drei Seiten (X, Y, Z) projiziert und an den Kanten weich gemischt.
//
//  Vorteile:
//   - Bauteile verschiedener Groesse passen nahtlos aneinander (gleiche Kachelgroesse = gleiche Steingroesse).
//   - Keine UVs noetig, keine gestreckten Texturen an Schraegen.
//   - Mit "Variation" faellt die Wiederholung weniger auf.
//
//  Benutzung:
//   1. Im Material den Shader "Industrialist/Triplanar" waehlen.
//   2. Albedo-Textur (kachelbar) und Normal-Map eintragen. Die Normal-Map muss im Import als
//      "Normal map" eingestellt sein.
//   3. "Kachelgroesse" = wie viele Meter die GANZE Textur breit sein soll (z. B. 2 = ein Bild je 2 m).
//   4. Ruhende Bauten: "Am Objekt kleben" AUS (Muster haengt an der Welt, alles passt zusammen).
//      Bewegliche Dinge (Loks, Wagen, Maschinen): "Am Objekt kleben" AN, sonst gleitet das Muster.
// =====================================================================
Shader "Industrialist/Triplanar"
{
    Properties
    {
        _Color ("Farbton", Color) = (1,1,1,1)
        [NoScaleOffset] _MainTex ("Albedo (kachelbar)", 2D) = "white" {}
        [NoScaleOffset] [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal-Staerke", Range(0, 2)) = 1
        _TileSize ("Kachelgroesse (Meter)", Float) = 2
        _Offset ("Verschiebung (Meter, x y z)", Vector) = (0,0,0,0)
        [Toggle] _ObjectSpace ("Am Objekt kleben (statt an der Welt)", Float) = 0
        _Sharpness ("Schaerfe der Uebergaenge", Range(1, 16)) = 6
        _Variation ("Helligkeits-Variation", Range(0, 1)) = 0.25
        _VariationScale ("Variation: Groesse (Meter)", Float) = 6
        _Glossiness ("Glaette", Range(0, 1)) = 0.15
        _Metallic ("Metall", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        fixed4 _Color;
        half _BumpScale;
        float _TileSize;
        float4 _Offset;
        float _ObjectSpace;
        half _Sharpness;
        half _Variation;
        float _VariationScale;
        half _Glossiness;
        half _Metallic;

        struct Input
        {
            float3 worldPos;
            float3 objPos;      // Position im Objekt (mit Massstab), fuer "Am Objekt kleben"
            float3 objNormal;   // Normale im Objekt
            INTERNAL_DATA
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            // Massstab des Objekts, damit die Kachelgroesse in Metern stimmt.
            float3 s = float3(length(unity_ObjectToWorld._m00_m10_m20),
                              length(unity_ObjectToWorld._m01_m11_m21),
                              length(unity_ObjectToWorld._m02_m12_m22));

            o.objPos = v.vertex.xyz * s;
            o.objNormal = normalize(v.normal / max(s, float3(1e-4, 1e-4, 1e-4)));
        }

        // Normal-Map auspacken und die Staerke einstellen.
        half3 UnpackScaled(half4 packed, half scale)
        {
            half3 n = UnpackNormal(packed);
            n.xy *= scale;
            return normalize(n);
        }

        // Einfaches 3D-Wertrauschen (fuer die Helligkeits-Variation).
        float Hash31(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float ValueNoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);

            return lerp(
                lerp(lerp(Hash31(i + float3(0, 0, 0)), Hash31(i + float3(1, 0, 0)), f.x),
                     lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), f.x),
                     lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), f.x), f.y),
                f.z);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            bool objMode = _ObjectSpace > 0.5;

            float3 basePos = objMode ? IN.objPos : IN.worldPos;
            float3 pos = (basePos + _Offset.xyz) / max(_TileSize, 0.01);

            // Die glatte Flaechennormale (ohne Normal-Map), in Welt- bzw. Objektachsen.
            float3 gn = objMode
                ? normalize(IN.objNormal)
                : normalize(WorldNormalVector(IN, float3(0, 0, 1)));

            // Gewichte der drei Projektionen: je mehr die Flaeche zu einer Achse zeigt, desto mehr zaehlt sie.
            float3 w = pow(abs(gn), _Sharpness);
            w /= (w.x + w.y + w.z + 1e-5);

            float2 uvX = pos.zy;
            float2 uvY = pos.xz;
            float2 uvZ = pos.xy;

            // Farbe
            fixed4 c = tex2D(_MainTex, uvX) * w.x
                     + tex2D(_MainTex, uvY) * w.y
                     + tex2D(_MainTex, uvZ) * w.z;

            // Helligkeits-Variation: grossflaechiges Rauschen, bricht das Kachelmuster auf.
            float3 vp = basePos / max(_VariationScale, 0.1);
            float nv = ValueNoise(vp) * 0.6 + ValueNoise(vp * 2.7) * 0.4;
            c.rgb *= 1.0 + (nv - 0.5) * 0.7 * _Variation;

            // Normal-Map (Whiteout-Mischung)
            half3 nx = UnpackScaled(tex2D(_BumpMap, uvX), _BumpScale);
            half3 ny = UnpackScaled(tex2D(_BumpMap, uvY), _BumpScale);
            half3 nz = UnpackScaled(tex2D(_BumpMap, uvZ), _BumpScale);

            nx = half3(nx.xy + gn.zy, abs(nx.z) * gn.x);
            ny = half3(ny.xy + gn.xz, abs(ny.z) * gn.y);
            nz = half3(nz.xy + gn.xy, abs(nz.z) * gn.z);

            float3 n = normalize(nx.zyx * w.x + ny.xzy * w.y + nz.xyz * w.z);

            // Im Objekt-Modus ist n in Objektachsen: in Weltachsen umrechnen.
            if (objMode)
                n = normalize(mul((float3x3)unity_ObjectToWorld, n));

            // Weltnormale in den Tangentenraum umrechnen. Dafuer die Tangentenachsen ueber
            // WorldNormalVector holen (laeuft in allen Unity-Versionen, WorldToTangentNormalVector nicht).
            float3 tX = WorldNormalVector(IN, float3(1, 0, 0));
            float3 tY = WorldNormalVector(IN, float3(0, 1, 0));
            float3 tZ = WorldNormalVector(IN, float3(0, 0, 1));
            o.Normal = normalize(float3(dot(n, tX), dot(n, tY), dot(n, tZ)));

            o.Albedo = c.rgb * _Color.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}

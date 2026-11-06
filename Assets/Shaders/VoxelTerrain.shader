// Gelaende-Shader (Built-in Render Pipeline).
//
// Mischt die fuenf Materialien weich ineinander:
//  - Wie stark welches Material sichtbar ist, steht pro Eckpunkt im Mesh
//    (Vertex-Farbe: R = Gras, G = Erde, B = Stein, A = Kohle,
//     Eisen kommt aus der zweiten UV-Koordinate).
//  - Die Texturen werden "triplanar" aufgetragen: von allen drei Seiten
//    projiziert und nach Flaechenrichtung gemischt. Dadurch gibt es keine
//    Nahtstellen und keine gedehnten Texturen an steilen Waenden.
Shader "Custom/VoxelTerrain"
{
    Properties
    {
        _GrassTex ("Gras", 2D) = "white" {}
        _DirtTex  ("Erde", 2D) = "white" {}
        _StoneTex ("Stein", 2D) = "white" {}
        _CoalTex  ("Kohle", 2D) = "white" {}
        _IronTex  ("Eisen", 2D) = "white" {}

        // So viele Meter deckt eine Textur-Wiederholung ab.
        _TexScale ("Textur-Groesse (m)", Float) = 1.5

        // 1 = sehr weiche Uebergaenge, hoeher = schaerfere Kanten.
        _Sharp ("Schaerfe der Uebergaenge", Range(1, 6)) = 1.8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        sampler2D _GrassTex;
        sampler2D _DirtTex;
        sampler2D _StoneTex;
        sampler2D _CoalTex;
        sampler2D _IronTex;
        float _TexScale;
        float _Sharp;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float4 color : COLOR;   // Gewichte: Gras, Erde, Stein, Kohle
            float ironWeight;       // Gewicht Eisen (aus der 2. UV-Koordinate)
        };

        // Vertex-Funktion: reicht das Eisen-Gewicht an den Pixel-Teil weiter.
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.ironWeight = v.texcoord1.x;
        }

        // Textur von drei Seiten auftragen und nach Flaechenrichtung mischen.
        fixed3 Triplanar (sampler2D tex, float3 p, float3 blend)
        {
            fixed3 x = tex2D(tex, p.zy).rgb;
            fixed3 y = tex2D(tex, p.xz).rgb;
            fixed3 z = tex2D(tex, p.xy).rgb;

            return x * blend.x + y * blend.y + z * blend.z;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Mischung der drei Projektionsrichtungen
            float3 blend = pow(abs(IN.worldNormal), 4);
            blend /= (blend.x + blend.y + blend.z + 0.0001);

            float3 p = IN.worldPos / _TexScale;

            // Gewichte der fuenf Materialien; hoehere Schaerfe betont
            // das jeweils staerkste Material.
            float w[5];
            w[0] = IN.color.r;
            w[1] = IN.color.g;
            w[2] = IN.color.b;
            w[3] = IN.color.a;
            w[4] = IN.ironWeight;

            float total = 0;

            for (int i = 0; i < 5; i++)
            {
                w[i] = pow(max(w[i], 0.0001), _Sharp);
                total += w[i];
            }

            fixed3 albedo =
                Triplanar(_GrassTex, p, blend) * (w[0] / total) +
                Triplanar(_DirtTex,  p, blend) * (w[1] / total) +
                Triplanar(_StoneTex, p, blend) * (w[2] / total) +
                Triplanar(_CoalTex,  p, blend) * (w[3] / total) +
                Triplanar(_IronTex,  p, blend) * (w[4] / total);

            o.Albedo = albedo;
            o.Metallic = 0;
            o.Smoothness = 0;
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}

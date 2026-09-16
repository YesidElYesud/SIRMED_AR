Shader "SIRMED/EvacuationRibbon"
{
    // Cinta de la ruta de evacuación (ver EvacuationRouteController). El UV.y de
    // la malla ya viene en metros de distancia acumulada a lo largo de la ruta
    // (no normalizado 0-1), así que _MainTex_ST.y (Tiling) controla directamente
    // cuántos metros ocupa cada repetición de la textura de flecha.
    Properties
    {
        _MainTex ("Textura de flecha (tileable en Y)", 2D) = "white" {}
        _Color ("Tinte / Alpha", Color) = (0.15, 0.95, 0.35, 0.85)
        _ScrollSpeed ("Velocidad de flujo (m/s)", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _ScrollSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                // Restar desplaza la muestra hacia atrás, lo que hace que el
                // patrón visible avance en +Y (hacia el Punto de Encuentro).
                uv.y -= _Time.y * _ScrollSpeed;

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half4 col = tex * _Color;
                return col;
            }
            ENDHLSL
        }
    }
}

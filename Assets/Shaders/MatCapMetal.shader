// ============================================================
//  SATC / MatCapMetal  —  Unity 6.5 · URP 17
//  Metal "fake" por MatCap para AR (no necesita reflejos ni Reflection Probe,
//  no se pone negro). Un solo material cubre:
//    - partes NO metalicas (cuerpo rojo): albedo + lambert normal.
//    - partes metalicas (rejillas/plata): se pintan con el MatCap de cromo.
//  La MASCARA de dónde es metal sale del canal R del mapa de Metalicidad
//  (igual que el que ya tienes: negro = no metal, blanco = metal).
//
//  Asignar:
//    _BaseMap  = Textura (albedo)        _MaskMap = Metalicidad (R = mascara metal)
//    _NormalMap = Normal                 _MatCap  = MatCap_Chrome.png
// ============================================================
Shader "SATC/MatCapMetal"
{
    Properties
    {
        [Header(Texturas)]
        _BaseMap   ("Textura (albedo)", 2D) = "white" {}
        _BaseTint  ("Tinte base", Color) = (1,1,1,1)
        _MaskMap   ("Metalicidad (R = mascara metal)", 2D) = "black" {}
        [Normal] _NormalMap ("Normal", 2D) = "bump" {}
        _NormalStrength ("Fuerza normal", Range(0,2)) = 1

        [Header(MatCap (metal))]
        _MatCap     ("MatCap (cromo)", 2D) = "black" {}
        _MatCapTint ("Tinte del metal", Color) = (1,1,1,1)
        _MatCapPower("Intensidad del metal", Range(0,2)) = 1
        _MetalShade ("Sombra sobre el metal (lambert)", Range(0,1)) = 0.25

        [Header(Iluminacion)]
        _Ambient ("Luz ambiente", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MaskMap);   SAMPLER(sampler_MaskMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_MatCap);    SAMPLER(sampler_MatCap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseTint, _MatCapTint;
                float  _NormalStrength, _MatCapPower, _MetalShade, _Ambient;
            CBUFFER_END

            struct Attributes {
                float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0;
            };
            struct Varyings {
                float4 positionHCS:SV_POSITION;
                float2 uv:TEXCOORD0;
                float3 normalWS:TEXCOORD1;
                float3 tangentWS:TEXCOORD2;
                float3 bitangentWS:TEXCOORD3;
                float3 positionWS:TEXCOORD4;
                float  fog:TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS = n.normalWS;
                OUT.tangentWS = n.tangentWS;
                OUT.bitangentWS = n.bitangentWS;
                OUT.fog = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN):SV_Target
            {
                // normal con detalle (tangent space -> world)
                float3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv), _NormalStrength);
                float3x3 TBN = float3x3(normalize(IN.tangentWS), normalize(IN.bitangentWS), normalize(IN.normalWS));
                float3 N = normalize(mul(nTS, TBN));

                // --- MatCap: normal a espacio de vista -> UV de la esfera ---
                float3 nVS = normalize(mul((float3x3)UNITY_MATRIX_V, N));
                float2 mcUV = nVS.xy * 0.5 + 0.5;
                float3 matcap = SAMPLE_TEXTURE2D(_MatCap, sampler_MatCap, mcUV).rgb * _MatCapTint.rgb * _MatCapPower;

                // --- base no metalica: albedo + lambert + ambient ---
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseTint.rgb;
                Light mainLight = GetMainLight();
                float ndl = saturate(dot(N, normalize(mainLight.direction)));
                float3 lit = albedo * (ndl * mainLight.color + _Ambient);

                // una pizca de lambert sobre el metal para que no quede plano
                float3 metal = matcap * lerp(1.0, (ndl*0.5+0.5), _MetalShade);

                // mascara de metal (canal R del mapa de metalicidad)
                float metalMask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, IN.uv).r;

                float3 col = lerp(lit, metal, metalMask);
                col = MixFog(col, IN.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

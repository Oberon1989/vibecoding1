Shader "Unlit/GpuVoxelDebug"
{
    Properties
    {
        _Color ("Color A", Color) = (0.3, 0.7, 1.0, 1.0)
        _ColorB ("Color B (Checker)", Color) = (0.15, 0.4, 0.6, 1.0)
        
        _CheckerScale ("Checker Size (Meters)", Float) = 1.0

        _Ambient ("Ambient", Range(0, 1)) = 0.20
        _LightStrength ("Light Strength", Range(0, 2)) = 1.0
        _Brightness ("Brightness", Range(0, 2)) = 1.0
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.05
        _Smoothness ("Smoothness", Range(0, 1)) = 0.25

        [Toggle]
        _DebugNormals ("Debug Normals", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // =================================================
            // BUFFERS & GLOBALVARS (Вне cbuffer)
            // =================================================
            StructuredBuffer<float3> VertexBuffer;
            StructuredBuffer<float3> NormalBuffer;
            
            // Вынесли сюда, так как этого параметра нет в секции Properties
            int _VertexBaseOffset; 

            // =================================================
            // MATERIAL PROPERTIES (Только то, что есть в Properties)
            // =================================================
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _ColorB;
                float _CheckerScale;

                float _Ambient;
                float _LightStrength;
                float _Brightness;
                float _SpecularStrength;
                float _Smoothness;
                float _DebugNormals;
            CBUFFER_END

            // =================================================
            // VERTEX OUTPUT
            // =================================================
            struct VertexOutput
            {
                float4 position : SV_POSITION;
                float3 trueWorldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 viewDirection : TEXCOORD2;
            };

            // =================================================
            // VERTEX
            // =================================================
            VertexOutput vert(uint vertexID : SV_VertexID)
            {
                VertexOutput output;

                uint compactVertexIndex = vertexID + (uint)_VertexBaseOffset;

                float3 localPosition = VertexBuffer[compactVertexIndex];
                float3 localNormal = NormalBuffer[compactVertexIndex];

                float3 worldPosition = mul(unity_ObjectToWorld, float4(localPosition, 1.0f)).xyz;
                float3 worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, localNormal));

                float normalLength = dot(worldNormal, worldNormal);
                if (normalLength < 0.000001f)
                {
                    worldNormal = float3(0.0f, 1.0f, 0.0f);
                }

                output.position = mul(UNITY_MATRIX_VP, float4(worldPosition, 1.0f));
                output.trueWorldPos = worldPosition;
                output.normal = worldNormal;
                output.viewDirection = normalize(_WorldSpaceCameraPos - worldPosition);

                return output;
            }

            // =================================================
            // FRAGMENT
            // =================================================
            float4 frag(VertexOutput input) : SV_Target
            {
                float3 normal = normalize(input.normal);

                // =================================================
                // DEBUG NORMALS
                // =================================================
                if (_DebugNormals > 0.5f)
                {
                    float3 normalColor = normal * 0.5f + 0.5f;
                    return float4(normalColor, 1.0f);
                }

                // =================================================
                // ШАХМАТКА
                // =================================================
                float checkScale = max(0.01f, _CheckerScale);
                
                // Небольшой сдвиг внутрь нормали, чтобы убрать мерцание на ребрах
                float3 biasedPos = input.trueWorldPos - normal * (checkScale * 0.01f);

                float3 grid = floor(biasedPos / checkScale);
                float checker = fmod(abs(grid.x) + abs(grid.y) + abs(grid.z), 2.0);
                
                float3 baseColor = (checker > 0.5f) ? _ColorB.rgb : _Color.rgb;

                // =================================================
                // LIGHTING
                // =================================================
                float3 lightDirection;
                if (_WorldSpaceLightPos0.w == 0.0f)
                {
                    lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                }
                else
                {
                    lightDirection = normalize(_WorldSpaceLightPos0.xyz - input.trueWorldPos);
                }

                float NdotL = saturate(dot(normal, lightDirection));
                float3 diffuse = _LightStrength * NdotL;
                float3 ambient = _Ambient.xxx;

                float3 viewDirection = normalize(input.viewDirection);
                float3 halfVector = normalize(lightDirection + viewDirection);
                float NdotH = saturate(dot(normal, halfVector));
                float specular = pow(NdotH, _Smoothness * 128.0f) * _SpecularStrength;

                float3 finalColor = (baseColor * (diffuse + ambient) + specular) * _Brightness;

                return float4(finalColor, 1.0f);
            }
            ENDHLSL
        }
    }
}

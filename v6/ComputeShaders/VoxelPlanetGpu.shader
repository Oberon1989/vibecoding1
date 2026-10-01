Shader "VoxelPlanet/VoxelPlanetGpu"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
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

            // Two-sided rendering.
            // Both front and back faces are rasterized.
           // Cull Off

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "UnityCG.cginc"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
            #include "UnityIndirect.cginc"

            StructuredBuffer<float3> _VertexBuffer;
            StructuredBuffer<uint> _IndexBuffer;

            float4 _BaseColor;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(
                uint svVertexID : SV_VertexID,
                uint svInstanceID : SV_InstanceID
            )
            {
                InitIndirectDrawArgs(0);

                Varyings output;

                uint drawIndex =
                    GetIndirectVertexID(svVertexID);

                uint vertexIndex =
                    _IndexBuffer[drawIndex];

                float3 positionWS =
                    _VertexBuffer[vertexIndex];

                output.positionCS =
                    mul(
                        UNITY_MATRIX_VP,
                        float4(positionWS, 1.0)
                    );

                return output;
            }

            float4 Frag(
                Varyings input
            ) : SV_Target
            {
                return _BaseColor;
            }

            ENDHLSL
        }
    }
}
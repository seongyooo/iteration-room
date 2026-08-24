Shader "IterationRoom/MirrorGlass"
{
    // THE GLASS OF A MIRROR, and the one thing it does that an ordinary material cannot is sample by
    // SCREEN POSITION.
    //
    // A planar reflection is rendered from a camera that stands behind the mirror looking back, so
    // what comes out is a picture of the room laid out in the reflection camera's screen space - not
    // in the mirror's UV space. Sampled with mesh UVs it would be a smeared texture stuck to a disc.
    // Sampled where the pixel actually IS on screen, it lines up with the room behind it.
    //
    // `mirror_trensum.glb` also has **no UVs at all** (the model dump says so), so there was never a
    // second option.
    //
    // **THE TINT IS WHAT ENDS THE INFINITE TUNNEL.** Two mirrors facing each other reflect each other
    // for ever; each bounce here comes back multiplied by this, so the fifth is at a quarter
    // brightness and the eighth is a tenth of one. It stops the way a real mirror tunnel stops -
    // dimmer and smaller until there is nothing - rather than at a number somebody chose.
    Properties
    {
        _Tint ("Tint", Color) = (0.76, 0.78, 0.82, 1)
        _ReflectionTex ("Reflection", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "MirrorGlass"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_ReflectionTex);
            SAMPLER(sampler_ReflectionTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.screenPos = ComputeScreenPos(positions.positionCS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                half3 reflected = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, uv).rgb;
                return half4(reflected * _Tint.rgb, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}

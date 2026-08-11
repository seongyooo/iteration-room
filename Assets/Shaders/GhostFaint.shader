// A ghost is an AFTERIMAGE, not a faint person. The difference is what this shader exists for.
//
// Dropping a lit material's alpha does not blur anything - every crease in the shirt and every
// feature of the face is still there, the whole figure is just dimmer, and the silhouette goes
// with it. What is wanted is the opposite: kill the interior detail and KEEP the outline.
//
// So the alpha comes from the fresnel term rather than from a constant. A surface turned toward
// the eye has its normal nearly parallel to the view direction, so it falls to _CoreAlpha and
// takes its detail with it; a surface at a grazing angle - which is exactly the band around the
// silhouette - rises to _RimAlpha. The figure ends up hollow with its edge intact, and because
// the edge is recomputed from the skinned normals every frame, the walk still reads perfectly.
//
// Unlit on purpose. The balloons are on URP/Lit because they are real objects that have to take
// the ceiling fixtures; an afterimage is not lit by anything in the room, and driving it from the
// light loop would only make it flicker as it walks under each downlight.
Shader "IterationRoom/GhostFaint"
{
    Properties
    {
        [MainColor] _BaseColor ("Core Colour", Color) = (0.70, 0.76, 0.86, 1)
        _RimColor   ("Rim Colour", Color) = (0.88, 0.93, 1.00, 1)
        // Higher = a tighter, harder outline. Below ~1.5 the whole figure hazes over and the
        // silhouette stops being a line; above ~4 the rim thins to a wire and reads as an outline
        // effect rather than a body.
        _RimPower   ("Rim Power", Range(0.5, 8)) = 2.2
        _RimAlpha   ("Rim Alpha", Range(0, 1)) = 0.75
        _CoreAlpha  ("Core Alpha", Range(0, 1)) = 0.06
        // Metres, measured up from the renderer's own origin - which sits at the ghost's feet.
        _BottomFade ("Bottom Fade", Float) = 0.35
        _SoftFade   ("Soft Intersection", Float) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GhostForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            // Off, or a ghost would occlude the ghosts behind it and Room3's four would punch
            // holes in each other. The cost is that overlapping ghosts accumulate alpha, which is
            // what _RimAlpha is kept below 1 for.
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float  _RimPower;
                float  _RimAlpha;
                float  _CoreAlpha;
                float  _BottomFade;
                float  _SoftFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
                float  baseY      : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                OUT.screenPos  = ComputeScreenPos(pos.positionCS);
                // World Y of the object's origin. Taken from the matrix rather than from the mesh's
                // own object space because the body is scaled to 0.377 - object-space height would
                // have to be un-scaled by hand, and would silently break the day that scale moves.
                OUT.baseY = UNITY_MATRIX_M._m13;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // The whole effect, in three lines.
                float facing = saturate(dot(N, V));
                float rim    = pow(1.0 - facing, _RimPower);
                float alpha  = lerp(_CoreAlpha, _RimAlpha, rim);

                // The feet dissolve instead of planting. A hard edge where the figure meets the
                // floor reads as a person standing on it; this reads as an afterimage left in the
                // room, which is the whole idea.
                alpha *= saturate((IN.positionWS.y - IN.baseY) / max(0.001, _BottomFade));

                // Ghosts have no colliders, so they walk through the bed and through each other.
                // Without this, the intersection is a hard cut across the body - the one place the
                // illusion breaks. Needs _CameraDepthTexture, which ConfigureLightingPipeline
                // turns on for exactly this.
                float2 uv        = IN.screenPos.xy / max(1e-5, IN.screenPos.w);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                alpha *= saturate((sceneDepth - IN.screenPos.w) / max(0.001, _SoftFade));

                half3 colour = lerp(_BaseColor.rgb, _RimColor.rgb, rim);
                return half4(colour, saturate(alpha));
            }
            ENDHLSL
        }
    }

    // No shadow caster and no depth pass, deliberately. See BuildGhostPrefab for why the shadows
    // went with the opacity.
    Fallback Off
}

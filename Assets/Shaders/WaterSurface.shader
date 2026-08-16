// WATER, AND THE REASON IT NEEDS A SHADER OF ITS OWN.
//
// Everything before this was URP/Lit with the alpha turned down and a texture scrolling over it,
// which is a tinted pane of glass moving sideways. It fails for a specific reason worth naming: a
// lit transparent surface has ONE brightness across the whole of it, and water has almost none of
// its appearance in its own colour. What you actually see when you look at a running tap is
//
//   - the room BEHIND it, bent (refraction),
//   - the room AROUND it, mirrored off the surface (reflection),
//   - a hard specular highlight where the light happens to line up,
//   - and all three of those weighted by the ANGLE you are looking at it from (fresnel).
//
// Nothing in that list is a colour, which is why making it bluer and more transparent never helped.
// This shader is those four things and a noise field to break them up.
//
// NEARLY COLOURLESS ON PURPOSE. `_BaseColor` is a faint tint at very low alpha; the visible body of
// the water comes from what it reflects and refracts. A blue stream reads as coloured jelly.
Shader "IterationRoom/WaterSurface"
{
    Properties
    {
        [MainColor] _BaseColor ("Tint", Color) = (0.82, 0.90, 0.94, 0.10)
        // How much of the far side shows through, and how far it is bent. Refraction is a screen-space
        // cheat - it offsets where the opaque texture is sampled - so large values smear rather than
        // bend, which is why this is small.
        _Refraction ("Refraction", Range(0, 0.12)) = 0.035
        _Reflection ("Reflection", Range(0, 1)) = 0.55
        _Smoothness ("Smoothness", Range(0, 1)) = 0.95
        _SpecPower  ("Specular Power", Range(8, 512)) = 190
        _SpecGain   ("Specular Gain", Range(0, 8)) = 2.6

        // Edge-on water is bright and opaque; head-on it is a window. This is most of what sells it.
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.4
        _EdgeAlpha    ("Edge Alpha", Range(0, 1)) = 0.72
        _CoreAlpha    ("Core Alpha", Range(0, 1)) = 0.10

        // ABSORPTION, AND IT IS WHAT MAKES WATER VISIBLE IN A WHITE ROOM.
        //
        // Physically clear water in a white box is INVISIBLE - it reflects white, refracts white, and
        // the more correct the material is the less there is to see. Real water in a white basin still
        // reads, and not because of its colour: at a grazing angle you are looking through far more of
        // it, so more light is absorbed and the edge goes darker and cooler. That darkening is the
        // signal. This is that term, and it does the job a tint cannot - a bluer material just looks
        // like coloured plastic, while this stays colourless where it is thin.
        _DeepColor    ("Deep Colour", Color) = (0.36, 0.55, 0.62, 1)
        _DeepStrength ("Deep Strength", Range(0, 1)) = 0.55

        // The noise that stops it being a smooth solid of revolution. Perturbs the NORMAL, so it
        // shows up as the highlight and the reflection breaking apart rather than as a pattern
        // painted on - which is what it does in real water.
        _NoiseScale  ("Noise Scale", Float) = 14
        _NoiseSpeed  ("Noise Speed", Float) = 2.2
        _NoiseAmount ("Noise Amount", Range(0, 1)) = 0.42

        // Vertex wobble, in metres. Breaks the silhouette so the stream is not a perfect cylinder.
        _WobbleAmp   ("Wobble Amplitude", Float) = 0.006
        _WobbleFreq  ("Wobble Frequency", Float) = 7
        _WobbleSpeed ("Wobble Speed", Float) = 3.5

        // Softens the line where the water meets the floor or the basin, instead of a hard cut.
        _DepthFade ("Depth Fade", Float) = 0.10
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
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            // OFF, and this is what gives the stream volume for free. With back faces drawn, the far
            // wall of the tube shows through the near one, so the silhouette - where both are seen
            // edge-on at once - doubles up and reads as thicker. That is exactly how a real column of
            // water looks, and it costs one word.
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Refraction;
                float  _Reflection;
                float  _Smoothness;
                float  _SpecPower;
                float  _SpecGain;
                float  _FresnelPower;
                float  _EdgeAlpha;
                float  _CoreAlpha;
                float4 _DeepColor;
                float  _DeepStrength;
                float  _NoiseScale;
                float  _NoiseSpeed;
                float  _NoiseAmount;
                float  _WobbleAmp;
                float  _WobbleFreq;
                float  _WobbleSpeed;
                float  _DepthFade;
            CBUFFER_END

            // Value noise, hashed rather than sampled. A texture would tile, and tiling is the one
            // thing this is here to avoid - the whole job of the noise is that nothing repeats.
            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0,0,0)), Hash(i + float3(1,0,0)), f.x),
                                 lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                                 lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            // Two octaves is enough here and a third is not free - this runs on every pixel of a
            // full-height column that can fill the screen.
            float Fbm(float3 p)
            {
                return Noise(p) * 0.65 + Noise(p * 2.17 + 3.1) * 0.35;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posOS = IN.positionOS.xyz;

                // THE SILHOUETTE IS BROKEN IN THE VERTEX STAGE, because no amount of shading fixes an
                // outline that is a perfect circle. Pushed along the object-space normal, so a tube
                // gets thinner and fatter down its length rather than bending sideways as a whole -
                // which is what a falling stream does as it necks and swells.
                float wob = Fbm(float3(posOS.xz * _WobbleFreq, posOS.y * _WobbleFreq + _Time.y * _WobbleSpeed));
                posOS += IN.normalOS * ((wob - 0.5) * 2.0 * _WobbleAmp);

                VertexPositionInputs pos = GetVertexPositionInputs(posOS);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                OUT.screenPos  = ComputeScreenPos(pos.positionCS);
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // A two-face normal, so the back wall of the tube is lit as a surface facing the eye
                // rather than as one facing away - without this every back face goes black.
                if (dot(N, V) < 0) N = -N;

                // THE NORMAL IS PERTURBED, NOT THE COLOUR. Sampling the field at three offsets gives a
                // gradient, and bending the normal along it makes the highlight and the reflection
                // crawl over the surface. Tint the albedo with noise instead and it reads as dirt.
                float3 np = IN.positionWS * _NoiseScale + float3(0, -_Time.y * _NoiseSpeed, 0);
                float  n0 = Fbm(np);
                float  nx = Fbm(np + float3(0.35, 0, 0));
                float  nz = Fbm(np + float3(0, 0, 0.35));
                float3 bend = float3(nx - n0, 0, nz - n0) * _NoiseAmount * 6.0;
                N = normalize(N + bend);

                float2 screenUV = IN.screenPos.xy / max(1e-5, IN.screenPos.w);

                // REFRACTION: the room behind, sampled off-axis. Screen-space, so it bends what is
                // already on screen rather than what is truly behind - which is invisible at this
                // strength and is the standard trade for not tracing anything.
                float2 offset = N.xz * _Refraction;
                half3 behind  = SampleSceneColor(screenUV + offset);

                // REFLECTION: the room around, off the probe this cycle already bakes. Water at this
                // smoothness is very nearly a mirror at grazing angles, which is where fresnel puts
                // most of the weight.
                float3 R = reflect(-V, N);
                half3 env = GlossyEnvironmentReflection(R, IN.positionWS, 1.0 - _Smoothness, 1.0, screenUV);

                // The specular, from the one directional light this facility does not have - so it is
                // the main light, which is whichever ceiling fixture is nearest.
                Light mainLight = GetMainLight();
                float3 H = normalize(mainLight.direction + V);
                float spec = pow(saturate(dot(N, H)), _SpecPower) * _SpecGain;

                // FRESNEL, which weights everything above: head-on it is a window, edge-on it is a
                // mirror. This is the single term that most makes it read as water rather than glass.
                float fres = pow(1.0 - saturate(dot(N, V)), _FresnelPower);

                half3 colour = lerp(behind, env, saturate(fres * _Reflection + _Reflection * 0.15));
                colour = lerp(colour, colour * _BaseColor.rgb + _BaseColor.rgb * 0.15, _BaseColor.a);

                // ABSORPTION, BEFORE THE HIGHLIGHT. The path through the water is longest where the
                // surface turns away, so that is where it darkens - which is the same `fres` term the
                // reflection uses, for the same underlying reason. Applied to the refracted colour
                // rather than added, because absorption REMOVES light; adding a dark colour would grey
                // the highlight out with it.
                colour *= lerp(half3(1, 1, 1), _DeepColor.rgb, saturate(fres * _DeepStrength));

                // And the specular last, so it stays white and sharp on top of a darkened edge - which
                // is the contrast that makes a stream read against a white wall.
                colour += spec * mainLight.color;

                float alpha = lerp(_CoreAlpha, _EdgeAlpha, fres);
                alpha = saturate(alpha + spec * 0.5);

                // WHERE IT MEETS SOMETHING SOLID. Without this the stream ends in a straight line
                // across the floor, which is the one place the eye is guaranteed to be looking.
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                alpha *= saturate((sceneDepth - IN.screenPos.w) / max(0.001, _DepthFade));

                return half4(colour, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}

// SPDX-License-Identifier: MIT
Shader "PmxImport/MMD Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Diffuse", Color) = (1,1,1,1)
        _AmbientColor("Ambient tint", Color) = (0,0,0,1)
        _SpecColor("Specular", Color) = (0,0,0,1)
        _SpecPower("Specular exponent", Float) = 16
        _ToonTex("Toon ramp", 2D) = "white" {}
        _UseToonTexture("Use toon ramp", Float) = 0
        _SphereTex("Sphere texture", 2D) = "white" {}
        _Sphere("Sphere mode", Float) = 0
        _EdgeColor("Outline", Color) = (0,0,0,1)
        _EdgeSize("Outline size", Float) = 0
        _EdgeScale("Outline pixels", Float) = 1.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Culling", Float) = 2
        _AlphaClip("Alpha clipping", Float) = 0
        _Cutoff("Alpha threshold", Range(0,1)) = 0.3
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _ZWrite("Depth write", Float) = 1
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_ToonTex); SAMPLER(sampler_ToonTex);
        TEXTURE2D(_SphereTex); SAMPLER(sampler_SphereTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _AmbientColor, _SpecColor, _EdgeColor;
            float _SpecPower, _UseToonTexture, _Sphere, _EdgeSize, _EdgeScale;
            float _AlphaClip, _Cutoff, _Cull, _Surface, _ZWrite, _SrcBlend, _DstBlend;
        CBUFFER_END
        struct InputVertex { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 extra : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct FragmentInput { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float2 extraUV : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
        FragmentInput ModelVertex(InputVertex input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            FragmentInput output = (FragmentInput)0; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap); output.extraUV = input.extra.xy;
            return output;
        }
        half4 SurfaceSample(float2 uv)
        {
            half4 value = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            if (_AlphaClip > 0.5) clip(value.a - _Cutoff);
            return value;
        }
        half4 ModelFragment(FragmentInput input, bool front : SV_IsFrontFace) : SV_Target
        {
            half4 surface = SurfaceSample(input.uv);
            float3 normal = normalize(input.normalWS) * (front ? 1 : -1);
            float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
            Light main = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
            float exposure = saturate(dot(normal, main.direction)) * main.shadowAttenuation * main.distanceAttenuation;
            half3 ramp = _UseToonTexture > 0.5 ? SAMPLE_TEXTURE2D(_ToonTex, sampler_ToonTex, float2(.5, exposure)).rgb : lerp(half3(.35,.35,.35), half3(1,1,1), smoothstep(.25,.55,exposure));
            half3 albedo = surface.rgb;
            if (_Sphere > 0.5)
            {
                float3 viewNormal = TransformWorldToViewDir(normal, true);
                float2 sphereUV = _Sphere > 2.5 ? input.extraUV : viewNormal.xy * .5 + .5;
                half3 sphere = SAMPLE_TEXTURE2D(_SphereTex, sampler_SphereTex, sphereUV).rgb;
                albedo = _Sphere > 1.5 && _Sphere < 2.5 ? albedo + sphere : albedo * sphere;
            }
            half3 result = albedo * (main.color * ramp + max(SampleSH(normal), half3(0,0,0)) * .35 + _AmbientColor.rgb);
            float highlight = pow(saturate(dot(normal, normalize(view + main.direction))), max(1, _SpecPower));
            result += _SpecColor.rgb * main.color * highlight * main.shadowAttenuation;
            #ifdef _ADDITIONAL_LIGHTS
            uint count = GetAdditionalLightsCount();
            for (uint i = 0; i < count; i++) { Light light = GetAdditionalLight(i, input.positionWS); result += albedo * light.color * saturate(dot(normal, light.direction)) * light.distanceAttenuation * light.shadowAttenuation; }
            #endif
            return half4(result, surface.a);
        }
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull] ZWrite [_ZWrite] Blend [_SrcBlend] [_DstBlend]
            HLSLPROGRAM
            #pragma vertex ModelVertex
            #pragma fragment ModelFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front ZWrite [_ZWrite] Blend [_SrcBlend] [_DstBlend]
            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing
            FragmentInput OutlineVertex(InputVertex input)
            {
                FragmentInput result = ModelVertex(input);
                float4 tip = TransformWorldToHClip(result.positionWS + normalize(result.normalWS));
                float2 direction = tip.xy / max(abs(tip.w), .0001) - result.positionCS.xy / max(abs(result.positionCS.w), .0001);
                direction /= max(length(direction), .0001);
                result.positionCS.xy += direction * _EdgeSize * _EdgeScale * max(input.color.a, 0) * 2 / _ScreenParams.xy * result.positionCS.w;
                return result;
            }
            half4 OutlineFragment(FragmentInput input) : SV_Target { clip(_EdgeSize - .00001); SurfaceSample(input.uv); return _EdgeColor; }
            ENDHLSL
        }
        Pass
        {
            Name "Depth"
            Tags { "LightMode"="DepthOnly" }
            Cull [_Cull] ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex ModelVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            half4 DepthFragment(FragmentInput input) : SV_Target { SurfaceSample(input.uv); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "Shadow"
            Tags { "LightMode"="ShadowCaster" }
            Cull [_Cull] ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            FragmentInput ShadowVertex(InputVertex input)
            {
                FragmentInput output = ModelVertex(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - output.positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, normalize(output.normalWS), lightDirection));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
                #endif
                return output;
            }
            half4 ShadowFragment(FragmentInput input) : SV_Target { SurfaceSample(input.uv); return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

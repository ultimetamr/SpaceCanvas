Shader "SpaceCanvas/UI/RoundedPanel"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 shapePosition : TEXCOORD0;
                float4 shape : TEXCOORD1;
                float4 border : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float4 shapePosition : TEXCOORD0;
                float4 shape : TEXCOORD1;
                fixed4 border : TEXCOORD2;
                float2 localPosition : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            fixed4 _Color;
            float4 _ClipRect;
            int _UIVertexColorAlwaysGammaSpace;
            v2f vert(appdata input)
            {
                v2f output = (v2f)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.localPosition = input.vertex.xy;
                output.shapePosition = input.shapePosition; output.shape = input.shape;
                output.border = input.border; output.color = input.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace)
                {
                    output.color.rgb = GammaToLinearSpace(output.color.rgb);
                    output.border.rgb = GammaToLinearSpace(output.border.rgb);
                }
                #endif
                output.color *= _Color;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 halfSize = input.shapePosition.zw;
                float2 q = abs(input.shapePosition.xy) - halfSize + input.shape.x;
                float distanceToEdge = length(max(q, 0)) + min(max(q.x, q.y), 0) - input.shape.x;
                // Follow the projected pixel footprint independently in each eye.
                float pixel = max(fwidth(distanceToEdge), .0001);
                float coverage = saturate(.5 - distanceToEdge / pixel);
                float borderWidth = input.shape.y > 0 ? max(input.shape.y, pixel * 1.25) : 0;
                float border = input.shape.y > 0 ? saturate(.5 + (distanceToEdge + borderWidth) / pixel) : 0;
                float top = saturate(input.shapePosition.y / max(halfSize.y * 2, .0001) + .5);
                fixed4 result = input.color;
                result.rgb = lerp(result.rgb, 1, top * input.shape.z);
                result.rgb = lerp(result.rgb, input.border.rgb, border * input.border.a);
                result.a *= coverage;
                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(input.localPosition, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - .001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}

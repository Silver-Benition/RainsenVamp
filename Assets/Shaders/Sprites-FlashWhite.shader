// ============================================================================
// Sprites-FlashWhite.shader
// 功能：在 Unity 默认 Sprite 渲染基础上，增加 _FlashAmount 通道。
//       当 _FlashAmount = 1 时整个精灵变为 _FlashColor（纯白闪烁）；
//       当 _FlashAmount = 0 时正常渲染原始贴图。
// 用法：挂在怪物 SpriteRenderer 上，通过 MaterialPropertyBlock 驱动 _FlashAmount，
//       零材质实例、零 GC，适合海量同屏实体。
// 兼容：Built-in Render Pipeline（若项目使用 URP，需改为 URP Sprite Unlit 变体）。
// ============================================================================
Shader "Custom/Sprites-FlashWhite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,1)) = 0

        // 以下为 Unity Sprite 默认所需属性（保持兼容）
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment FlashFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON

            #include "UnityCG.cginc"

            // ---- Sprite 顶点结构 ----
            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _FlashColor;
            fixed _FlashAmount;
            fixed4 _RendererColor;

            v2f SpriteVert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color * _RendererColor;

                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif

                return OUT;
            }

            fixed4 FlashFrag(v2f IN) : SV_Target
            {
                // 采样原始精灵贴图
                fixed4 col = tex2D(_MainTex, IN.texcoord) * IN.color;

                // 核心：将 RGB 向 _FlashColor 做线性插值，保留原始 Alpha
                // _FlashAmount = 0 → 正常显示；_FlashAmount = 1 → 纯闪白
                col.rgb = lerp(col.rgb, _FlashColor.rgb * col.a, _FlashAmount);

                // Unity Sprite 默认预乘 Alpha
                col.rgb *= col.a;

                return col;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}

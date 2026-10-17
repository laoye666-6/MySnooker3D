// =====================================================================================
// BallShadow.shader —— 贴地假阴影（v1.0）
//
// 纯程序化径向衰减：不采样任何贴图、不使用任何 shader keyword、无光照、单变体。
//
// 为什么不用 Standard + _ALPHABLEND_ON（v0.56 的做法）：
//   · 运行时 new Material(Shader.Find("Standard")) 再 EnableKeyword("_ALPHABLEND_ON")，
//     构建时并不会把该 keyword 变体编进包（只有构建期存在的材质才保证被收集）——
//     真机/MuMu 上可能拿到不透明变体 → 阴影退化成硬边黑方块（v1.0 装机实测）。
//   · 贴图 alpha 在移动端压缩格式下也有丢失风险，同样退化成黑块。
// 这里去掉全部不确定性：一个 Pass、Alpha 混合、uv 半径求 alpha，GLES3 行为确定。
//
// _Color.a 由 BallController 每帧经 MaterialPropertyBlock 写入（球跳起时淡出）。
// =====================================================================================
Shader "Snooker/BallShadow"
{
    Properties
    {
        _Color ("Shadow Color", Color) = (0, 0, 0, 0.85)
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 d = i.uv * 2.0 - 1.0;
                float r = sqrt(dot(d, d));
                float a = saturate(1.0 - r);       // 中心密实 → 边缘透明
                a = a * a;                         // 平滑肩部
                return fixed4(0.0, 0.0, 0.0, a * _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}

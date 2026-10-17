// =====================================================================================
// ColorGrade.shader —— ACES 全屏调色四边形（v0.56/57 光影追平）
//
// 用途：ScreenGrade 在主相机前方架一台"展示相机"，只渲染本 shader 的全屏四边形；
//       四边形采样主相机的 RT（_MainTex）并完成：ACES 色调映射 + 对比度 + 暗角。
//       全链路 = 主相机渲 RT + 展示相机画四边形 —— 纯多相机路径，零帧缓冲拷贝
//       （GrabPass/命令缓冲/OnRenderImage 读帧缓冲在本工程 MuMu GLES3 上均翻过车，
//       多相机是唯一被真机验证的确定性方案，踩坑 39/43）。
//
// PCOL 对照：它的每个 shader fragment 里都有同一条 ACES（Narkowicz 拟合）——
//       高光柔和滚落 + 暗部上提，是"写实光影"观感的最后一块。
//
// GLES 铁律：pow 底数全部 max(…,1e-4)；无循环、无分支采样。
// 母本在 E:\Snooker\shaders\，sync.bat 只同步 *.cs，需手工拷到 Assets\Resources\Shaders\。
// 改完必须查 Console 无 "Shader error"（Fallback 会吞编译错误，踩坑 42）。
// =====================================================================================
Shader "Hidden/ColorGrade"
{
    Properties
    {
        _MainTex ("Scene RT", 2D) = "black" {}
        _Exposure ("Exposure", Range(0.2, 3.0)) = 1.06
        _Contrast ("Contrast", Range(0.5, 2.0)) = 1.06
        _Lift ("Shadow Lift", Range(0, 0.2)) = 0.035
        _Vignette ("Vignette", Range(0, 1)) = 0.24
    }
    SubShader
    {
        Tags { "Queue"="Geometry-100" "RenderType"="Opaque" "PreviewType"="Plane" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Exposure, _Contrast, _Lift, _Vignette;

            struct appdata_t { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            // ACES 近似（Narkowicz 2015），输入线性、输出线性
            float3 ACESFilm(float3 x)
            {
                return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;

                // gamma 空间 → 线性（Built-in 工程是 Gamma 色彩空间，ACES 要吃线性）
                c = pow(max(c, 1e-4), 2.2);
                c *= _Exposure;
                c = ACESFilm(c);

                // 暗部提亮（ACES 吸黑，补偿一点环境感）
                c += _Lift * (1.0 - c);

                // 对比度 S 曲线（围绕中灰 0.42，避开死黑死白）
                c = saturate((c - 0.42) * _Contrast + 0.42);

                // 回 gamma 空间
                c = pow(max(c, 1e-4), 1.0 / 2.2);

                // 暗角：椭圆径向，只压四角（PCOL 的"灯只照亮桌面"氛围）
                float2 d = (i.uv - 0.5) * float2(1.15, 1.0);
                float vig = saturate(1.0 - dot(d, d) * _Vignette * 2.2);
                c *= lerp(0.72, 1.0, vig);

                return fixed4(c, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}

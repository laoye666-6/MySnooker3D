// =====================================================================================
// LiquidGlass.shader —— iOS 26 风格「液态玻璃」：重磨砂 + 边缘透镜拉丝（v0.47 重写）
//
// 参考质感（DeepSeek 输入框风格的液态玻璃）：
//   · 透过玻璃的背景被【重度高斯模糊】并整体提亮偏白（奶白磨砂感）；
//   · 玻璃边界处背景被【挤压+沿边缘方向拉丝】——法向把界外内容"拉进来"，
//     切向把边缘内容"抹开"，形成边缘一圈流动的光带（厚玻璃边的透镜效应）；
//   · 顶部内侧一道受光高光、底部内侧一道厚度阴影、最边缘一圈细亮边；
//   · 中心相对通透（仍能看到模糊后的色块），越靠边变形越强。
//
// 场景源（双 RT，GlassSceneCamera 提供）：
//   _GlassScene —— 半分辨率原始场景（清晰折射层，_Crisp 混入）；
//   _GlassBlur  —— 四分之一分辨率 + 两轮分离高斯（磨砂主体）。
//   确定性方案沿用 v0.44 结论：GrabPass/命令缓冲/OnRenderImage 在 MuMu GLES3 上
//   间歇黑帧，只有"多相机渲染 + RT→RT Blit"可靠。
//
// 几何输入：UIGlass 的 uv0 = [-1,1] 矩形局部坐标 + 材质上的 _RectHW(半宽高,px)
// 与 _CornerR(圆角,px)，fragment 内用圆角矩形 SDF 求到边缘距离/法线/切向
// （带符号距离场纯解析计算，无纹理、无循环，GLES2/3 都安全）。
//
// GLES 铁律：所有 pow() 的底数必须 max(…, 1e-4)——pow(0,k) 在部分 GLES 驱动返回
// NaN，曾把整片玻璃中心染黑（编辑器 D3D11 正常，极具迷惑性）。
//
// 母本在 E:\Snooker\shaders\，sync.bat 只同步 *.cs，需手工拷到 Assets\Resources\Shaders\。
// =====================================================================================
Shader "UI/LiquidGlass"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _BlurTexels ("Extra Blur (blur-RT texels)", Range(0, 8)) = 2
        _Frost ("Frost Whiten", Range(0, 1)) = 0.30
        _Lift ("Brightness Lift", Range(0, 1)) = 0.10
        _Desat ("Desaturation", Range(0, 1)) = 0.10
        _Crisp ("Clear Mix (1=clear)", Range(0, 1)) = 0.10
        _Refr ("Dome Refraction (px)", Range(0, 40)) = 10
        _Bulge ("Dome Bulge", Range(0.1, 3)) = 1.3
        _Lens ("Edge Lens (px)", Range(0, 80)) = 30
        _Streak ("Edge Streak (px)", Range(0, 120)) = 36
        _EdgeW ("Edge Band (px)", Range(4, 80)) = 24
        _Sheen ("Top Sheen", Range(0, 1)) = 0.28
        _InnerSh ("Bottom Inner Shadow", Range(0, 1)) = 0.12
        _Rim ("Edge Rim Light", Range(0, 1)) = 0.32
        _EdgeDark ("Edge Darken", Range(0, 1)) = 0.10
        _SpecInt ("Specular Strength", Range(0, 2)) = 0.45
        _EdgeAlpha ("Edge Alpha Boost", Range(0, 1)) = 0.50
        _Glow ("Light Halos Strength", Range(0, 1.5)) = 0.18
        _SoftMode ("Soft Edge Mode (shadow)", Float) = 0
        _SoftFade ("Soft Edge Fade (px)", Float) = 16
        _RectHW ("Rect Half Size (px)", Vector) = (100, 50, 0, 0)
        _CornerR ("Corner Radius (px)", Float) = 24
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
               "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
                  ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            sampler2D _GlassScene;                  // 半分辨率清晰场景
            sampler2D _GlassBlur;                   // 四分辨率磨砂场景（两轮分离高斯）
            float4 _GlassScene_TexelSize;
            float4 _GlassBlur_TexelSize;
            fixed4 _Color;
            float _BlurTexels, _Frost, _Lift, _Desat, _Crisp, _Refr, _Bulge;
            float _Lens, _Streak, _EdgeW, _Sheen, _InnerSh, _Rim, _EdgeDark;
            float _SpecInt, _EdgeAlpha, _Glow;
            float _SoftMode, _SoftFade;
            float4 _RectHW;                         // x=半宽 y=半高（px）
            float _CornerR;

            struct appdata_t { float4 vertex : POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f      { float4 vertex : SV_POSITION; fixed4 color : COLOR;
                              float4 scr : TEXCOORD0; float2 uv : TEXCOORD1; };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.scr = ComputeScreenPos(o.vertex);
                o.uv = v.texcoord;                  // [-1,1] 矩形局部坐标
                return o;
            }

            // ---- 圆角矩形 SDF（px 空间；b = 半尺寸 - 圆角）----
            float SdRBox(float2 q, float2 b, float r)
            {
                float2 w = abs(q) - b;
                return length(max(w, 0.0)) + min(max(w.x, w.y), 0.0) - r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 q = i.uv * _RectHW;                        // [-1,1] → 像素（各向同性）
                float2 b = max(_RectHW.xy - _CornerR, float2(0.0, 0.0));
                float d = SdRBox(q, b, _CornerR);                 // <0 在玻璃内，0 在边缘

                // ---- 软边模式（投影专用）：不采场景，中心实、向网格边缘淡出 ----
                if (_SoftMode > 0.5)
                {
                    float a = i.color.a * smoothstep(0.0, max(_SoftFade, 1.0), -d);
                    return fixed4(i.color.rgb, a);
                }

                // ---- 边缘 SDF 法线/切向（数值梯度，SDF 无纹理、代价可忽略）----
                float e = 1.5;
                float2 g = float2(SdRBox(q + float2(e, 0), b, _CornerR) - SdRBox(q - float2(e, 0), b, _CornerR),
                                  SdRBox(q + float2(0, e), b, _CornerR) - SdRBox(q - float2(0, e), b, _CornerR));
                float2 nrm = normalize(g + float2(1e-5, 1e-5));   // 边缘外法线（px 空间，屏幕对齐）
                float2 tng = float2(-nrm.y, nrm.x);               // 沿边缘方向

                float band = 1.0 - saturate((-d) / max(_EdgeW, 4.0));  // 1=贴边 → 0=带外
                float band2 = band * band;

                // ---- 采样偏移：中心穹顶折射 + 边缘法向透镜（把界外内容拉进来）----
                float3 nd = normalize(float3(i.uv * _Bulge, 1.0));
                float fres = pow(max(1.0 - nd.z, 1e-4), 1.7);
                float2 lens = (nd.xy * _Refr + nrm * _Lens * band2);       // px
                float2 uvS = (i.scr.xy / i.scr.w) + lens * _GlassScene_TexelSize.xy;

                // ---- 磨砂主体：磨砂 RT 上小核 9 点模糊 ----
                float2 bt = _GlassBlur_TexelSize.xy * _BlurTexels;
                float2 uvB = (i.scr.xy / i.scr.w) + lens * _GlassBlur_TexelSize.xy;
                half3 c = tex2D(_GlassBlur, uvB).rgb * 2.0;
                c += tex2D(_GlassBlur, uvB + float2( bt.x,  0)).rgb;
                c += tex2D(_GlassBlur, uvB + float2(-bt.x,  0)).rgb;
                c += tex2D(_GlassBlur, uvB + float2( 0,  bt.y)).rgb;
                c += tex2D(_GlassBlur, uvB + float2( 0, -bt.y)).rgb;
                c += tex2D(_GlassBlur, uvB + bt).rgb;
                c += tex2D(_GlassBlur, uvB - bt).rgb;
                c += tex2D(_GlassBlur, uvB + float2( bt.x, -bt.y)).rgb;
                c += tex2D(_GlassBlur, uvB + float2(-bt.x,  bt.y)).rgb;
                c *= (1.0 / 10.0);

                // ---- 清晰折射层（少量混入，保住"玻璃后面有东西"的通透）----
                half3 clear = tex2D(_GlassScene, uvS).rgb;
                c = lerp(c, clear, _Crisp);

                // ---- 边缘切向拉丝：沿边缘方向 5 点加权拖影，越贴边越强 ----
                float span = _Streak * band2;                     // px
                half3 str = tex2D(_GlassBlur, uvB + tng * (span * -1.0) * _GlassBlur_TexelSize.xy).rgb * 0.14
                          + tex2D(_GlassBlur, uvB + tng * (span * -0.5) * _GlassBlur_TexelSize.xy).rgb * 0.21
                          + c * 0.30
                          + tex2D(_GlassBlur, uvB + tng * (span * 0.5) * _GlassBlur_TexelSize.xy).rgb * 0.21
                          + tex2D(_GlassBlur, uvB + tng * (span * 1.0) * _GlassBlur_TexelSize.xy).rgb * 0.14;
                c = lerp(c, str, band2 * 0.9);

                // ---- 三盏游动光源（保留但很淡：给 HUD 一点"活"气）----
                float2 lp1 = float2(sin(_Time.y * 0.53 + 1.7) * 0.70, cos(_Time.y * 0.41 + 0.3) * 0.55);
                float2 lp2 = float2(cos(_Time.y * 0.33 + 4.2) * 0.60, sin(_Time.y * 0.61 + 2.9) * 0.60);
                float2 lp3 = float2(sin(_Time.y * 0.44 + 5.1) * 0.50, sin(_Time.y * 0.37 + 1.2) * 0.65);
                float d1 = dot(i.uv - lp1, i.uv - lp1);
                float d2 = dot(i.uv - lp2, i.uv - lp2);
                float d3 = dot(i.uv - lp3, i.uv - lp3);
                half3 glow = half3(1.00, 0.95, 0.86) * exp(-d1 * 6.0) * 0.5
                           + half3(0.80, 0.88, 1.00) * exp(-d2 * 7.0) * 0.35
                           + half3(1.00, 0.92, 0.72) * exp(-d3 * 5.0) * 0.4;
                glow *= _Glow;

                // ---- 玻璃光学合成 ----
                half3 col = c * i.color.rgb;
                float lum = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(col, half3(lum, lum, lum), _Desat);    // 去饱和（v0.53：可调，默认调低→更透）
                col = lerp(col, half3(1, 1, 1), _Frost);          // 奶白提亮（磨砂主体）
                float spec = pow(max(dot(nd, normalize(float3(-0.35, 0.55, 0.75))), 1e-4), 20.0);
                col += _SpecInt * spec * 0.35 + glow;             // 左上镜面高光 + 游灯光晕
                col += _Sheen * band2 * saturate(nrm.y * 1.4);    // 顶部内侧受光高光
                col *= 1.0 - _InnerSh * band * saturate(-nrm.y);  // 底部内侧厚度阴影
                col *= 1.0 - _EdgeDark * band2;                   // 边缘轻收暗（亮背景上的可读性）
                col += _Rim * pow(max(band, 1e-4), 6.0);          // 最边缘细亮边（底数钳住：GLES pow(0,k)=NaN）
                col = col * (1.0 - _Lift) + _Lift;

                // 边缘更"实"：alpha 随贴边程度抬升。
                // v0.54 关键修复：必须【乘法】抬升（a * (1+edge)），不能加法（a + edge）。
                // 加法时 a=0 的元素在边缘仍有 0.4+ 的 alpha —— 菜单/面板淡出后只留下
                // 一圈"玻璃轮廓幽灵"叠在台面上（v0.53 实测：菜单按钮的两条胶囊轮廓
                // 在游戏画面里一直可见，看起来像 UI 与台面重叠）。乘法保证 a=0 → 彻底消失。
                float alpha = saturate(i.color.a * (1.0 + _EdgeAlpha * band2));
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}

// =====================================================================================
// LiquidGlass.shader —— iOS「液态玻璃」清澈折射 + 反射 + 游动光源光晕（v0.44f）
//
// 场景源：_GlassScene（GlassSceneCamera 副相机每帧渲染的半分辨率场景纹理，
// 确定性方案——GrabPass/命令缓冲/OnRenderImage 三种帧缓冲拷贝在 MuMu GLES3 上
// 间歇性黑帧/清屏色，全部弃用，见 GlassSceneCamera.cs 头注释）。
//
// frag 组成：
//   1) 玻璃穹顶法线：UIGlass 网格的 uv0 是 [-1,1] 局部坐标，据此生成凸面法线
//      （中心朝上、边缘外倾，曲率 _Bulge）；
//   2) 折射：按法线横向偏移采样场景（_Refr 像素）——真正的"透过玻璃看到弯掉的
//      背景"，每帧实时；清澈感 = 低模糊 + 高清晰占比（_Crisp），水玻璃透而略弯；
//   3) 反射：菲涅尔边缘亮环 + 左上方向镜面高光；
//   4) 光晕：三盏程序化"游灯"（不同频率/相位的正弦漂移，伪随机而平滑），
//      各带暖白/冷蓝/淡金颜色，在玻璃上投出实时移动的光斑（高斯衰减）；
//   5) 边缘 alpha 随菲涅尔抬升——玻璃边缘密度更高、更"实"，中心保持极透。
//
// GLES 铁律：所有 pow() 的底数必须 max(…, 1e-4)——pow(0,k) 在部分 GLES 驱动返回
// NaN，曾把整片玻璃中心染黑（编辑器 D3D11 正常，极具迷惑性）。
//
// 形状（圆角矩形/胶囊/圆形）由 UIGlass 的顶点网格负责。母本在 E:\Snooker\shaders\，
// sync.bat 只同步 *.cs，需手工拷到 Assets\Resources\Shaders\。
// =====================================================================================
Shader "UI/LiquidGlass"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Blur ("Blur Radius (texels)", Range(0, 8)) = 2
        _Frost ("Frost Whiten", Range(0, 1)) = 0.10
        _Lift ("Brightness Lift", Range(0, 1)) = 0.08
        _Crisp ("Clear Mix (1=clear)", Range(0, 1)) = 0.55
        _Refr ("Refraction (texels)", Range(0, 40)) = 20
        _Bulge ("Dome Bulge", Range(0.1, 3)) = 1.4
        _SpecInt ("Specular Strength", Range(0, 2)) = 0.9
        _EdgeAlpha ("Edge Alpha Boost", Range(0, 1)) = 0.55
        _Glow ("Light Halos Strength", Range(0, 1.5)) = 0.55
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

            sampler2D _GlassScene;                   // GlassSceneCamera 每帧渲染的场景
            float4 _GlassScene_TexelSize;
            fixed4 _Color;
            float _Blur;
            float _Frost;
            float _Lift;
            float _Crisp;
            float _Refr;
            float _Bulge;
            float _SpecInt;
            float _EdgeAlpha;
            float _Glow;

            struct appdata_t { float4 vertex : POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f      { float4 vertex : SV_POSITION; fixed4 color : COLOR;
                              float4 scr : TEXCOORD0; float2 uv : TEXCOORD1; };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.scr = ComputeScreenPos(o.vertex);
                o.uv = v.texcoord;                   // UIGlass 网格写入的 [-1,1] 局部坐标
                return o;
            }

            half3 Tap(float2 uv) { return tex2D(_GlassScene, uv).rgb; }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv;                                  // [-1,1] 局部坐标
                float3 n = normalize(float3(p * _Bulge, 1.0));    // 玻璃穹顶法线
                float fres = pow(max(1.0 - n.z, 1e-4), 1.7);      // 菲涅尔：边缘强

                // ---- 折射：采样点沿法线横向偏移，透过玻璃看到弯掉的场景（每帧实时）----
                float2 base = i.scr.xy / i.scr.w;
                float2 uv = base + n.xy * _Refr * _GlassScene_TexelSize.xy;

                // 小半径 13 点模糊（水玻璃要清澈 _Blur 很小；磨砂面板调大 _Blur）
                float2 t = _GlassScene_TexelSize.xy * _Blur;
                half3 c = Tap(uv) * 2.0;
                c += Tap(uv + float2( t.x,  0.0));
                c += Tap(uv + float2(-t.x,  0.0));
                c += Tap(uv + float2( 0.0,  t.y));
                c += Tap(uv + float2( 0.0, -t.y));
                c += Tap(uv + t);
                c += Tap(uv - t);
                c += Tap(uv + float2( t.x, -t.y));
                c += Tap(uv + float2(-t.x,  t.y));
                c += Tap(uv + t * 2.0);
                c += Tap(uv - t * 2.0);
                c += Tap(uv + float2( t.x, -t.y) * 2.0);
                c += Tap(uv + float2(-t.x,  t.y) * 2.0);
                c *= (1.0 / 14.0);
                half3 clear = Tap(uv);                // 清晰折射层
                c = lerp(c, clear, _Crisp);

                // ---- 三盏游动光源的光晕（程序化漂移 = 伪随机而平滑，全部实时）----
                float2 lp1 = float2(sin(_Time.y * 0.53 + 1.7) * 0.70, cos(_Time.y * 0.41 + 0.3) * 0.55);
                float2 lp2 = float2(cos(_Time.y * 0.33 + 4.2) * 0.60, sin(_Time.y * 0.61 + 2.9) * 0.60);
                float2 lp3 = float2(sin(_Time.y * 0.44 + 5.1) * 0.50, sin(_Time.y * 0.37 + 1.2) * 0.65);
                float d1 = dot(p - lp1, p - lp1);
                float d2 = dot(p - lp2, p - lp2);
                float d3 = dot(p - lp3, p - lp3);
                half3 glow = half3(1.00, 0.93, 0.80) * exp(-d1 * 6.0) * 0.55   // 暖白
                           + half3(0.72, 0.83, 1.00) * exp(-d2 * 7.0) * 0.40   // 冷蓝
                           + half3(1.00, 0.88, 0.60) * exp(-d3 * 5.0) * 0.45;  // 淡金
                glow *= _Glow;

                // ---- 合成：场景 × 玻璃色调，霜化，边缘微暗，反射/光晕加亮 ----
                half3 col = c * i.color.rgb;
                col = lerp(col, half3(1, 1, 1), _Frost * 0.5);
                col *= (1.0 - fres * 0.32);           // 边缘折射微暗（透镜边缘光弯折，暗边让玻璃在亮背景上可读）
                col += fres * 0.45 + spec + glow;     // 边缘亮环 + 镜面反射 + 光源光晕
                col = col * (1.0 - _Lift) + _Lift;

                // 边缘更"实"：alpha 随菲涅尔抬升（中心保持极透）
                float alpha = saturate(i.color.a + fres * _EdgeAlpha);
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}

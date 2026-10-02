// =====================================================================================
// GlassBlur.shader —— 液态玻璃磨砂源的分离高斯模糊（v0.47 新增）
//
// 只在 GlassSceneCamera 内部使用：把半分辨率场景 RT 降采样到四分之一分辨率后，
// 用本 shader 做 水平/垂直 两轮 9 点高斯（重复两轮 = 更宽的磨砂半径），
// 产出 _GlassBlur 全局纹理供 LiquidGlass.shader 采样。
// RT→RT 的 Graphics.Blit 是常规渲染路径（非帧缓冲拷贝），在 MuMu GLES3 上确定可靠。
// 母本在 E:\Snooker\shaders\，需手工拷到 Assets\Resources\Shaders\。
// =====================================================================================
Shader "Hidden/GlassBlur"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _Dir;                            // (1,0)=水平 / (0,1)=垂直；xy 同时是步长系数

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 st = _MainTex_TexelSize.xy * _Dir.xy * 1.5;   // 半径 1.5 texel
                half3 c = tex2D(_MainTex, i.uv).rgb * 0.2270270270;
                c += (tex2D(_MainTex, i.uv + st * 1.0).rgb
                   +  tex2D(_MainTex, i.uv - st * 1.0).rgb) * 0.1945945946;
                c += (tex2D(_MainTex, i.uv + st * 2.0).rgb
                   +  tex2D(_MainTex, i.uv - st * 2.0).rgb) * 0.1216216216;
                c += (tex2D(_MainTex, i.uv + st * 3.0).rgb
                   +  tex2D(_MainTex, i.uv - st * 3.0).rgb) * 0.0540540541;
                c += (tex2D(_MainTex, i.uv + st * 4.0).rgb
                   +  tex2D(_MainTex, i.uv - st * 4.0).rgb) * 0.0162162162;
                return fixed4(c, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}

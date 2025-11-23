Shader "Hidden/SharedProfilerHUD"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        
        Pass
        {
            Cull Off
            ZClip Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            
            CGPROGRAM
            #include "SharedProfilerHUD.hlsl"

            #pragma vertex QuadDrawVertex
            #pragma fragment QuadDrawFragment

            ENDCG
        }

        Pass
        {
            Cull Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            
            CGPROGRAM
            #include "SharedProfilerHUD.hlsl"

            #pragma vertex QuadDrawSimpleVertex
            #pragma fragment QuadDrawFragment

            ENDCG
        }
   }
}
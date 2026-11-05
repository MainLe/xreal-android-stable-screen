Shader "XREAL/Stable Calibration Grid"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Off ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 grid=abs(frac(i.uv*16+.5)-.5)/max(fwidth(i.uv*16),.0001);
                float gridLine=1-saturate(min(grid.x,grid.y));
                float2 center=abs(i.uv-.5)/max(fwidth(i.uv),.0001);
                float crossMask=1-saturate(min(center.x,center.y)-1);
                return fixed4(lerp(lerp(float3(.015,.04,.06),float3(.65,.8,.85),gridLine),float3(.1,1,.6),crossMask),1);
            }
            ENDCG
        }
    }
}

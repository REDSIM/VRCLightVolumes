Shader "Hidden/VRCLV/Tests/OverdrawClusterMask"
{
    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            int _FirstWord;
            int _LastWord;

            int4 Fragment(v2f_img input) : SV_Target
            {
                return int4(_FirstWord, 0, 0, _LastWord);
            }
            ENDCG
        }
    }
}

Shader "Hidden/USD/Texture Alpha To Red" {
	Properties{
		_TextureToFlip("Texture", 2D) = "white" {}
	}

	SubShader {
		 Pass {
			 CGPROGRAM

			 #pragma vertex vert
			 #pragma fragment frag
			 #include "UnityCG.cginc"

			 #define ALPHA_SCALE 1.7f;

			 struct vertInput {
			 float4 pos : POSITION;
			 float2 texcoord : TEXCOORD0;
			 };

			 struct vertOutput {
			 float4 pos : SV_POSITION;
			 float2 texcoord : TEXCOORD0;
			 };

			 sampler2D _TextureToFlip;
			 int _FlipY;

			 vertOutput vert(vertInput input) {
				 vertOutput o;
				 o.pos = UnityObjectToClipPos(input.pos);
				 o.texcoord.x = input.texcoord.x;
				 o.texcoord.y = input.texcoord.y;

				 return o;
			 }

			 float4 frag(vertOutput output) : COLOR {
			     float alpha = tex2D(_TextureToFlip, output.texcoord).a;
			     alpha = alpha * ALPHA_SCALE;
			     return float4(alpha, 0.0, 0.0, 1.0);
			 }

			ENDCG
		}
	}
}

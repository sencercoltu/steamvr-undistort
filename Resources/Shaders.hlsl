cbuffer vertexConstBuffer : register(b0)
{
	float4x4 WorldViewProj;
};

cbuffer pixelConstBuffer : register(b1)
{	
	float4 lightPos;		
	int undistort;
	int wireframe;
	int controller;
	int activecolor;
};

cbuffer distortionConstBuffer : register(b2)
{	
	float4 RedCoeffs;	
	float4 GreenCoeffs;
	float4 BlueCoeffs;
	float4 RedCenter;
	float4 GreenCenter;
	float4 BlueCenter;	
	float2 EyeCenter;
	float GrowToUndistort;
	float CutOff;
	float Aspect;
};

Texture2D diffuseTexture : register(t0);
SamplerState diffuseSampler : register(s0);

struct MODEL_VS_IN
{
	float3 pos : POSITION;
	float3 normal : NORMAL;
	float2 uv : TEXCOORD;
};

struct MODEL_PS_IN
{
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD;
	float3 normal : NORMAL;
	float3 worldPos : POSITION;
};


MODEL_PS_IN Model_VS(MODEL_VS_IN input)
{
	MODEL_PS_IN output = (MODEL_PS_IN)0;	
	output.pos = mul(float4(input.pos, 1), WorldViewProj);
	output.normal = input.normal.xyz;
	output.worldPos = input.pos.xyz;
	output.uv = input.uv;
	return output;
}

float4 Model_PS(MODEL_PS_IN input) : SV_Target
{
	float4 color = float4(1, 1, 1, 1); 
	if (!wireframe || controller)
	{	
		float3 L = normalize(lightPos.xyz - input.worldPos);
		float3 N = normalize(input.normal);
		float3 diffuseTex = diffuseTexture.Sample(diffuseSampler, input.uv).xyz;
		float3 diffuse = diffuseTex * saturate(dot(N,L));
		color = float4(diffuse, 1);
	}

	if (activecolor == 0) color.g = color.b = 0;
	else if (activecolor == 1) color.r = color.b = 0;
	else if (activecolor == 2) color.r = color.g = 0;

	return color;
}


float4 CrossHair_VS(float2 position : POSITION) : SV_POSITION
{
	return float4(position, 0, 1);
}

float4 CrossHair_PS(float4 position : SV_POSITION) : SV_Target
{
   return float4(0.0, 1.0, 0.0, 1.0);
}

struct INFO_VS_IN
{
	float3 pos : POSITION;
	float2 uv : TEXCOORD;	
};

struct INFO_PS_IN
{
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD;	
};

INFO_PS_IN Info_VS(INFO_VS_IN input)
{
	INFO_PS_IN output = (INFO_PS_IN)0;
	output.pos = mul(float4(input.pos, 1), WorldViewProj);
	output.uv = input.uv;	
	return output;
}

float4 Info_PS(INFO_PS_IN input) : SV_Target
{
   return diffuseTexture.Sample(diffuseSampler, input.uv);
}

INFO_PS_IN Backbuffer_VS(INFO_VS_IN input)
{
	INFO_PS_IN output = (INFO_PS_IN)0;
	output.pos = float4(input.pos, 1);
	output.uv = input.uv;
	return output;
}

float4 Backbuffer_PS(INFO_PS_IN input) : SV_Target
{
   return diffuseTexture.Sample(diffuseSampler, input.uv);
}

float4 HiddenMesh_VS(float2 position : POSITION) : SV_POSITION
{
	return float4(position, 0, 1);
}

float4 HiddenMesh_PS(float4 position : SV_POSITION) : SV_Target
{
   return float4(0.0, 0.0, 0.0, 1); 
}

struct Undistort_VS_IN
{
	float2 pos : POSITION;
	float2 uv : TEXCOORD;
};

struct Undistort_PS_IN
{
	float4 sv_pos : SV_POSITION;
	float2 uv : TEXCOORD;		
};

Undistort_PS_IN Undistort_VS(Undistort_VS_IN input)
{	
	Undistort_PS_IN output = (Undistort_PS_IN)0;	
	output.sv_pos = float4(input.pos, 0, 1);
	output.uv = input.uv;
	return output;
}

float4 Undistort_PS(Undistort_PS_IN input) : SV_Target
{	
	float scale = 1.0 + GrowToUndistort;	

	float2 UV = (input.uv * 2) - 1;	//convert 0;1 to -1;1	
	UV.y *= Aspect;

	float2 ruv = UV - RedCenter.xy;	
	float rr2 = dot(ruv, ruv);		
	float rk = 1.0 / (RedCoeffs.w + RedCoeffs.x * rr2 + RedCoeffs.y * rr2 * rr2 + RedCoeffs.z * rr2 * rr2 * rr2);
	ruv = (ruv * rk + RedCenter.xy) / scale;	
	ruv.y /= Aspect;
	ruv = (ruv + 1) / 2;
	float R = diffuseTexture.Sample(diffuseSampler, ruv).r;

	float2 guv = UV - GreenCenter.xy;
	float gr2 = dot(guv, guv);
	float gk = 1.0 / (GreenCoeffs.w + GreenCoeffs.x * gr2 + GreenCoeffs.y * gr2 * gr2 + GreenCoeffs.z * gr2 * gr2 * gr2);
	guv = (guv * gk + GreenCenter.xy) / scale;
	guv.y /= Aspect;
	guv = (guv + 1) / 2;
	float G = diffuseTexture.Sample(diffuseSampler, guv).g;
	
	float2 buv = UV - BlueCenter.xy;
	float br2 = dot(buv, buv);
	float bk = 1.0 / (BlueCoeffs.w + BlueCoeffs.x * br2 + BlueCoeffs.y * br2 * br2 + BlueCoeffs.z * br2 * br2 * br2);
	buv = (buv * bk + BlueCenter.xy) / scale;
	buv.y /= Aspect;
	buv = (buv + 1) / 2;
	float B = diffuseTexture.Sample(diffuseSampler, buv).b;

	return float4(R, G, B, 1);
}


/*
float4 Undistort_PS2(Undistort_PS_IN input) : SV_Target
{	

	float2 cop = intrinsics._m13_m23;
	float2 asp = intrinsics._m00_m11;
	
	float2 ret = float2((input.uv.x - 0.5) * 2.0, (input.uv.y - 0.5) * 2.0);

	float2 diff = ret + cop;

	float ratioX = 1.0 / asp.x ;
	float ratioY = 1.0 / asp.y;

	ret.x = diff.x * ratioX - cop.x;
	ret.y = diff.y * ratioY - cop.y;
	
	float2 centerOfDistortion = float2(cgx, cgy);

	float2 offset = ret - centerOfDistortion;
	float r = sqrt(offset.x * offset.x + offset.y * offset.y);	

	float radiusCoeff = 1.0 / (1.0 + gk1 * pow(r, 2) + gk2 * pow(r, 4) + gk3 * pow(r, 6));

	ret.x = (centerOfDistortion.x + (offset.x * radiusCoeff)) / 2.0 + 0.5;
	ret.y = (centerOfDistortion.y + (offset.y * radiusCoeff)) / 2.0 + 0.5;

	return diffuseTexture.Sample(diffuseSampler, ret);




	float bdx = (input.uv.x - 0.5) * 2.0 - cbx;
	float bdy = (input.uv.y - 0.5) * 2.0 - cby;
	float br2 = bdx * bdx + bdy * bdy;
	float br4 = br2 * br2;
	float br6 = br4 * br2;
	float bk = 1.0 / (1 + bk1 * br2 + bk2 * br4 + bk3 * br6);
	float2 buv = float2((bdx * bk + cbx) / 2.0 + 0.5, (bdy * bk + cby) / 2.0 + 0.5);
	float4 bCol = diffuseTexture.Sample(diffuseSampler, buv);

	float rdx = (input.uv.x - 0.5) * 2.0 - crx;
	float rdy = (input.uv.y - 0.5) * 2.0 - cry;
	float rr2 = rdx * rdx + rdy * rdy;
	float rr4 = rr2 * rr2;
	float rr6 = rr4 * rr2;
	float rk = 1.0 / (1 + rk1 * rr2 + rk2 * rr4 + rk3 * rr6);
	float2 ruv = float2((rdx * rk + crx) / 2.0 + 0.5, (rdy * rk + cry) / 2.0 + 0.5);
	float4 rCol = diffuseTexture.Sample(diffuseSampler, ruv);

	return float4(rCol.r, gCol.g, bCol.b, 1);

}
*/
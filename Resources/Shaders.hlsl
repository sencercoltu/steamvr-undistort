cbuffer vertexConstBuffer : register(b0)
{
	float4x4 WorldViewProj;
	float2 ActiveEyeCenter;
	float ActiveAspect;
	float Reserved1;
};

cbuffer pixelConstBuffer : register(b1)
{	
	float4 LightPos;	

	bool Undistort;
	bool Wireframe;
	bool Controller;	

	int ActiveColor;
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
	float UndistortR2Cutoff;

	float Aspect;
	float FocalX;
	float FocalY;
	int ActiveEye;

	float4x4 Extrinsics;
	float3x3 Intrinsics;
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
	if (!Wireframe || Controller)
	{	
		float3 L = normalize(LightPos.xyz - input.worldPos);
		float3 N = normalize(input.normal);
		float3 diffuseTex = diffuseTexture.Sample(diffuseSampler, input.uv).xyz;
		float3 diffuse = diffuseTex * saturate(dot(N,L));
		color = float4(diffuse, 1);
	}

	if (ActiveColor == 0) color.g = color.b = 0;
	else if (ActiveColor == 1) color.r = color.b = 0;
	else if (ActiveColor == 2) color.r = color.g = 0;

	return color;
}


float4 CrossHair_VS(float2 position : POSITION) : SV_POSITION
{
	return float4(float2(position.x - ActiveEyeCenter.x, (position.y - ActiveEyeCenter.y) / ActiveAspect), 1.0f, 1.0f); //put a bit away from eye
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

	float2 UV = (input.uv * 2) - 1;	//convert [0;1] to [-1;1]	
	float2 center = RedCenter.xy;	
	float2 ruv = UV - center;
	ruv.y *= Aspect;
	float rr2 = dot(ruv, ruv);		
	float rk = 1.0 / (RedCoeffs.w + RedCoeffs.x * rr2 + RedCoeffs.y * rr2 * rr2 + RedCoeffs.z * rr2 * rr2 * rr2);	
	ruv = (ruv * rk + center) / scale;
	ruv.y /= Aspect;
	ruv = (ruv + 1) / 2; //convert [-1;1] back to [0;1]
	float R = diffuseTexture.Sample(diffuseSampler, ruv).r;

	center = GreenCenter.xy;	
	float2 guv = UV - center;
	guv.y *= Aspect;
	float gr2 = dot(guv, guv);
	float gk = 1.0 / (GreenCoeffs.w + GreenCoeffs.x * gr2 + GreenCoeffs.y * gr2 * gr2 + GreenCoeffs.z * gr2 * gr2 * gr2);
	guv = (guv * gk + center) / scale;
	guv.y /= Aspect;
	guv = (guv + 1) / 2;
	float G = diffuseTexture.Sample(diffuseSampler, guv).g;
	
	center = BlueCenter.xy;	
	float2 buv = UV - center;
	buv.y *= Aspect;
	float br2 = dot(buv, buv);
	float bk = 1.0 / (BlueCoeffs.w + BlueCoeffs.x * br2 + BlueCoeffs.y * br2 * br2 + BlueCoeffs.z * br2 * br2 * br2);	
	buv = (buv * bk + center) / scale;
	buv.y /= Aspect;
	buv = (buv + 1) / 2;
	float B = diffuseTexture.Sample(diffuseSampler, buv).b;

	return float4(R, G, B, 1);
}


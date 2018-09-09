cbuffer vertexConstBuffer : register(b0)
{
	float4x4 Head;
	float4x4 EyeToHead;
	float4x4 Projection;
	float4x4 WorldViewProj;
	//float4x4 Intrinsic;
	//float4x4 Extrinsic;
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
	float rk1, rk2, rk3;
	float gk1, gk2, gk3;
	float bk1, bk2, bk3;
	float crx, cry;
	float cgx, cgy;
	float cbx, cby;
	float reserved1;
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
	if (!wireframe || controller) //wireframe and not controller
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


float4 CrossHair_VS(float4 position : POSITION) : SV_POSITION
{
	return position;
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

float4 HiddenMesh_VS(float2 position : POSITION) : SV_POSITION
{
	return float4(position, 0, 1);
}

float4 HiddenMesh_PS(float4 position : SV_POSITION) : SV_Target
{
   return float4(0.0, 0.0, 0.0, 1.0); 
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
	output.sv_pos = float4(input.pos.x, input.pos.y, 0, 1);
	output.uv = input.uv;	
	return output;
}

float4 Undistort_PS(Undistort_PS_IN input) : SV_Target
{	
	float gdx = (input.uv.x - 0.5) * 2.0 - cgx;
	float gdy = (input.uv.y - 0.5) * 2.0 - cgy;
	float gr2 = gdx * gdx + gdy * gdy;
	float gr4 = gr2 * gr2;
	float gr6 = gr4 * gr2;
	float gk = 1.0 / (1 + gk1 * gr2 + gk2 * gr4 + gk3 * gr6);
	float2 guv = float2((gdx * gk + cgx) / 2.0 + 0.5, (gdy * gk + cgy) / 2.0 + 0.5);
	float4 gCol = diffuseTexture.Sample(diffuseSampler, guv);

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
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using System;

namespace Undistort
{
    public class Shader : IDisposable
    {
        public InputLayout inputLayout;
        public InputElement[] inputElements;
        public ShaderBytecode vertexShaderByteCode;
        public ShaderBytecode pixelShaderByteCode;
        public VertexShader vertexShader;
        public PixelShader pixelShader;

        public Shader(Device device, string vsEntry, string psEntry, InputElement[] inputElems)
        {
            inputElements = inputElems;
            vertexShaderByteCode = ShaderBytecode.Compile(Properties.Resources.Shaders, vsEntry, "vs_5_0");
            pixelShaderByteCode = ShaderBytecode.Compile(Properties.Resources.Shaders, psEntry, "ps_5_0");
            vertexShader = new VertexShader(device, vertexShaderByteCode);
            pixelShader = new PixelShader(device, pixelShaderByteCode);
            inputLayout = new InputLayout(device, ShaderSignature.GetInputSignature(vertexShaderByteCode), inputElements);
        }

        public void Dispose()
        {
            inputLayout.Dispose();
            vertexShaderByteCode.Dispose();
            pixelShaderByteCode.Dispose();
            pixelShader.Dispose();
            vertexShader.Dispose();
        }

        public void Apply(DeviceContext context)
        {
            context.InputAssembler.InputLayout = inputLayout;
            context.VertexShader.Set(vertexShader);
            context.PixelShader.Set(pixelShader);
            context.GeometryShader.Set(null);
            context.DomainShader.Set(null);
            context.HullShader.Set(null);
        }

    }
}

using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using System;
using System.Runtime.InteropServices;
using Valve.VR;
using static Undistort.Program;

namespace Undistort
{
    public static class UndistortShader
    {
        private static SharpDX.Direct3D11.Buffer vertexBuffer;
        private static VertexBufferBinding vertexBufferBinding;
        private static Shader shader;

        public static void Load(Device device)
        {
            shader = new Shader(device, "Undistort_VS", "Undistort_PS", new InputElement[]
            {
                    new InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32_Float, 0, 0),
                    new InputElement("TEXCOORD", 0, SharpDX.DXGI.Format.R32G32_Float, 8, 0)
            });

            vertexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, new[]
            {
                // 3D coordinates              UV Texture coordinates
                -1.0f, -1.0f, 0.0f, 1.0f,
                 1.0f,  1.0f, 1.0f, 0.0f,
                -1.0f,  1.0f, 0.0f, 0.0f,
                -1.0f, -1.0f, 0.0f, 1.0f,
                 1.0f, -1.0f, 1.0f, 1.0f,
                 1.0f,  1.0f, 1.0f, 0.0f,
            });
            vertexBufferBinding = new VertexBufferBinding(vertexBuffer, sizeof(float) * 4, 0);
        }

        public static void Render(DeviceContext context, ref EyeData eye)
        {
            context.OutputMerger.SetDepthStencilState(Program.depthStencilState);
            context.OutputMerger.SetBlendState(null);

            var textureView = Program.undistortTextureView;
            if (eye.Eye == -1)            
                textureView = Program.windowEye.TextureView;

            context.Rasterizer.SetViewport(0, 0, eye.FrameSize.Width, eye.FrameSize.Height);
            context.ClearRenderTargetView(textureView, SharpDX.Color.Black);
            context.ClearDepthStencilView(eye.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
            context.OutputMerger.SetTargets(eye.DepthStencilView, textureView);

            shader.Apply(context);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;            
            context.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
            context.PixelShader.SetShaderResource(0, Program.leftEye.ShaderView);
            context.Draw(6, 0);
        }

    }
}

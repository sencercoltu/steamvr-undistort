using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Valve.VR;

namespace Undistort
{
    public static class CrossHairModel
    {
        //private static float[] verticesLeft;
        //private static float[] verticesRight;
        private static float[] vertices;
        private static SharpDX.Direct3D11.Buffer vertexBuffer;
        private static VertexBufferBinding vertexBufferBinding;
        private static Shader shader;


        public static void Init(SharpDX.Direct3D11.Device device)
        {
            shader = new Shader(device, "CrossHair_VS", "CrossHair_PS", new[]
            {
                new InputElement("POSITION", 0, Format.R32G32_Float, 0)
            });

            ModifyCircles(device, 0.05f);
        }        

        public static void Render(DeviceContext context, EVREye eye)
        {
            shader.Apply(context);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
            context.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
            context.Draw(vertices.Length / 2, 0);
        }

        public static void ModifyCircles(SharpDX.Direct3D11.Device device, float radius)
        {
            if (radius > 1.0f) radius = 1.0f;
            if (radius < 0.01f) radius = 0.01f;
            var verticesList = new List<float>();

            verticesList.AddRange(new float[] { -1f, 0f, 1f, 0f, 0f, -1f, 0f, 1f });

            for (var r = 0.1; r < 1.0; r += radius)
            {
                double px = 0.0;
                double py = 0.0;
                double x = 0.0;
                double y = 0.0;
                for (var d = 0; d <= 360; d++)
                {

                    var dd = d * (Math.PI / 180.0);
                    x = r * Math.Cos(dd);
                    y = r * Math.Sin(dd);
                    if (d == 0)
                    {
                        px = x;
                        py = y;
                    }
                    verticesList.Add((float)px);
                    verticesList.Add((float)py);
                    verticesList.Add((float)x);
                    verticesList.Add((float)y);
                    px = x;
                    py = y;
                }
            }
            vertices = verticesList.ToArray();
            if (vertexBuffer != null)
                vertexBuffer.Dispose();
            vertexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, vertices);
            vertexBufferBinding = new VertexBufferBinding(vertexBuffer, sizeof(float) * 2, 0);


        }

    }
}

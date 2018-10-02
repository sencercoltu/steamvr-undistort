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


            var verticesList = new List<float>();
            //var right = new List<float>();

            verticesList.AddRange(new float[] { -1f, 0f, 1f, 0f, 0f, -1f, 0f, 1f });

            for (var r = 0.1; r < 1.0; r += 0.05)
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
            //verticesLeft = new float[] { -1f, -Program.leftEye.DistortionData.EyeCenter.Y, 1f, -Program.leftEye.DistortionData.EyeCenter.Y, -Program.leftEye.DistortionData.EyeCenter.X, -1f, -Program.leftEye.DistortionData.EyeCenter.X, 1f };
            //verticesRight = new float[] { -1f, -Program.rightEye.DistortionData.EyeCenter.Y, 1f, -Program.rightEye.DistortionData.EyeCenter.Y, -Program.rightEye.DistortionData.EyeCenter.X, -1f, -Program.rightEye.DistortionData.EyeCenter.X, 1f };

            vertexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, vertices);
            vertexBufferBinding = new VertexBufferBinding(vertexBuffer, sizeof(float) * 2, 0);
        }        

        public static void Render(DeviceContext context, EVREye eye)
        {
            //context.UpdateSubresource(vertices, vertexBuffer);
            //switch (eye)
            //{
            //    case EVREye.Eye_Right:                    
            //        context.UpdateSubresource(verticesRight, vertexBuffer);
            //        break;
            //    case EVREye.Eye_Left:
            //        context.UpdateSubresource(verticesLeft, vertexBuffer);
            //        break;
            //}

            shader.Apply(context);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
            context.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
            context.Draw(vertices.Length / 2, 0);
        }

        public static void MoveCenter(double lx, double ly, double rx, double ry)
        {
            Program.leftEye.DistortionData.EyeCenter.X += (float)lx;
            Program.leftEye.DistortionData.EyeCenter.Y += (float)ly;
            Program.rightEye.DistortionData.EyeCenter.X += (float)rx;
            Program.rightEye.DistortionData.EyeCenter.Y += (float)ry;
            //verticesLeft[1] = verticesLeft[3] = -Program.leftEye.DistortionData.EyeCenter.Y;
            //verticesRight[1] = verticesRight[3] = -Program.rightEye.DistortionData.EyeCenter.Y;
            //verticesLeft[4] = verticesLeft[6] = -Program.leftEye.DistortionData.EyeCenter.X;
            //verticesRight[4] = verticesRight[6] = -Program.rightEye.DistortionData.EyeCenter.X;
        }
    }
}

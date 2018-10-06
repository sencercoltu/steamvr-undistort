using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using Valve.VR;
using static Undistort.Program;

namespace Undistort
{
    public static class PointerModel
    {
        private static float[] vertices;
        private static SharpDX.Direct3D11.Buffer vertexBuffer;
        private static VertexBufferBinding vertexBufferBinding;
        private static Shader shader;
        private static Matrix Origin = Matrix.Translation(0, 0, 0);
        private static Matrix Direction = Matrix.Translation(0, 0, -100);

        public static Matrix WVP = Matrix.Zero;
        
        public static void Init(SharpDX.Direct3D11.Device device)
        {
            shader = new Shader(device, "Pointer_VS", "Pointer_PS", new[]
            {
                new SharpDX.Direct3D11.InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0)                
            });

            vertices = new float[] {
                Origin.TranslationVector.X, Origin.TranslationVector.Y, Origin.TranslationVector.Z,
                Direction.TranslationVector.X, Direction.TranslationVector.Y, Direction.TranslationVector.Z
            };
            vertexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, vertices);
            vertexBufferBinding = new VertexBufferBinding(vertexBuffer, sizeof(float) * 3, 0);            
        }

        public static void Render(SharpDX.Direct3D11.DeviceContext context)
        {
            //we apply wvp here to update z
            var origin = (Origin * WVP).TranslationVector;
            var direction = (Direction * WVP).TranslationVector;                        
            var ray = new SharpDX.Ray(origin, direction);
            

            //if ray is inside panel, show            
            if (AdjustmentPanelModel.CheckBounds(ref ray, out var z))
            {
                var k = Direction.TranslationVector.Length();
                vertices[5] = -z * k;
                shader.Apply(context);                
                context.UpdateSubresource(vertices, vertexBuffer);
                context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
                context.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
                context.Draw(2, 0);
            }
        }

    }
}

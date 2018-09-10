using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Drawing;
using System.Linq;
using Valve.VR;

namespace Undistort
{
    public static class CrossHairModel
    {
        public struct EyeCenters
        {
            public double LeftX;
            public double LeftY;
            public double RightX;
            public double RightY;
        }
                
        private static float[] verticesLeft;
        private static float[] verticesRight;
        private static Buffer vertexBuffer;
        public static EyeCenters Centers;
        private static VertexBufferBinding vertexBufferBinding;
        private static Shader shader;


        public static void Init(SharpDX.Direct3D11.Device device, double lx, double ly, double rx, double ry)
        {
            shader = new Shader(device, "CrossHair_VS", "CrossHair_PS", new[]
            {
                new InputElement("POSITION", 0, Format.R32G32_Float, 0)
            });
                        
            Centers.LeftX = lx;
            Centers.LeftY = ly;
            Centers.RightX = rx;
            Centers.RightY = ry;            
            verticesLeft  = new float[] { -1f, (float)Centers.LeftY,  1f, (float)Centers.LeftY,  (float)Centers.LeftX,  -1f, (float)Centers.LeftX,  1f };
            verticesRight = new float[] { -1f, (float)Centers.RightY, 1f, (float)Centers.RightY, (float)Centers.RightX, -1f, (float)Centers.RightX, 1f };
                        
            vertexBuffer = Buffer.Create(device, BindFlags.VertexBuffer, verticesLeft);
            vertexBufferBinding = new VertexBufferBinding(vertexBuffer, sizeof(float) * 2, 0);
        }

        

        public static void Render(DeviceContext context, EVREye eye)
        {
            switch (eye)
            {
                case EVREye.Eye_Right:                    
                    context.UpdateSubresource(verticesRight, vertexBuffer);
                    break;
                case EVREye.Eye_Left:
                    context.UpdateSubresource(verticesLeft, vertexBuffer);
                    break;
            }

            shader.Apply(context);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
            context.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
            context.Draw(verticesLeft.Length / 2, 0);
        }

        public static void MoveCenter(double lx, double ly, double rx, double ry)
        {
            Centers.LeftX += lx;
            Centers.LeftY += ly;
            Centers.RightX += rx;
            Centers.RightY += ry;
            verticesLeft[1] = verticesLeft[3] = (float)Centers.LeftY;
            verticesRight[1] = verticesRight[3] = (float)Centers.RightY;
            verticesLeft[4] = verticesLeft[6] = (float)Centers.LeftX;
            verticesRight[4] = verticesRight[6] = (float)Centers.RightX;            
        }
    }
}

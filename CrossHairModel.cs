using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;

namespace Undistort
{
    public static class CrossHairModel
    {
        private static float[] vertices;
        private static SharpDX.Direct3D11.Buffer vertexBuffer;
        private static VertexBufferBinding vertexBufferBinding;

        private static float[] points;
        private static SharpDX.Direct3D11.Buffer pointBuffer;
        private static VertexBufferBinding pointBufferBinding;

        private static Shader shader;

        public static float Radius;
        private static int DefautVerticesCount;
        private static int PointCount;


        public static void Init(SharpDX.Direct3D11.Device device)
        {
            shader = new Shader(device, "CrossHair_VS", "CrossHair_PS", new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0),
                new InputElement("COLOR", 0, Format.R32G32B32_Float, 0),
            });


            var pointList = new List<float>();
            var green = new float[] { 0, 1, 0 };

            for (var x = -5f; x < 5f; x += 0.5f)
            {
                for (var y = 0f; y < 7f; y += 0.5f)
                {
                    pointList.Add(x);
                    pointList.Add(y);
                    pointList.Add(5);
                    pointList.AddRange(green);
                    pointList.Add(x);
                    pointList.Add(y);
                    pointList.Add(4);
                    pointList.AddRange(green);
                }
            }

            points = pointList.ToArray();
            pointBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, points);
            pointBufferBinding = new VertexBufferBinding(pointBuffer, sizeof(float) * 6, 0);
            PointCount = pointList.Count / 6;

            Radius = 0.3f;
            ModifyCircles(device, 0);
        }

        public static void Render(DeviceContext context)
        {
            shader.Apply(context);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
            context.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
            switch (Program.RenderMode)
            {
                case 0:
                    context.Draw(DefautVerticesCount, 0);
                    //case 1:

                    break;
                default:
                    context.Draw(vertices.Length / 6, 0);
                    break;
            }
        }

        public static void RenderPoints(DeviceContext context)
        {
            shader.Apply(context);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
            context.InputAssembler.SetVertexBuffers(0, pointBufferBinding);
            context.Draw(PointCount, 0);
        }


        public static void ModifyCircles(SharpDX.Direct3D11.Device device, float adj)
        {
            var depth = -1f;
            Radius += adj;
            if (Radius > 1.0f) Radius = 1.0f;
            if (Radius < 0.001f) Radius = 0.001f;

            var verticesList = new List<float>();
            float smallMarkerSize = 0.02f;
            //float bigMarkerSize = 0.02f;

            //vertical and horiz crosshair
            verticesList.AddRange(new float[] {
                -1f, 0f, depth, 0, 1, 0,
                1f, 0f, depth, 0, 1, 0,
                0f, -1f, depth, 0, 1, 0,
                0f, 1f, depth, 0, 1, 0
            });

            if (Program.pixelShaderData._Undistort == 1)
            {
                int sk = 1;

                //markers
                var green = new float[] { 0, 1, 0 };
                for (var y = 0.1f; y < 1.0f / Program.ScreenAspect; y += 0.1f)
                {
                    float markerSize = smallMarkerSize * (1.2f * sk++);
                    //if ((sk++ % 5) == 0)
                    //    markerSize = bigMarkerSize * 1.5f * bk++;
                    //vert
                    verticesList.Add((float)-markerSize);
                    verticesList.Add((float)y);
                    verticesList.Add(depth);
                    verticesList.AddRange(green);
                    verticesList.Add((float)markerSize);
                    verticesList.Add((float)y);
                    verticesList.Add(depth);
                    verticesList.AddRange(green);
                    verticesList.Add((float)-markerSize);
                    verticesList.Add((float)-y);
                    verticesList.Add(depth);
                    verticesList.AddRange(green);
                    verticesList.Add((float)markerSize);
                    verticesList.Add((float)-y);
                    verticesList.Add(depth);
                    verticesList.AddRange(green);
                }
                sk = 1;
                for (var x = 0.1f; x < 1.0f; x += 0.1f)
                {
                    float markerSize = smallMarkerSize * Program.ScreenAspect * (1.2f * sk++); ;
                    //if ((sk++ % 5) == 0)
                    //    markerSize = bigMarkerSize * 1.5f * Program.ScreenAspect * bk++;

                    //horz
                    verticesList.Add((float)x);
                    verticesList.Add((float)-markerSize / Program.ScreenAspect);
                    verticesList.Add(depth);
                    verticesList.AddRange(green);
                    verticesList.Add((float)x);
                    verticesList.Add((float)markerSize / Program.ScreenAspect);
                    verticesList.Add(depth);
                    verticesList.AddRange(green);
                    verticesList.Add((float)-x);
                    verticesList.Add((float)-markerSize / Program.ScreenAspect);
                    verticesList.Add(depth);
                    verticesList.AddRange(green);
                    verticesList.Add((float)-x);
                    verticesList.Add((float)markerSize / Program.ScreenAspect);
                    verticesList.Add(depth);
                    verticesList.AddRange(green);
                }
            }

            DefautVerticesCount = verticesList.Count / 6;

            var white = new float[] { 1, 1, 1 };
            //grid
            for (var y = 0.1f; y < 1.0f / Program.ScreenAspect; y += 0.1f)
            {
                //horz
                verticesList.Add((float)-1);
                verticesList.Add((float)y);
                verticesList.Add(depth);
                verticesList.AddRange(white);
                verticesList.Add((float)1);
                verticesList.Add((float)y);
                verticesList.Add(depth);
                verticesList.AddRange(white);
                verticesList.Add((float)-1);
                verticesList.Add((float)-y);
                verticesList.Add(depth);
                verticesList.AddRange(white);
                verticesList.Add((float)1);
                verticesList.Add((float)-y);
                verticesList.Add(depth);
                verticesList.AddRange(white);
            }
            for (var x = 0.1f; x < 1.0f; x += 0.1f)
            {
                //horz
                verticesList.Add((float)x);
                verticesList.Add((float)-1 / Program.ScreenAspect);
                verticesList.Add(depth);
                verticesList.AddRange(white);
                verticesList.Add((float)x);
                verticesList.Add((float)1 / Program.ScreenAspect);
                verticesList.Add(depth);
                verticesList.AddRange(white);
                verticesList.Add((float)-x);
                verticesList.Add((float)-1 / Program.ScreenAspect);
                verticesList.Add(depth);
                verticesList.AddRange(white);
                verticesList.Add((float)-x);
                verticesList.Add((float)1 / Program.ScreenAspect);
                verticesList.Add(depth);
                verticesList.AddRange(white);
            }

            //circles
            for (var r = Radius; r < 1.0; r += Radius)
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
                    verticesList.Add(depth);
                    verticesList.AddRange(white);
                    verticesList.Add((float)x);
                    verticesList.Add((float)y);
                    verticesList.Add(depth);
                    verticesList.AddRange(white);
                    px = x;
                    py = y;
                }
            }
            vertices = verticesList.ToArray();
            if (vertexBuffer != null)
                vertexBuffer.Dispose();
            vertexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, vertices);
            vertexBufferBinding = new VertexBufferBinding(vertexBuffer, sizeof(float) * 6, 0);

        }

    }
}

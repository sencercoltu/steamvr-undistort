using SharpDX.Direct2D1;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DirectWrite;
using SharpDX.DXGI;
using System.Globalization;
using static Undistort.Program;

namespace Undistort
{
    public class InfoBoardModel
    {
        private float[] vertices;
        private Buffer vertexBuffer;
        private Buffer indexBuffer;
        private VertexBufferBinding vertexBufferBinding;
        private Shader shader;

        private Texture2D texture;
        private ShaderResourceView textureView;

        private RenderTarget textRenderTarget;
        private TextFormat textFormat;
        private TextFormat headerTextFormat;
        private SolidColorBrush textBrush;

        public void Init(SharpDX.Direct3D11.Device device)
        {
            shader = new Shader(device, "Info_VS", "Info_PS", new[]
            {
                new SharpDX.Direct3D11.InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new SharpDX.Direct3D11.InputElement("TEXCOORD", 0, Format.R32G32_Float, 12, 0)
            });

            vertices = new float[] {
                -0.2f, 0.05f, 0f, 0, 1, //0
                0.2f, 0.05f, 0f, 1, 1, //1
                0.2f, 0.25f, -0.25f, 1, 0, //2
                -0.2f, 0.25f, -0.25f, 0, 0//3
            };

            var indices = new int[] { 0, 2, 3, 0, 1, 2 };

            indexBuffer = Buffer.Create(device, BindFlags.IndexBuffer, indices);
            vertexBuffer = Buffer.Create(device, BindFlags.VertexBuffer, vertices);
            vertexBufferBinding = new VertexBufferBinding(vertexBuffer, sizeof(float) * 5, 0);

            Texture2DDescription textureDesc = new Texture2DDescription
            {
                ArraySize = 1,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.Write,
                Format = Format.B8G8R8A8_UNorm,
                Width = WindowSize.Width,
                Height = WindowSize.Height,
                MipLevels = 1,
                OptionFlags = ResourceOptionFlags.None,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default
            };

            texture = new Texture2D(device, textureDesc);
            textureView = new ShaderResourceView(device, texture);

            var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();            
            var d2DDevice = new SharpDX.Direct2D1.Device(dxgiDevice);            
            var surface = texture.QueryInterface<Surface>();
            var d2DContext = new SharpDX.Direct2D1.DeviceContext(surface);
            var directWriteFactory = new SharpDX.DirectWrite.Factory();
            textRenderTarget = new RenderTarget(d2DContext.Factory,
                        surface, new RenderTargetProperties()
                        {
                            Type = RenderTargetType.Hardware,
                            PixelFormat = new PixelFormat()
                            {
                                Format = Format.Unknown,
                                AlphaMode = SharpDX.Direct2D1.AlphaMode.Ignore
                            },
                            DpiX = 0,
                            DpiY = 0,
                            Usage = RenderTargetUsage.None
                        });

            textFormat = new TextFormat(directWriteFactory, "Courier New", 15.0f);            
            headerTextFormat = new TextFormat(directWriteFactory, "Courier New", FontWeight.Bold, FontStyle.Normal, 25.0f);
            textBrush = new SolidColorBrush(textRenderTarget, SharpDX.Color4.Black);
        }

        private SharpDX.Color4 activeColor = new SharpDX.Color4(0.666f, 1, 0.784f, 1);
        private SharpDX.Color4 inactiveColor = new SharpDX.Color4(1, 0.666f, 0.784f, 1);

        public void Render(SharpDX.Direct3D11.DeviceContext context, ref EyeData eye)
        {
            textRenderTarget.BeginDraw();
            textRenderTarget.Clear(IsEyeActive(eye.Eye) ? activeColor : inactiveColor);
            var topPos = 0f;
            var str = eye.EyeName + " EYE INFO" + (IsEyeActive(eye.Eye) ? (RenderHiddenMesh && Program.Undistort) ? " - ACTIVE - HMA" : " - ACTIVE" : "") + "\n";
            str += "CENTERS\n";
            textRenderTarget.DrawText(str, headerTextFormat, new SharpDX.Mathematics.Interop.RawRectangleF(0, topPos, WindowSize.Width, WindowSize.Height - topPos), textBrush);
            topPos += headerTextFormat.FontSize * 2;
            str = "CH: " + eye.DistortionData.EyeCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            str += eye.DistortionData.EyeCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            str += "Gc: " + eye.DistortionData.GreenCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            str += eye.DistortionData.GreenCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            str += "Bc: " + eye.DistortionData.BlueCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            str += eye.DistortionData.BlueCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            str += "Rc: " + eye.DistortionData.RedCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            str += eye.DistortionData.RedCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";            
            textRenderTarget.DrawText(str, textFormat, new SharpDX.Mathematics.Interop.RawRectangleF(0, topPos, WindowSize.Width, WindowSize.Height - topPos), textBrush);
            topPos += textFormat.FontSize * 5;

            str = "COEFFICIENTS\n";
            textRenderTarget.DrawText(str, headerTextFormat, new SharpDX.Mathematics.Interop.RawRectangleF(0, topPos, WindowSize.Width, WindowSize.Height - topPos), textBrush);
            topPos += headerTextFormat.FontSize;
            str = "Gk: " + eye.DistortionData.GreenCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            str += eye.DistortionData.GreenCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            str += eye.DistortionData.GreenCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            str += "Bk: " + eye.DistortionData.BlueCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            str += eye.DistortionData.BlueCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            str += eye.DistortionData.BlueCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            str += "Rk: " + eye.DistortionData.RedCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            str += eye.DistortionData.RedCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            str += eye.DistortionData.RedCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            textRenderTarget.DrawText(str, textFormat, new SharpDX.Mathematics.Interop.RawRectangleF(0, topPos, WindowSize.Width, WindowSize.Height - topPos), textBrush);
            topPos += textFormat.FontSize * 4;

            textRenderTarget.EndDraw(out long tag1, out long tag2);

            shader.Apply(context);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
            context.InputAssembler.SetIndexBuffer(indexBuffer, Format.R32_UInt, 0);
            context.PixelShader.SetShaderResource(0, textureView);
            context.DrawIndexed(6, 0, 0);
        }
    }
}

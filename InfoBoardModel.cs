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
                Width = windowEye.FrameSize.Width,
                Height = windowEye.FrameSize.Height,
                MipLevels = 1,
                OptionFlags = ResourceOptionFlags.None,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default
            };

            texture = new Texture2D(device, textureDesc);
            textureView = new ShaderResourceView(device, texture);

            //SharpDX.Direct2D1.Device d2dDevice;


            //DeviceContextRenderTarget d2dDevice = new DeviceContextRenderTarget(
            //    (new SharpDX.Direct2D1.Factory(SharpDX.Direct2D1.FactoryType.SingleThreaded)),            
            //    new RenderTargetProperties(new PixelFormat(swapChain.Description.ModeDescription.Format, SharpDX.Direct2D1.AlphaMode.Ignore)));

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

            textFormat = new TextFormat(directWriteFactory, "Arial", 20.0f);
            textBrush = new SolidColorBrush(textRenderTarget, SharpDX.Color4.Black);
        }

        public void Render(SharpDX.Direct3D11.DeviceContext context, ref EyeData eye)
        {
            var str = eye.EyeName + " EYE INFO\n";
            str += "Green Center X: " + eye.Coefficients.center_green_x.ToString(CultureInfo.InvariantCulture) + "\n";
            str += "Green Center Y: " + eye.Coefficients.center_green_y.ToString(CultureInfo.InvariantCulture) + "\n";
            str += "Blue Center X: " + eye.Coefficients.center_blue_x.ToString(CultureInfo.InvariantCulture) + "\n";
            str += "Blue Center Y: " + eye.Coefficients.center_blue_y.ToString(CultureInfo.InvariantCulture) + "\n";
            str += "Red Center X: " + eye.Coefficients.center_red_x.ToString(CultureInfo.InvariantCulture) + "\n";
            str += "Red Center Y: " + eye.Coefficients.center_red_y.ToString(CultureInfo.InvariantCulture) + "\n";

            textRenderTarget.BeginDraw();
            textRenderTarget.Clear(SharpDX.Color4.White);
            textRenderTarget.DrawText(str, textFormat, new SharpDX.Mathematics.Interop.RawRectangleF(0, 0, windowEye.FrameSize.Width, windowEye.FrameSize.Height), textBrush);
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

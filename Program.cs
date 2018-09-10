using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SharpDX.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Valve.VR;


namespace Undistort
{
    public static class Program
    {
        private struct VertexShaderData
        {
            public Matrix WorldViewProj;
        }

        private struct PixelShaderData
        {
            public Vector4 LightPosition;
            public int undistort;
            public int wireframe;
            public int controller;
            public int activecolor;
        }

        public struct DistortShaderData
        {
            public void Init(float init = 0f)
            {
                reserved1 = 1.0f;
                red_k1 = red_k2 = red_k3 = green_k1 = green_k2 = green_k3 = blue_k1 = blue_k2 = blue_k3 = init;
                center_red_x = center_red_y = center_green_x = center_green_y = center_blue_x = center_blue_y = 0;
            }
            public float red_k1, red_k2, red_k3;
            public float green_k1, green_k2, green_k3;
            public float blue_k1, blue_k2, blue_k3;
            public float center_red_x, center_red_y;
            public float center_green_x, center_green_y;
            public float center_blue_x, center_blue_y;
            public float reserved1; //for alignment to 32 bytes
        }


        public static bool Undistort;
        public static bool Wireframe;
        public static bool RenderHiddenMesh;

        private static CVRSystem vrSystem;
        private static CVRCompositor vrCompositor;
        private static uint maxTrackedDeviceCount;
        private static uint hmdID;
        private static Dictionary<uint, ETrackedControllerRole> controllers;
        private static uint[] controllerIDs = new uint[0];
        private static TrackedDevicePose_t[] currentPoses;
        private static TrackedDevicePose_t[] nextPoses;



        private static SharpDX.Direct3D11.Device d3dDevice;
        private static DeviceContext d3dDeviceContext;
        public static SwapChain d3dSwapChain;
        public static RawColor4 d3dClearColor;

        private static RasterizerState WireFrameRasterizerState;
        private static RasterizerState SolidRasteizerState;
        private static RasterizerState ncWireFrameRasterizerState;
        private static RasterizerState ncRasterizerState;

        public static DepthStencilState DepthStencilState;

        private static BlendState blendState;
        private static SamplerState samplerState;

        private static Matrix headMatrix;

        public static Texture2D UndistortTexture;
        public static RenderTargetView UndistortTextureView;
        public static ShaderResourceView UndistortShaderView;

        private static Shader hmaShader;
        private static VertexBufferBinding hmaVertexBufferBinding;

        private static JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
        private static IDictionary<string, object> lightHouseConfigJson;

        private static VertexShaderData vertexShaderData = default(VertexShaderData);
        private static PixelShaderData pixelShaderData = default(PixelShaderData);
        private static SharpDX.Direct3D11.Buffer vertexConstantBuffer;
        private static SharpDX.Direct3D11.Buffer pixelConstantBuffer;
        private static SharpDX.Direct3D11.Buffer coefficientConstantBuffer;

        public static Size WindowSize;
        public static Texture2D BackBufferTexture;
        public static RenderTargetView BackBufferTextureView;
        public static Texture2D BackBufferDepthTexture;
        public static DepthStencilView BackBufferDepthStencilView;

        public static SharpDX.Direct3D11.Buffer BackBufferIndexBuffer;

        public class EyeData
        {
            public EyeData(EVREye eye)
            {
                Eye = eye;
                Coefficients.Init(1);                
            }

            public EVREye Eye;
            public Size FrameSize;
            public IDictionary<string, object> Json;
            public DistortShaderData Coefficients;
            public Matrix Projection;
            public Matrix OriginalProjection;
            public Matrix EyeToHeadView;
            public HiddenAreaMesh_t HiddenAreaMesh;
            public SharpDX.Direct3D11.Buffer HiddenAreaMeshVertexBuffer;
            public Texture2D Texture;
            public RenderTargetView TextureView;
            public ShaderResourceView ShaderView;
            public Texture2D DepthTexture;
            public DepthStencilView DepthStencilView;
            public Matrix Intrinsics;
            public Matrix Extrinsics;
            public InfoBoardModel Board;
            public bool ShowBoard;
            public SharpDX.Direct3D11.Buffer BackBufferVertexBuffer;
            public string EyeName
            {
                get
                {
                    switch (Eye)
                    {
                        case EVREye.Eye_Left:
                            return "LEFT";
                        case EVREye.Eye_Right:
                            return "RIGHT";
                        default:
                            return "WTF";
                    }
                }
            }
        }


        public static EyeData leftEye = new EyeData(EVREye.Eye_Left);
        public static EyeData rightEye = new EyeData(EVREye.Eye_Right);


        private static Model environmentModel;
        private static Model controllerModel;
        public static Shader environmentShader;
        public static Shader backbufferShader;


        [Flags]
        public enum RenderFlag
        {
            Red = 1 << 0,
            Green = 1 << 1,
            Blue = 1 << 2,
            Left = 1 << 3,
            Right = 1 << 4,
            K1 = 1 << 5,
            K2 = 1 << 6,
            K3 = 1 << 7,
            ALL = Red | Green | Blue | Left | Right | K1 | K2 | K3
        }

        public static float zoomLevel = 1.0f;

        public static RenderFlag RenderFlags = RenderFlag.ALL;

        private static void MarshalUnmananagedArray2Struct<T>(IntPtr unmanagedArray, int length, out T[] mangagedArray)
        {
            var size = Marshal.SizeOf(typeof(T));
            mangagedArray = new T[length];

            for (int i = 0; i < length; i++)
            {
                IntPtr ins = new IntPtr(unmanagedArray.ToInt64() + i * size);
                mangagedArray[i] = Marshal.PtrToStructure<T>(ins);
            }
        }

        public static float adjustStep = 0.001f;

        [STAThread]
        private static void Main()
        {
            var initError = EVRInitError.None;

            vrSystem = OpenVR.Init(ref initError);

            if (initError != EVRInitError.None)
                return;

            var ovrPath = OpenVR.RuntimePath();

            LoadLHSettings(ovrPath);

            vrCompositor = OpenVR.Compositor;

            vrCompositor.CompositorBringToFront();
            vrCompositor.FadeGrid(5.0f, false);

            maxTrackedDeviceCount = OpenVR.k_unMaxTrackedDeviceCount;

            currentPoses = new TrackedDevicePose_t[maxTrackedDeviceCount];
            nextPoses = new TrackedDevicePose_t[maxTrackedDeviceCount];

            controllers = new Dictionary<uint, ETrackedControllerRole>();

            uint width = 1080;
            uint height = 1200;

            vrSystem.GetRecommendedRenderTargetSize(ref width, ref height);

            leftEye.FrameSize = rightEye.FrameSize = new Size((int)width, (int)height);
            WindowSize = new Size(1080, 1200 / 2);

            leftEye.Projection = leftEye.OriginalProjection = Convert(vrSystem.GetProjectionMatrix(EVREye.Eye_Left, 0.01f, 1000.0f));
            rightEye.OriginalProjection = rightEye.Projection = Convert(vrSystem.GetProjectionMatrix(EVREye.Eye_Right, 0.01f, 1000.0f));

            leftEye.EyeToHeadView = Convert(vrSystem.GetEyeToHeadTransform(EVREye.Eye_Left));
            rightEye.EyeToHeadView = Convert(vrSystem.GetEyeToHeadTransform(EVREye.Eye_Right));

            leftEye.HiddenAreaMesh = vrSystem.GetHiddenAreaMesh(EVREye.Eye_Left, EHiddenAreaMeshType.k_eHiddenAreaMesh_Standard);
            rightEye.HiddenAreaMesh = vrSystem.GetHiddenAreaMesh(EVREye.Eye_Right, EHiddenAreaMeshType.k_eHiddenAreaMesh_Standard);

            int adapterIndex = 0;

            vrSystem.GetDXGIOutputInfo(ref adapterIndex);

            using (var form = new RenderForm())
            {
                using (var factory = new Factory4())
                {
                    form.Text = "SteamVR Coefficient Utility";
                    form.ClientSize = WindowSize;
                    form.FormBorderStyle = FormBorderStyle.FixedSingle;
                    form.MinimizeBox = false;
                    form.MaximizeBox = false;

                    form.FormClosing += (s, e) =>
                    {
                        var confPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)) + "\\LH_Config_Out.json";
                        var jsonData = javaScriptSerializer.Serialize(lightHouseConfigJson);
                        var formatter = new JsonFormatter(jsonData);
                        File.WriteAllText(confPath, formatter.Format());
                    };

                    form.KeyUp += (s, e) =>
                    {
                        RenderHiddenMesh = e.Control;
                    };

                    form.KeyDown += (s, e) =>
                    {
                        RenderHiddenMesh = e.Control;

                        switch (e.KeyCode)
                        {
                            case Keys.NumPad5:
                                Undistort = !Undistort;
                                pixelShaderData.undistort = Undistort ? 1 : 0;
                                if (Undistort)
                                {
                                    //get raw matrix and modify scale value according to instrints/extrincts??
                                    //leftEye.Projection = GetRawMatrix(EVREye.Eye_Left, 0.01f, 1000.0f);
                                    //rightEye.Projection = GetRawMatrix(EVREye.Eye_Right, 0.01f, 1000.0f)
                                    SetProjectionZoomLevel();
                                }
                                else
                                {
                                    //reset proj                                    
                                    leftEye.Projection = leftEye.OriginalProjection;
                                    rightEye.Projection = rightEye.OriginalProjection;
                                }
                                break;
                            case Keys.PageUp:
                                Wireframe = !Wireframe;
                                pixelShaderData.wireframe = Wireframe ? 1 : 0;
                                break;
                            case Keys.Escape:
                                form.Close();
                                break;
                            case Keys.NumPad7:
                                RenderFlags ^= RenderFlag.Red;
                                break;
                            case Keys.NumPad8:
                                RenderFlags ^= RenderFlag.Green;
                                break;
                            case Keys.NumPad9:
                                RenderFlags ^= RenderFlag.Blue;
                                break;
                            case Keys.NumPad1:
                                RenderFlags ^= RenderFlag.K1;
                                break;
                            case Keys.NumPad2:
                                RenderFlags ^= RenderFlag.K2;
                                break;
                            case Keys.NumPad3:
                                RenderFlags ^= RenderFlag.K3;
                                break;
                            case Keys.NumPad4:
                                RenderFlags ^= RenderFlag.Left;
                                break;
                            case Keys.NumPad6:
                                RenderFlags ^= RenderFlag.Right;
                                break;
                            case Keys.Subtract:
                                zoomLevel -= 0.01f;
                                SetProjectionZoomLevel();
                                break;
                            case Keys.Add:
                                zoomLevel += 0.01f;
                                SetProjectionZoomLevel();
                                break;
                            case Keys.Home:
                                if (RenderFlags.HasFlag(RenderFlag.Left)) leftEye.Coefficients.Init();
                                if (RenderFlags.HasFlag(RenderFlag.Right)) rightEye.Coefficients.Init();
                                //vertexShaderData.ResetZoom();
                                break;
                            case Keys.Left:
                                if (e.Shift)
                                    CrossHairModel.MoveCenter(RenderFlags.HasFlag(RenderFlag.Left) ? -adjustStep : 0, 0, RenderFlags.HasFlag(RenderFlag.Right) ? -adjustStep : 0, 0);
                                else
                                {
                                    adjustStep *= 10;
                                    if (adjustStep > 1) adjustStep = 1;
                                }
                                break;
                            case Keys.Right:
                                if (e.Shift)
                                    CrossHairModel.MoveCenter(RenderFlags.HasFlag(RenderFlag.Left) ? adjustStep : 0, 0, RenderFlags.HasFlag(RenderFlag.Right) ? adjustStep : 0, 0);
                                else
                                {
                                    adjustStep /= 10;
                                    if (adjustStep < 0.00000001f) adjustStep = 0.00000001f;
                                }
                                break;
                            case Keys.Up:
                            case Keys.Down:
                                if (e.Shift)
                                {
                                    if (e.KeyCode == Keys.Down && e.Shift)
                                        CrossHairModel.MoveCenter(0, RenderFlags.HasFlag(RenderFlag.Left) ? -adjustStep : 0, 0, RenderFlags.HasFlag(RenderFlag.Right) ? -adjustStep : 0);
                                    if (e.KeyCode == Keys.Up && e.Shift)
                                        CrossHairModel.MoveCenter(0, RenderFlags.HasFlag(RenderFlag.Left) ? adjustStep : 0, 0, RenderFlags.HasFlag(RenderFlag.Right) ? adjustStep : 0);
                                    break;
                                }

                                var step = adjustStep;
                                if (e.KeyCode == Keys.Down) step *= -1f;
                                if (RenderFlags.HasFlag(RenderFlag.Left))
                                {
                                    if (RenderFlags.HasFlag(RenderFlag.Red))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) leftEye.Coefficients.red_k1 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) leftEye.Coefficients.red_k2 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) leftEye.Coefficients.red_k3 += step;
                                    }
                                    if (RenderFlags.HasFlag(RenderFlag.Green))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) leftEye.Coefficients.green_k1 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) leftEye.Coefficients.green_k2 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) leftEye.Coefficients.green_k3 += step;
                                    }

                                    if (RenderFlags.HasFlag(RenderFlag.Blue))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) leftEye.Coefficients.blue_k1 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) leftEye.Coefficients.blue_k2 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) leftEye.Coefficients.blue_k3 += step;
                                    }
                                }
                                if (RenderFlags.HasFlag(RenderFlag.Right))
                                {
                                    if (RenderFlags.HasFlag(RenderFlag.Red))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) rightEye.Coefficients.red_k1 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) rightEye.Coefficients.red_k2 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) rightEye.Coefficients.red_k3 += step;
                                    }
                                    if (RenderFlags.HasFlag(RenderFlag.Green))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) rightEye.Coefficients.green_k1 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) rightEye.Coefficients.green_k2 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) rightEye.Coefficients.green_k3 += step;
                                    }

                                    if (RenderFlags.HasFlag(RenderFlag.Blue))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) rightEye.Coefficients.blue_k1 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) rightEye.Coefficients.blue_k2 += step;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) rightEye.Coefficients.blue_k3 += step;
                                    }
                                }
                                break;
                        }
                    };

                    var adapter = factory.GetAdapter(adapterIndex);

                    var swapChainDescription = new SwapChainDescription
                    {
                        BufferCount = 1,
                        Flags = SwapChainFlags.None,
                        IsWindowed = true,
                        ModeDescription = new ModeDescription
                        {
                            Format = Format.B8G8R8A8_UNorm,
                            Width = WindowSize.Width,
                            Height = WindowSize.Height,
                            RefreshRate = new Rational(90, 1)
                        },
                        OutputHandle = form.Handle,
                        SampleDescription = new SampleDescription(1, 0),
                        SwapEffect = SwapEffect.Discard,
                        Usage = Usage.RenderTargetOutput
                    };

                    SharpDX.Direct3D11.Device.CreateWithSwapChain(adapter, DeviceCreationFlags.BgraSupport, swapChainDescription, out d3dDevice, out d3dSwapChain);

                    factory.MakeWindowAssociation(form.Handle, WindowAssociationFlags.None);

                    d3dDeviceContext = d3dDevice.ImmediateContext;

                    BackBufferTexture = d3dSwapChain.GetBackBuffer<Texture2D>(0);
                    BackBufferTextureView = new RenderTargetView(d3dDevice, BackBufferTexture);

                    var depthBufferDescription = new Texture2DDescription
                    {
                        Format = Format.D16_UNorm,
                        ArraySize = 1,
                        MipLevels = 1,
                        Width = WindowSize.Width,
                        Height = WindowSize.Height,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.DepthStencil,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None
                    };

                    BackBufferDepthTexture = new Texture2D(d3dDevice, depthBufferDescription);
                    BackBufferDepthStencilView = new DepthStencilView(d3dDevice, BackBufferDepthTexture);

                    // Create Eye Textures
                    var eyeTextureDescription = new Texture2DDescription
                    {
                        ArraySize = 1,
                        BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                        CpuAccessFlags = CpuAccessFlags.None,
                        Format = Format.B8G8R8A8_UNorm,
                        Width = leftEye.FrameSize.Width,
                        Height = leftEye.FrameSize.Height,
                        MipLevels = 1,
                        OptionFlags = ResourceOptionFlags.None,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default
                    };

                    leftEye.Texture = rightEye.Texture = new Texture2D(d3dDevice, eyeTextureDescription);
                    leftEye.TextureView = rightEye.TextureView = new RenderTargetView(d3dDevice, leftEye.Texture);
                    leftEye.ShaderView = rightEye.ShaderView = new ShaderResourceView(d3dDevice, leftEye.Texture);

                    UndistortTexture = new Texture2D(d3dDevice, eyeTextureDescription);
                    UndistortTextureView = new RenderTargetView(d3dDevice, UndistortTexture);
                    UndistortShaderView = new ShaderResourceView(d3dDevice, UndistortTexture);

                    // Create Eye Depth Buffer
                    eyeTextureDescription.BindFlags = BindFlags.DepthStencil;
                    eyeTextureDescription.Format = Format.D32_Float;
                    leftEye.DepthTexture = rightEye.DepthTexture = new Texture2D(d3dDevice, eyeTextureDescription);
                    leftEye.DepthStencilView = rightEye.DepthStencilView = new DepthStencilView(d3dDevice, leftEye.DepthTexture);

                    var modelLoader = new ModelLoader(d3dDevice);

                    environmentShader = new Shader(d3dDevice, "Model_VS", "Model_PS", new InputElement[]
                    {
                        new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                        new InputElement("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                        new InputElement("TEXCOORD", 0, Format.R32G32_Float, 24, 0)
                    });

                    backbufferShader = new Shader(d3dDevice, "Backbuffer_VS", "Backbuffer_PS", new InputElement[]
                    {
                        new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                        new InputElement("TEXCOORD", 0, Format.R32G32_Float, 12, 0)
                    });

                    BackBufferIndexBuffer = SharpDX.Direct3D11.Buffer.Create(d3dDevice, BindFlags.IndexBuffer, new int[] { 0, 2, 3, 0, 1, 2 });
                    leftEye.BackBufferVertexBuffer = SharpDX.Direct3D11.Buffer.Create(d3dDevice, BindFlags.VertexBuffer, new float[] {
                            -1f, -1f, 0f, 0, 1, //0
                            0f, -1f, 0f, 1, 1,  //1
                            0f, 1f, 0f, 1, 0,   //2
                            -1f, 1f, 0f, 0, 0 //3
                        });
                    rightEye.BackBufferVertexBuffer = SharpDX.Direct3D11.Buffer.Create(d3dDevice, BindFlags.VertexBuffer, new float[] {
                            0f, -1f, 0f, 0, 1, //0
                            1f, -1f, 0f, 1, 1,  //1
                            1f, 1f, 0f, 1, 0,   //2
                            0f, 1f, 0f, 0, 0 //3
                        });
                   
                    UndistortShader.Load(d3dDevice);

                    //var fileName = ovrPath + @"..\..\workshop\content\250820\928165436\spacecpod\spacecpod.obj";
                    var fileName = ovrPath + @"..\..\workshop\content\250820\716774474\VertigoRoom\VertigoRoom.obj";
                    environmentModel = modelLoader.Load(fileName);
                    environmentModel.SetInputLayout(d3dDevice, ShaderSignature.GetInputSignature(environmentShader.vertexShaderByteCode));

                    fileName = ovrPath + "\\resources\\rendermodels\\vr_controller_vive_1_5\\vr_controller_vive_1_5.obj";
                    controllerModel = modelLoader.Load(fileName);
                    controllerModel.SetInputLayout(d3dDevice, ShaderSignature.GetInputSignature(environmentShader.vertexShaderByteCode));

                    for (uint cdevice = 0; cdevice < maxTrackedDeviceCount; cdevice++)
                    {
                        var deviceClass = vrSystem.GetTrackedDeviceClass(cdevice);

                        switch (deviceClass)
                        {
                            case ETrackedDeviceClass.HMD:
                                hmdID = cdevice;
                                break;
                            case ETrackedDeviceClass.Controller:
                                if (!controllers.ContainsKey(cdevice))
                                {
                                    controllers.Add(cdevice, vrSystem.GetControllerRoleForTrackedDeviceIndex(cdevice));
                                    controllerIDs = controllers.Keys.ToArray();
                                }
                                break;
                        }
                    }

                    vertexConstantBuffer = new SharpDX.Direct3D11.Buffer(d3dDevice, Utilities.SizeOf<VertexShaderData>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
                    pixelConstantBuffer = new SharpDX.Direct3D11.Buffer(d3dDevice, Utilities.SizeOf<PixelShaderData>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
                    coefficientConstantBuffer = new SharpDX.Direct3D11.Buffer(d3dDevice, Utilities.SizeOf<DistortShaderData>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);

                    var rasterizerStateDescription = RasterizerStateDescription.Default();
                    rasterizerStateDescription.IsFrontCounterClockwise = true;
                    rasterizerStateDescription.FillMode = FillMode.Solid;
                    rasterizerStateDescription.IsAntialiasedLineEnabled = false;
                    rasterizerStateDescription.IsMultisampleEnabled = true;
                    SolidRasteizerState = new RasterizerState(d3dDevice, rasterizerStateDescription);
                    rasterizerStateDescription.CullMode = CullMode.None;
                    ncRasterizerState = new RasterizerState(d3dDevice, rasterizerStateDescription);
                    rasterizerStateDescription.CullMode = CullMode.Back;
                    rasterizerStateDescription.FillMode = FillMode.Wireframe;
                    WireFrameRasterizerState = new RasterizerState(d3dDevice, rasterizerStateDescription);
                    rasterizerStateDescription.CullMode = CullMode.None;
                    ncWireFrameRasterizerState = new RasterizerState(d3dDevice, rasterizerStateDescription);

                    var blendStateDescription = BlendStateDescription.Default();
                    blendStateDescription.RenderTarget[0].BlendOperation = BlendOperation.Add;
                    blendStateDescription.RenderTarget[0].SourceBlend = BlendOption.One;
                    blendStateDescription.RenderTarget[0].DestinationBlend = BlendOption.One;
                    blendStateDescription.RenderTarget[0].IsBlendEnabled = true;
                    blendState = new BlendState(d3dDevice, blendStateDescription);

                    var depthStateDescription = DepthStencilStateDescription.Default();
                    depthStateDescription.DepthComparison = Comparison.LessEqual;
                    depthStateDescription.IsDepthEnabled = true;
                    depthStateDescription.IsStencilEnabled = false;
                    DepthStencilState = new DepthStencilState(d3dDevice, depthStateDescription);

                    var samplerStateDescription = SamplerStateDescription.Default();

                    samplerStateDescription.Filter = Filter.MinMagMipLinear;
                    samplerStateDescription.BorderColor = d3dClearColor;
                    samplerStateDescription.AddressU = TextureAddressMode.Border;
                    samplerStateDescription.AddressV = TextureAddressMode.Border;

                    samplerState = new SamplerState(d3dDevice, samplerStateDescription);

                    d3dClearColor = new RawColor4(0.0f, 0.0f, 0.0f, 1);

                    var vrEvent = new VREvent_t();
                    var eventSize = (uint)Utilities.SizeOf<VREvent_t>();

                    headMatrix = Matrix.Identity;

                    d3dDeviceContext.VertexShader.SetConstantBuffer(0, vertexConstantBuffer);
                    d3dDeviceContext.PixelShader.SetConstantBuffer(1, pixelConstantBuffer);
                    d3dDeviceContext.PixelShader.SetConstantBuffer(2, coefficientConstantBuffer);

                    //lgy = -0.005899557863971562;
                    //rgy = -0.001024579015277309;                    

                    leftEye.Board = new InfoBoardModel(); leftEye.Board.Init(d3dDevice); leftEye.ShowBoard = true;
                    rightEye.Board = new InfoBoardModel(); rightEye.Board.Init(d3dDevice); rightEye.ShowBoard = true;

                    CrossHairModel.Init(d3dDevice, leftEye.Coefficients.center_green_x, leftEye.Coefficients.center_green_y, rightEye.Coefficients.center_green_x, rightEye.Coefficients.center_green_y);
                    CrossHairModel.MoveCenter(0, 0, 0, 0);

                    hmaShader = new Shader(d3dDevice, "HiddenMesh_VS", "HiddenMesh_PS", new InputElement[]
                    {
                        new InputElement("POSITION", 0, Format.R32G32_Float, 0, 0)
                    });

                    MarshalUnmananagedArray2Struct<HmdVector2_t>(leftEye.HiddenAreaMesh.pVertexData, (int)(leftEye.HiddenAreaMesh.unTriangleCount * 3), out var leftHAMVertices);
                    MarshalUnmananagedArray2Struct<HmdVector2_t>(rightEye.HiddenAreaMesh.pVertexData, (int)(rightEye.HiddenAreaMesh.unTriangleCount * 3), out var rightHAMVertices);

                    //convert 0/1 range to -1/1
                    for (var i = 0; i < leftHAMVertices.Length; i++)
                    {
                        var vert = leftHAMVertices[i];
                        vert.v0 -= 0.5f; vert.v0 *= 2;
                        vert.v1 -= 0.5f; vert.v1 *= 2;
                        leftHAMVertices[i] = vert;
                    }
                    for (var i = 0; i < rightHAMVertices.Length; i++)
                    {
                        var vert = rightHAMVertices[i];
                        vert.v0 -= 0.5f; vert.v0 *= 2;
                        vert.v1 -= 0.5f; vert.v1 *= 2;
                        rightHAMVertices[i] = vert;
                    }


                    leftEye.HiddenAreaMeshVertexBuffer = SharpDX.Direct3D11.Buffer.Create(d3dDevice, BindFlags.VertexBuffer, leftHAMVertices);
                    rightEye.HiddenAreaMeshVertexBuffer = SharpDX.Direct3D11.Buffer.Create(d3dDevice, BindFlags.VertexBuffer, rightHAMVertices);

                    SetProjectionZoomLevel();

                    RenderLoop.Run(form, () =>
                    {
                        while (vrSystem.PollNextEvent(ref vrEvent, eventSize))
                        {
                            switch ((EVREventType)vrEvent.eventType)
                            {
                                case EVREventType.VREvent_TrackedDeviceUpdated:
                                    controllers.Remove(vrEvent.trackedDeviceIndex);
                                    controllers.Add(vrEvent.trackedDeviceIndex, vrSystem.GetControllerRoleForTrackedDeviceIndex(vrEvent.trackedDeviceIndex));
                                    break;
                                case EVREventType.VREvent_TrackedDeviceActivated:
                                    if (!controllers.ContainsKey(vrEvent.trackedDeviceIndex))
                                    {
                                        controllers.Add(vrEvent.trackedDeviceIndex, vrSystem.GetControllerRoleForTrackedDeviceIndex(vrEvent.trackedDeviceIndex));
                                        controllerIDs = controllers.Keys.ToArray();
                                    }
                                    break;

                                case EVREventType.VREvent_TrackedDeviceDeactivated:
                                    controllers.Remove(vrEvent.trackedDeviceIndex);
                                    controllerIDs = controllers.Keys.ToArray();
                                    break;
                                case EVREventType.VREvent_Quit:
                                    //case EVREventType.VREvent_ProcessQuit:
                                    form.Close();
                                    break;
                                case EVREventType.VREvent_ButtonPress:
                                    var role = vrSystem.GetControllerRoleForTrackedDeviceIndex(vrEvent.trackedDeviceIndex);
                                    var button = vrEvent.data.controller.button;
                                    var state = default(VRControllerState_t);
                                    vrSystem.GetControllerState(vrEvent.trackedDeviceIndex, ref state, (uint)Utilities.SizeOf<VRControllerState_t>());
                                    ButtonPressed(role, ref state, (EVRButtonId)vrEvent.data.controller.button);
                                    break;
                                default:
                                    //System.Diagnostics.Debug.WriteLine((EVREventType)vrEvent.eventType);
                                    break;
                            }
                        }

                        // Update Device Tracking
                        vrCompositor.WaitGetPoses(currentPoses, nextPoses);

                        if (currentPoses[hmdID].bPoseIsValid)
                            Convert(ref currentPoses[hmdID].mDeviceToAbsoluteTracking, ref headMatrix);

                        d3dDeviceContext.ClearRenderTargetView(BackBufferTextureView, d3dClearColor); // clear backbuffer once
                        d3dDeviceContext.ClearDepthStencilView(BackBufferDepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);

                        #region Render LeftEye
                        RenderView(ref leftEye);
                        #endregion 

                        #region Render RightEye
                        RenderView(ref rightEye);
                        #endregion

                        // Show Backbuffer
                        d3dSwapChain.Present(0, PresentFlags.None);
                    });
                }
            }

        }

        private static void SetProjectionZoomLevel()
        {
            leftEye.Projection.M11 = leftEye.OriginalProjection.M11 * zoomLevel;
            leftEye.Projection.M22 = leftEye.OriginalProjection.M22 * zoomLevel;
            rightEye.Projection.M11 = leftEye.OriginalProjection.M11 * zoomLevel;
            rightEye.Projection.M22 = leftEye.OriginalProjection.M22 * zoomLevel;
        }

        private static Matrix GetRawMatrix(EVREye eye, float zNear, float zFar)
        {
            float fLeft = 0f;
            float fRight = 0f;
            float fTop = 0f;
            float fBottom = 0f;
            vrSystem.GetProjectionRaw(eye, ref fLeft, ref fRight, ref fTop, ref fBottom);
            var proj = new Matrix(0);

            float idx = 1.0f / (fRight - fLeft);
            float idy = 1.0f / (fBottom - fTop);
            float idz = 1.0f / (zFar - zNear);
            float sx = fRight + fLeft;
            float sy = fBottom + fTop;

            proj.M11 = 2 * idx; proj.M13 = sx * idx;
            proj.M22 = 2 * idy; proj.M23 = sy * idy;
            proj.M33 = -zFar * idz; proj.M34 = -zFar * zNear * idz;
            proj.M43 = -1.0f;

            proj.Transpose();
            return proj;
        }

        private static void ButtonPressed(ETrackedControllerRole role, ref VRControllerState_t state, EVRButtonId button)
        {
            switch (button)
            {
                case EVRButtonId.k_EButton_Grip: //grip
                    {
                        switch (role)
                        {
                            case ETrackedControllerRole.LeftHand:
                                leftEye.ShowBoard = !leftEye.ShowBoard;
                                break;
                            case ETrackedControllerRole.RightHand:
                                rightEye.ShowBoard = !rightEye.ShowBoard;
                                break;
                        }
                        break;
                    }
                case EVRButtonId.k_EButton_ApplicationMenu: //grip
                    {
                        break;
                    }
                case EVRButtonId.k_EButton_SteamVR_Touchpad:
                    {
                        if (state.rAxis0.x > -0.3 && state.rAxis0.x < 0.3 && state.rAxis0.y > -0.3 && state.rAxis0.y < 0.3)
                        {
                            //center pressed
                        }
                        else if (state.rAxis0.x < 0 && Math.Abs(state.rAxis0.y) < Math.Abs(state.rAxis0.x))
                        {
                            //left

                        }
                        else if (state.rAxis0.x > 0 && Math.Abs(state.rAxis0.y) < Math.Abs(state.rAxis0.x))
                        {
                            //right
                        }
                        else if (state.rAxis0.y > 0 && Math.Abs(state.rAxis0.x) < Math.Abs(state.rAxis0.y))
                        {
                            //up
                        }
                        else if (state.rAxis0.y < 0 && Math.Abs(state.rAxis0.x) < Math.Abs(state.rAxis0.y))
                        {
                            //down
                        }
                    }
                    break;
            }

        }

        public static bool IsEyeActive(EVREye eye)
        {
            return (RenderFlags.HasFlag(RenderFlag.Left) && eye == EVREye.Eye_Left) ||
                   (RenderFlags.HasFlag(RenderFlag.Right) && eye == EVREye.Eye_Right);
        }

        private static void RenderView(ref EyeData eye)
        {            
            d3dDeviceContext.PixelShader.SetSampler(0, samplerState);
            d3dDeviceContext.Rasterizer.SetViewport(0, 0, eye.FrameSize.Width, eye.FrameSize.Height);
            d3dDeviceContext.OutputMerger.SetTargets(eye.DepthStencilView, eye.TextureView);
            d3dDeviceContext.ClearRenderTargetView(eye.TextureView, d3dClearColor);
            d3dDeviceContext.ClearDepthStencilView(eye.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
            d3dDeviceContext.OutputMerger.SetDepthStencilState(DepthStencilState);
            d3dDeviceContext.OutputMerger.SetBlendState(blendState);
            d3dDeviceContext.Rasterizer.State = Wireframe ? WireFrameRasterizerState : SolidRasteizerState;
            if (eye.Eye == EVREye.Eye_Left)
                d3dDeviceContext.UpdateSubresource(ref leftEye.Coefficients, coefficientConstantBuffer);
            else if (eye.Eye == EVREye.Eye_Right)
                d3dDeviceContext.UpdateSubresource(ref rightEye.Coefficients, coefficientConstantBuffer);

            //vertexShaderData.view = Matrix.Invert(eye.EyeToHeadView);

            environmentShader.Apply(d3dDeviceContext);

            pixelShaderData.LightPosition = new Vector4(0, 1, 0, 0);

            vertexShaderData.WorldViewProj = Matrix.Invert(eye.EyeToHeadView * headMatrix) * eye.Projection; vertexShaderData.WorldViewProj.Transpose();

            //vertexShaderData.Intrinsics = eye.Intrinsics; vertexShaderData.Intrinsics.Transpose();
            for (int i = 0; i < 3; i++)
            {
                if (i == 0 && !RenderFlags.HasFlag(RenderFlag.Red)) continue;
                if (i == 1 && !RenderFlags.HasFlag(RenderFlag.Green)) continue;
                if (i == 2 && !RenderFlags.HasFlag(RenderFlag.Blue)) continue;
                pixelShaderData.activecolor = i;
                pixelShaderData.controller = 0;                       
                d3dDeviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);
                d3dDeviceContext.UpdateSubresource(ref pixelShaderData, pixelConstantBuffer);
                environmentModel.Render(d3dDeviceContext);
            }

            
            if (Wireframe) //revert            
                d3dDeviceContext.Rasterizer.State = SolidRasteizerState;

            d3dDeviceContext.OutputMerger.SetBlendState(null);
            CrossHairModel.Render(d3dDeviceContext, eye.Eye);

            //Render info panels
            pixelShaderData.activecolor = -1;
            pixelShaderData.controller = 1;

            Matrix controllerMat = default(Matrix);
            foreach (var controllerId in controllerIDs)
            {
                if (controllers[controllerId] == ETrackedControllerRole.Invalid)
                    controllers[controllerId] = vrSystem.GetControllerRoleForTrackedDeviceIndex(controllerId);

                var controllerRole = controllers[controllerId];

                if (currentPoses[controllerId].bPoseIsValid)
                {
                    Convert(ref currentPoses[controllerId].mDeviceToAbsoluteTracking, ref controllerMat);
                    vertexShaderData.WorldViewProj = controllerMat * Matrix.Invert(eye.EyeToHeadView * headMatrix) * eye.Projection; vertexShaderData.WorldViewProj.Transpose();
                    d3dDeviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);
                    d3dDeviceContext.UpdateSubresource(ref pixelShaderData, pixelConstantBuffer);
                    environmentShader.Apply(d3dDeviceContext); //back 
                    controllerModel.Render(d3dDeviceContext);
                    if (leftEye.ShowBoard && controllerRole == ETrackedControllerRole.LeftHand)
                        leftEye.Board.Render(d3dDeviceContext, ref leftEye);
                    if (rightEye.ShowBoard && controllerRole == ETrackedControllerRole.RightHand)
                        rightEye.Board.Render(d3dDeviceContext, ref rightEye);
                }
            }

            if (RenderHiddenMesh && IsEyeActive(eye.Eye))
            {
                d3dDeviceContext.Rasterizer.State = Wireframe ? ncWireFrameRasterizerState : ncRasterizerState;
                //render hidden mesh area just for control distortion area
                vertexShaderData.WorldViewProj = Matrix.Invert(eye.EyeToHeadView * headMatrix) * eye.Projection; vertexShaderData.WorldViewProj.Transpose();
                d3dDeviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);
                d3dDeviceContext.UpdateSubresource(ref pixelShaderData, pixelConstantBuffer);
                d3dDeviceContext.OutputMerger.SetBlendState(null);
                d3dDeviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                hmaVertexBufferBinding = new VertexBufferBinding(eye.HiddenAreaMeshVertexBuffer, sizeof(float) * 2, 0);
                d3dDeviceContext.InputAssembler.SetVertexBuffers(0, hmaVertexBufferBinding);
                hmaShader.Apply(d3dDeviceContext);
                d3dDeviceContext.Draw((int)(3 * eye.HiddenAreaMesh.unTriangleCount), 0);
            }

            if (Wireframe) //revert if in wireframe
                d3dDeviceContext.Rasterizer.State = SolidRasteizerState;

            var texView = eye.TextureView;
            var shaderView = eye.ShaderView;

            if (Undistort)
            {
                //render and undistort         
                texView = UndistortTextureView;
                shaderView = UndistortShaderView;
                UndistortShader.Render(d3dDeviceContext, ref eye);
            }

            //render eye to screen            
            d3dDeviceContext.Rasterizer.SetViewport(0, 0, WindowSize.Width, WindowSize.Height);                        
            d3dDeviceContext.OutputMerger.SetTargets(BackBufferDepthStencilView, BackBufferTextureView);
            d3dDeviceContext.OutputMerger.SetDepthStencilState(DepthStencilState);
            d3dDeviceContext.OutputMerger.SetBlendState(null);
            d3dDeviceContext.PixelShader.SetShaderResource(0, shaderView);
            d3dDeviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            d3dDeviceContext.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(eye.BackBufferVertexBuffer, sizeof(float) * 5, 0));
            d3dDeviceContext.InputAssembler.SetIndexBuffer(BackBufferIndexBuffer, Format.R32_UInt, 0);
            backbufferShader.Apply(d3dDeviceContext);
            d3dDeviceContext.DrawIndexed(6, 0, 0);

            //submit to openvr
            var texture = new Texture_t
            {
                eType = ETextureType.DirectX,
                eColorSpace = EColorSpace.Gamma,
                handle = texView.Resource.NativePointer
            };

            var bounds = new VRTextureBounds_t
            {
                uMin = 0.0f,
                uMax = 1.0f,
                vMin = 0.0f,
                vMax = 1.0f,
            };

            var submitError = vrCompositor.Submit((EVREye)eye.Eye, ref texture, ref bounds, Undistort ? EVRSubmitFlags.Submit_LensDistortionAlreadyApplied : EVRSubmitFlags.Submit_Default);

            if (submitError != EVRCompositorError.None)
                Debug.WriteLine(submitError);

        }

        private static void Convert(ref HmdMatrix34_t source, ref Matrix destination)
        {
            destination.M11 = source.m0;
            destination.M21 = source.m1;
            destination.M31 = source.m2;
            destination.M41 = source.m3;
            destination.M12 = source.m4;
            destination.M22 = source.m5;
            destination.M32 = source.m6;
            destination.M42 = source.m7;
            destination.M13 = source.m8;
            destination.M23 = source.m9;
            destination.M33 = source.m10;
            destination.M43 = source.m11;
            destination.M14 = 0.0f;
            destination.M24 = 0.0f;
            destination.M34 = 0.0f;
            destination.M44 = 1.0f;
        }

        private static Matrix Convert(HmdMatrix34_t source)
        {
            var destination = new Matrix();

            destination.M11 = source.m0;
            destination.M21 = source.m1;
            destination.M31 = source.m2;
            destination.M41 = source.m3;
            destination.M12 = source.m4;
            destination.M22 = source.m5;
            destination.M32 = source.m6;
            destination.M42 = source.m7;
            destination.M13 = source.m8;
            destination.M23 = source.m9;
            destination.M33 = source.m10;
            destination.M43 = source.m11;
            destination.M14 = 0.0f;
            destination.M24 = 0.0f;
            destination.M34 = 0.0f;
            destination.M44 = 1.0f;

            return destination;
        }

        private static Matrix Convert(HmdMatrix44_t source)
        {
            var destination = new Matrix();

            destination.M11 = source.m0;
            destination.M21 = source.m1;
            destination.M31 = source.m2;
            destination.M41 = source.m3;
            destination.M12 = source.m4;
            destination.M22 = source.m5;
            destination.M32 = source.m6;
            destination.M42 = source.m7;
            destination.M13 = source.m8;
            destination.M23 = source.m9;
            destination.M33 = source.m10;
            destination.M43 = source.m11;
            destination.M14 = source.m12;
            destination.M24 = source.m13;
            destination.M34 = source.m14;
            destination.M44 = source.m15;

            return destination;
        }

        private static void LoadLHSettings(string ovrPath)
        {
            var toolPath = ovrPath + @"tools\lighthouse\bin\win32\lighthouse_console.exe";
            var confPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)) + "\\LH_Config_In.json";
            var processInfo = new ProcessStartInfo
            {
                Arguments = "downloadconfig " + confPath,
                CreateNoWindow = true,
                FileName = toolPath,
                WindowStyle = ProcessWindowStyle.Hidden

            };
            var process = Process.Start(processInfo);
            process.WaitForExit();

            var jsonData = File.ReadAllText(confPath);
            var formatter = new JsonFormatter(jsonData);
            File.WriteAllText(confPath, formatter.Format());
            lightHouseConfigJson = javaScriptSerializer.Deserialize<IDictionary<string, object>>(jsonData);

            var transforms = lightHouseConfigJson["tracking_to_eye_transform"] as object[];
            leftEye.Json = (transforms[0]) as IDictionary<string, object>;
            rightEye.Json = (transforms[1]) as IDictionary<string, object>;

            var lgx = (float)System.Convert.ToDouble((leftEye.Json["distortion"] as Dictionary<string, object>)["center_x"]);
            var lgy = (float)System.Convert.ToDouble((leftEye.Json["distortion"] as Dictionary<string, object>)["center_y"]);
            var lbx = (float)System.Convert.ToDouble((leftEye.Json["distortion_blue"] as Dictionary<string, object>)["center_x"]);
            var lby = (float)System.Convert.ToDouble((leftEye.Json["distortion_blue"] as Dictionary<string, object>)["center_y"]);
            var lrx = (float)System.Convert.ToDouble((leftEye.Json["distortion_red"] as Dictionary<string, object>)["center_x"]);
            var lry = (float)System.Convert.ToDouble((leftEye.Json["distortion_red"] as Dictionary<string, object>)["center_y"]);

            var rgx = (float)System.Convert.ToDouble((rightEye.Json["distortion"] as Dictionary<string, object>)["center_x"]);
            var rgy = (float)System.Convert.ToDouble((rightEye.Json["distortion"] as Dictionary<string, object>)["center_y"]);
            var rbx = (float)System.Convert.ToDouble((rightEye.Json["distortion_blue"] as Dictionary<string, object>)["center_x"]);
            var rby = (float)System.Convert.ToDouble((rightEye.Json["distortion_blue"] as Dictionary<string, object>)["center_y"]);
            var rrx = (float)System.Convert.ToDouble((rightEye.Json["distortion_red"] as Dictionary<string, object>)["center_x"]);
            var rry = (float)System.Convert.ToDouble((rightEye.Json["distortion_red"] as Dictionary<string, object>)["center_y"]);


            leftEye.Coefficients.center_green_x = lgx; leftEye.Coefficients.center_green_y = lgy;
            leftEye.Coefficients.center_blue_x = lbx; leftEye.Coefficients.center_blue_y = lby;
            leftEye.Coefficients.center_red_x = lrx; leftEye.Coefficients.center_red_y = lry;

            rightEye.Coefficients.center_green_x = rgx; rightEye.Coefficients.center_green_y = rgy;
            rightEye.Coefficients.center_blue_x = rbx; rightEye.Coefficients.center_blue_y = rby;
            rightEye.Coefficients.center_red_x = rrx; rightEye.Coefficients.center_red_y = rry;

            var lg = ((leftEye.Json["distortion"] as Dictionary<string, object>)["coeffs"] as object[]).Select(a => (float)System.Convert.ToDouble(a)).ToArray();
            leftEye.Coefficients.green_k1 = lg[0]; leftEye.Coefficients.green_k2 = lg[1]; leftEye.Coefficients.green_k3 = lg[2];
            var lb = ((leftEye.Json["distortion_blue"] as Dictionary<string, object>)["coeffs"] as object[]).Select(a => (float)System.Convert.ToDouble(a)).ToArray();
            leftEye.Coefficients.blue_k1 = lb[0]; leftEye.Coefficients.blue_k2 = lb[1]; leftEye.Coefficients.blue_k3 = lb[2];
            var lr = ((leftEye.Json["distortion_red"] as Dictionary<string, object>)["coeffs"] as object[]).Select(a => (float)System.Convert.ToDouble(a)).ToArray();
            leftEye.Coefficients.red_k1 = lr[0]; leftEye.Coefficients.red_k2 = lr[1]; leftEye.Coefficients.red_k3 = lr[2];

            var row = leftEye.Json["intrinsics"] as object[];
            var col = row[0] as object[];
            leftEye.Intrinsics = new Matrix(1);
            leftEye.Intrinsics.M11 = (float)System.Convert.ToDouble(col[0]);
            leftEye.Intrinsics.M12 = (float)System.Convert.ToDouble(col[1]);
            leftEye.Intrinsics.M13 = (float)System.Convert.ToDouble(col[2]);
            col = row[1] as object[];
            leftEye.Intrinsics.M21 = (float)System.Convert.ToDouble(col[0]);
            leftEye.Intrinsics.M22 = (float)System.Convert.ToDouble(col[1]);
            leftEye.Intrinsics.M23 = (float)System.Convert.ToDouble(col[2]);
            col = row[2] as object[];
            leftEye.Intrinsics.M31 = (float)System.Convert.ToDouble(col[0]);
            leftEye.Intrinsics.M32 = (float)System.Convert.ToDouble(col[1]);
            leftEye.Intrinsics.M33 = (float)System.Convert.ToDouble(col[2]);

            row = leftEye.Json["extrinsics"] as object[];
            col = row[0] as object[];
            leftEye.Extrinsics = new Matrix(1);
            leftEye.Extrinsics.M11 = (float)System.Convert.ToDouble(col[0]);
            leftEye.Extrinsics.M12 = (float)System.Convert.ToDouble(col[1]);
            leftEye.Extrinsics.M13 = (float)System.Convert.ToDouble(col[2]);
            leftEye.Extrinsics.M14 = (float)System.Convert.ToDouble(col[3]);
            col = row[1] as object[];
            leftEye.Extrinsics.M21 = (float)System.Convert.ToDouble(col[0]);
            leftEye.Extrinsics.M22 = (float)System.Convert.ToDouble(col[1]);
            leftEye.Extrinsics.M23 = (float)System.Convert.ToDouble(col[2]);
            leftEye.Extrinsics.M24 = (float)System.Convert.ToDouble(col[3]);
            col = row[2] as object[];
            leftEye.Extrinsics.M31 = (float)System.Convert.ToDouble(col[0]);
            leftEye.Extrinsics.M32 = (float)System.Convert.ToDouble(col[1]);
            leftEye.Extrinsics.M33 = (float)System.Convert.ToDouble(col[2]);
            leftEye.Extrinsics.M34 = (float)System.Convert.ToDouble(col[3]);

            row = rightEye.Json["intrinsics"] as object[];
            col = row[0] as object[];
            rightEye.Intrinsics = new Matrix(1);
            rightEye.Intrinsics.M11 = (float)System.Convert.ToDouble(col[0]);
            rightEye.Intrinsics.M12 = (float)System.Convert.ToDouble(col[1]);
            rightEye.Intrinsics.M13 = (float)System.Convert.ToDouble(col[2]);
            col = row[1] as object[];
            rightEye.Intrinsics.M21 = (float)System.Convert.ToDouble(col[0]);
            rightEye.Intrinsics.M22 = (float)System.Convert.ToDouble(col[1]);
            rightEye.Intrinsics.M23 = (float)System.Convert.ToDouble(col[2]);
            col = row[2] as object[];
            rightEye.Intrinsics.M31 = (float)System.Convert.ToDouble(col[0]);
            rightEye.Intrinsics.M32 = (float)System.Convert.ToDouble(col[1]);
            rightEye.Intrinsics.M33 = (float)System.Convert.ToDouble(col[2]);

            row = rightEye.Json["extrinsics"] as object[];
            col = row[0] as object[];
            rightEye.Extrinsics = new Matrix(1);
            rightEye.Extrinsics.M11 = (float)System.Convert.ToDouble(col[0]);
            rightEye.Extrinsics.M12 = (float)System.Convert.ToDouble(col[1]);
            rightEye.Extrinsics.M13 = (float)System.Convert.ToDouble(col[2]);
            rightEye.Extrinsics.M14 = (float)System.Convert.ToDouble(col[3]);
            col = row[1] as object[];
            rightEye.Extrinsics.M21 = (float)System.Convert.ToDouble(col[0]);
            rightEye.Extrinsics.M22 = (float)System.Convert.ToDouble(col[1]);
            rightEye.Extrinsics.M23 = (float)System.Convert.ToDouble(col[2]);
            rightEye.Extrinsics.M24 = (float)System.Convert.ToDouble(col[3]);
            col = row[2] as object[];
            rightEye.Extrinsics.M31 = (float)System.Convert.ToDouble(col[0]);
            rightEye.Extrinsics.M32 = (float)System.Convert.ToDouble(col[1]);
            rightEye.Extrinsics.M33 = (float)System.Convert.ToDouble(col[2]);
            rightEye.Extrinsics.M34 = (float)System.Convert.ToDouble(col[3]);

            var rg = ((rightEye.Json["distortion"] as Dictionary<string, object>)["coeffs"] as object[]).Select(a => (float)System.Convert.ToDouble(a)).ToArray();
            rightEye.Coefficients.green_k1 = rg[0]; rightEye.Coefficients.green_k2 = rg[1]; rightEye.Coefficients.green_k3 = rg[2];
            var rb = ((rightEye.Json["distortion_blue"] as Dictionary<string, object>)["coeffs"] as object[]).Select(a => (float)System.Convert.ToDouble(a)).ToArray();
            rightEye.Coefficients.blue_k1 = rb[0]; rightEye.Coefficients.blue_k2 = rb[1]; rightEye.Coefficients.blue_k3 = rb[2];
            var rr = ((rightEye.Json["distortion_red"] as Dictionary<string, object>)["coeffs"] as object[]).Select(a => (float)System.Convert.ToDouble(a)).ToArray();
            rightEye.Coefficients.red_k1 = rr[0]; rightEye.Coefficients.red_k2 = rr[1]; rightEye.Coefficients.red_k3 = rr[2];


        }


    }
}
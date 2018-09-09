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
            public Matrix Head;
            public Matrix EyeToHead;
            public Matrix Projection;
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
        private static bool Wireframe;
        public static bool RenderHiddenMesh;

        private static CVRSystem vrSystem;
        private static CVRCompositor vrCompositor;
        private static uint maxTrackedDeviceCount;
        private static uint hmdID;
        private static Dictionary<uint, ETrackedControllerRole> controllers;
        private static uint[] controllerIDs = new uint[0];
        private static TrackedDevicePose_t[] currentPoses;
        private static TrackedDevicePose_t[] nextPoses;



        private static SharpDX.Direct3D11.Device device;
        private static DeviceContext deviceContext;
        public static SwapChain swapChain;
        public static RawColor4 clearColor;

        private static RasterizerState wireFrameRasterizerState;
        private static RasterizerState rasterizerState;
        private static RasterizerState ncWireFrameRasterizerState;
        private static RasterizerState ncRasterizerState;

        public static DepthStencilState depthStencilState;

        private static BlendState blendState;
        private static SamplerState samplerState;

        private static Matrix headMatrix;

        public static Texture2D undistortTexture;
        public static RenderTargetView undistortTextureView;

        private static Shader hmaShader;
        private static VertexBufferBinding hmaVertexBufferBinding;

        private static JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
        private static IDictionary<string, object> lightHouseConfigJson;

        private static VertexShaderData vertexShaderData = default(VertexShaderData);
        private static PixelShaderData pixelShaderData = default(PixelShaderData);
        private static SharpDX.Direct3D11.Buffer vertexConstantBuffer;
        private static SharpDX.Direct3D11.Buffer pixelConstantBuffer;
        private static SharpDX.Direct3D11.Buffer coefficientConstantBuffer;

        public struct EyeData
        {
            public int Eye;
            public Size FrameSize;
            public IDictionary<string, object> Json;
            public DistortShaderData Coefficients;
            public Matrix Projection;
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

            public string EyeName
            {
                get
                {
                    switch (Eye)
                    {
                        case -1:
                            return "WINDOW";
                        case 0:
                            return "LEFT";
                        case 1:
                            return "RIGHT";
                        default:
                            return "I THE";
                    }
                }
            }
        }


        public static EyeData leftEye = default(EyeData);
        public static EyeData rightEye = default(EyeData);
        public static EyeData windowEye = default(EyeData);

        private static Model environmentModel;
        private static Model controllerModel;
        public static Shader environmentShader;


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

        public static double zoomLevel = 0.6000000238418579; //1.0;

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
            windowEye.FrameSize = new Size(1080 / 2, 1200 / 2);

            windowEye.Projection = leftEye.Projection = Convert(vrSystem.GetProjectionMatrix(EVREye.Eye_Left, 0.01f, 1000.0f));
            rightEye.Projection = Convert(vrSystem.GetProjectionMatrix(EVREye.Eye_Right, 0.01f, 1000.0f));

            windowEye.EyeToHeadView = leftEye.EyeToHeadView = Convert(vrSystem.GetEyeToHeadTransform(EVREye.Eye_Left));
            rightEye.EyeToHeadView = Convert(vrSystem.GetEyeToHeadTransform(EVREye.Eye_Right));

            windowEye.HiddenAreaMesh = leftEye.HiddenAreaMesh = vrSystem.GetHiddenAreaMesh(EVREye.Eye_Left, EHiddenAreaMeshType.k_eHiddenAreaMesh_Standard);
            rightEye.HiddenAreaMesh = vrSystem.GetHiddenAreaMesh(EVREye.Eye_Right, EHiddenAreaMeshType.k_eHiddenAreaMesh_Standard);

            int adapterIndex = 0;

            vrSystem.GetDXGIOutputInfo(ref adapterIndex);

            using (var form = new RenderForm())
            {
                using (var factory = new Factory4())
                {
                    form.Text = "SteamVR Coefficient Utility";
                    form.ClientSize = windowEye.FrameSize;
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
                                zoomLevel *= 0.999;
                                break;
                            case Keys.Add:
                                zoomLevel *= 1.001;
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

                                if (e.KeyCode == Keys.Down) adjustStep *= -1f;
                                if (RenderFlags.HasFlag(RenderFlag.Left))
                                {
                                    if (RenderFlags.HasFlag(RenderFlag.Red))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) leftEye.Coefficients.red_k1 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) leftEye.Coefficients.red_k2 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) leftEye.Coefficients.red_k3 += adjustStep;
                                    }
                                    if (RenderFlags.HasFlag(RenderFlag.Green))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) leftEye.Coefficients.green_k1 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) leftEye.Coefficients.green_k2 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) leftEye.Coefficients.green_k3 += adjustStep;
                                    }

                                    if (RenderFlags.HasFlag(RenderFlag.Blue))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) leftEye.Coefficients.blue_k1 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) leftEye.Coefficients.blue_k2 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) leftEye.Coefficients.blue_k3 += adjustStep;
                                    }
                                }
                                if (RenderFlags.HasFlag(RenderFlag.Right))
                                {
                                    if (RenderFlags.HasFlag(RenderFlag.Red))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) rightEye.Coefficients.red_k1 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) rightEye.Coefficients.red_k2 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) rightEye.Coefficients.red_k3 += adjustStep;
                                    }
                                    if (RenderFlags.HasFlag(RenderFlag.Green))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) rightEye.Coefficients.green_k1 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) rightEye.Coefficients.green_k2 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) rightEye.Coefficients.green_k3 += adjustStep;
                                    }

                                    if (RenderFlags.HasFlag(RenderFlag.Blue))
                                    {
                                        if (RenderFlags.HasFlag(RenderFlag.K1)) rightEye.Coefficients.blue_k1 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K2)) rightEye.Coefficients.blue_k2 += adjustStep;
                                        if (RenderFlags.HasFlag(RenderFlag.K3)) rightEye.Coefficients.blue_k3 += adjustStep;
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
                            Width = windowEye.FrameSize.Width,
                            Height = windowEye.FrameSize.Height,
                            RefreshRate = new Rational(90, 1)
                        },
                        OutputHandle = form.Handle,
                        SampleDescription = new SampleDescription(1, 0),
                        SwapEffect = SwapEffect.Discard,
                        Usage = Usage.RenderTargetOutput
                    };

                    SharpDX.Direct3D11.Device.CreateWithSwapChain(adapter, DeviceCreationFlags.BgraSupport, swapChainDescription, out device, out swapChain);

                    factory.MakeWindowAssociation(form.Handle, WindowAssociationFlags.None);

                    deviceContext = device.ImmediateContext;

                    windowEye.Texture = swapChain.GetBackBuffer<Texture2D>(0);
                    windowEye.TextureView = new RenderTargetView(device, windowEye.Texture);

                    var depthBufferDescription = new Texture2DDescription
                    {
                        Format = Format.D16_UNorm,
                        ArraySize = 1,
                        MipLevels = 1,
                        Width = windowEye.FrameSize.Width,
                        Height = windowEye.FrameSize.Height,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.DepthStencil,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None
                    };

                    windowEye.DepthTexture = new Texture2D(device, depthBufferDescription);
                    windowEye.DepthStencilView = new DepthStencilView(device, windowEye.DepthTexture);

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

                    leftEye.Texture = rightEye.Texture = new Texture2D(device, eyeTextureDescription);
                    undistortTexture = new Texture2D(device, eyeTextureDescription);

                    leftEye.TextureView = rightEye.TextureView = new RenderTargetView(device, leftEye.Texture);
                    leftEye.ShaderView = rightEye.ShaderView = new ShaderResourceView(device, leftEye.Texture);

                    undistortTextureView = new RenderTargetView(device, undistortTexture);

                    // Create Eye Depth Buffer
                    eyeTextureDescription.BindFlags = BindFlags.DepthStencil;
                    eyeTextureDescription.Format = Format.D32_Float;

                    leftEye.DepthTexture = rightEye.DepthTexture = new Texture2D(device, eyeTextureDescription);
                    leftEye.DepthStencilView = rightEye.DepthStencilView = new DepthStencilView(device, leftEye.DepthTexture);

                    var modelLoader = new ModelLoader(device);

                    environmentShader = new Shader(device, "Model_VS", "Model_PS", new InputElement[]
                    {
                        new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                        new InputElement("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                        new InputElement("TEXCOORD", 0, Format.R32G32_Float, 24, 0)
                    });

                    UndistortShader.Load(device);

                    //var fileName = ovrPath + @"..\..\workshop\content\250820\928165436\spacecpod\spacecpod.obj";
                    var fileName = ovrPath + @"..\..\workshop\content\250820\716774474\VertigoRoom\VertigoRoom.obj";
                    environmentModel = modelLoader.Load(fileName);
                    environmentModel.SetInputLayout(device, ShaderSignature.GetInputSignature(environmentShader.vertexShaderByteCode));

                    fileName = ovrPath + "\\resources\\rendermodels\\vr_controller_vive_1_5\\vr_controller_vive_1_5.obj";
                    controllerModel = modelLoader.Load(fileName);
                    controllerModel.SetInputLayout(device, ShaderSignature.GetInputSignature(environmentShader.vertexShaderByteCode));

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

                    vertexConstantBuffer = new SharpDX.Direct3D11.Buffer(device, Utilities.SizeOf<VertexShaderData>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
                    pixelConstantBuffer = new SharpDX.Direct3D11.Buffer(device, Utilities.SizeOf<PixelShaderData>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
                    coefficientConstantBuffer = new SharpDX.Direct3D11.Buffer(device, Utilities.SizeOf<DistortShaderData>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);

                    var rasterizerStateDescription = RasterizerStateDescription.Default();
                    rasterizerStateDescription.IsFrontCounterClockwise = true;
                    rasterizerStateDescription.FillMode = FillMode.Solid;
                    rasterizerStateDescription.IsAntialiasedLineEnabled = false;
                    rasterizerStateDescription.IsMultisampleEnabled = true;
                    rasterizerState = new RasterizerState(device, rasterizerStateDescription);
                    rasterizerStateDescription.CullMode = CullMode.None;
                    ncRasterizerState = new RasterizerState(device, rasterizerStateDescription);
                    rasterizerStateDescription.CullMode = CullMode.Back;
                    rasterizerStateDescription.FillMode = FillMode.Wireframe;
                    wireFrameRasterizerState = new RasterizerState(device, rasterizerStateDescription);
                    rasterizerStateDescription.CullMode = CullMode.None;
                    ncWireFrameRasterizerState = new RasterizerState(device, rasterizerStateDescription);

                    var blendStateDescription = BlendStateDescription.Default();
                    blendStateDescription.RenderTarget[0].BlendOperation = BlendOperation.Add;
                    blendStateDescription.RenderTarget[0].SourceBlend = BlendOption.One;
                    blendStateDescription.RenderTarget[0].DestinationBlend = BlendOption.One;
                    blendStateDescription.RenderTarget[0].IsBlendEnabled = true;
                    blendState = new BlendState(device, blendStateDescription);

                    var depthStateDescription = DepthStencilStateDescription.Default();
                    depthStateDescription.DepthComparison = Comparison.LessEqual;
                    depthStateDescription.IsDepthEnabled = true;
                    depthStateDescription.IsStencilEnabled = false;
                    depthStencilState = new DepthStencilState(device, depthStateDescription);

                    var samplerStateDescription = SamplerStateDescription.Default();

                    samplerStateDescription.Filter = Filter.MinMagMipLinear;
                    samplerStateDescription.BorderColor = clearColor;
                    samplerStateDescription.AddressU = TextureAddressMode.Border;
                    samplerStateDescription.AddressV = TextureAddressMode.Border;

                    samplerState = new SamplerState(device, samplerStateDescription);

                    clearColor = new RawColor4(0.0f, 0.0f, 0.0f, 1);

                    var vrEvent = new VREvent_t();
                    var eventSize = (uint)Utilities.SizeOf<VREvent_t>();

                    headMatrix = Matrix.Identity;

                    deviceContext.VertexShader.SetConstantBuffer(0, vertexConstantBuffer);
                    deviceContext.PixelShader.SetConstantBuffer(1, pixelConstantBuffer);
                    deviceContext.PixelShader.SetConstantBuffer(2, coefficientConstantBuffer);

                    //lgy = -0.005899557863971562;
                    //rgy = -0.001024579015277309;                    

                    leftEye.Board = new InfoBoardModel(); leftEye.Board.Init(device); leftEye.ShowBoard = true; windowEye.Board = leftEye.Board; windowEye.ShowBoard = true;
                    rightEye.Board = new InfoBoardModel(); rightEye.Board.Init(device); rightEye.ShowBoard = true;

                    CrossHairModel.Init(device, leftEye.Coefficients.center_green_x, leftEye.Coefficients.center_green_y, rightEye.Coefficients.center_green_x, rightEye.Coefficients.center_green_y);
                    CrossHairModel.MoveCenter(0, 0, 0, 0);

                    hmaShader = new Shader(device, "HiddenMesh_VS", "HiddenMesh_PS", new InputElement[]
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


                    leftEye.HiddenAreaMeshVertexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, leftHAMVertices);
                    rightEye.HiddenAreaMeshVertexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, rightHAMVertices);
                    windowEye.HiddenAreaMeshVertexBuffer = leftEye.HiddenAreaMeshVertexBuffer;

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

                        #region Render LeftEye and present
                        RenderView(ref leftEye);
                        #endregion 

                        #region Render RightEye and present
                        RenderView(ref rightEye);
                        #endregion

                        #region Render Left eye to Window 
                        windowEye.Coefficients = leftEye.Coefficients;
                        RenderView(ref windowEye);
                        #endregion 

                        // Show Backbuffer
                        swapChain.Present(0, PresentFlags.None);
                    });
                }
            }

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
                                windowEye.ShowBoard = leftEye.ShowBoard;
                                break;
                            case ETrackedControllerRole.RightHand:
                                leftEye.ShowBoard = !leftEye.ShowBoard;
                                windowEye.ShowBoard = leftEye.ShowBoard;
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

        public static bool IsEyeActive(int eye)
        {
            return (RenderFlags.HasFlag(RenderFlag.Left) && eye != 1) ||
                   (RenderFlags.HasFlag(RenderFlag.Right) && eye == 1);
        }

        private static void RenderView(ref EyeData eye)
        {
            deviceContext.PixelShader.SetSampler(0, samplerState);
            deviceContext.Rasterizer.SetViewport(0, 0, eye.FrameSize.Width, eye.FrameSize.Height);
            deviceContext.OutputMerger.SetTargets(eye.DepthStencilView, eye.TextureView);
            deviceContext.ClearRenderTargetView(eye.TextureView, clearColor);
            deviceContext.ClearDepthStencilView(eye.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);

            deviceContext.OutputMerger.SetDepthStencilState(depthStencilState);
            deviceContext.OutputMerger.SetBlendState(blendState);
            deviceContext.Rasterizer.State = Wireframe ? wireFrameRasterizerState : rasterizerState;
            if (eye.Eye == 0)
                deviceContext.UpdateSubresource(ref leftEye.Coefficients, coefficientConstantBuffer);
            else if (eye.Eye == 1)
                deviceContext.UpdateSubresource(ref rightEye.Coefficients, coefficientConstantBuffer);
            //vertexShaderData.view = Matrix.Invert(eye.EyeToHeadView);

            environmentShader.Apply(deviceContext);

            pixelShaderData.LightPosition = new Vector4(0, 1, 0, 0);

            vertexShaderData.Head = headMatrix; vertexShaderData.Head.Transpose();
            vertexShaderData.EyeToHead = eye.EyeToHeadView; vertexShaderData.EyeToHead.Invert(); vertexShaderData.EyeToHead.Transpose();
            vertexShaderData.Projection = eye.Projection; vertexShaderData.Projection.Transpose();
            vertexShaderData.WorldViewProj = Matrix.Invert(eye.EyeToHeadView * headMatrix) * eye.Projection; vertexShaderData.WorldViewProj.Transpose();

            //vertexShaderData.Intrinsics = eye.Intrinsics; vertexShaderData.Intrinsics.Transpose();
            for (int i = 0; i < 3; i++)
            {
                if (i == 0 && !RenderFlags.HasFlag(RenderFlag.Red)) continue;
                if (i == 1 && !RenderFlags.HasFlag(RenderFlag.Green)) continue;
                if (i == 2 && !RenderFlags.HasFlag(RenderFlag.Blue)) continue;
                pixelShaderData.activecolor = i;
                pixelShaderData.controller = 0;
                //vertexShaderData.zoomLevel = Undistort? zoomLevel : 1.0;                
                deviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);
                deviceContext.UpdateSubresource(ref pixelShaderData, pixelConstantBuffer);
                environmentModel.Render(deviceContext);
            }

            deviceContext.OutputMerger.SetBlendState(null);
            if (Wireframe) //revert            
                deviceContext.Rasterizer.State = rasterizerState;


            CrossHairModel.Render(deviceContext, eye.Eye);

            //Render infoboard


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
                    deviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);
                    deviceContext.UpdateSubresource(ref pixelShaderData, pixelConstantBuffer);
                    environmentShader.Apply(deviceContext); //back 
                    controllerModel.Render(deviceContext);
                    if (leftEye.ShowBoard && controllerRole == ETrackedControllerRole.LeftHand)
                        leftEye.Board.Render(deviceContext, ref leftEye);
                    if (rightEye.ShowBoard && controllerRole == ETrackedControllerRole.RightHand)
                        rightEye.Board.Render(deviceContext, ref rightEye);
                }
            }

            if (RenderHiddenMesh && IsEyeActive((int)eye.Eye))
            {
                deviceContext.Rasterizer.State = Wireframe ? ncWireFrameRasterizerState : ncRasterizerState;
                //render hidden mesh area just for control distortion
                vertexShaderData.Head = headMatrix; vertexShaderData.Head.Transpose();
                vertexShaderData.EyeToHead = eye.EyeToHeadView; vertexShaderData.EyeToHead.Invert(); vertexShaderData.EyeToHead.Transpose();
                vertexShaderData.Projection = eye.Projection; vertexShaderData.Projection.Transpose();
                vertexShaderData.WorldViewProj = Matrix.Invert(eye.EyeToHeadView * headMatrix) * eye.Projection; vertexShaderData.WorldViewProj.Transpose();
                deviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);
                deviceContext.UpdateSubresource(ref pixelShaderData, pixelConstantBuffer);
                deviceContext.OutputMerger.SetBlendState(null);
                hmaShader.Apply(deviceContext);
                deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                hmaVertexBufferBinding = new VertexBufferBinding(eye.HiddenAreaMeshVertexBuffer, sizeof(float) * 2, 0);
                deviceContext.InputAssembler.SetVertexBuffers(0, hmaVertexBufferBinding);
                deviceContext.Draw((int)(3 * eye.HiddenAreaMesh.unTriangleCount), 0);
            }

            if (Wireframe) //revert            
                deviceContext.Rasterizer.State = rasterizerState;


            var texView = eye.TextureView;

            if (Undistort)
            {
                //render and undistort         
                if (eye.Eye != -1)
                    texView = undistortTextureView;
                UndistortShader.Render(deviceContext, ref eye);
            }

            if (eye.Eye == -1)
                return;

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
            leftEye.Coefficients.Init(1);
            rightEye.Coefficients.Init(1);
            leftEye.Eye = (int)EVREye.Eye_Left;
            rightEye.Eye = (int)EVREye.Eye_Right;
            windowEye.Eye = -1;

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

            windowEye.Intrinsics = leftEye.Intrinsics;
            windowEye.Extrinsics = leftEye.Extrinsics;

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
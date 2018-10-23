using FastJsonLib;
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
using System.Windows.Forms;
using Valve.VR;


namespace Undistort
{
    public static class Program
    {
        private static void Log(string fmt, params object[] args)
        {
            File.AppendAllText("Debug.log", string.Format(fmt + "\n", args));
        }

        private struct VertexShaderData
        {
            public Matrix WorldViewProj;
            public Vector2 ActiveEyeCenter;
            public float ActiveAspect;
            public float Reserved1;
        }

        public struct PixelShaderData
        {
            public Vector3 LightPosition;
            public float Persistence;
            public int _Undistort; //bool is 1 byte inside struct, we use int to convert to 4 bytes and use getter setter
            public bool Undistort { get { return _Undistort > 0; } }
            private int _Wireframe;
            public bool Wireframe { get { return _Wireframe == 1; } set { _Wireframe = value ? 1 : 0; } }
            private int _Controller;
            public bool Controller { get { return _Controller == 1; } set { _Controller = value ? 1 : 0; } }
            public int ActiveColor;
        }

        public struct DistortShaderData
        {
            public Vector4 RedCoeffs;

            public Vector4 GreenCoeffs;

            public Vector4 BlueCoeffs;

            public Vector4 RedCenter;

            public Vector4 GreenCenter;

            public Vector4 BlueCenter;

            public Vector2 EyeCenter;
            public float GrowToUndistort;
            public float UndistortR2Cutoff;

            public float Aspect;
            public float FocalX;
            public float FocalY;
            public int ActiveEye;

            public Matrix Extrinsics;
            public Matrix3x3 Intrinsics;
            public Vector3 Reserved2;

        }


        //public static bool Undistort;
        //public static bool Wireframe;
        public static bool RenderHiddenMesh = true;

        private static CVRSystem vrSystem;
        private static CVRCompositor vrCompositor;
        private static uint maxTrackedDeviceCount;
        private static uint hmdID;
        private static Dictionary<uint, ETrackedControllerRole> controllers;
        private static uint[] controllerIDs = new uint[0];
        private static TrackedDevicePose_t[] currentPoses;
        private static TrackedDevicePose_t[] nextPoses;



        public static SharpDX.Direct3D11.Device d3dDevice;
        private static DeviceContext d3dDeviceContext;
        public static SwapChain d3dSwapChain;
        public static RawColor4 d3dClearColor;

        private static RasterizerState WireFrameRasterizerState;
        private static RasterizerState SolidRasteizerState;
        private static RasterizerState ncWireFrameRasterizerState;
        private static RasterizerState ncRasterizerState;

        public static DepthStencilState DepthStencilState;
        public static DepthStencilState ControllerDepthStencilState;

        private static BlendState blendState;
        private static SamplerState samplerState;

        private static Matrix headMatrix;

        public static Texture2D UndistortTexture;
        public static RenderTargetView UndistortTextureView;
        public static ShaderResourceView UndistortShaderView;

        private static Shader hmaShader;
        private static VertexBufferBinding hmaVertexBufferBinding;

        private static IDictionary<string, object> lightHouseConfigJson;

        private static VertexShaderData vertexShaderData = default(VertexShaderData);
        public static PixelShaderData pixelShaderData = default(PixelShaderData);
        private static SharpDX.Direct3D11.Buffer vertexConstantBuffer;
        private static SharpDX.Direct3D11.Buffer pixelConstantBuffer;
        private static SharpDX.Direct3D11.Buffer coefficientConstantBuffer;

        public static Size WindowSize;
        public static Texture2D BackBufferTexture;
        public static RenderTargetView BackBufferTextureView;
        public static Texture2D BackBufferDepthTexture;
        public static DepthStencilView BackBufferDepthStencilView;

        public static SharpDX.Direct3D11.Buffer BackBufferIndexBuffer;

        public static int RenderMode = 0;

        public class EyeData
        {
            public const float Near = 0.01f;
            public const float Far = 1000f;


            public EyeData(EVREye eye)
            {
                Eye = eye;
                ResetDistortionCoefficients();
            }

            public EVREye Eye;
            public SizeF PanelSize;
            public SizeF FrameSize;
            public IDictionary<string, object> Json;
            public Matrix OriginalProjection;
            public Matrix EyeToHeadView;
            public HiddenAreaMesh_t HiddenAreaMesh;
            public SharpDX.Direct3D11.Buffer HiddenAreaMeshVertexBuffer;
            public Texture2D Texture;
            public RenderTargetView TextureView;
            public ShaderResourceView ShaderView;
            public Texture2D DepthTexture;
            public DepthStencilView DepthStencilView;
            public DistortShaderData DistortionData;
            public DistortShaderData OriginalDistortionData;
            public DistortShaderData GetData(bool original)
            {
                return original ? OriginalDistortionData : DistortionData;
            }
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

            public void ResetDistortionCoefficients()
            {
                DistortionData.RedCoeffs = Vector4.Zero; DistortionData.RedCoeffs.W = 1;
                DistortionData.GreenCoeffs = Vector4.Zero; DistortionData.GreenCoeffs.W = 1;
                DistortionData.BlueCoeffs = Vector4.Zero; DistortionData.BlueCoeffs.W = 1;
                DistortionData.RedCenter = Vector4.Zero;
                DistortionData.GreenCenter = Vector4.Zero;
                DistortionData.BlueCenter = Vector4.Zero;
            }

            public void UpdateIntrinsicsFromFocusAndCenter()
            {
                DistortionData.Intrinsics.M11 = 2.0f * DistortionData.FocalX / PanelSize.Width;
                DistortionData.Intrinsics.M31 = DistortionData.EyeCenter.X;
                DistortionData.Intrinsics.M22 = 2.0f * DistortionData.FocalY / PanelSize.Height;
                DistortionData.Intrinsics.M32 = DistortionData.EyeCenter.Y;
            }

            public void CalcFocusCenterAspect()
            {
                DistortionData.FocalX = PanelSize.Width / 2 * DistortionData.Intrinsics.M11;
                DistortionData.FocalY = PanelSize.Height / 2 * DistortionData.Intrinsics.M22;
                DistortionData.EyeCenter.X = DistortionData.Intrinsics.M31;
                DistortionData.EyeCenter.Y = DistortionData.Intrinsics.M32;
                DistortionData.Aspect = DistortionData.Intrinsics.M11 / DistortionData.Intrinsics.M22;
            }

            public Matrix GetProjectionFromIntrinsics()
            {
                var scale = 1.0f + DistortionData.GrowToUndistort;
                var matrix = Matrix.Zero;

                matrix.M11 = DistortionData.Intrinsics.M11 / scale;
                matrix.M31 = DistortionData.Intrinsics.M31 / scale;
                matrix.M22 = DistortionData.Intrinsics.M22 / scale;
                matrix.M32 = DistortionData.Intrinsics.M32 / scale;
                matrix.M34 = -1f;
                matrix.M33 = (Near + Far) / (Near - Far);
                matrix.M43 = (Far * Near) / (Near - Far);

                return matrix;
            }

            internal void ResetEyeCenters()
            {
                DistortionData.EyeCenter.X = DistortionData.EyeCenter.Y = 0;
            }
        }


        public static EyeData leftEye;
        public static EyeData rightEye;


        private static Model environmentModel;
        private static Model controllerModel;
        public static Shader environmentShader;
        public static Shader backbufferShader;


        [Flags]
        public enum RenderFlag
        {
            RedActive = 1 << 0,
            GreenActive = 1 << 1,
            BlueActive = 1 << 2,
            Left = 1 << 3,
            Right = 1 << 4,
            K1 = 1 << 5,
            K2 = 1 << 6,
            K3 = 1 << 7,
            RenderRed = 1 << 8,
            RenderGreen = 1 << 9,
            RenderBlue = 1 << 10,
            ALL = RedActive | GreenActive | BlueActive | Left | Right | K1 | K2 | K3 | RenderRed | RenderGreen | RenderBlue
        }

        //public static float zoomLevel = 1.0f;

        public static RenderFlag RenderFlags = RenderFlag.ALL;

        private static void IntPtrToStructArray<T>(IntPtr unmanagedArray, int length, out T[] mangagedArray)
        {
            var size = Marshal.SizeOf(typeof(T));
            mangagedArray = new T[length];

            for (int i = 0; i < length; i++)
            {
                IntPtr ins = new IntPtr(unmanagedArray.ToInt64() + i * size);
                mangagedArray[i] = (T)Marshal.PtrToStructure(ins, typeof(T));
            }
        }

        public static float AdjustStep = 0.001f;

        public static string OvrPath;
        private static RenderForm MainForm;

        private static uint ScreenWidth;
        private static uint ScreenHeight;
        public static float ScreenAspect;

        [STAThread]
        private static void Main()
        {
            if (File.Exists("Debug.log")) File.Delete("Debug.log");

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;


            var initError = EVRInitError.None;

            vrSystem = OpenVR.Init(ref initError);

            if (initError != EVRInitError.None)
            {
                Log("Error: Initialize OpenVR, {0}", initError.ToString());
                return;
            }

            OvrPath = OpenVR.RuntimePath();

            LoadLHSettings(OvrPath);

            vrCompositor = OpenVR.Compositor;

            vrCompositor.CompositorBringToFront();
            vrCompositor.FadeGrid(5.0f, false);

            maxTrackedDeviceCount = OpenVR.k_unMaxTrackedDeviceCount;

            currentPoses = new TrackedDevicePose_t[maxTrackedDeviceCount];
            nextPoses = new TrackedDevicePose_t[maxTrackedDeviceCount];

            controllers = new Dictionary<uint, ETrackedControllerRole>();

            vrSystem.GetRecommendedRenderTargetSize(ref ScreenWidth, ref ScreenHeight);

            //update aspect with recommended sizes
            ScreenAspect = (float)ScreenWidth / (float)ScreenHeight;

            leftEye.FrameSize = rightEye.FrameSize = new Size((int)ScreenWidth, (int)ScreenHeight);
            ScreenWidth *= 2;

            //scale down proportionally to fit
            while (ScreenWidth > Screen.PrimaryScreen.Bounds.Width || ScreenHeight > Screen.PrimaryScreen.Bounds.Height)
            {
                ScreenWidth /= 2;
                ScreenHeight /= 2;
            }

            WindowSize = new Size((int)ScreenWidth, (int)ScreenHeight);

            leftEye.OriginalProjection = Convert(vrSystem.GetProjectionMatrix(EVREye.Eye_Left, EyeData.Near, EyeData.Far));
            rightEye.OriginalProjection = Convert(vrSystem.GetProjectionMatrix(EVREye.Eye_Right, EyeData.Near, EyeData.Far));

            leftEye.EyeToHeadView = Convert(vrSystem.GetEyeToHeadTransform(EVREye.Eye_Left));
            rightEye.EyeToHeadView = Convert(vrSystem.GetEyeToHeadTransform(EVREye.Eye_Right));

            leftEye.HiddenAreaMesh = vrSystem.GetHiddenAreaMesh(EVREye.Eye_Left, EHiddenAreaMeshType.k_eHiddenAreaMesh_Standard);
            rightEye.HiddenAreaMesh = vrSystem.GetHiddenAreaMesh(EVREye.Eye_Right, EHiddenAreaMeshType.k_eHiddenAreaMesh_Standard);

            int adapterIndex = 0;

            vrSystem.GetDXGIOutputInfo(ref adapterIndex);

            using (MainForm = new RenderForm())
            {
                using (var factory = new Factory4())
                {
                    MainForm.StartPosition = FormStartPosition.CenterScreen;
                    MainForm.Text = "SteamVR Lens Adjustment Utility";
                    MainForm.ClientSize = WindowSize;
                    MainForm.FormBorderStyle = FormBorderStyle.FixedSingle;
                    MainForm.MinimizeBox = false;
                    MainForm.MaximizeBox = false;

                    MainForm.KeyDown += (s, e) =>
                    {
                        if (e.Control)
                            ToggleHiddenMesh();

                        switch (e.KeyCode)
                        {
                            case Keys.NumPad5:
                                ToggleDistortion();
                                break;
                            case Keys.PageUp:
                                ChangeRenderMode();
                                break;
                            case Keys.Escape:
                                MainForm.Close();
                                break;
                            case Keys.NumPad7:
                                RenderFlags ^= RenderFlag.RedActive;
                                break;
                            case Keys.NumPad8:
                                RenderFlags ^= RenderFlag.GreenActive;
                                break;
                            case Keys.NumPad9:
                                RenderFlags ^= RenderFlag.BlueActive;
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
                                AdjustFocus(-AdjustStep, -AdjustStep);
                                break;
                            case Keys.Add:
                                AdjustFocus(AdjustStep, AdjustStep);
                                break;
                            case Keys.Home:
                                ResetEyes();
                                break;
                            case Keys.Left:
                                if (e.Shift)
                                    AdjustEyeCenters(-AdjustStep, 0);
                                else
                                    IncreaseAdjustStep();
                                break;
                            case Keys.Right:
                                if (e.Shift)
                                    AdjustEyeCenters(AdjustStep, 0);
                                else
                                    DecreaseAdjustStep();
                                break;
                            case Keys.Up:
                            case Keys.Down:
                                if (e.Shift)
                                {
                                    if (e.KeyCode == Keys.Down && e.Shift)
                                        AdjustEyeCenters(0, -AdjustStep);
                                    if (e.KeyCode == Keys.Up && e.Shift)
                                        AdjustEyeCenters(0, AdjustStep);
                                    break;
                                }

                                var step = AdjustStep;
                                if (e.KeyCode == Keys.Down) step *= -1f;
                                AdjustCoefficients(step);
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
                        OutputHandle = MainForm.Handle,
                        SampleDescription = new SampleDescription(1, 0),
                        SwapEffect = SwapEffect.Discard,
                        Usage = Usage.RenderTargetOutput
                    };
                    
                    SharpDX.Direct3D11.Device.CreateWithSwapChain(adapter, DeviceCreationFlags.BgraSupport /*| DeviceCreationFlags.Debug*/, swapChainDescription, out d3dDevice, out d3dSwapChain);

                    factory.MakeWindowAssociation(MainForm.Handle, WindowAssociationFlags.None);

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
                        Width = (int)leftEye.FrameSize.Width,
                        Height = (int)leftEye.FrameSize.Height,
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
                    //var fileName = ovrPath + @"..\..\workshop\content\250820\716774474\VertigoRoom\VertigoRoom.obj";
                    //var fileName = ovrPath + @"..\..\workshop\content\250820\686754013\holochamber\holochamber.obj";
                    //var fileName = OvrPath + @"..\..\workshop\content\250820\717646476\TheCube\TheCube.obj";
                    var fileName = @"environment\environment.obj";


                    environmentModel = modelLoader.Load(fileName);
                    environmentModel.SetInputLayout(d3dDevice, ShaderSignature.GetInputSignature(environmentShader.vertexShaderByteCode));

                    fileName = OvrPath + "\\resources\\rendermodels\\vr_controller_vive_1_5\\vr_controller_vive_1_5.obj";
                    controllerModel = modelLoader.Load(fileName);
                    controllerModel.SetInputLayout(d3dDevice, ShaderSignature.GetInputSignature(environmentShader.vertexShaderByteCode));

                    QueryDevices();

                    vertexShaderData.Reserved1 = 0f; //prevent compiler warning

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
                    depthStateDescription.DepthComparison = Comparison.Less;
                    ControllerDepthStencilState = new DepthStencilState(d3dDevice, depthStateDescription);

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

                    AdjustmentPanelModel.Init(d3dDevice);
                    CrossHairModel.Init(d3dDevice);
                    PointerModel.Init(d3dDevice);
                    AdjustCenter(0, 0, 0, 0);

                    hmaShader = new Shader(d3dDevice, "HiddenMesh_VS", "HiddenMesh_PS", new InputElement[]
                    {
                        new InputElement("POSITION", 0, Format.R32G32_Float, 0, 0)
                    });

                    IntPtrToStructArray<HmdVector2_t>(leftEye.HiddenAreaMesh.pVertexData, (int)(leftEye.HiddenAreaMesh.unTriangleCount * 3), out var leftHAMVertices);
                    IntPtrToStructArray<HmdVector2_t>(rightEye.HiddenAreaMesh.pVertexData, (int)(rightEye.HiddenAreaMesh.unTriangleCount * 3), out var rightHAMVertices);

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

                    //SetProjectionZoomLevel();

                    RenderLoop.Run(MainForm, () =>
                    {
                        while (vrSystem.PollNextEvent(ref vrEvent, eventSize))
                        {
                            //Debug.WriteLine("VR Event: " + (EVREventType)vrEvent.eventType);
                            switch ((EVREventType)vrEvent.eventType)
                            {
                                case EVREventType.VREvent_IpdChanged:
                                    //modify projection??
                                    break;
                                case EVREventType.VREvent_PropertyChanged:
                                    {
                                        QueryDevices();
                                    }
                                    break;

                                case EVREventType.VREvent_TrackedDeviceUpdated:
                                    {
                                        QueryDevices();

                                    }
                                    break;
                                case EVREventType.VREvent_TrackedDeviceRoleChanged:
                                    {
                                        //controllers.Remove(vrEvent.trackedDeviceIndex);
                                        QueryDevices();
                                    }
                                    break;
                                case EVREventType.VREvent_TrackedDeviceActivated:
                                    QueryDevices();
                                    //if (!controllers.ContainsKey(vrEvent.trackedDeviceIndex))
                                    //{
                                    //    //var pError = ETrackedPropertyError.TrackedProp_Success;
                                    //    //var newrole = (ETrackedControllerRole)vrSystem.GetInt32TrackedDeviceProperty(vrEvent.trackedDeviceIndex, ETrackedDeviceProperty.Prop_ControllerRoleHint_Int32, ref pError);
                                    //    var newrole = vrSystem.GetControllerRoleForTrackedDeviceIndex(vrEvent.trackedDeviceIndex);
                                    //    if (newrole == ETrackedControllerRole.LeftHand || newrole == ETrackedControllerRole.RightHand)
                                    //    {
                                    //        controllers.Add(vrEvent.trackedDeviceIndex, vrSystem.GetControllerRoleForTrackedDeviceIndex(vrEvent.trackedDeviceIndex));
                                    //        controllerIDs = controllers.Keys.ToArray();
                                    //    }
                                    //}
                                    break;

                                case EVREventType.VREvent_TrackedDeviceDeactivated:
                                    controllers.Remove(vrEvent.trackedDeviceIndex);
                                    controllerIDs = controllers.Keys.ToArray();
                                    break;
                                case EVREventType.VREvent_Quit:
                                    //case EVREventType.VREvent_ProcessQuit:
                                    MainForm.Close();
                                    break;
                                case EVREventType.VREvent_ButtonPress:
                                    {
                                        var role = vrSystem.GetControllerRoleForTrackedDeviceIndex(vrEvent.trackedDeviceIndex);
                                        var button = vrEvent.data.controller.button;
                                        var state = default(VRControllerState_t);
                                        vrSystem.GetControllerState(vrEvent.trackedDeviceIndex, ref state, (uint)Utilities.SizeOf<VRControllerState_t>());
                                        ButtonPressed(role, ref state, (EVRButtonId)vrEvent.data.controller.button);
                                    }
                                    break;
                                case EVREventType.VREvent_ButtonUntouch:
                                case EVREventType.VREvent_ButtonUnpress:
                                    {
                                        var role = vrSystem.GetControllerRoleForTrackedDeviceIndex(vrEvent.trackedDeviceIndex);
                                        var button = vrEvent.data.controller.button;
                                        var state = default(VRControllerState_t);
                                        vrSystem.GetControllerState(vrEvent.trackedDeviceIndex, ref state, (uint)Utilities.SizeOf<VRControllerState_t>());
                                        ButtonUnPressed(role, ref state, (EVRButtonId)vrEvent.data.controller.button);
                                    }
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

        private static void QueryDevices()
        {
            controllers.Clear();
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
                            //var pError = ETrackedPropertyError.TrackedProp_Success;
                            //var newrole = (ETrackedControllerRole) vrSystem.GetInt32TrackedDeviceProperty(cdevice, ETrackedDeviceProperty.Prop_ControllerRoleHint_Int32, ref pError);
                            var newrole = vrSystem.GetControllerRoleForTrackedDeviceIndex(cdevice);
                            if (newrole == ETrackedControllerRole.LeftHand || newrole == ETrackedControllerRole.RightHand)
                            {
                                controllers.Add(cdevice, newrole);
                                controllerIDs = controllers.Keys.ToArray();
                            }
                        }
                        break;
                }
            }
        }

        private static void CurrentDomain_FirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            Log("Exception: {0}", e.ToString());
            MessageBox.Show(e.Exception.Message);
            Application.Exit();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log("Exception: {0}", e.ToString());
            MessageBox.Show((e.ExceptionObject as Exception).Message);
            Application.Exit();
        }

        public static void ToggleHiddenMesh()
        {
            RenderHiddenMesh = !RenderHiddenMesh;
        }

        public static void DecreaseAdjustStep()
        {
            AdjustStep /= 10;
            if (AdjustStep < 0.00000001f) AdjustStep = 0.00000001f;
        }

        public static void IncreaseAdjustStep()
        {
            AdjustStep *= 10;
            if (AdjustStep > 1) AdjustStep = 1;
        }

        public static void AdjustEyeCenters(float xStep, float yStep)
        {
            AdjustCenter(RenderFlags.HasFlag(RenderFlag.Left) ? xStep : 0, RenderFlags.HasFlag(RenderFlag.Left) ? yStep : 0, RenderFlags.HasFlag(RenderFlag.Right) ? xStep : 0, RenderFlags.HasFlag(RenderFlag.Right) ? yStep : 0);
            if (RenderFlags.HasFlag(RenderFlag.Left))
            {
                leftEye.DistortionData.RedCenter.X =
                leftEye.DistortionData.GreenCenter.X =
                leftEye.DistortionData.BlueCenter.X = -leftEye.DistortionData.EyeCenter.X;
                leftEye.DistortionData.RedCenter.Y =
                leftEye.DistortionData.GreenCenter.Y =
                leftEye.DistortionData.BlueCenter.Y = -leftEye.DistortionData.EyeCenter.Y;
            }
            if (RenderFlags.HasFlag(RenderFlag.Right))
            {
                rightEye.DistortionData.RedCenter.X =
                rightEye.DistortionData.GreenCenter.X =
                rightEye.DistortionData.BlueCenter.X = -rightEye.DistortionData.EyeCenter.X;
                rightEye.DistortionData.RedCenter.Y =
                rightEye.DistortionData.GreenCenter.Y =
                rightEye.DistortionData.BlueCenter.Y = -rightEye.DistortionData.EyeCenter.Y;
            }
        }

        private static void AdjustCenter(double lx, double ly, double rx, double ry)
        {
            leftEye.DistortionData.EyeCenter.X += (float)lx;
            leftEye.DistortionData.EyeCenter.Y += (float)ly;
            leftEye.UpdateIntrinsicsFromFocusAndCenter();
            rightEye.DistortionData.EyeCenter.X += (float)rx;
            rightEye.DistortionData.EyeCenter.Y += (float)ry;
            rightEye.UpdateIntrinsicsFromFocusAndCenter();
        }

        public static void AdjustColorCenters(float xStep, float yStep)
        {
            if (RenderFlags.HasFlag(RenderFlag.Left))
            {
                if (RenderFlags.HasFlag(RenderFlag.RedActive))
                {
                    leftEye.DistortionData.RedCenter.X += xStep;
                    leftEye.DistortionData.RedCenter.Y += yStep;
                }
                if (RenderFlags.HasFlag(RenderFlag.GreenActive))
                {
                    leftEye.DistortionData.GreenCenter.X += xStep;
                    leftEye.DistortionData.GreenCenter.Y += yStep;
                }
                if (RenderFlags.HasFlag(RenderFlag.BlueActive))
                {
                    leftEye.DistortionData.BlueCenter.X += xStep;
                    leftEye.DistortionData.BlueCenter.Y += yStep;
                }
            }
            if (RenderFlags.HasFlag(RenderFlag.Right))
            {
                if (RenderFlags.HasFlag(RenderFlag.RedActive))
                {
                    rightEye.DistortionData.RedCenter.X += xStep;
                    rightEye.DistortionData.RedCenter.Y += yStep;
                }
                if (RenderFlags.HasFlag(RenderFlag.GreenActive))
                {
                    rightEye.DistortionData.GreenCenter.X += xStep;
                    rightEye.DistortionData.GreenCenter.Y += yStep;
                }
                if (RenderFlags.HasFlag(RenderFlag.BlueActive))
                {
                    rightEye.DistortionData.BlueCenter.X += xStep;
                    rightEye.DistortionData.BlueCenter.Y += yStep;
                }
            }
        }

        public static void ResetEyes()
        {
            if (RenderFlags.HasFlag(RenderFlag.Left)) { leftEye.ResetDistortionCoefficients(); leftEye.CalcFocusCenterAspect(); leftEye.ResetEyeCenters(); }
            if (RenderFlags.HasFlag(RenderFlag.Right)) { rightEye.ResetDistortionCoefficients(); rightEye.CalcFocusCenterAspect(); rightEye.ResetEyeCenters(); }
            AdjustCenter(0, 0, 0, 0);
        }

        public static void AdjustCoefficients(float step)
        {
            if (RenderFlags.HasFlag(RenderFlag.Left))
            {
                if (RenderFlags.HasFlag(RenderFlag.RedActive))
                {
                    if (RenderFlags.HasFlag(RenderFlag.K1)) leftEye.DistortionData.RedCoeffs.X += step;
                    if (RenderFlags.HasFlag(RenderFlag.K2)) leftEye.DistortionData.RedCoeffs.Y += step;
                    if (RenderFlags.HasFlag(RenderFlag.K3)) leftEye.DistortionData.RedCoeffs.Z += step;
                }
                if (RenderFlags.HasFlag(RenderFlag.GreenActive))
                {
                    if (RenderFlags.HasFlag(RenderFlag.K1)) leftEye.DistortionData.GreenCoeffs.X += step;
                    if (RenderFlags.HasFlag(RenderFlag.K2)) leftEye.DistortionData.GreenCoeffs.Y += step;
                    if (RenderFlags.HasFlag(RenderFlag.K3)) leftEye.DistortionData.GreenCoeffs.Z += step;
                }

                if (RenderFlags.HasFlag(RenderFlag.BlueActive))
                {
                    if (RenderFlags.HasFlag(RenderFlag.K1)) leftEye.DistortionData.BlueCoeffs.X += step;
                    if (RenderFlags.HasFlag(RenderFlag.K2)) leftEye.DistortionData.BlueCoeffs.Y += step;
                    if (RenderFlags.HasFlag(RenderFlag.K3)) leftEye.DistortionData.BlueCoeffs.Z += step;
                }
            }
            if (RenderFlags.HasFlag(RenderFlag.Right))
            {
                if (RenderFlags.HasFlag(RenderFlag.RedActive))
                {
                    if (RenderFlags.HasFlag(RenderFlag.K1)) rightEye.DistortionData.RedCoeffs.X += step;
                    if (RenderFlags.HasFlag(RenderFlag.K2)) rightEye.DistortionData.RedCoeffs.Y += step;
                    if (RenderFlags.HasFlag(RenderFlag.K3)) rightEye.DistortionData.RedCoeffs.Z += step;
                }
                if (RenderFlags.HasFlag(RenderFlag.GreenActive))
                {
                    if (RenderFlags.HasFlag(RenderFlag.K1)) rightEye.DistortionData.GreenCoeffs.X += step;
                    if (RenderFlags.HasFlag(RenderFlag.K2)) rightEye.DistortionData.GreenCoeffs.Y += step;
                    if (RenderFlags.HasFlag(RenderFlag.K3)) rightEye.DistortionData.GreenCoeffs.Z += step;
                }

                if (RenderFlags.HasFlag(RenderFlag.BlueActive))
                {
                    if (RenderFlags.HasFlag(RenderFlag.K1)) rightEye.DistortionData.BlueCoeffs.X += step;
                    if (RenderFlags.HasFlag(RenderFlag.K2)) rightEye.DistortionData.BlueCoeffs.Y += step;
                    if (RenderFlags.HasFlag(RenderFlag.K3)) rightEye.DistortionData.BlueCoeffs.Z += step;
                }
            }
        }

        public static void ChangeRenderMode()
        {
            RenderMode = (RenderMode + 1) % 3;
            pixelShaderData.Wireframe = (RenderMode == 1);
        }

        public static void ToggleDistortion()
        {
            pixelShaderData._Undistort++;
            if (pixelShaderData._Undistort >= 3) pixelShaderData._Undistort = 0;
            CrossHairModel.ModifyCircles(d3dDevice, 0);
            //pixelShaderData.Undistort = !pixelShaderData.Undistort;
        }

        public static void AdjustFocus(float stepX, float stepY)
        {
            if (RenderFlags.HasFlag(RenderFlag.Left))
            {
                leftEye.DistortionData.FocalX += stepX;
                leftEye.DistortionData.FocalY += stepY;
                leftEye.UpdateIntrinsicsFromFocusAndCenter();
            }
            if (RenderFlags.HasFlag(RenderFlag.Right))
            {
                rightEye.DistortionData.FocalX += stepX;
                rightEye.DistortionData.FocalY += stepY;
                rightEye.UpdateIntrinsicsFromFocusAndCenter();
            }
        }

        public static void AdjustGrow(float adjustStep)
        {
            if (RenderFlags.HasFlag(RenderFlag.Left))
            {
                leftEye.DistortionData.GrowToUndistort += adjustStep;
                leftEye.UpdateIntrinsicsFromFocusAndCenter();
            }
            if (RenderFlags.HasFlag(RenderFlag.Right))
            {
                rightEye.DistortionData.GrowToUndistort += adjustStep;
                rightEye.UpdateIntrinsicsFromFocusAndCenter();
            }
        }

        public static void ResetToOriginalValues()
        {
            if (RenderFlags.HasFlag(RenderFlag.Left))
            {
                leftEye.DistortionData = leftEye.OriginalDistortionData;
                leftEye.CalcFocusCenterAspect();
            }
            if (RenderFlags.HasFlag(RenderFlag.Right))
            {
                rightEye.DistortionData = rightEye.OriginalDistortionData;
                rightEye.CalcFocusCenterAspect();
            }
        }

        public static void AdjustCutoff(float adjustStep)
        {
            if (RenderFlags.HasFlag(RenderFlag.Left))
            {
                leftEye.DistortionData.UndistortR2Cutoff += adjustStep;
                leftEye.UpdateIntrinsicsFromFocusAndCenter();
            }
            if (RenderFlags.HasFlag(RenderFlag.Right))
            {
                rightEye.DistortionData.UndistortR2Cutoff += adjustStep;
                rightEye.UpdateIntrinsicsFromFocusAndCenter();
            }
        }

        //private static Matrix GetRawMatrix(EVREye eye, float zNear, float zFar)
        //{
        //    float fLeft = 0f;
        //    float fRight = 0f;
        //    float fTop = 0f;
        //    float fBottom = 0f;
        //    vrSystem.GetProjectionRaw(eye, ref fLeft, ref fRight, ref fTop, ref fBottom);
        //    var proj = new Matrix(0);

        //    float idx = 1.0f / (fRight - fLeft);
        //    float idy = 1.0f / (fBottom - fTop);
        //    float idz = 1.0f / (zFar - zNear);
        //    float sx = fRight + fLeft;
        //    float sy = fBottom + fTop;

        //    proj.M11 = 2 * idx; proj.M13 = sx * idx;
        //    proj.M22 = 2 * idy; proj.M23 = sy * idy;
        //    proj.M33 = -zFar * idz; proj.M34 = -zFar * zNear * idz;
        //    proj.M43 = -1.0f;

        //    proj.Transpose();
        //    return proj;
        //}

        private static void ButtonPressed(ETrackedControllerRole role, ref VRControllerState_t state, EVRButtonId button)
        {
            switch (button)
            {
                case EVRButtonId.k_EButton_Grip: //grip
                    {
                        AdjustmentPanelModel.ButtonPressed("G", role);
                        break;
                    }
                case EVRButtonId.k_EButton_ApplicationMenu: //grip
                    {
                        switch (role)
                        {
                            case ETrackedControllerRole.LeftHand:
                                ToggleDistortion();
                                break;
                            case ETrackedControllerRole.RightHand:
                                ChangeRenderMode();
                                break;
                        }
                        break;
                    }
                case EVRButtonId.k_EButton_SteamVR_Trigger:
                    AdjustmentPanelModel.ButtonPressed("T", role);
                    break;
                case EVRButtonId.k_EButton_SteamVR_Touchpad:
                    {
                        if (state.rAxis0.x > -0.3 && state.rAxis0.x < 0.3 && state.rAxis0.y > -0.3 && state.rAxis0.y < 0.3)
                        {
                            //center pressed
                            switch (role)
                            {
                                case ETrackedControllerRole.LeftHand:
                                    ToggleInfoPanel();
                                    break;
                                case ETrackedControllerRole.RightHand:
                                    ToggleHiddenMesh();
                                    break;
                            }
                        }
                        else if (state.rAxis0.x < 0 && Math.Abs(state.rAxis0.y) < Math.Abs(state.rAxis0.x))
                        {
                            //left
                            AdjustmentPanelModel.ButtonPressed("L", role);
                        }
                        else if (state.rAxis0.x > 0 && Math.Abs(state.rAxis0.y) < Math.Abs(state.rAxis0.x))
                        {
                            //right
                            AdjustmentPanelModel.ButtonPressed("R", role);
                        }
                        else if (state.rAxis0.y > 0 && Math.Abs(state.rAxis0.x) < Math.Abs(state.rAxis0.y))
                        {
                            //up
                            AdjustmentPanelModel.ButtonPressed("U", role);
                        }
                        else if (state.rAxis0.y < 0 && Math.Abs(state.rAxis0.x) < Math.Abs(state.rAxis0.y))
                        {
                            //down
                            AdjustmentPanelModel.ButtonPressed("D", role);
                        }
                    }
                    break;
            }

        }

        private static void ButtonUnPressed(ETrackedControllerRole role, ref VRControllerState_t state, EVRButtonId button)
        {
            switch (button)
            {
                case EVRButtonId.k_EButton_Grip: //grip
                    {
                        AdjustmentPanelModel.ButtonUnPressed("G", role);
                        break;
                    }
                case EVRButtonId.k_EButton_SteamVR_Trigger:
                    AdjustmentPanelModel.ButtonUnPressed("T", role);
                    break;
                case EVRButtonId.k_EButton_SteamVR_Touchpad:
                    {
                        if (state.rAxis0.x > -0.3 && state.rAxis0.x < 0.3 && state.rAxis0.y > -0.3 && state.rAxis0.y < 0.3)
                        {
                            //center pressed
                        }
                        else if (state.rAxis0.x < 0 && Math.Abs(state.rAxis0.y) < Math.Abs(state.rAxis0.x))
                        {
                            //left
                            AdjustmentPanelModel.ButtonUnPressed("L", role);
                        }
                        else if (state.rAxis0.x > 0 && Math.Abs(state.rAxis0.y) < Math.Abs(state.rAxis0.x))
                        {
                            //right
                            AdjustmentPanelModel.ButtonUnPressed("R", role);
                        }
                        else if (state.rAxis0.y > 0 && Math.Abs(state.rAxis0.x) < Math.Abs(state.rAxis0.y))
                        {
                            //up
                            AdjustmentPanelModel.ButtonUnPressed("U", role);
                        }
                        else if (state.rAxis0.y < 0 && Math.Abs(state.rAxis0.x) < Math.Abs(state.rAxis0.y))
                        {
                            //down
                            AdjustmentPanelModel.ButtonUnPressed("D", role);
                        }
                    }
                    break;
            }

        }

        private static void ToggleInfoPanel()
        {
            AdjustmentPanelModel.Show = !AdjustmentPanelModel.Show;
        }

        public static bool IsEyeActive(EVREye eye)
        {
            return (RenderFlags.HasFlag(RenderFlag.Left) && eye == EVREye.Eye_Left) ||
                   (RenderFlags.HasFlag(RenderFlag.Right) && eye == EVREye.Eye_Right);
        }

        private static void RenderView(ref EyeData eye)
        {
            var projection = (pixelShaderData.Undistort ? eye.GetProjectionFromIntrinsics() : eye.OriginalProjection);

            d3dDeviceContext.PixelShader.SetSampler(0, samplerState);
            d3dDeviceContext.Rasterizer.SetViewport(0, 0, eye.FrameSize.Width, eye.FrameSize.Height);
            d3dDeviceContext.OutputMerger.SetTargets(eye.DepthStencilView, eye.TextureView);
            d3dDeviceContext.ClearRenderTargetView(eye.TextureView, d3dClearColor);
            d3dDeviceContext.ClearDepthStencilView(eye.DepthStencilView, DepthStencilClearFlags.Depth, 1, 0);
            d3dDeviceContext.OutputMerger.SetDepthStencilState(DepthStencilState);
            d3dDeviceContext.OutputMerger.SetBlendState(blendState);
            d3dDeviceContext.Rasterizer.State = pixelShaderData.Wireframe ? WireFrameRasterizerState : SolidRasteizerState;

            var render = true;
            var texView = eye.TextureView;
            var shaderView = eye.ShaderView;
            
            if (eye.Eye == EVREye.Eye_Left)
            {
                //if (!RenderFlags.HasFlag(RenderFlag.Left)) render = false;
                d3dDeviceContext.UpdateSubresource(ref leftEye.DistortionData, coefficientConstantBuffer);
                vertexShaderData.ActiveEyeCenter = leftEye.DistortionData.EyeCenter;
                vertexShaderData.ActiveAspect = leftEye.DistortionData.Aspect;
            }
            else if (eye.Eye == EVREye.Eye_Right)
            {
                //if (!RenderFlags.HasFlag(RenderFlag.Right)) render = false;
                d3dDeviceContext.UpdateSubresource(ref rightEye.DistortionData, coefficientConstantBuffer);
                vertexShaderData.ActiveEyeCenter = rightEye.DistortionData.EyeCenter;
                vertexShaderData.ActiveAspect = rightEye.DistortionData.Aspect;
            }

            if (render)
            {
                environmentShader.Apply(d3dDeviceContext);
                pixelShaderData.LightPosition = headMatrix.TranslationVector;


                vertexShaderData.WorldViewProj = Matrix.Invert(eye.EyeToHeadView * headMatrix) * projection;
                vertexShaderData.WorldViewProj.Transpose();
                d3dDeviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);

                //pixelShaderData.Intrinsics = Matrix.Invert(eye.CreateIntrinsics()); //pixelShaderData.Intrinsics.Transpose();            

                if (RenderMode != 2)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (i == 0 && !RenderFlags.HasFlag(RenderFlag.RenderRed)) continue;
                        if (i == 1 && !RenderFlags.HasFlag(RenderFlag.RenderGreen)) continue;
                        if (i == 2 && !RenderFlags.HasFlag(RenderFlag.RenderBlue)) continue;
                        pixelShaderData.ActiveColor = i;
                        pixelShaderData.Controller = false;
                        d3dDeviceContext.UpdateSubresource(ref pixelShaderData, pixelConstantBuffer);
                        environmentModel.Render(d3dDeviceContext);
                    }
                }

                if (pixelShaderData.Wireframe) //revert            
                    d3dDeviceContext.Rasterizer.State = SolidRasteizerState;

                pixelShaderData.ActiveColor = -1;
                pixelShaderData.Controller = true;
                d3dDeviceContext.UpdateSubresource(ref pixelShaderData, pixelConstantBuffer);
                d3dDeviceContext.OutputMerger.SetDepthStencilState(ControllerDepthStencilState);
                d3dDeviceContext.OutputMerger.SetBlendState(null);

                //render points on empty wall
                CrossHairModel.RenderPoints(d3dDeviceContext);

                if (pixelShaderData.Undistort || RenderMode != 0)
                {
                    vertexShaderData.WorldViewProj = Matrix.Invert(eye.EyeToHeadView) * projection;
                    vertexShaderData.WorldViewProj.Transpose();
                    d3dDeviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);
                    CrossHairModel.Render(d3dDeviceContext);

                }

                //Render info panels
                Matrix controllerMat = default(Matrix);
                var hasLeft = false;
                var hasRight = false;
                foreach (var controllerId in controllerIDs)
                {
                    if (controllers[controllerId] == ETrackedControllerRole.Invalid)
                        controllers[controllerId] = vrSystem.GetControllerRoleForTrackedDeviceIndex(controllerId);

                    var controllerRole = controllers[controllerId];

                    if (currentPoses[controllerId].bPoseIsValid)
                    {
                        Convert(ref currentPoses[controllerId].mDeviceToAbsoluteTracking, ref controllerMat);

                        vertexShaderData.WorldViewProj = controllerMat * Matrix.Invert(eye.EyeToHeadView * headMatrix) * projection;
                        if (pixelShaderData.Undistort)
                        {
                            if (AdjustmentPanelModel.Show && controllerRole == ETrackedControllerRole.LeftHand)
                            {
                                AdjustmentPanelModel.WVP = vertexShaderData.WorldViewProj;
                                hasLeft = true;
                            }
                            if (controllerRole == ETrackedControllerRole.RightHand)
                            {
                                PointerModel.WVP = vertexShaderData.WorldViewProj;
                                hasRight = true;
                            }
                        }
                        vertexShaderData.WorldViewProj.Transpose();
                        d3dDeviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);

                        environmentShader.Apply(d3dDeviceContext); //back 
                        controllerModel.Render(d3dDeviceContext);
                    }
                }

                if (hasRight)
                {
                    vertexShaderData.WorldViewProj = PointerModel.WVP;
                    vertexShaderData.WorldViewProj.Transpose();
                    d3dDeviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);
                    PointerModel.Render(d3dDeviceContext);
                }
                if (hasLeft)
                {
                    vertexShaderData.WorldViewProj = AdjustmentPanelModel.WVP;
                    vertexShaderData.WorldViewProj.Transpose();
                    d3dDeviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);
                    AdjustmentPanelModel.Render(d3dDeviceContext);
                }


                if (RenderHiddenMesh /*&& IsEyeActive(eye.Eye)*/)
                {
                    d3dDeviceContext.Rasterizer.State = pixelShaderData.Wireframe ? ncWireFrameRasterizerState : ncRasterizerState;
                    //render hidden mesh area just for control distortion area
                    //vertexShaderData.WorldViewProj = Matrix.Invert(eye.EyeToHeadView * headMatrix) * projection;
                    //vertexShaderData.WorldViewProj.Transpose();
                    //d3dDeviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);


                    d3dDeviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                    hmaVertexBufferBinding = new VertexBufferBinding(eye.HiddenAreaMeshVertexBuffer, sizeof(float) * 2, 0);
                    d3dDeviceContext.InputAssembler.SetVertexBuffers(0, hmaVertexBufferBinding);
                    hmaShader.Apply(d3dDeviceContext);
                    d3dDeviceContext.Draw((int)(3 * eye.HiddenAreaMesh.unTriangleCount), 0);
                }

                if (pixelShaderData.Wireframe) //revert if in wireframe
                    d3dDeviceContext.Rasterizer.State = SolidRasteizerState;

                if (pixelShaderData.Undistort)
                {
                    //render and undistort         
                    texView = UndistortTextureView;
                    shaderView = UndistortShaderView;
                    d3dDeviceContext.UpdateSubresource(ref vertexShaderData, vertexConstantBuffer);
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
            }
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

            var submitError = vrCompositor.Submit((EVREye)eye.Eye, ref texture, ref bounds, pixelShaderData.Undistort ? EVRSubmitFlags.Submit_LensDistortionAlreadyApplied : EVRSubmitFlags.Submit_Default);

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

        public static void SaveSettings()
        {
            if (MessageBox.Show(null, "Only upload configuration if you know what you are doing.\n(Did you backup your original config?)\nA backup of the input file will be created anyway.", "Confirm configuration upload.", MessageBoxButtons.OKCancel) == DialogResult.OK)
            {
                if (MessageBox.Show(null, "Are you sure?", "Confirm configuration upload.", MessageBoxButtons.YesNo) == DialogResult.No)
                    return;

                //backup input file first
                File.Copy("LH_Config_In.json", "LH_Config_In." + DateTime.Now.Ticks + ".backup.json");

                SaveLHSettings(OvrPath);

                var toolPath = OvrPath + @"tools\lighthouse\bin\win32\lighthouse_console.exe";
                //var confPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)) + "\\LH_Config_Out.json";
                var processInfo = new ProcessStartInfo
                {
                    Arguments = "uploadconfig LH_Config_Out.json",
                    //CreateNoWindow = true,
                    FileName = toolPath,
                    //WindowStyle = ProcessWindowStyle.Hidden

                };
                //MessageBox.Show("Running process " + toolPath + " " + processInfo.Arguments);
                var process = Process.Start(processInfo);
                process.WaitForExit();

                MessageBox.Show("Now you have to restart SteamVR.");
                MainForm.Close();
            }
        }

        private static void SaveLHSettings(string ovrPath)
        {
            var confPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)) + "\\LH_Config_Out.json";

            SaveEyeSettings(leftEye);
            SaveEyeSettings(rightEye);

            var jsonData = FastJson.Serialize(lightHouseConfigJson);
            File.WriteAllText(confPath, jsonData.Prettify());
        }

        private static void SaveEyeSettings(EyeData eye)
        {
            eye.Json["grow_for_undistort"] = (double)eye.DistortionData.GrowToUndistort;
            eye.Json["undistort_r2_cutoff"] = (double)eye.DistortionData.UndistortR2Cutoff;

            (eye.Json["distortion"] as Dictionary<string, object>)["center_x"] = (double)eye.DistortionData.GreenCenter.X;
            (eye.Json["distortion"] as Dictionary<string, object>)["center_y"] = (double)eye.DistortionData.GreenCenter.Y;
            (eye.Json["distortion_blue"] as Dictionary<string, object>)["center_x"] = (double)eye.DistortionData.BlueCenter.X;
            (eye.Json["distortion_blue"] as Dictionary<string, object>)["center_y"] = (double)eye.DistortionData.BlueCenter.Y;
            (eye.Json["distortion_red"] as Dictionary<string, object>)["center_x"] = (double)eye.DistortionData.RedCenter.X;
            (eye.Json["distortion_red"] as Dictionary<string, object>)["center_y"] = (double)eye.DistortionData.RedCenter.Y;

            var g = ((eye.Json["distortion"] as Dictionary<string, object>)["coeffs"] as object[]);
            g[0] = (double)eye.DistortionData.GreenCoeffs.X; g[1] = (double)eye.DistortionData.GreenCoeffs.Y; g[2] = (double)eye.DistortionData.GreenCoeffs.Z;
            var b = ((eye.Json["distortion_blue"] as Dictionary<string, object>)["coeffs"] as object[]);
            b[0] = (double)eye.DistortionData.BlueCoeffs.X; b[1] = (double)eye.DistortionData.BlueCoeffs.Y; b[2] = (double)eye.DistortionData.BlueCoeffs.Z;
            var r = ((eye.Json["distortion_red"] as Dictionary<string, object>)["coeffs"] as object[]);
            r[0] = (double)eye.DistortionData.RedCoeffs.X; r[1] = (double)eye.DistortionData.RedCoeffs.Y; r[2] = (double)eye.DistortionData.RedCoeffs.Z;

            var row = eye.Json["intrinsics"] as object[];
            var col = row[0] as object[];
            col[0] = (double)eye.DistortionData.Intrinsics.M11;
            col[1] = (double)eye.DistortionData.Intrinsics.M21;
            col[2] = (double)eye.DistortionData.Intrinsics.M31;
            col = row[1] as object[];
            col[0] = (double)eye.DistortionData.Intrinsics.M12;
            col[1] = (double)eye.DistortionData.Intrinsics.M22;
            col[2] = (double)eye.DistortionData.Intrinsics.M32;
            col = row[2] as object[];
            col[0] = (double)eye.DistortionData.Intrinsics.M13;
            col[1] = (double)eye.DistortionData.Intrinsics.M23;
            col[2] = (double)eye.DistortionData.Intrinsics.M33;

            row = eye.Json["extrinsics"] as object[];
            col = row[0] as object[];
            col[0] = (double)eye.DistortionData.Extrinsics.M11;
            col[1] = (double)eye.DistortionData.Extrinsics.M21;
            col[2] = (double)eye.DistortionData.Extrinsics.M31;
            col[3] = (double)eye.DistortionData.Extrinsics.M41;
            col = row[1] as object[];
            col[0] = (double)eye.DistortionData.Extrinsics.M12;
            col[1] = (double)eye.DistortionData.Extrinsics.M22;
            col[2] = (double)eye.DistortionData.Extrinsics.M32;
            col[3] = (double)eye.DistortionData.Extrinsics.M42;
            col = row[2] as object[];
            col[0] = (double)eye.DistortionData.Extrinsics.M13;
            col[1] = (double)eye.DistortionData.Extrinsics.M23;
            col[2] = (double)eye.DistortionData.Extrinsics.M33;
            col[3] = (double)eye.DistortionData.Extrinsics.M43;
        }

        private static void LoadLHSettings(string ovrPath)
        {
            var toolPath = ovrPath + @"tools\lighthouse\bin\win32\lighthouse_console.exe";
            var confPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)) + "\\LH_Config_In.json";
            var processInfo = new ProcessStartInfo
            {
                Arguments = "downloadconfig LH_Config_In.json",
                CreateNoWindow = true,
                FileName = toolPath,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            //MessageBox.Show("Running process " + toolPath + " " + processInfo.Arguments);
            var process = Process.Start(processInfo);
            process.WaitForExit();

            var jsonData = File.ReadAllText(confPath);
            File.WriteAllText(confPath, jsonData.Prettify());
            lightHouseConfigJson = FastJson.Deserialize<IDictionary<string, object>>(jsonData);

            var deviceData = lightHouseConfigJson["device"] as IDictionary<string, object>;
            ScreenWidth = (uint)System.Convert.ToDouble(deviceData["eye_target_width_in_pixels"]);
            ScreenHeight = (uint)System.Convert.ToDouble(deviceData["eye_target_height_in_pixels"]);
            ScreenAspect = (float)System.Convert.ToDouble(deviceData["physical_aspect_x_over_y"]);
            pixelShaderData.Persistence = (float)System.Convert.ToDouble(deviceData["persistence"]);

            leftEye = new EyeData(EVREye.Eye_Left);
            rightEye = new EyeData(EVREye.Eye_Right);

            leftEye.PanelSize.Width = rightEye.PanelSize.Width = ScreenWidth;
            leftEye.PanelSize.Height = rightEye.PanelSize.Height = ScreenHeight;

            var transforms = lightHouseConfigJson["tracking_to_eye_transform"] as object[];
            leftEye.Json = (transforms[0]) as IDictionary<string, object>;
            rightEye.Json = (transforms[1]) as IDictionary<string, object>;


            LoadEyeSettings(leftEye);
            LoadEyeSettings(rightEye);


        }

        private static void LoadEyeSettings(EyeData eye)
        {
            eye.DistortionData.GrowToUndistort = (float)System.Convert.ToDouble(eye.Json["grow_for_undistort"]);
            eye.DistortionData.UndistortR2Cutoff = (float)System.Convert.ToDouble(eye.Json["undistort_r2_cutoff"]);

            eye.DistortionData.GreenCenter.X = (float)System.Convert.ToDouble((eye.Json["distortion"] as Dictionary<string, object>)["center_x"]);
            eye.DistortionData.GreenCenter.Y = (float)System.Convert.ToDouble((eye.Json["distortion"] as Dictionary<string, object>)["center_y"]);
            eye.DistortionData.BlueCenter.X = (float)System.Convert.ToDouble((eye.Json["distortion_blue"] as Dictionary<string, object>)["center_x"]);
            eye.DistortionData.BlueCenter.Y = (float)System.Convert.ToDouble((eye.Json["distortion_blue"] as Dictionary<string, object>)["center_y"]);
            eye.DistortionData.RedCenter.X = (float)System.Convert.ToDouble((eye.Json["distortion_red"] as Dictionary<string, object>)["center_x"]);
            eye.DistortionData.RedCenter.Y = (float)System.Convert.ToDouble((eye.Json["distortion_red"] as Dictionary<string, object>)["center_y"]);

            var g = ((eye.Json["distortion"] as Dictionary<string, object>)["coeffs"] as object[]).Select(a => (float)System.Convert.ToDouble(a)).ToArray();
            eye.DistortionData.GreenCoeffs.X = g[0]; eye.DistortionData.GreenCoeffs.Y = g[1]; eye.DistortionData.GreenCoeffs.Z = g[2];
            var b = ((eye.Json["distortion_blue"] as Dictionary<string, object>)["coeffs"] as object[]).Select(a => (float)System.Convert.ToDouble(a)).ToArray();
            eye.DistortionData.BlueCoeffs.X = b[0]; eye.DistortionData.BlueCoeffs.Y = b[1]; eye.DistortionData.BlueCoeffs.Z = b[2];
            var r = ((eye.Json["distortion_red"] as Dictionary<string, object>)["coeffs"] as object[]).Select(a => (float)System.Convert.ToDouble(a)).ToArray();
            eye.DistortionData.RedCoeffs.X = r[0]; eye.DistortionData.RedCoeffs.Y = r[1]; eye.DistortionData.RedCoeffs.Z = r[2];

            var row = eye.Json["intrinsics"] as object[];
            var col = row[0] as object[];
            eye.DistortionData.Intrinsics = Matrix3x3.Zero;
            eye.DistortionData.Intrinsics.M11 = (float)System.Convert.ToDouble(col[0]);
            eye.DistortionData.Intrinsics.M21 = (float)System.Convert.ToDouble(col[1]);
            eye.DistortionData.Intrinsics.M31 = (float)System.Convert.ToDouble(col[2]);
            col = row[1] as object[];
            eye.DistortionData.Intrinsics.M12 = (float)System.Convert.ToDouble(col[0]);
            eye.DistortionData.Intrinsics.M22 = (float)System.Convert.ToDouble(col[1]);
            eye.DistortionData.Intrinsics.M32 = (float)System.Convert.ToDouble(col[2]);
            col = row[2] as object[];
            eye.DistortionData.Intrinsics.M13 = (float)System.Convert.ToDouble(col[0]);
            eye.DistortionData.Intrinsics.M23 = (float)System.Convert.ToDouble(col[1]);
            eye.DistortionData.Intrinsics.M33 = (float)System.Convert.ToDouble(col[2]);

            row = eye.Json["extrinsics"] as object[];
            col = row[0] as object[];
            eye.DistortionData.Extrinsics = new Matrix(0);
            eye.DistortionData.Extrinsics.M11 = (float)System.Convert.ToDouble(col[0]);
            eye.DistortionData.Extrinsics.M21 = (float)System.Convert.ToDouble(col[1]);
            eye.DistortionData.Extrinsics.M31 = (float)System.Convert.ToDouble(col[2]);
            eye.DistortionData.Extrinsics.M41 = (float)System.Convert.ToDouble(col[3]);
            col = row[1] as object[];
            eye.DistortionData.Extrinsics.M12 = (float)System.Convert.ToDouble(col[0]);
            eye.DistortionData.Extrinsics.M22 = (float)System.Convert.ToDouble(col[1]);
            eye.DistortionData.Extrinsics.M32 = (float)System.Convert.ToDouble(col[2]);
            eye.DistortionData.Extrinsics.M42 = (float)System.Convert.ToDouble(col[3]);
            col = row[2] as object[];
            eye.DistortionData.Extrinsics.M13 = (float)System.Convert.ToDouble(col[0]);
            eye.DistortionData.Extrinsics.M23 = (float)System.Convert.ToDouble(col[1]);
            eye.DistortionData.Extrinsics.M33 = (float)System.Convert.ToDouble(col[2]);
            eye.DistortionData.Extrinsics.M43 = (float)System.Convert.ToDouble(col[3]);
            eye.DistortionData.Extrinsics.M44 = 1;



            eye.CalcFocusCenterAspect();

            eye.OriginalDistortionData = eye.DistortionData;
        }
    }
}
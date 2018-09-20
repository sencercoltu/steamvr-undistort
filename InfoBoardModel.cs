using SharpDX.Direct2D1;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DirectWrite;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Valve.VR;
using static Undistort.Program;

namespace Undistort
{
    public static class InfoBoardModel
    {
        private static float[] vertices;
        private static SharpDX.Direct3D11.Buffer vertexBuffer;
        private static SharpDX.Direct3D11.Buffer indexBuffer;
        private static VertexBufferBinding vertexBufferBinding;
        private static Shader shader;

        private static Texture2D texture;
        private static ShaderResourceView textureView;

        private static RenderTarget textRenderTarget;
        private static TextFormat textFormat;
        private static TextFormat headerTextFormat;
        private static SolidColorBrush blackBrush;
        private static SolidColorBrush inactiveItemBrush;
        private static SolidColorBrush selectedItemBrush;

        private static EyeData eye;
        public static bool Show = true;

        private static SharpDX.Color4 activeColor = new SharpDX.Color4(0.666f, 1, 0.784f, 1);
        private static SharpDX.Color4 inactiveColor = new SharpDX.Color4(1, 0.666f, 0.784f, 1);


        private class MenuCell
        {
            public MenuRow Row;
            public RawRectangleF Area;                      
            public bool Selectable;
            public string DataName;

            public void Draw(string text, RenderTarget textRenderTarget, TextFormat headerTextFormat, SolidColorBrush blackBrush)
            {
                textRenderTarget.FillRectangle(Area, Row.BackBrush);
                if (!string.IsNullOrEmpty(text))                                    
                    textRenderTarget.DrawText(text, headerTextFormat, Area, blackBrush);
                

            }
        };

        private class MenuRow
        {
            public bool IsEnabled;
            public RawColor4 BackColor;
            public Brush BackBrush;
            public List<MenuCell> Columns = new List<MenuCell>();
        };

        private static List<MenuRow> Rows = new List<MenuRow>
        {
            new MenuRow //left right
            {
                IsEnabled = true,
                BackColor = new RawColor4(1, 1, 0, 1)
            },
            new MenuRow //centerx
            {
                IsEnabled = true,
                BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                Columns =
                {
                    new MenuCell
                    {
                        Area = new RawRectangleF(116, 32, 295+116, 28+32),
                        Selectable = false,
                        DataName = "LCX"
                    },
                    new MenuCell
                    {
                        Area = new RawRectangleF(529, 32, 295+529, 28+32),
                        Selectable = false,
                        DataName = "RCX"
                    }
                }
            },
            new MenuRow //centery
            {
                IsEnabled = true,
                BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                Columns =
                {
                    new MenuCell
                    {
                        Area = new RawRectangleF(116, 61, 295+116, 28+61),
                        Selectable = false,
                        DataName = "LCY"
                    },
                    new MenuCell
                    {
                        Area = new RawRectangleF(529, 61, 295+529, 28+61),
                        Selectable = false,
                        DataName = "RCY"
                    }
                }
            },
            new MenuRow //focalx
            {
                IsEnabled = true,
                BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                Columns =
                {
                    new MenuCell
                    {
                        Area = new RawRectangleF(116, 90, 295+116, 28+90),
                        Selectable = false,
                        DataName = "LFX"
                    },
                    new MenuCell
                    {
                        Area = new RawRectangleF(529, 90, 295+529, 28+90),
                        Selectable = false,
                        DataName = "RFX"
                    }
                }
            },
            new MenuRow //focaly
            {
                IsEnabled = true,
                BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                Columns =
                {
                    new MenuCell
                    {
                        Area = new RawRectangleF(116, 119, 295+116, 28+119),
                        Selectable = false,
                        DataName = "LFY"
                    },
                    new MenuCell
                    {
                        Area = new RawRectangleF(529, 119, 295+529, 28+119),
                        Selectable = false,
                        DataName = "RFY"
                    }
                }
            },
            new MenuRow //grow
            {
                IsEnabled = true,
                BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                Columns =
                {
                    new MenuCell
                    {
                        Area = new RawRectangleF(116, 148, 295+116, 28+148),
                        Selectable = false,
                        DataName = "LGR"
                    },
                    new MenuCell
                    {
                        Area = new RawRectangleF(529, 148, 295+529, 28+148),
                        Selectable = false,
                        DataName = "RGR"
                    }
                }
            },
            new MenuRow //cutoff
            {
                IsEnabled = true,
                BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                Columns =
                {
                    new MenuCell
                    {
                        Area = new RawRectangleF(116, 177, 295+116, 28+177),
                        Selectable = false,
                        DataName = "LCU"
                    },
                    new MenuCell
                    {
                        Area = new RawRectangleF(529, 177, 295+529, 28+177),
                        Selectable = false,
                        DataName = "RCU"
                    }
                }
            }


        };

        public static bool NeedsTableRedraw = true;

        private static Bitmap Table;

        private static Dictionary<string, MenuCell> CellMap = new Dictionary<string, MenuCell>();

        public static void Init(SharpDX.Direct3D11.Device device, EyeData attachedSide)
        {
            eye = attachedSide;

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

            indexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.IndexBuffer, indices);
            vertexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, vertices);
            vertexBufferBinding = new VertexBufferBinding(vertexBuffer, sizeof(float) * 5, 0);

            Texture2DDescription textureDesc = new Texture2DDescription
            {
                ArraySize = 1,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.Write,
                Format = Format.B8G8R8A8_UNorm,
                Width = Properties.Resources.InfoTable.Width,
                Height = Properties.Resources.InfoTable.Height,
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
            textFormat.ParagraphAlignment = ParagraphAlignment.Center;
            textFormat.TextAlignment = TextAlignment.Trailing;
            headerTextFormat = new TextFormat(directWriteFactory, "Courier New", FontWeight.Bold, FontStyle.Normal, 20.0f);
            headerTextFormat.ParagraphAlignment = ParagraphAlignment.Center;
            headerTextFormat.TextAlignment = TextAlignment.Leading;
            blackBrush = new SolidColorBrush(textRenderTarget, SharpDX.Color4.Black);
            inactiveItemBrush = new SolidColorBrush(textRenderTarget, new SharpDX.Color4(0.5f, 0.5f, 0.5f, 1.0f));
            selectedItemBrush = new SolidColorBrush(textRenderTarget, new SharpDX.Color4(0.0f, 1.0f, 0.0f, 1.0f));

            foreach (var row in Rows)
            {
                row.BackBrush = new SolidColorBrush(textRenderTarget, row.BackColor);
                foreach (var col in row.Columns)
                {
                    col.Row = row;
                    CellMap[col.DataName] = col;
                }
            }

            var bitmap = Properties.Resources.InfoTable;

            var sourceArea = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapProperties = new BitmapProperties(new PixelFormat(Format.R8G8B8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Premultiplied));
            var size = new SharpDX.Size2(bitmap.Width, bitmap.Height);

            // Transform pixels from BGRA to RGBA
            int stride = bitmap.Width * sizeof(int);
            using (var tempStream = new SharpDX.DataStream(bitmap.Height * stride, true, true))
            {
                // Lock System.Drawing.Bitmap
                var bitmapData = bitmap.LockBits(sourceArea, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                // Convert all pixels 
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int offset = bitmapData.Stride * y;
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        // Not optimized 
                        byte B = Marshal.ReadByte(bitmapData.Scan0, offset++);
                        byte G = Marshal.ReadByte(bitmapData.Scan0, offset++);
                        byte R = Marshal.ReadByte(bitmapData.Scan0, offset++);
                        byte A = Marshal.ReadByte(bitmapData.Scan0, offset++);
                        int rgba = R | (G << 8) | (B << 16) | (A << 24);
                        tempStream.Write(rgba);
                    }

                }
                bitmap.UnlockBits(bitmapData);
                tempStream.Position = 0;

                Table = new Bitmap(textRenderTarget, size, tempStream, stride, bitmapProperties);
            }

        }

        public static void Render(SharpDX.Direct3D11.DeviceContext context)
        {
            textRenderTarget.BeginDraw();
            if (NeedsTableRedraw)
            {
                NeedsTableRedraw = false;
                textRenderTarget.DrawBitmap(Table, 1.0f, BitmapInterpolationMode.NearestNeighbor);
                //textRenderTarget.Clear(SharpDX.Color4.Black);
                //textRenderTarget.DrawRectangle(new RawRectangleF(0, 0, WindowSize.Width - 1, WindowSize.Height - 1), blackBrush, 3.0f);
            }

            MenuCell cell;
            CellMap.TryGetValue("LCX", out cell); if (cell != null) cell.Draw(leftEye.DistortionData.EyeCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);
            CellMap.TryGetValue("LCY", out cell); if (cell != null) cell.Draw(leftEye.DistortionData.EyeCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);
            CellMap.TryGetValue("LFX", out cell); if (cell != null) cell.Draw(leftEye.DistortionData.FocalX.ToString(" 0.000;-0.000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);
            CellMap.TryGetValue("LFY", out cell); if (cell != null) cell.Draw(leftEye.DistortionData.FocalY.ToString(" 0.000;-0.000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);
            CellMap.TryGetValue("LGR", out cell); if (cell != null) cell.Draw(leftEye.DistortionData.GrowToUndistort.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);
            CellMap.TryGetValue("LCU", out cell); if (cell != null) cell.Draw(leftEye.DistortionData.UndistortR2Cutoff.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);


            CellMap.TryGetValue("RCX", out cell); if (cell != null) cell.Draw(rightEye.DistortionData.EyeCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);
            CellMap.TryGetValue("RCY", out cell); if (cell != null) cell.Draw(rightEye.DistortionData.EyeCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);
            CellMap.TryGetValue("RFX", out cell); if (cell != null) cell.Draw(rightEye.DistortionData.FocalX.ToString(" 0.000;-0.000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);
            CellMap.TryGetValue("RFY", out cell); if (cell != null) cell.Draw(rightEye.DistortionData.FocalY.ToString(" 0.000;-0.000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);
            CellMap.TryGetValue("RGR", out cell); if (cell != null) cell.Draw(rightEye.DistortionData.GrowToUndistort.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);
            CellMap.TryGetValue("RCU", out cell); if (cell != null) cell.Draw(rightEye.DistortionData.UndistortR2Cutoff.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture), textRenderTarget, textFormat, blackBrush);



            textRenderTarget.EndDraw(out long tag1, out long tag2);
            //var topPos = 0f;
            //var str = eye.EyeName + " EYE INFO" + (IsEyeActive(eye.Eye) ? (RenderHiddenMesh && Program.pixelShaderData.Undistort) ? " - ACTIVE - HMA" : " - ACTIVE" : "") + "\n";
            //str += "CENTERS\n";
            //textRenderTarget.DrawText(str, headerTextFormat, new SharpDX.Mathematics.Interop.RawRectangleF(0, topPos, WindowSize.Width, WindowSize.Height - topPos), textBrush);
            //topPos += headerTextFormat.FontSize * 2;
            //str = "CH: " + eye.DistortionData.EyeCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            //str += eye.DistortionData.EyeCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            //str += "Gc: " + eye.DistortionData.GreenCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            //str += eye.DistortionData.GreenCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            //str += "Bc: " + eye.DistortionData.BlueCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            //str += eye.DistortionData.BlueCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            //str += "Rc: " + eye.DistortionData.RedCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            //str += eye.DistortionData.RedCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";            
            //textRenderTarget.DrawText(str, textFormat, new SharpDX.Mathematics.Interop.RawRectangleF(0, topPos, WindowSize.Width, WindowSize.Height - topPos), textBrush);
            //topPos += textFormat.FontSize * 5;

            //str = "COEFFICIENTS\n";
            //textRenderTarget.DrawText(str, headerTextFormat, new SharpDX.Mathematics.Interop.RawRectangleF(0, topPos, WindowSize.Width, WindowSize.Height - topPos), textBrush);
            //topPos += headerTextFormat.FontSize;
            //str = "Gk: " + eye.DistortionData.GreenCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            //str += eye.DistortionData.GreenCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            //str += eye.DistortionData.GreenCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            //str += "Bk: " + eye.DistortionData.BlueCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            //str += eye.DistortionData.BlueCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            //str += eye.DistortionData.BlueCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            //str += "Rk: " + eye.DistortionData.RedCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            //str += eye.DistortionData.RedCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + " / ";
            //str += eye.DistortionData.RedCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture) + "\n";
            //textRenderTarget.DrawText(str, textFormat, new SharpDX.Mathematics.Interop.RawRectangleF(0, topPos, WindowSize.Width, WindowSize.Height - topPos), textBrush);
            //topPos += textFormat.FontSize * 4;



            shader.Apply(context);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
            context.InputAssembler.SetIndexBuffer(indexBuffer, Format.R32_UInt, 0);
            context.PixelShader.SetShaderResource(0, textureView);
            context.DrawIndexed(6, 0, 0);
        }

        public static void ButtonPressed(string v, ETrackedControllerRole role)
        {

        }
    }
}

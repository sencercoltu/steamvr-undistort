using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DirectWrite;
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
    public static class AdjustmentPanelModel
    {
        private static float[] panelVertices;
        private static SharpDX.Direct3D11.Buffer panelVertexBuffer;
        private static SharpDX.Direct3D11.Buffer panelIndexBuffer;
        private static VertexBufferBinding panelVertexBufferBinding;

        private static SharpDX.Direct3D11.Buffer pointerVertexBuffer;
        private static VertexBufferBinding pointerVertexBufferBinding;

        private static Shader panelShader;

        private static Texture2D texture;
        private static ShaderResourceView textureView;

        private static RenderTarget textRenderTarget;
        private static TextFormat textFormat;
        //private static TextFormat headerTextFormat;
        private static SolidColorBrush blackBrush;
        private static SolidColorBrush selectedItemBrush;

        //private static EyeData eye;
        public static bool Show = true;

        private static SharpDX.Color4 activeColor = new SharpDX.Color4(0f, 1, 0f, 1);
        private static SharpDX.Color4 inactiveColor = new SharpDX.Color4(1, 0.666f, 0.784f, 1);

        private static System.Timers.Timer ButtonTimer;

        public class IconArea
        {
            public string Type = "L";
            public PointF Location;
        }

        private class ActionGroup
        {
            public Action OnToggle;
            public Func<bool> IsActive;
            public Func<bool> IsColorVisible;
            public Action<string> OnButtonPressed;
            //public Action<string> OnButtonUnPressed;
            public void Draw(RenderTarget textRenderTarget)
            {
                foreach (var point in Icons)
                {
                    var area = new RawRectangleF(point.Location.X, point.Location.Y, point.Location.X + 28, point.Location.Y + 28);
                    if (IsActive() && point.Type == "L")
                        textRenderTarget.DrawBitmap(LockedIcon, area, 1.0f, BitmapInterpolationMode.Linear);
                    else if (point.Type == "V" && IsColorVisible != null)
                    {
                        textRenderTarget.DrawBitmap(IsColorVisible() ? ShownIcon : HiddenIcon, area, 1.0f, BitmapInterpolationMode.Linear);
                    }
                }
            }
            public List<IconArea> Icons = new List<IconArea>();
        }

        private static Dictionary<string, ActionGroup> Actions = new Dictionary<string, ActionGroup>();

        private static MenuCell SelectedCell;

        private class MenuCell
        {
            public readonly int RowIndex;
            public MenuRow Row;
            public RawRectangleF TextArea;
            public bool Checkable = true;
            public Func<string> GetText;
            public MenuCell(int rowIndex, float left)
            {
                RowIndex = rowIndex;
                TextArea.Left = left;
                TextArea.Right = TextArea.Left + 160;
                TextArea.Top = 5 + (1 + 28) * (rowIndex + 1);
                TextArea.Bottom = TextArea.Top + 28;
                TextArea.Left += 5;
                TextArea.Right -= 5;
            }
            public void Draw(RenderTarget textRenderTarget, TextFormat textFormat, SolidColorBrush brush)
            {
                var text = GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    textRenderTarget.FillRectangle(TextArea, (Row.ActionGroup == SelectedCell.Row.ActionGroup) ? selectedItemBrush : Row.BackBrush);
                    textRenderTarget.DrawText(text, textFormat, TextArea, brush);
                }
            }
        };

        private class MenuRow
        {
            public readonly int RowIndex;
            public RawRectangleF SelectionRect;
            public string ActionGroup;
            public bool IsFocused = false;
            public RawColor4 BackColor;
            public SharpDX.Direct2D1.Brush BackBrush;
            public List<MenuCell> Columns = new List<MenuCell>();
            public MenuRow(int rowIndex, string group, float left)
            {
                RowIndex = rowIndex;
                ActionGroup = group;
                SelectionRect.Left = left;
                SelectionRect.Right = SelectionRect.Left + 320 + 6;
                SelectionRect.Top = 5 + (1 + 28) * (rowIndex + 1);
                SelectionRect.Bottom = SelectionRect.Top + 28;
                SelectionRect.Left += 1;
                SelectionRect.Right -= 1;
                SelectionRect.Top += 1;
                SelectionRect.Bottom -= 1;
            }
            public void Draw(RenderTarget textRenderTarget, TextFormat textFormat, SolidColorBrush brush)
            {
                foreach (var column in Columns)
                    column.Draw(textRenderTarget, textFormat, brush);
            }
        };

        private static double TimerDelay = 1000.0;

        private static string TimerButton = "";

        static AdjustmentPanelModel()
        {
            ButtonTimer = new System.Timers.Timer(TimerDelay)
            {
                AutoReset = false,
                Enabled = false
            };
            ButtonTimer.Elapsed += ButtonTimer_Elapsed;
        }

        private static void ButtonTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            //Debug.Write("T");
            TimerDelay = TimerDelay / 2.0;
            if (TimerDelay < 5) TimerDelay = 5;
            ButtonTimer.Interval = TimerDelay;
            if (SelectedCell != null)
            {
                Actions.TryGetValue(SelectedCell.Row.ActionGroup, out var group);
                if (group != null)
                {
                    group.OnButtonPressed?.Invoke(TimerButton);
                    NeedsTableRedraw = true;
                }
            }
            ButtonTimer.Start();
        }

        private static List<MenuRow> MenuRows = new List<MenuRow>();

        public static bool NeedsTableRedraw = true;

        private static SharpDX.Direct2D1.Bitmap InfoTable;
        private static SharpDX.Direct2D1.Bitmap ShownIcon;
        private static SharpDX.Direct2D1.Bitmap HiddenIcon;
        private static SharpDX.Direct2D1.Bitmap LockedIcon;

        private static Dictionary<string, MenuCell> CellMap = new Dictionary<string, MenuCell>();

        private static bool ShowOriginalValue = false;

        private static void InitTable()
        {
            Actions.Add("LEFT", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.Left); },
                Icons =
                {
                    new IconArea { Location = new PointF { X = 140, Y = 5 } }
                },
                OnButtonPressed = (b) => { }
            });
            Actions.Add("RIGHT", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.Right); },
                Icons =
                {
                    new IconArea { Location = new PointF { X = 310, Y = 5 } }
                },
                OnButtonPressed = (b) => { }
            });
            Actions.Add("ECENTERX", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustEyeCenters(AdjustStep, 0);
                            break;
                        case "D":
                            AdjustEyeCenters(-AdjustStep, 0);
                            break;
                    }
                },
            });
            Actions.Add("ECENTERY", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustEyeCenters(0, AdjustStep);
                            break;
                        case "D":
                            AdjustEyeCenters(0, -AdjustStep);
                            break;
                    }
                },
            });
            Actions.Add("FOCALX", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "D":
                            AdjustFocus(-AdjustStep, 0);
                            break;
                        case "U":
                            AdjustFocus(AdjustStep, 0);
                            break;
                    }
                },
            });
            Actions.Add("FOCALY", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustFocus(0, AdjustStep);
                            break;
                        case "D":
                            AdjustFocus(0, -AdjustStep);
                            break;
                    }
                },
            });
            Actions.Add("GROW", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustGrow(AdjustStep);
                            break;
                        case "D":
                            AdjustGrow(-AdjustStep);
                            break;
                    }
                },
            });
            Actions.Add("CUTOFF", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustCutoff(AdjustStep);
                            break;
                        case "D":
                            AdjustCutoff(-AdjustStep);
                            break;
                    }
                },
            });
            Actions.Add("CCENTERX", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustColorCenters(AdjustStep, 0);
                            break;
                        case "D":
                            AdjustColorCenters(-AdjustStep, 0);
                            break;
                    }
                },
            });
            Actions.Add("CCENTERY", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustColorCenters(0, AdjustStep);
                            break;
                        case "D":
                            AdjustColorCenters(0, -AdjustStep);
                            break;
                    }
                },
            });
            Actions.Add("COEFF1", new ActionGroup
            {
                Icons =
                {
                    new IconArea { Location = new PointF { X = 80, Y = 295 } },
                    new IconArea { Location = new PointF { X = 80, Y = 295+29+29+29+29+29+29 } },
                    new IconArea { Location = new PointF { X = 80, Y = 295+29+29+29+29+29+29+29+29+29+29+29+29 } },
                },
                OnToggle = () => { RenderFlags ^= RenderFlag.K1; },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.K1); },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustCoefficients(AdjustStep);
                            break;
                        case "D":
                            AdjustCoefficients(-AdjustStep);
                            break;
                    }
                },
            });
            Actions.Add("COEFF2", new ActionGroup
            {
                Icons =
                {
                    new IconArea { Location = new PointF { X = 80, Y = 295+29 } },
                    new IconArea { Location = new PointF { X = 80, Y = 295+29+29+29+29+29+29+29 } },
                    new IconArea { Location = new PointF { X = 80, Y = 295+29+29+29+29+29+29+29+29+29+29+29+29+29 } },
                },
                OnToggle = () => { RenderFlags ^= RenderFlag.K2; },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.K2); },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustCoefficients(AdjustStep);
                            break;
                        case "D":
                            AdjustCoefficients(-AdjustStep);
                            break;
                    }
                },
            });
            Actions.Add("COEFF3", new ActionGroup
            {
                Icons =
                {
                    new IconArea { Location = new PointF { X = 80, Y = 295+29+29} },
                    new IconArea { Location = new PointF { X = 80, Y = 295+29+29+29+29+29+29+29+29} },
                    new IconArea { Location = new PointF { X = 80, Y = 295+29+29+29+29+29+29+29+29+29+29+29+29+29+29} }
                },
                OnToggle = () => { RenderFlags ^= RenderFlag.K3; },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.K3); },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustCoefficients(AdjustStep);
                            break;
                        case "D":
                            AdjustCoefficients(-AdjustStep);
                            break;
                    }
                },
            });
            Actions.Add("RED", new ActionGroup
            {
                IsColorVisible = () => { return RenderFlags.HasFlag(RenderFlag.RenderRed); },
                Icons =
                {
                    new IconArea { Location = new PointF { X = 150, Y = 208 } },
                    new IconArea { Location = new PointF { X = 390, Y = 208 } , Type = "V"}

                },
                OnToggle = () => { RenderFlags ^= RenderFlag.RedActive; },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.RedActive); },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "T":
                            RenderFlags ^= RenderFlag.RenderRed;
                            break;
                    }
                },
            });
            Actions.Add("GREEN", new ActionGroup
            {
                IsColorVisible = () => { return RenderFlags.HasFlag(RenderFlag.RenderGreen); },
                Icons =
                {
                    new IconArea { Location = new PointF { X = 150, Y = 382 } },
                    new IconArea { Location = new PointF { X = 390, Y = 382 } , Type = "V"}
                },
                OnToggle = () => { RenderFlags ^= RenderFlag.GreenActive; },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.GreenActive); },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "T":
                            RenderFlags ^= RenderFlag.RenderGreen;
                            break;
                    }
                },
            });
            Actions.Add("BLUE", new ActionGroup
            {
                IsColorVisible = () => { return RenderFlags.HasFlag(RenderFlag.RenderBlue); },
                Icons =
                {
                    new IconArea { Location = new PointF { X = 150, Y = 556 } },
                    new IconArea { Location = new PointF { X = 390, Y = 556} , Type = "V"}
                },
                OnToggle = () => { RenderFlags ^= RenderFlag.BlueActive; },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.BlueActive); },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "T":
                            RenderFlags ^= RenderFlag.RenderBlue;
                            break;
                    }
                },
            });
            Actions.Add("RESET", new ActionGroup
            {
                OnToggle = () => { ResetToOriginalValues(); },
                IsActive = () => { return true; },
                OnButtonPressed = (b) => { },
            });
            Actions.Add("SCRATCH", new ActionGroup
            {
                OnToggle = () => { ResetEyes(); },
                IsActive = () => { return true; },
                OnButtonPressed = (b) => { },
            });
            Actions.Add("RADIUS", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButtonPressed = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            CrossHairModel.ModifyCircles(Program.d3dDevice, AdjustStep);
                            break;
                        case "D":
                            CrossHairModel.ModifyCircles(Program.d3dDevice, -AdjustStep);
                            break;
                    }
                },
            });
            Actions.Add("WRITE", new ActionGroup
            {
                OnToggle = () => { SaveSettings(); },
                IsActive = () => { return true; },
                OnButtonPressed = (b) => { },
            });


            int rowIndex = 0;
            MenuRows.AddRange(new[]
            {
                new MenuRow(rowIndex, "ECENTERX", 120) //centerx
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).EyeCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).EyeCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "ECENTERY", 120) //centery
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).EyeCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).EyeCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "FOCALX", 120) //focalx
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).FocalX.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).FocalX.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "FOCALY", 120) //focaly
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).FocalY.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).FocalY.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "GROW", 120) //grow
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).GrowToUndistort.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).GrowToUndistort.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "CUTOFF", 120) //cutoff
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).UndistortR2Cutoff.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).UndistortR2Cutoff.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "RED", 120) //RED
                {
                    BackColor = new RawColor4(1, 0, 0, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return null; }
                        }
                    }
                },
                new MenuRow(++rowIndex, "CCENTERX", 120) //ccenterx
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).RedCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).RedCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                    }
                },
                new MenuRow(++rowIndex, "CCENTERY", 120) //ccentery
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).RedCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).RedCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                    }
                },
                new MenuRow(++rowIndex, "COEFF1", 120) //C1
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).RedCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).RedCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "COEFF2", 120) //C2
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).RedCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).RedCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "COEFF3", 120) //C3
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).RedCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).RedCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "GREEN", 120) //green
                {
                    BackColor = new RawColor4(1, 0, 0, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return null; }
                        }
                    }
                },
                new MenuRow(++rowIndex, "CCENTERX", 120) //ccenterx
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).GreenCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).GreenCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                    }
                },
                new MenuRow(++rowIndex, "CCENTERY", 120) //ccentery
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).GreenCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).GreenCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                    }
                },
                new MenuRow(++rowIndex, "COEFF1", 120) //c1
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).GreenCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).GreenCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "COEFF2", 120) //c2
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).GreenCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).GreenCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "COEFF3", 120) //c3
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).GreenCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).GreenCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "BLUE", 120) //blue
                {
                    BackColor = new RawColor4(1, 0, 0, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return null; }
                        }
                    }
                },
                new MenuRow(++rowIndex, "CCENTERX", 120) //ccenterx
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).BlueCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).BlueCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                    }
                },
                new MenuRow(++rowIndex, "CCENTERY", 120) //ccentery
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).BlueCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).BlueCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                    }
                },
                new MenuRow(++rowIndex, "COEFF1", 120) //c1
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).BlueCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).BlueCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "COEFF2", 120) //c2
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).BlueCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).BlueCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "COEFF3", 120) //c3
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.GetData(ShowOriginalValue).BlueCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.GetData(ShowOriginalValue).BlueCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "RESET", 120) //step
                {
                    BackColor = new RawColor4(0.97255f, 0.796078f, 0.678431f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            Checkable = false,
                            GetText = () => { return null; }
                        }
                    }
                },
                new MenuRow(++rowIndex, "SCRATCH", 120) //step
                {
                    BackColor = new RawColor4(0.97255f, 0.796078f, 0.678431f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            Checkable = false,
                            GetText = () => { return null; }
                        }
                    }
                },
                new MenuRow(++rowIndex, "RADIUS", 120) //step
                {
                    BackColor = new RawColor4(0.97255f, 0.796078f, 0.678431f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            Checkable = false,
                            GetText = () => { return CrossHairModel.Radius.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "WRITE", 120) //step
                {
                    BackColor = new RawColor4(0.97255f, 0.796078f, 0.678431f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            Checkable = false,
                            GetText = () => { return null; }
                        }
                    }
                },
                new MenuRow(++rowIndex, "ADJ", 120) //step
                {
                    BackColor = new RawColor4(0.97255f, 0.796078f, 0.678431f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            Checkable = false,
                            GetText = () => { return AdjustStep.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
            });

            foreach (var row in MenuRows)
            {

                row.BackBrush = new SolidColorBrush(textRenderTarget, row.BackColor);
                var c = 0;
                foreach (var col in row.Columns)
                {
                    if (SelectedCell == null)
                    {
                        row.IsFocused = true;
                        SelectedCell = col;
                    }
                    col.Row = row;
                    c++;
                }
            }


            ShownIcon = LoadFromResource(Properties.Resources.shown);
            LockedIcon = LoadFromResource(Properties.Resources.locked);
            HiddenIcon = LoadFromResource(Properties.Resources.hidden);
            InfoTable = LoadFromResource(Properties.Resources.InfoTable);
        }

        public static SharpDX.Direct2D1.Bitmap LoadFromResource(System.Drawing.Bitmap bitmap)
        {
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
                return new SharpDX.Direct2D1.Bitmap(textRenderTarget, size, tempStream, stride, bitmapProperties);
            }
        }

        public static Matrix WVP = Matrix.Zero;        

        public static bool CheckBounds(ref Ray ray, out float depth)
        {
            var rayLength = (ray.Direction - ray.Position).Length();

            var p0 = (Matrix.Translation(panelVertices[0], panelVertices[1], panelVertices[2]) * WVP).TranslationVector;
            var p1 = (Matrix.Translation(panelVertices[5], panelVertices[6], panelVertices[7]) * WVP).TranslationVector;
            var p2 = (Matrix.Translation(panelVertices[10], panelVertices[11], panelVertices[12]) * WVP).TranslationVector;
            var p3 = (Matrix.Translation(panelVertices[15], panelVertices[16], panelVertices[17]) * WVP).TranslationVector;

            Vector3 point;
            if (SharpDX.Collision.RayIntersectsTriangle(ref ray, ref p0, ref p2, ref p3, out point))
            {                
                var collLength = (point - ray.Position).Length();
                depth = (collLength / rayLength);
                var f1 = p0 - point;
                var f2 = p2 - point;
                var f3 = p3 - point;
                var a = Vector3.Cross(p0 - p2, p0 - p3).Length(); // main triangle area a
                var a1 = Vector3.Cross(f2, f3).Length() / a; // p1's triangle area / a
                var a2 = Vector3.Cross(f3, f1).Length() / a; // p2's triangle area / a 
                var a3 = Vector3.Cross(f1, f2).Length() / a; // p3's triangle area / a

                var uv = new Vector2(-1, -1) * a1 + new Vector2(1, 1) * a2 + new Vector2(-1, 1) * a3;
                uv = (uv + 1) / 2;

                var x = uv.X * Properties.Resources.InfoTable.Width;
                var y = Properties.Resources.InfoTable.Height - (uv.Y * Properties.Resources.InfoTable.Height);

                UpdateSelection(x, y);

                return true;
            }

            if (SharpDX.Collision.RayIntersectsTriangle(ref ray, ref p0, ref p1, ref p2, out point))
            {
                var collLength = (point - ray.Position).Length();
                depth = (collLength / rayLength);
                var f1 = p0 - point;
                var f2 = p1 - point;
                var f3 = p2 - point;
                var a = Vector3.Cross(p0 - p1, p0 - p2).Length(); // main triangle area a
                var a1 = Vector3.Cross(f2, f3).Length() / a; // p1's triangle area / a
                var a2 = Vector3.Cross(f3, f1).Length() / a; // p2's triangle area / a 
                var a3 = Vector3.Cross(f1, f2).Length() / a; // p3's triangle area / a

                var uv = new Vector2(-1, -1) * a1 + new Vector2(1, -1) * a2 + new Vector2(1, 1) * a3;
                uv = (uv + 1) / 2;
                var x = uv.X * Properties.Resources.InfoTable.Width;
                var y = Properties.Resources.InfoTable.Height - (uv.Y * Properties.Resources.InfoTable.Height);

                UpdateSelection(x, y);

                return true;
            }

            

            depth = 0;
            return false;
        }

        private static void UpdateSelection(float x, float y)
        {
            foreach (var row in MenuRows)
            {
                if (y >= row.SelectionRect.Top && y <= row.SelectionRect.Bottom)
                {
                    if (SelectedCell != row.Columns[0])
                    {
                        SelectedCell = row.Columns[0];
                        NeedsTableRedraw = true;
                    }
                    return;
                }
            }
        }

        public static void Init(SharpDX.Direct3D11.Device device)
        {
            //eye = attachedSide;

            panelShader = new Shader(device, "Info_VS", "Info_PS", new[]
            {
                new SharpDX.Direct3D11.InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new SharpDX.Direct3D11.InputElement("TEXCOORD", 0, Format.R32G32_Float, 12, 0)
            });

            var panelAspect = (float)Properties.Resources.InfoTable.Width / (float)Properties.Resources.InfoTable.Height;
            panelAspect /= leftEye.DistortionData.Aspect;

            panelVertices = new float[] {
                -0.2f * panelAspect, 0.05f, 0f, 0, 1, //0
                0.2f * panelAspect, 0.05f, 0f, 1, 1, //1
                0.2f * panelAspect, 0.25f, -0.25f, 1, 0, //2
                -0.2f * panelAspect, 0.25f, -0.25f, 0, 0//3
            };

            var indices = new int[] { 0, 2, 3, 0, 1, 2 };

            panelIndexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.IndexBuffer, indices);
            panelVertexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, panelVertices);
            panelVertexBufferBinding = new VertexBufferBinding(panelVertexBuffer, sizeof(float) * 5, 0);

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

            //var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
            //var d2DDevice = new SharpDX.Direct2D1.Device(dxgiDevice);
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
            //headerTextFormat = new TextFormat(directWriteFactory, "Courier New", FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 20.0f);
            //headerTextFormat.ParagraphAlignment = ParagraphAlignment.Center;
            //headerTextFormat.TextAlignment = TextAlignment.Leading;
            blackBrush = new SolidColorBrush(textRenderTarget, SharpDX.Color4.Black);
            selectedItemBrush = new SolidColorBrush(textRenderTarget, new SharpDX.Color4(1.0f, 1.0f, 0.0f, 0.5f));


            var vertices = new float[] { -1f, 0f };
            pointerVertexBuffer = SharpDX.Direct3D11.Buffer.Create(device, BindFlags.VertexBuffer, vertices);
            pointerVertexBufferBinding = new VertexBufferBinding(pointerVertexBuffer, sizeof(float) * 2, 0);

            InitTable();
        }

        public static void Render(SharpDX.Direct3D11.DeviceContext context)
        {

            if (NeedsTableRedraw) //very slow, only redraw if needed
            {
                NeedsTableRedraw = false;
                textRenderTarget.BeginDraw();

                textRenderTarget.DrawBitmap(InfoTable, 1.0f, BitmapInterpolationMode.Linear); //super slow, put on 2nd texture and prevent redraw

                foreach (var row in MenuRows)
                {
                    if (row.IsFocused || (row.ActionGroup == SelectedCell.Row.ActionGroup))
                        textRenderTarget.FillRectangle(row.SelectionRect, selectedItemBrush);
                    row.Draw(textRenderTarget, textFormat, blackBrush);
                }

                foreach (var action in Actions.Values)
                {
                    action.Draw(textRenderTarget); //
                }


                textRenderTarget.EndDraw(out long tag1, out long tag2);
            }

            panelShader.Apply(context);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, panelVertexBufferBinding);
            context.InputAssembler.SetIndexBuffer(panelIndexBuffer, Format.R32_UInt, 0);
            context.PixelShader.SetShaderResource(0, textureView);
            context.DrawIndexed(6, 0, 0);
        }

        public static void ButtonPressed(string button, ETrackedControllerRole role)
        {
            var prevSelected = SelectedCell;
            switch (role)
            {
                case ETrackedControllerRole.LeftHand:
                    switch (button)
                    {
                        case "G":
                            RenderFlags ^= RenderFlag.Left;
                            break;
                        case "T":
                            if (SelectedCell != null)
                            {
                                Actions.TryGetValue(SelectedCell.Row.ActionGroup, out var group);
                                if (group != null)
                                    group.OnToggle?.Invoke();
                            }
                            break;
                        case "U":
                            if (SelectedCell != null)
                            {
                                while (prevSelected.Row.ActionGroup == SelectedCell.Row.ActionGroup)
                                {
                                    var rowidx = SelectedCell.RowIndex;
                                    rowidx--;
                                    if (rowidx < 0)
                                        return;
                                    SelectedCell = MenuRows[rowidx].Columns[0];
                                }
                                SelectedCell.Row.IsFocused = true;
                                if (prevSelected != null && prevSelected != SelectedCell) prevSelected.Row.IsFocused = false;
                            }
                            break;
                        case "D":
                            if (SelectedCell != null)
                            {
                                while (prevSelected.Row.ActionGroup == SelectedCell.Row.ActionGroup)
                                {
                                    var rowidx = SelectedCell.RowIndex;
                                    rowidx++;
                                    if (rowidx > MenuRows.Count - 2) //skip last menuitem
                                        return;
                                    SelectedCell = MenuRows[rowidx].Columns[0];
                                }
                                SelectedCell.Row.IsFocused = true;
                                if (prevSelected != null && prevSelected != SelectedCell) prevSelected.Row.IsFocused = false;
                            }
                            break;
                    }
                    break;
                case ETrackedControllerRole.RightHand:
                    if (button == "T")
                        ShowOriginalValue = true;
                    switch (button)
                    {
                        case "L":
                            IncreaseAdjustStep();
                            break;
                        case "R":
                            DecreaseAdjustStep();
                            break;
                        case "G":
                            RenderFlags ^= RenderFlag.Right;
                            break;
                        default:
                            if (SelectedCell != null)
                            {
                                if (button == "U" || button == "D")
                                {
                                    TimerButton = button;
                                    TimerDelay = 1000;
                                    ButtonTimer.Interval = TimerDelay;
                                    ButtonTimer.Start();
                                }

                                Actions.TryGetValue(SelectedCell.Row.ActionGroup, out var group);
                                if (group != null)
                                    group.OnButtonPressed?.Invoke(button);
                            }
                            break;
                    }
                    break;
            }
            NeedsTableRedraw = true;
        }

        public static void ButtonUnPressed(string button, ETrackedControllerRole role)
        {
            var prevSelected = SelectedCell;
            switch (role)
            {
                case ETrackedControllerRole.RightHand:
                    if (button == "U" || button == "D")
                    {
                        ButtonTimer.Stop();
                        TimerDelay = 1000;
                        ButtonTimer.Interval = TimerDelay;
                        TimerButton = "";
                    }

                    if (button == "T")
                        ShowOriginalValue = false;
                    //if (SelectedCell != null)
                    //{
                    //    Actions.TryGetValue(SelectedCell.Row.ActionGroup, out var group);
                    //    if (group != null)
                    //        group.OnButtonUnPressed?.Invoke(button);
                    //}
                    break;
            }
            NeedsTableRedraw = true;
        }
    }
}

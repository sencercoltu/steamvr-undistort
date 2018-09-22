using SharpDX.Direct2D1;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DirectWrite;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using System;
using System.Collections.Generic;
using System.Drawing;
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
        private static SolidColorBrush checkedItemBrush;
        private static SolidColorBrush selectedItemBrush;

        //private static EyeData eye;
        public static bool Show = true;

        private static SharpDX.Color4 activeColor = new SharpDX.Color4(0f, 1, 0f, 1);
        private static SharpDX.Color4 inactiveColor = new SharpDX.Color4(1, 0.666f, 0.784f, 1);

        public class IconArea
        {
            public string Type= "L";
            public PointF Location;
        }

        private class ActionGroup
        {
            public Action OnToggle;
            public Func<bool> IsActive;
            public Func<bool> IsColorVisible;
            public Action<string> OnButton;
            public void Draw(RenderTarget textRenderTarget)
            {
                foreach (var point in Icons)
                {
                    var area = new RawRectangleF(point.Location.X, point.Location.Y, point.Location.X + 28, point.Location.Y + 28);
                    if (IsActive() && point.Type == "L")
                        textRenderTarget.DrawBitmap(LockedIcon, area, 1.0f, BitmapInterpolationMode.Linear);
                    else if (point.Type == "V" && IsColorVisible != null)
                    {
                        textRenderTarget.DrawBitmap(IsColorVisible()? ShownIcon : HiddenIcon, area, 1.0f, BitmapInterpolationMode.Linear);                            
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
                    textRenderTarget.FillRectangle(TextArea, Row.BackBrush);
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
                if (IsFocused)
                    textRenderTarget.DrawRectangle(SelectionRect, selectedItemBrush, 3f);

            }
        };

        private static List<MenuRow> MenuRows = new List<MenuRow>();

        public static bool NeedsTableRedraw = true;

        private static SharpDX.Direct2D1.Bitmap InfoTable;
        private static SharpDX.Direct2D1.Bitmap ShownIcon;
        private static SharpDX.Direct2D1.Bitmap HiddenIcon;
        private static SharpDX.Direct2D1.Bitmap LockedIcon;

        private static Dictionary<string, MenuCell> CellMap = new Dictionary<string, MenuCell>();

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
                OnButton = (b) => { }
            });
            Actions.Add("RIGHT", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.Right); },
                Icons =
                {
                    new IconArea { Location = new PointF { X = 310, Y = 5 } }
                },
                OnButton = (b) => { }
            });
            Actions.Add("ECENTER", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButton = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustEyeCenters(0, AdjustStep);
                            break;
                        case "D":
                            AdjustEyeCenters(0, -AdjustStep);
                            break;
                        case "L":
                            AdjustEyeCenters(-AdjustStep, 0);
                            break;
                        case "R":
                            AdjustEyeCenters(AdjustStep, 0);
                            break;
                    }
                },
            });
            Actions.Add("FOCAL", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButton = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustFocus(0, AdjustStep);
                            break;
                        case "D":
                            AdjustFocus(0, -AdjustStep);
                            break;
                        case "L":
                            AdjustFocus(-AdjustStep, 0);
                            break;
                        case "R":
                            AdjustFocus(AdjustStep, 0);
                            break;
                    }
                },
            });
            Actions.Add("GROW", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButton = (b) =>
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
                OnButton = (b) =>
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
            Actions.Add("CCENTER", new ActionGroup
            {
                OnToggle = () => { },
                IsActive = () => { return true; },
                OnButton = (b) =>
                {
                    switch (b)
                    {
                        case "U":
                            AdjustColorCenters(0, AdjustStep);
                            break;
                        case "D":
                            AdjustColorCenters(0, -AdjustStep);
                            break;
                        case "L":
                            AdjustColorCenters(-AdjustStep, 0);
                            break;
                        case "R":
                            AdjustColorCenters(AdjustStep, 0);
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
                OnButton = (b) =>
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
                OnButton = (b) =>
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
                OnButton = (b) =>
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
            Actions.Add("ADJ", new ActionGroup
            {
                IsActive = () => { return true; },
                OnButton = (b) =>
                {
                    switch (b)
                    {
                        case "L":
                            IncreaseAdjustStep();
                            break;
                        case "R":
                            DecreaseAdjustStep();
                            break;
                    }
                },
            });
            Actions.Add("RED", new ActionGroup
            {
                IsColorVisible = () => { return RenderFlags.HasFlag(RenderFlag.RenderRed); },
                Icons =
                {
                    new IconArea { Location = new PointF { X = 220, Y = 208 } },
                    new IconArea { Location = new PointF { X = 330, Y = 208 } , Type = "V"}

                },
                OnToggle = () => { RenderFlags ^= RenderFlag.RedActive; },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.RedActive); },
                OnButton = (b) =>
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
                    new IconArea { Location = new PointF { X = 220, Y = 382 } },
                    new IconArea { Location = new PointF { X = 330, Y = 382 } , Type = "V"}
                },
                OnToggle = () => { RenderFlags ^= RenderFlag.GreenActive; },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.GreenActive); },
                OnButton = (b) =>
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
                    new IconArea { Location = new PointF { X = 220, Y = 556 } },
                    new IconArea { Location = new PointF { X = 330, Y = 556} , Type = "V"}
                },
                OnToggle = () => { RenderFlags ^= RenderFlag.BlueActive; },
                IsActive = () => { return RenderFlags.HasFlag(RenderFlag.BlueActive); },
                OnButton = (b) =>
                {
                    switch (b)
                    {
                        case "T":
                            RenderFlags ^= RenderFlag.RenderBlue;
                            break;
                    }
                },
            });



            int rowIndex = 0;
            MenuRows.AddRange(new[]
            {
                new MenuRow(rowIndex, "ECENTER", 120) //centerx
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.DistortionData.EyeCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.EyeCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "ECENTER", 120) //centery
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.DistortionData.EyeCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.EyeCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "FOCAL", 120) //focalx
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.DistortionData.FocalX.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.FocalX.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        }
                    }
                },
                new MenuRow(++rowIndex, "FOCAL", 120) //focaly
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.DistortionData.FocalY.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.FocalY.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.GrowToUndistort.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.GrowToUndistort.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.UndistortR2Cutoff.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.UndistortR2Cutoff.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                new MenuRow(++rowIndex, "CCENTER", 120) //ccenterx
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.DistortionData.RedCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.RedCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                    }
                },
                new MenuRow(++rowIndex, "CCENTER", 120) //ccentery
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.DistortionData.RedCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.RedCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.RedCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.RedCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.RedCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.RedCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.RedCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.RedCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                new MenuRow(++rowIndex, "CCENTER", 120) //ccenterx
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.DistortionData.GreenCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.GreenCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                    }
                },
                new MenuRow(++rowIndex, "CCENTER", 120) //ccentery
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.DistortionData.GreenCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.GreenCenter.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.GreenCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.GreenCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.GreenCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.GreenCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.GreenCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.GreenCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                new MenuRow(++rowIndex, "CCENTER", 120) //ccenterx
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.DistortionData.BlueCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.BlueCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                    }
                },
                new MenuRow(++rowIndex, "CCENTER", 120) //ccentery
                {
                    BackColor = new RawColor4(0.85f, 0.85f, 0.85f, 1),
                    Columns =
                    {
                        new MenuCell(rowIndex, 120)
                        {
                            GetText = () => { return leftEye.DistortionData.BlueCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.BlueCenter.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.BlueCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.BlueCoeffs.X.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.BlueCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.BlueCoeffs.Y.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                            GetText = () => { return leftEye.DistortionData.BlueCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
                        },
                        new MenuCell(rowIndex, 286)
                        {
                            GetText = () => { return rightEye.DistortionData.BlueCoeffs.Z.ToString(" 0.00000000;-0.00000000", CultureInfo.InvariantCulture); }
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
                }
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

        public static void Init(SharpDX.Direct3D11.Device device)
        {
            //eye = attachedSide;

            shader = new Shader(device, "Info_VS", "Info_PS", new[]
            {
                new SharpDX.Direct3D11.InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new SharpDX.Direct3D11.InputElement("TEXCOORD", 0, Format.R32G32_Float, 12, 0)
            });

            var aspect = (float)Properties.Resources.InfoTable.Width / (float)Properties.Resources.InfoTable.Height;
            aspect /= leftEye.DistortionData.Aspect;

            vertices = new float[] {
                -0.2f * aspect, 0.05f, 0f, 0, 1, //0
                0.2f * aspect, 0.05f, 0f, 1, 1, //1
                0.2f * aspect, 0.25f, -0.25f, 1, 0, //2
                -0.2f * aspect, 0.25f, -0.25f, 0, 0//3
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
            headerTextFormat = new TextFormat(directWriteFactory, "Courier New", FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 20.0f);
            headerTextFormat.ParagraphAlignment = ParagraphAlignment.Center;
            headerTextFormat.TextAlignment = TextAlignment.Leading;
            blackBrush = new SolidColorBrush(textRenderTarget, SharpDX.Color4.Black);
            checkedItemBrush = new SolidColorBrush(textRenderTarget, new SharpDX.Color4(0f, 1f, 0f, 1.0f));
            selectedItemBrush = new SolidColorBrush(textRenderTarget, new SharpDX.Color4(1.0f, 1.0f, 0.0f, 1.0f));

            InitTable();
        }

        private static long LastRenderTime = 0;

        public static void Render(SharpDX.Direct3D11.DeviceContext context)
        {
            var now = DateTime.Now.Ticks;
            if (now - LastRenderTime > 6666666) //render texture only at 15 fps seconds
            {
                textRenderTarget.BeginDraw();
                if (NeedsTableRedraw)
                {
                    //very slow, only redraw if needed
                    NeedsTableRedraw = false;
                    textRenderTarget.DrawBitmap(InfoTable, 1.0f, BitmapInterpolationMode.NearestNeighbor);
                }

                foreach (var row in MenuRows)
                    row.Draw(textRenderTarget, textFormat, blackBrush);

                foreach (var action in Actions.Values)
                {
                    action.Draw(textRenderTarget);
                }

                textRenderTarget.EndDraw(out long tag1, out long tag2);
                LastRenderTime = now;
            }

            shader.Apply(context);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
            context.InputAssembler.SetIndexBuffer(indexBuffer, Format.R32_UInt, 0);
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
                                var rowidx = SelectedCell.RowIndex;
                                rowidx--;
                                if (rowidx < 0)
                                    return;
                                SelectedCell = MenuRows[rowidx].Columns[0];
                                SelectedCell.Row.IsFocused = true;
                                if (prevSelected != null && prevSelected != SelectedCell) prevSelected.Row.IsFocused = false;
                            }
                            break;
                        case "D":
                            if (SelectedCell != null)
                            {
                                var rowidx = SelectedCell.RowIndex;
                                rowidx++;
                                if (rowidx > MenuRows.Count - 1)
                                    return;
                                SelectedCell = MenuRows[rowidx].Columns[0];
                                SelectedCell.Row.IsFocused = true;
                                if (prevSelected != null && prevSelected != SelectedCell) prevSelected.Row.IsFocused = false;
                            }
                            break;
                        case "R":
                            if (SelectedCell != null && SelectedCell.RowIndex == 0)
                            {
                                var rowidx = SelectedCell.RowIndex;
                                SelectedCell = MenuRows[rowidx].Columns[0];
                                SelectedCell.Row.IsFocused = true;
                                if (prevSelected != null && prevSelected != SelectedCell) prevSelected.Row.IsFocused = false;
                            }
                            break;
                        case "L":
                            if (SelectedCell != null && SelectedCell.RowIndex == 0)
                            {
                                var rowidx = SelectedCell.RowIndex;
                                SelectedCell = MenuRows[rowidx].Columns[0];
                                SelectedCell.Row.IsFocused = true;
                                if (prevSelected != null && prevSelected != SelectedCell) prevSelected.Row.IsFocused = false;
                            }
                            break;
                    }
                    break;
                case ETrackedControllerRole.RightHand:
                    switch (button)
                    {
                        case "G":
                            RenderFlags ^= RenderFlag.Right;
                            break;
                        default:
                            if (SelectedCell != null)
                            {
                                Actions.TryGetValue(SelectedCell.Row.ActionGroup, out var group);
                                if (group != null)
                                    group.OnButton?.Invoke(button);
                            }
                            break;
                    }
                    break;
            }
            LastRenderTime = 0;
            NeedsTableRedraw = true;
        }
    }
}

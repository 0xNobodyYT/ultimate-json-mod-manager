using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CdJsonModManager
{
    internal class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; } = 18;
        public int BorderWidth { get; set; } = 1;
        public Color BorderColor { get; set; } = Color.FromArgb(80, 216, 166, 64);
        public Color GradientTopOverride { get; set; } = Color.Empty;
        public Color GradientBottomOverride { get; set; } = Color.Empty;
        public Color BottomBorderColor { get; set; } = Color.Empty;
        public bool Dashed { get; set; } = false;
        public bool PulseAccent { get; set; } = false;
        public Color AccentColor { get; set; } = Color.FromArgb(216, 166, 64);

        public RoundedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            var top = GradientTopOverride == Color.Empty ? Color.FromArgb(245, 32, 33, 21) : GradientTopOverride;
            var bot = GradientBottomOverride == Color.Empty ? Color.FromArgb(245, 18, 19, 14) : GradientBottomOverride;
            using (var path = RoundedRect(rect, CornerRadius))
            {
                if (rect.Width > 0 && rect.Height > 0)
                {
                    using (var brush = new LinearGradientBrush(rect, top, bot, 90f))
                        g.FillPath(brush, path);
                }
                if (BorderWidth > 0)
                {
                    using (var pen = new Pen(PulseAccent ? AccentColor : BorderColor, BorderWidth))
                    {
                        if (Dashed)
                        {
                            pen.DashStyle = DashStyle.Dash;
                            pen.DashPattern = new float[] { 6f, 4f };
                        }
                        g.DrawPath(pen, path);
                    }
                }
                if (BottomBorderColor != Color.Empty && CornerRadius == 0)
                {
                    using (var pen = new Pen(BottomBorderColor, 1))
                        g.DrawLine(pen, 0, rect.Bottom, rect.Right, rect.Bottom);
                }
            }
            base.OnPaint(e);
        }

        public static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0 || rect.Width <= 0 || rect.Height <= 0)
            {
                if (rect.Width > 0 && rect.Height > 0) path.AddRectangle(rect);
                else path.AddRectangle(new Rectangle(0, 0, 1, 1));
                return path;
            }
            var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseAllFigures();
            return path;
        }
    }

    internal sealed class Pill : Control
    {
        public Color DotColor { get; set; } = Color.Empty;
        public Color WarnColor { get; set; } = Color.FromArgb(216, 166, 64);
        public Color PillFillColor { get; set; } = Color.FromArgb(12, 255, 255, 255);
        public Color PillBorderColor { get; set; } = Color.FromArgb(30, 255, 255, 255);
        public Color PillTextColor { get; set; } = Color.FromArgb(169, 157, 124);
        public bool BorderlessTag { get; set; } = false;

        public Pill()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            Font = new Font("Consolas", 8.5f, FontStyle.Bold);
            ForeColor = PillTextColor;
            BackColor = Color.Transparent;
            Height = 28;
            Margin = new Padding(0, 0, 8, 0);
        }

        public override string Text
        {
            get { return base.Text; }
            set
            {
                base.Text = value;
                AutoFitWidth();
                Invalidate();
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            AutoFitWidth();
        }

        private void AutoFitWidth()
        {
            using (var g = CreateGraphics())
            {
                var size = TextRenderer.MeasureText(g, Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                var leftPad = (DotColor != Color.Empty) ? 26 : 12;
                var rightPad = 12;
                Width = size.Width + leftPad + rightPad;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using (var path = RoundedPanel.RoundedRect(rect, Math.Max(0, Height / 2)))
            using (var bg = new SolidBrush(PillFillColor))
            {
                g.FillPath(bg, path);
                if (!BorderlessTag)
                {
                    using (var border = new Pen(PillBorderColor, 1))
                        g.DrawPath(border, path);
                }
            }

            var dotSpace = DotColor != Color.Empty ? 20 : 0;
            if (DotColor != Color.Empty)
            {
                using (var glow = new SolidBrush(Color.FromArgb(80, DotColor)))
                    g.FillEllipse(glow, 7, Height / 2 - 7, 14, 14);
                using (var dot = new SolidBrush(DotColor))
                    g.FillEllipse(dot, 9, Height / 2 - 5, 10, 10);
            }
            var fg = BorderlessTag ? PillTextColor : ForeColor;
            var size = TextRenderer.MeasureText(g, Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            var textX = Math.Max(dotSpace + 6, (Width - size.Width + dotSpace) / 2);
            var y = (Height - size.Height) / 2;
            TextRenderer.DrawText(g, Text ?? "", Font, new Point(textX, y), fg, TextFormatFlags.NoPadding);
        }
    }


    // Double-buffered scrolling Panel - used for the install panel body. Uses standard
    // DoubleBuffered (NOT WS_EX_COMPOSITED, which defers paint passes and breaks live thumb tracking).
    // We additionally handle WM_VSCROLL with SB_THUMBTRACK explicitly: WinForms' default ScrollableControl
    // updates AutoScrollPosition on thumb track but doesn't always paint immediately - calling Refresh
    // forces an immediate repaint so the user sees the content move while their mouse is still held.
    internal sealed class BufferedScrollPanel : Panel
    {
        public BufferedScrollPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_VSCROLL = 0x115;
            const int WM_HSCROLL = 0x114;
            const int SB_THUMBTRACK = 5;
            const int SB_THUMBPOSITION = 4;
            base.WndProc(ref m);
            if (m.Msg == WM_VSCROLL || m.Msg == WM_HSCROLL)
            {
                int code = m.WParam.ToInt32() & 0xFFFF;
                if (code == SB_THUMBTRACK || code == SB_THUMBPOSITION)
                {
                    // Force an immediate repaint so the content tracks the thumb in real time.
                    Refresh();
                }
                else
                {
                    Update();
                }
            }
        }
    }


    // Same trick for FlowLayoutPanel - used for the mod card host so the cards repaint smoothly
    // during AutoScroll. Same WS_EX_COMPOSITED reasoning: skip it.
    internal sealed class BufferedFlowPanel : FlowLayoutPanel
    {
        public BufferedFlowPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }


    // Fully custom checkbox built on Control, NOT CheckBox - the CheckBox base class still drives
    // some native painting through the Windows comctl32 button class even with UserPaint set, which
    // is what was leaving the checkbox looking unchanged regardless of our OnPaint colour tweaks.
    internal sealed class FlatCheck : Control
    {
        private bool _checked;
        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler CheckedChanged;

        public Color BoxFill { get; set; } = Color.FromArgb(80, 82, 66);
        public Color BoxBorder { get; set; } = Color.FromArgb(230, 210, 160);
        public Color CheckedFill { get; set; } = Color.FromArgb(216, 166, 64);
        public Color CheckedBorder { get; set; } = Color.FromArgb(244, 199, 103);
        public Color CheckMarkColor { get; set; } = Color.FromArgb(21, 15, 8);

        public FlatCheck()
        {
            SetStyle(ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Width = 20;
            Height = 20;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int side = Math.Min(Width, Height) - 2;
            var rect = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
            using (var path = RoundedPanel.RoundedRect(rect, 4))
            {
                using (var fill = new SolidBrush(Checked ? CheckedFill : BoxFill))
                    g.FillPath(fill, path);
                using (var pen = new Pen(Checked ? CheckedBorder : BoxBorder, 1.8f))
                    g.DrawPath(pen, path);
            }
            if (Checked)
            {
                using (var pen = new Pen(CheckMarkColor, 2.4f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    int x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;
                    g.DrawLines(pen, new[] {
                        new Point(x + (int)(w * 0.22), y + (int)(h * 0.52)),
                        new Point(x + (int)(w * 0.42), y + (int)(h * 0.72)),
                        new Point(x + (int)(w * 0.78), y + (int)(h * 0.30))
                    });
                }
            }
        }
    }


    // TabControl subclass that paints its full background dark before letting DrawItem render the tabs.
    // The stock TabControl with Appearance.FlatButtons paints its strip with SystemColors.Control (white),
    // which leaks through under our owner-drawn tab buttons. Taking over OnPaint kills that completely.
    internal sealed class DarkTabControl : TabControl
    {
        public Color StripColor { get; set; } = Color.FromArgb(14, 15, 11);

        public DarkTabControl()
        {
            SetStyle(ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            DrawMode = TabDrawMode.OwnerDrawFixed;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(StripColor)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Background - the whole control area, including the strip-right and the page-border line.
            using (var b = new SolidBrush(StripColor)) e.Graphics.FillRectangle(b, ClientRectangle);
            // Tabs - fire DrawItem for each so existing handlers render the buttons.
            for (int i = 0; i < TabCount; i++)
            {
                var rect = GetTabRect(i);
                var state = (i == SelectedIndex) ? DrawItemState.Selected : DrawItemState.None;
                var args = new DrawItemEventArgs(e.Graphics, Font, rect, i, state);
                OnDrawItem(args);
            }
        }
    }

    internal sealed class GradientButton : Control
    {
        public enum Style { Default, Primary, Safe, Danger, Donate }
        public Style Kind { get; set; } = Style.Default;
        private bool hovering;
        private bool pressed;

        public GradientButton()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Consolas", 9f, FontStyle.Bold);
            Height = 36;
            Margin = new Padding(0, 0, 8, 0);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { pressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            Color a, b, fg, border;
            switch (Kind)
            {
                case Style.Primary:
                    a = Color.FromArgb(244, 199, 103); b = Color.FromArgb(216, 166, 64);
                    fg = Color.FromArgb(23, 16, 6); border = Color.FromArgb(120, 244, 199, 103); break;
                case Style.Safe:
                    a = Color.FromArgb(155, 229, 179); b = Color.FromArgb(101, 197, 134);
                    fg = Color.FromArgb(8, 19, 13); border = Color.FromArgb(120, 101, 197, 134); break;
                case Style.Danger:
                    a = Color.FromArgb(238, 141, 127); b = Color.FromArgb(216, 92, 76);
                    fg = Color.FromArgb(24, 7, 5); border = Color.FromArgb(120, 216, 92, 76); break;
                case Style.Donate:
                    a = Color.FromArgb(255, 221, 0); b = Color.FromArgb(255, 193, 7);
                    fg = Color.FromArgb(24, 16, 4); border = Color.FromArgb(140, 255, 193, 7); break;
                default:
                    a = Color.FromArgb(34, 255, 255, 255); b = Color.FromArgb(14, 255, 255, 255);
                    fg = ForeColor; border = Color.FromArgb(36, 255, 255, 255); break;
            }
            if (hovering)
            {
                a = Lighten(a, 0.10f); b = Lighten(b, 0.10f);
            }
            if (pressed)
            {
                a = Darken(a, 0.10f); b = Darken(b, 0.10f);
            }
            using (var path = RoundedPanel.RoundedRect(rect, 12))
            using (var brush = new LinearGradientBrush(rect, a, b, 135f))
            using (var pen = new Pen(border, 1))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text ?? "", Font, rect, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private static Color Lighten(Color c, float amt)
        {
            return Color.FromArgb(c.A,
                (int)Math.Min(255, c.R + 255 * amt),
                (int)Math.Min(255, c.G + 255 * amt),
                (int)Math.Min(255, c.B + 255 * amt));
        }

        private static Color Darken(Color c, float amt)
        {
            return Color.FromArgb(c.A,
                (int)Math.Max(0, c.R - 255 * amt),
                (int)Math.Max(0, c.G - 255 * amt),
                (int)Math.Max(0, c.B - 255 * amt));
        }
    }

    internal sealed class BrandMark : Control
    {
        public string Letters { get; set; } = "UJ";

        // Loaded once at startup. If logo.png sits next to the exe (or as an
        // embedded resource named "logo.png"), it is rendered into the brand
        // tile. Otherwise we fall back to the gold "UJ" gradient tile.
        private static readonly Image logoImage = LoadLogoImage();

        private static Image LoadLogoImage()
        {
            try
            {
                var sidecar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
                if (File.Exists(sidecar))
                {
                    using (var fs = File.OpenRead(sidecar))
                    {
                        return Image.FromStream(new MemoryStream(File.ReadAllBytes(sidecar)));
                    }
                }
                var asm = Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var stream = asm.GetManifestResourceStream(name))
                        {
                            if (stream != null) return Image.FromStream(stream);
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public BrandMark()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            if (logoImage != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var path = RoundedPanel.RoundedRect(rect, Math.Max(6, Math.Min(rect.Width, rect.Height) / 6)))
                {
                    g.SetClip(path);
                    g.DrawImage(logoImage, rect);
                    g.ResetClip();
                }
                return;
            }
            using (var path = RoundedPanel.RoundedRect(rect, 14))
            using (var brush = new LinearGradientBrush(rect, Color.FromArgb(244, 199, 103), Color.FromArgb(159, 103, 36), 135f))
            using (var border = new Pen(Color.FromArgb(120, 216, 166, 64), 1))
            {
                g.FillPath(brush, path);
                g.DrawPath(border, path);
            }
            using (var f = new Font("Consolas", 18, FontStyle.Bold))
            {
                var ts = TextRenderer.MeasureText(g, Letters ?? "", f);
                var x = (Width - ts.Width) / 2;
                var y = (Height - ts.Height) / 2;
                TextRenderer.DrawText(g, Letters ?? "", f, new Point(x, y), Color.FromArgb(21, 16, 7), TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class ThemeSwatch : Control
    {
        public Theme SwatchTheme { get; private set; }
        public bool IsActive { get; set; }
        // When true the swatch renders as a dashed-border placeholder with a centred "+" - used for the
        // unconfigured Custom theme slot, so it reads as "click to choose" rather than as a real colour.
        public bool IsEmptyPlaceholder { get; set; }

        public ThemeSwatch(Theme theme)
        {
            SwatchTheme = theme;
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

            if (IsEmptyPlaceholder)
            {
                // Subtle translucent fill so the swatch reads as "empty" against the dark panel.
                using (var path = RoundedPanel.RoundedRect(rect, 12))
                using (var brush = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                {
                    g.FillPath(brush, path);
                }
                // Dashed border to communicate "click to set"
                using (var path2 = RoundedPanel.RoundedRect(rect, 12))
                using (var pen = new Pen(Color.FromArgb(120, 200, 200, 200), 1.4f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash, DashPattern = new float[] { 3f, 3f } })
                {
                    g.DrawPath(pen, path2);
                }
                // Centred "+" glyph
                var plusColor = Color.FromArgb(170, 220, 220, 220);
                using (var pen = new Pen(plusColor, 1.6f))
                {
                    int cx = Width / 2;
                    int cy = Height / 2;
                    int arm = Math.Min(Width, Height) / 6;
                    g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
                    g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
                }
                if (IsActive)
                {
                    using (var path3 = RoundedPanel.RoundedRect(rect, 12))
                    using (var pen = new Pen(SwatchTheme.Accent2, 2))
                    {
                        g.DrawPath(pen, path3);
                    }
                }
                return;
            }

            using (var path = RoundedPanel.RoundedRect(rect, 12))
            using (var brush = new LinearGradientBrush(rect, SwatchTheme.Accent2, SwatchTheme.Panel2, 135f))
            {
                g.FillPath(brush, path);
            }
            if (IsActive)
            {
                using (var path2 = RoundedPanel.RoundedRect(rect, 12))
                using (var pen = new Pen(SwatchTheme.Accent2, 2))
                {
                    g.DrawPath(pen, path2);
                }
            }
            else
            {
                using (var path2 = RoundedPanel.RoundedRect(rect, 12))
                using (var pen = new Pen(Color.FromArgb(36, 255, 255, 255), 1))
                {
                    g.DrawPath(pen, path2);
                }
            }
        }
    }

    internal enum BadgeKind { Neutral, Ok, Warn, Bad }

    internal sealed class CheckGridRow : RoundedPanel
    {
        private readonly BadgePill badgeControl;
        private readonly Font labelFont = new Font("Consolas", 9.5f);
        private string labelText;
        private Color dotColor = Color.FromArgb(140, 140, 140);

        public CheckGridRow(string label, string badge, BadgeKind kind)
        {
            CornerRadius = 14;
            BorderWidth = 1;
            GradientTopOverride = Color.FromArgb(36, 0, 0, 0);
            GradientBottomOverride = Color.FromArgb(58, 0, 0, 0);
            BorderColor = Color.FromArgb(28, 255, 255, 255);
            Padding = new Padding(11, 0, 11, 0);
            Height = 38;
            DoubleBuffered = true;
            labelText = label;

            // Badge is the only child control - everything else is painted in OnPaint so the dot
            // and label always sit on the row's exact vertical centre, regardless of font metrics.
            badgeControl = new BadgePill
            {
                Width = 78,
                Height = 22,
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            // Position badge against the right edge, vertically centred.
            badgeControl.Location = new Point(0, 0); // updated in OnLayout
            Controls.Add(badgeControl);

            SetState(badge, kind);
            Resize += (s, e) => PositionBadge();
            PositionBadge();
        }

        private void PositionBadge()
        {
            int x = Width - Padding.Right - badgeControl.Width;
            int y = (Height - badgeControl.Height) / 2;
            badgeControl.Location = new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); // draw the rounded background
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int dotSize = 12;
            int dotX = Padding.Left;
            int dotY = (Height - dotSize) / 2;
            // Glow + filled dot.
            using (var glow = new SolidBrush(Color.FromArgb(80, dotColor)))
                g.FillEllipse(glow, dotX - 1, dotY - 1, dotSize + 2, dotSize + 2);
            using (var b = new SolidBrush(dotColor))
                g.FillEllipse(b, dotX + 1, dotY + 1, dotSize - 2, dotSize - 2);

            // Label - painted at the row's exact vertical centre so it always lines up with the dot.
            int textLeft = dotX + dotSize + 8;
            int textRight = badgeControl.Left - 6;
            var textRect = new Rectangle(textLeft, 0, Math.Max(20, textRight - textLeft), Height);
            TextRenderer.DrawText(g, labelText ?? "", labelFont, textRect,
                Color.FromArgb(244, 234, 209),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        public void SetState(string text, BadgeKind kind)
        {
            badgeControl.SetState(text, kind);
            switch (kind)
            {
                case BadgeKind.Ok: dotColor = Color.FromArgb(101, 197, 134); break;
                case BadgeKind.Warn: dotColor = Color.FromArgb(216, 166, 64); break;
                case BadgeKind.Bad: dotColor = Color.FromArgb(216, 92, 76); break;
                default: dotColor = Color.FromArgb(140, 140, 140); break;
            }
            Invalidate();
        }
    }

    internal sealed class BadgePill : Control
    {
        private string text = "";
        private BadgeKind kind = BadgeKind.Neutral;

        public BadgePill()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Consolas", 8.5f, FontStyle.Bold);
            Height = 22;
        }

        public void SetState(string newText, BadgeKind newKind)
        {
            text = newText;
            kind = newKind;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            Color fill, fg;
            switch (kind)
            {
                case BadgeKind.Ok: fill = Color.FromArgb(101, 197, 134); fg = Color.FromArgb(8, 19, 13); break;
                case BadgeKind.Warn: fill = Color.FromArgb(216, 166, 64); fg = Color.FromArgb(24, 16, 8); break;
                case BadgeKind.Bad: fill = Color.FromArgb(216, 92, 76); fg = Color.White; break;
                default: fill = Color.FromArgb(40, 255, 255, 255); fg = Color.FromArgb(169, 157, 124); break;
            }
            using (var path = RoundedPanel.RoundedRect(rect, Math.Max(0, Height / 2)))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
            }
            var ts = TextRenderer.MeasureText(g, text ?? "", Font);
            var x = (Width - ts.Width) / 2;
            var y = (Height - ts.Height) / 2;
            TextRenderer.DrawText(g, text ?? "", Font, new Point(x, y), fg, TextFormatFlags.NoPadding);
        }
    }

    internal sealed class DotPanel : Panel
    {
        public Color Color { get; set; } = System.Drawing.Color.FromArgb(101, 197, 134);

        public DotPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = System.Drawing.Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var glow = new SolidBrush(System.Drawing.Color.FromArgb(80, Color)))
                g.FillEllipse(glow, -1, -1, Width + 2, Height + 2);
            using (var b = new SolidBrush(Color))
                g.FillEllipse(b, 1, 1, Width - 2, Height - 2);
        }
    }
}

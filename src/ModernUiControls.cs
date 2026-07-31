using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexRouterSwitch
{
    internal enum ModernButtonKind
    {
        Primary,
        Secondary,
        Ghost
    }

    internal static class ModernUi
    {
        public static readonly Color WindowBorder = Color.FromArgb(195, 207, 222);
        public static readonly Color TitleBar = Color.FromArgb(245, 249, 255);
        public static readonly Color Canvas = Color.FromArgb(255, 255, 255);
        public static readonly Color Navigation = Color.FromArgb(248, 250, 253);
        public static readonly Color Footer = Color.FromArgb(251, 252, 254);
        public static readonly Color Divider = Color.FromArgb(226, 232, 240);
        public static readonly Color Text = Color.FromArgb(25, 33, 46);
        public static readonly Color MutedText = Color.FromArgb(88, 99, 116);
        public static readonly Color Primary = Color.FromArgb(15, 108, 189);
        public static readonly Color PrimaryHover = Color.FromArgb(17, 94, 163);
        public static readonly Color PrimaryPressed = Color.FromArgb(12, 74, 130);
        public static readonly Color PrimarySoft = Color.FromArgb(237, 246, 255);
        public static readonly Color PrimaryBorder = Color.FromArgb(167, 207, 244);
        public static readonly Color Success = Color.FromArgb(16, 124, 55);
        public static readonly Color Warning = Color.FromArgb(157, 90, 0);
        public static readonly Color Error = Color.FromArgb(196, 43, 28);

        public static Font CreateIconFont(float size)
        {
            return new Font(
                "Segoe MDL2 Assets",
                size,
                FontStyle.Regular,
                GraphicsUnit.Point
            );
        }

        public static GraphicsPath CreateRoundedRectangle(
            Rectangle bounds,
            int radius
        )
        {
            int safeRadius = Math.Max(1, radius);
            int diameter = Math.Min(
                safeRadius * 2,
                Math.Min(bounds.Width, bounds.Height)
            );
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 2)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(
                bounds.Right - diameter,
                bounds.Top,
                diameter,
                diameter,
                270,
                90
            );
            path.AddArc(
                bounds.Right - diameter,
                bounds.Bottom - diameter,
                diameter,
                diameter,
                0,
                90
            );
            path.AddArc(
                bounds.Left,
                bounds.Bottom - diameter,
                diameter,
                diameter,
                90,
                90
            );
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ModernButton : Button
    {
        private bool hovering;
        private bool pressed;
        private ModernButtonKind kind;
        private string iconGlyph = "";
        private int cornerRadius = 9;
        private bool dangerOnHover;

        public ModernButtonKind Kind
        {
            get { return kind; }
            set
            {
                kind = value;
                Invalidate();
            }
        }

        public string IconGlyph
        {
            get { return iconGlyph; }
            set
            {
                iconGlyph = value ?? "";
                Invalidate();
            }
        }

        public int CornerRadius
        {
            get { return cornerRadius; }
            set
            {
                cornerRadius = Math.Max(1, value);
                Invalidate();
            }
        }

        public bool DangerOnHover
        {
            get { return dangerOnHover; }
            set
            {
                dangerOnHover = value;
                Invalidate();
            }
        }

        public ModernButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint |
                ControlStyles.SupportsTransparentBackColor,
                true
            );
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            ForeColor = ModernUi.Text;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovering = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovering = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (Enabled && mevent.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            Color fill;
            Color border;
            Color foreground;
            ResolveColors(out fill, out border, out foreground);

            using (GraphicsPath path = ModernUi.CreateRoundedRectangle(
                bounds,
                cornerRadius
            ))
            using (SolidBrush fillBrush = new SolidBrush(fill))
            using (Pen borderPen = new Pen(border))
            {
                pevent.Graphics.FillPath(fillBrush, path);
                if (border.A > 0)
                {
                    pevent.Graphics.DrawPath(borderPen, path);
                }
            }

            DrawContent(pevent.Graphics, foreground);

            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = Rectangle.Inflate(bounds, -3, -3);
                using (GraphicsPath focusPath = ModernUi.CreateRoundedRectangle(
                    focusBounds,
                    Math.Max(2, cornerRadius - 2)
                ))
                using (Pen focusPen = new Pen(
                    kind == ModernButtonKind.Primary
                        ? Color.White
                        : ModernUi.Primary
                ))
                {
                    focusPen.DashStyle = DashStyle.Dot;
                    pevent.Graphics.DrawPath(focusPen, focusPath);
                }
            }
        }

        private void ResolveColors(
            out Color fill,
            out Color border,
            out Color foreground
        )
        {
            if (!Enabled)
            {
                fill = Color.FromArgb(241, 244, 248);
                border = Color.FromArgb(221, 226, 233);
                foreground = Color.FromArgb(150, 158, 170);
                return;
            }

            if (dangerOnHover && hovering)
            {
                fill = Color.FromArgb(196, 43, 28);
                border = fill;
                foreground = Color.White;
                return;
            }

            if (kind == ModernButtonKind.Primary)
            {
                fill = pressed
                    ? ModernUi.PrimaryPressed
                    : (hovering ? ModernUi.PrimaryHover : ModernUi.Primary);
                border = fill;
                foreground = Color.White;
                return;
            }

            if (kind == ModernButtonKind.Ghost)
            {
                fill = pressed
                    ? Color.FromArgb(222, 232, 245)
                    : (hovering
                        ? Color.FromArgb(233, 241, 251)
                        : Color.Transparent);
                border = Color.Transparent;
                foreground = ModernUi.Text;
                return;
            }

            fill = pressed
                ? Color.FromArgb(232, 238, 246)
                : (hovering ? Color.FromArgb(246, 249, 253) : Color.White);
            border = hovering
                ? Color.FromArgb(153, 177, 207)
                : Color.FromArgb(199, 210, 225);
            foreground = ModernUi.Text;
        }

        private void DrawContent(Graphics graphics, Color foreground)
        {
            bool hasIcon = !String.IsNullOrEmpty(iconGlyph);
            bool hasText = !String.IsNullOrEmpty(Text);
            Size textSize = hasText
                ? TextRenderer.MeasureText(
                    graphics,
                    Text,
                    Font,
                    new Size(Int32.MaxValue, Height),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                )
                : Size.Empty;
            int iconWidth = hasIcon ? 20 : 0;
            int gap = hasIcon && hasText ? 9 : 0;
            int contentWidth = iconWidth + gap + textSize.Width;
            int startX = Math.Max(8, (Width - contentWidth) / 2);

            if (hasIcon)
            {
                using (Font iconFont = ModernUi.CreateIconFont(13.5F))
                {
                    Rectangle iconBounds = new Rectangle(
                        startX,
                        0,
                        iconWidth,
                        Height
                    );
                    TextRenderer.DrawText(
                        graphics,
                        iconGlyph,
                        iconFont,
                        iconBounds,
                        foreground,
                        TextFormatFlags.NoPadding |
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine
                    );
                }
            }

            if (hasText)
            {
                Rectangle textBounds = new Rectangle(
                    startX + iconWidth + gap,
                    0,
                    Math.Max(1, Width - startX - iconWidth - gap - 8),
                    Height
                );
                TextRenderer.DrawText(
                    graphics,
                    Text,
                    Font,
                    textBounds,
                    foreground,
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis
                );
            }
        }
    }

    internal sealed class ModeOption : RadioButton
    {
        private bool hovering;
        private string description = "";
        private string iconGlyph = "";

        public string Description
        {
            get { return description; }
            set
            {
                description = value ?? "";
                Invalidate();
            }
        }

        public string IconGlyph
        {
            get { return iconGlyph; }
            set
            {
                iconGlyph = value ?? "";
                Invalidate();
            }
        }

        public ModeOption()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint |
                ControlStyles.SupportsTransparentBackColor,
                true
            );
            Appearance = Appearance.Button;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            ForeColor = ModernUi.Text;
            Cursor = Cursors.Hand;
            TextAlign = ContentAlignment.MiddleLeft;
            AccessibleRole = AccessibleRole.RadioButton;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovering = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovering = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Invalidate();
            base.OnCheckedChanged(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            Color fill = Checked
                ? ModernUi.PrimarySoft
                : (hovering ? Color.FromArgb(252, 253, 255) : Color.White);
            Color border = Checked
                ? ModernUi.PrimaryBorder
                : (hovering
                    ? Color.FromArgb(179, 195, 215)
                    : Color.FromArgb(211, 219, 229));
            Color titleColor = Enabled
                ? ModernUi.Text
                : Color.FromArgb(146, 154, 166);
            Color detailColor = Enabled
                ? ModernUi.MutedText
                : Color.FromArgb(164, 171, 181);
            Color iconColor = Checked
                ? ModernUi.Primary
                : Color.FromArgb(74, 87, 105);

            using (GraphicsPath path = ModernUi.CreateRoundedRectangle(bounds, 11))
            using (SolidBrush fillBrush = new SolidBrush(fill))
            using (Pen borderPen = new Pen(border))
            {
                pevent.Graphics.FillPath(fillBrush, path);
                pevent.Graphics.DrawPath(borderPen, path);
            }

            if (Checked)
            {
                Rectangle stripe = new Rectangle(
                    bounds.Left,
                    bounds.Top + 10,
                    4,
                    bounds.Height - 20
                );
                using (GraphicsPath stripePath = ModernUi.CreateRoundedRectangle(
                    stripe,
                    2
                ))
                using (SolidBrush stripeBrush = new SolidBrush(ModernUi.Primary))
                {
                    pevent.Graphics.FillPath(stripeBrush, stripePath);
                }
            }

            using (Font iconFont = ModernUi.CreateIconFont(25F))
            {
                Rectangle iconBounds = new Rectangle(
                    bounds.Left + 18,
                    bounds.Top + 24,
                    38,
                    42
                );
                TextRenderer.DrawText(
                    pevent.Graphics,
                    iconGlyph,
                    iconFont,
                    iconBounds,
                    iconColor,
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine
                );
            }

            Rectangle titleBounds = new Rectangle(
                bounds.Left + 72,
                bounds.Top + 20,
                Math.Max(20, bounds.Width - 96),
                28
            );
            TextRenderer.DrawText(
                pevent.Graphics,
                Text,
                Font,
                titleBounds,
                titleColor,
                TextFormatFlags.NoPadding |
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis
            );

            using (Font detailFont = new Font(
                Font.FontFamily,
                Math.Max(8.5F, Font.Size - 1F),
                FontStyle.Regular,
                GraphicsUnit.Point
            ))
            {
                Rectangle detailBounds = new Rectangle(
                    bounds.Left + 72,
                    bounds.Top + 50,
                    Math.Max(20, bounds.Width - 94),
                    Math.Max(28, bounds.Height - 60)
                );
                TextRenderer.DrawText(
                    pevent.Graphics,
                    description,
                    detailFont,
                    detailBounds,
                    detailColor,
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.Left |
                    TextFormatFlags.Top |
                    TextFormatFlags.WordBreak |
                    TextFormatFlags.EndEllipsis
                );
            }

            if (Checked)
            {
                Rectangle checkBounds = new Rectangle(
                    bounds.Right - 32,
                    bounds.Bottom - 32,
                    22,
                    22
                );
                using (SolidBrush checkBrush = new SolidBrush(ModernUi.Primary))
                {
                    pevent.Graphics.FillEllipse(checkBrush, checkBounds);
                }
                using (Font checkFont = ModernUi.CreateIconFont(10F))
                {
                    TextRenderer.DrawText(
                        pevent.Graphics,
                        "\uE73E",
                        checkFont,
                        checkBounds,
                        Color.White,
                        TextFormatFlags.NoPadding |
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine
                    );
                }
            }

            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = Rectangle.Inflate(bounds, -4, -4);
                using (GraphicsPath focusPath = ModernUi.CreateRoundedRectangle(
                    focusBounds,
                    8
                ))
                using (Pen focusPen = new Pen(ModernUi.Primary))
                {
                    focusPen.DashStyle = DashStyle.Dot;
                    pevent.Graphics.DrawPath(focusPen, focusPath);
                }
            }
        }
    }

    internal sealed class IconLabel : Control
    {
        private string iconGlyph = "";
        private Color iconColor = ModernUi.MutedText;
        private float iconSize = 14F;

        public string IconGlyph
        {
            get { return iconGlyph; }
            set
            {
                iconGlyph = value ?? "";
                Invalidate();
            }
        }

        public Color IconColor
        {
            get { return iconColor; }
            set
            {
                iconColor = value;
                Invalidate();
            }
        }

        public float IconSize
        {
            get { return iconSize; }
            set
            {
                iconSize = Math.Max(8F, value);
                Invalidate();
            }
        }

        public IconLabel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint |
                ControlStyles.SupportsTransparentBackColor,
                true
            );
            BackColor = Color.Transparent;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font iconFont = ModernUi.CreateIconFont(iconSize))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    iconGlyph,
                    iconFont,
                    ClientRectangle,
                    iconColor,
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine
                );
            }
        }
    }

    internal sealed class SeparatorControl : Control
    {
        public SeparatorControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true
            );
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(ModernUi.Divider);
        }
    }
}

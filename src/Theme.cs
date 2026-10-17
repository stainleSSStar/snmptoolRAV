using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnmptoolRAV {
    static class Theme {
        public static readonly bool Contrast = SystemInformation.HighContrast;
        public static readonly Color Background = Contrast ? SystemColors.Control : Color.FromArgb(245,246,250);
        public static readonly Color Surface = Contrast ? SystemColors.Window : Color.White;
        public static readonly Color Text = Contrast ? SystemColors.WindowText : Color.FromArgb(27,32,48);
        public static readonly Color Muted = Contrast ? SystemColors.WindowText : Color.FromArgb(94,105,125);
        public static readonly Color Border = Contrast ? SystemColors.WindowText : Color.FromArgb(222,226,235);
        public static readonly Color Accent = Contrast ? SystemColors.Highlight : Color.FromArgb(36,95,205);
        public static readonly Color Success = Contrast ? SystemColors.WindowText : Color.FromArgb(27,116,82);
        public static GraphicsPath Round(Rectangle rect, int radius) {
            var path = new GraphicsPath(); int d = radius * 2;
            path.AddArc(rect.X,rect.Y,d,d,180,90); path.AddArc(rect.Right-d,rect.Y,d,d,270,90);
            path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90); path.AddArc(rect.X,rect.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
        }
    }
    sealed class Card : Panel {
        public Card() { DoubleBuffered = true; BackColor = Theme.Surface; Padding = new Padding(20); }
        protected override void OnPaintBackground(PaintEventArgs e) {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new Rectangle(0,0,Width-1,Height-1), 12))
            using (var fill = new SolidBrush(Theme.Surface))
            using (var pen = new Pen(Theme.Border)) { e.Graphics.FillPath(fill,path); e.Graphics.DrawPath(pen,path); }
        }
    }
    sealed class ActionButton : Button {
        readonly bool primary;
        bool hover;
        public ActionButton(string text, bool prominent) {
            Text = text; primary = prominent; Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Font = new Font("Segoe UI",10,FontStyle.Bold); Height = 44;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = primary ? Theme.Accent : Theme.Surface;
            if (hover && Enabled) fill = ControlPaint.Light(fill, primary ? 0.12f : 0.04f);
            if (!Enabled) fill = Theme.Contrast ? SystemColors.Control : Color.FromArgb(232,234,241);
            using (var path = Theme.Round(new Rectangle(0,0,Width-1,Height-1),8))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(primary ? fill : Theme.Border)) { e.Graphics.FillPath(brush,path); e.Graphics.DrawPath(pen,path); }
            Color ink = !Enabled ? SystemColors.GrayText : primary ? (Theme.Contrast ? SystemColors.HighlightText : Color.White) : Theme.Text;
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ink,TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),ink,fill);
        }
    }
    sealed class BrandIcon : Control {
        public BrandIcon() { Size = new Size(48,48); DoubleBuffered = true; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Round(new Rectangle(0,0,Width-1,Height-1),12))
            using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillPath(b,p);
            float scale = Width / 48f;
            e.Graphics.ScaleTransform(scale,scale);
            using (var pen = new Pen(Theme.Contrast ? SystemColors.HighlightText : Color.White,2.2f)) {
                e.Graphics.DrawLine(pen,24,12,24,29); e.Graphics.DrawLine(pen,18,23,24,29); e.Graphics.DrawLine(pen,24,29,30,23);
                e.Graphics.DrawLines(pen,new[] {new Point(13,29),new Point(13,35),new Point(35,35),new Point(35,29)});
            }
        }
    }
}

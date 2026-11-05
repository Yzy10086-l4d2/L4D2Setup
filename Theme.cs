using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Installer
{
    /// <summary>
    /// 视觉 token。所有颜色/字号/间距/圆角只在这里定义一次，
    /// 界面代码只引用，不再写字面量。
    /// </summary>
    internal static class Theme
    {
        // 表面
        public static readonly Color Bg = Color.FromArgb(243, 245, 247);   // 窗口底
        public static readonly Color Surface = Color.FromArgb(255, 255, 255); // 卡片
        public static readonly Color SurfaceAlt = Color.FromArgb(250, 251, 252);
        public static readonly Color BtnHover = Color.FromArgb(245, 247, 249);
        public static readonly Color BtnDown = Color.FromArgb(236, 239, 243);

        // 结构线（只用于分割与状态，不做装饰）
        public static readonly Color Line = Color.FromArgb(226, 230, 235);
        public static readonly Color LineSoft = Color.FromArgb(238, 241, 244);

        // 文字三级
        public static readonly Color Ink = Color.FromArgb(28, 32, 36);    // 正文 / 值
        public static readonly Color Ink2 = Color.FromArgb(90, 98, 107);  // 标签 / 次级
        public static readonly Color Ink3 = Color.FromArgb(102, 112, 122); // 提示 / 占位（白底约 5.0:1）

        // 强调：只给主操作和一个危险信号。取自图标里的那支血红。
        public static readonly Color Accent = Color.FromArgb(179, 38, 30);
        public static readonly Color AccentHover = Color.FromArgb(156, 31, 25);
        public static readonly Color AccentDown = Color.FromArgb(135, 26, 21);
        public static readonly Color Warn = Color.FromArgb(169, 97, 0);
        public static readonly Color Ok = Color.FromArgb(46, 125, 50);
        // 信息蓝：表示「这里有可叠加的东西」，中性提示，不用红——红容易让人误以为装冲突了。
        public static readonly Color Info = Color.FromArgb(2, 119, 189);

        // 列表行的状态底色：一眼扫过去就能分清「装没装」，不用逐行读字
        public static readonly Color RowOn = Color.FromArgb(238, 247, 238);    // 已装·开（淡绿）
        public static readonly Color RowOff = Color.FromArgb(253, 246, 231);   // 已装·关（淡琥珀）
        public static readonly Color RowGame = Color.FromArgb(240, 246, 252);  // 游戏里本来就有（淡蓝）

        // 字体：一个族 + 固定角色刻度，不换身份
        public static readonly Font UI = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font UIBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font UISmall = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Mono = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);

        // 栅格：16 外边距 / 12 栏间距 / 8 行内步长
        public const int M = 16;
        public const int G = 12;
        public const int S = 8;
        public const int H = 28;   // 普通控件高
        public const int HP = 36;  // 主操作高
        public const int R = 6;    // 控件圆角

        /// <summary>只在颜色变化上做很短的过渡；系统开启高对比度或用户选择减少动效时直接切到终态。</summary>
        public static bool MotionEnabled
        {
            get
            {
                try { return !Core.Cfg.ReduceMotion && SystemInformation.UIEffectsEnabled && !SystemInformation.HighContrast; }
                catch { return false; }
            }
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>输入类控件统一外观：白底、一条细线、内边距靠系统。</summary>
        public static void Dress(Control c)
        {
            c.Font = UI;
            if (c is TextBox)
            {
                TextBox t = (TextBox)c;
                t.BorderStyle = BorderStyle.FixedSingle;
                t.BackColor = Surface;
                t.ForeColor = Ink;
            }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c;
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Surface;
                cb.ForeColor = Ink;
            }
            else if (c is CheckBox)
            {
                c.BackColor = Color.Transparent;
                c.ForeColor = Ink;
            }
            else if (c is Label)
            {
                c.BackColor = Color.Transparent;
                c.ForeColor = Ink2;
            }
        }
    }

    /// <summary>
    /// 平面按钮。两种角色：Primary（实心强调色，每屏只给一个）/ 其余（白底细线）。
    /// 状态齐全：normal / hover / down / disabled / focus。
    /// </summary>
    internal class FlatButton : Button
    {
        public bool Primary;
        private bool hover, down;
        private readonly Timer anim;
        private Color shownFace;
        private Color animFrom, animTo;
        private float animT = 1f;
        private bool faceReady;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Theme.UI;
            Cursor = Cursors.Hand;
            BackColor = Theme.Surface;
            UseVisualStyleBackColor = false;

            // 83–120ms 的轻量颜色反馈，不位移、不缩放；按钮按下仍然立即变色。
            anim = new Timer();
            anim.Interval = 15;
            anim.Tick += delegate
            {
                animT = Math.Min(1f, animT + 0.16f);
                float eased = 1f - (1f - animT) * (1f - animT);
                shownFace = Mix(animFrom, animTo, eased);
                if (animT >= 1f) anim.Stop();
                Invalidate();
            };
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; AnimateFace(); Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; AnimateFace(); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; SetFace(TargetFace()); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; AnimateFace(); Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { SetFace(TargetFace()); Invalidate(); base.OnEnabledChanged(e); }

        private Color TargetFace()
        {
            if (Primary)
                return !Enabled ? Color.FromArgb(203, 187, 186)
                     : (down ? Theme.AccentDown : (hover ? Theme.AccentHover : Theme.Accent));
            return !Enabled ? Theme.SurfaceAlt
                 : (down ? Theme.BtnDown : (hover ? Theme.BtnHover : Theme.Surface));
        }

        private void AnimateFace()
        {
            Color target = TargetFace();
            if (!faceReady || !Theme.MotionEnabled || !IsHandleCreated)
            {
                SetFace(target);
                return;
            }
            if (shownFace.ToArgb() == target.ToArgb()) return;
            anim.Stop();
            animFrom = shownFace;
            animTo = target;
            animT = 0f;
            anim.Start();
        }

        private void SetFace(Color c)
        {
            anim.Stop();
            animT = 1f;
            shownFace = c;
            faceReady = true;
        }

        private static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && anim != null) anim.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);

            Color ink, edge;
            if (!faceReady) SetFace(TargetFace());
            if (!Theme.MotionEnabled && shownFace.ToArgb() != TargetFace().ToArgb()) SetFace(TargetFace());
            Color face = shownFace;
            if (Primary)
            {
                ink = Color.White;
                edge = face;
            }
            else
            {
                ink = Enabled ? Theme.Ink : Theme.Ink3;
                edge = Theme.Line;
            }

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(r, Theme.R))
            {
                using (SolidBrush b = new SolidBrush(face)) g.FillPath(b, p);
                using (Pen pen = new Pen(edge)) g.DrawPath(pen, p);
            }

            TextRenderer.DrawText(g, Text, Font, r, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, new Rectangle(3, 3, Width - 7, Height - 7), ink, face);
        }
    }

    /// <summary>
    /// 区块：白卡片 + 标题 + 标题下一条细线。替代 GroupBox 的四边框，
    /// 同屏不再有三层描边套娃。子控件坐标相对本面板，规则同 GroupBox。
    /// </summary>
    internal class Section : Panel
    {
        private string caption = "";
        public string Caption
        {
            get { return caption; }
            set { caption = value == null ? "" : value; Invalidate(); }
        }

        public Section()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Font = Theme.UI;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            using (Pen pen = new Pen(Theme.LineSoft))
                g.DrawLine(pen, 0, 22, Width, 22);
            TextRenderer.DrawText(g, caption, Theme.UIBold, new Rectangle(0, 0, Width, 22), Theme.Ink2,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

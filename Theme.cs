using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Installer
{
    internal enum ThemeMode { System = 0, Light = 1, Dark = 2 }

    /// <summary>
    /// 一套配色。浅色、深色各一份，字段同名，界面代码只认 Theme.Xxx，不关心当前是哪套。
    /// 深色取「近黑画布 + 少量亮度台阶」的做法：层级靠 1px 线和 2% 的明度差，
    /// 不靠阴影；红色只留给错误与破坏性操作。
    /// </summary>
    internal sealed class Palette
    {
        public bool IsDark;
        public Color Bg, Surface, SurfaceAlt, BtnHover, BtnDown;
        public Color Chip, ChipHover, ChipDown;
        public Color Line, LineSoft;
        public Color Ink, Ink2, Ink3;
        public Color Primary, PrimaryHover, PrimaryDown, PrimaryInk;   // 主操作：浅色近黑，深色近白
        public Color Mark;                                             // 非破坏性强调：选中条、状态点、开关
        public Color Accent, AccentHover, AccentDown, DangerHover, DangerDown;
        public Color Warn, Ok, Info;
        public Color RowOn, RowOff, RowGame;
        public Color CardEdge, SegTrack, SegEdge, TrackOff, ThumbOn;
        public Color TreeBg, TreeSel, TreeHover, TreeBranch;   // 左侧方案树

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static readonly Palette Light = new Palette
        {
            IsDark = false,
            Bg = C(243, 245, 247),
            Surface = C(255, 255, 255),
            SurfaceAlt = C(250, 251, 252),
            BtnHover = C(245, 247, 249),
            BtnDown = C(236, 239, 243),
            Chip = C(242, 243, 246),
            ChipHover = C(234, 235, 240),
            ChipDown = C(225, 227, 233),
            Line = C(226, 230, 235),
            LineSoft = C(238, 241, 244),
            Ink = C(28, 32, 36),
            Ink2 = C(90, 98, 107),
            Ink3 = C(102, 112, 122),
            Primary = C(28, 32, 36),
            PrimaryHover = C(48, 54, 61),
            PrimaryDown = C(12, 15, 18),
            PrimaryInk = C(255, 255, 255),
            Mark = C(28, 32, 36),
            Accent = C(179, 38, 30),
            AccentHover = C(156, 31, 25),
            AccentDown = C(135, 26, 21),
            DangerHover = C(255, 244, 243),
            DangerDown = C(250, 235, 234),
            Warn = C(169, 97, 0),
            Ok = C(46, 125, 50),
            Info = C(2, 119, 189),
            RowOn = C(238, 247, 238),
            RowOff = C(253, 246, 231),
            RowGame = C(240, 246, 252),
            CardEdge = C(232, 233, 238),
            SegTrack = C(239, 239, 242),
            SegEdge = C(222, 222, 228),
            TrackOff = C(233, 233, 235),
            ThumbOn = C(255, 255, 255),
            TreeBg = C(247, 249, 251),
            TreeSel = C(240, 243, 247),
            TreeHover = C(238, 242, 246),
            TreeBranch = C(198, 206, 215),
        };

        public static readonly Palette Dark = new Palette
        {
            IsDark = true,
            Bg = C(18, 20, 23),
            Surface = C(30, 33, 38),
            SurfaceAlt = C(36, 39, 45),
            BtnHover = C(42, 46, 53),
            BtnDown = C(50, 55, 63),
            Chip = C(42, 46, 53),
            ChipHover = C(51, 56, 65),
            ChipDown = C(59, 65, 75),
            Line = C(51, 56, 64),
            LineSoft = C(42, 46, 53),
            Ink = C(236, 238, 241),
            Ink2 = C(167, 174, 184),
            Ink3 = C(139, 147, 158),      // 对卡片约 5.2:1，仍过 WCAG AA
            Primary = C(236, 238, 241),
            PrimaryHover = C(255, 255, 255),
            PrimaryDown = C(203, 208, 214),
            PrimaryInk = C(18, 20, 23),
            Mark = C(236, 238, 241),
            Accent = C(240, 112, 106),    // 深底上把红提亮，小字也读得清
            AccentHover = C(248, 132, 126),
            AccentDown = C(214, 88, 82),
            DangerHover = C(58, 36, 34),
            DangerDown = C(69, 41, 38),
            Warn = C(224, 163, 58),
            Ok = C(111, 191, 115),
            Info = C(88, 166, 255),
            RowOn = C(27, 42, 31),
            RowOff = C(44, 39, 24),
            RowGame = C(27, 38, 50),
            CardEdge = C(44, 48, 55),
            SegTrack = C(36, 39, 45),
            SegEdge = C(54, 59, 67),
            TrackOff = C(58, 63, 71),
            ThumbOn = C(22, 25, 29),
        
            TreeBg = C(24, 26, 30),
            TreeSel = C(48, 52, 60),
            TreeHover = C(38, 41, 47),
            TreeBranch = C(72, 78, 88),
        };
    }

    /// <summary>
    /// 视觉 token。所有颜色/字号/间距/圆角只在这里定义一次，
    /// 界面代码只引用，不再写字面量。
    /// </summary>
    internal static class Theme
    {
        // ---- 调色板：浅 / 深两套。字段名一个不改，界面代码照旧写 Theme.Bg ----
        private static Palette pal = Palette.Light;
        private static Palette prevPal = Palette.Light;
        private static ThemeMode mode = ThemeMode.System;
        private static Color[] mapFrom = new Color[0];
        private static Color[] mapTo = new Color[0];

        // 表面
        public static Color Bg { get { return pal.Bg; } }                    // 窗口底
        public static Color Surface { get { return pal.Surface; } }          // 卡片
        public static Color SurfaceAlt { get { return pal.SurfaceAlt; } }
        public static Color BtnHover { get { return pal.BtnHover; } }
        public static Color BtnDown { get { return pal.BtnDown; } }
        // 浅灰胶囊按钮（iOS 的 gray tinted 次要操作）
        public static Color Chip { get { return pal.Chip; } }
        public static Color ChipHover { get { return pal.ChipHover; } }
        public static Color ChipDown { get { return pal.ChipDown; } }

        // 结构线（只用于分割与状态，不做装饰）
        public static Color Line { get { return pal.Line; } }
        public static Color LineSoft { get { return pal.LineSoft; } }

        // 文字三级
        public static Color Ink { get { return pal.Ink; } }      // 正文 / 值
        public static Color Ink2 { get { return pal.Ink2; } }    // 标签 / 次级
        public static Color Ink3 { get { return pal.Ink3; } }    // 提示 / 占位

        // 主操作：近黑（深色主题下自动反过来变近白），不再用品牌红
        public static Color Primary { get { return pal.Primary; } }
        public static Color PrimaryHover { get { return pal.PrimaryHover; } }
        public static Color PrimaryDown { get { return pal.PrimaryDown; } }
        public static Color PrimaryInk { get { return pal.PrimaryInk; } }
        // 非破坏性的强调：选中条、状态点、开关。「重要但没危险」都用它
        public static Color Mark { get { return pal.Mark; } }

        // 强调：只给错误提示与破坏性操作（一键还原、删除）
        public static Color Accent { get { return pal.Accent; } }
        public static Color AccentHover { get { return pal.AccentHover; } }
        public static Color AccentDown { get { return pal.AccentDown; } }
        public static Color DangerHover { get { return pal.DangerHover; } }
        public static Color DangerDown { get { return pal.DangerDown; } }
        public static Color Warn { get { return pal.Warn; } }
        public static Color Ok { get { return pal.Ok; } }
        // 信息蓝：表示「这里有可叠加的东西」，中性提示，不用红
        public static Color Info { get { return pal.Info; } }

        // 列表行的状态底色：一眼扫过去就能分清「装没装」，不用逐行读字
        public static Color TreeBg { get { return pal.TreeBg; } }
        public static Color TreeSel { get { return pal.TreeSel; } }
        public static Color TreeHover { get { return pal.TreeHover; } }
        public static Color TreeBranch { get { return pal.TreeBranch; } }

        public static Color RowOn { get { return pal.RowOn; } }      // 已装·开
        public static Color RowOff { get { return pal.RowOff; } }    // 已装·关
        public static Color RowGame { get { return pal.RowGame; } }  // 游戏里本来就有

        public static bool IsDark { get { return pal.IsDark; } }
        public static ThemeMode Mode { get { return mode; } }

        /// <summary>启动时读配置 + 系统偏好。必须在任何窗口创建之前调用。</summary>
        public static void Init()
        {
            mode = Parse(Core.Cfg == null ? "" : Core.Cfg.Appearance);
            pal = Resolve(mode);
            prevPal = pal;
            Native.SetAppMode(pal.IsDark);
        }

        public static ThemeMode Parse(string s)
        {
            if (string.Equals(s, "dark", StringComparison.OrdinalIgnoreCase)) return ThemeMode.Dark;
            if (string.Equals(s, "light", StringComparison.OrdinalIgnoreCase)) return ThemeMode.Light;
            return ThemeMode.System;
        }

        public static string ModeKey
        {
            get { return mode == ThemeMode.Dark ? "dark" : (mode == ThemeMode.Light ? "light" : "system"); }
        }

        public static string ModeLabel
        {
            get { return mode == ThemeMode.Dark ? "深色" : (mode == ThemeMode.Light ? "浅色" : "跟随系统"); }
        }

        private static Palette Resolve(ThemeMode m)
        {
            if (m == ThemeMode.Dark) return Palette.Dark;
            if (m == ThemeMode.Light) return Palette.Light;
            return SystemPrefersDark() ? Palette.Dark : Palette.Light;
        }

        /// <summary>系统「应用模式」是深色吗。读不到就当浅色，不让主题拖垮启动。</summary>
        public static bool SystemPrefersDark()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int) return (int)v == 0;
                }
            }
            catch { }
            return false;
        }

        /// <summary>只切调色板并记下旧的一套（给 Remap 用）。写配置由调用方负责。</summary>
        public static bool SetMode(ThemeMode m)
        {
            ThemeMode old = mode;
            mode = m;
            Palette next = Resolve(m);
            if (ReferenceEquals(next, pal)) return old != m;
            prevPal = pal;
            pal = next;
            BuildMap();
            Native.SetAppMode(pal.IsDark);
            return old != m;
        }

        /// <summary>把旧调色板里的每个色对应到新的一套，界面上的字色/底色就能整体迁移。</summary>
        private static void BuildMap()
        {
            Color[] a = new Color[40];
            Color[] b = new Color[40];
            int n = 0;
            Action<Color, Color> add = delegate(Color x, Color y)
            {
                if (x.ToArgb() == y.ToArgb()) return;
                for (int i = 0; i < n; i++) if (a[i].ToArgb() == x.ToArgb()) return;   // 同色只映射一次
                a[n] = x; b[n] = y; n++;
            };
            add(prevPal.Bg, pal.Bg);
            add(prevPal.Surface, pal.Surface);
            add(prevPal.SurfaceAlt, pal.SurfaceAlt);
            add(prevPal.BtnHover, pal.BtnHover);
            add(prevPal.BtnDown, pal.BtnDown);
            add(prevPal.Chip, pal.Chip);
            add(prevPal.ChipHover, pal.ChipHover);
            add(prevPal.ChipDown, pal.ChipDown);
            add(prevPal.Line, pal.Line);
            add(prevPal.LineSoft, pal.LineSoft);
            add(prevPal.Ink, pal.Ink);
            add(prevPal.Ink2, pal.Ink2);
            add(prevPal.Ink3, pal.Ink3);
            add(prevPal.Primary, pal.Primary);
            add(prevPal.PrimaryHover, pal.PrimaryHover);
            add(prevPal.PrimaryDown, pal.PrimaryDown);
            add(prevPal.PrimaryInk, pal.PrimaryInk);
            add(prevPal.Mark, pal.Mark);
            add(prevPal.Accent, pal.Accent);
            add(prevPal.AccentHover, pal.AccentHover);
            add(prevPal.AccentDown, pal.AccentDown);
            add(prevPal.DangerHover, pal.DangerHover);
            add(prevPal.DangerDown, pal.DangerDown);
            add(prevPal.Warn, pal.Warn);
            add(prevPal.Ok, pal.Ok);
            add(prevPal.Info, pal.Info);
            add(prevPal.RowOn, pal.RowOn);
            add(prevPal.RowOff, pal.RowOff);
            add(prevPal.RowGame, pal.RowGame);
            add(prevPal.CardEdge, pal.CardEdge);
            add(prevPal.SegTrack, pal.SegTrack);
            add(prevPal.SegEdge, pal.SegEdge);
            add(prevPal.TrackOff, pal.TrackOff);
            add(prevPal.ThumbOn, pal.ThumbOn);
            mapFrom = new Color[n];
            mapTo = new Color[n];
            Array.Copy(a, mapFrom, n);
            Array.Copy(b, mapTo, n);
        }

        private static Color Remap(Color c)
        {
            if (c.A != 255) return c;
            int v = c.ToArgb();
            for (int i = 0; i < mapFrom.Length; i++)
                if (mapFrom[i].ToArgb() == v) return mapTo[i];
            return c;
        }

        /// <summary>递归把已有控件的字色/底色迁移到新调色板。自绘控件重画时会自己读 Theme，不用管。</summary>
        public static void ApplyTo(Control root)
        {
            if (root == null || root.IsDisposed) return;
            try
            {
                if (root.BackColor.A == 255) root.BackColor = Remap(root.BackColor);
                if (root.ForeColor.A == 255) root.ForeColor = Remap(root.ForeColor);
                CatTree t = root as CatTree;
                if (t != null) t.OnThemeChanged();
                root.Invalidate(true);
                for (int i = 0; i < root.Controls.Count; i++) ApplyTo(root.Controls[i]);
            }
            catch { }
        }

        /// <summary>把当前主题套到所有已打开的窗口上（设置页里切换外观时用）。</summary>
        public static void ApplyToOpenForms()
        {
            FormCollection fs = Application.OpenForms;
            for (int i = 0; i < fs.Count; i++) ApplyTo(fs[i]);
            for (int i = 0; i < fs.Count; i++) { ApplyChrome(fs[i]); DeepNative(fs[i]); }
        }

        /// <summary>标题栏跟主题走 + Win11 圆角。Win10 上这两个属性会静默失败，不影响功能。</summary>
        public static void ApplyChrome(Form f)
        {
            if (f == null || f.IsDisposed || !f.IsHandleCreated) return;
            Native.DarkTitleBar(f.Handle, pal.IsDark);
            Native.RoundCorners(f.Handle);
        }

        /// <summary>原生控件（输入框、列表、进度条）的深色皮肤：滚动条、表头、下拉箭头。</summary>
        public static void DeepNative(Control root)
        {
            if (root == null || root.IsDisposed) return;
            if (root is ComboBox) AttachComboArrow((ComboBox)root);
            if (root.IsHandleCreated
                && (root is TextBox || root is ComboBox || root is ListView
                    || root is TreeView || root is ProgressBar || root is NumericUpDown))
                Native.DarkControl(root.Handle, pal.IsDark);
            // 滚动面板的滚动条是系统画的，要单独打招呼；列表的表头同理
            if (root.IsHandleCreated && root is ScrollableControl && !(root is TextBox))
            {
                Native.DarkControl(root.Handle, pal.IsDark);
                // 面板的滚动条属于「非客户区」，由 DWM 画，光换主题不管用，还得打上深色暗标记
                Native.DarkTitleBar(root.Handle, pal.IsDark);
            }
            if (root.IsHandleCreated && root is ListView)
                Native.DarkHeader(root.Handle, pal.IsDark);
            for (int i = 0; i < root.Controls.Count; i++) DeepNative(root.Controls[i]);
        }

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

        // 参照 iOS 26（Liquid Glass）的「分组列表」：卡片圆角 + 组标题在卡片外 + 内缩分隔线。
        // 触控端的 44pt 点击目标在桌面上按 26–32px 收敛；留白仍按 8 的倍数走。
        public const int CardR = 10;     // 卡片圆角
        public const int CardPad = 16;   // 卡片内边距
        public static Color CardEdge { get { return pal.CardEdge; } }
        public static Color SegTrack { get { return pal.SegTrack; } }   // 分段控件底
        public static Color SegEdge { get { return pal.SegEdge; } }
        public static Color TrackOff { get { return pal.TrackOff; } }   // 开关·关
        public static Color ThumbOn { get { return pal.ThumbOn; } }     // 开关圆点
        public static readonly Font H1 = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font RowTitle = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

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

        /// <summary>两色按 t(0..1) 线性混合，用于动画中间帧。</summary>
        public static Color Mix(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
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
                // 原生下拉箭头永远按浅色画（WinForms 自己画的，不受深色影响），
                // 所以打开自绘列表，再用一个小控件盖住那块白方块，画成当前主题的箭头。
                cb.DrawMode = DrawMode.OwnerDrawFixed;
                cb.ItemHeight = 20;
                cb.DrawItem += ComboItemPaint;
                // 等窗口句柄真的建好、并且已经从 Controls.Add 里出来之后再盖，
                // 否则在 Add 过程里往同一个集合里塞控件会把 z 序搞乱（箭头会被压到下面）
                cb.HandleCreated += delegate
                {
                    try { cb.BeginInvoke((MethodInvoker)delegate { AttachComboArrow(cb); }); }
                    catch { }
                };
            }
            else if (c is CheckBox || c is RadioButton)
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

        private static readonly ConditionalWeakTable<ComboBox, ComboPainter> comboPainters =
            new ConditionalWeakTable<ComboBox, ComboPainter>();

        private static void ComboItemPaint(object sender, DrawItemEventArgs e)
        {
            ComboBox cb = (ComboBox)sender;
            Graphics g = e.Graphics;
            bool hot = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(hot ? Theme.BtnHover : Theme.Surface))
                g.FillRectangle(b, e.Bounds);
            if (e.Index < 0 || e.Index >= cb.Items.Count) return;
            string text = cb.GetItemText(cb.Items[e.Index]);
            TextRenderer.DrawText(g, text, cb.Font, e.Bounds, Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        /// <summary>
        /// 下拉框右侧那枚按钮是 WinForms 自己画的浅色小块，不受窗口主题影响，
        /// 子控件也盖不住它。所以挂一个 NativeWindow 在它的消息处理之后补画一遍：
        /// 同一块区域按当前主题重画底色和箭头，等于把它覆盖掉，又不用改动控件类型。
        /// </summary>
        private sealed class ComboPainter : NativeWindow
        {
            private static readonly int W = SystemInformation.VerticalScrollBarWidth;
            private readonly ComboBox cb;

            public ComboPainter(ComboBox c)
            {
                cb = c;
                if (c.IsHandleCreated) AssignHandle(c.Handle);
                c.HandleCreated += delegate { AssignHandle(c.Handle); };
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == 0x000F) PaintButton(IntPtr.Zero);        // WM_PAINT
                else if (m.Msg == 0x0085) PaintButton(IntPtr.Zero);    // WM_NCPAINT
                else if (m.Msg == 0x0318 || m.Msg == 0x0317)           // 截图/打印时走的通道
                    PaintButton(m.WParam);
            }

            private void PaintButton(IntPtr hdc)
            {
                if (cb == null || cb.IsDisposed || !cb.IsHandleCreated) return;
                if (cb.DropDownStyle == ComboBoxStyle.Simple) return;
                try
                {
                    int h = cb.ClientSize.Height, w = cb.ClientSize.Width;
                    if (h <= 0 || w <= W + 4) return;
                    Rectangle r = new Rectangle(w - W, 0, W, h);
                    using (Graphics g = hdc == IntPtr.Zero ? Graphics.FromHwnd(cb.Handle) : Graphics.FromHdc(hdc))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (SolidBrush b = new SolidBrush(cb.Enabled ? Theme.Surface : Theme.SurfaceAlt))
                            g.FillRectangle(b, r);
                        using (Pen sep = new Pen(Theme.Line))
                            g.DrawLine(sep, r.Left, 3, r.Left, h - 4);

                        int cx = r.Left + W / 2, cy = h / 2;
                        using (Pen p = new Pen(cb.Enabled ? Theme.Ink2 : Theme.Ink3, 1.4f))
                        {
                            p.StartCap = LineCap.Round;
                            p.EndCap = LineCap.Round;
                            g.DrawLines(p, new Point[] {
                                new Point(cx - 4, cy - 1), new Point(cx, cy + 3), new Point(cx + 4, cy - 1) });
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>给下拉框补一枚主题色的箭头按钮（同一控件只挂一次）。</summary>
        private static void AttachComboArrow(ComboBox cb)
        {
            if (cb == null || cb.IsDisposed || !cb.IsHandleCreated) return;
            ComboPainter p;
            if (comboPainters.TryGetValue(cb, out p)) return;
            comboPainters.Add(cb, new ComboPainter(cb));
            cb.Invalidate();

        }
    }

    /// <summary>
    /// 平面按钮。三种角色：Primary（实心近黑，每屏只给一个）/ Danger（红字描边，破坏性操作，如一键还原）/ 其余（白底细线）。
    /// 状态齐全：normal / hover / down / disabled / focus。
    /// </summary>
    internal class FlatButton : Button
    {
        private bool primary, danger;
        public bool Primary
        {
            get { return primary; }
            set
            {
                if (primary == value) return;
                primary = value;
                SetFace(TargetFace());
                Invalidate();
            }
        }
        public bool Danger
        {
            get { return danger; }
            set
            {
                if (danger == value) return;
                danger = value;
                SetFace(TargetFace());
                Invalidate();
            }
        }
        private bool hover, down;
        private bool plain;
        /// <summary>无边框文字按钮：iOS 里的 plain 样式，靠 hover 底色提示可点，不用描边。</summary>
        public bool Plain
        {
            get { return plain; }
            set { if (plain == value) return; plain = value; Invalidate(); }
        }

        private bool soft;
        /// <summary>浅灰胶囊按钮：iOS 的 gray tinted 次要操作。不用描边也能看出这是按钮。</summary>
        public bool Soft
        {
            get { return soft; }
            set { if (soft == value) return; soft = value; Invalidate(); }
        }
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
        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            SetFace(TargetFace());
            Invalidate();
            base.OnEnabledChanged(e);
        }

        private Color TargetFace()
        {
            if (Primary)
                return !Enabled ? Theme.LineSoft
                     : (down ? Theme.PrimaryDown : (hover ? Theme.PrimaryHover : Theme.Primary));
            if (Danger)
                return !Enabled ? Theme.SurfaceAlt
                     : (down ? Theme.DangerDown : (hover ? Theme.DangerHover : Theme.Surface));
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
                ink = Enabled ? Theme.PrimaryInk : Theme.Ink3;
                edge = face;
            }
            else if (Danger)
            {
                ink = Enabled ? Theme.Accent : Theme.Ink3;
                edge = Enabled ? Theme.Accent : Theme.Line;
            }
            else
            {
                ink = Enabled ? Theme.Ink : Theme.Ink3;
                edge = Theme.Line;
            }

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (plain || soft)
            {
                Color pbg = Parent != null ? Parent.BackColor : Theme.Bg;
                g.Clear(pbg);
                Color fill = Color.Empty;
                if (soft)
                {
                    // 禁用就完全不上色：深色主题下 SurfaceAlt 和 Chip 太接近，看不出「按不动」
                    fill = !Enabled ? Color.Empty
                         : (down ? Theme.ChipDown : (hover ? Theme.ChipHover : Theme.Chip));
                }
                else if (Enabled && (hover || down))
                {
                    fill = down ? Theme.BtnDown : Theme.BtnHover;
                }
                if (fill != Color.Empty)
                {
                    using (GraphicsPath hp = Theme.Round(r, Theme.R))
                    {
                        using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, hp);
                    }
                }
                Color pink = Enabled ? Theme.Ink : Theme.Ink3;
                TextRenderer.DrawText(g, Text, Font, r, pink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(g, new Rectangle(3, 3, Width - 7, Height - 7), pink, pbg);
                return;
            }

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
            using (SolidBrush mark = new SolidBrush(Theme.Mark))
                g.FillRectangle(mark, 0, 6, 3, 10);
            TextRenderer.DrawText(g, caption, Theme.UIBold, new Rectangle(10, 0, Math.Max(0, Width - 10), 22), Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// 圆角卡片：设置页的分组容器。分组标题画在卡片外上方，卡片内用内缩分隔线分行，
    /// 取的是 iOS「inset grouped list」的形状，不再用四边框 GroupBox，也不再拿红条装饰标题。
    /// </summary>
    internal class Card : Panel
    {
        private readonly List<int> seps = new List<int>();

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Font = Theme.UI;
        }

        /// <summary>在卡片坐标 y 处加一条内缩分隔线。</summary>
        public void Separate(int y) { seps.Add(y); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(r, Theme.CardR))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.CardEdge)) g.DrawPath(pen, p);
            }
            foreach (int y in seps)
            {
                using (Pen pen = new Pen(Theme.Line))
                    g.DrawLine(pen, Theme.CardPad, y, Width - Theme.CardPad, y);
            }
        }
    }

    /// <summary>
    /// 分段控件（UISegmentedControl）：互斥选项收进一条胶囊，点哪段选哪段。
    /// 比分开放两个单选按钮省地方，语义也更接近「切换」而不是「填空」。
    /// </summary>
    internal class Segmented : Control
    {
        private string[] items = new string[0];
        private int index;
        private float pos;
        private readonly Timer anim;

        public event EventHandler SelectedIndexChanged;

        public Segmented()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.UI;
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = 30;
            anim = new Timer();
            anim.Interval = 15;
            anim.Tick += delegate
            {
                pos += (index - pos) * 0.4f;
                if (Math.Abs(index - pos) < 0.02f) { pos = index; anim.Stop(); }
                Invalidate();
            };
        }

        public string[] Items
        {
            get { return items; }
            set { items = value == null ? new string[0] : value; Invalidate(); }
        }

        /// <summary>只设值不触发事件，供初始化用。</summary>
        public int SelectedIndex
        {
            get { return index; }
            set
            {
                int v = Math.Max(0, Math.Min(value, Math.Max(0, items.Length - 1)));
                if (v == index && pos == v) return;
                index = v;
                pos = v;
                anim.Stop();
                Invalidate();
            }
        }

        private int CellWidth()
        {
            return Math.Max(1, (Width - 4) / Math.Max(1, items.Length));
        }

        private void Pick(int i)
        {
            if (i < 0 || i >= items.Length || i == index) return;
            index = i;
            if (Theme.MotionEnabled && IsHandleCreated) anim.Start();
            else { pos = index; anim.Stop(); }
            Invalidate();
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            Pick(Math.Min(items.Length - 1, Math.Max(0, (e.X - 2) / CellWidth())));
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) Pick(index - 1);
            else if (e.KeyCode == Keys.Right) Pick(index + 1);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);

            int rr = Height / 2;
            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(track, rr))
            {
                using (SolidBrush b = new SolidBrush(Theme.SegTrack)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.SegEdge)) g.DrawPath(pen, p);
            }
            if (items.Length == 0) return;

            int segW = CellWidth();
            int x = 2 + (int)Math.Round(pos * segW);
            Rectangle thumb = new Rectangle(x, 2, segW, Height - 5);
            using (GraphicsPath p = Theme.Round(thumb, Math.Max(2, rr - 2)))
            {
                using (GraphicsPath sp = Theme.Round(new Rectangle(thumb.X, thumb.Y + 1, thumb.Width, thumb.Height), Math.Max(2, rr - 2)))
                {
                    using (SolidBrush shade = new SolidBrush(Color.FromArgb(Theme.IsDark ? 90 : 20, 0, 0, 0))) g.FillPath(shade, sp);
                }
                using (SolidBrush b2 = new SolidBrush(Theme.Surface)) g.FillPath(b2, p);
                using (Pen pen = new Pen(Theme.SegEdge)) g.DrawPath(pen, p);
            }
            for (int i = 0; i < items.Length; i++)
            {
                Rectangle cell = new Rectangle(2 + i * segW, 0, segW, Height);
                Font f = i == index ? Theme.UIBold : Theme.UI;
                Color ink = !Enabled ? Theme.Ink3 : (i == index ? Theme.Ink : Theme.Ink2);
                TextRenderer.DrawText(g, items[i], f, cell, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, new Rectangle(3, 3, Width - 8, Height - 8), Theme.Ink, Theme.SegTrack);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && anim != null) anim.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 开关（UISwitch 的桌面收敛版）：44×26 胶囊，开=近黑（深色下近白），关=灰，圆点带 150ms 滑动。
    /// 比 CheckBox 更能表达「这是立即生效的状态」而不是「这是待提交的勾选」。
    /// </summary>
    internal class Toggle : Control
    {
        private bool on;
        private float pos;
        private readonly Timer anim;

        public event EventHandler CheckedChanged;

        public Toggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(44, 26);
            Cursor = Cursors.Hand;
            TabStop = true;
            anim = new Timer();
            anim.Interval = 15;
            anim.Tick += delegate
            {
                float target = on ? 1f : 0f;
                pos += (target - pos) * 0.4f;
                if (Math.Abs(target - pos) < 0.02f) { pos = target; anim.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                if (Theme.MotionEnabled && IsHandleCreated) anim.Start();
                else { pos = on ? 1f : 0f; anim.Stop(); }
                Invalidate();
            }
        }

        private void Flip()
        {
            on = !on;
            if (Theme.MotionEnabled && IsHandleCreated) anim.Start();
            else { pos = on ? 1f : 0f; anim.Stop(); }
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e) { Flip(); base.OnClick(e); }

        protected override bool IsInputKey(Keys k) { return k == Keys.Space || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Flip(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);

            Color track = Enabled ? Theme.Mix(Theme.TrackOff, Theme.Mark, pos) : Theme.TrackOff;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(r, Height / 2))
            {
                using (SolidBrush b = new SolidBrush(track)) g.FillPath(b, p);
            }

            int d = Height - 6;
            int x = 2 + (int)Math.Round(pos * (Width - d - 4));
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(Theme.IsDark ? 90 : 32, 0, 0, 0)))
                g.FillEllipse(sh, x, 4, d, d);
            using (SolidBrush b = new SolidBrush(Enabled ? Theme.ThumbOn : Theme.SurfaceAlt))
                g.FillEllipse(b, x, 3, d, d);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width, Height), Theme.Ink, Theme.Surface);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && anim != null) anim.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 系统层的几个开关：标题栏深浅、Win11 圆角、原生控件的深色皮肤。
    /// 全部「失败也不报错」——Win10 老版本没有这些属性时返回非 0，当作没这回事。
    /// </summary>
    internal static class Native
    {
        private delegate int SetPreferredAppModeDelegate(int mode);   // uxtheme 序号 135

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string sub, string list);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibrary(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string proc);

        private static IntPtr ux;
        private static bool uxTried;

        /// <summary>声明「本进程按深色画原生控件」。建窗口前调一次，之后切主题再调即可。</summary>
        public static void SetAppMode(bool dark)
        {
            try
            {
                if (!uxTried) { uxTried = true; ux = LoadLibrary("uxtheme.dll"); }
                if (ux == IntPtr.Zero) return;

                IntPtr p = GetProcAddress(ux, "#135");
                if (p != IntPtr.Zero)
                {
                    SetPreferredAppModeDelegate d = (SetPreferredAppModeDelegate)
                        Marshal.GetDelegateForFunctionPointer(p, typeof(SetPreferredAppModeDelegate));
                    d(dark ? 2 : 3);    // 2 = 强制深色，3 = 强制浅色
                }
                IntPtr f = GetProcAddress(ux, "#104");    // FlushMenuThemes
                if (f != IntPtr.Zero)
                {
                    Action flush = (Action)Marshal.GetDelegateForFunctionPointer(f, typeof(Action));
                    flush();
                }
            }
            catch { }
        }

        public static void DarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref v, 4);   // 1809–1903 用老编号
            }
            catch { }
        }

        public static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int v = 2;    // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref v, 4);
            }
            catch { }
        }

        private const int LVM_GETHEADER = 0x101F;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private delegate int AllowDarkModeForWindowDelegate(IntPtr hwnd, int allow);   // uxtheme 序号 133

        /// <summary>
        /// 告诉系统「这个窗口也可以按深色画」。只调 SetWindowTheme 时，
        /// 滚动条这类由主题引擎直接绘制的部件不会变色，必须先放行。
        /// </summary>
        private static void AllowDark(IntPtr hwnd, bool dark)
        {
            try
            {
                if (ux == IntPtr.Zero) return;
                IntPtr p = GetProcAddress(ux, "#133");
                if (p == IntPtr.Zero) return;
                AllowDarkModeForWindowDelegate d = (AllowDarkModeForWindowDelegate)
                    Marshal.GetDelegateForFunctionPointer(p, typeof(AllowDarkModeForWindowDelegate));
                d(hwnd, dark ? 1 : 0);
            }
            catch { }
        }

        /// <summary>单个控件的深色皮肤：滚动条、表头、输入框边框、下拉箭头。</summary>
        public static void DarkControl(IntPtr hwnd, bool dark)
        {
            try
            {
                if (ux == IntPtr.Zero) { uxTried = true; ux = LoadLibrary("uxtheme.dll"); }
                AllowDark(hwnd, dark);
                SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch { }
        }

        /// <summary>
        /// 列表的表头是 ListView 的子窗口，单独给它一套主题色；不给它，
        /// 深色界面里就会有一条通白的列标题栏。
        /// </summary>
        public static void DarkHeader(IntPtr listViewHandle, bool dark)
        {
            try
            {
                IntPtr head = SendMessage(listViewHandle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (head != IntPtr.Zero)
                {
                    AllowDark(head, dark);
                    SetWindowTheme(head, dark ? "DarkMode_ItemsView" : "ItemsView", null);
                }
            }
            catch { }
        }
    }
}

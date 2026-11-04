using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Installer
{
    // ============================================================
    //  方案树（A 版）
    //  左边按「能替换的东西」列成横向树：枪械 → AK-47 / M16 …
    //  右边是这件东西的候选 MOD，单选。
    //  规则：一个位置只留一个 MOD，选新的就把同位置原来的取消。
    //  不区分「模型 / 音效 / 视角」—— 那件事交给用户自己判断。
    // ============================================================

    /// <summary>一个「能替换的东西」：AK-47 / 医疗包 / 教练 …</summary>
    internal sealed class Slot
    {
        public string Key;                       // 被替换的模型名，如 w_rifle_ak47
        public string Name;                      // 显示名：AK-47
        public string Cat;                       // 大类：枪械 / 医疗 / 人物 …
        public readonly List<ModGroup> Items = new List<ModGroup>();
    }

    /// <summary>把 MOD 库按「改的是什么」聚成槽位。认不出改什么的，不进树。</summary>
    internal static class SlotIndex
    {
        public static List<Slot> Build()
        {
            Dictionary<string, Slot> map = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
            List<Slot> list = new List<Slot>();
            foreach (ModGroup g in Catalog.Groups)
            {
                ModItem m = g.Main;
                if (m == null) continue;
                // 地图能叠好几张、喷漆不占位置 —— 这两种没有「只能一个」的说法，不进树
                if (g.Cat == "地图" || g.Cat == "喷漆") continue;
                string key = (m.SubjectKey ?? "").Trim();
                string nm = (m.Subject ?? "").Trim();
                if (IsCountName(nm)) continue;                 // 「4 件」是「一个包里改了好几样」的上报格式，不是东西
                if (key.Length == 0)
                {
                    // 人物、投掷物这类没有「模型名」，只有认出来的名字 —— 就用名字当位置
                    if (nm.Length == 0) continue;              // 名字都认不出：不进树
                    key = "sub:" + nm;
                }
                Slot s;
                if (!map.TryGetValue(key, out s))
                {
                    s = new Slot();
                    s.Key = key;
                    s.Name = nm.Length > 0 ? nm : key;
                    s.Cat = g.Cat;
                    map[key] = s;
                    list.Add(s);
                }
                else if (Vague(s.Name) && nm.Length > 0 && !Vague(nm))
                {
                    s.Name = nm;                               // 「步枪」这种泛称，换成更具体的名字
                }
                s.Items.Add(g);
            }
            Disambiguate(list);
            return list;
        }

        /// <summary>两把不同的枪可能都叫「鸟狙」，加个尾巴区分开，否则树里看着像重复项。</summary>
        private static void Disambiguate(List<Slot> list)
        {
            Dictionary<string, int> freq = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Slot s in list)
            {
                int n;
                freq.TryGetValue(s.Name, out n);
                freq[s.Name] = n + 1;
            }
            foreach (Slot s in list)
            {
                int n;
                if (!freq.TryGetValue(s.Name, out n) || n < 2) continue;
                s.Name = s.Name + " · " + Tag(s.Key);
            }
        }

        private static string Tag(string key)
        {
            string k = key;
            if (k.StartsWith("w_", StringComparison.OrdinalIgnoreCase)) k = k.Substring(2);
            else if (k.StartsWith("v_", StringComparison.OrdinalIgnoreCase)) k = k.Substring(2);
            int us = k.LastIndexOf('_');
            if (us >= 0 && us < k.Length - 1) k = k.Substring(us + 1);
            return k.ToUpperInvariant();
        }

        private static bool IsCountName(string s)
        {
            if (s == null || s.Length < 2 || s[s.Length - 1] != '件') return false;
            for (int i = 0; i < s.Length - 1; i++)
                if (!char.IsDigit(s[i]) && s[i] != ' ') return false;
            return true;
        }

        private static bool Vague(string s)
        {
            return s == "步枪" || s == "冲锋枪" || s == "霰弹枪" || s == "投掷物"
                || s == "幸存者" || s == "近战" || s == "其他" || s.Length == 0;
        }

        /// <summary>大类按 Catalog.Cats 的顺序排，类内方案内的排前面。</summary>
        public static List<string> CatOrder(List<Slot> slots)
        {
            // 先放最常用的四类，地图/喷漆排在后面（现在其实不进树了）
            string[] prefer = { "枪械", "人物", "药品", "杂项" };
            List<string> order = new List<string>();
            foreach (string c in prefer) order.Add(c);
            foreach (string c in Catalog.Cats) if (!order.Contains(c)) order.Add(c);
            foreach (Slot s in slots) if (!order.Contains(s.Cat)) order.Add(s.Cat);
            return order;
        }
    }

    /// <summary>树里的一行：分类标题，或者一个槽位。</summary>
    internal sealed class TreeRow
    {
        public bool IsCat;
        public bool IsNote;      // 「未归类」那一行：只是个说明入口
        public string Cat;
        public Slot Slot;
    }

    // ============================================================
    //  方案树视图
    // ============================================================
    internal sealed class CatTree : UserControl
    {
        private readonly MainForm mf;
        private readonly Panel scroll;
        private readonly SlotTree body;
        private readonly ListView lv;
        private readonly Label lblTitle, lblCur, lblFoot;
        private readonly FlatButton btnPick;

        private List<Slot> slots = new List<Slot>();
        private int unmatched;                       // 一个位置都归不上的 MOD 数
        private Label lblHint;
        private readonly HashSet<string> folded = new HashSet<string>(StringComparer.Ordinal);
        private string selKey = "";

        public string SelectedKey { get { return selKey; } }
        public bool IsFolded(string cat) { return folded.Contains(cat); }

        public CatTree(MainForm owner)
        {
            mf = owner;
            BackColor = Theme.Surface;
            Font = Theme.UI;

            // ---- 左：树 ----
            scroll = new Panel();
            scroll.BackColor = Color.FromArgb(247, 249, 251);
            scroll.AutoScroll = true;
            scroll.BorderStyle = BorderStyle.None;
            Controls.Add(scroll);

            body = new SlotTree(this);
            body.BackColor = Theme.SurfaceAlt;
            scroll.Controls.Add(body);

            // ---- 右：候选 ----
            lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.MultiSelect = false;
            lv.BorderStyle = BorderStyle.None;
            lv.BackColor = Theme.Surface;
            lv.ForeColor = Theme.Ink;
            lv.Font = Theme.UI;
            lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lv.ShowItemToolTips = true;
            lv.HideSelection = false;
            ImageList pad = new ImageList();
            pad.ImageSize = new Size(1, 24);
            pad.ColorDepth = ColorDepth.Depth32Bit;
            lv.SmallImageList = pad;
            lv.StateImageList = Dots();
            lv.Columns.Add("MOD", 380);
            lv.Columns.Add("改了什么", 100);
            lv.Columns.Add("大小", 74).TextAlign = HorizontalAlignment.Right;
            lv.MouseClick += delegate(object s, MouseEventArgs e)
            {
                ListViewItem li = lv.GetItemAt(e.X, e.Y);
                if (li == null) return;
                Choose(li.Tag as ModGroup);
            };
            lv.Resize += delegate { LayoutCols(); };
            Controls.Add(lv);

            lblTitle = new Label();
            lblTitle.Font = Theme.UIBold;
            lblTitle.ForeColor = Theme.Ink;
            lblTitle.BackColor = Color.Transparent;
            lblTitle.AutoEllipsis = true;
            Controls.Add(lblTitle);

            lblCur = new Label();
            lblCur.Font = Theme.UISmall;
            lblCur.ForeColor = Theme.Ink2;
            lblCur.BackColor = Color.Transparent;
            lblCur.AutoEllipsis = true;
            Controls.Add(lblCur);

            lblFoot = new Label();
            lblFoot.Font = Theme.UISmall;
            lblFoot.ForeColor = Theme.Ink3;
            lblFoot.BackColor = Color.Transparent;
            lblFoot.AutoEllipsis = true;
            Controls.Add(lblFoot);

            lblHint = new Label();
            lblHint.Font = Theme.UI;
            lblHint.ForeColor = Theme.Ink3;
            lblHint.BackColor = Color.Transparent;
            lblHint.Visible = false;
            Controls.Add(lblHint);

            btnPick = new FlatButton();
            btnPick.Text = "留原版";
            btnPick.SetBounds(0, 0, 66, Theme.H);
            btnPick.Click += delegate { Choose(null); };
            btnPick.Visible = false;          // 列表里已经有「留原版」那一行，按钮就多余了
            Controls.Add(btnPick);

            Resize += delegate { LayoutSelf(); };
            LayoutSelf();
        }

        private static ImageList Dots()
        {
            ImageList il = new ImageList();
            il.ImageSize = new Size(14, 14);
            il.ColorDepth = ColorDepth.Depth32Bit;
            il.TransparentColor = Color.Transparent;
            il.Images.Add(Dot(false));
            il.Images.Add(Dot(true));
            return il;
        }

        private static Bitmap Dot(bool on)
        {
            Bitmap b = new Bitmap(14, 14);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(on ? Theme.Accent : Color.FromArgb(184, 192, 200), 1.5f))
                    g.DrawEllipse(p, 2.5f, 2.5f, 8f, 8f);
                if (on)
                    using (SolidBrush br = new SolidBrush(Theme.Accent))
                        g.FillEllipse(br, 5f, 5f, 4f, 4f);
            }
            return b;
        }

        // ---------------------------------------------------------- 构建

        /// <summary>重新分组并铺树。库变了才需要调用。</summary>
        public void Rebuild()
        {
            slots = SlotIndex.Build();
            SortSlots();
            int inTree = 0;
            foreach (Slot s0 in slots) inTree += s0.Items.Count;
            // 地图和喷漆本来就不该占位置，别把它们算成「认不出」
            int eligible = 0;
            foreach (ModGroup g in Catalog.Groups)
                if (g.Cat != "地图" && g.Cat != "喷漆") eligible++;
            unmatched = Math.Max(0, eligible - inTree);
            BuildRows();
            if (selKey.Length == 0 && slots.Count > 0) selKey = slots[0].Key;
            FillDetail();
        }

        private void SortSlots()
        {
            List<string> order = SlotIndex.CatOrder(slots);
            slots.Sort(delegate(Slot a, Slot b)
            {
                int ia = order.IndexOf(a.Cat), ib = order.IndexOf(b.Cat);
                if (ia != ib) return ia.CompareTo(ib);
                bool x = HasChecked(a), y = HasChecked(b);
                if (x != y) return x ? -1 : 1;
                int c = string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
                return c != 0 ? c : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });
        }

        private void BuildRows()
        {
            List<TreeRow> rows = new List<TreeRow>();
            List<string> order = SlotIndex.CatOrder(slots);
            foreach (string cat in order)
            {
                List<Slot> inCat = new List<Slot>();
                foreach (Slot s in slots) if (s.Cat == cat) inCat.Add(s);
                if (inCat.Count == 0) continue;

                TreeRow h = new TreeRow();
                h.IsCat = true; h.Cat = cat;
                rows.Add(h);
                if (!folded.Contains(cat))
                    foreach (Slot s in inCat)
                    {
                        TreeRow r = new TreeRow();
                        r.Slot = s; r.Cat = cat;
                        rows.Add(r);
                    }
            }
            if (unmatched > 0)
            {
                TreeRow h = new TreeRow();
                h.IsCat = true; h.Cat = "未归类";
                rows.Add(h);
                if (!folded.Contains("未归类"))
                {
                    TreeRow n = new TreeRow();
                    n.IsNote = true; n.Cat = "未归类";
                    rows.Add(n);
                }
            }
            body.SetRows(rows);
        }

        // ---------------------------------------------------------- 状态

        internal bool HasChecked(Slot s)
        {
            foreach (ModGroup g in s.Items) if (mf.CountChecked(g) > 0) return true;
            return false;
        }

        /// <summary>这个位置当前勾了几件（正常是 0 或 1；>1 说明用户手工叠了）。</summary>
        internal int CheckedGroups(Slot s)
        {
            int n = 0;
            foreach (ModGroup g in s.Items) if (mf.CountChecked(g) > 0) n++;
            return n;
        }

        /// <summary>这个位置当前用了哪个 MOD（多件时取第一件）。</summary>
        internal ModGroup ChosenOf(Slot s)
        {
            foreach (ModGroup g in s.Items) if (mf.CountChecked(g) > 0) return g;
            return null;
        }

        internal int Unmatched { get { return unmatched; } }
        internal int RowCount { get { return body == null ? 0 : body.RowCount; } }
        internal int SlotCount { get { return slots.Count; } }
        internal int SlotCheckedCount
        {
            get { int n = 0; foreach (Slot s in slots) if (HasChecked(s)) n++; return n; }
        }

        /// <summary>外部勾选变了：只重画，不重建行。</summary>
        public void Sync()
        {
            body.Invalidate();
            FillDetail();
        }

        public void SelectUnmatched()
        {
            if (selKey == "#unmatched") return;
            selKey = "#unmatched";
            body.Invalidate();
            FillDetail();
        }

        public void SelectSlot(string key)
        {
            if (key == null || key == selKey) return;
            selKey = key;
            body.Invalidate();
            FillDetail();
        }

        /// <summary>自测用：把第 n 个槽位选出来。</summary>
        public Slot SlotAt(int n)
        {
            return (n >= 0 && n < slots.Count) ? slots[n] : null;
        }

        /// <summary>自测用：按显示名找槽位。</summary>
        public Slot SlotByName(string name)
        {
            foreach (Slot s in slots) if (s.Name == name) return s;
            return null;
        }

        public void SetFolded(string cat, bool on)
        {
            if (on) folded.Add(cat); else folded.Remove(cat);
            BuildRows();
        }

        // ---------------------------------------------------------- 右侧

        private Slot Current
        {
            get
            {
                foreach (Slot s in slots) if (s.Key == selKey) return s;
                return null;
            }
        }

        private void FillDetail()
        {
            if (selKey == "#unmatched") { ShowUnmatched(); return; }
            lv.Visible = true; lblHint.Visible = false;
            Slot s = Current;
            lv.BeginUpdate();
            try
            {
                lv.Items.Clear();
                if (s == null)
                {
                    lblTitle.Text = "选一个位置";
                    lblCur.Text = "左边点一个武器 / 药品 / 人物，这里列它的 MOD";
                    return;
                }
                int n = CheckedGroups(s);
                lblTitle.Text = s.Name;
                if (n == 0) lblCur.Text = "还没选 · 游戏里还是原版";
                else if (n == 1)
                {
                    ModGroup c = ChosenOf(s);
                    lblCur.Text = "当前：" + (c == null ? "" : c.Name);
                }
                else lblCur.Text = "这个位置叠了 " + n + " 件（音效/视角/材质，自己确认会不会打架）";

                foreach (ModGroup g in s.Items)
                {
                    bool on = mf.CountChecked(g) > 0;
                    ListViewItem li = new ListViewItem(g.Name);
                    li.SubItems.Add(KindsOf(g));
                    li.SubItems.Add(Core.Human(g.Size));
                    li.StateImageIndex = on ? 1 : 0;
                    if (on) li.Font = Theme.UIBold;
                    li.Tag = g;
                    if (g.IsSet) li.ToolTipText = g.Parts.Count + " 个 vpk 组成，选它就是选默认部件";
                    lv.Items.Add(li);
                }

                ListViewItem none = new ListViewItem("留原版（这个位置不装 MOD）");
                none.SubItems.Add("");
                none.SubItems.Add("—");
                none.ForeColor = Theme.Ink3;
                none.StateImageIndex = n == 0 ? 1 : 0;
                none.Tag = null;
                lv.Items.Add(none);

                lblFoot.Text = s.Items.Count + " 个候选 · 点一个就换掉原来的";
            }
            finally { lv.EndUpdate(); }
            LayoutCols();
        }

        /// <summary>未归类：说明白为什么不在这里动它，而不是假装没有。</summary>
        private void ShowUnmatched()
        {
            lv.Visible = false;
            lblHint.Visible = true;
            lblFoot.Text = "";
            lblTitle.Text = "未归类";
            lblCur.Text = unmatched + " 个 MOD 认不出改的是哪件东西";
            lblHint.Text = "它们的 vpk 里没有能和枪械/人物对上的模型名，程序没法判断该占哪个位置，\n"
                         + "所以在这里不参与「一个位置一个」。\n\n"
                         + "不影响装机：在「分类」页里照常勾选，装的时候一模一样。\n"
                         + "如果想让它们进树，右键那一行 →「设置它替换什么」，手工指定一次就会记住。";
        }

        private static string KindsOf(ModGroup g)
        {
            ModItem m = g.Main;
            if (m == null) return "";
            string k = m.Kinds;
            if (string.IsNullOrEmpty(k)) k = m.SubjectLevel;
            return k == null ? "" : k.Replace(",", "·");
        }

        /// <summary>点了一个候选（null = 留原版）：清掉这个位置的旧件，再勾新件。</summary>
        private void Choose(ModGroup g)
        {
            Slot s = Current;
            if (s == null) return;
            foreach (ModGroup o in s.Items) mf.UncheckGroup(o);
            if (g != null) mf.CheckGroupDefault(g);
            mf.AfterTreePick();
            FillDetail();
        }

        /// <summary>自测用：直接选某个槽位里的第 i 个候选。</summary>
        public string ChooseAt(int slotIdx, int itemIdx)
        {
            Slot s = SlotAt(slotIdx);
            if (s == null) return "没有槽位";
            selKey = s.Key;
            if (itemIdx < 0) { Choose(null); return "留原版"; }
            if (itemIdx >= s.Items.Count) return "候选越界";
            ModGroup g = s.Items[itemIdx];
            Choose(g);
            return g.Name;
        }

        // ---------------------------------------------------------- 布局

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (scroll == null) return;
            using (Pen p = new Pen(Theme.Line))
                e.Graphics.DrawLine(p, scroll.Right, 0, scroll.Right, Height);
        }

        private void LayoutSelf()
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W <= 0 || H <= 0) return;
            int treeW = Math.Min(404, Math.Max(240, W * 42 / 100));
            if (scroll != null) scroll.SetBounds(0, 0, treeW, H);
            if (body != null) body.Width = Math.Max(60, scroll.ClientSize.Width > 0 ? scroll.ClientSize.Width : treeW);

            int x = treeW + 1;
            int rightW = Math.Max(120, W - x);
            if (lblTitle != null) lblTitle.SetBounds(x + 16, 12, Math.Max(60, rightW - 32 - 100), 22);
            if (lblCur != null) lblCur.SetBounds(x + 16, 34, Math.Max(60, rightW - 32), 18);
            if (lv != null) lv.SetBounds(x + 4, 60, rightW - 8, Math.Max(60, H - 60 - 42));
            if (lblHint != null) lblHint.SetBounds(x + 16, 66, Math.Max(80, rightW - 32), Math.Max(60, H - 120));
            if (btnPick != null) btnPick.SetBounds(x + 16, H - 34, 76, Theme.H);
            if (lblFoot != null) lblFoot.SetBounds(x + 16, H - 30, Math.Max(60, rightW - 32), 18);
            LayoutCols();
        }

        private void LayoutCols()
        {
            if (lv == null || lv.Columns.Count < 3) return;
            int w = lv.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8;
            const int kindW = 100, sizeW = 74;
            lv.Columns[0].Width = Math.Max(160, w - kindW - sizeW);
            lv.Columns[1].Width = kindW;
            lv.Columns[2].Width = sizeW;
        }
    }

    // ============================================================
    //  树体：自绘，两级（分类 / 槽位）
    // ============================================================
    internal sealed class SlotTree : Control
    {
        private const int RowH = 26;
        private readonly CatTree owner;
        private readonly List<TreeRow> rows = new List<TreeRow>();
        private int hover = -1;

        public SlotTree(CatTree o)
        {
            owner = o;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.UI;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int RowCount { get { return rows.Count; } }

        public void SetRows(List<TreeRow> r)
        {
            rows.Clear();
            if (r != null) rows.AddRange(r);
            Height = Math.Max(12, rows.Count * RowH + 6);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(247, 249, 251));
            int W = Width;
            if (W <= 0) return;

            using (Pen branch = new Pen(Color.FromArgb(198, 206, 215)))
            using (SolidBrush selBg = new SolidBrush(Color.FromArgb(250, 240, 239)))
            using (SolidBrush hovBg = new SolidBrush(Color.FromArgb(238, 242, 246)))
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    TreeRow r = rows[i];
                    int y = i * RowH + 4;

                    if (r.IsCat)
                    {
                        bool open = !owner.IsFolded(r.Cat);
                        if (i == hover) g.FillRectangle(hovBg, 0, y, W, RowH);
                        DrawTri(g, 10, y + RowH / 2, open);
                        TextRenderer.DrawText(g, r.Cat, Theme.UIBold, new Rectangle(23, y, W - 70, RowH), Theme.Ink,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        int n = 0;
                        foreach (TreeRow x in rows) if (!x.IsCat && !x.IsNote && x.Cat == r.Cat) n++;
                        string num = r.Cat == "未归类" ? owner.Unmatched + " 个" : n + " 个";
                        TextRenderer.DrawText(g, num, Theme.UISmall, new Rectangle(W - 62, y, 50, RowH), Theme.Ink3,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        continue;
                    }

                    if (r.IsNote)
                    {
                        if (i == hover) g.FillRectangle(hovBg, 0, y, W, RowH);
                        g.DrawLine(branch, 17, y + RowH / 2, 26, y + RowH / 2);
                        TextRenderer.DrawText(g, "认不出改的是什么", Theme.UI,
                            new Rectangle(33, y, W - 46, RowH), Theme.Ink3,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        continue;
                    }

                    Slot s = r.Slot;
                    if (s == null) continue;
                    bool sel = s.Key == owner.SelectedKey;
                    if (sel) g.FillRectangle(selBg, 0, y, W, RowH);
                    else if (i == hover) g.FillRectangle(hovBg, 0, y, W, RowH);
                    if (sel) using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, y + 3, 2, RowH - 6);

                    // 分支线：竖线 + 一根横线
                    bool nextSame = (i + 1 < rows.Count) && !rows[i + 1].IsCat && rows[i + 1].Cat == r.Cat;
                    int bx = 17;
                    g.DrawLine(branch, bx, y, bx, y + (nextSame ? RowH : RowH / 2 + 1));
                    g.DrawLine(branch, bx, y + RowH / 2, bx + 9, y + RowH / 2);

                    // 状态点
                    int cnt = owner.CheckedGroups(s);
                    Color dot = cnt == 0 ? Color.FromArgb(210, 216, 223) : (cnt > 1 ? Theme.Warn : Theme.Ok);
                    using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, bx + 17, y + RowH / 2 - 3, 6, 6);

                    int nx = bx + 32;
                    int nameW = 92;
                    TextRenderer.DrawText(g, s.Name, sel ? Theme.UIBold : Theme.UI,
                        new Rectangle(nx, y, nameW, RowH), sel ? Theme.Ink : Theme.Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                    ModGroup chosen = owner.ChosenOf(s);
                    string sub = chosen == null ? "未选" : chosen.Name;
                    if (cnt > 1) sub = "叠了 " + cnt + " 件";
                    Color subInk = chosen == null ? Theme.Ink3 : Theme.Ink3;
                    int subX = nx + nameW + 8;
                    int subW = W - subX - 34;
                    if (subW > 20)
                        TextRenderer.DrawText(g, sub, Theme.UISmall, new Rectangle(subX, y, subW, RowH), subInk,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                    TextRenderer.DrawText(g, s.Items.Count.ToString(), Theme.UISmall,
                        new Rectangle(W - 30, y, 22, RowH), Theme.Ink3,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        private static void DrawTri(Graphics g, int cx, int cy, bool open)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(140, 150, 160)))
            {
                if (open)
                {
                    Point[] p = { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 3) };
                    g.FillPolygon(b, p);
                }
                else
                {
                    Point[] p = { new Point(cx - 2, cy - 4), new Point(cx - 2, cy + 4), new Point(cx + 3, cy) };
                    g.FillPolygon(b, p);
                }
            }
        }

        private int IndexAt(int y)
        {
            int i = y / RowH;
            return (i >= 0 && i < rows.Count) ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i != hover) { hover = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i < 0) return;
            TreeRow r = rows[i];
            if (r.IsCat)
            {
                owner.SetFolded(r.Cat, !owner.IsFolded(r.Cat));
            }
            else if (r.IsNote)
            {
                owner.SelectUnmatched();
            }
            else if (r.Slot != null)
            {
                owner.SelectSlot(r.Slot.Key);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Panel p = Parent as Panel;
            if (p != null && p.VerticalScroll.Visible)
            {
                int v = p.VerticalScroll.Value - e.Delta;
                v = Math.Max(p.VerticalScroll.Minimum, Math.Min(p.VerticalScroll.Maximum, v));
                p.VerticalScroll.Value = v;
                p.PerformLayout();
                return;
            }
            base.OnMouseWheel(e);
        }
    }
}

namespace Installer
{
    public partial class MainForm
    {
        /// <summary>自测：切到方案树、数槽位、验证「一个位置只留一个」的替换逻辑。</summary>
        internal void UiTestTree()
        {
            if (viewHost == null) { Core.Log("FAIL", "TREE 没有 viewHost"); return; }
            // 自测会改勾选，先留一份底，测完原样放回去 —— 不能把用户的方案改坏
            List<string> backup = new List<string>(checkedKeys);
            ShowTree();
            Application.DoEvents();

            int slots = treeView.SlotCount;
            int used = treeView.SlotCheckedCount;
            Core.Log("INFO", "TREE 槽位 " + slots + " 个，其中已选 " + used + " 个");

            // 分类分布
            System.Collections.Generic.Dictionary<string, int> byCat =
                new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < slots; i++)
            {
                Slot s = treeView.SlotAt(i);
                if (s == null) continue;
                int n;
                byCat.TryGetValue(s.Cat, out n);
                byCat[s.Cat] = n + 1;
                if (i < 12) Core.Log("INFO", "TREE  " + s.Cat + " / " + s.Name + "  候选 " + s.Items.Count
                                            + "  已选 " + (treeView.HasChecked(s) ? "是" : "否"));
            }
            foreach (KeyValuePair<string, int> kv in byCat)
                Core.Log("INFO", "TREE 分类 " + kv.Key + " = " + kv.Value + " 个位置");

            // 覆盖诊断：每类有多少个 mod 认得出来
            foreach (string c in Catalog.Cats)
            {
                int tot = 0, known = 0;
                foreach (ModGroup g in Catalog.Groups)
                {
                    if (g.Cat != c) continue;
                    tot++;
                    ModItem mi = g.Main;
                    if (mi != null && ((mi.Subject ?? "").Trim().Length > 0 || (mi.SubjectKey ?? "").Trim().Length > 0)) known++;
                }
                Core.Log("INFO", "TREE 覆盖 " + c + "  " + known + "/" + tot + " 认得出来");
            }

            // 找一个候选多于 3 的槽位来测替换
            int idx = -1;
            for (int i = 0; i < slots; i++)
            {
                Slot s = treeView.SlotAt(i);
                if (s != null && s.Items.Count >= 3) { idx = i; break; }
            }
            if (idx < 0) { Core.Log("FAIL", "TREE 没有候选 >=3 的槽位，测不了替换"); return; }

            Slot t = treeView.SlotAt(idx);
            Core.Log("INFO", "TREE 试验槽位 " + t.Name + "（" + t.Items.Count + " 个候选）");

            int n0 = TotalCheckedParts();
            string pick1 = treeView.ChooseAt(idx, 1);
            Application.DoEvents();
            int n1 = TotalCheckedParts();
            Core.Log("INFO", "TREE 选第 2 个候选「" + pick1 + "」：勾选数 " + n0 + " → " + n1);

            string pick2 = treeView.ChooseAt(idx, 2);
            Application.DoEvents();
            int n2 = TotalCheckedParts();
            Core.Log("INFO", "TREE 换成第 3 个「" + pick2 + "」：勾选数 " + n1 + " → " + n2
                            + "（应该只换不增）");
            if (n2 != n1) Core.Log("FAIL", "TREE 替换后勾选数变了 " + n1 + " → " + n2);

            int groupsInSlot = treeView.CheckedGroups(treeView.SlotAt(idx));
            Core.Log("INFO", "TREE 这个位置现在有 " + groupsInSlot + " 件");
            if (groupsInSlot != 1) Core.Log("FAIL", "TREE 一个位置出现了 " + groupsInSlot + " 件");

            treeView.ChooseAt(idx, -1);
            Application.DoEvents();
            int n3 = TotalCheckedParts();
            Core.Log("INFO", "TREE 点「留原版」：勾选数 " + n2 + " → " + n3);
            if (n3 != n2 - treeView.SlotAt(idx).Items.Count && n3 >= n2) Core.Log("FAIL", "TREE 留原版没清掉");

            // 折叠 / 展开
            int rowsBefore = treeView.RowCount;
            treeView.SetFolded("枪械", true);
            Application.DoEvents();
            int rowsAfter = treeView.RowCount;
            Core.Log("INFO", "TREE 折叠「枪械」：行数 " + rowsBefore + " → " + rowsAfter);
            if (rowsAfter >= rowsBefore) Core.Log("FAIL", "TREE 折叠没生效");
            treeView.SetFolded("枪械", false);
            Application.DoEvents();
            Core.Log("INFO", "TREE 展开回去：行数 " + treeView.RowCount);

            // 未归类入口
            treeView.SelectUnmatched();
            Application.DoEvents();
            Core.Log("INFO", "TREE 未归类 " + treeView.Unmatched + " 个（这些不进树，但照常能勾）");

            // 原样放回：按备份逐条还原，不是「再点一下」—— 点一下只会换成另一个候选
            checkedKeys.Clear();
            foreach (string k in backup) checkedKeys.Add(k);
            AfterTreePick();
            treeView.Sync();
            Application.DoEvents();
            int n4 = TotalCheckedParts();
            Core.Log("INFO", "TREE 还原后勾选数 " + n4 + "（原 " + n0 + "）");
            if (n4 != n0) Core.Log("FAIL", "TREE 还原后勾选数对不上 " + n0 + " → " + n4);
            Slot first = treeView.SlotAt(0);
            if (first != null) treeView.SelectSlot(first.Key);   // 截图停在正常状态
        }

        /// <summary>把勾选恢复成某个方案的样子并存盘（自测把方案改坏时用来救回来）。</summary>
        internal void UiFixProfile(string name)
        {
            ApplyProfileByName(name);
            Application.DoEvents();
            Core.Cfg.Mods = new List<string>(checkedKeys);
            Core.SaveConfig();
            Core.Log("INFO", "FIX 已按方案「" + name + "」恢复勾选，共 " + checkedKeys.Count + " 个文件");
        }

        private int TotalCheckedParts()
        {
            int n = 0;
            foreach (ModGroup g in Catalog.Groups) n += CountChecked(g);
            return n;
        }
    }
}

namespace Installer
{
    public partial class MainForm
    {
        /// <summary>自测：右键菜单文案，以及「只装这个 MOD / 重命名」取的是不是右键那一行（而不是勾选）。</summary>
        internal void UiTestMenu()
        {
            string cat = "枪械";
            ShowCatView(cat);
            Application.DoEvents();
            CatView v;
            if (!catViews.TryGetValue(cat, out v) || v.List == null)
            { Core.Log("FAIL", "MENU 拿不到枪械列表"); return; }
            ListView lv = v.List;
            if (lv.Items.Count < 3) { Core.Log("FAIL", "MENU 行数太少 " + lv.Items.Count); return; }

            int checkedRows = lv.CheckedItems.Count;
            ContextMenuStrip cms = BuildRowMenu(lv);
            string all = "";
            foreach (ToolStripItem it in cms.Items)
                all += (it is ToolStripSeparator ? " | " : it.Text + " · ");
            Core.Log("INFO", "MENU 当时勾了 " + checkedRows + " 行，菜单：" + all);

            // 选中第 3 行，勾选一个都不动 —— 看取值是不是跟着选中行走
            lv.SelectedItems.Clear();
            lv.Items[2].Selected = true;
            lv.Items[2].Focused = true;
            Application.DoEvents();

            ModGroup rowGroup = lv.Items[2].Tag as ModGroup;
            ModItem rowItem = lv.Items[2].Tag as ModItem;
            string nm = rowGroup != null ? rowGroup.Name : lv.Items[2].Text;
            List<ModItem> mine = RowItems(lv);
            string names = "";
            foreach (ModItem m in mine) names += m.Name + "、";
            Core.Log("INFO", "MENU 右键行 = [" + nm + "]，取到 " + mine.Count + " 个 vpk：" + names);

            bool ok = mine.Count > 0;
            foreach (ModItem m in mine)
            {
                bool belong = (rowItem != null && rowItem.Key == m.Key);
                if (rowGroup != null)
                    foreach (ModItem p in rowGroup.Parts) if (p.Key == m.Key) belong = true;
                if (!belong) ok = false;
            }
            if (!ok) Core.Log("FAIL", "MENU 取到的 vpk 不属于右键那一行（勾选优先的老毛病还在）");

            bool hasOne = false, hasBatch = false;
            foreach (ToolStripItem it in cms.Items)
            {
                if (it.Text != null && it.Text.StartsWith("只装这个 MOD")) hasOne = true;
                if (it.Text != null && it.Text.StartsWith("只装勾中的这")) hasBatch = true;
            }
            if (!hasOne) Core.Log("FAIL", "MENU 没有「只装这个 MOD」");
            if (checkedRows > 1 && !hasBatch) Core.Log("FAIL", "MENU 勾了 " + checkedRows + " 行却没有批量项");
            if (checkedRows <= 1 && hasBatch) Core.Log("FAIL", "MENU 只勾 " + checkedRows + " 行却有批量项");
            Core.Log("INFO", "MENU 单项=" + hasOne + " 批量项=" + hasBatch + "，重命名项文案="
                + MenuTextOf(cms, "重命名"));

            lv.SelectedItems.Clear();
            ShowTree();
            Application.DoEvents();
        }

        private static string MenuTextOf(ContextMenuStrip cms, string keyword)
        {
            foreach (ToolStripItem it in cms.Items)
                if (it.Text != null && it.Text.IndexOf(keyword, StringComparison.Ordinal) >= 0) return it.Text;
            return "（没有）";
        }
    }
}

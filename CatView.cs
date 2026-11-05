using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Installer
{
    // 每个大类不再是独立窗口：一类一个视图，嵌在主窗口右栏里，靠顶部页签切换。
    // 好处是勾选、搜索、方案下拉始终在同一屏，关不掉的窗口不会把进程留在后台。
    public partial class MainForm
    {
        private readonly Dictionary<string, CatView> catViews = new Dictionary<string, CatView>(StringComparer.Ordinal);
        private string tabCur = "#tree";               // 当前页签：方案树，或某个大类

        internal string TabCurrent { get { return tabCur; } }

        /// <summary>
        /// 页签撤掉之后，「切到某一类」这件事没有了 —— 一律回到方案树。
        /// 保留这个方法只是为了让原来那些调用点（自测、定位、菜单）不用全改，
        /// 语义变成「显示方案树」。
        /// </summary>
        internal void ShowCatView(string cat)
        {
            ShowTree();
        }

        private CatView GetCatView(string cat)
        {
            CatView v;
            if (catViews.TryGetValue(cat, out v)) return v;
            v = new CatView(this, cat);
            v.SetBounds(0, 0, Math.Max(200, viewHost.ClientSize.Width), Math.Max(120, viewHost.ClientSize.Height));
            catViews[cat] = v;
            viewHost.Controls.Add(v);
            return v;
        }

        internal bool IsCatOpen(string cat) { return tabCur == cat; }

        /// <summary>方案树：按「能替换的东西」看这套方案，右边是候选 MOD。</summary>
        internal void ShowTree()
        {
            EnsureTree();
            if (lblLoading != null)              // 读完第一屏就撤掉读取提示
            {
                viewHost.Controls.Remove(lblLoading);
                lblLoading.Dispose();
                lblLoading = null;
            }
            tabCur = "#tree";
            foreach (CatView o in catViews.Values) if (o.Visible) o.Visible = false;
            viewHost.Visible = true;
            treeView.Visible = true;
            treeView.BringToFront();
            treeView.Rebuild();
            UpdateModCaption();
            SyncTabs();
        }

        private void EnsureTree()
        {
            if (treeView != null) return;
            treeView = new CatTree(this);
            treeView.SetBounds(0, 0, Math.Max(200, viewHost.ClientSize.Width), Math.Max(120, viewHost.ClientSize.Height));
            viewHost.Controls.Add(treeView);
        }

        /// <summary>勾选变了：把已建好的视图里的勾、套装计数、方案列同步一遍（不重建，滚动位置不动）。</summary>
        internal void SyncCatViews()
        {
            foreach (CatView v in catViews.Values) v.RefreshChecks();
            if (treeView != null && treeView.Visible) treeView.Sync();
            UpdateSel();
            UpdateSchemeLabel();
            SyncTabs();
        }

        /// <summary>页签上的数字：每类共几个、这套方案里有几个、现在勾了几个。</summary>
        internal void SyncTabs()
        {
            if (tabs == null) return;
            Dictionary<string, int[]> d = new Dictionary<string, int[]>(StringComparer.Ordinal);
            foreach (string c in Catalog.Cats) d[c] = new int[3];        // total / 方案内 / 已选
            foreach (ModGroup g in Catalog.Groups)
            {
                int[] a;
                if (!d.TryGetValue(g.Cat, out a)) continue;
                a[0]++;
                if (InScheme(g)) a[1]++;
                if (CountChecked(g) > 0) a[2]++;
            }
            tabs.SetData(d);
        }

        /// <summary>右栏标题栏：在看方案树还是某一类，顺带把这一类的数字写出来。</summary>
        internal void UpdateModCaption()
        {
            if (gMod == null) return;
            if (tabCur == "#tree")
            {
                int sc = treeView == null ? 0 : treeView.SlotCount;
                int sk = treeView == null ? 0 : treeView.SlotCheckedCount;
                gMod.Caption = "方案树　" + sc + " 个可替换的位置 · 已选 " + sk
                             + "　点一个位置，右边列它的候选";
                return;
            }
            int total = 0, inn = 0, on = 0;
            foreach (ModGroup g in Catalog.Groups)
            {
                if (g.Cat != tabCur) continue;
                total++;
                if (InScheme(g)) inn++;
                if (CountChecked(g) > 0) on++;
            }
            gMod.Caption = "MOD 库 · " + tabCur + "　" + total + " 个 · 方案内 " + inn + " · 已选 " + on;
        }

        /// <summary>页签条：方案树 + 六个大类，每格写着「这类有几个、方案里放了几个」。</summary>
        internal sealed class CatTabs : Panel
        {
            private readonly MainForm mf;
            private readonly List<Rectangle> rects = new List<Rectangle>();
            private readonly List<string> keys = new List<string>();
            private readonly Dictionary<string, int[]> data = new Dictionary<string, int[]>(StringComparer.Ordinal);
            private int hover = -1;

            public CatTabs(MainForm owner)
            {
                mf = owner;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Surface;
                Cursor = Cursors.Hand;
                Height = 32;
                if (owner != null && owner.tips != null)
                    owner.tips.SetToolTip(this, "点页签换类。右侧数字是「这套方案在这一类里有几个 / 这一类一共几个」。");
            }

            public void SetData(Dictionary<string, int[]> d)
            {
                data.Clear();
                if (d != null) foreach (KeyValuePair<string, int[]> kv in d) data[kv.Key] = kv.Value;
                Invalidate();
            }

            private int TreeW { get { return 74; } }

            private int CellW
            {
                get
                {
                    int n = Catalog.Cats.Length;
                    int avail = Width - 2 - TreeW;
                    if (n <= 0) return avail;
                    return Math.Max(74, avail / n);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.Surface);
                rects.Clear(); keys.Clear();
                using (Pen p = new Pen(Theme.Line)) g.DrawLine(p, 0, Height - 1, Width - 1, Height - 1);

                int x = 2;
                DrawTab(g, ref x, "#tree", "方案树", 0, 0, 0);
                foreach (string c in Catalog.Cats)
                {
                    int[] a;
                    if (!data.TryGetValue(c, out a)) a = new int[3];
                    DrawTab(g, ref x, c, c, a[0], a[1], a[2]);
                }
            }

            private void DrawTab(Graphics g, ref int x, string key, string label, int total, int inn, int on)
            {
                int w = key == "#tree" ? TreeW : CellW;
                if (x + w > Width - 1) w = Math.Max(40, Width - 1 - x);
                Rectangle r = new Rectangle(x, 0, w, Height - 1);
                int idx = rects.Count;
                rects.Add(r); keys.Add(key);

                bool sel = mf.TabCurrent == key;
                bool hv = hover == idx;
                int numW = 0;
                string num = "";
                if (key.Length > 0 && key != "#tree" && w >= 96) { num = inn + "/" + total; numW = 46; }
                else if (key.Length > 0 && key != "#tree" && w >= 82) { num = inn.ToString(); numW = 18; }

                Color ink = sel ? Theme.Ink : (hv ? Theme.Ink2 : Theme.Ink3);
                Font f = sel ? Theme.UIBold : Theme.UI;
                TextRenderer.DrawText(g, label, f, new Rectangle(r.X + 8, 1, w - 16 - numW, Height - 4), ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (num.Length > 0)
                {
                    // 方案里有东西就给个绿数，一眼看出这套在一类里放了多少
                    Color nc = inn > 0 ? Theme.Ok : Theme.Ink3;
                    TextRenderer.DrawText(g, num, Theme.UISmall,
                        new Rectangle(r.Right - numW - 8, 1, numW, Height - 4), nc,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                if (sel)
                {
                    Color line = key == "#tree" ? Theme.Ink2 : CatInk(key);
                    using (SolidBrush b = new SolidBrush(line))
                        g.FillRectangle(b, r.X + 6, Height - 3, w - 12, 2);
                }
                else if (hv)
                {
                    using (Pen p = new Pen(Theme.LineSoft)) g.DrawLine(p, r.X + 6, Height - 2, r.Right - 6, Height - 2);
                }
                x += w;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int h = -1;
                for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(e.Location)) { h = i; break; }
                if (h != hover) { hover = h; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (hover != -1) { hover = -1; Invalidate(); }
                base.OnMouseLeave(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                for (int i = 0; i < rects.Count; i++)
                {
                    if (!rects[i].Contains(e.Location)) continue;
                    mf.ShowCatView(keys[i]);
                    return;
                }
                base.OnMouseUp(e);
            }
        }

        /// <summary>一个大类的内嵌视图（不是窗口）。</summary>
        internal sealed class CatView : UserControl
        {
            private readonly MainForm mf;
            private readonly string cat;
            private ListView lv;
            private CheckBox chkOnly;
            private ComboBox cboShow;
            private Label lblFoot, hint;
            private bool filling;
            private bool syncing;          // 正在同步勾选：这时的重入直接跳过，否则会互相改状态
            private int listed;

            public string Cat { get { return cat; } }

            public CatView(MainForm owner, string cat)
            {
                mf = owner;
                this.cat = cat;
                Font = Theme.UI;
                BackColor = Theme.Surface;
                DoubleBuffered = true;

                chkOnly = new CheckBox(); chkOnly.Text = "只看方案内";
                chkOnly.SetBounds(12, 10, 96, 20);
                Theme.Dress(chkOnly);
                chkOnly.CheckedChanged += delegate { Fill(); };
                Controls.Add(chkOnly);

                FlatButton btnAll = new FlatButton(); btnAll.Text = "全选本类";
                btnAll.SetBounds(112, 8, 78, Theme.H);
                btnAll.Click += delegate { mf.SetAllIn(lv, true); };
                Controls.Add(btnAll);

                FlatButton btnNone = new FlatButton(); btnNone.Text = "清空";
                btnNone.SetBounds(196, 8, 50, Theme.H);
                btnNone.Click += delegate { mf.SetAllIn(lv, false); };
                Controls.Add(btnNone);

                // 显示：按「装到游戏里没有」筛。机房最常见的问题是「这台到底装过没」，不是「方案里有什么」
                Label lShow = new Label(); lShow.Text = "显示"; lShow.SetBounds(252, 12, 30, 18);
                Theme.Dress(lShow); Controls.Add(lShow);
                cboShow = new ComboBox(); cboShow.SetBounds(284, 8, 104, 24);
                cboShow.DropDownStyle = ComboBoxStyle.DropDownList; Theme.Dress(cboShow);
                cboShow.Items.Add("全部");
                cboShow.Items.Add("已装");
                cboShow.Items.Add("未装");
                cboShow.Items.Add("已装但关闭");
                cboShow.SelectedIndex = 0;
                cboShow.SelectedIndexChanged += delegate { Fill(); };
                Controls.Add(cboShow);

                FlatButton btnRescan = new FlatButton(); btnRescan.Text = "重扫";
                btnRescan.SetBounds(396, 8, 50, Theme.H);
                btnRescan.Click += delegate { mf.RescanMods(); };
                Controls.Add(btnRescan);

                hint = new Label();
                hint.Text = "双击套装行展开，可单个勾选";
                hint.Font = Theme.UISmall;
                hint.ForeColor = Theme.Ink3;
                hint.BackColor = Color.Transparent;
                hint.AutoSize = false;
                hint.TextAlign = ContentAlignment.MiddleRight;
                hint.SetBounds(250, 12, 260, 18);
                Controls.Add(hint);

                lv = new ListView();
                lv.View = View.Details;
                lv.CheckBoxes = true;
                lv.FullRowSelect = true;
                lv.BorderStyle = BorderStyle.None;
                lv.BackColor = Theme.Surface;
                lv.ForeColor = Theme.Ink;
                lv.Font = Theme.UI;
                lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
                lv.ShowItemToolTips = true;
                ImageList pad = new ImageList(); pad.ImageSize = new Size(1, 22);
                lv.SmallImageList = pad;
                lv.Columns.Add("名称", 300);
                lv.Columns.Add("方案", 58);
                lv.Columns.Add("改了什么", 84);
                lv.Columns.Add("大小", 70).TextAlign = HorizontalAlignment.Right;
                lv.Columns.Add("细分", 132);
                lv.Columns.Add("状态", 96);
                lv.ItemChecked += delegate(object s, ItemCheckedEventArgs e)
                {
                    if (filling) return;
                    mf.OnRowChecked(lv, e);
                };
                lv.DoubleClick += delegate { mf.OpenRowAt(lv); };
                // 右键先把鼠标底下那一行选中：WinForms 的 ListView 右键不会自动选中，
                // 否则「重命名 / 只装这个 MOD」会作用在上一次左键点的那行上。
                lv.MouseDown += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Right) return;
                    ListViewItem hit = lv.GetItemAt(e.X, e.Y);
                    if (hit == null || hit.Selected) return;
                    lv.SelectedItems.Clear();
                    hit.Selected = true;
                    hit.Focused = true;
                };
                lv.ContextMenuStrip = mf.BuildRowMenu(lv);
                lv.Resize += delegate { LayoutCols(); };
                lv.SelectedIndexChanged += delegate { if (mf.tabs != null) mf.SyncTabs(); };
                lv.KeyDown += OnViewKey;
                Controls.Add(lv);

                lblFoot = new Label();
                lblFoot.Font = Theme.UISmall;
                lblFoot.ForeColor = Theme.Ink3;
                lblFoot.BackColor = Color.Transparent;
                lblFoot.AutoEllipsis = true;
                Controls.Add(lblFoot);

                Resize += delegate { LayoutSelf(); };
                LayoutSelf();
            }

            private void OnViewKey(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { Fill(); e.Handled = true; }
                if (e.Control && e.KeyCode == Keys.A)
                {
                    lv.BeginUpdate();
                    foreach (ListViewItem li in lv.Items) li.Selected = true;
                    lv.EndUpdate();
                    e.Handled = true;
                }
            }

            // 嵌在别人的面板里，位置自己算，别指望 Anchor 会跟着父容器一起动
            private void LayoutSelf()
            {
                int W = ClientSize.Width, H = ClientSize.Height;
                if (W <= 0 || H <= 0) return;
                if (hint != null) hint.SetBounds(Math.Max(250, W - 12 - 260), 12, Math.Min(260, Math.Max(80, W - 274)), 18);
                lv.SetBounds(12, 42, Math.Max(120, W - 24), Math.Max(60, H - 42 - 24));
                lblFoot.SetBounds(12, H - 21, Math.Max(60, W - 24), 18);
                LayoutCols();
            }

            private void LayoutCols()
            {
                if (lv == null || lv.Columns.Count < 6) return;
                int w = lv.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6;
                const int markW = 58, subjW = 76, sizeW = 62, stateW = 96;
                int rest = w - markW - subjW - sizeW - stateW;      // 名称 + 细分 分剩下的
                int nameW = Math.Max(180, Math.Min(620, rest - 120));
                int subW = Math.Max(80, rest - nameW);
                lv.Columns[0].Width = nameW;
                lv.Columns[1].Width = markW;
                lv.Columns[2].Width = subjW;
                lv.Columns[3].Width = sizeW;
                lv.Columns[4].Width = subW;
                lv.Columns[5].Width = stateW;
            }

            /// <summary>重建列表：这一类的全部条目，方案内的排前面。</summary>
            public void Fill()
            {
                if (lv == null || lv.IsDisposed) return;
                string kw = mf.SearchText;          // 搜索框只有一个，就在主窗口顶上
                bool only = chkOnly.Checked;
                int show = cboShow == null ? 0 : cboShow.SelectedIndex;   // 0 全部 / 1 已装 / 2 未装 / 3 已装但关闭
                List<ModGroup> pass = new List<ModGroup>();
                foreach (ModGroup g in Catalog.Groups)
                {
                    if (g.Cat != cat) continue;
                    if (only && !mf.InScheme(g)) continue;
                    if (show > 0 && !MainForm.MatchShow(g, show)) continue;
                    if (kw.Length > 0 && !mf.MatchGroup(g, kw)) continue;
                    pass.Add(g);
                }
                pass.Sort(delegate(ModGroup a, ModGroup b)
                {
                    bool x = mf.InScheme(a), y = mf.InScheme(b);
                    if (x != y) return x ? -1 : 1;                 // 方案内的先露出来
                    return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
                });

                bool prev = mf.refreshing;
                filling = true;
                mf.refreshing = true;
                lv.BeginUpdate();
                try
                {
                    lv.Items.Clear();
                    foreach (ModGroup g in pass)
                    {
                        lv.Items.Add(mf.MakeRow(g));
                        // 展开过的套装：底下挂上每个 vpk，可以只勾其中一个
                        if (g.IsSet && mf.IsExpanded(g))
                            foreach (ModItem p in g.Parts) lv.Items.Add(mf.MakePartRow(g, p));
                    }
                }
                finally { lv.EndUpdate(); mf.refreshing = prev; filling = false; }
                listed = pass.Count;
                UpdateFoot();
                mf.SyncTabs();
            }

            /// <summary>就地展开 / 收起一个套装的子件行。不重建整张表，滚动位置和选中都不动。</summary>
            internal void SyncGroup(ModGroup g)
            {
                if (lv == null || lv.IsDisposed || g == null) return;
                int idx = -1;
                for (int i = 0; i < lv.Items.Count; i++) if (lv.Items[i].Tag == g) { idx = i; break; }
                if (idx < 0) { Fill(); return; }        // 这行被搜索/筛选挡掉了，整表重来

                bool want = mf.IsExpanded(g);
                bool has = idx + 1 < lv.Items.Count && lv.Items[idx + 1].Tag is ModItem;

                bool prev = mf.refreshing;
                mf.refreshing = true;
                filling = true;
                lv.BeginUpdate();
                try
                {
                    if (want && !has)
                    {
                        for (int k = 0; k < g.Parts.Count; k++)
                            lv.Items.Insert(idx + 1 + k, mf.MakePartRow(g, g.Parts[k]));
                    }
                    else if (!want && has)
                    {
                        while (idx + 1 < lv.Items.Count && lv.Items[idx + 1].Tag is ModItem)
                            lv.Items.RemoveAt(idx + 1);
                    }
                    lv.Items[idx].Text = mf.GroupRowText(g);
                    lv.Items[idx].EnsureVisible();
                }
                finally { lv.EndUpdate(); mf.refreshing = prev; filling = false; }
                UpdateFoot();
            }

            /// <summary>外部改了勾选：只更新现有行，不重建（滚动和选中都不跳）。</summary>
            public void RefreshChecks()
            {
                if (lv == null || lv.IsDisposed || syncing) return;
                syncing = true;
                try { RefreshChecksCore(); }
                catch (Exception ex) { Core.Log("FAIL", "REFRESH 失败 " + ex.ToString()); }
                finally { syncing = false; }
            }

            private void RefreshChecksCore()
            {
                bool prev = mf.refreshing;
                filling = true;
                mf.refreshing = true;
                // 先拍一份快照：这期间列表可能被重建，直接按索引取会拿到已经清掉的行
                ListViewItem[] rows = new ListViewItem[lv.Items.Count];
                try { lv.Items.CopyTo(rows, 0); }
                catch { rows = new ListViewItem[0]; }
                lv.BeginUpdate();
                try
                {
                    foreach (ListViewItem li in rows)
                    {
                        if (li == null) continue;
                        // 展开出来的单件行：勾状态跟着 checkedKeys 走
                        ModItem one = li.Tag as ModItem;
                        if (one != null)
                        {
                            bool w = mf.IsChecked(one.Key);
                            if (li.Checked != w) li.Checked = w;
                            continue;
                        }
                        ModGroup g = li.Tag as ModGroup;
                        if (g == null || g.Parts == null) continue;
                        int n = mf.CountChecked(g);
                        Color c;
                        string t = mf.SchemeMark(g, out c);
                        li.Text = mf.GroupRowText(g, n);
                        if (li.SubItems.Count > 1)
                        {
                            li.SubItems[1].Text = t;
                            li.SubItems[1].ForeColor = c;
                        }
                        // 组行：套装里勾了任意一件就显示勾（filling/refreshing 挡着，不会回头改状态）
                        bool want = n > 0;
                        if (li.Checked != want) li.Checked = want;
                    }
                }
                finally { lv.EndUpdate(); mf.refreshing = prev; filling = false; }
                UpdateFoot();
            }

            private void UpdateFoot()
            {
                int total = 0, inn = 0, on = 0, instN = 0, offN = 0, partN = 0;
                foreach (ModGroup g in Catalog.Groups)
                {
                    if (g.Cat != cat) continue;
                    total++;
                    if (mf.InScheme(g)) inn++;
                    if (mf.CountChecked(g) > 0) on++;
                    int all, inst, live, game;
                    MainForm.CountState(g, out all, out inst, out live, out game);
                    if (inst > 0) instN++;
                    if (inst > 0 && live == 0) offN++;
                    if (inst > 0 && inst < all) partN++;
                }
                string st = "已装 " + instN
                          + (partN > 0 ? "（部分 " + partN + "）" : "")
                          + (offN > 0 ? "（关 " + offN + "）" : "");
                string s = (listed == total ? "本类 " + total + " 个" : "筛出 " + listed + " 个（本类 " + total + "）")
                         + " · " + st + " · 方案内 " + inn + " · 已选 " + on;
                lblFoot.Text = s;
                if (mf.tabCur == cat) mf.UpdateModCaption();
            }

            public void FocusList() { lv.Focus(); }

            /// <summary>自测用：直接拿这块列表。</summary>
            internal ListView List { get { return lv; } }

            internal void SetOnlyScheme(bool on) { chkOnly.Checked = on; }

            /// <summary>自测用：勾/取消第一行，返回它的名字。</summary>
            internal string ToggleFirst()
            {
                if (lv.Items.Count == 0) return "（空）";
                ListViewItem li = lv.Items[0];
                li.Checked = !li.Checked;
                Application.DoEvents();
                ModGroup g = li.Tag as ModGroup;
                return g == null ? "?" : g.Name;
            }

            /// <summary>自测用：第一行「方案」列现在写的是什么。</summary>
            internal string FirstMark()
            {
                if (lv.Items.Count == 0) return "（空）";
                return lv.Items[0].SubItems.Count > 1 ? lv.Items[0].SubItems[1].Text : "?";
            }

            /// <summary>自测用：前 N 行的「名称 ⇒ 分类列」，用来核对「已装」这类文本。</summary>
            internal string DumpRows(int n)
            {
                if (lv == null || lv.Items.Count == 0) return "（空）";
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < lv.Items.Count && i < n; i++)
                {
                    ListViewItem li = lv.Items[i];
                    sb.Append(li.Text);
                    if (li.SubItems.Count > 4) sb.Append(" ⇒ ").Append(li.SubItems[4].Text);
                    sb.Append(" ／ ");
                }
                return sb.ToString();
            }

            internal int DebugRows() { return lv == null ? 0 : lv.Items.Count; }

            /// <summary>自测用：某个套装行现在显示的文字（含「已选 x/y」）。</summary>
            internal string GroupText(ModGroup g)
            {
                if (lv == null) return "?";
                foreach (ListViewItem li in lv.Items) if (li.Tag == g) return li.Text;
                return "（找不到这行）";
            }

            /// <summary>自测用：勾/取消某个套装下面的第一个子件行，返回它的名字。</summary>
            internal string ToggleFirstPart(ModGroup g)
            {
                if (lv == null) return "?";
                foreach (ListViewItem li in lv.Items)
                {
                    ModItem p = li.Tag as ModItem;
                    if (p == null || p.GroupKey != g.Key) continue;
                    li.Checked = !li.Checked;
                    Application.DoEvents();
                    return p.Name;
                }
                return "（没找到子件行）";
            }

            /// <summary>自测用：勾/取消某个套装行（走正常的勾选事件）。</summary>
            internal void CheckGroupRow(ModGroup g, bool on)
            {
                if (lv == null) return;
                foreach (ListViewItem li in lv.Items)
                {
                    if (li.Tag != g) continue;
                    if (li.Checked != on) li.Checked = on;
                    Application.DoEvents();
                    return;
                }
            }

            /// <summary>自测用：这个套装下面挂着的子件行里，有几行是勾着的。</summary>
            internal int PartCheckedRows(ModGroup g)
            {
                int n = 0;
                if (lv == null) return 0;
                foreach (ListViewItem li in lv.Items)
                {
                    ModItem p = li.Tag as ModItem;
                    if (p != null && p.GroupKey == g.Key && li.Checked) n++;
                }
                return n;
            }

            internal void CheckAll(bool on)
            {
                lv.BeginUpdate();
                foreach (ListViewItem li in lv.Items) li.Checked = on;
                lv.EndUpdate();
            }

            /// <summary>自测用：把这行统计成一句话，方便和日志对账。</summary>
            internal string Stats()
            {
                int marked = 0, plus = 0, minus = 0;
                string minusNames = "";
                foreach (ListViewItem li in lv.Items)
                {
                    string t = li.SubItems[1].Text;
                    if (t == "方案内") marked++;
                    else if (t == "新增") plus++;
                    else if (t == "已取消") { minus++; minusNames += " | " + li.Text; }
                }
                return "列表行=" + lv.Items.Count + " 方案内=" + marked + " 新增=" + plus
                     + " 已取消=" + minus + minusNames;
            }

            internal void Shot(string path)
            {
                try
                {
                    using (Bitmap bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height)))
                    {
                        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (Exception ex) { Core.Log("WARN", "类视图截图失败: " + ex.Message); }
            }

            /// <summary>从别处定位过来：必要时清掉筛选，再选中那一行。</summary>
            internal void RevealItem(ModItem it, ModGroup g)
            {
                if (chkOnly.Checked) chkOnly.Checked = false;     // 触发 Fill
                mf.ClearSearchIfAny();                            // 主窗口的搜索词也挡着，先清掉
                if (lv.Items.Count == 0) Fill();
                foreach (ListViewItem li in lv.Items)
                {
                    if (li.Tag != g) continue;
                    li.Selected = true;
                    li.Focused = true;
                    li.EnsureVisible();
                    FocusList();
                    return;
                }
                Fill();
                foreach (ListViewItem li in lv.Items)
                {
                    if (li.Tag != g) continue;
                    li.Selected = true;
                    li.Focused = true;
                    li.EnsureVisible();
                    return;
                }
                Core.Log("WARN", "类视图里定位不到这一行：" + it.Name);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Installer
{
    // 每个大类一个独立窗口：自己挑、自己勾，主窗口只负责摆磁贴和汇总。
    // 勾选状态是全局的（checkedKeys 在 MainForm 上），所以几个窗口同时开着也不会打架。
    public partial class MainForm
    {
        private readonly Dictionary<string, CatWindow> catWins = new Dictionary<string, CatWindow>(StringComparer.Ordinal);
        private readonly Dictionary<string, CatTile> catTiles = new Dictionary<string, CatTile>(StringComparer.Ordinal);

        /// <summary>打开（或激活已开着的）某个大类的窗口。</summary>
        internal void OpenCatWindow(string cat)
        {
            CatWindow w;
            if (catWins.TryGetValue(cat, out w))
            {
                if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
                w.Activate();
                return;
            }
            w = new CatWindow(this, cat);
            catWins[cat] = w;

            // 摆位：先贴着主窗口右边放，一列放满了就叠着偏移，别跑出屏幕
            int idx = 0;
            foreach (CatWindow o in catWins.Values) if (o != w) idx++;
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int x = Right + 8 + (idx % 3) * 24;
            int y = Top + 40 + (idx % 6) * 26;
            if (x + w.Width > wa.Right) x = Math.Max(wa.Left + 8, Left + 40 + idx * 26);
            if (y + w.Height > wa.Bottom) y = Math.Max(wa.Top + 8, wa.Bottom - w.Height - 8);
            w.Location = new Point(x, y);

            w.FormClosed += delegate { catWins.Remove(cat); SyncTileStates(); };
            w.Show(this);
            SyncTileStates();
        }

        internal void CloseAllCatWindows()
        {
            List<CatWindow> all = new List<CatWindow>(catWins.Values);
            catWins.Clear();
            foreach (CatWindow w in all)
            {
                if (w.IsDisposed) continue;

                try { w.Close(); } catch { }
            }
        }

        /// <summary>勾选变了：所有开着的类窗口把勾、套装计数、方案列同步一遍（不重建，滚动位置不动）。</summary>
        internal void SyncCatViews()
        {
            List<CatWindow> ws = new List<CatWindow>(catWins.Values);   // 同步过程中可能有窗口关掉
            foreach (CatWindow w in ws) w.RefreshChecks();
            UpdateSel();
        }

        internal bool IsCatOpen(string cat) { return catWins.ContainsKey(cat); }

        /// <summary>磁贴上的字要跟着勾选、方案、搜索变。</summary>
        internal void SyncTileStates()
        {
            if (catTiles.Count == 0) return;
            string kw = txtSearch == null ? "" : txtSearch.Text.Trim();
            foreach (CatTile t in catTiles.Values)
            {
                int total = 0, inn = 0, on = 0;
                List<string> names = new List<string>();
                foreach (ModGroup g in Catalog.Groups)
                {
                    if (g.Cat != t.Cat) continue;
                    if (kw.Length > 0 && !MatchGroup(g, kw)) continue;
                    total++;
                    if (InScheme(g)) { inn++; if (names.Count < 3) names.Add(g.Name); }
                    if (CountChecked(g) > 0) on++;
                }
                // 方案里这一类没东西（或只有一两个）时，补几个类里别的名字把磁贴填满：
                // 空着一大片什么信息都没有，不如让人看见这一类大概是什么货色
                if (names.Count < 3)
                {
                    foreach (ModGroup g in Catalog.Groups)
                    {
                        if (names.Count >= 3) break;
                        if (g.Cat != t.Cat) continue;
                        if (kw.Length > 0 && !MatchGroup(g, kw)) continue;
                        if (InScheme(g)) continue;
                        names.Add(g.Name);
                    }
                }
                t.Update(total, inn, on, names, catWins.ContainsKey(t.Cat), schemeKeys.Count > 0);
            }
            int tot = 0;
            foreach (ModGroup g in Catalog.Groups) tot++;
            gMod.Caption = "MOD 库（U 盘的 vpk + 压缩包）   " + tot + " 个 MOD · "
                         + Catalog.Cats.Length + " 类，点磁贴打开";
        }

        // 一格磁贴：类名 + 总数 + 方案内几个 + 方案内前几条名字
        internal sealed class CatTile : Panel
        {
            public readonly string Cat;
            private int total, inn, on;
            private bool open, hover, hasScheme;
            private readonly List<string> names = new List<string>();

            public CatTile(string cat)
            {
                Cat = cat;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Bg;
                Font = Theme.UI;
                Cursor = Cursors.Hand;
            }

            public void Update(int total, int inn, int on, List<string> names, bool open, bool hasScheme)
            {
                this.total = total; this.inn = inn; this.on = on; this.open = open; this.hasScheme = hasScheme;
                this.names.Clear();
                if (names != null) this.names.AddRange(names);
                Invalidate();
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Theme.Bg);

                Color accent = CatInk(Cat);
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, r);
                using (Pen p = new Pen(hover ? accent : Theme.Line)) g.DrawRectangle(p, r);
                using (SolidBrush b = new SolidBrush(accent)) g.FillRectangle(b, 0, 0, Width - 1, 3);

                TextRenderer.DrawText(g, Cat, Theme.UIBold, new Rectangle(12, 7, Width - 100, 20), Theme.Ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, total + " 个", Theme.UISmall, new Rectangle(Width - 88, 8, 76, 18), Theme.Ink3,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                // 第二行：方案内几个 / 已选几个；没有方案时就只报已选
                string sub = hasScheme ? ("方案内 " + inn + " · 已选 " + on) : ("已选 " + on);
                TextRenderer.DrawText(g, sub, Theme.UISmall, new Rectangle(12, 27, Width - 96, 16),
                    inn > 0 ? Theme.Ok : Theme.Ink3,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, open ? "已打开" : "打开", Theme.UISmall, new Rectangle(Width - 64, 27, 52, 16),
                    open ? Theme.Ink3 : accent,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                using (Pen p = new Pen(Theme.LineSoft)) g.DrawLine(p, 12, 45, Width - 13, 45);

                // 下面几条：有方案就列方案内的名字 —— 不开窗也知道这套里放了什么
                int y = 49;
                if (names.Count == 0)
                {
                    string tip = total == 0 ? "这一类没有匹配" : "点开看全部 " + total + " 个";
                    TextRenderer.DrawText(g, tip, Theme.UISmall, new Rectangle(12, y, Width - 24, 16), Theme.Ink3,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    return;
                }
                int room = (Height - y - 4) / 15;
                int show = Math.Min(names.Count, Math.Max(0, room));
                int strong = Math.Min(inn, show);
                for (int i = 0; i < show; i++)
                {
                    TextRenderer.DrawText(g, names[i], Theme.UISmall, new Rectangle(12, y + i * 15, Width - 24, 15),
                        i < strong ? Theme.Ink2 : Theme.Ink3,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                return;
            }
        }

        /// <summary>一个大类的独立窗口。</summary>
        internal sealed class CatWindow : Form
        {
            private readonly MainForm mf;
            private readonly string cat;
            private ListView lv;
            private TextBox txtSearch;
            private CheckBox chkOnly;
            private Label lblFoot;
            private bool filling;
            private bool syncing;          // 正在同步勾选：这时的重入直接跳过，否则会互相改状态
            private int listed;

            public string Cat { get { return cat; } }

            public CatWindow(MainForm owner, string cat)
            {
                mf = owner;
                this.cat = cat;
                Text = "MOD · " + cat;
                Font = Theme.UI;
                ClientSize = new Size(706, 552);
                MinimumSize = new Size(500, 340);
                StartPosition = FormStartPosition.Manual;
                BackColor = Theme.Surface;
                ShowIcon = false;
                KeyPreview = true;

                Label l0 = new Label(); l0.Text = "搜"; l0.SetBounds(12, 13, 24, 18);
                l0.Font = Theme.UISmall; l0.ForeColor = Theme.Ink2; l0.BackColor = Color.Transparent;
                Controls.Add(l0);

                txtSearch = new TextBox(); txtSearch.SetBounds(38, 10, 210, 24);
                Theme.Dress(txtSearch);
                txtSearch.Text = mf.SearchText;          // 继承主窗口的搜索词：在主窗口搜了「AK」，开窗直接就在结果里
                txtSearch.TextChanged += delegate { Fill(); };
                Controls.Add(txtSearch);

                chkOnly = new CheckBox(); chkOnly.Text = "只看方案内";
                chkOnly.SetBounds(256, 12, 96, 20);
                Theme.Dress(chkOnly);
                chkOnly.CheckedChanged += delegate { Fill(); };
                Controls.Add(chkOnly);

                FlatButton btnAll = new FlatButton(); btnAll.Text = "全选本类";
                btnAll.SetBounds(356, 10, 78, Theme.H);
                btnAll.Click += delegate { mf.SetAllIn(lv, true); };
                Controls.Add(btnAll);

                FlatButton btnNone = new FlatButton(); btnNone.Text = "清空";
                btnNone.SetBounds(440, 10, 50, Theme.H);
                btnNone.Click += delegate { mf.SetAllIn(lv, false); };
                Controls.Add(btnNone);

                FlatButton btnProf = new FlatButton(); btnProf.Text = "方案…";
                btnProf.SetBounds(498, 10, 56, Theme.H);
                btnProf.Click += delegate { mf.ManageProfiles(); };
                Controls.Add(btnProf);

                lv = new ListView();
                lv.SetBounds(12, 42, ClientSize.Width - 24, ClientSize.Height - 42 - 26);
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
                lv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                lv.ItemChecked += delegate(object s, ItemCheckedEventArgs e)
                {
                    if (filling) return;
                    mf.OnRowChecked(lv, e);
                };
                lv.DoubleClick += delegate { mf.OpenRowAt(lv); };
                lv.ContextMenuStrip = mf.BuildRowMenu(lv);
                lv.Resize += delegate { LayoutCols(); };
                lv.SelectedIndexChanged += delegate { UpdateFoot(); };
                Controls.Add(lv);

                lblFoot = new Label();
                lblFoot.SetBounds(12, ClientSize.Height - 22, ClientSize.Width - 24, 18);
                lblFoot.Font = Theme.UISmall;
                lblFoot.ForeColor = Theme.Ink3;
                lblFoot.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                lblFoot.BackColor = Color.Transparent;
                Controls.Add(lblFoot);

                KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.F5) { Fill(); e.Handled = true; }
                    if (e.Control && e.KeyCode == Keys.A)
                    {
                        lv.BeginUpdate();
                        foreach (ListViewItem li in lv.Items) li.Selected = true;
                        lv.EndUpdate();
                        e.Handled = true;
                    }
                };

                Fill();
                LayoutCols();
            }

            private void LayoutCols()
            {
                if (lv == null || lv.Columns.Count < 5) return;
                int w = lv.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6;
                const int markW = 58, subjW = 84, sizeW = 70;
                int nameW = Math.Max(180, Math.Min(420, w - markW - subjW - sizeW - 132));
                lv.Columns[0].Width = nameW;
                lv.Columns[1].Width = markW;
                lv.Columns[2].Width = subjW;
                lv.Columns[3].Width = sizeW;
                lv.Columns[4].Width = Math.Max(90, w - nameW - markW - subjW - sizeW);
            }

            /// <summary>重建列表：这一类的全部条目，方案内的排前面。</summary>
            public void Fill()
            {
                string kw = txtSearch.Text.Trim();
                bool only = chkOnly.Checked;
                List<ModGroup> pass = new List<ModGroup>();
                foreach (ModGroup g in Catalog.Groups)
                {
                    if (g.Cat != cat) continue;
                    if (only && !mf.InScheme(g)) continue;
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
                    foreach (ModGroup g in pass) lv.Items.Add(mf.MakeRow(g));
                }
                finally { lv.EndUpdate(); mf.refreshing = prev; filling = false; }
                listed = pass.Count;
                UpdateFoot();
                mf.SyncTileStates();
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
                // 先拍一份快照：这期间列表可能被别人重建，直接按索引取会拿到已经清掉的行
                ListViewItem[] rows = new ListViewItem[lv.Items.Count];
                try { lv.Items.CopyTo(rows, 0); }
                catch { rows = new ListViewItem[0]; }
                lv.BeginUpdate();
                try
                {
                    foreach (ListViewItem li in rows)
                    {
                        if (li == null) continue;
                        ModGroup g = li.Tag as ModGroup;
                        if (g == null || g.Parts == null) continue;
                        int n = mf.CountChecked(g);
                        Color c;
                        string t = mf.SchemeMark(g, out c);
                        if (g.IsSet) li.Text = RowName(g, n);
                        if (li.SubItems.Count > 1)
                        {
                            li.SubItems[1].Text = t;
                            li.SubItems[1].ForeColor = c;
                        }
                        // 勾选状态不用动：一个套装只属于一个大类、只会出现在一个窗口里。
                        // 在这里顺手写 li.Checked 会触发一遍勾选通知，把状态倒着改回去。
                    }
                }
                finally { lv.EndUpdate(); mf.refreshing = prev; filling = false; }
                UpdateFoot();
            }

            private void UpdateFoot()
            {
                int total = 0, inn = 0, on = 0;
                foreach (ModGroup g in Catalog.Groups)
                {
                    if (g.Cat != cat) continue;
                    total++;
                    if (mf.InScheme(g)) inn++;
                    if (mf.CountChecked(g) > 0) on++;
                }
                string s = listed == total
                    ? "本类 " + total + " 个 · 方案内 " + inn + " · 已选 " + on
                    : "筛出 " + listed + " 个（本类 " + total + "） · 方案内 " + inn + " · 已选 " + on;
                lblFoot.Text = s;
                Text = "MOD · " + cat + "　" + total + " 个 · 方案内 " + inn + " · 已选 " + on;
            }

            public void FocusList() { lv.Focus(); }

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
                    using (Bitmap bmp = new Bitmap(Width, Height))
                    {
                        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (Exception ex) { Core.Log("WARN", "类窗口截图失败: " + ex.Message); }
            }

            /// <summary>从别处定位过来：必要时清掉筛选，再选中那一行。</summary>
            internal void RevealItem(ModItem it, ModGroup g)
            {
                if (chkOnly.Checked) chkOnly.Checked = false;     // 触发 Fill
                if (txtSearch.Text.Length > 0) txtSearch.Text = "";
                if (lv.Items.Count == 0) Fill();
                foreach (ListViewItem li in lv.Items)
                {
                    if (li.Tag != g) continue;
                    li.Selected = true;
                    li.Focused = true;
                    li.EnsureVisible();
                    Activate();
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
                Core.Log("WARN", "类窗口里定位不到这一行：" + it.Name);
            }
        }
    }
}

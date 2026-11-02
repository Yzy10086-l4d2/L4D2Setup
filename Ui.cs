using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Installer
{
    public class MainForm : Form
    {
        private ComboBox txtGame;
        private Button btnBrowse, btnFind, btnInstall, btnRestore, btnOpen, btnSwitch;
        private CheckBox chkL4N, chkSkeeto, chkAsset, chkMqcam, chkDxvk, chkLaunch, chkCfg, chkSwap;
        private TextBox txtLaunch;
        private ComboBox cboCat, cboProfile;
        private GroupBox gMod, gLog, gPlat;
        private Dictionary<string, string> modTags;
        private Button btnManage;
        private TextBox txtSearch;
        private Button btnAll, btnNone, btnSaveProfile, btnApplyProfile;
        private ListView lvMods;
        private Label lblSel, lblStatus, lblConflict;
        private ProgressBar bar;
        private TextBox txtLog;
        private readonly HashSet<string> checkedRel = new HashSet<string>();
        private List<ModItem> shown = new List<ModItem>();

        public MainForm(string gameRootHint)
        {
            Text = "网吧装机台  v1.1  ——  启动秒开 + 平台 + MOD 库 + 方案 + 还原 + 并行校验 + 批量入库 + 脚本冲突";
            ClientSize = new Size(940, 684);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            StartPosition = FormStartPosition.CenterScreen;
            BuildUi();
            Core.Sink = AppendLog;

            if (gameRootHint != null && Core.TestGame(gameRootHint)) txtGame.Text = gameRootHint;
            else if (Core.TestGame(Core.Cfg.GameRoot)) txtGame.Text = Core.Cfg.GameRoot;
            else
            {
                List<string> g = Core.FindGames();
                if (g.Count > 0) { txtGame.Text = g[0]; Log("自动找到游戏: " + g[0]); }
                else Log("没自动找到游戏，点「自动找」或「浏览…」");
            }

            // 重活放到窗口显示之后做，避免启动干等
            Load += delegate { BeginInit(); };
        }

        private bool initializing = true;

        private void BeginInit()
        {
            lblStatus.Text = "正在读取 U 盘…";
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                try
                {
                    bool packOk;
                    System.Diagnostics.Stopwatch __sw = System.Diagnostics.Stopwatch.StartNew();
                    Core.CheckPackages(out packOk);
                    Core.Log("INFO", "启动计时: 包检查 " + __sw.ElapsedMilliseconds + " ms");
                    __sw.Restart();
                    Core.ScanMods();
                    Core.Log("INFO", "启动计时: 扫库 " + __sw.ElapsedMilliseconds + " ms（" + Core.ModList.Count + " 个）");
                    modTags = ModTags.Load();
                    Invoke(new Action(delegate
                    {
                        System.Diagnostics.Stopwatch __sw2 = System.Diagnostics.Stopwatch.StartNew();
                        FillCats();
                        RefreshMods();
                        Core.Log("INFO", "启动计时: 填充列表 " + __sw2.ElapsedMilliseconds + " ms");
                        __sw2.Restart();
                        ApplyConfig();
                        FillProfiles();
                        Core.Log("INFO", "启动计时: 读配置/方案 " + __sw2.ElapsedMilliseconds + " ms");
                        __sw2.Restart();
                        RefreshConflicts();
                        Core.Log("INFO", "启动计时: 冲突检查 " + __sw2.ElapsedMilliseconds + " ms");
                        initializing = false;
                        lblStatus.Text = "就绪";
                    }));
                }
                catch (Exception ex) { Core.Log("FAIL", "初始化失败: " + ex.Message); }
            });
            th.IsBackground = true;
            th.Start();
        }

        private void BuildUi()
        {
            Label l1 = new Label(); l1.Text = "游戏目录"; l1.SetBounds(12, 12, 60, 20); Controls.Add(l1);
            txtGame = new ComboBox(); txtGame.SetBounds(76, 9, 596, 24); txtGame.DropDownStyle = ComboBoxStyle.DropDown; Controls.Add(txtGame);
            btnBrowse = new Button(); btnBrowse.Text = "浏览…"; btnBrowse.SetBounds(680, 7, 80, 26); btnBrowse.Click += delegate { BrowseGame(); }; Controls.Add(btnBrowse);
            btnFind = new Button(); btnFind.Text = "自动找"; btnFind.SetBounds(766, 7, 80, 26); btnFind.Click += delegate { FindGame(); }; Controls.Add(btnFind);

            GroupBox gp = new GroupBox(); gp.Text = "装什么"; gp.SetBounds(12, 42, 440, 254); Controls.Add(gp);
            chkL4N = AddChk(gp, "L4N 平台（必装，其它都靠它）", 12, 24, true, false);
            chkSkeeto = AddChk(gp, "Skeeto 增强框架（命中反馈/准星/HUD）", 12, 52, true, true);
            chkAsset = AddChk(gp, "Skeeto 素材包 177 MB", 12, 80, true, true);
            chkMqcam = AddChk(gp, "MQCam 相机（F2/F3/F4 视角）", 12, 108, true, true);
            chkDxvk = AddChk(gp, "dxvk 防闪退（d3d9.dll）", 12, 136, true, true);
            chkLaunch = AddChk(gp, "写 Steam 启动项", 12, 164, true, true);
            chkCfg = AddChk(gp, "搬 autoexec.cfg（我的按键/设置）", 12, 192, true, true);
            Label l4 = new Label(); l4.Text = "启动项"; l4.SetBounds(12, 224, 50, 20); gp.Controls.Add(l4);
            txtLaunch = new TextBox(); txtLaunch.SetBounds(66, 221, 360, 24); gp.Controls.Add(txtLaunch);

            GroupBox gm = new GroupBox(); gm.Text = "MOD 库（U 盘上的 vpk）"; gm.SetBounds(462, 42, 466, 532); Controls.Add(gm);
            Label l2 = new Label(); l2.Text = "分类"; l2.SetBounds(12, 26, 34, 20); gm.Controls.Add(l2);
            cboCat = new ComboBox(); cboCat.SetBounds(48, 23, 190, 24); cboCat.DropDownStyle = ComboBoxStyle.DropDownList; cboCat.SelectedIndexChanged += delegate { RefreshMods(); }; gm.Controls.Add(cboCat);
            txtSearch = new TextBox(); txtSearch.SetBounds(244, 23, 116, 24); txtSearch.TextChanged += delegate { RefreshMods(); }; gm.Controls.Add(txtSearch);
            btnAll = new Button(); btnAll.Text = "全选"; btnAll.SetBounds(366, 22, 44, 26); btnAll.Click += delegate { SetAll(true); }; gm.Controls.Add(btnAll);
            btnNone = new Button(); btnNone.Text = "清空"; btnNone.SetBounds(414, 22, 44, 26); btnNone.Click += delegate { SetAll(false); }; gm.Controls.Add(btnNone);

            Label l3 = new Label(); l3.Text = "方案"; l3.SetBounds(12, 56, 34, 20); gm.Controls.Add(l3);
            cboProfile = new ComboBox(); cboProfile.SetBounds(48, 53, 186, 24); cboProfile.DropDownStyle = ComboBoxStyle.DropDownList; gm.Controls.Add(cboProfile);
            cboProfile.SelectedIndexChanged += delegate { PreviewProfile(); };
            btnSaveProfile = new Button(); btnSaveProfile.Text = "存为方案"; btnSaveProfile.SetBounds(240, 52, 90, 26); btnSaveProfile.Click += delegate { SaveProfile(); }; gm.Controls.Add(btnSaveProfile);
            btnApplyProfile = new Button(); btnApplyProfile.Text = "套用"; btnApplyProfile.SetBounds(336, 52, 60, 26); btnApplyProfile.Click += delegate { ApplyProfile(); }; gm.Controls.Add(btnApplyProfile);
            btnManage = new Button(); btnManage.Text = "管理…"; btnManage.SetBounds(400, 52, 48, 26); btnManage.Click += delegate { (new ProfileManageForm(this)).ShowDialog(this); FillProfiles(); }; gm.Controls.Add(btnManage);

            lvMods = new ListView();
            lvMods.SetBounds(12, 86, 442, 356);
            lvMods.View = View.Details;
            lvMods.CheckBoxes = true;
            lvMods.FullRowSelect = true;
            lvMods.Columns.Add("名称", 258);
            lvMods.Columns.Add("大小", 78);
            lvMods.Columns.Add("位置", 170);
            lvMods.ItemChecked += delegate(object s, ItemCheckedEventArgs e)
            {
                ModItem it = e.Item.Tag as ModItem;
                if (it == null) return;
                if (e.Item.Checked) checkedRel.Add(it.RelPath); else checkedRel.Remove(it.RelPath);
                UpdateSel();
            };
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.Items.Add("删除所选 → U 盘 _回收的MOD", null, delegate { DeleteSelectedMods(); });
            cms.Items.Add("重命名所选", null, delegate { RenameSelectedMod(); });
            cms.Items.Add("设置标签…", null, delegate { SetTags(); });
            cms.Items.Add("批量导入 vpk…", null, delegate { BulkImport(); });
            cms.Items.Add("清理同名重复（每个名字留第一个）", null, delegate { CleanDupMods(); });
            cms.Items.Add("在资源管理器里定位", null, delegate { RevealMod(); });
            lvMods.ContextMenuStrip = cms;
            lvMods.DoubleClick += delegate { ShowModInfo(); };
            gm.Controls.Add(lvMods);
            lblSel = new Label(); lblSel.SetBounds(12, 446, 442, 20); lblSel.Text = "已选 0 个"; gm.Controls.Add(lblSel);
            chkSwap = new CheckBox(); chkSwap.Text = "换整套：先清掉 addons 里不属于这次方案的 MOD（注意：换到回收站，不会真删）"; chkSwap.SetBounds(12, 466, 442, 22); gm.Controls.Add(chkSwap);
            lblConflict = new Label(); lblConflict.SetBounds(12, 490, 442, 38); lblConflict.Text = ""; lblConflict.ForeColor = Color.DarkOrange; gm.Controls.Add(lblConflict);

            GroupBox gl = new GroupBox(); gl.Text = "日志"; gl.SetBounds(12, 308, 440, 266); Controls.Add(gl);
            Button btnLog = new Button(); btnLog.Text = "打开日志"; btnLog.SetBounds(300, 2, 64, 20); btnLog.Click += delegate { try { System.Diagnostics.Process.Start("explorer.exe", Core.LogDir); } catch { } }; gl.Controls.Add(btnLog);
            Button btnEnv = new Button(); btnEnv.Text = "环境"; btnEnv.SetBounds(368, 2, 52, 20); btnEnv.Click += delegate { ShowEnv(); }; gl.Controls.Add(btnEnv);
            txtLog = new TextBox(); txtLog.SetBounds(10, 22, 420, 234); txtLog.Multiline = true; txtLog.ScrollBars = ScrollBars.Vertical; txtLog.ReadOnly = true; gl.Controls.Add(txtLog);

            btnInstall = new Button(); btnInstall.Text = "开始装"; btnInstall.SetBounds(462, 584, 128, 34); btnInstall.Font = new Font(Font, FontStyle.Bold); btnInstall.Click += delegate { DoInstall(); }; Controls.Add(btnInstall);
            btnSwitch = new Button(); btnSwitch.Text = "已装 MOD 开关"; btnSwitch.SetBounds(596, 584, 110, 34); btnSwitch.Click += delegate { DoSwitch(); }; Controls.Add(btnSwitch);
            btnRestore = new Button(); btnRestore.Text = "一键还原"; btnRestore.SetBounds(712, 584, 106, 34); btnRestore.Click += delegate { DoRestore(); }; Controls.Add(btnRestore);
            btnOpen = new Button(); btnOpen.Text = "打开游戏目录"; btnOpen.SetBounds(824, 584, 104, 34); btnOpen.Click += delegate { try { System.Diagnostics.Process.Start("explorer.exe", txtGame.Text); } catch { } }; Controls.Add(btnOpen);

            bar = new ProgressBar(); bar.SetBounds(12, 628, 700, 16); Controls.Add(bar);
            lblStatus = new Label(); lblStatus.SetBounds(12, 650, 916, 20); lblStatus.Text = "就绪"; Controls.Add(lblStatus);

            // 随窗口缩放：左右拉伸的跟着走，底部按钮贴右下
            gMod = gm; gLog = gl; gPlat = gp;
            MinimumSize = new Size(1000, 640);
            txtGame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnFind.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gp.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            gm.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lvMods.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblSel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            chkSwap.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblConflict.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            gl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            btnInstall.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSwitch.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnRestore.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnOpen.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            bar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            lvMods.Columns[0].Width = 300;
            lvMods.Columns[1].Width = 90;
            Resize += delegate { ApplyResponsive(); };
            ApplyResponsive();
        }

        private CheckBox AddChk(GroupBox p, string text, int x, int y, bool chk, bool enabled)
        {
            CheckBox c = new CheckBox();
            c.Text = text; c.SetBounds(x, y, 400, 22); c.Checked = chk; c.Enabled = enabled;
            p.Controls.Add(c);
            return c;
        }

        private void ApplyConfig()
        {
            chkDxvk.Checked = Core.Cfg.Dxvk;
            chkSkeeto.Checked = Core.Cfg.Skeeto;
            chkAsset.Checked = Core.Cfg.AssetPack;
            chkMqcam.Checked = Core.Cfg.Mqcam;
            chkLaunch.Checked = Core.Cfg.Launch;
            txtLaunch.Text = Core.Cfg.LaunchOptions;
            chkCfg.Checked = Core.Cfg.CfgFile;
            foreach (string m in Core.Cfg.Mods) checkedRel.Add(m);
            foreach (ListViewItem li in lvMods.Items)
            {
                ModItem it = li.Tag as ModItem;
                if (it != null) li.Checked = checkedRel.Contains(it.RelPath);
            }
            UpdateSel();
            Log("已载入上次勾选（" + checkedRel.Count + " 个 MOD）");
        }

        private void FillCats()
        {
            cboCat.Items.Clear();
            cboCat.Items.Add("(全部)");
            foreach (string f in Core.FolderChoices()) cboCat.Items.Add(f);
            cboCat.SelectedIndex = 0;
        }

        private void FillProfiles()
        {
            cboProfile.Items.Clear();
            List<string> ps = ProfileStore.ListProfiles();
            if (ps.Count == 0) { cboProfile.Items.Add("（还没有方案）"); cboProfile.SelectedIndex = 0; return; }
            foreach (string p in ps) cboProfile.Items.Add(p);
            cboProfile.SelectedIndex = 0;
        }

        private void RefreshMods()
        {
            string cat = cboCat.SelectedItem == null ? "(全部)" : cboCat.SelectedItem.ToString();
            string kw = txtSearch.Text.Trim();
            lvMods.BeginUpdate();
            lvMods.Items.Clear();
            shown.Clear();
            foreach (ModItem it in Core.ModList)
            {
                if (cat != "(全部)" && !it.RelPath.StartsWith(cat + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (kw.Length > 0 && it.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    string tg = ModTags.Get(modTags, it.RelPath);
                    if (tg.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                ListViewItem li = new ListViewItem(it.Name);
                li.SubItems.Add(Core.Human(it.Size));
                li.SubItems.Add(Core.RelDir(it.RelPath));
                li.Tag = it;
                li.Checked = checkedRel.Contains(it.RelPath);
                lvMods.Items.Add(li);
                shown.Add(it);
            }
            lvMods.EndUpdate();
            UpdateSel();
        }

        private void SetAll(bool on)
        {
            lvMods.BeginUpdate();
            foreach (ListViewItem li in lvMods.Items)
            {
                li.Checked = on;
                ModItem it = li.Tag as ModItem;
                if (it != null)
                {
                    if (on) checkedRel.Add(it.RelPath); else checkedRel.Remove(it.RelPath);
                }
            }
            lvMods.EndUpdate();
            UpdateSel();
        }

        private List<ModItem> Selected()
        {
            List<ModItem> sel = new List<ModItem>();
            foreach (ModItem it in Core.ModList) if (checkedRel.Contains(it.RelPath)) sel.Add(it);
            return sel;
        }

        private void UpdateSel()
        {
            List<ModItem> sel = Selected();
            long total = 0;
            foreach (ModItem it in sel) total += it.Size;
            lblSel.Text = string.Format("已选 {0} 个，共 {1}（约 {2} 秒）", sel.Count, Core.Human(total), (int)(total / (22.0 * 1048576.0)));
        }

        private void RefreshConflicts()
        {
            string root = txtGame.Text.Trim();
            if (!Core.TestGame(root)) { lblConflict.Text = ""; return; }
            List<string> msgs = ConflictCheck.Run(Core.ModList, root);
            if (msgs.Count == 0) { lblConflict.Text = ""; lblConflict.ForeColor = Color.Green; return; }
            lblConflict.ForeColor = Color.DarkOrange;
            string first = msgs[0];
            if (first.Length > 46) first = first.Substring(0, 46) + "…";
            lblConflict.Text = "⚠ 共 " + msgs.Count + " 条提示：" + first;
            foreach (string m in msgs) Core.Log("WARN", m);
        }

        private void BrowseGame()
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = "选游戏根目录（里面有 left4dead2.exe 的那个文件夹）";
            if (d.ShowDialog() == DialogResult.OK) { txtGame.Text = d.SelectedPath; RefreshConflicts(); }
        }

        private void FindGame()
        {
            List<string> g = Core.FindGames();
            if (g.Count == 0) { Log("没找到。点「浏览…」手动选。"); return; }
            txtGame.Text = g[0];
            if (g.Count == 1) Log("找到: " + g[0]);
            else
            {
                foreach (string s in g) Log("候选: " + s);
                Log("找到多个，先用第一个（不对就用浏览改）");
            }
            RefreshConflicts();
        }

        private List<ModItem> CheckedOrSelected()
        {
            List<ModItem> list = new List<ModItem>();
            if (lvMods.CheckedItems.Count > 0)
            {
                foreach (ListViewItem li in lvMods.CheckedItems) { ModItem it = li.Tag as ModItem; if (it != null) list.Add(it); }
            }
            else
            {
                foreach (ListViewItem li in lvMods.SelectedItems) { ModItem it = li.Tag as ModItem; if (it != null) list.Add(it); }
            }
            return list;
        }

        private void DeleteSelectedMods()
        {
            List<ModItem> list = CheckedOrSelected();
            if (list.Count == 0) { MessageBox.Show("先在列表里勾选，或选中一行。"); return; }
            if (MessageBox.Show("把这 " + list.Count + " 个 MOD 移到 U 盘 _回收的MOD？\r\n（不真删，想用可以再拖回来）", "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            int n = 0;
            foreach (ModItem it in list)
            {
                try { Work.MoveModToRecycle(it); n++; checkedRel.Remove(it.RelPath); }
                catch (Exception ex) { Core.Log("WARN", "移动 " + it.Name + " 失败: " + ex.Message); }
            }
            Core.Log("OK", "已移到 _回收的MOD：" + n + " 个");
            Core.ScanMods(); RefreshMods(); RefreshConflicts();
        }

        private void RenameSelectedMod()
        {
            List<ModItem> list = CheckedOrSelected();
            if (list.Count != 1) { MessageBox.Show("重命名一次只能选一个。"); return; }
            string nn = PromptForm.Ask("新名字（不用写 .vpk）", "重命名 " + list[0].Name);
            if (string.IsNullOrEmpty(nn)) return;
            string err = Work.RenameMod(list[0], nn.Trim());
            if (err != null) { MessageBox.Show(err); return; }
            Core.Log("OK", "已改名: " + list[0].Name + " -> " + nn.Trim() + ".vpk");
            Core.ScanMods(); RefreshMods();
        }

        private void CleanDupMods()
        {
            if (MessageBox.Show("扫描全库同名文件，每个名字只留第一个，其余移到 _回收的MOD？", "清理重复", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            List<string> moved = Work.CleanDuplicateMods();
            Core.Log("OK", "清理重复: 移走 " + moved.Count + " 个");
            MessageBox.Show(moved.Count == 0 ? "没有同名重复。" : ("已移走 " + moved.Count + " 个重复文件，详见日志。"));
            Core.ScanMods(); RefreshMods(); RefreshConflicts();
        }

        private void RevealMod()
        {
            List<ModItem> list = CheckedOrSelected();
            if (list.Count == 0) { MessageBox.Show("先选中一个 MOD。"); return; }
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + list[0].FullPath + "\""); } catch { }
        }

        private void SetTags()
        {
            List<ModItem> list = CheckedOrSelected();
            if (list.Count == 0) { MessageBox.Show("先选中一个 MOD。"); return; }
            ModItem m = list[0];
            string cur = ModTags.Get(modTags, m.RelPath);
            string v = PromptForm.Ask("标签，用逗号分隔（例：武器,刀,替换）\r\n留空 = 清除", "设置标签 " + m.Name + "（当前：" + (cur.Length == 0 ? "无" : cur) + "）");
            if (v == null) return;
            modTags[m.RelPath] = v.Trim();
            ModTags.Save(modTags);
            Core.Log("OK", "标签已保存: " + m.Name + " = " + v.Trim());
        }

        private void ShowModInfo()
        {
            List<ModItem> list = CheckedOrSelected();
            if (list.Count == 0) { MessageBox.Show("先选中一个 MOD。"); return; }
            ModItem m = list[0];
            Cursor = Cursors.WaitCursor;
            List<string> paths = Vpk.Paths(m.FullPath, 200000);
            Cursor = Cursors.Default;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("文件  : " + m.Name);
            sb.AppendLine("分类  : " + m.Category);
            sb.AppendLine("大小  : " + Core.Human(m.Size));
            sb.AppendLine("标签  : " + (ModTags.Get(modTags, m.RelPath).Length == 0 ? "（没设）" : ModTags.Get(modTags, m.RelPath)));
            sb.AppendLine("内部条目: " + paths.Count + " 个");
            if (paths.Count > 0)
            {
                sb.AppendLine("示例:");
                int n = Math.Min(8, paths.Count);
                for (int i = 0; i < n; i++) sb.AppendLine("  " + paths[i]);
            }
            MessageBox.Show(sb.ToString(), "MOD 信息");
        }

        private void BulkImport()
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = "选一个文件夹，把里面的 vpk（含子目录）全部收进库并按枪械归类";
            if (d.ShowDialog() != DialogResult.OK) return;
            Cursor = Cursors.WaitCursor;
            int imported, skipped;
            List<string> log = Work.ImportVpks(d.SelectedPath, out imported, out skipped);
            Cursor = Cursors.Default;
            foreach (string l in log) Core.Log("INFO", l);
            Core.Log("OK", "批量导入完成：入库 " + imported + " 个，已存在跳过 " + skipped + " 个");
            RefreshMods(); RefreshConflicts(); FillCats();
            MessageBox.Show("入库 " + imported + " 个，跳过重复 " + skipped + " 个。\r\n（按文件名归到 AK47、MP5 等文件夹，认不出的放「杂项功能」）", "批量导入");
        }

        private void SaveProfile()
        {
            List<ModItem> sel = Selected();
            if (sel.Count == 0) { MessageBox.Show("先勾一些 MOD 再存方案。"); return; }
            string name = PromptForm.Ask("方案名字（比如：日常 / 打僵尸 / 拍视频）", "存为方案");
            if (string.IsNullOrEmpty(name)) return;
            List<string> rels = new List<string>();
            foreach (ModItem it in sel) rels.Add(it.RelPath);
            ProfileStore.Save(name.Trim(), rels, chkDxvk.Checked, PlatFlags());
            FillProfiles();
            Core.Log("OK", "方案已存：" + name + "（" + rels.Count + " 个 MOD）");
        }

        private void PreviewProfile()
        {
            if (initializing) return;
            if (cboProfile.SelectedItem == null) return;
            string name = cboProfile.SelectedItem.ToString();
            if (name.StartsWith("（")) return;
            bool dxvk; string plat;
            List<string> rels = ProfileStore.Load(name, out dxvk, out plat);
            if (rels.Count == 0) return;
            checkedRel.Clear();
            foreach (string r in rels) checkedRel.Add(r);
            chkDxvk.Checked = dxvk;
            ApplyPlatFlags(plat);
            foreach (ListViewItem li in lvMods.Items)
            {
                ModItem it = li.Tag as ModItem;
                if (it != null) li.Checked = checkedRel.Contains(it.RelPath);
            }
            UpdateSel();
            int shown = 0;
            foreach (ModItem m in Core.ModList) if (checkedRel.Contains(m.RelPath)) shown++;
            Log("方案「" + name + "」：已按它勾好 " + shown + " 个 MOD（含不在当前筛选里的），点「开始装」即装这些");
        }

        private void ApplyProfile()
        {
            if (cboProfile.SelectedItem == null) return;
            string name = cboProfile.SelectedItem.ToString();
            bool dxvk; string plat;
            List<string> rels = ProfileStore.Load(name, out dxvk, out plat);
            if (rels.Count == 0) { MessageBox.Show("这个方案是空的。"); return; }
            checkedRel.Clear();
            foreach (string r in rels) checkedRel.Add(r);
            chkDxvk.Checked = dxvk;
            ApplyPlatFlags(plat);
            foreach (ListViewItem li in lvMods.Items)
            {
                ModItem it = li.Tag as ModItem;
                if (it != null) li.Checked = checkedRel.Contains(it.RelPath);
            }
            UpdateSel();
            Core.Log("OK", "已套用方案：" + name + "（" + rels.Count + " 个 MOD）");
        }

        private string PlatFlags()
        {
            return "Skeeto=" + (chkSkeeto.Checked ? "1" : "0") +
                   ";Asset=" + (chkAsset.Checked ? "1" : "0") +
                   ";Mqcam=" + (chkMqcam.Checked ? "1" : "0") +
                   ";Dxvk=" + (chkDxvk.Checked ? "1" : "0") +
                   ";Launch=" + (chkLaunch.Checked ? "1" : "0") +
                   ";Cfg=" + (chkCfg.Checked ? "1" : "0");
        }

        private void ApplyPlatFlags(string plat)
        {
            if (string.IsNullOrEmpty(plat)) return;
            foreach (string part in plat.Split(';'))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2) continue;
                bool on = kv[1] == "1";
                switch (kv[0])
                {
                    case "Skeeto": chkSkeeto.Checked = on; break;
                    case "Asset": chkAsset.Checked = on; break;
                    case "Mqcam": chkMqcam.Checked = on; break;
                    case "Dxvk": chkDxvk.Checked = on; break;
                    case "Launch": chkLaunch.Checked = on; break;
                    case "Cfg": chkCfg.Checked = on; break;
                }
            }
        }

        public string LayoutDump()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("窗口客户区: " + ClientSize.Width + " x " + ClientSize.Height);
            sb.AppendLine("游戏路径框: " + txtGame.Width + " x " + txtGame.Height);
            sb.AppendLine("MOD 库分组: " + gMod.Width + " x " + gMod.Height);
            sb.AppendLine("MOD 列表  : " + lvMods.Width + " x " + lvMods.Height);
            sb.AppendLine("日志分组  : " + gLog.Width + " x " + gLog.Height);
            sb.AppendLine("日志框    : " + txtLog.Width + " x " + txtLog.Height);
            sb.AppendLine("进度条    : " + bar.Left + "," + bar.Top + " " + bar.Width + " x " + bar.Height);
            sb.AppendLine("按钮行    : 顶=" + btnInstall.Top + " 左=" + btnInstall.Left);
            sb.AppendLine("开始装按钮: " + btnInstall.Left + "," + btnInstall.Top + " " + btnInstall.Width + " x " + btnInstall.Height);
            return sb.ToString();
        }

        private void ApplyResponsive()
        {
            int leftW = (int)((ClientSize.Width - 36) * 0.42);
            if (leftW < 380) leftW = 380;
            int rightW = ClientSize.Width - 12 - leftW - 10;
            if (rightW < 420) rightW = 420;
            gPlat.Width = leftW;
            gLog.Width = leftW;
            txtLog.Width = leftW - 20;
            gMod.Left = 12 + leftW + 10;
            gMod.Width = rightW;
            int limit = ClientSize.Width - 534;
            if (limit < 200) limit = 200;
            bar.Width = limit;
            lblStatus.Width = limit;
            int w = lvMods.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6;
            int fixedW = 300 + 90;
            if (w - fixedW > 120) lvMods.Columns[2].Width = w - fixedW;
        }

        private bool TestWritable(string dir)
        {
            try
            {
                string f = Path.Combine(dir, "_write_test.tmp");
                File.WriteAllText(f, "x");
                File.Delete(f);
                return true;
            }
            catch { return false; }
        }

        private void ShowEnv()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("系统      : " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? "  64 位" : "  32 位"));
            sb.AppendLine(".NET      : " + Environment.Version);
            bool admin = false;
            try
            {
                admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { }
            sb.AppendLine("管理员    : " + (admin ? "是" : "否（写系统目录、装运行库会受限）"));
            string root = txtGame.Text.Trim();
            sb.AppendLine("游戏可写  : " + (Core.TestGame(root) ? (TestWritable(root) ? "是" : "否（只读/占用/权限不足）") : "（还没选对目录）"));
            try
            {
                DriveInfo di = new DriveInfo(Path.GetPathRoot(root));
                sb.AppendLine("游戏盘剩余: " + (di.AvailableFreeSpace / 1073741824.0).ToString("N1") + " GB");
            }
            catch { }
            sb.AppendLine("U 盘根    : " + Core.UsbRoot);
            sb.AppendLine("日志目录  : " + Core.LogDir);
            sb.AppendLine("MOD 库    : " + Core.ModList.Count + " 个 vpk");
            MessageBox.Show(sb.ToString(), "环境自检");
            Core.Log("INFO", "环境自检：" + Environment.OSVersion.VersionString + " / .NET " + Environment.Version + " / 管理员=" + admin);
        }

        public void ApplyProfileByName(string name)
        {
            bool dxvk; string plat;
            List<string> rels = ProfileStore.Load(name, out dxvk, out plat);
            if (rels.Count == 0) { MessageBox.Show("这个方案是空的。"); return; }
            checkedRel.Clear();
            foreach (string r in rels) checkedRel.Add(r);
            chkDxvk.Checked = dxvk;
            ApplyPlatFlags(plat);
            foreach (ListViewItem li in lvMods.Items) { ModItem it = li.Tag as ModItem; if (it != null) li.Checked = checkedRel.Contains(it.RelPath); }
            UpdateSel();
            Core.Log("OK", "已套用方案：" + name + "（" + rels.Count + " 个 MOD）");
        }

        private void AppendLog(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), new object[] { line }); return; }
            txtLog.AppendText(line + "\r\n");
        }

        private void Log(string s) { Core.Log("INFO", s); }
        private void Log(string level, string s) { Core.Log(level, s); }

        private void SetBusy(bool busy)
        {
            btnInstall.Enabled = !busy;
            btnRestore.Enabled = !busy;
            btnSwitch.Enabled = !busy;
            btnFind.Enabled = !busy;
            btnBrowse.Enabled = !busy;
        }

        private void DoSwitch()
        {
            string root = txtGame.Text.Trim();
            if (!Core.TestGame(root)) { MessageBox.Show("先选对游戏根目录。"); return; }
            AddonSwitchForm f = new AddonSwitchForm(root);
            f.ShowDialog(this);
        }

        private void DoRestore()
        {
            string root = txtGame.Text.Trim();
            if (!Core.TestGame(root)) { MessageBox.Show("先选对游戏根目录。"); return; }
            if (!TestWritable(root))
            {
                MessageBox.Show("这个游戏目录写不进去（可能只读、被占用或权限不足）。\r\n可以试试以管理员身份运行，或换一个有写权限的目录。", "目录不可写");
                return;
            }
            if (System.Diagnostics.Process.GetProcessesByName("left4dead2").Length > 0)
            {
                MessageBox.Show("游戏还开着，先完全退出再还原。", "游戏正在运行");
                return;
            }
            if (MessageBox.Show("彻底还原：删掉 L4N / Skeeto / MQCam / dxvk 写进去的文件，恢复原版 exe、addonlist、autoexec，并把本次铺的 MOD 挪进回收站。\r\n\r\n确定？", "确认彻底还原", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            Log("--- 彻底还原 ---");
            try
            {
                foreach (string l in Work.FullRestore(root)) Log("OK", l);
                Log("OK", "还原完成，现在进的是原版游戏");
            }
            catch (Exception ex) { Log("FAIL", "还原失败: " + ex.Message); }
        }

        private void DoInstall()
        {
            string root = txtGame.Text.Trim();
            if (!Core.TestGame(root)) { MessageBox.Show("这个目录里没有 left4dead2.exe，先选对游戏根目录。", "路径不对"); return; }
            if (!TestWritable(root))
            {
                MessageBox.Show("这个游戏目录写不进去（可能只读、被占用或权限不足）。\r\n可以试试以管理员身份运行，或换一个有写权限的目录。", "目录不可写");
                return;
            }
            if (System.Diagnostics.Process.GetProcessesByName("left4dead2").Length > 0)
            {
                MessageBox.Show("游戏还开着。先把《求生之路2》完全退出（不是最小化），否则 exe 和 dll 被占用，装不进去。", "游戏正在运行");
                return;
            }
            List<ModItem> sel = Selected();
            bool wantL4N = chkL4N.Checked;
            bool wantDxvk = chkDxvk.Checked;
            bool wantSkeeto = chkSkeeto.Checked;
            bool wantAsset = chkAsset.Checked;
            bool wantMqcam = chkMqcam.Checked;
            bool wantLaunch = chkLaunch.Checked;
            bool wantCfg = chkCfg.Checked;
            string launchVal = txtLaunch.Text.Trim();
            if (!wantL4N && !wantDxvk && !wantSkeeto && !wantMqcam && sel.Count == 0 && !wantLaunch) { MessageBox.Show("什么都没勾。"); return; }
            if (sel.Count >= 2)
            {
                Cursor = Cursors.WaitCursor;
                List<string> deep = ConflictCheck.Deep(sel);
                deep.AddRange(Work.ScriptLibConflicts(sel));
                Cursor = Cursors.Default;
                if (deep.Count > 0)
                {
                    string msg = string.Join("\r\n", deep.ToArray()) + "\r\n\r\n仍然继续装吗？";
                    if (MessageBox.Show(msg, "这些 MOD 有文件重叠", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                    foreach (string l in deep) Core.Log("WARN", l);
                }
            }
            Core.Cfg.GameRoot = root;
            Core.Cfg.Dxvk = wantDxvk;
            Core.Cfg.Skeeto = wantSkeeto;
            Core.Cfg.AssetPack = wantAsset;
            Core.Cfg.Mqcam = wantMqcam;
            Core.Cfg.Launch = wantLaunch;
            Core.Cfg.CfgFile = wantCfg;
            Core.Cfg.LaunchOptions = launchVal;
            Core.Cfg.Recent.RemoveAll(delegate(string s) { return string.Equals(s, root, StringComparison.OrdinalIgnoreCase); });
            Core.Cfg.Recent.Insert(0, root);
            while (Core.Cfg.Recent.Count > 6) Core.Cfg.Recent.RemoveAt(Core.Cfg.Recent.Count - 1);
            Core.Cfg.Mods = new List<string>();
            foreach (ModItem it in sel) Core.Cfg.Mods.Add(it.RelPath);
            Core.SaveConfig();

            SetBusy(true);
            bar.Style = ProgressBarStyle.Marquee;
            lblStatus.Text = "正在备份…";
            Thread th = new Thread(delegate()
            {
                try
                {
                    string backupDir;
                    Core.Log("STEP", "备份原文件");
                    Core.BackupGame(root, out backupDir);
                    if (wantL4N)
                    {
                        Core.Log("STEP", "装 L4N");
                        SetStatus("正在装 L4N…");
                        Work.InstallL4N(root, delegate(int done, int total) { SetBar(total > 0 ? done * 1000 / total : 0); });
                    }
                    if (wantDxvk)
                    {
                        Core.Log("STEP", "装 dxvk");
                        SetStatus("正在装 dxvk…");
                        Work.InstallDxvk(root, backupDir);
                    }
                    if (wantSkeeto)
                    {
                        Core.Log("STEP", "装 Skeeto");
                        SetStatus("正在装 Skeeto…");
                        Work.InstallSkeeto(root);
                    }
                    string assetName = null;
                    if (wantAsset)
                    {
                        Core.Log("STEP", "装素材包");
                        SetStatus("正在复制素材包…");
                        assetName = Work.InstallAssetPack(root);
                    }
                    if (wantCfg)
                    {
                        Core.Log("STEP", "搬 autoexec.cfg");
                        SetStatus("正在搬 autoexec.cfg…");
                        Work.InstallCfg(root);
                    }
                    if (wantMqcam)
                    {
                        Core.Log("STEP", "装 MQCam");
                        SetStatus("正在装 MQCam…");
                        Work.InstallMqcam(root);
                    }
                    if (chkSwap.Checked)
                    {
                        Core.Log("STEP", "换整套：先清掉旧 MOD");
                        SetStatus("正在清掉上一套…");
                        Work.SwapToSet(root, sel);
                    }
                    List<string> added = new List<string>();
                    if (sel.Count > 0)
                    {
                        Core.Log("STEP", "铺 MOD（" + sel.Count + " 个）");
                        SetStatus("正在铺 MOD…");
                        List<ModItem> todo = new List<ModItem>(sel);
                        for (int attempt = 0; attempt <= 2; attempt++)
                        {
                            int copied, skipped;
                            List<string> addedThis, failed;
                            Work.CopyMods(root, todo, delegate(long done, long total)
                            {
                                int pct = total > 0 ? (int)(done * 1000 / total) : 0;
                                SetBar(pct);
                            }, out copied, out skipped, out addedThis, out failed);
                            if (addedThis.Count > 0) added.AddRange(addedThis);
                            if (failed.Count == 0 || attempt == 2) break;
                            List<ModItem> retryList = new List<ModItem>();
                            foreach (ModItem it in todo) if (failed.Contains(it.Name)) retryList.Add(it);
                            if (retryList.Count == 0) break;
                            string q = "有 " + retryList.Count + " 个 MOD 复制失败，重试一次？（最多重试 2 次）";
                            bool yes = false;
                            Invoke(new Action(delegate { yes = MessageBox.Show(q, "复制失败", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes; }));
                            if (!yes) break;
                            Core.Log("WARN", "重试 " + retryList.Count + " 个失败的 MOD");
                            todo = retryList;
                        }
                        Dictionary<string, bool> st = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                        foreach (ModItem it in sel) st[it.Name] = true;
                        if (assetName != null) st[assetName] = true;
                        string bak = AddonList.Write(root, st);
                        Core.Log("OK", "启用状态已写入（原文件备份 " + Path.GetFileName(bak) + "）");
                    }
                    else if (assetName != null)
                    {
                        Dictionary<string, bool> st2 = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                        st2[assetName] = true;
                        AddonList.Write(root, st2);
                    }
                    if (wantLaunch)
                    {
                        Core.Log("STEP", "写 Steam 启动项");
                        Core.Log("INFO", Work.WriteLaunchOptions(launchVal));
                    }
                    Core.Log("STEP", "逐项核对");
                    Work.Verify(root);
                    Work.WriteManifest(root, backupDir, added);
                    SetStatus("完成");
                    Core.Log("OK", "全部完成。进游戏即可，开关在「已装 MOD 开关」里调。");
                    BeginInvoke(new Action(delegate
                    {
                        if (MessageBox.Show("装好了。现在启动游戏吗？", "完成", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            try { System.Diagnostics.Process.Start("steam://rungameid/550"); }
                            catch (Exception ex2) { Core.Log("WARN", "启动游戏失败（请在 Steam 里手动开）: " + ex2.Message); }
                        }
                    }));
                }
                catch (Exception ex)
                {
                    Core.Log("FAIL", "中断: " + ex.Message);
                    SetStatus("失败，看日志");
                }
                finally
                {
                    SetBusy(false);
                    SetBar(0);
                    SetStatus("就绪");
                }
            });
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
        }

        private void SetStatus(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(SetStatus), new object[] { s }); return; }
            lblStatus.Text = s;
        }

        private void SetBar(int permille)
        {
            if (InvokeRequired) { BeginInvoke(new Action<int>(SetBar), new object[] { permille }); return; }
            if (bar.Style != ProgressBarStyle.Continuous) { bar.Style = ProgressBarStyle.Continuous; bar.Maximum = 1000; }
            if (permille < 0) permille = 0;
            if (permille > 1000) permille = 1000;
            bar.Value = permille;
        }
    }

    public class ProfileManageForm : Form
    {
        private ListBox lb, lbItems;
        private Label lblInfo;
        private MainForm owner;

        public ProfileManageForm(MainForm o)
        {
            owner = o;
            Text = "方案管理 —— 左边选方案，右边看它装什么";
            ClientSize = new Size(640, 420);
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false;

            Label l1 = new Label(); l1.Text = "方案"; l1.SetBounds(12, 8, 60, 20); Controls.Add(l1);
            lb = new ListBox(); lb.SetBounds(12, 30, 220, 250); lb.SelectedIndexChanged += delegate { ShowItems(); }; lb.DoubleClick += delegate { ApplySel(); }; Controls.Add(lb);
            Label l2 = new Label(); l2.Text = "这套会装什么"; l2.SetBounds(244, 8, 200, 20); Controls.Add(l2);
            lbItems = new ListBox(); lbItems.SetBounds(244, 30, 384, 250); Controls.Add(lbItems);
            lblInfo = new Label(); lblInfo.SetBounds(244, 284, 384, 22); lblInfo.Text = ""; Controls.Add(lblInfo);

            Button ap = new Button(); ap.Text = "套用"; ap.SetBounds(12, 296, 76, 30); ap.Click += delegate { ApplySel(); }; Controls.Add(ap);
            Button rn = new Button(); rn.Text = "改名"; rn.SetBounds(94, 296, 76, 30); rn.Click += delegate { RenameSel(); }; Controls.Add(rn);
            Button de = new Button(); de.Text = "删除"; de.SetBounds(176, 296, 76, 30); de.Click += delegate { DeleteSel(); }; Controls.Add(de);
            Button ex = new Button(); ex.Text = "导出…"; ex.SetBounds(272, 296, 90, 30); ex.Click += delegate { ExportSel(); }; Controls.Add(ex);
            Button im = new Button(); im.Text = "导入…"; im.SetBounds(368, 296, 90, 30); im.Click += delegate { ImportFile(); }; Controls.Add(im);
            Button cl = new Button(); cl.Text = "关闭"; cl.SetBounds(524, 296, 104, 30); cl.DialogResult = DialogResult.Cancel; Controls.Add(cl);
            CancelButton = cl;

            LoadList();
        }

        private void LoadList()
        {
            string keep = lb.SelectedItem == null ? null : lb.SelectedItem.ToString();
            lb.Items.Clear();
            foreach (string p in ProfileStore.ListProfiles()) lb.Items.Add(p);
            if (lb.Items.Count == 0) { lb.Items.Add("（还没有方案）"); lbItems.Items.Clear(); lblInfo.Text = ""; return; }
            if (keep != null && lb.Items.Contains(keep)) lb.SelectedItem = keep;
            else lb.SelectedIndex = 0;
        }

        private void ShowItems()
        {
            lbItems.Items.Clear();
            string s = Sel();
            if (s == null) { lblInfo.Text = ""; return; }
            bool dxvk; string plat;
            List<string> rels = ProfileStore.Load(s, out dxvk, out plat);
            foreach (string r in rels)
            {
                string show = r.Replace("addons", "").TrimStart('\\');
                lbItems.Items.Add(show);
            }
            if (rels.Count == 0) lbItems.Items.Add("（空方案）");
            lblInfo.Text = "共 " + rels.Count + " 个 MOD  ｜  平台: " + PrettyPlat(plat);
        }

        private static string PrettyPlat(string plat)
        {
            if (string.IsNullOrEmpty(plat)) return "（未记录）";
            string r = "";
            foreach (string part in plat.Split(';'))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2 || kv[1] != "1") continue;
                switch (kv[0])
                {
                    case "Skeeto": r += "Skeeto "; break;
                    case "Asset": r += "素材包 "; break;
                    case "Mqcam": r += "MQCam "; break;
                    case "Dxvk": r += "dxvk "; break;
                    case "Launch": r += "启动项 "; break;
                    case "Cfg": r += "autoexec "; break;
                }
            }
            return r.Length == 0 ? "（只装 MOD）" : r.Trim();
        }

        private string Sel()
        {
            if (lb.SelectedItem == null) return null;
            string s = lb.SelectedItem.ToString();
            if (s.StartsWith("（")) return null;
            return s;
        }

        private void ApplySel()
        {
            string s = Sel();
            if (s == null) return;
            owner.ApplyProfileByName(s);
            Close();
        }

        private void RenameSel()
        {
            string s = Sel();
            if (s == null) return;
            string nn = PromptForm.Ask("新名字", "给方案改名");
            if (string.IsNullOrEmpty(nn)) return;
            string err = ProfileStore.Rename(s, nn.Trim());
            if (err != null) { MessageBox.Show(err); return; }
            LoadList();
        }

        private void DeleteSel()
        {
            string s = Sel();
            if (s == null) return;
            if (MessageBox.Show("删除方案「" + s + "」？", "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            ProfileStore.Delete(s);
            LoadList();
        }

        private void ExportSel()
        {
            string s = Sel();
            if (s == null) { MessageBox.Show("先选一个方案。"); return; }
            SaveFileDialog d = new SaveFileDialog(); d.Filter = "方案文件|*.txt"; d.FileName = s + ".txt";
            if (d.ShowDialog() != DialogResult.OK) return;
            File.Copy(Path.Combine(ProfileStore.DirOf(), s + ".txt"), d.FileName, true);
            MessageBox.Show("已导出: " + d.FileName);
        }

        private void ImportFile()
        {
            OpenFileDialog d = new OpenFileDialog(); d.Filter = "方案文件|*.txt";
            if (d.ShowDialog() != DialogResult.OK) return;
            string name = Path.GetFileNameWithoutExtension(d.FileName);
            Directory.CreateDirectory(ProfileStore.DirOf());
            string dst = Path.Combine(ProfileStore.DirOf(), name + ".txt");
            if (File.Exists(dst) && MessageBox.Show("已有同名方案，覆盖？", "确认", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            File.Copy(d.FileName, dst, true);
            LoadList();
            MessageBox.Show("已导入方案: " + name);
        }
    }
    public class PromptForm : Form
    {
        private TextBox box;
        private PromptForm(string title, string hint)
        {
            Text = title; ClientSize = new Size(420, 130); Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false;
            Label l = new Label(); l.Text = hint; l.SetBounds(12, 10, 396, 20); Controls.Add(l);
            box = new TextBox(); box.SetBounds(12, 36, 396, 24); Controls.Add(box);
            Button ok = new Button(); ok.Text = "确定"; ok.SetBounds(240, 76, 80, 28); ok.DialogResult = DialogResult.OK; Controls.Add(ok);
            Button no = new Button(); no.Text = "取消"; no.SetBounds(328, 76, 80, 28); no.DialogResult = DialogResult.Cancel; Controls.Add(no);
            AcceptButton = ok; CancelButton = no;
        }
        public static string Ask(string hint, string title)
        {
            PromptForm f = new PromptForm(title, hint);
            if (f.ShowDialog() != DialogResult.OK) return null;
            return f.box.Text.Trim();
        }
    }
}
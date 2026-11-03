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
        private FlatButton btnBrowse, btnFind, btnInstall, btnRestore, btnOpen, btnSwitch;
        private CheckBox chkL4N, chkSkeeto, chkAsset, chkMqcam, chkDxvk, chkLaunch, chkCfg, chkSwap, chkStrict;
        private RadioButton rdSkip, rdOver;
        private ComboBox cboTweak;
        private FlatButton btnTweak, btnTweakRestore;
        private FlatButton btnLaunchEdit;
        private TextBox txtLaunch;
        private ComboBox cboCat, cboProfile;
        // 最近套用/保存过的方案名（空=没套方案）：自检结果写回它
        private string appliedProfile = "";
        private Section gMod, gLog, gPlat, gOpt;
        private Dictionary<string, string> modTags;
        private Button btnManage;
        private TextBox txtSearch;
        private Button btnAll, btnNone, btnSaveProfile, btnApplyProfile;
        private ListView lvMods;
        private Label lblSel, lblStatus, lblConflict, lblEmpty;
        private ToolTip tips;
        private ProgressBar bar;
        private TextBox txtLog;
        private readonly HashSet<string> checkedKeys = new HashSet<string>();
        private List<ModGroup> shown = new List<ModGroup>();
        private List<string> conflictMsgs = new List<string>();
        private bool bulk;                       // 批量勾选时先别逐项刷新
        private int logLines;                    // 日志行数，超了就截断，避免文本框越用越慢
        private readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer();
        private volatile bool bgDone;            // RunBusy 的完成标志
        private int lastRespW = -1;   // 上一次重排用的窗口宽度（拖动时去重，少重排一半以上）
        private object bgResult;
        private Exception bgError;

        // 缩放窗口时闪/错位：整窗走系统合成（WS_EX_COMPOSITED）+ 双缓冲，
        // 子控件不再各自重绘；配合 ApplyResponsive 里的 SuspendLayout，拖动时只排一次
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;   // WS_EX_COMPOSITED
                return cp;
            }
        }

        public MainForm(string gameRootHint)
        {
            // 版本只写在 AssemblyInfo 里，标题跟着走，省得两处对不上
            Version ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Text = "网吧装机台  v" + ver.Major + "." + ver.Minor;
            ClientSize = new Size(940, 700);
            Font = Theme.UI;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            BuildUi();
            Core.Sink = AppendLog;

            if (gameRootHint != null && Core.TestGame(gameRootHint)) txtGame.Text = gameRootHint;
            else if (Core.TestGame(Core.Cfg.GameRoot)) txtGame.Text = Core.Cfg.GameRoot;
            // 赋值后文本会整段处于选中态（蓝底），每次开窗都像待输入，光标退回开头
            txtGame.SelectionStart = 0;
            txtGame.SelectionLength = 0;
            // 都没配好也不在这里找：找游戏要遍历所有盘（含只读虚拟卷），放到 BeginInit 的后台线程做

            // 重活放到窗口显示之后做，避免启动干等
            Load += delegate { BeginInit(); };
            // ComboBox 显示后拿到焦点会全选文本（看着像"等你重新输入"），等激活完再把选区收掉
            Shown += delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    txtGame.SelectionStart = txtGame.Text.Length;
                    txtGame.SelectionLength = 0;
                });
            };
        }

        private bool initializing = true;

        /// <summary>初始化（扫库、找游戏）跑完了没有。自测截图要等它。</summary>
        internal bool Ready { get { return !initializing; } }

        private void BeginInit()
        {
            lblStatus.Text = "正在读取 U 盘…";
            bool needFind = !Core.TestGame(txtGame.Text);
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                try
                {
                    bool packOk;
                    System.Diagnostics.Stopwatch __sw = System.Diagnostics.Stopwatch.StartNew();
                    List<string> packs = Core.CheckPackages(out packOk);
                    Core.Log("INFO", "启动计时: 包检查 " + __sw.ElapsedMilliseconds + " ms");
                    __sw.Restart();
                    Core.ScanMods();
                    Core.Log("INFO", "启动计时: 扫库 " + __sw.ElapsedMilliseconds + " ms（" + Core.ModList.Count + " 个）");
                    modTags = ModTags.Load();

                    string found = null;
                    if (needFind)
                    {
                        __sw.Restart();
                        List<string> g = Core.FindGames();
                        if (g.Count > 0) found = g[0];
                        Core.Log("INFO", "启动计时: 找游戏 " + __sw.ElapsedMilliseconds + " ms（找到 " + g.Count + " 个）");
                    }

                    SafeInvoke(delegate
                    {
                        System.Diagnostics.Stopwatch __sw2 = System.Diagnostics.Stopwatch.StartNew();
                        if (found != null) { txtGame.Text = found; Core.Log("INFO", "自动找到游戏: " + found); }
                        else if (!Core.TestGame(txtGame.Text)) Core.Log("WARN", "没自动找到游戏，点「自动找」或「浏览…」");
                        FillCats();
                        ApplyConfig();
                        FillProfiles();
                        Core.Log("INFO", "启动计时: 填充列表 " + __sw2.ElapsedMilliseconds + " ms");
                        __sw2.Restart();
                        RefreshConflicts(false);          // 启动只做内存里的同名/同大小判断
                        Core.Log("INFO", "启动计时: 冲突检查 " + __sw2.ElapsedMilliseconds + " ms");
                        initializing = false;
                        ShowPackStatus(packs, packOk);
                    });
                }
                catch (Exception ex) { Core.Log("FAIL", "初始化失败: " + ex.Message); }
            });
            th.IsBackground = true;
            th.Start();
        }

        // 包不齐就直接顶到状态栏上，不用等装到一半才发现
        private void ShowPackStatus(List<string> packs, bool packOk)
        {
            if (packOk) { lblStatus.Text = "就绪"; lblStatus.ForeColor = Theme.Ink2; return; }
            List<string> miss = new List<string>();
            foreach (string l in packs)
            {
                if (l.IndexOf("缺", StringComparison.Ordinal) >= 0) miss.Add(l.Substring(0, l.IndexOf("缺", StringComparison.Ordinal)).Trim());
            }
            lblStatus.ForeColor = Theme.Accent;
            lblStatus.Text = "⚠ U 盘包不齐：缺 " + string.Join("、", miss.ToArray()) + "（详见日志）";
        }

        private void BuildUi()
        {
            BackColor = Theme.Bg;
            tips = new ToolTip();
            tips.AutoPopDelay = 15000;
            tips.InitialDelay = 400;
            tips.ReshowDelay = 100;

            int W = ClientSize.Width;
            int leftW = 396;
            int rightX = Theme.M + leftW + Theme.G;
            int rightW = W - Theme.M - rightX;

            // ---- 顶部：游戏目录 ----
            Label l1 = new Label(); l1.Text = "游戏目录"; l1.SetBounds(Theme.M, 20, 56, 20); Theme.Dress(l1); Controls.Add(l1);
            txtGame = new ComboBox(); txtGame.SetBounds(76, 18, W - 76 - Theme.M - 84 - 8 - 84 - 8, 24);
            txtGame.DropDownStyle = ComboBoxStyle.DropDown; Theme.Dress(txtGame); Controls.Add(txtGame);
            btnBrowse = new FlatButton(); btnBrowse.Text = "浏览…"; btnBrowse.SetBounds(W - Theme.M - 84 - 8 - 84, 16, 84, Theme.H); btnBrowse.Click += delegate { BrowseGame(); }; Controls.Add(btnBrowse);
            btnFind = new FlatButton(); btnFind.Text = "自动找"; btnFind.SetBounds(W - Theme.M - 84, 16, 84, Theme.H); btnFind.Click += delegate { FindGame(); }; Controls.Add(btnFind);

            // ---- 左栏上：装什么 ----
            Section gp = new Section();
            gp.Caption = "装什么"; gp.SetBounds(Theme.M, 56, leftW, 222); Controls.Add(gp);
            chkL4N = AddChk(gp, "L4N 平台（必装，其余都依赖它）", 12, 28, true, false);
            tips.SetToolTip(chkL4N, "L4N 是底座，Skeeto / MQCam 都要靠它加载，所以固定勾上不给取消。\r\n装过一次之后再装会自动跳过（除非在下面选「覆盖重装」）。");
            chkSkeeto = AddChk(gp, "Skeeto 增强框架（命中反馈 / 准星 / HUD）", 12, 52, true, true);
            chkAsset = AddChk(gp, "Skeeto 素材包 177 MB", 12, 76, true, true);
            chkMqcam = AddChk(gp, "MQCam 相机（F2 / F3 / F4 视角）", 12, 100, true, true);
            chkDxvk = AddChk(gp, "dxvk 防闪退（d3d9.dll）", 12, 124, true, true);
            chkLaunch = AddChk(gp, "写 Steam 启动项", 12, 148, true, true);
            chkCfg = AddChk(gp, "写参数配置（L4N 按键 + Skeeto 设置）", 12, 172, true, true);
            tips.SetToolTip(chkCfg, "从 U 盘的参数存档里取出你调好的 autoexec.cfg 和 skeeto_setting.ini，\r\n直接写进这台机器。换机器装机就不用再手调了。\r\n下面那行选套用哪一套；没有存档就退回旧做法（搬 U 盘根目录的 autoexec.cfg）。");
            Label l4 = new Label(); l4.Text = "启动项"; l4.SetBounds(12, 198, 44, 20); Theme.Dress(l4); gp.Controls.Add(l4);
            txtLaunch = new TextBox(); txtLaunch.SetBounds(60, 196, leftW - 60 - 12 - 30, 24); Theme.Dress(txtLaunch); gp.Controls.Add(txtLaunch);
            // 一行输入框放不下 130 个字符：外面只给个预览，真正的编辑交给「…」里的放大窗口
            txtLaunch.TextChanged += delegate { UpdateLaunchTip(); };
            txtLaunch.DoubleClick += delegate { EditLaunch(); };
            btnLaunchEdit = new FlatButton(); btnLaunchEdit.Text = "…"; btnLaunchEdit.Font = Theme.UIBold;
            btnLaunchEdit.SetBounds(leftW - 12 - 26, 196, 26, 24);
            btnLaunchEdit.Click += delegate { EditLaunch(); };
            tips.SetToolTip(btnLaunchEdit, "放大编辑启动项（可换行、可恢复默认、可把 0/1 换成 0 或 1）。双击输入框也能打开。");
            gp.Controls.Add(btnLaunchEdit);
            UpdateLaunchTip();

            // ---- 左栏中：安装选项（原来挤在 MOD 列表底部，语义上属于"怎么装"，跟"挑什么"分开）----
            gOpt = new Section();
            gOpt.Caption = "安装选项"; gOpt.SetBounds(Theme.M, 286, leftW, 144); Controls.Add(gOpt);
            chkSwap = new CheckBox(); chkSwap.Text = "换整套（清掉不在方案里的 MOD）";
            chkSwap.SetBounds(12, 28, leftW - 24, 22); Theme.Dress(chkSwap); gOpt.Controls.Add(chkSwap);
            tips.SetToolTip(chkSwap, "先清掉游戏 addons 目录里不属于这次方案的 MOD，再装。\r\n清掉的会挪到回收站，不会真删。");
            chkStrict = new CheckBox(); chkStrict.Text = "严格校验（慢一倍，更保险）";
            chkStrict.SetBounds(12, 52, leftW - 24, 22); Theme.Dress(chkStrict); gOpt.Controls.Add(chkStrict);
            tips.SetToolTip(chkStrict, "默认只比文件大小。勾上之后，复制完再回读源文件和目标各算一遍 SHA256 比对。");

            // 已装的怎么办：这是机房里最常遇到的一件事 —— 有的机器上次装过一半，有的要原样重来
            Label l5 = new Label(); l5.Text = "已装的"; l5.SetBounds(12, 80, 56, 20); Theme.Dress(l5); gOpt.Controls.Add(l5);
            rdSkip = new RadioButton(); rdSkip.Text = "跳过"; rdSkip.SetBounds(70, 78, 66, 22); Theme.Dress(rdSkip); gOpt.Controls.Add(rdSkip);
            rdOver = new RadioButton(); rdOver.Text = "覆盖重装"; rdOver.SetBounds(140, 78, 96, 22); Theme.Dress(rdOver); gOpt.Controls.Add(rdOver);
            Label l6 = new Label(); l6.Text = "跳过已有 / 全部重写"; l6.SetBounds(240, 80, leftW - 240 - 12, 18); l6.Font = Theme.UISmall; l6.ForeColor = Theme.Ink3; Theme.Dress(l6); gOpt.Controls.Add(l6);
            tips.SetToolTip(rdSkip, "平台、Skeeto、素材包、MQCam、MOD 只要已经装过且大小一致就跳过。\r\n装得快，适合新机器或者上次已经装好了。");
            tips.SetToolTip(rdOver, "不管装没装过，全都重新写一遍。\r\n用来修「装了一半」「文件被删/被改」的机器，慢一些但最干净。");

            // 参数配置：全套管理在对话框里，外面只留「选哪套 + 还原」
            Label l7 = new Label(); l7.Text = "参数"; l7.SetBounds(12, 110, 56, 20); Theme.Dress(l7); gOpt.Controls.Add(l7);
            cboTweak = new ComboBox(); cboTweak.SetBounds(70, 108, 156, 24); cboTweak.DropDownStyle = ComboBoxStyle.DropDownList; Theme.Dress(cboTweak);
            cboTweak.SelectedIndexChanged += delegate { UpdateTweakTip(); };
            gOpt.Controls.Add(cboTweak);
            tips.SetToolTip(cboTweak, "装机时勾着「写参数配置」，就会把这套 autoexec.cfg + skeeto_setting.ini 推过去。");
            btnTweakRestore = new FlatButton(); btnTweakRestore.Text = "还原"; btnTweakRestore.SetBounds(230, 106, 44, Theme.H); btnTweakRestore.Font = Theme.UISmall; btnTweakRestore.Click += delegate { RestoreTweaks(); }; gOpt.Controls.Add(btnTweakRestore);
            tips.SetToolTip(btnTweakRestore, "把这台机器的 autoexec.cfg 和 skeeto_setting.ini 回滚到装机之前。");
            btnTweak = new FlatButton(); btnTweak.Text = "参数配置…"; btnTweak.SetBounds(278, 106, 90, Theme.H); btnTweak.Click += delegate { ManageTweaks(); }; gOpt.Controls.Add(btnTweak);
            tips.SetToolTip(btnTweak, "保存这台机器调好的参数 / 套用存档 / 还原 / 删除存档。");

            // ---- 左栏下：日志 ----
            Section gl = new Section();
            gl.Caption = "日志"; gl.SetBounds(Theme.M, 438, leftW, 146); Controls.Add(gl);
            FlatButton btnLog = new FlatButton(); btnLog.Text = "打开日志"; btnLog.SetBounds(leftW - 12 - 68 - 6 - 52, 1, 68, 20); btnLog.Font = Theme.UISmall; btnLog.Click += delegate { try { System.Diagnostics.Process.Start("explorer.exe", Core.LogDir); } catch { } }; gl.Controls.Add(btnLog);
            FlatButton btnEnv = new FlatButton(); btnEnv.Text = "环境"; btnEnv.SetBounds(leftW - 12 - 52, 1, 52, 20); btnEnv.Font = Theme.UISmall; btnEnv.Click += delegate { ShowEnv(); }; gl.Controls.Add(btnEnv);
            txtLog = new TextBox();
            txtLog.SetBounds(12, 30, leftW - 24, 104);
            txtLog.Multiline = true; txtLog.ScrollBars = ScrollBars.Both; txtLog.WordWrap = false; txtLog.ReadOnly = true;
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.BackColor = Theme.Surface;
            txtLog.ForeColor = Theme.Ink2;
            txtLog.Font = Theme.Mono;      // 日志是数据：等宽才能让时间戳和级别对齐
            gl.Controls.Add(txtLog);

            // ---- 右栏：MOD 库 ----
            Section gm = new Section();
            gm.Caption = "MOD 库（U 盘的 vpk + 压缩包）"; gm.SetBounds(rightX, 56, rightW, 528); Controls.Add(gm);
            Label l2 = new Label(); l2.Text = "分类"; l2.SetBounds(12, 34, 34, 20); Theme.Dress(l2); gm.Controls.Add(l2);
            cboCat = new ComboBox(); cboCat.SetBounds(50, 30, 188, 24); cboCat.DropDownStyle = ComboBoxStyle.DropDownList; Theme.Dress(cboCat); cboCat.SelectedIndexChanged += delegate { RefreshMods(); ShowCatTip(); }; gm.Controls.Add(cboCat);
            // 框只有 188 宽，但展开时给足，别让「铁喷 · [東方Project]…」在下拉里被砍掉
            cboCat.DropDownWidth = 360;
            txtSearch = new TextBox(); txtSearch.SetBounds(246, 30, rightW - 246 - 12 - 44 - 8 - 44 - 12, 24); Theme.Dress(txtSearch); txtSearch.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); }; gm.Controls.Add(txtSearch);
            tips.SetToolTip(txtSearch, "按名字或标签过滤，回车不必要，停止输入 250 ms 自动筛。");
            btnAll = new FlatButton(); btnAll.Text = "全选"; btnAll.SetBounds(rightW - 12 - 44 - 8 - 44, 28, 44, Theme.H); btnAll.Click += delegate { SetAll(true); }; gm.Controls.Add(btnAll);
            btnNone = new FlatButton(); btnNone.Text = "清空"; btnNone.SetBounds(rightW - 12 - 44, 28, 44, Theme.H); btnNone.Click += delegate { SetAll(false); }; gm.Controls.Add(btnNone);

            Label l3 = new Label(); l3.Text = "方案"; l3.SetBounds(12, 70, 34, 20); Theme.Dress(l3); gm.Controls.Add(l3);
            cboProfile = new ComboBox(); cboProfile.SetBounds(50, 66, 204, 24); cboProfile.DropDownStyle = ComboBoxStyle.DropDownList; Theme.Dress(cboProfile); gm.Controls.Add(cboProfile);
            cboProfile.SelectedIndexChanged += delegate { PreviewProfile(); };
            btnSaveProfile = new FlatButton(); btnSaveProfile.Text = "存为方案"; btnSaveProfile.SetBounds(262, 64, 84, Theme.H); btnSaveProfile.Click += delegate { SaveProfile(); }; gm.Controls.Add(btnSaveProfile);
            btnApplyProfile = new FlatButton(); btnApplyProfile.Text = "套用"; btnApplyProfile.SetBounds(354, 64, 58, Theme.H); btnApplyProfile.Click += delegate { ApplyProfile(); }; gm.Controls.Add(btnApplyProfile);
            btnManage = new FlatButton(); btnManage.Text = "管理…"; btnManage.SetBounds(rightW - 12 - 68, 64, 68, Theme.H); btnManage.Click += delegate { (new ProfileManageForm(this)).ShowDialog(this); FillProfiles(); }; gm.Controls.Add(btnManage);

            lvMods = new ListView();
            lvMods.SetBounds(12, 104, rightW - 24, 350);
            lvMods.View = View.Details;
            lvMods.CheckBoxes = true;
            lvMods.FullRowSelect = true;
            lvMods.BorderStyle = BorderStyle.FixedSingle;
            lvMods.BackColor = Theme.Surface;
            lvMods.ForeColor = Theme.Ink;
            lvMods.Font = Theme.UI;
            lvMods.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lvMods.ShowItemToolTips = true;   // 名字列宽有限，悬停能看全
            ImageList rowPad = new ImageList();           // 空图片列表，只用来把行高撑到 22
            rowPad.ImageSize = new Size(1, 22);
            lvMods.SmallImageList = rowPad;
            lvMods.Columns.Add("名称", 238);
            lvMods.Columns.Add("改了什么", 84);
            lvMods.Columns.Add("大小", 76);
            lvMods.Columns.Add("分类", 134);
            lvMods.ItemChecked += delegate(object s, ItemCheckedEventArgs e)
            {
                ModGroup g = e.Item.Tag as ModGroup;
                if (g == null) return;
                if (e.Item.Checked)
                {
                    // 勾这一行 = 装默认部件（主体 + 必装 + 第一个选配）；想挑就双击
                    foreach (ModItem p in Catalog.DefaultParts(g)) checkedKeys.Add(p.Key);
                }
                else
                {
                    foreach (ModItem p in g.Parts) checkedKeys.Remove(p.Key);
                }
                if (g.IsSet) e.Item.Text = RowName(g, CountChecked(g));
                if (bulk) return;
                UpdateSel();
            };
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.Font = Theme.UI;
            cms.Items.Add("选择部件…", null, delegate { PickParts(); });
            cms.Items.Add("删除所选 → U 盘 _回收的MOD", null, delegate { DeleteSelectedMods(); });
            cms.Items.Add("重命名所选", null, delegate { RenameSelectedMod(); });
            cms.Items.Add("设置标签…", null, delegate { SetTags(); });
            cms.Items.Add("只装勾中的 MOD（不动平台/启动项）", null, delegate { InstallSelectedOnly(); });
            cms.Items.Add("从文件夹导入…（vpk / rar / zip）", null, delegate { BulkImport(); });
            cms.Items.Add("清理同名重复（每个名字留第一个）", null, delegate { CleanDupMods(); });
            cms.Items.Add("在资源管理器里定位", null, delegate { RevealMod(); });
            lvMods.ContextMenuStrip = cms;
            lvMods.DoubleClick += delegate { OpenRow(); };
            gm.Controls.Add(lvMods);

            // 空态：搜不到东西时至少告诉人下一步做什么，而不是给一张空表
            lblEmpty = new Label();
            lblEmpty.SetBounds(12, 104, rightW - 24, 350);   // 与列表同矩形，空态时二选一显示
            lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
            lblEmpty.Text = "没有匹配的 MOD\r\n\r\n换个关键词，或把「分类」切回（全部）";
            lblEmpty.Font = Theme.UISmall;
            lblEmpty.ForeColor = Theme.Ink3;
            lblEmpty.BackColor = Theme.Surface;
            lblEmpty.Visible = false;
            gm.Controls.Add(lblEmpty);
            lblEmpty.BringToFront();

            lblSel = new Label(); lblSel.SetBounds(12, 462, rightW - 24, 18); lblSel.Text = "已选 0 个"; lblSel.Font = Theme.UISmall; Theme.Dress(lblSel); gm.Controls.Add(lblSel);
            lblConflict = new Label();
            lblConflict.SetBounds(12, 486, rightW - 24, 36);
            lblConflict.Text = "";
            lblConflict.Font = Theme.UISmall;
            lblConflict.BackColor = Color.Transparent;
            lblConflict.ForeColor = Theme.Warn;
            lblConflict.Cursor = Cursors.Hand;
            lblConflict.Click += delegate { ShowConflictDetail(); };
            gm.Controls.Add(lblConflict);

            // ---- 底部动作区：主操作只有一个 ----
            btnInstall = new FlatButton(); btnInstall.Text = "开始装"; btnInstall.Primary = true;
            btnInstall.Font = new Font(Theme.UI, FontStyle.Bold);
            btnInstall.SetBounds(W - Theme.M - 104 - 8 - 96 - 8 - 116 - 8 - 112, 596, 112, Theme.HP);
            btnInstall.Click += delegate { DoInstall(); }; Controls.Add(btnInstall);
            btnSwitch = new FlatButton(); btnSwitch.Text = "已装 MOD 开关";
            btnSwitch.SetBounds(W - Theme.M - 104 - 8 - 96 - 8 - 116, 596, 116, Theme.HP);
            btnSwitch.Click += delegate { DoSwitch(); }; Controls.Add(btnSwitch);
            btnRestore = new FlatButton(); btnRestore.Text = "一键还原";
            btnRestore.SetBounds(W - Theme.M - 104 - 8 - 96, 596, 96, Theme.HP);
            btnRestore.Click += delegate { DoRestore(); }; Controls.Add(btnRestore);
            btnOpen = new FlatButton(); btnOpen.Text = "打开游戏目录";
            btnOpen.SetBounds(W - Theme.M - 104, 596, 104, Theme.HP);
            btnOpen.Click += delegate { try { System.Diagnostics.Process.Start("explorer.exe", txtGame.Text); } catch { } }; Controls.Add(btnOpen);

            bar = new ProgressBar(); bar.SetBounds(Theme.M, 644, 440, 12); Controls.Add(bar);
            lblStatus = new Label(); lblStatus.SetBounds(Theme.M, 664, W - 2 * Theme.M, 20); lblStatus.Text = "就绪";
            lblStatus.ForeColor = Theme.Ink2; lblStatus.BackColor = Color.Transparent; Controls.Add(lblStatus);

            // 随窗口缩放：左右拉伸的跟着走，底部按钮贴右下
            gMod = gm; gLog = gl; gPlat = gp;
            MinimumSize = new Size(1000, 700);
            txtGame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnFind.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gp.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            gOpt.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            gl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            gm.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lvMods.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblEmpty.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblSel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblConflict.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            btnInstall.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSwitch.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnRestore.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnOpen.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            bar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            searchTimer.Interval = 250;
            searchTimer.Tick += delegate(object s2, EventArgs e2) { searchTimer.Stop(); RefreshMods(); };
            Resize += delegate { ApplyResponsive(); };
            ApplyResponsive();
        }

        private CheckBox AddChk(Section p, string text, int x, int y, bool chk, bool enabled)
        {
            CheckBox c = new CheckBox();
            c.Text = text; c.SetBounds(x, y, p.Width - x - 12, 22); c.Checked = chk; c.Enabled = enabled;
            Theme.Dress(c);
            p.Controls.Add(c);
            return c;
        }

        private void ApplyConfig()
        {
            chkL4N.Checked = Core.Cfg.L4N;
            chkDxvk.Checked = Core.Cfg.Dxvk;
            chkSkeeto.Checked = Core.Cfg.Skeeto;
            chkAsset.Checked = Core.Cfg.AssetPack;
            chkMqcam.Checked = Core.Cfg.Mqcam;
            chkLaunch.Checked = Core.Cfg.Launch;
            txtLaunch.Text = Core.Cfg.LaunchOptions;
            // 第一次发现有参数存档就默认勾上 —— 用户存过参数，摆明了就是下次装机要自动写进去
            bool firstTimeTweak = Core.Cfg.TweakSet.Length == 0 && Tweaks.List().Count > 0;
            chkCfg.Checked = Core.Cfg.CfgFile || firstTimeTweak;
            rdSkip.Checked = !Core.Cfg.Overwrite;
            rdOver.Checked = Core.Cfg.Overwrite;
            FillTweaks(Core.Cfg.TweakSet);
            foreach (string m in Core.Cfg.Mods) checkedKeys.Add(m);
            CheckRowsByKeys();
            UpdateSel();
            Log("已载入上次勾选（" + checkedKeys.Count + " 个 MOD）");
        }

        private void FillCats()
        {
            // 重扫后尽量停在原来的分类上，别每次都跳回（全部）
            CatChoice keep = cboCat.SelectedItem as CatChoice;
            string keepCat = keep == null ? "" : keep.Cat;
            string keepSub = keep == null ? null : keep.Sub;
            cboCat.Items.Clear();
            cboCat.Items.Add(new CatChoice("", null, "(全部)"));
            int idx = 0, sel = 0;
            foreach (string cat in Catalog.Cats)
            {
                int n = 0;
                Dictionary<string, int> subs = new Dictionary<string, int>(StringComparer.CurrentCulture);
                List<string> order = new List<string>();
                foreach (ModGroup g in Catalog.Groups)
                {
                    if (g.Cat != cat) continue;
                    n++;
                    if (!subs.ContainsKey(g.Sub)) { subs[g.Sub] = 0; order.Add(g.Sub); }
                    subs[g.Sub] = subs[g.Sub] + 1;
                }
                if (n == 0) continue;
                order.Sort(StringComparer.CurrentCulture);
                idx++;
                if (cat == keepCat && keepSub == null) sel = idx;
                cboCat.Items.Add(new CatChoice(cat, null, cat + "（" + n + "）"));
                foreach (string sb in order)
                {
                    idx++;
                    if (cat == keepCat && sb == keepSub) sel = idx;
                    cboCat.Items.Add(new CatChoice(cat, sb, "    " + sb + "（" + subs[sb] + "）"));
                }
            }
            cboCat.SelectedIndex = sel;
        }

        // ---------------------------------------------------------- 参数配置

        /// <summary>填「参数」下拉：第一项是不套用，其余是 U 盘上的存档。</summary>
        private void FillTweaks(string keep)
        {
            if (keep == null) keep = TweakName();
            Tweaks.ImportLegacy(txtGame == null ? null : txtGame.Text.Trim());
            cboTweak.Items.Clear();
            cboTweak.Items.Add("(不套用)");
            foreach (string n in Tweaks.List()) cboTweak.Items.Add(n);
            int idx = 0;
            for (int i = 1; i < cboTweak.Items.Count; i++)
                if (string.Equals(cboTweak.Items[i].ToString(), keep, StringComparison.OrdinalIgnoreCase)) idx = i;
            // 从没选过但有存档：默认停在第一套上，让人一眼看到"这台会写哪套参数"
            if (idx == 0 && (keep == null || keep.Length == 0) && cboTweak.Items.Count > 1) idx = 1;
            cboTweak.SelectedIndex = idx;
            UpdateTweakTip();
        }

        /// <summary>当前选的存档名；空串表示不套用。</summary>
        private string TweakName()
        {
            string s2 = cboTweak == null ? null : (cboTweak.SelectedItem as string);
            if (s2 == null || s2 == "(不套用)") return "";
            return s2;
        }

        private void SelectTweak(string name)
        {
            for (int i = 0; i < cboTweak.Items.Count; i++)
                if (string.Equals(cboTweak.Items[i].ToString(), name, StringComparison.OrdinalIgnoreCase)) { cboTweak.SelectedIndex = i; return; }
        }

        private void UpdateTweakTip()
        {
            string n = TweakName();
            bool canRestore = false;
            try
            {
                string r = txtGame == null ? "" : txtGame.Text.Trim();
                canRestore = r.Length > 0 && Core.TestGame(r) && Tweaks.RestorableCount(r) > 0;
            }
            catch { }
            btnTweakRestore.Enabled = canRestore;
            tips.SetToolTip(cboTweak, n.Length == 0
                ? "不套用存档。勾着「写参数配置」时会退回旧做法：搬 U 盘根目录的 autoexec.cfg。"
                : ("装机时写入：" + Tweaks.DirOf(n)));
        }

        /// <summary>启动项输入框只有 ~300px，鼠标悬停看全文，改内容进放大窗口。</summary>
        private void UpdateLaunchTip()
        {
            string s = LaunchArgs.Clean(txtLaunch.Text);
            string tip;
            if (s.Length == 0) tip = "现在是空的：不会写启动项，Steam 里已有的启动项也不会被清掉。\r\n点右边的「…」或双击这里编辑。";
            else
            {
                tip = s + "\r\n\r\n" + (LaunchArgs.HasSlot(s)
                        ? "注意：-skeeto_particle_platform 还是 0/1，要改成 0 或 1 才生效。"
                        : "共 " + LaunchArgs.CountSwitches(s) + " 个开关。")
                    + "\r\n点右边的「…」或双击这里放大编辑。";
            }
            tips.SetToolTip(txtLaunch, tip);
        }

        private void EditLaunch()
        {
            using (LaunchForm f = new LaunchForm(txtLaunch.Text))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                txtLaunch.Text = f.Result;
                UpdateLaunchTip();
            }
        }

        private void ManageTweaks()
        {
            string root = txtGame.Text.Trim();
            using (TweakForm f = new TweakForm(root, TweakName()))
            {
                f.ShowDialog(this);
                FillTweaks(f.Chosen);
                if (f.Applied) { chkCfg.Checked = true; Log("参数配置动过了，装机时会写进这台机器"); }
            }
        }

        private void RestoreTweaks()
        {
            string root = txtGame.Text.Trim();
            if (!Core.TestGame(root)) { MessageBox.Show("先在上面选对游戏目录。", "没有游戏目录"); return; }
            int can = Tweaks.RestorableCount(root);
            if (can == 0)
            {
                MessageBox.Show("这台机器上没有 .装之前 / .上一次 备份，没东西可还原。\r\n\r\n（备份是在装机写参数、或在「参数配置」里套用存档时自动留的）", "没有备份");
                return;
            }
            if (MessageBox.Show("把 autoexec.cfg 和 skeeto_setting.ini 回滚到装机之前？\r\n\r\n" + can + " 项可还原，现在这份会留成 .还原前。\r\n只回滚这两个文件，不动平台和 MOD。",
                                "还原参数配置", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            List<string> lines;
            int n = Tweaks.Restore(root, out lines);
            UpdateTweakTip();
            MessageBox.Show((n > 0 ? "已还原 " + n + " 项：\r\n" : "没还原成功：\r\n") + string.Join("\r\n", lines.ToArray()), "还原");
        }

        private void ShowCatTip()
        {
            // 下拉只有 188 px，长名字显示不全；悬停给完整的
            CatChoice c = cboCat.SelectedItem as CatChoice;
            if (c == null || c.Cat.Length == 0) { tips.SetToolTip(cboCat, "按大类和细分筛。括号里是这个分类的 MOD 个数。"); return; }
            tips.SetToolTip(cboCat, c.Sub == null ? ("整个「" + c.Cat + "」类") : ("只看：" + c.Cat + " · " + c.Sub));
        }

        private void FillProfiles()
        {
            cboProfile.Items.Clear();
            List<string> ps = ProfileStore.ListProfiles();
            if (ps.Count == 0) { cboProfile.Items.Add("（还没有方案）"); cboProfile.SelectedIndex = 0; return; }
            foreach (string p in ps) cboProfile.Items.Add(p);
            cboProfile.SelectedIndex = 0;
        }

        /// <summary>自测用：打开「启动项」放大编辑并截图，同时核对默认值与清洗结果。</summary>
        internal void UiTestLaunch()
        {
            Core.Log("INFO", "LAUNCHTEST 输入框里的值 = " + LaunchArgs.Clean(txtLaunch.Text)
                + " | 默认 = " + LaunchArgs.Default
                + " | 含 0/1 占位 = " + LaunchArgs.HasSlot(txtLaunch.Text)
                + " | 参数个数 = " + LaunchArgs.Clean(txtLaunch.Text).Split(' ').Length);
            using (LaunchForm lf = new LaunchForm(txtLaunch.Text))
            {
                lf.StartPosition = FormStartPosition.Manual;
                lf.Location = new System.Drawing.Point(0, 0);
                lf.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(lf.Width, lf.Height))
                {
                    lf.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, lf.Width, lf.Height));
                    bmp.Save(Path.Combine(Core.LogDir, "launch.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                lf.Close();
            }
        }

        /// <summary>自测用：打开「重复嫌疑」并核对内容，截图 + 把结论写日志。</summary>
        internal void UiTestDup()
        {
            List<DupGroup> gs = ConflictCheck.DupGroups(Core.ModList);
            List<string> others = new List<string>();
            foreach (string m in conflictMsgs)
                if (!m.StartsWith("体积完全相同", StringComparison.Ordinal)) others.Add(m);
            int files = 0;
            foreach (DupGroup g in gs) files += g.Items.Count;
            Core.Log("INFO", "DUPTEST " + gs.Count + " 组 / " + files + " 个文件，其它提示 " + others.Count + " 条");
            if (gs.Count == 0) { Core.Log("WARN", "DUPTEST 没有体积重复，跳过"); return; }
            using (DupForm f = new DupForm(gs, others))
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new System.Drawing.Point(0, 0);
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(200);
                Application.DoEvents();
                f.VerifyAll();                       // 真算一遍内容指纹，验证 rar 的 CRC 和独立 vpk 的 SHA 都走得通
                Application.DoEvents();
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(Path.Combine(Core.LogDir, "dup.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                foreach (DupGroup g in gs)
                    Core.Log("INFO", "DUPTEST " + Core.Human(g.Size) + " x" + g.Items.Count + " → " + g.Verdict
                        + (g.VerdictNote.Length > 0 ? "（" + g.VerdictNote + "）" : ""));
                f.Close();
            }
        }

        /// <summary>自测用：打开「参数配置」对话框并截图。</summary>
        internal void UiTestTweaks()
        {
            using (TweakForm tf = new TweakForm(txtGame.Text.Trim(), TweakName()))
            {
                tf.StartPosition = FormStartPosition.Manual;
                tf.Location = new System.Drawing.Point(0, 0);
                tf.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(tf.Width, tf.Height))
                {
                    tf.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, tf.Width, tf.Height));
                    bmp.Save(Path.Combine(Core.LogDir, "tweaks.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                string root = txtGame.Text.Trim();
                Core.Log("INFO", "TWEAKTEST 存档 " + Tweaks.List().Count + " 套，当前选 " + TweakName()
                    + "，可还原 " + (Core.TestGame(root) ? Tweaks.RestorableCount(root) : -1) + " 项，存档根 " + Tweaks.Root);
                tf.Close();
            }
        }

        /// <summary>自测用：按名字找一个套装，打开「选择部件」并截图，验证对话框排版。</summary>
        internal void UiTestParts(string name)
        {
            foreach (ModGroup g in Catalog.Groups)
            {
                if (!g.IsSet) continue;
                if (g.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (PartsForm pf = new PartsForm(g, checkedKeys))
                {
                    pf.StartPosition = FormStartPosition.Manual;
                    pf.Location = new System.Drawing.Point(0, 0);
                    pf.Show();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(250);
                    Application.DoEvents();
                    using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(pf.Width, pf.Height))
                    {
                        pf.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, pf.Width, pf.Height));
                        bmp.Save(Path.Combine(Core.LogDir, "parts.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Core.Log("INFO", "PARTSTEST " + g.Name + " 共 " + g.Parts.Count + " 件，默认勾 " + Catalog.DefaultParts(g).Count + " 件");
                    pf.Close();
                }
                return;
            }
            Core.Log("WARN", "PARTSTEST 没找到套装: " + name);
        }

        /// <summary>自测用：按显示名 / 序号 / 原始路径选一个分类，验证筛选和「位置」列。</summary>
        internal void UiTestCat(string s)
        {
            int n;
            if (int.TryParse(s, out n) && n >= 0 && n < cboCat.Items.Count) { cboCat.SelectedIndex = n; return; }
            foreach (object o in cboCat.Items)
            {
                CatChoice c = o as CatChoice;
                if (c == null) continue;
                if (c.Text == s || c.Text.Trim() == s || c.Cat == s || (c.Sub != null && c.Sub == s)) { cboCat.SelectedItem = c; return; }
            }
            Core.Log("WARN", "UICA T 没找到分类: " + s);
        }

        /// <summary>自测用：把搜索框设成某个词，用来验证筛选与空态。</summary>
        internal void UiTestFilter(string kw)
        {
            txtSearch.Text = kw;
            RefreshMods();
        }

        /// <summary>自测用：全选当前可见项并跑深度冲突检查，结果写日志。</summary>
        internal void UiTestDeep()
        {
            lvMods.BeginUpdate();
            foreach (ListViewItem li in lvMods.Items) li.Checked = true;
            lvMods.EndUpdate();
            List<ModItem> sel = Selected();
            Core.Log("INFO", "DEEP 选中 " + sel.Count + " 个，开始深度检查");
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            List<string> msgs = ConflictCheck.DeepAll(sel);
            foreach (string m in msgs) Core.Log("INFO", "DEEP " + m);
            Core.Log("INFO", "DEEP 用时 " + sw.ElapsedMilliseconds + " ms，共 " + msgs.Count + " 条");
        }

        private void RefreshMods()
        {
            CatChoice cc = cboCat.SelectedItem as CatChoice;
            string cat = cc == null ? "" : cc.Cat;
            string sub = cc == null ? null : cc.Sub;
            string kw = txtSearch.Text.Trim();
            lvMods.BeginUpdate();
            lvMods.Items.Clear();
            shown.Clear();
            foreach (ModGroup g in Catalog.Groups)
            {
                if (cat.Length > 0 && g.Cat != cat) continue;
                if (sub != null && g.Sub != sub) continue;
                if (kw.Length > 0 && !MatchGroup(g, kw)) continue;
                ListViewItem li = new ListViewItem(RowName(g, 0));
                // 改了什么：有硬证据才显示；只有材质路径推出来的用灰字，表示仅供参考
                li.SubItems.Add(g.Subject.Length > 0 ? g.Subject : "—");
                if (g.Subject.Length > 0 && g.SubjectLevel == "材质")
                    li.SubItems[1].ForeColor = Theme.Ink3;
                li.SubItems.Add(Core.Human(g.Size));
                li.SubItems.Add(WhereText(g));
                li.Tag = g;
                int sel = CountChecked(g);
                li.Checked = sel > 0;
                li.Text = RowName(g, sel);
                if (g.IsSet) li.ToolTipText = PartsTip(g);
                lvMods.Items.Add(li);
                shown.Add(g);
            }
            lvMods.EndUpdate();
            // 空态：列表和提示占同一个矩形，二选一显示（叠着放会被 ListView 盖住）
            bool empty = shown.Count == 0;
            lvMods.Visible = !empty;
            lblEmpty.Visible = empty;
            gMod.Caption = "MOD 库（U 盘的 vpk + 压缩包）   显示 " + shown.Count + " / " + Catalog.Groups.Count;
            UpdateSel();
        }

        // 分类列只放得下 130 px：人物的「二代 · 西装Nick」取最后一段，省「人物 · 二代」那截
        private static string WhereText(ModGroup g)
        {
            if (g.Cat == g.Sub) return g.Cat;
            string sub = g.Sub;
            int i = sub.LastIndexOf(" · ", StringComparison.Ordinal);
            if (i >= 0) sub = sub.Substring(i + 3);
            return g.Cat + " · " + sub;
        }

        // 一行显示什么：套装在后面标「已选/共」，让人知道这行不止一个文件
        private static string RowName(ModGroup g, int sel)
        {
            if (!g.IsSet) return g.Name;
            return g.Name + "（" + (sel > 0 ? sel : 0) + "/" + g.Parts.Count + "）";
        }

        private static string PartsTip(ModGroup g)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("这套由 ").Append(g.Parts.Count).Append(" 个 vpk 组成（双击可选部件）：");
            foreach (ModItem p in g.Parts) sb.Append("\r\n· [").Append(p.Role).Append("] ").Append(p.Name);
            return sb.ToString();
        }

        private int CountChecked(ModGroup g)
        {
            int n = 0;
            foreach (ModItem p in g.Parts) if (checkedKeys.Contains(p.Key)) n++;
            return n;
        }

        private bool MatchGroup(ModGroup g, string kw)
        {
            if (g.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (g.Subject.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (g.Where.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (ModItem p in g.Parts)
            {
                if (p.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (ModTags.Get(modTags, p.Key).IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private void SetAll(bool on)
        {
            lvMods.BeginUpdate();
            bulk = true;
            try
            {
                foreach (ListViewItem li in lvMods.Items) li.Checked = on;   // 勾选事件里统一处理部件
            }
            finally { bulk = false; }
            lvMods.EndUpdate();
            UpdateSel();
        }

        private List<ModItem> Selected()
        {
            // checkedKeys 是「零件级」的：一个套装可能只选了其中几个 vpk
            List<ModItem> sel = new List<ModItem>();
            foreach (string k in checkedKeys)
            {
                ModItem it;
                if (Core.ByRel.TryGetValue(k, out it)) sel.Add(it);
            }
            return sel;
        }

        private void UpdateSel()
        {
            List<ModItem> sel = Selected();
            long total = 0;
            foreach (ModItem it in sel) total += it.Size;
            int mods = 0;
            foreach (ModGroup g in Catalog.Groups) if (CountChecked(g) > 0) mods++;
            double rate = Core.LastRateMBps > 0.5 ? Core.LastRateMBps : 20.0;
            string files = mods == sel.Count ? "" : "（" + sel.Count + " 个文件）";
            lblSel.Text = string.Format("已选 {0} 个 MOD{1}  ·  {2}  ·  约 {3} 秒（按 {4:0} MB/s）", mods, files, Core.Human(total), (int)(total / (rate * 1048576.0)), rate);
            lblSel.ForeColor = sel.Count > 0 ? Theme.Ink : Theme.Ink3;
        }

        private void RefreshConflicts()
        {
            RefreshConflicts(false);
        }

        // checkExisting=false：不去逐个 stat 游戏 addons 里的文件（共享盘上很贵），启动时用
        private void RefreshConflicts(bool checkExisting)
        {
            string root = txtGame.Text.Trim();
            if (!Core.TestGame(root)) { lblConflict.Text = ""; conflictMsgs.Clear(); lblConflict.Visible = false; return; }
            List<string> msgs = ConflictCheck.Run(Core.ModList, root, checkExisting);
            conflictMsgs = msgs;
            if (msgs.Count == 0) { lblConflict.Text = ""; lblConflict.Visible = false; return; }
            lblConflict.ForeColor = Theme.Warn;
            lblConflict.Visible = true;
            // 数量 + 最多白占多少 + 一个样本 + 下一步；明细点开看，不硬塞进一行
            List<DupGroup> dups = ConflictCheck.DupGroups(Core.ModList);
            if (dups.Count > 0)
            {
                long waste = 0;
                foreach (DupGroup g in dups) waste += g.Size * (g.Items.Count - 1);
                string one = dups[0].Items[0].Name;
                if (one.Length > 26) one = one.Substring(0, 26) + "…";
                lblConflict.Text = "⚠ " + dups.Count + " 组体积重复 · 最多白占 " + Core.Human(waste) + " · " + one + "\r\n"
                                 + "点这里处理：先核对内容是不是同一个文件，再这次不装 / 移到回收站";
            }
            else
            {
                lblConflict.Text = "⚠ " + msgs.Count + " 条提示（同名 / 已装）\r\n点这里看清单";
            }
            foreach (string m in msgs) Core.Log("WARN", m);
        }

        // 点「N 条重复嫌疑」：不再是只读文本框 —— 每一组都能核对、能这次不装、能删
        private void ShowConflictDetail()
        {
            List<DupGroup> groups = ConflictCheck.DupGroups(Core.ModList);
            List<string> others = new List<string>();
            foreach (string m in conflictMsgs)
                if (!m.StartsWith("体积完全相同", StringComparison.Ordinal)) others.Add(m);

            if (groups.Count == 0)
            {
                // 没有体积重复，只有同名/已装之类的提示，仍然是文本框
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < others.Count; i++) sb.AppendLine((i + 1) + ". " + others[i]);
                sb.AppendLine();
                sb.AppendLine("这些不是体积重复，装机台不自动处理：");
                sb.AppendLine("· 同名文件：右键菜单「清理同名重复」可以每个名字只留第一个");
                sb.AppendLine("· 已装文件：勾「覆盖重装」或「换整套」再装一次就清掉了");
                MessageBox.Show(this, sb.ToString(), "其它提示 · " + others.Count + " 条", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (DupForm f = new DupForm(groups, others))
            {
                bool deleted = false;
                f.OnDelete = delegate(ModItem m)
                {
                    string err = DeleteModFromLibrary(m);
                    if (err == null) deleted = true;
                    return err;
                };
                f.OnSkip = delegate(List<ModItem> items)
                {
                    foreach (ModItem it in items)
                    {
                        checkedKeys.Remove(it.Key);
                        Core.Log("INFO", "这次不装（重复嫌疑里排除）：" + it.Name);
                    }
                    CheckRowsByKeys();
                    UpdateSel();
                };
                f.OnLocate = delegate(ModItem it) { RevealModRow(it); };
                f.ShowDialog(this);

                // 列表是按 Catalog.Groups 渲染的，光从 Core.ModList 里摘掉还会留一行幽灵
                if (deleted) Core.ScanMods();
            }
            // 删过/排除过，列表和提示都要跟着变
            FillCats();
            RefreshMods();
            RefreshConflicts();
        }

        /// <summary>把库里的一个 vpk 挪到 _回收的MOD。返回 null 表示成功。</summary>
        private string DeleteModFromLibrary(ModItem m)
        {
            if (m.IsArchive) return "在压缩包里，装机台不改压缩包";
            try
            {
                Work.MoveModToRecycle(m);
                Core.ModList.Remove(m);
                Core.ByRel.Remove(m.Key);
                checkedKeys.Remove(m.Key);
                Core.Log("OK", "已移到 _回收的MOD：" + m.Name);
                return null;
            }
            catch (Exception ex)
            {
                Core.Log("WARN", "移动 " + m.Name + " 失败: " + ex.Message);
                return ex.Message;
            }
        }

        /// <summary>在主列表里选中某一行；被分类/搜索挡住就先复位再找。</summary>
        private void RevealModRow(ModItem it)
        {
            if (RevealRow(it)) return;
            if (cboCat.Items.Count > 0 && cboCat.SelectedIndex != 0) cboCat.SelectedIndex = 0;
            if (txtSearch.Text.Length > 0) txtSearch.Text = "";
            RefreshMods();
            if (!RevealRow(it)) Core.Log("WARN", "定位不到这一行（可能刚被删/改名）：" + it.Name);
        }

        private bool RevealRow(ModItem it)
        {
            foreach (ListViewItem li in lvMods.Items)
            {
                ModGroup g = li.Tag as ModGroup;
                if (g == null) continue;
                foreach (ModItem p in g.Parts)
                {
                    if (!string.Equals(p.Key, it.Key, StringComparison.OrdinalIgnoreCase)) continue;
                    li.Selected = true;
                    li.Focused = true;
                    li.EnsureVisible();
                    return true;
                }
            }
            return false;
        }

        private void BrowseGame()
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = "选游戏根目录（里面有 left4dead2.exe 的那个文件夹）";
            if (d.ShowDialog() == DialogResult.OK) { txtGame.Text = d.SelectedPath; RefreshConflicts(true); }
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
            RefreshConflicts(true);
        }

        private List<ModItem> CheckedOrSelected()
        {
            List<ModItem> list = new List<ModItem>();
            bool useChecked = lvMods.CheckedItems.Count > 0;
            System.Collections.IEnumerable rows = useChecked ? (System.Collections.IEnumerable)lvMods.CheckedItems
                                                             : (System.Collections.IEnumerable)lvMods.SelectedItems;
            foreach (ListViewItem li in rows)
            {
                ModGroup g = li.Tag as ModGroup;
                if (g == null) continue;
                if (useChecked)
                {
                    foreach (ModItem p in g.Parts) if (checkedKeys.Contains(p.Key)) list.Add(p);
                }
                else list.AddRange(Catalog.DefaultParts(g));
            }
            return list;
        }

        private ModGroup FirstSelectedGroup()
        {
            if (lvMods.SelectedItems.Count == 0) return null;
            return lvMods.SelectedItems[0].Tag as ModGroup;
        }

        // 双击一行：套装 → 挑部件；单个 vpk → 看详情
        private void OpenRow()
        {
            ModGroup g = FirstSelectedGroup();
            if (g == null) return;
            if (g.IsSet) PickParts(g); else ShowModInfo(g);
        }

        private void PickParts()
        {
            ModGroup g = FirstSelectedGroup();
            if (g == null) { MessageBox.Show("先选中一行。"); return; }
            if (!g.IsSet) { MessageBox.Show("这个 MOD 只有一个 vpk，没有可选的部件。"); return; }
            PickParts(g);
        }

        private void PickParts(ModGroup g)
        {
            using (PartsForm f = new PartsForm(g, checkedKeys))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                foreach (ModItem p in g.Parts) checkedKeys.Remove(p.Key);
                foreach (ModItem p in f.Result) checkedKeys.Add(p.Key);
            }
            RefreshMods();
        }

        private void DeleteSelectedMods()
        {
            List<ModItem> list = CheckedOrSelected();
            if (list.Count == 0) { MessageBox.Show("先在列表里勾选，或选中一行。"); return; }
            foreach (ModItem it in list)
                if (it.IsArchive) { MessageBox.Show("「" + it.Name + "」在压缩包里（" + it.Container + "），\r\n装机台不会去改 U 盘上的压缩包。想删请自己在资源管理器里处理。", "不能删除"); return; }
            if (MessageBox.Show("把这 " + list.Count + " 个 vpk 移到 U 盘 _回收的MOD？\r\n（不真删，想用可以再拖回来）", "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            int n = 0;
            foreach (ModItem it in list)
            {
                try { Work.MoveModToRecycle(it); n++; checkedKeys.Remove(it.Key); }
                catch (Exception ex) { Core.Log("WARN", "移动 " + it.Name + " 失败: " + ex.Message); }
            }
            Core.Log("OK", "已移到 _回收的MOD：" + n + " 个");
            Core.ScanMods(); FillCats(); RefreshMods(); RefreshConflicts();
        }

        private void RenameSelectedMod()
        {
            List<ModItem> list = CheckedOrSelected();
            if (list.Count != 1) { MessageBox.Show("重命名一次只能选一个。"); return; }
            if (list[0].IsArchive) { MessageBox.Show("压缩包里的 vpk 不能改名。", "不能重命名"); return; }
            string nn = PromptForm.Ask("新名字（不用写 .vpk）", "重命名 " + list[0].Name);
            if (string.IsNullOrEmpty(nn)) return;
            string err = Work.RenameMod(list[0], nn.Trim());
            if (err != null) { MessageBox.Show(err); return; }
            Core.Log("OK", "已改名: " + list[0].Name + " -> " + nn.Trim() + ".vpk");
            Core.ScanMods(); FillCats(); RefreshMods();
        }

        private void CleanDupMods()
        {
            if (MessageBox.Show("扫描全库同名文件，每个名字只留第一个，其余移到 _回收的MOD？", "清理重复", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            List<string> moved = Work.CleanDuplicateMods();
            Core.Log("OK", "清理重复: 移走 " + moved.Count + " 个");
            MessageBox.Show(moved.Count == 0 ? "没有同名重复。" : ("已移走 " + moved.Count + " 个重复文件，详见日志。"));
            Core.ScanMods(); FillCats(); RefreshMods(); RefreshConflicts();
        }

        private void RevealMod()
        {
            ModGroup g = FirstSelectedGroup();
            if (g == null) { MessageBox.Show("先选中一个 MOD。"); return; }
            ModItem m = g.Main;
            try
            {
                // 压缩包里的东西：直接定位到压缩包本身
                string target = m.IsArchive ? Path.Combine(Core.ModRoot, m.Container) : m.FullPath;
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + target + "\"");
            }
            catch { }
        }

        private void SetTags()
        {
            ModGroup g = FirstSelectedGroup();
            if (g == null) { MessageBox.Show("先选中一个 MOD。"); return; }
            ModItem m = g.Main;
            string cur = ModTags.Get(modTags, m.Key);
            string v = PromptForm.Ask("标签，用逗号分隔（例：武器,刀,替换）\r\n留空 = 清除", "设置标签 " + g.Name + "（当前：" + (cur.Length == 0 ? "无" : cur) + "）");
            if (v == null) return;
            foreach (ModItem p in g.Parts) modTags[p.Key] = v.Trim();
            ModTags.Save(modTags);
            Core.Log("OK", "标签已保存: " + g.Name + " = " + v.Trim());
        }

        private void ShowModInfo(ModGroup g)
        {
            ModItem m = g.Main;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("名字  : " + g.Name);
            sb.AppendLine("分类  : " + g.Where);
            sb.AppendLine("大小  : " + Core.Human(g.Size));
            if (g.IsSet) sb.AppendLine("组成  : " + g.Parts.Count + " 个 vpk（双击这一行可以挑部件）");
            sb.AppendLine("改了什么: " + (g.Subject.Length == 0 ? "（认不出来）" : g.Subject + "  [" + g.SubjectLevel + "]"));
            sb.AppendLine();
            foreach (ModItem p in g.Parts)
            {
                sb.AppendLine("· [" + p.Role + "] " + p.Name + "  " + Core.Human(p.Size));
                if (p.IsArchive) sb.AppendLine("      在压缩包里：" + p.Container);
                else sb.AppendLine("      路径：" + p.RelPath);
                if (p.Subject.Length > 0) sb.AppendLine("      改了什么：" + p.Subject);
            }
            if (!m.IsArchive)
            {
                Cursor = Cursors.WaitCursor;
                List<string> paths = Vpk.Paths(m.FullPath, 200000);
                Cursor = Cursors.Default;
                sb.AppendLine();
                sb.AppendLine("内部条目: " + paths.Count + " 个");
                int n = Math.Min(6, paths.Count);
                for (int i = 0; i < n; i++) sb.AppendLine("  " + paths[i]);
            }
            MessageBox.Show(sb.ToString(), "MOD 信息");
        }

        // 只装勾中的这几个 MOD：不碰平台、不写启动项、不改参数（给"我就想加一个"用）
        private void InstallSelectedOnly()
        {
            string root = txtGame == null ? "" : txtGame.Text.Trim();
            if (root.Length == 0 || !Core.TestGame(root))
            {
                MessageBox.Show("先选对游戏目录（有 left4dead2.exe 的那个）。");
                return;
            }
            List<ModItem> sel = Selected();
            if (sel.Count == 0)
            {
                MessageBox.Show("先在右边列表里勾上要装的 MOD（行首的方块）。");
                return;
            }
            Work.Overwrite = !rdSkip.Checked;
            int copied = 0, skipped = 0;
            List<string> added = null, failed = null;
            try
            {
                RunBusy<int>("正在装这几个 MOD…", delegate
                {
                    Work.CopyMods(root, sel, null, out copied, out skipped, out added, out failed);
                    return 0;
                });
            }
            catch (Exception ex)
            {
                Core.Log("FAIL", "只装 MOD 出错: " + ex.Message);
                MessageBox.Show("装失败了：" + ex.Message + "\r\n详细信息看下面的日志。", "只装 MOD");
                return;
            }
            if (failed != null && failed.Count > 0)
            {
                Core.Log("FAIL", "只装 MOD：失败 " + failed.Count + " 个（" + string.Join("、", failed.ToArray()) + "）");
                MessageBox.Show("有 " + failed.Count + " 个没装进去，多半是游戏正开着（文件被占用）或盘满了。\r\n看下面日志的 FAIL 行。", "没装全", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Dictionary<string, bool> st = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (ModItem it in sel) st[it.Name] = true;
            AddonList.Write(root, st);
            Core.Log("OK", "只装 MOD：写入 " + copied + " 个、跳过 " + skipped + " 个（平台/启动项/参数都没动）");
            MessageBox.Show("装好了：" + copied + " 个文件" + (skipped > 0 ? "，已是同样大小跳过 " + skipped + " 个" : "") + "。\r\n\r\n已经写进 addonlist，进游戏就生效。\r\n平台、启动项、参数都没动。", "只装 MOD");
        }

        private void BulkImport()
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = "选一个放着 MOD 的文件夹（vpk / rar / zip 都认，会按类型自动建文件夹归类）";
            if (d.ShowDialog() != DialogResult.OK) return;
            string src = d.SelectedPath;
            if (src.TrimEnd('\\').StartsWith(Core.ModRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("这个文件夹已经在 MOD 库里了，换个外面的文件夹。");
                return;
            }
            DialogResult how = MessageBox.Show(
                "导入后，源文件夹里的原文件怎么处理？\r\n\r\n[是] 移动（收进库，不占双份体积）\r\n[否] 复制（原文件留着）\r\n",
                "导入方式", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (how == DialogResult.Cancel) return;
            bool move = (how == DialogResult.Yes);
            int imported = 0, skipped = 0;
            List<string> log = null;
            try
            {
                log = RunBusy<List<string>>("正在导入（文件大，别关窗口）…", delegate { return Work.ImportVpks(src, move, out imported, out skipped); });
            }
            catch (Exception ex)
            {
                Core.Log("FAIL", "导入出错: " + ex.Message);
                MessageBox.Show("导入失败：" + ex.Message, "导入");
                return;
            }
            Core.Log("OK", "导入完成：" + imported + " 个，已存在跳过 " + skipped + " 个，方式=" + (move ? "移动" : "复制"));
            FillCats();
            RefreshMods();
            RefreshConflicts(true);
            string sample = "";
            if (log != null)
                for (int i = 0; i < log.Count && i < 6; i++) sample += "\r\n  · " + log[i];
            MessageBox.Show("导入完成：入库 " + imported + " 个，已存在跳过 " + skipped + " 个。" + sample
                + "\r\n\r\n分类文件夹是按文件名猜的；猜不准就在列表里右键「重命名所选」或直接拖到对的文件夹。\r\n"
                + "这些 MOD 现在只是躺在库里，**没装进游戏**——要用的时候勾上，右键「只装勾中的 MOD」。", "导入");
        }

        private void SaveProfile()
        {
            List<ModItem> sel = Selected();
            if (sel.Count == 0) { MessageBox.Show("先勾一些 MOD 再存方案。"); return; }
            string name = PromptForm.Ask("方案名字（比如：日常 / 打僵尸 / 拍视频）", "存为方案");
            if (string.IsNullOrEmpty(name)) return;
            name = name.Trim();
            Profile old = ProfileStore.Load(name);
            if (old != null)
            {
                string when = old.Saved == DateTime.MinValue ? "未知" : old.Saved.ToString("yyyy-MM-dd HH:mm");
                string q = "已经有「" + name + "」这个方案了（" + old.Mods.Count + " 个 MOD，存于 " + when + "）。\r\n\r\n覆盖它？（旧版会自动留一份 .bak，不会丢）";
                if (MessageBox.Show(q, "覆盖方案", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            }
            Profile p = new Profile();
            p.Name = name;
            p.Dxvk = chkDxvk.Checked;
            p.Plat = PlatFlags();
            p.HasOpt = true;
            p.Swap = chkSwap.Checked;
            p.Overwrite = rdOver != null && rdOver.Checked;
            p.Tweak = TweakName();
            p.Launch = txtLaunch == null ? "" : LaunchArgs.ForWrite(txtLaunch.Text);
            foreach (ModItem it in sel) p.Mods.Add(it.Key);
            string bak = ProfileStore.Save(p, true);
            appliedProfile = p.Name;
            FillProfiles();
            Core.Log("OK", "方案已存：" + p.Name + "（" + p.Mods.Count + " 个 MOD" + (bak == null ? "" : "；旧版备份 " + Path.GetFileName(bak)) + "）");
        }

        // 把方案灌进界面。withOptions=false 时只勾 MOD 和平台（下拉预览用），不动安装选项
        private void LoadProfileIntoUi(Profile p, bool withOptions, bool quiet)
        {
            if (p == null) { MessageBox.Show("找不到这个方案（文件可能被删了）。"); return; }
            if (p.Mods.Count == 0) { MessageBox.Show("这个方案是空的。"); return; }
            List<string> ok = new List<string>();
            List<string> miss = new List<string>();
            foreach (string r in p.Mods)
            {
                if (Core.ByRel.ContainsKey(r)) ok.Add(r);
                else miss.Add(r);
            }
            checkedKeys.Clear();
            foreach (string r in ok) checkedKeys.Add(r);
            chkDxvk.Checked = p.Dxvk;
            ApplyPlatFlags(p.Plat);
            if (withOptions && p.HasOpt)
            {
                chkSwap.Checked = p.Swap;
                if (rdOver != null) rdOver.Checked = p.Overwrite;
                if (rdSkip != null) rdSkip.Checked = !p.Overwrite;
                if (cboTweak != null) SelectTweak(p.Tweak);
                if (txtLaunch != null && !string.IsNullOrEmpty(p.Launch)) txtLaunch.Text = p.Launch;
            }
            CheckRowsByKeys();
            UpdateSel();
            if (withOptions) appliedProfile = p.Name;
            Core.Log("OK", "已套用方案：" + p.Name + "（" + ok.Count + " 个 MOD）");
            if (miss.Count > 0)
            {
                Core.Log("WARN", "方案「" + p.Name + "」有 " + miss.Count + " 个引用在库里找不到，已跳过");
                string ex = "";
                for (int i = 0; i < miss.Count && i < 5; i++) ex += "\r\n  · " + miss[i];
                if (miss.Count > 5) ex += "\r\n  · …（还有 " + (miss.Count - 5) + " 个）";
                string q = "方案「" + p.Name + "」里有 " + miss.Count + " 个文件在库里已经找不到了，这次会跳过它们（能用的 " + ok.Count + " 个照常勾上）：" + ex
                    + "\r\n\r\n要修：把文件放回 U 盘原来的位置，或者重新存一版方案。";
                if (!quiet) MessageBox.Show(q, "方案有失效引用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }


        private void PreviewProfile()
        {
            if (initializing) return;
            if (cboProfile.SelectedItem == null) return;
            string name = cboProfile.SelectedItem.ToString();
            if (name.StartsWith("（")) return;
            Profile p = ProfileStore.Load(name);
            if (p == null || p.Mods.Count == 0) return;
            LoadProfileIntoUi(p, false, true);
            int shown = 0;
            foreach (ModItem m in Core.ModList) if (checkedKeys.Contains(m.Key)) shown++;
            Log("方案「" + name + "」：已按它勾好 " + shown + " 个 MOD（含不在当前筛选里的），点「开始装」即装这些");
        }


        private void ApplyProfile()
        {
            if (cboProfile.SelectedItem == null) return;
            string name = cboProfile.SelectedItem.ToString();
            if (name.StartsWith("（")) return;
            LoadProfileIntoUi(ProfileStore.Load(name), true, false);
        }


        // 方案套用后把行的勾选状态跟 checkedKeys 对齐（不逐个触发部件默认逻辑）
        private void CheckRowsByKeys()
        {
            lvMods.BeginUpdate();
            bulk = true;
            try
            {
                foreach (ListViewItem li in lvMods.Items)
                {
                    ModGroup g = li.Tag as ModGroup;
                    if (g == null) continue;
                    int n = CountChecked(g);
                    li.Checked = n > 0;
                    if (g.IsSet) li.Text = RowName(g, n);
                }
            }
            finally { bulk = false; }
            lvMods.EndUpdate();
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
            sb.AppendLine("启动项框  : " + txtLaunch.Left + "," + txtLaunch.Top + " " + txtLaunch.Width + " x " + txtLaunch.Height
                          + "  编辑按钮 " + btnLaunchEdit.Left + "," + btnLaunchEdit.Top + " " + btnLaunchEdit.Width + "x" + btnLaunchEdit.Height);
            sb.AppendLine("启动项值  : " + LaunchArgs.Clean(txtLaunch.Text)
                          + (LaunchArgs.HasSlot(txtLaunch.Text) ? "   [含 0/1 占位]" : ""));
            sb.AppendLine("启动项默认: " + LaunchArgs.Default);
            sb.AppendLine("开始装按钮: " + btnInstall.Left + "," + btnInstall.Top + " " + btnInstall.Width + " x " + btnInstall.Height);
            // 分类下拉到底长什么样，自测时要能看出来（屏幕上只显示 Text，筛选用的是 Path）
            sb.AppendLine("分类下拉  : 共 " + cboCat.Items.Count + " 项，当前第 " + cboCat.SelectedIndex + " 项");
            foreach (object o in cboCat.Items)
            {
                CatChoice c = o as CatChoice;
                if (c == null) { sb.AppendLine("  [?] " + o); continue; }
                sb.AppendLine("  [" + (c.Cat.Length == 0 ? "全部" : c.Cat) + (c.Sub == null ? "" : " / " + c.Sub) + "] " + c.Text);
            }
            return sb.ToString();
        }

        private void ApplyResponsive()
        {
            int W = ClientSize.Width;
            if (W == lastRespW) return;
            lastRespW = W;
            SuspendLayout();
            int leftW = 396;                                   // 左栏定宽：够放最长的复选框文案
            int rightX = Theme.M + leftW + Theme.G;
            int rightW = W - Theme.M - rightX;

            gPlat.Width = leftW;
            gOpt.Width = leftW;
            gLog.Width = leftW;
            txtLog.Width = leftW - 24;
            txtLaunch.Width = leftW - 60 - 12 - 30;
            btnLaunchEdit.Left = leftW - 12 - 26;
            chkSwap.Width = leftW - 24;
            chkStrict.Width = leftW - 24;
            cboTweak.Width = Math.Max(120, leftW - 24 - 70 - 44 - 90 - 8);
            btnTweakRestore.Left = cboTweak.Left + cboTweak.Width + 4;
            btnTweak.Left = btnTweakRestore.Left + btnTweakRestore.Width + 4;

            gMod.Left = rightX;
            gMod.Width = rightW;
            lvMods.Width = rightW - 24;
            // 「位置」列现在写的是「物品库 · AK47」这种短文案，省下的宽度全给名称：名字截断才是真看不清
            if (lvMods.Columns.Count == 4)
                lvMods.Columns[0].Width = Math.Max(190, lvMods.Width - 84 - 76 - 134 - 4);
            lblEmpty.Width = rightW - 24;
            lblSel.Width = rightW - 24;
            lblConflict.Width = rightW - 24;

            // 右栏第一行控件靠右对齐，宽度变化时要跟着挪
            btnNone.Left = rightW - 12 - 44;
            btnAll.Left = btnNone.Left - 8 - 44;
            txtSearch.Width = btnAll.Left - 8 - txtSearch.Left;
            btnManage.Left = rightW - 12 - 68;

            // 顶部
            txtGame.Width = W - 76 - Theme.M - 84 - 8 - 84 - 8;
            btnBrowse.Left = W - Theme.M - 84 - 8 - 84;
            btnFind.Left = W - Theme.M - 84;

            // 底部（按钮整组贴右下）
            btnOpen.Left = W - Theme.M - 104;
            btnRestore.Left = btnOpen.Left - 8 - 96;
            btnSwitch.Left = btnRestore.Left - 8 - 116;
            btnInstall.Left = btnSwitch.Left - 8 - 112;
            lblStatus.Width = W - 2 * Theme.M;
            bar.Width = Math.Max(200, btnInstall.Left - 8 - Theme.M);

            // 多出来的宽度给「名称」——它才是扫读对象；「改了什么」和「大小」定宽，「位置」吃掉剩余
            int w = lvMods.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6;
            const int subjW = 96, sizeW = 78;
            int nameW = Math.Max(200, Math.Min(340, w - subjW - sizeW - 140));
            lvMods.Columns[0].Width = nameW;
            lvMods.Columns[1].Width = subjW;
            lvMods.Columns[2].Width = sizeW;
            lvMods.Columns[3].Width = Math.Max(104, w - nameW - subjW - sizeW);
            ResumeLayout(true);
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

        private static bool GameRunning()
        {
            try { return System.Diagnostics.Process.GetProcessesByName("left4dead2").Length > 0; }
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

        // 方案管理窗口复用主界面的分类清单（同一个 cboCat，显示与主界面完全一致）
        public List<CatChoice> CatChoices()
        {
            List<CatChoice> r = new List<CatChoice>();
            if (cboCat == null) return r;
            foreach (object o in cboCat.Items)
            {
                CatChoice c = o as CatChoice;
                if (c != null) r.Add(c);
            }
            return r;
        }

        public void ApplyProfileByName(string name)
        {
            Profile p = ProfileStore.Load(name);
            if (p == null) { MessageBox.Show("找不到方案「" + name + "」。" ); return; }
            LoadProfileIntoUi(p, true, false);
        }


        private void AppendLog(string line)
        {
            SafeInvoke(delegate
            {
                if (++logLines > 1200) { txtLog.Clear(); logLines = 1; }
                txtLog.AppendText(line + "\r\n");
            });
        }

        // 统一的“安全”调用：窗口已经关掉 / 句柄没了就直接放弃，别再抛 ObjectDisposedException
        private void SafeInvoke(Action a)
        {
            try
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return;
                if (InvokeRequired) Invoke(a); else a();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void SafeBeginInvoke(Action a)
        {
            try
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(a); else a();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // 后台跑一段活、同时保持界面能重绘；结果通过字段回传
        private T RunBusy<T>(string status, Func<T> work)
        {
            bgDone = false; bgResult = null; bgError = null;
            SetStatus(status);
            bar.Style = ProgressBarStyle.Marquee;
            Cursor = Cursors.WaitCursor;
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                try { bgResult = work(); }
                catch (Exception ex) { bgError = ex; }
                finally { bgDone = true; }
            });
            th.IsBackground = true;
            th.Start();
            while (!bgDone)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(40);
            }
            Cursor = Cursors.Default;
            bar.Style = ProgressBarStyle.Blocks;
            if (bgError != null) Core.Log("FAIL", "后台任务出错: " + bgError.Message);
            return bgResult == null ? default(T) : (T)bgResult;
        }

        private void Log(string s) { Core.Log("INFO", s); }
        private void Log(string level, string s) { Core.Log(level, s); }

        private void SetBusy(bool busy)
        {
            // 这个方法会被后台线程调用，必须回到 UI 线程再改控件
            SafeInvoke(delegate
            {
                btnInstall.Enabled = !busy;
                btnRestore.Enabled = !busy;
                btnSwitch.Enabled = !busy;
                btnFind.Enabled = !busy;
                btnBrowse.Enabled = !busy;
            });
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
            if (GameRunning())
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
            if (GameRunning())
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
            string launchVal = LaunchArgs.ForWrite(txtLaunch.Text);
            Core.StrictVerify = chkStrict.Checked;
            if (!wantL4N && !wantDxvk && !wantSkeeto && !wantMqcam && sel.Count == 0 && !wantLaunch) { MessageBox.Show("什么都没勾。"); return; }
            List<string> deep = null;
            if (sel.Count >= 2)
            {
                SetBusy(true);
                deep = RunBusy<List<string>>("正在检查 " + sel.Count + " 个 MOD 有没有文件重叠…", delegate { return ConflictCheck.DeepAll(sel); });
                SetBusy(false);
            }
            if (deep != null && deep.Count > 0)
            {
                string msg = string.Join("\r\n", deep.ToArray()) + "\r\n\r\n仍然继续装吗？";
                if (MessageBox.Show(msg, "这些 MOD 有文件重叠", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                foreach (string l in deep) Core.Log("WARN", l);
            }

            // 装之前先看目标盘放不放得下
            long need = 0;
            foreach (ModItem it in sel) need += it.Size;
            try
            {
                DriveInfo di = new DriveInfo(Path.GetPathRoot(root));
                if (di.AvailableFreeSpace < need + 300L * 1024 * 1024)
                {
                    if (MessageBox.Show("目标盘只剩 " + Core.Human(di.AvailableFreeSpace) + "，这次要铺 " + Core.Human(need) + "。\r\n空间可能不够，仍然继续吗？", "空间可能不足", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                }
            }
            catch { }
            Work.Overwrite = !rdSkip.Checked;          // 所有 Install* 都读这个开关
            Core.Cfg.GameRoot = root;
            Core.Cfg.Dxvk = wantDxvk;
            Core.Cfg.Skeeto = wantSkeeto;
            Core.Cfg.AssetPack = wantAsset;
            Core.Cfg.Mqcam = wantMqcam;
            Core.Cfg.Launch = wantLaunch;
            Core.Cfg.CfgFile = wantCfg;
            Core.Cfg.Overwrite = !rdSkip.Checked;
            Core.Cfg.TweakSet = TweakName();
            Core.Cfg.LaunchOptions = launchVal;
            Core.Cfg.Recent.RemoveAll(delegate(string s) { return string.Equals(s, root, StringComparison.OrdinalIgnoreCase); });
            Core.Cfg.Recent.Insert(0, root);
            while (Core.Cfg.Recent.Count > 6) Core.Cfg.Recent.RemoveAt(Core.Cfg.Recent.Count - 1);
            Core.Cfg.Mods = new List<string>();
            foreach (ModItem it in sel) Core.Cfg.Mods.Add(it.Key);
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
                    Core.Log("INFO", Work.Overwrite ? "模式：覆盖重装（已装的也全部重写一遍）" : "模式：跳过已装（只写缺的和不一样的）");
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
                        string tw = TweakName();
                        if (tw.Length > 0 && Tweaks.Exists(tw))
                        {
                            Core.Log("STEP", "写参数配置「" + tw + "」");
                            SetStatus("正在写参数配置…");
                            List<string> twLines;
                            string twErr = Tweaks.Apply(root, tw, out twLines);
                            if (twErr != null) Core.Log("WARN", "参数配置没写进去: " + twErr);
                        }
                        else
                        {
                            Core.Log("STEP", "搬 autoexec.cfg（没选参数存档，走旧做法）");
                            SetStatus("正在搬 autoexec.cfg…");
                            Work.InstallCfg(root);
                        }
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
                    SafeBeginInvoke(delegate
                    {
                        UpdateTweakTip();
                        RefreshConflicts(true);
                        bool doTest = MessageBox.Show(
                            "装好了。\r\n\r\n要不要先跑一次自检？（最多 2 分钟）\r\n自检＝装机台自己开一次游戏，看会不会崩。崩了就说明这次装的 MOD 里有坏包（安全，不改任何文件）。",
                            "完成 · 自检", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                        if (!doTest)
                        {
                            if (MessageBox.Show("那现在启动游戏吗？", "完成", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                            {
                                try { System.Diagnostics.Process.Start("steam://rungameid/550"); }
                                catch (Exception ex2) { Core.Log("WARN", "启动游戏失败（请在 Steam 里手动开）: " + ex2.Message); }
                            }
                            return;
                        }
                        Core.Log("STEP", "自检：起游戏看会不会崩（最多 120 秒）");
                        SetBusy(true);
                        SetStatus("自检中：正在起游戏…");
                        Thread thTest = new Thread(delegate()
                        {
                            SelfTest.Result sr = null;
                            try { sr = SelfTest.Run(root, 120, sel.Count, delegate(string s) { SetStatus(s); }); }
                            catch (Exception ex3) { Core.Log("FAIL", "自检出错: " + ex3.Message); }
                            SetBusy(false);
                            SetBar(0);
                            SetStatus("就绪");
                            if (sr != null && appliedProfile.Length > 0 && (sr.Kind == "crash" || sr.Kind == "clean"))
                                ProfileStore.SetSelfTest(appliedProfile, sr.Kind == "crash" ? "FAIL" : "PASS", sr.Seconds);
                            if (sr == null) return;
                            Core.Log(sr.Kind == "crash" ? "FAIL" : "OK", "自检: " + sr.Message.Replace("\r\n", " "));
                            SafeBeginInvoke(delegate
                            {
                                MessageBox.Show(sr.Message, "自检结果 - " + sr.Kind,
                                    MessageBoxButtons.OK,
                                    sr.Kind == "crash" ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                            });
                        });
                        thTest.IsBackground = true;
                        thTest.Start();
                    });
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
            SafeInvoke(delegate { lblStatus.Text = s; });
        }

        private void SetBar(int permille)
        {
            SafeInvoke(delegate
            {
                if (bar.Style != ProgressBarStyle.Continuous) { bar.Style = ProgressBarStyle.Continuous; bar.Maximum = 1000; }
                int v = permille < 0 ? 0 : (permille > 1000 ? 1000 : permille);
                bar.Value = v;
            });
        }
    }

    public class ProfileManageForm : Form
    {
        private ListBox lb;
        private CheckedListBox clb;
        private TextBox txtSearch;
        private CheckBox chkOnlyPicked;
        private ComboBox cboCat;
        private Label lblTitle2, lblInfo;
        private MainForm owner;

        private Profile current;
        private readonly List<string> rowKeys = new List<string>();
        private HashSet<string> picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool filling = false;
        private bool dirty = false;
        private bool hintOn = true;
        private const string HintText = "搜 MOD 名字或分类…";

        public ProfileManageForm(MainForm o)
        {
            owner = o;
            Text = "方案管理";
            ClientSize = new Size(820, 520);
            MinimumSize = new Size(760, 470);
            BackColor = Theme.Bg;
            Font = Theme.UI;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            SuspendLayout();

            // ---- 左：方案 ----
            Label l1 = new Label(); l1.Text = "方案"; l1.SetBounds(12, 12, 60, 20); Theme.Dress(l1); Controls.Add(l1);
            lb = new ListBox(); lb.SetBounds(12, 34, 220, 386);
            lb.BorderStyle = BorderStyle.FixedSingle; lb.BackColor = Theme.Surface; lb.ForeColor = Theme.Ink;
            lb.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            lb.SelectedIndexChanged += delegate { LoadSel(); }; lb.DoubleClick += delegate { ApplySel(); }; Controls.Add(lb);

            FlatButton ap = new FlatButton(); ap.Text = "套用"; ap.Primary = true; ap.SetBounds(12, 428, 68, Theme.H);
            ap.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; ap.Click += delegate { ApplySel(); }; Controls.Add(ap);
            FlatButton rn = new FlatButton(); rn.Text = "改名"; rn.SetBounds(86, 428, 68, Theme.H);
            rn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; rn.Click += delegate { RenameSel(); }; Controls.Add(rn);
            FlatButton de = new FlatButton(); de.Text = "删除"; de.SetBounds(160, 428, 68, Theme.H);
            de.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; de.Click += delegate { DeleteSel(); }; Controls.Add(de);
            FlatButton cp = new FlatButton(); cp.Text = "另存为…"; cp.SetBounds(12, 462, 104, Theme.H);
            cp.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; cp.Click += delegate { CopySel(); }; Controls.Add(cp);
            FlatButton im = new FlatButton(); im.Text = "导入…"; im.SetBounds(124, 462, 96, Theme.H);
            im.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; im.Click += delegate { ImportFile(); }; Controls.Add(im);

            // ---- 右：方案内容 ----
            lblTitle2 = new Label(); lblTitle2.Text = "这套会装什么（勾/取消 = 加减 MOD）"; lblTitle2.SetBounds(248, 12, 320, 20);
            lblTitle2.Anchor = AnchorStyles.Top | AnchorStyles.Left; Theme.Dress(lblTitle2); Controls.Add(lblTitle2);

            cboCat = new ComboBox(); cboCat.SetBounds(248, 34, 160, 24);
            cboCat.DropDownStyle = ComboBoxStyle.DropDownList;
            cboCat.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Theme.Dress(cboCat);
            foreach (CatChoice cc in owner.CatChoices()) cboCat.Items.Add(cc);
            if (cboCat.Items.Count > 0) cboCat.SelectedIndex = 0;
            cboCat.SelectedIndexChanged += delegate { RebuildRows(); };
            Controls.Add(cboCat);

            txtSearch = new TextBox(); txtSearch.SetBounds(416, 34, 190, 24);
            txtSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Theme.Dress(txtSearch);
            txtSearch.Text = HintText; txtSearch.ForeColor = Theme.Ink3;
            txtSearch.GotFocus += delegate
            {
                if (hintOn) { hintOn = false; txtSearch.Text = ""; txtSearch.ForeColor = Theme.Ink; }
            };
            txtSearch.LostFocus += delegate
            {
                if (txtSearch.Text.Trim().Length == 0) { hintOn = true; txtSearch.Text = HintText; txtSearch.ForeColor = Theme.Ink3; }
            };
            txtSearch.TextChanged += delegate { if (!hintOn) RebuildRows(); };
            Controls.Add(txtSearch);

            FlatButton selAll = new FlatButton(); selAll.Text = "全选"; selAll.SetBounds(248, 62, 64, Theme.H);
            selAll.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            selAll.Click += delegate { SetFiltered(true); }; Controls.Add(selAll);
            FlatButton selNone = new FlatButton(); selNone.Text = "全不选"; selNone.SetBounds(318, 62, 72, Theme.H);
            selNone.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            selNone.Click += delegate { SetFiltered(false); }; Controls.Add(selNone);

            chkOnlyPicked = new CheckBox(); chkOnlyPicked.Text = "只看已选"; chkOnlyPicked.SetBounds(714, 36, 94, 22);
            chkOnlyPicked.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Theme.Dress(chkOnlyPicked);
            chkOnlyPicked.CheckedChanged += delegate { RebuildRows(); };
            Controls.Add(chkOnlyPicked);

            ToolTip tips = new ToolTip();
            tips.SetToolTip(selAll, "把当前筛选出来的行全部勾上（算未保存改动，点「保存」才写回方案）");
            tips.SetToolTip(selNone, "把当前筛选出来的行全部取消（同样要点「保存」）");
            tips.SetToolTip(txtSearch, "按 MOD 文件名或分类筛选；配合「只看已选」可以只看这个方案里的");

            clb = new CheckedListBox(); clb.SetBounds(248, 94, 560, 326);
            clb.BorderStyle = BorderStyle.FixedSingle; clb.BackColor = Theme.Surface; clb.ForeColor = Theme.Ink;
            clb.CheckOnClick = true;
            clb.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            clb.ItemCheck += OnItemCheck;
            Controls.Add(clb);

            lblInfo = new Label(); lblInfo.SetBounds(248, 428, 380, 20); lblInfo.Text = "";
            lblInfo.ForeColor = Theme.Ink3; lblInfo.Font = Theme.UISmall;
            lblInfo.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(lblInfo);

            FlatButton sv = new FlatButton(); sv.Text = "保存"; sv.Primary = true; sv.SetBounds(636, 452, 80, Theme.H);
            sv.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; sv.Click += delegate { SaveScheme(); }; Controls.Add(sv);
            FlatButton rl = new FlatButton(); rl.Text = "还原"; rl.SetBounds(724, 452, 80, Theme.H);
            rl.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; rl.Click += delegate { LoadSel(); }; Controls.Add(rl);
            FlatButton ex = new FlatButton(); ex.Text = "导出…"; ex.SetBounds(636, 486, 80, Theme.H);
            ex.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; ex.Click += delegate { ExportSel(); }; Controls.Add(ex);
            FlatButton ea = new FlatButton(); ea.Text = "全部导出…"; ea.SetBounds(716, 486, 92, Theme.H);
            ea.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; ea.Click += delegate { ExportAll(); }; Controls.Add(ea);
            FlatButton cl = new FlatButton(); cl.Text = "关闭"; cl.SetBounds(716, 424, 92, Theme.H);
            cl.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; cl.DialogResult = DialogResult.Cancel; Controls.Add(cl);
            CancelButton = cl;

            ResumeLayout(true);
            LoadList();
        }

        private void LoadList()
        {
            Profile keep = Sel();
            string keepName = keep == null ? null : keep.Name;
            lb.Items.Clear();
            List<Profile> items = ProfileStore.LoadAll();
            foreach (Profile p in items) lb.Items.Add(p);
            if (lb.Items.Count == 0)
            {
                lb.Items.Add("（还没有方案）");
                clb.Items.Clear(); rowKeys.Clear(); lblInfo.Text = ""; current = null;
                return;
            }
            int idx = -1;
            if (keepName != null)
                for (int i = 0; i < items.Count; i++) if (items[i].Name == keepName) { idx = i; break; }
            lb.SelectedIndex = idx < 0 ? 0 : idx;
        }

        private Profile Sel()
        {
            return lb.SelectedItem as Profile;
        }

        private void LoadSel()
        {
            Profile p = Sel();
            if (p == null) { current = null; clb.Items.Clear(); rowKeys.Clear(); lblInfo.Text = ""; return; }
            Profile fresh = ProfileStore.Load(p.Name);
            current = fresh == null ? p : fresh;
            picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in current.Mods) picked.Add(k);
            dirty = false;
            MarkTitle();
            RebuildRows();
        }

        private void MarkTitle()
        {
            Text = "方案管理" + (dirty ? " *" : "") + (current == null ? "" : "  ——  " + current.Name);
        }

        private string TextOf(ModItem it)
        {
            string cat = it.Cat + (string.IsNullOrEmpty(it.Sub) ? "" : "/" + it.Sub);
            ModGroup g;
            if (Catalog.GroupByKey.TryGetValue(it.GroupKey, out g) && g.IsSet)
                return it.Name + "    ·  " + cat + "    ·  套装：" + g.Name;
            return it.Name + "    ·  " + cat;
        }

        private void RebuildRows()
        {
            filling = true;
            try
            {
                clb.BeginUpdate();
                clb.Items.Clear();
                rowKeys.Clear();
                string q = hintOn || txtSearch.Text == null ? "" : txtSearch.Text.Trim();
                CatChoice cc = cboCat.SelectedItem as CatChoice;
                string fcat = cc == null ? "" : cc.Cat;
                string fsub = cc == null ? null : cc.Sub;
                bool only = chkOnlyPicked.Checked;
                List<ModItem> sorted = new List<ModItem>(Core.ModList);
                sorted.Sort(delegate(ModItem a, ModItem b)
                {
                    int c = string.Compare(a.Cat, b.Cat, StringComparison.Ordinal);
                    if (c != 0) return c;
                    c = string.Compare(a.Sub, b.Sub, StringComparison.Ordinal);
                    if (c != 0) return c;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                foreach (ModItem it in sorted)
                {
                    if (only && !picked.Contains(it.Key)) continue;
                    if (fcat.Length > 0 && !string.Equals(it.Cat, fcat, StringComparison.Ordinal)) continue;
                    if (fsub != null && !string.Equals(it.Sub, fsub, StringComparison.Ordinal)) continue;
                    string text = TextOf(it);
                    if (q.Length > 0 && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int i = clb.Items.Add(text);
                    rowKeys.Add(it.Key);
                    if (picked.Contains(it.Key)) clb.SetItemChecked(i, true);
                }
                clb.EndUpdate();
            }
            finally { filling = false; }
            UpdateInfo();
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (filling) return;
            int i = e.Index;
            if (i < 0 || i >= rowKeys.Count) return;
            string key = rowKeys[i];
            if (e.NewValue == CheckState.Checked) picked.Add(key); else picked.Remove(key);
            if (!dirty) { dirty = true; MarkTitle(); }
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            int miss = 0;
            foreach (string k in picked) if (!Core.ByRel.ContainsKey(k)) miss++;
            string t = "已选 " + picked.Count + " 个文件 ｜ 列表显示 " + rowKeys.Count + " 行";
            if (miss > 0) t += " ｜ ⚠ " + miss + " 个库里找不到（保存后仍保留）";
            if (dirty) t += " ｜ 有改动未保存";
            lblInfo.Text = t;
        }

        // 批量：对"当前列表里显示的行"全选 / 全不选
        private void SetFiltered(bool on)
        {
            if (rowKeys.Count == 0) return;
            bool changed = false;
            foreach (string k in rowKeys)
            {
                if (on && !picked.Contains(k)) { picked.Add(k); changed = true; }
                else if (!on && picked.Contains(k)) { picked.Remove(k); changed = true; }
            }
            if (!changed) return;
            filling = true;
            try
            {
                for (int i = 0; i < rowKeys.Count; i++) clb.SetItemChecked(i, on);
            }
            finally { filling = false; }
            dirty = true;
            MarkTitle();
            UpdateInfo();
        }

        private void SaveScheme()
        {
            Profile p = Sel();
            if (p == null || current == null) { MessageBox.Show("先选一个方案。"); return; }
            Profile fresh = ProfileStore.Load(current.Name);
            if (fresh == null) { MessageBox.Show("这个方案的文件不见了。"); return; }
            List<string> mods = new List<string>();
            foreach (ModItem it in Core.ModList) if (picked.Contains(it.Key)) mods.Add(it.Key);
            foreach (string k in picked) if (!mods.Contains(k)) mods.Add(k);
            fresh.Mods = mods;
            string bak = ProfileStore.Save(fresh, true);
            current = fresh;
            dirty = false;
            LoadList();
            MessageBox.Show("已保存「" + fresh.Name + "」：" + mods.Count + " 个文件。" + (bak == null ? "" : "\r\n旧版备份：" + Path.GetFileName(bak)), "保存方案");
        }

        private void ApplySel()
        {
            Profile p = Sel();
            if (p == null) return;
            if (dirty && MessageBox.Show("这个方案有改过但没保存，套用会丢掉这些改动。继续？", "未保存", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            owner.ApplyProfileByName(p.Name);
            Close();
        }

        private void RenameSel()
        {
            Profile p = Sel();
            if (p == null) return;
            string nn = PromptForm.Ask("新名字（当前：" + p.Name + "）", "给方案改名");
            if (string.IsNullOrEmpty(nn)) return;
            string err = ProfileStore.Rename(p.Name, nn.Trim());
            if (err != null) { MessageBox.Show(err); return; }
            LoadList();
        }

        private void CopySel()
        {
            Profile p = Sel();
            if (p == null) return;
            string nn = PromptForm.Ask("复制成什么名字？（当前：" + p.Name + "）", "另存为");
            if (string.IsNullOrEmpty(nn)) return;
            string err = ProfileStore.CopyAs(p.Name, nn.Trim());
            if (err != null) { MessageBox.Show(err); return; }
            LoadList();
        }

        private void DeleteSel()
        {
            Profile p = Sel();
            if (p == null) return;
            if (MessageBox.Show("删除方案「" + p.Name + "」？", "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            ProfileStore.Delete(p.Name);
            LoadList();
        }

        private void ExportSel()
        {
            Profile p = Sel();
            if (p == null) { MessageBox.Show("先选一个方案。"); return; }
            SaveFileDialog d = new SaveFileDialog(); d.Filter = "方案文件|*.txt"; d.FileName = p.Name + ".txt";
            if (d.ShowDialog() != DialogResult.OK) return;
            File.Copy(Path.Combine(ProfileStore.DirOf(), p.Name + ".txt"), d.FileName, true);
            MessageBox.Show("已导出: " + d.FileName);
        }

        private void ExportAll()
        {
            List<Profile> all = ProfileStore.LoadAll();
            if (all.Count == 0) { MessageBox.Show("还没有方案可导出。"); return; }
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = "选一个文件夹，把所有方案（" + all.Count + " 个）导出进去";
            if (d.ShowDialog() != DialogResult.OK) return;
            int n = 0;
            foreach (Profile p in all)
            {
                try
                {
                    File.Copy(Path.Combine(ProfileStore.DirOf(), p.Name + ".txt"), Path.Combine(d.SelectedPath, p.Name + ".txt"), true);
                    n++;
                }
                catch { }
            }
            MessageBox.Show("已导出 " + n + " 个方案到：\r\n" + d.SelectedPath);
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
            Profile p = ProfileStore.Load(name);
            LoadList();
            if (p != null && p.Mods.Count == 0)
                MessageBox.Show("已导入方案: " + name + "\r\n\r\n⚠ 但这个文件里一个 MOD 都没读到，可能是别的版本导出的。", "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show("已导入方案: " + name + (p == null ? "" : "（" + p.Mods.Count + " 个 MOD）"));
        }
    }



    /// <summary>分类下拉的一项：两级（大类 + 细分），屏幕上看人话，筛选用字段。</summary>
    public sealed class CatChoice
    {
        public readonly string Cat;   // "" = 全部；地图/人物/枪械/药品/喷漆/杂项
        public readonly string Sub;   // null = 整个大类
        public readonly string Text;
        public CatChoice(string cat, string sub, string text) { Cat = cat; Sub = sub; Text = text; }
        public override string ToString() { return Text; }
    }

    public class PromptForm : Form
    {
        private TextBox box;
        private PromptForm(string title, string hint) : this(title, hint, "") { }

        private PromptForm(string title, string hint, string def)
        {
            Initialize(title, hint, def);
        }

        private void Initialize(string title, string hint, string def)
        {
            Text = title; ClientSize = new Size(420, 132); Font = Theme.UI; BackColor = Theme.Bg;
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false;
            Label l = new Label(); l.Text = hint; l.SetBounds(12, 12, 396, 20); Theme.Dress(l); Controls.Add(l);
            box = new TextBox(); box.SetBounds(12, 38, 396, 24); Theme.Dress(box); box.Text = def ?? ""; Controls.Add(box);
            FlatButton ok = new FlatButton(); ok.Text = "确定"; ok.Primary = true; ok.SetBounds(240, 76, 80, Theme.H); ok.DialogResult = DialogResult.OK; Controls.Add(ok);
            FlatButton no = new FlatButton(); no.Text = "取消"; no.SetBounds(328, 76, 80, Theme.H); no.DialogResult = DialogResult.Cancel; Controls.Add(no);
            AcceptButton = ok; CancelButton = no;
        }
        public static string Ask(string hint, string title)
        {
            return Ask(hint, title, "");
        }

        public static string Ask(string hint, string title, string def)
        {
            PromptForm f = new PromptForm(title, hint, def);
            if (f.ShowDialog() != DialogResult.OK) return null;
            return f.box.Text.Trim();
        }
    }
}
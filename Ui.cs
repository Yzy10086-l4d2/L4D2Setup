using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Installer
{
    public partial class MainForm : Form
    {
        private ComboBox txtGame;
        private ComboBox cboMode;          // 工作模式：U 盘模式 / 本地模式
        private bool modeSync;             // 回填下拉时别触发重扫
        private FlatButton btnBrowse, btnFind, btnInstall, btnRestore, btnOpen, btnSwitch;
        private CheckBox chkL4N, chkSkeeto, chkAsset, chkMqcam, chkDxvk, chkLaunch, chkCfg, chkSwap, chkStrict;
        private RadioButton rdSkip, rdOver;
        private ComboBox cboTweak;
        private FlatButton btnTweak, btnTweakRestore;
        private FlatButton btnLaunchEdit;
        private TextBox txtLaunch;
        private ComboBox cboProfile;
        // 最近套用/保存过的方案名（空=没套方案）：自检结果写回它
        private string appliedProfile = "";
        private Section gMod, gLog, gPlat, gOpt;
        private Button btnManage;
        private TextBox txtSearch;
        private Button btnSaveProfile, btnApplyProfile;
        private Panel viewHost;                  // 右栏：方案树就嵌在这里
        private Label lblLoading;                // 启动读取期间的提示，读完第一屏就撤
        private CatTree treeView;                // 方案树：按「能替换的东西」看，一个位置只留一个 MOD
        private Label lblSel, lblStatus, lblConflict;
        // 方案标记：当前下拉里那套方案包含哪些文件，用来在列表里一眼看出「这条属不属于方案」
        private Label lblScheme;
        private readonly HashSet<string> schemeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string schemeName = "";
        private ToolTip tips;
        private ProgressBar bar;
        private TextBox txtLog;
        private readonly HashSet<string> checkedKeys = new HashSet<string>();
        // 展开了哪些套装（按 ModGroup.Key）：展开后每个 vpk 单独一行，可以只勾其中一个
        private readonly HashSet<string> expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> conflictMsgs = new List<string>();
        private bool bulk;                       // 批量勾选时先别逐项刷新
        private bool applying;                   // 正在回填配置：这时的勾选变化不要触发联动
        private bool refreshing;                 // 正在重建列表：这时的勾选状态是「照着现有勾选画的」，不许反过来改 checkedKeys
        private int logLines;                    // 日志行数，超了就截断，避免文本框越用越慢
        private readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer();
        private volatile bool bgDone;            // RunBusy 的完成标志
        private int lastRespW, lastRespH = -1;   // 上一次重排用的窗口宽度（拖动时去重，少重排一半以上）
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
            // 一开始就开够大：右栏要放下方案树和一整张类列表，940 宽时列挤得没地方
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w0 = Math.Min(1440, Math.Max(1120, wa.Width - 100));
            int h0 = Math.Min(940, Math.Max(740, wa.Height - 80));
            ClientSize = new Size(Math.Min(w0, wa.Width - 20), Math.Min(h0, wa.Height - 20));
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
            lblStatus.Text = Core.IsLocal ? "正在扫描游戏目录…" : "正在读取 U 盘…";
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
                    // 先找游戏再扫库：游戏 addons 里的已装 MOD 是扫描范围的一部分，
                    // 第一次在别的电脑上打开也得第一时间认出来，不能等下次启动
                    string found = null;
                    if (needFind)
                    {
                        List<string> g = Core.FindGames();
                        if (g.Count > 0) found = g[0];
                        Core.Log("INFO", "启动计时: 找游戏 " + __sw.ElapsedMilliseconds + " ms（找到 " + g.Count + " 个）");
                        __sw.Restart();
                    }
                    Core.ScanMods();
                    Core.Log("INFO", "启动计时: 扫库 " + __sw.ElapsedMilliseconds + " ms（" + Core.ModList.Count + " 个）");

                    SafeInvoke(delegate
                    {
                        System.Diagnostics.Stopwatch __sw2 = System.Diagnostics.Stopwatch.StartNew();
                        if (found != null) { txtGame.Text = found; Core.Log("INFO", "自动找到游戏: " + found); }
                        else if (!Core.TestGame(txtGame.Text)) Core.Log("WARN", "没自动找到游戏，点「自动找」或「浏览…」");
                        ApplyConfig();
                        FillProfiles();
                        Core.Log("INFO", "启动计时: 填充列表 " + __sw2.ElapsedMilliseconds + " ms");
                        __sw2.Restart();
                        RefreshConflicts(false);          // 启动只做内存里的同名/同大小判断
                        Core.Log("INFO", "启动计时: 冲突检查 " + __sw2.ElapsedMilliseconds + " ms");
                        initializing = false;
                        ShowPackStatus(packs, packOk);
                        ShowTree();                        // 启动第一屏就是方案树
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
            int CH = ClientSize.Height;                        // 布局按实际客户区算，别再写死 700 时代的坐标
            int leftW = 396;
            int rightX = Theme.M + leftW + Theme.G;
            int rightW = W - Theme.M - rightX;

            // ---- 顶部：游戏目录 ----
            Label l1 = new Label(); l1.Text = "游戏目录"; l1.SetBounds(Theme.M, 20, 56, 20); Theme.Dress(l1); Controls.Add(l1);
            const int modeW = 104;
            int modeX = W - Theme.M - 84 - 8 - 84 - 8 - modeW;
            txtGame = new ComboBox(); txtGame.SetBounds(76, 18, modeX - 8 - 76, 24);
            txtGame.DropDownStyle = ComboBoxStyle.DropDown; Theme.Dress(txtGame); Controls.Add(txtGame);
            cboMode = new ComboBox(); cboMode.SetBounds(modeX, 18, modeW, 24);
            cboMode.DropDownStyle = ComboBoxStyle.DropDownList; Theme.Dress(cboMode);
            cboMode.Items.Add("U 盘模式"); cboMode.Items.Add("本地模式");
            cboMode.SelectedIndexChanged += delegate { OnModeChanged(); };
            tips.SetToolTip(cboMode, "U 盘模式：U 盘库是主，装 MOD = 从库里复制到游戏。\r\n游戏目录里和库里同名同大小的那份会合并显示（标「已装」），不再重复列。\r\n本地模式：不扫 U 盘库，游戏目录 addons 直接当库；删除 = 从游戏里卸掉（文件进回收目录）。");
            Controls.Add(cboMode);
            btnBrowse = new FlatButton(); btnBrowse.Text = "浏览…"; btnBrowse.SetBounds(W - Theme.M - 84 - 8 - 84, 16, 84, Theme.H); btnBrowse.Click += delegate { BrowseGame(); }; Controls.Add(btnBrowse);
            btnFind = new FlatButton(); btnFind.Text = "自动找"; btnFind.SetBounds(W - Theme.M - 84, 16, 84, Theme.H); btnFind.Click += delegate { FindGame(); }; Controls.Add(btnFind);

            // ---- 左栏上：装什么 ----
            Section gp = new Section();
            gp.Caption = "装什么"; gp.SetBounds(Theme.M, 56, leftW, 222); Controls.Add(gp);
            chkL4N = AddChk(gp, "L4N 平台（只有 MQCam 相机要用它）", 12, 28, true, true);
            tips.SetToolTip(chkL4N, "L4N 不是强依赖：不装它照样能装 Skeeto / dxvk / MOD。\r\n只有 MQCam 相机要跑在它的 neko 框架里 —— 勾 MQCam 时会自动把 L4N 带上，取消 L4N 会把 MQCam 一起取消。\r\n装过一次之后再装会自动跳过（除非在下面选「覆盖重装」）。");
            chkSkeeto = AddChk(gp, "Skeeto 增强框架（命中反馈 / 准星 / HUD）", 12, 52, true, true);
            chkAsset = AddChk(gp, "Skeeto 素材包 177 MB", 12, 76, true, true);
            chkMqcam = AddChk(gp, "MQCam 相机（F2 / F3 / F4 视角）", 12, 100, true, true);
            tips.SetToolTip(chkMqcam, "相机模组要跑在 L4N 的 neko 框架里：勾它会自动把上面的 L4N 一起勾上。");
            // L4N 和 MQCam 的依赖只在这两格之间：勾相机 → 带上平台；去掉平台 → 相机也跟着去掉
            chkMqcam.CheckedChanged += delegate
            {
                if (applying || !chkMqcam.Checked || chkL4N.Checked) return;
                chkL4N.Checked = true;
                Log("MQCam 相机要用 L4N 的 neko 框架，已自动勾上 L4N");
            };
            chkL4N.CheckedChanged += delegate
            {
                if (applying || chkL4N.Checked || !chkMqcam.Checked) return;
                chkMqcam.Checked = false;
                Log("不装 L4N 的话 MQCam 也跑不起来，已把 MQCam 一起取消（要相机就两个都勾）");
            };
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
            chkSwap = new CheckBox(); chkSwap.Text = "换整套（把不在这套里的 MOD 关掉）";
            chkSwap.SetBounds(12, 28, leftW - 24, 22); Theme.Dress(chkSwap); gOpt.Controls.Add(chkSwap);
            chkSwap.Checked = true;
            tips.SetToolTip(chkSwap, "切方案时，上一套的 MOD 在 addonlist 里改成「关闭」，文件留在 addons 里不动。\r\n"
                + "切回去直接打开就能用，不用重新复制。\r\n"
                + "只认 U 盘库里有的 vpk —— 你自己丢进 addons 的不会被碰。");
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
            gl.Caption = "日志"; gl.SetBounds(Theme.M, 438, leftW, CH - 438 - 116); Controls.Add(gl);
            FlatButton btnLog = new FlatButton(); btnLog.Text = "打开日志"; btnLog.SetBounds(leftW - 12 - 68 - 6 - 52, 1, 68, 20); btnLog.Font = Theme.UISmall; btnLog.Click += delegate { try { System.Diagnostics.Process.Start("explorer.exe", Core.LogDir); } catch { } }; gl.Controls.Add(btnLog);
            FlatButton btnEnv = new FlatButton(); btnEnv.Text = "环境"; btnEnv.SetBounds(leftW - 12 - 52, 1, 52, 20); btnEnv.Font = Theme.UISmall; btnEnv.Click += delegate { ShowEnv(); }; gl.Controls.Add(btnEnv);
            txtLog = new TextBox();
            txtLog.SetBounds(12, 30, leftW - 24, Math.Max(60, CH - 438 - 116 - 34));   // 日志框撑满卡片
            txtLog.Multiline = true; txtLog.ScrollBars = ScrollBars.Both; txtLog.WordWrap = false; txtLog.ReadOnly = true;
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.BackColor = Theme.Surface;
            txtLog.ForeColor = Theme.Ink2;
            txtLog.Font = Theme.Mono;      // 日志是数据：等宽才能让时间戳和级别对齐
            gl.Controls.Add(txtLog);

            // ---- 右栏：MOD 库 ----
            // 两样东西叠在同一块区域里，靠顶部页签切换：方案树 / 任意一个大类的列表。
            // 不另开窗口，勾选、搜索、方案下拉始终在视线里，关窗口把进程留在后台这种事也不会再发生。
            int modH = CH - 56 - 116;                        // 到下方按钮行为止，中间全给 MOD 库
            Section gm = new Section();
            gm.Caption = "MOD 库（U 盘的 vpk + 压缩包）"; gm.SetBounds(rightX, 56, rightW, modH); Controls.Add(gm);

            Label l8 = new Label(); l8.Text = "方案"; l8.SetBounds(12, 34, 34, 20); Theme.Dress(l8); gm.Controls.Add(l8);
            cboProfile = new ComboBox(); cboProfile.SetBounds(50, 30, 204, 24); cboProfile.DropDownStyle = ComboBoxStyle.DropDownList; Theme.Dress(cboProfile); gm.Controls.Add(cboProfile);
            cboProfile.SelectedIndexChanged += delegate { PreviewProfile(); };
            btnSaveProfile = new FlatButton(); btnSaveProfile.Text = "存为方案"; btnSaveProfile.SetBounds(262, 28, 84, Theme.H); btnSaveProfile.Click += delegate { SaveProfile(); }; gm.Controls.Add(btnSaveProfile);
            btnApplyProfile = new FlatButton(); btnApplyProfile.Text = "套用"; btnApplyProfile.SetBounds(354, 28, 58, Theme.H); btnApplyProfile.Click += delegate { ApplyProfile(); }; gm.Controls.Add(btnApplyProfile);
            btnManage = new FlatButton(); btnManage.Text = "管理…"; btnManage.SetBounds(rightW - 12 - 68, 28, 68, Theme.H); btnManage.Click += delegate { ManageProfiles(); }; gm.Controls.Add(btnManage);

            Label l9 = new Label(); l9.Text = "搜"; l9.SetBounds(12, 66, 24, 20); Theme.Dress(l9); gm.Controls.Add(l9);
            txtSearch = new TextBox(); txtSearch.SetBounds(50, 62, rightW - 62, 24); Theme.Dress(txtSearch); txtSearch.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); }; gm.Controls.Add(txtSearch);
            tips.SetToolTip(txtSearch, "按名字或标签过滤。停止输入 250 ms 自动筛，页签和当前这一类的列表一起跟着变。");

            // 方案分布：一行说完这套在各类里放了几个
            lblScheme = new Label();
            lblScheme.SetBounds(12, 92, rightW - 24, 18);
            lblScheme.Font = Theme.UISmall;
            lblScheme.ForeColor = Theme.Ink3;
            lblScheme.AutoEllipsis = true;
            lblScheme.Text = "";
            gm.Controls.Add(lblScheme);

            int cw = rightW - 24;
            int ch = modH - 118 - 74;

            // 方案树就放在这块矩形里
            viewHost = new Panel();
            viewHost.SetBounds(12, 118, cw, ch);
            viewHost.BackColor = Theme.Surface;
            viewHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            viewHost.SizeChanged += delegate
            {
                if (treeView != null)
                    treeView.SetBounds(0, 0, viewHost.ClientSize.Width, viewHost.ClientSize.Height);
            };
            gm.Controls.Add(viewHost);

            // 启动读取提示：扫库没完成前树区是空的，不放这句用户会以为程序坏了
            lblLoading = new Label();
            lblLoading.Dock = DockStyle.Fill;
            lblLoading.BackColor = Theme.Surface;
            lblLoading.Font = Theme.UIBold;
            lblLoading.ForeColor = Theme.Ink2;
            lblLoading.TextAlign = ContentAlignment.MiddleCenter;
            lblLoading.Text = "正在读取 U 盘上的 MOD 库…\r\n第一次启动或刚更新完可能要等几秒，别急着关";
            viewHost.Controls.Add(lblLoading);
            lblLoading.BringToFront();

            lblSel = new Label(); lblSel.SetBounds(12, modH - 64, rightW - 24, 18); lblSel.Text = "已选 0 个"; lblSel.Font = Theme.UISmall; Theme.Dress(lblSel); gm.Controls.Add(lblSel);
            lblConflict = new Label();
            lblConflict.SetBounds(12, modH - 40, rightW - 24, 34);
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
            btnInstall.SetBounds(W - Theme.M - 104 - 8 - 96 - 8 - 116 - 8 - 112, CH - 104, 112, Theme.HP);
            btnInstall.Click += delegate { DoInstall(); }; Controls.Add(btnInstall);
            btnSwitch = new FlatButton(); btnSwitch.Text = "已装 MOD 开关";
            btnSwitch.SetBounds(W - Theme.M - 104 - 8 - 96 - 8 - 116, CH - 104, 116, Theme.HP);
            btnSwitch.Click += delegate { DoSwitch(); }; Controls.Add(btnSwitch);
            btnRestore = new FlatButton(); btnRestore.Text = "一键还原";
            btnRestore.SetBounds(W - Theme.M - 104 - 8 - 96, CH - 104, 96, Theme.HP);
            btnRestore.Click += delegate { DoRestore(); }; Controls.Add(btnRestore);
            btnOpen = new FlatButton(); btnOpen.Text = "打开游戏目录";
            btnOpen.SetBounds(W - Theme.M - 104, CH - 104, 104, Theme.HP);
            btnOpen.Click += delegate { try { System.Diagnostics.Process.Start("explorer.exe", txtGame.Text); } catch { } }; Controls.Add(btnOpen);

            bar = new ProgressBar(); bar.SetBounds(Theme.M, CH - 56, 440, 12); Controls.Add(bar);
            lblStatus = new Label(); lblStatus.SetBounds(Theme.M, CH - 36, W - 2 * Theme.M, 20); lblStatus.Text = "就绪";
            lblStatus.ForeColor = Theme.Ink2; lblStatus.BackColor = Color.Transparent; Controls.Add(lblStatus);

            // 随窗口缩放：左右拉伸的跟着走，底部按钮贴右下
            gMod = gm; gLog = gl; gPlat = gp;
            // 最小尺寸也守着屏幕：网吧小屏上不能出现「窗口比桌面还宽」
            Rectangle wmin = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = new Size(Math.Min(1120, wmin.Width - 20), Math.Min(740, wmin.Height - 20));
            txtGame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cboMode.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnFind.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gp.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            gOpt.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            gl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            gm.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
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
            // 回填配置时别触发「勾相机自动勾平台」这类联动，否则会把存下来的组合改掉
            applying = true;
            try
            {
                chkL4N.Checked = Core.Cfg.L4N;
                chkDxvk.Checked = Core.Cfg.Dxvk;
                chkSkeeto.Checked = Core.Cfg.Skeeto;
                chkAsset.Checked = Core.Cfg.AssetPack;
                chkMqcam.Checked = Core.Cfg.Mqcam;
                chkLaunch.Checked = Core.Cfg.Launch;
            }
            finally { applying = false; }
            txtLaunch.Text = Core.Cfg.LaunchOptions;
            // 第一次发现有参数存档就默认勾上 —— 用户存过参数，摆明了就是下次装机要自动写进去
            bool firstTimeTweak = Core.Cfg.TweakSet.Length == 0 && Tweaks.List().Count > 0;
            chkCfg.Checked = Core.Cfg.CfgFile || firstTimeTweak;
            rdSkip.Checked = !Core.Cfg.Overwrite;
            rdOver.Checked = Core.Cfg.Overwrite;
            modeSync = true;
            try { cboMode.SelectedIndex = Core.IsLocal ? 1 : 0; }
            finally { modeSync = false; }
            FillTweaks(Core.Cfg.TweakSet);
            foreach (string m in Core.Cfg.Mods) checkedKeys.Add(m);
            CheckRowsByKeys();
            UpdateSel();
            Log("已载入上次勾选（" + checkedKeys.Count + " 个 MOD）");
        }

        /// <summary>
        /// 切工作模式：存进配置，再按新模式重扫。
        /// U 盘模式 = U 盘库是主，游戏目录里同名同大小的那份合并显示；本地模式 = 只把游戏 addons 当库。
        /// </summary>
        private void OnModeChanged()
        {
            if (modeSync || cboMode == null || cboMode.SelectedIndex < 0) return;
            string want = cboMode.SelectedIndex == 1 ? "local" : "usb";
            if (string.Equals(want, Core.Cfg.Mode, StringComparison.OrdinalIgnoreCase)) return;
            Core.Cfg.Mode = want;
            Core.SaveConfig();
            Core.Log("OK", "工作模式：" + (want == "local" ? "本地模式（游戏目录 addons 当库）" : "U 盘模式（U 盘库是主，游戏目录重复项合并）"));
            lblStatus.Text = want == "local" ? "正在按「本地模式」重扫游戏目录…" : "正在按「U 盘模式」重扫库…";
            btnInstall.Enabled = false;
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                try { Core.ScanMods(); }
                catch (Exception ex) { Core.Log("FAIL", "切换模式后重扫失败: " + ex.Message); }
                SafeInvoke(delegate()
                {
                    btnInstall.Enabled = true;
                    RefreshMods();
                    RefreshConflicts();
                    if (treeView != null) treeView.Rebuild();
                    lblStatus.Text = "就绪";
                });
            });
            th.IsBackground = true;
            th.Start();
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

        private void FillProfiles()
        {
            cboProfile.Items.Clear();
            List<string> ps = ProfileStore.ListProfiles();
            if (ps.Count == 0) { cboProfile.Items.Add("（还没有方案）"); cboProfile.SelectedIndex = 0; LoadSchemeKeys(); SyncPicks(); return; }
            foreach (string p in ps) cboProfile.Items.Add(p);
            cboProfile.SelectedIndex = 0;
            // SelectedIndex 变了会走 PreviewProfile 把方案灌进来；这里再兜一次，防止方案文件被改过
            LoadSchemeKeys();
            RefreshMods();
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

        /// <summary>自测用：把搜索框设成某个词，用来验证筛选与空态。</summary>
        internal void UiTestFilter(string kw)
        {
            txtSearch.Text = kw;
            RefreshMods();
        }

        /// <summary>自测用：把「方案管理」窗口画成图片，核对分组和列宽。</summary>
        internal void UiTestProfileForm()
        {
            using (ProfileManageForm f = new ProfileManageForm(this))
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, -4000);
                f.Show();
                Application.DoEvents();
                f.PerformLayout();
                Application.DoEvents();
                Size full = f.Size;
                using (Bitmap bmp = new Bitmap(full.Width, full.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, full.Width, full.Height));
                    bmp.Save(System.IO.Path.Combine(Core.LogDir, "uitest_profile.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                Core.Log("INFO", "PROFILE 方案数=" + ProfileStore.ListProfiles().Count + " 列表行=" + f.DebugRows());
            }
        }

        /// <summary>自测用：L4N 与 MQCam 的勾选联动（只有相机依赖平台，其他都不依赖）。</summary>
        internal void UiTestDeps()
        {
            chkMqcam.Checked = false; chkL4N.Checked = false;
            Core.Log("INFO", "DEPS 起点：L4N=" + chkL4N.Checked + " MQCam=" + chkMqcam.Checked);
            chkMqcam.Checked = true;
            Core.Log("INFO", "DEPS 勾 MQCam 后：L4N=" + chkL4N.Checked + "（应为 True）MQCam=" + chkMqcam.Checked);
            chkL4N.Checked = false;
            Core.Log("INFO", "DEPS 取消 L4N 后：MQCam=" + chkMqcam.Checked + "（应为 False）");
            chkL4N.Checked = false; chkMqcam.Checked = false;
            chkSkeeto.Checked = true; chkDxvk.Checked = true;
            Core.Log("INFO", "DEPS 只装 Skeeto/dxvk、不装 L4N：L4N=" + chkL4N.Checked + " MQCam=" + chkMqcam.Checked + "（应为 False/False，L4N 不该被逼着勾上）");
        }

        /// <summary>自测用：全选当前可见项并跑深度冲突检查，结果写日志。</summary>
        internal void UiTestDeep()
        {
            // 全库全选（默认部件），再跑深度冲突检查
            foreach (ModGroup g in Catalog.Groups)
                foreach (ModItem p in Catalog.DefaultParts(g)) checkedKeys.Add(p.Key);
            SyncPicks();
            List<ModItem> sel = Selected();
            Core.Log("INFO", "DEEP 选中 " + sel.Count + " 个，开始深度检查");
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            List<string> msgs = ConflictCheck.DeepAll(sel);
            foreach (string m in msgs) Core.Log("INFO", "DEEP " + m);
            Core.Log("INFO", "DEEP 用时 " + sw.ElapsedMilliseconds + " ms，共 " + msgs.Count + " 条");
        }

        internal string SearchText { get { return txtSearch == null ? "" : txtSearch.Text.Trim(); } }

        internal void ClearSearchIfAny()
        {
            if (txtSearch != null && txtSearch.Text.Length > 0) txtSearch.Text = "";   // 会触发一次重新筛选
        }

        /// <summary>行首的展开标记：套装才给三角，单 vpk 的行前面留空。</summary>
        internal string GroupRowText(ModGroup g, int sel)
        {
            if (!g.IsSet) return g.Name;
            return (expanded.Contains(g.Key) ? "▼ " : "▶ ") + g.Name + "（" + (sel > 0 ? sel : 0) + "/" + g.Parts.Count + "）";
        }

        internal string GroupRowText(ModGroup g) { return GroupRowText(g, CountChecked(g)); }

        internal bool IsExpanded(ModGroup g) { return g != null && expanded.Contains(g.Key); }

        /// <summary>双击套装行：就地展开/收起子件，不弹窗、不重建整张表。</summary>
        internal void ToggleExpand(ModGroup g)
        {
            if (g == null || !g.IsSet) return;
            if (!expanded.Remove(g.Key)) expanded.Add(g.Key);
        }

        /// <summary>把一条套装渲染成一行：类窗口和方案窗口共用同一套配色规则。</summary>
        internal ListViewItem MakeRow(ModGroup g)
        {
            int sel = CountChecked(g);
            ListViewItem li = new ListViewItem(GroupRowText(g, sel));
            Color mk;
            li.SubItems.Add(SchemeMark(g, out mk));           // 方案：方案内 / 已取消 / 新增
            li.SubItems[1].ForeColor = mk;
            li.SubItems[1].Font = Theme.UISmall;
            li.SubItems.Add(g.Subject.Length > 0 ? g.Subject : "—");
            if (g.Subject.Length > 0 && g.SubjectLevel == "材质") li.SubItems[2].ForeColor = Theme.Ink3;
            li.SubItems.Add(Core.Human(g.Size));
            li.SubItems.Add(WhereText(g));
            li.SubItems[4].ForeColor = CatInk(g.Cat);
            Color stInk, stBg;
            li.SubItems.Add(StatusOf(g, out stInk, out stBg));      // 状态：已装·开 / 已装·关 / 未装 …
            li.SubItems[5].ForeColor = stInk;
            li.SubItems[5].Font = Theme.UISmall;
            if (stBg != Theme.Surface) li.BackColor = stBg;
            li.Tag = g;
            li.Checked = sel > 0;
            if (g.IsSet) li.ToolTipText = PartsTip(g);
            foreach (ModItem p in g.Parts)
                if (p.Installed)
                {
                    li.ToolTipText = (li.ToolTipText.Length > 0 ? li.ToolTipText + "\r\n" : "")
                        + "游戏 addons 里已经有同名同大小的这一份，安装时会直接跳过复制。\r\n"
                        + "addonlist.txt 里现在是「" + (p.AddonOn ? "1 · 开" : "0 · 关") + "」"
                        + (p.AddonOn ? "，进游戏就能看到。" : "，文件在但没启用 —— 想开就在「已装 MOD 开关」里打开，或勾上它再装一次。");
                    break;
                }
            return li;
        }

        /// <summary>展开后挂在套装下面的一个 vpk：勾这行只装这一个文件。</summary>
        internal ListViewItem MakePartRow(ModGroup g, ModItem p)
        {
            ListViewItem li = new ListViewItem("      └ " + p.Name);
            li.SubItems.Add("");
            li.SubItems.Add(p.Subject.Length > 0 ? p.Subject : "—");
            li.SubItems.Add(Core.Human(p.Size));
            li.SubItems.Add(p.Role + " · 单件");
            li.SubItems[4].ForeColor = Theme.Ink3;
            Color pInk, pBg;
            li.SubItems.Add(PartStatus(p, out pInk, out pBg));
            li.SubItems[5].ForeColor = pInk;
            li.SubItems[5].Font = Theme.UISmall;
            if (pBg != Theme.Surface) li.BackColor = pBg;
            li.Tag = p;
            li.ForeColor = Theme.Ink3;
            li.Checked = checkedKeys.Contains(p.Key);
            li.ToolTipText = "这一个 vpk：" + p.Name + "\r\n勾上 = 只装它（" + p.Role + "）"
                           + "\r\n现在：" + PartStatus(p, out pInk, out pBg);
            return li;
        }

        /// <summary>任何窗口里勾了一行，都走这里：先改 checkedKeys，再把所有窗口同步一遍。</summary>
        internal void OnRowChecked(ListView lv, ItemCheckedEventArgs e)
        {
            if (refreshing) return;

            // 展开出来的单件行：勾哪个就装哪个，不动同套装的其他 vpk
            ModItem part = e.Item.Tag as ModItem;
            if (part != null)
            {
                // 刷新列表时 ListView 会把勾选状态再补发一遍；状态本来就一致就放过，否则会死循环
                if (e.Item.Checked == checkedKeys.Contains(part.Key)) return;
                if (e.Item.Checked) checkedKeys.Add(part.Key);
                else checkedKeys.Remove(part.Key);

                if (bulk) return;
                // 组行的「已选 2/5」和子行勾状态统一由 SyncPicks → 树的 Sync 更新
                SyncPicks();
                UpdateSchemeLabel();
                UpdateSel();
                return;
            }

            ModGroup g = e.Item.Tag as ModGroup;
            if (g == null) return;
            // 刷新列表时 ListView 会把每一行的勾选状态再补发一遍消息；那时 checkedKeys 本来就是对的，
            // 认了就变成「同步一次 → 又补发 → 再同步」的死循环，所以状态一致就直接放过
            if (e.Item.Checked == (CountChecked(g) > 0)) return;
            if (e.Item.Checked)
            {
                // 勾这一行 = 装默认部件（主体 + 必装 + 第一个选配）；想挑就双击展开
                foreach (ModItem p in Catalog.DefaultParts(g)) checkedKeys.Add(p.Key);
            }
            else
            {
                foreach (ModItem p in g.Parts) checkedKeys.Remove(p.Key);
            }
            if (g.IsSet) e.Item.Text = GroupRowText(g);
            Color c;
            e.Item.SubItems[1].Text = SchemeMark(g, out c);
            e.Item.SubItems[1].ForeColor = c;
            if (bulk) return;
            SyncPicks();
            UpdateSchemeLabel();
            UpdateSel();
        }

        /// 方案树右边那张候选表用的菜单：只留对「一个位置挑一个 MOD」有用的事。
        /// 导入、清重复、展开这些是分类页的活，不往这儿塞。
        /// </summary>
        internal ContextMenuStrip BuildTreeMenu(ListView lv)
        {
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.Font = Theme.UI;
            cms.Items.Add("用这个替换（点这一行也一样）", null, delegate { PickInTree(lv); });
            cms.Items.Add("选择部件…（模型+材质+颜色的组合装）", null, delegate { PickPartsIn(lv); });
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add("只装这个 MOD（不动平台/启动项）", null, delegate { InstallThisOne(lv); });
            cms.Items.Add("开 / 关这个 MOD（只改 addonlist 的 1/0，不动文件）", null, delegate { ToggleSwitchIn(lv); });
            cms.Items.Add("放进我的分支…（认不出改什么的，自己归一组）", null, delegate { PutIntoBranchIn(lv); });
            cms.Items.Add("从这个分支移出（文件不动）", null, delegate { TakeOutOfBranchIn(lv); });
            cms.Items.Add("重命名这个 VPK 文件名", null, delegate { RenameSelectedIn(lv); });
            cms.Items.Add("删除这个 MOD → 回收目录（可反悔）", null, delegate { DeleteInTree(lv); });
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add("设置 MOD 分类…（算哪一类 / 替换什么）", null, delegate { SetModCatIn(lv); });
            cms.Items.Add("在资源管理器里定位", null, delegate { RevealIn(lv); });
            return cms;
        }

        /// <summary>右键「放进我的分支」：把这个 MOD 归到用户自己拉的分组里（不动文件）。</summary>
        internal void PutIntoBranchIn(ListView lv)
        {
            ListViewItem li = RowTarget(lv);
            ModGroup g = li == null ? null : li.Tag as ModGroup;
            if (g == null) { MessageBox.Show("先右键点一行 MOD。"); return; }
            if (treeView == null) return;
            treeView.PutIntoBranch(g);
        }

        /// <summary>右键「从这个分支移出」：只把这一条从分组里摘掉，文件和勾选都不动。</summary>
        internal void TakeOutOfBranchIn(ListView lv)
        {
            ListViewItem li = RowTarget(lv);
            ModGroup g = li == null ? null : li.Tag as ModGroup;
            if (g == null) { MessageBox.Show("先右键点一行 MOD。"); return; }
            if (treeView == null) return;
            treeView.TakeOutOfBranch(g);
        }

        /// <summary>
        /// 右键菜单的「开 / 关这个 MOD」：和点列表最右边「开关 ⇄」列是同一个动作
        /// （攒进 pending，最后统一按「应用改动」写 addonlist；文件一个都不动）。
        /// 开关列那一小格不好点中，右键这条是给鼠标手抖的人留的第二条路。
        /// </summary>
        internal void ToggleSwitchIn(ListView lv)
        {
            ListViewItem li = RowTarget(lv);
            if (li == null) { MessageBox.Show("先右键点一行 MOD。"); return; }
            ModGroup g = li.Tag as ModGroup;
            if (g == null) { MessageBox.Show("这一行不是 MOD（标题行或「留原版」没有开关）。"); return; }
            if (treeView == null) return;
            treeView.ToggleFromMenu(g);
        }

        /// <summary>
        /// 方案树候选表里删一个 MOD：文件移进回收目录（可反悔），删完重扫，树留在原来那个位置。
        /// 套装行删整套；展开出来的单件行只删那一个文件。
        /// </summary>
        internal void DeleteInTree(ListView lv)
        {
            ListViewItem li = RowTarget(lv);
            if (li == null) { MessageBox.Show("先右键点一行。"); return; }
            ModGroup g = li.Tag as ModGroup;
            ModItem one = li.Tag as ModItem;
            string name;
            List<ModItem> parts = new List<ModItem>();
            if (g != null) { name = g.Name; parts.AddRange(g.Parts); }
            else if (one != null) { name = one.Name; parts.Add(one); }
            else { MessageBox.Show("这一行没有对应的 vpk。"); return; }

            foreach (ModItem it in parts)
            {
                if (it.IsArchive) { MessageBox.Show("「" + it.Name + "」在压缩包里（" + it.Container + "），\r\n装机台不会去改压缩包。想删请自己在资源管理器里处理。", "不能删除"); return; }
                if (it.FromGame && !Core.IsLocal) { MessageBox.Show("「" + it.Name + "」在游戏目录 addons 里。\r\nU 盘模式不动游戏目录的文件；要删它，把右上角切到「本地模式」再来。", "不能删除"); return; }
            }
            string where = Core.IsLocal ? "回收目录（" + Core.RecycleDir + "）" : "U 盘 _回收的MOD";
            string tail = parts.Count > 1 ? "（整套 " + parts.Count + " 个 vpk）" : "";
            bool fromGame = parts.Exists(delegate(ModItem m) { return m.FromGame; });
            if (MessageBox.Show("删除「" + name + "」" + tail + "？\r\n\r\n· 文件移到" + where + "\r\n· 不真删，想用可以再拖回来"
                + (fromGame ? "\r\n· 这是游戏 addons 里的文件，移走 = 从游戏里卸掉" : ""),
                "确认删除", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

            int n = 0;
            foreach (ModItem it in parts)
            {
                try { if (Work.MoveModToRecycle(it) != null) { n++; checkedKeys.Remove(it.Key); } }
                catch (Exception ex) { Core.Log("WARN", "移动 " + it.Name + " 失败: " + ex.Message); }
            }
            Core.Log("OK", "方案树里删掉 " + n + " 个 vpk：" + name + "（" + where + "）");
            Core.ScanMods();
            RefreshMods();
            RefreshConflicts();
            if (treeView != null) { treeView.Rebuild(); treeView.Sync(); }
        }

        /// <summary>方案树里的「用这个替换」：交给树自己去换，位置和勾选都按树的规则走。</summary>
        private void PickInTree(ListView lv)        {
            ListViewItem li = RowTarget(lv);
            if (li == null) { MessageBox.Show("先右键点一行。"); return; }
            if (treeView == null) return;
            treeView.PickRow(li);
        }

        /// <summary>设置这个 MOD 算哪一类、替换什么。分类只有一份：页签 / 方案树一起跟着变。</summary>
        internal void SetModCatIn(ListView lv)
        {
            ModGroup g = FirstSelectedGroup(lv);
            if (g == null) { MessageBox.Show("先选中一个 MOD。"); return; }
            using (ModCatForm f = new ModCatForm(g))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                    Core.Log("OK", "分类已保存: " + g.Name + " → " + f.ResultCat
                        + (f.ResultSlot.Length > 0 ? " / " + f.ResultSlot : ""));
            }
            // 窗口里还可能新建/改名/排序过分类，取消也要同步一次
            Catalog.ApplyCats();          // 人工分类落到 ModGroup.Cat 上：页签、方案树、搜索全跟着走
            if (treeView != null) { treeView.Rebuild(); treeView.Sync(); }
            RefreshMods();
        }

        /// <summary>右键点中的那一行。右键时已经把它选中了，所以取第一行选中项。</summary>
        private ListViewItem RowTarget(ListView lv)
        {
            return lv.SelectedItems.Count > 0 ? lv.SelectedItems[0] : null;
        }

        /// <summary>
        /// 这一行到底是哪几个 vpk。
        /// 展开出来的子件行 = 那一个文件；套装行 = 这套里当前勾着的件，一件都没勾就按默认部件（主体+必装+第一个选配）。
        /// </summary>
        private List<ModItem> RowItems(ListView lv)
        {
            List<ModItem> list = new List<ModItem>();
            ListViewItem li = RowTarget(lv);
            if (li == null) return list;
            ModItem single = li.Tag as ModItem;
            if (single != null) { list.Add(single); return list; }
            ModGroup g = li.Tag as ModGroup;
            if (g == null) return list;
            foreach (ModItem p in g.Parts) if (checkedKeys.Contains(p.Key)) list.Add(p);
            if (list.Count == 0) list.AddRange(Catalog.DefaultParts(g));
            return list;
        }

        /// <summary>只装右键点的那一行，别的什么都不动。</summary>
        internal void InstallThisOne(ListView lv)
        {
            ListViewItem li = RowTarget(lv);
            if (li == null) { MessageBox.Show("先在列表里右键点一行。"); return; }
            List<ModItem> sel = RowItems(lv);
            if (sel.Count == 0) { MessageBox.Show("这一行没有对应的 vpk。"); return; }
            string what = sel.Count == 1
                ? sel[0].Name
                : "「" + ((li.Tag as ModGroup) != null ? ((ModGroup)li.Tag).Name : li.Text) + "」的 " + sel.Count + " 个 vpk";
            InstallMods(sel, "只装这个 MOD", what);
        }

        internal void ManageProfiles()
        {
            using (ProfileManageForm f = new ProfileManageForm(this)) f.ShowDialog(this);
            FillProfiles();
            RefreshMods();
        }

        /// <summary>搜索或方案变了：树和标题跟着重排。</summary>
        private void RefreshMods()
        {
            UpdateSchemeLabel();
            // 方案树是常驻第一屏：万一扫库还没完就点进来了，树会是空的，扫完这里补一次；
            // 树有内容时只刷新状态点，不重建（搜索打字不至于反复重排）
            if (treeView != null)
            {
                if (treeView.SlotCount == 0) treeView.Rebuild();
                else if (treeView.Visible) treeView.SyncIfSame();   // 正在看树：重扫后状态列、圆点要立刻对上（换了新对象就整棵重建）
                treeView.RefreshPane();                             // 搜索词变了，右边（结果 / 候选）跟着换
            }
            UpdateSel();
            UpdateModCaption();
        }

        /// <summary>手动重扫：库或游戏目录在别处被改过（别人手动丢过 vpk、装了一半），点一下重读。</summary>
        internal void RescanMods()
        {
            try
            {
                SetBusy(true);
                SetStatus("正在重扫…");
                Core.ScanMods();
                RefreshMods();
                Core.Log("OK", "已重扫：共 " + Core.ModList.Count + " 个 vpk");
            }
            catch (Exception ex) { Core.Log("FAIL", "重扫失败: " + ex.Message); }
            finally { SetBusy(false); SetStatus("就绪"); }
        }

        // 一行说清这套方案在各类里放了几个
        private void UpdateSchemeLabel()
        {
            if (lblScheme == null) return;
            if (schemeName.Length == 0)
            {
                lblScheme.ForeColor = Theme.Ink3;
                lblScheme.Text = "没选方案：挑一套，列表里就会标出它在这一类里装了什么";
                return;
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("「").Append(schemeName).Append("」");
            int mods = 0;
            foreach (string c in Catalog.Cats)
            {
                int n = 0;
                foreach (ModGroup g in Catalog.Groups) if (g.Cat == c && InScheme(g)) n++;
                if (n == 0) continue;
                mods += n;
                sb.Append("  ").Append(c).Append(" ").Append(n);
            }
            sb.Append("　共 ").Append(mods).Append(" 个 / ").Append(schemeKeys.Count).Append(" 文件");
            int add = 0, del = 0;
            foreach (string k in checkedKeys) if (!schemeKeys.Contains(k)) add++;
            foreach (string k in schemeKeys) if (!checkedKeys.Contains(k)) del++;
            if (add > 0 || del > 0) sb.Append("　·　勾选差 多 ").Append(add).Append(" / 少 ").Append(del);
            lblScheme.ForeColor = add == 0 && del == 0 ? Theme.Ink2 : Theme.Warn;
            lblScheme.Text = sb.ToString();
        }

        // 大类各一种颜色。只用来区分，不做装饰：同色系深浅差别够看就行。
        internal static Color CatInk(string cat)
        {
            switch (cat)
            {
                case Catalog.CAT_MAP: return Color.FromArgb(24, 102, 133);
                case Catalog.CAT_CHAR: return Color.FromArgb(107, 71, 148);
                case Catalog.CAT_GUN: return Color.FromArgb(150, 88, 24);
                case Catalog.CAT_MED: return Color.FromArgb(38, 116, 55);
                case Catalog.CAT_SPRAY: return Color.FromArgb(160, 54, 116);
                default: return Theme.Ink2;
            }
        }

        /// <summary>这条在不在当前方案里（套装里任意一个 vpk 在就算在）。</summary>
        internal bool InScheme(ModGroup g)
        {
            if (schemeKeys.Count == 0) return false;
            foreach (ModItem p in g.Parts) if (schemeKeys.Contains(p.Key)) return true;
            return false;
        }

        // 方案列：一句话说清「这条跟当前方案是什么关系」
        private string SchemeMark(ModGroup g, out Color color)
        {
            bool inn = InScheme(g);
            bool on = CountChecked(g) > 0;
            if (inn && on) { color = Theme.Ok; return "方案内"; }    // 方案里有，这次也勾着
            if (inn && !on) { color = Theme.Warn; return "已取消"; }  // 方案里有，这次被取消了
            color = Theme.Accent;
            return on ? "新增" : "";                                  // 方案里没有，这次新勾的
        }

        // 把当前方案读进 schemeKeys（下拉换了 / 方案存过之后都要刷一次）
        private void LoadSchemeKeys()
        {
            schemeKeys.Clear();
            schemeName = "";
            if (cboProfile == null || cboProfile.SelectedItem == null) return;
            string n = cboProfile.SelectedItem.ToString();
            if (n.StartsWith("（")) return;
            Profile p = ProfileStore.Load(n);
            if (p == null) return;
            schemeName = p.Name;
            foreach (string k in p.Mods) schemeKeys.Add(k);
        }

        // 分类列只放得下 130 px：人物的「二代 · 西装Nick」取最后一段，省「人物 · 二代」那截
        // 装没装已经单独有一列（状态），这里不再拼「· 已装」，免得一格里塞两件事
        private static string WhereText(ModGroup g)
        {
            return SubText(g);
        }

        // ---------- 状态：两维 ----------
        // 第一维「文件在不在 addons 里」（Installed / FromGame），第二维「addonlist 里是 1 还是 0」（AddonOn）。
        // 只看第一维会把「装了但被关掉」和「装好了正开着」说成一样，机房里这两种要区别对待。

        /// <summary>这条现在在游戏里是什么状态：一共几个、装了几个、开着几个、几个本来就是游戏里的。</summary>
        internal static void CountState(ModGroup g, out int total, out int inst, out int on, out int game)
        {
            total = g.Parts.Count; inst = 0; on = 0; game = 0;
            foreach (ModItem p in g.Parts)
            {
                if (p.FromGame) game++;
                if (p.FromGame || p.Installed) { inst++; if (p.AddonOn) on++; }
            }
        }

        /// <summary>一行要显示的状态文字、字色、整行底色。</summary>
        internal static string StatusOf(ModGroup g, out Color ink, out Color bg)
        {
            int total, inst, on, game;
            CountState(g, out total, out inst, out on, out game);
            ink = Theme.Ink3; bg = Theme.Surface;
            if (inst == 0) return "未装";
            if (game == total)                       // 全都在游戏目录里、库里没有
            {
                ink = Theme.Ink2; bg = Theme.RowGame;
                return on > 0 ? "仅游戏内 · 开" : "仅游戏内 · 关";
            }
            if (inst < total)
            {
                ink = Theme.Info; bg = Theme.RowGame;
                return "部分已装 " + inst + "/" + total;
            }
            if (on == inst) { ink = Theme.Ok; bg = Theme.RowOn; return "已装 · 开"; }
            if (on == 0) { ink = Theme.Warn; bg = Theme.RowOff; return "已装 · 关"; }
            ink = Theme.Warn; bg = Theme.RowOff;
            return "已装 · 开 " + on + "/" + inst;
        }

        /// <summary>单个 vpk 的状态（套装展开后的子行用）。</summary>
        internal static string PartStatus(ModItem p, out Color ink, out Color bg)
        {
            bg = Theme.Surface; ink = Theme.Ink3;
            if (p.FromGame)
            {
                ink = Theme.Ink2; bg = Theme.RowGame;
                return p.AddonOn ? "游戏内 · 开" : "游戏内 · 关";
            }
            if (p.Installed)
            {
                ink = p.AddonOn ? Theme.Ok : Theme.Warn;
                bg = p.AddonOn ? Theme.RowOn : Theme.RowOff;
                return p.AddonOn ? "已装 · 开" : "已装 · 关";
            }
            return "未装";
        }

        /// <summary>页头「显示」下拉的过滤：0 全部 / 1 已装 / 2 未装 / 3 已装但关闭。</summary>
        internal static bool MatchShow(ModGroup g, int show)
        {
            if (show <= 0) return true;
            int total, inst, on, game;
            CountState(g, out total, out inst, out on, out game);
            if (show == 1) return inst > 0;
            if (show == 2) return inst == 0;
            if (show == 3) return inst > 0 && on == 0;
            return true;
        }

        // 分组标题已经写明大类了，这一列只留细分，别每行重复一遍「地图 ·」
        internal static string SubOf(ModItem it)
        {
            if (it.Cat == it.Sub) return "—";
            string sub = it.Sub == null ? "" : it.Sub;
            int i = sub.LastIndexOf(" · ", StringComparison.Ordinal);
            if (i >= 0) sub = sub.Substring(i + 3);
            return sub.Length == 0 ? "—" : sub;
        }

        private static string SubText(ModGroup g)
        {
            if (g.Cat == g.Sub) return "—";
            string sub = g.Sub;
            int i = sub.LastIndexOf(" · ", StringComparison.Ordinal);
            if (i >= 0) sub = sub.Substring(i + 3);
            return sub;
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
            sb.Append("这套由 ").Append(g.Parts.Count).Append(" 个 vpk 组成（双击这一行展开，可以只勾其中一个）：");
            foreach (ModItem p in g.Parts) sb.Append("\r\n· [").Append(p.Role).Append("] ").Append(p.Name);
            return sb.ToString();
        }

        internal int CountChecked(ModGroup g)
        {
            int n = 0;
            foreach (ModItem p in g.Parts) if (checkedKeys.Contains(p.Key)) n++;
            return n;
        }

        /// <summary>整个套装下架：把它的每个 vpk 都从勾选里去掉。</summary>
        internal void UncheckGroup(ModGroup g)
        {
            if (g == null) return;
            foreach (ModItem p in g.Parts) checkedKeys.Remove(p.Key);
        }

        /// <summary>整个套装上架：按默认部件勾（主体 + 必装 + 第一个选配）。</summary>
        internal void CheckGroupDefault(ModGroup g)
        {
            if (g == null) return;
            foreach (ModItem p in Catalog.DefaultParts(g)) checkedKeys.Add(p.Key);
        }

        /// <summary>在方案树里点了一个候选：方案行、底部计数一起对齐。</summary>
        internal void AfterTreePick()
        {
            UpdateSchemeLabel();
            UpdateSel();
            UpdateModCaption();
        }

        // 展开出来的单件行要照这个判断自己该不该打勾
        internal bool IsChecked(string key) { return key != null && checkedKeys.Contains(key); }

        /// <summary>搜名字、搜「替换了什么」、搜大类/位置 —— 分类就是筛选词，不再另有一套标签。</summary>
        private bool MatchGroup(ModGroup g, string kw)
        {
            if (g.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (g.Subject.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (g.Where.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string slot = SlotIndex.SlotNameOf(g);
            if (slot.Length > 0 && slot.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (ModItem p in g.Parts)
                if (p.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        internal void SetAllIn(ListView lv, bool on)
        {
            lv.BeginUpdate();
            bulk = true;
            try
            {
                foreach (ListViewItem li in lv.Items) li.Checked = on;   // 勾选事件里统一处理部件
            }
            finally { bulk = false; }
            lv.EndUpdate();
            SyncPicks();
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
            UpdateSchemeLabel();
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

        /// <summary>在方案树里选中这个 MOD 所在的格子（没有格子就落到「未归类」）。</summary>
        private void RevealModRow(ModItem it)
        {
            ModGroup g;
            if (!Catalog.GroupByKey.TryGetValue(it.GroupKey, out g)) { Core.Log("WARN", "定位不到这一行：" + it.Name); return; }
            ShowTree();
            if (treeView != null) treeView.RevealGroup(g);
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

        private ModGroup FirstSelectedGroup(ListView lv)
        {
            if (lv.SelectedItems.Count == 0) return null;
            return lv.SelectedItems[0].Tag as ModGroup;
        }

        // 双击一行：套装 → 就地展开看每个 vpk；单件行 → 直接切勾选
        internal void OpenRowAt(ListView lv)
        {
            if (lv.SelectedItems.Count == 0) return;
            ListViewItem li = lv.SelectedItems[0];
            ModItem p = li.Tag as ModItem;
            if (p != null) { li.Checked = !li.Checked; return; }
            ModGroup g = li.Tag as ModGroup;
            if (g == null) return;
            if (g.IsSet) { ToggleExpand(g); return; }
            ShowModInfo(g);
        }

        internal void PickPartsIn(ListView lv)
        {
            ModGroup g = FirstSelectedGroup(lv);
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
            if (treeView != null && treeView.SlotCount > 0) treeView.Sync();   // 树上的状态点、右侧明细一起对齐
        }

        /// <summary>改名作用在右键点的那一行上，不再看勾选；一次只改一个 vpk。</summary>
        internal void RenameSelectedIn(ListView lv)
        {
            ListViewItem li = RowTarget(lv);
            if (li == null) { MessageBox.Show("先在列表里右键点一行。"); return; }

            ModItem it = li.Tag as ModItem;          // 展开出来的子件行：就是那一个文件
            if (it == null)
            {
                ModGroup g = li.Tag as ModGroup;
                if (g == null) return;
                if (g.IsSet && g.Parts.Count > 1)
                {
                    MessageBox.Show("「" + g.Name + "」这套由 " + g.Parts.Count + " 个 vpk 组成，一次只能改一个。\r\n"
                        + "先双击这一行展开，再对里面具体那一件右键改名。", "重命名 VPK 文件名");
                    return;
                }
                it = g.Main;                          // 只有一个 vpk 的套装，直接改它
            }
            if (it == null) { MessageBox.Show("这一行没有对应的 vpk。"); return; }
            if (it.IsArchive) { MessageBox.Show("压缩包里的 vpk 不能改名。\r\n要用改名后的版本，请先解压出来。", "不能重命名"); return; }
            if (it.FromGame) { MessageBox.Show("这个 MOD 在游戏目录 addons 里，改名会破坏游戏识别。\r\n想改请先把它挪进 MOD 库。", "不能重命名"); return; }

            string nn = PromptForm.Ask("新名字（不用写 .vpk）", "重命名 " + it.Name);
            if (string.IsNullOrEmpty(nn)) return;
            string err = Work.RenameMod(it, nn.Trim());
            if (err != null) { MessageBox.Show(err); return; }
            Core.Log("OK", "已改名: " + it.Name + " -> " + nn.Trim() + ".vpk");
            Core.ScanMods(); RefreshMods();
        }

        internal void CleanDupMods()
        {
            if (MessageBox.Show("扫描全库同名文件，每个名字只留第一个，其余移到 _回收的MOD？", "清理重复", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            List<string> moved = Work.CleanDuplicateMods();
            Core.Log("OK", "清理重复: 移走 " + moved.Count + " 个");
            MessageBox.Show(moved.Count == 0 ? "没有同名重复。" : ("已移走 " + moved.Count + " 个重复文件，详见日志。"));
            Core.ScanMods(); RefreshMods(); RefreshConflicts();
        }

        internal void RevealIn(ListView lv)
        {
            ModGroup g = FirstSelectedGroup(lv);
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

        /// <summary>
        /// 真正的「只装这几个 vpk」：复制文件 + 写 addonlist，平台/启动项/参数一概不碰。
        /// what 用来在提示里说清楚这次装的是哪一份。
        /// </summary>
        private void InstallMods(List<ModItem> sel, string title, string what)
        {
            string root = txtGame == null ? "" : txtGame.Text.Trim();
            if (root.Length == 0 || !Core.TestGame(root))
            {
                MessageBox.Show("先选对游戏目录（有 left4dead2.exe 的那个）。");
                return;
            }
            if (sel == null || sel.Count == 0)
            {
                MessageBox.Show("这次没有要装的 vpk。");
                return;
            }
            // 压缩包里的 vpk 也能装：CopyMods 会单独抽那一个出来，不解整包、不动 U 盘上的包
            Work.Overwrite = !rdSkip.Checked;
            int copied = 0, skipped = 0;
            List<string> added = null, failed = null;
            try
            {
                RunBusy<int>("正在装 " + what + "…", delegate
                {
                    Work.CopyMods(root, sel, null, out copied, out skipped, out added, out failed);
                    return 0;
                });
            }
            catch (Exception ex)
            {
                Core.Log("FAIL", title + " 出错: " + ex.Message);
                MessageBox.Show("装失败了：" + ex.Message + "\r\n详细信息看下面的日志。", title);
                return;
            }
            if (failed != null && failed.Count > 0)
            {
                Core.Log("FAIL", title + "：失败 " + failed.Count + " 个（" + string.Join("、", failed.ToArray()) + "）");
                MessageBox.Show("有 " + failed.Count + " 个没装进去，多半是游戏正开着（文件被占用）或盘满了。\r\n看下面日志的 FAIL 行。", "没装全", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Dictionary<string, bool> st = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (ModItem it in sel) st[it.Name] = true;
            AddonList.Write(root, st);
            // 状态列是从 addons + addonlist 现读的，装完得重扫一次，否则列表还显示旧状态
            try { Core.ScanMods(); } catch (Exception exs) { Core.Log("WARN", "装完重扫失败: " + exs.Message); }
            RefreshMods();
            if (copied == 0)
            {
                // 全都已经在游戏目录里：这条路上一个文件都不用动，只改开关
                Core.Log("OK", title + "：全部已在游戏目录里，只把开关写成「开」，没有复制文件");
                MessageBox.Show("这些 MOD 本来就在游戏目录里，这次没有复制、移动任何文件。\r\n\r\n"
                    + "只把 addonlist 里它们的开关写成「开」（" + skipped + " 个），进游戏就生效。\r\n"
                    + "其它 MOD 的开关状态没动，平台、启动项、参数也没动。", title);
                return;
            }
            Core.Log("OK", title + "：写入 " + copied + " 个、跳过 " + skipped + " 个（已在游戏里的只切开关，平台/启动项/参数都没动）");
            MessageBox.Show("装好了：" + what + "，共 " + copied + " 个文件"
                + (skipped > 0 ? "（另有 " + skipped + " 个已在游戏里，只切了开关）" : "") + "。\r\n\r\n"
                + "已经写进 addonlist，进游戏就生效。\r\n"
                + "其它 MOD 的开关状态没动，平台、启动项、参数也没动。", title);
        }

        // 压缩包内容识别：第一次把每个 rar 里的 vpk 目录表读一遍，结果落缓存
        internal void ProbeArchivesUi()
        {
            if (MessageBox.Show("会在后台把库里每个压缩包打开看一眼（只读前几百 KB，不解压整包），用来按内容判类型。\r\n\r\n第一次可能要几十秒，之后走缓存不再重复。开始吗？",
                "识别压缩包内容", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            int done = 0;
            try
            {
                RunBusy<int>("正在识别压缩包内容…", delegate
                {
                    done = Catalog.ProbeArchives(delegate(string s) { SetStatus(s); });
                    return done;
                });
            }
            catch (Exception ex)
            {
                Core.Log("FAIL", "识别压缩包出错: " + ex.Message);
                MessageBox.Show("出错了：" + ex.Message, "识别压缩包");
                return;
            }
            Core.ScanMods();
            RefreshMods();
            RefreshConflicts(true);
            Core.Log("OK", "压缩包内容识别完成：新增 " + done + " 条");
            MessageBox.Show("识别完成：新判出 " + done + " 条（其余读不出结论的也记下来了，不会再重算）。\r\n\r\n分类已刷新，切到「地图 / 人物」看看。", "识别压缩包内容");
        }

        internal void BulkImport()
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
            RefreshMods();
            RefreshConflicts(true);
            string sample = "";
            if (log != null)
                for (int i = 0; i < log.Count && i < 6; i++) sample += "\r\n  · " + log[i];
            MessageBox.Show("导入完成：入库 " + imported + " 个，已存在跳过 " + skipped + " 个。" + sample
                + "\r\n\r\n分类文件夹是按文件名猜的；猜不准就在列表里右键「重命名所选」或直接拖到对的文件夹。\r\n"
                + "这些 MOD 现在只是躺在库里，**没装进游戏**——要用的时候勾上，右键「只装勾中的 MOD」。", "导入");
        }

        /// <summary>库文件夹管理：把电脑上任意目录加进/移出扫描范围。工具不再只服务 U 盘布局。</summary>
        internal void ManageModDirs()
        {
            using (LibraryDirsForm f = new LibraryDirsForm())
            {
                f.ShowDialog(this);
            }
            if (LibraryDirsForm.Dirty)
            {
                Core.SaveConfig();
                Core.Log("OK", "库目录已改，重新扫描…");
                Core.ScanMods();
                RefreshMods();
                RefreshConflicts();
                if (treeView != null) { treeView.Rebuild(); treeView.Sync(); }
            }
        }

        /// <summary>小窗口：列出现有扫描目录（主库 + 自设目录），可添加 / 移除。</summary>
        private class LibraryDirsForm : Form
        {
            public static bool Dirty = false;      // 关掉时让外面知道要不要重扫
            private readonly ListBox lb = new ListBox();
            private readonly Label lblMain = new Label();

            public LibraryDirsForm()
            {
                Dirty = false;
                Text = "库文件夹管理";
                ClientSize = new Size(560, 380);
                Font = Theme.UI;
                BackColor = Theme.Bg;
                StartPosition = FormStartPosition.CenterParent;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;

                Label top = new Label();
                top.SetBounds(12, 10, 536, 52);
                top.Font = Theme.UISmall;
                top.ForeColor = Theme.Ink2;
                top.Text = "下面的文件夹都会被扫描（vpk / rar / zip 全认）。游戏目录 addons 里的已装 MOD 自动识别，不用加。\r\n"
                         + "在这里加目录只是「原地扫描」，不复制不移动任何文件。";
                Controls.Add(top);

                lblMain.SetBounds(12, 66, 536, 20);
                lblMain.Font = Theme.UIBold;
                lblMain.ForeColor = Theme.Ink2;
                lblMain.Text = "主库（跟着程序走的默认库）：" + Core.ModRoot;
                Controls.Add(lblMain);

                lb.SetBounds(12, 92, 536, 220);
                lb.BorderStyle = BorderStyle.FixedSingle;
                lb.IntegralHeight = false;
                lb.BackColor = Theme.Surface;
                lb.ForeColor = Theme.Ink;
                Controls.Add(lb);

                FlatButton add = new FlatButton();
                add.Text = "添加文件夹…";
                add.SetBounds(12, 326, 120, Theme.H);
                add.Primary = true;
                add.Click += delegate { AddDir(); };
                Controls.Add(add);

                FlatButton del = new FlatButton();
                del.Text = "移除所选";
                del.SetBounds(142, 326, 96, Theme.H);
                del.Click += delegate { RemoveDir(); };
                Controls.Add(del);

                FlatButton close = new FlatButton();
                close.Text = "关闭";
                close.SetBounds(464, 326, 84, Theme.H);
                close.DialogResult = DialogResult.Cancel;
                Controls.Add(close);
                CancelButton = close;

                Fill();
            }

            private void Fill()
            {
                lb.BeginUpdate();
                lb.Items.Clear();
                foreach (string d in Core.Cfg.ModDirs) lb.Items.Add(d);
                lb.EndUpdate();
            }

            private void AddDir()
            {
                FolderBrowserDialog d = new FolderBrowserDialog();
                d.Description = "选要扫描的 MOD 文件夹（里面放 vpk / rar / zip，子文件夹也会翻）";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string p = d.SelectedPath.TrimEnd('\\');
                if (p.Equals(Core.ModRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                { MessageBox.Show("这就是主库本身，不用再加。"); return; }
                foreach (object o in lb.Items)
                    if (string.Equals((string)o, p, StringComparison.OrdinalIgnoreCase))
                    { MessageBox.Show("已经在列表里了。"); return; }
                // 和游戏 addons 重叠的不管：游戏目录已装 MOD 是单独认的，重复扫只会多出同名项
                Core.Cfg.ModDirs.Add(p);
                Dirty = true;
                Fill();
            }

            private void RemoveDir()
            {
                if (lb.SelectedItem == null) { MessageBox.Show("先选中一行。"); return; }
                string p = (string)lb.SelectedItem;
                Core.Cfg.ModDirs.Remove(p);
                Dirty = true;
                Fill();
            }
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
                string q = "已经有「" + name + "」这个方案了（" + old.Mods.Count + " 个 MOD，存于 " + when + "）。\r\n\r\n直接覆盖它？";
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
            ProfileStore.Save(p, false);              // 同名就是覆盖，不留 .bak（用户明确要求）
            appliedProfile = p.Name;
            FillProfiles();
            Core.Log("OK", "方案已存（覆盖式）：" + p.Name + "（" + p.Mods.Count + " 个 MOD）");
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
            LoadSchemeKeys();
            LoadProfileIntoUi(p, false, true);
            // 分组标题里的「方案内 N」和「只看方案内」的筛选结果都跟方案走，换方案就得重排
            RefreshMods();
            int shown = 0;
            foreach (ModItem m in Core.ModList) if (checkedKeys.Contains(m.Key)) shown++;
            Log("方案「" + name + "」：已按它勾好 " + shown + " 个 MOD（含不在当前筛选里的），点「开始装」即装这些");
        }


        private void ApplyProfile()
        {
            if (cboProfile.SelectedItem == null) return;
            string name = cboProfile.SelectedItem.ToString();
            if (name.StartsWith("（")) return;
            LoadSchemeKeys();
            LoadProfileIntoUi(ProfileStore.Load(name), true, false);
            RefreshMods();
        }


        // 方案套用后把行的勾选状态跟 checkedKeys 对齐（不逐个触发部件默认逻辑）
        private void CheckRowsByKeys()
        {
            SyncPicks();
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
            sb.AppendLine("模式下拉  : " + cboMode.Left + "," + cboMode.Top + " " + cboMode.Width + "x" + cboMode.Height
                          + "  文本=" + (cboMode.SelectedIndex >= 0 ? cboMode.SelectedItem.ToString() : "(未选)")
                          + "  可见=" + cboMode.Visible);
            sb.AppendLine("MOD 库分组: " + gMod.Width + " x " + gMod.Height);
            sb.AppendLine("方案树    : 树可见=" + (treeView != null && treeView.Visible) + "，树尺寸="
                          + (treeView == null ? "-" : treeView.Width + "x" + treeView.Height)
                          + "，位置 " + (treeView == null ? "-" : treeView.SlotCount.ToString()));
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
            return sb.ToString();
        }

        private void ApplyResponsive()
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W == lastRespW && H == lastRespH) return;
            lastRespW = W;
            lastRespH = H;
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
            viewHost.Width = rightW - 24;
            lblSel.Width = rightW - 24;
            lblConflict.Width = rightW - 24;

            txtSearch.Width = rightW - 62;
            btnManage.Left = rightW - 12 - 68;

            // 顶部
            const int modeW = 104;
            int modeX = W - Theme.M - 84 - 8 - 84 - 8 - modeW;
            txtGame.Width = modeX - 8 - 76;
            cboMode.Left = modeX;
            cboMode.Top = 18;
            btnBrowse.Left = W - Theme.M - 84 - 8 - 84;
            btnFind.Left = W - Theme.M - 84;

            // 底部（按钮整组贴右下）
            btnOpen.Left = W - Theme.M - 104;
            btnRestore.Left = btnOpen.Left - 8 - 96;
            btnSwitch.Left = btnRestore.Left - 8 - 116;
            btnInstall.Left = btnSwitch.Left - 8 - 112;
            lblStatus.Width = W - 2 * Theme.M;
            bar.Width = Math.Max(200, btnInstall.Left - 8 - Theme.M);

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
            // 方案管理窗口要一个「大类 + 细分」的清单，跟以前分类下拉显示的一致
            List<CatChoice> r = new List<CatChoice>();
            r.Add(new CatChoice("", null, "(全部)"));
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
                r.Add(new CatChoice(cat, null, cat + "（" + n + "）"));
                foreach (string sb in order) r.Add(new CatChoice(cat, sb, "    " + sb + "（" + subs[sb] + "）"));
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
            // 这个窗口现在也能删 MOD（移进回收目录）：删过就重扫，列表/树跟上
            if (f.Deleted > 0)
            {
                Log("OK", "已装 MOD 开关里删掉 " + f.Deleted + " 个，重新扫库");
                Core.ScanMods();
                RefreshMods();
                RefreshConflicts();
                if (treeView != null) treeView.Rebuild();
            }
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

        /// <summary>
        /// 「应用改动」：把方案树里点出来的开关改动一次性写进 left4dead2\addonlist.txt。
        /// 只改这几行的 1/0，vpk 文件一个都不碰；写之前原文件备份成 addonlist.txt.装之前。
        /// 不动内存里的库扫描结果，只把 AddonOn 对齐，所以点完立刻生效、不用重扫。
        /// </summary>
        internal bool ApplySwitches(Dictionary<string, bool> st)
        {
            if (st == null || st.Count == 0) return false;
            string root = txtGame.Text.Trim();
            if (!Core.TestGame(root)) { MessageBox.Show("这个目录里没有 left4dead2.exe，先选对游戏根目录。", "路径不对"); return false; }
            if (!TestWritable(root))
            {
                MessageBox.Show("这个游戏目录写不进去（可能只读、被占用或权限不足）。\r\n可以试试以管理员身份运行。", "目录不可写");
                return false;
            }
            int on = 0;
            foreach (KeyValuePair<string, bool> kv in st) if (kv.Value) on++;
            try
            {
                string bak = AddonList.Write(root, st);
                Core.Log("OK", "应用开关改动 " + st.Count + " 处（开 " + on + "、关 " + (st.Count - on)
                    + "），原文件备份 " + Path.GetFileName(bak));

                // 只改了开关，文件没动：直接对齐内存里的值，省一次全库重扫
                foreach (ModGroup g in Catalog.Groups)
                    foreach (ModItem p in g.Parts)
                    {
                        bool v;
                        if (st.TryGetValue(p.Name, out v)) p.AddonOn = v;
                    }
                RefreshMods();
                RefreshConflicts(true);
                SetStatus(GameRunning()
                    ? "已应用 " + st.Count + " 处 · 游戏开着，重启后才生效"
                    : "已应用 " + st.Count + " 处开关改动");
                return true;
            }
            catch (Exception ex)
            {
                Core.Log("FAIL", "应用开关改动失败: " + ex.Message);
                MessageBox.Show("写不进 addonlist.txt：" + ex.Message + "\r\n\r\n可以试试以管理员身份运行。", "出错了",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        /// <summary>
        /// 方案树「只装这里」：只把这个分支选中的 vpk 铺进游戏，别的位置一个都不动。
        /// 与整机装机的区别：不碰 L4N / dxvk / 素材包 / 参数配置，不改全局勾选，也不写整机清单。
        /// </summary>
        internal void DeploySlot(List<ModItem> sel, List<ModItem> off, string slotName, Dictionary<string, bool> extra, Action onDone)
        {
            string root = txtGame.Text.Trim();
            if (!Core.TestGame(root)) { MessageBox.Show("这个目录里没有 left4dead2.exe，先选对游戏根目录。", "路径不对"); return; }
            if (!TestWritable(root))
            {
                MessageBox.Show("这个游戏目录写不进去（可能只读、被占用或权限不足）。\r\n可以试试以管理员身份运行，或换一个有写权限的目录。", "目录不可写");
                return;
            }
            // 先分清：哪些已经在游戏根目录里（只切开关就够），哪些确实要拷进去。
            // 判「已装」只看同名 + 同字节大小，和列表「已装」列同一套判定；
            // 不用 Core.SameFile（它连 mtime 一起比），否则时间戳差一点就白拷一遍 ——
            // 已装的 MOD 只该动 addonlist 的 1/0，不该把文件重新搬一次。
            string addonsDir = Path.Combine(Path.Combine(root, "left4dead2"), "addons");
            HashSet<string> selNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ModItem> inPlace = new List<ModItem>();
            List<ModItem> needCopy = new List<ModItem>();
            foreach (ModItem it in sel)
            {
                if (!selNames.Add(it.Name)) continue;                   // 同名只算一次
                string d = Path.Combine(addonsDir, it.Name);
                bool here = false;
                try { here = it.FromGame || (File.Exists(d) && new FileInfo(d).Length == it.Size); }
                catch { here = it.FromGame; }
                if (here) inPlace.Add(it); else needCopy.Add(it);
            }
            // 要关掉的旧 MOD：本位置里已经进游戏的都算（库里认识的和只在游戏目录里的都一样），
            // 但本次选中的不能关 —— 选中的永远赢。
            List<ModItem> offReal = new List<ModItem>();
            if (off != null)
                foreach (ModItem it in off)
                    if (!selNames.Contains(it.Name)) offReal.Add(it);

            // 纯切开关不动文件，游戏开着也能改（进游戏才读 addonlist）；要拷文件就必须先退游戏
            if (needCopy.Count > 0 && GameRunning())
            {
                MessageBox.Show("这次要往游戏目录里拷 " + needCopy.Count + " 个 vpk，游戏还开着，文件被占用。\r\n\r\n"
                    + "先把《求生之路2》完全退出（不是最小化）。\r\n"
                    + "如果只是想改开关，把勾选改成已经装好的那些，就不用退游戏。", "游戏正在运行");
                return;
            }

            long need = 0;
            foreach (ModItem it in needCopy) need += it.Size;
            string q = "只装「" + slotName + "」这一处：\r\n\r\n";
            if (inPlace.Count > 0)
                q += "· 已在游戏里：" + inPlace.Count + " 个 → 只改开关，不复制、不移动、不删除文件\r\n";
            if (needCopy.Count > 0)
                q += "· 需要拷进去：" + needCopy.Count + " 个，共 " + Core.Human(need) + "\r\n";
            if (offReal.Count > 0)
                q += "· 关掉这个位置之前装的 " + offReal.Count + " 个（文件留在原地，只把开关写成 0）\r\n";
            if (extra != null && extra.Count > 0)
                q += "· 另有 " + extra.Count + " 处开关改动一并写入 addonlist\r\n";
            q += "· 其它位置、L4N / dxvk / 参数配置一律不动\r\n";
            if (needCopy.Count == 0) q += "\r\n这次不复制任何文件，只写 addonlist.txt。";
            q += "\r\n\r\n继续吗？";
            if (MessageBox.Show(q, "只装这个位置", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            bool overwrite = !rdSkip.Checked;
            SetBusy(true);
            bar.Style = ProgressBarStyle.Marquee;
            SetStatus(needCopy.Count == 0 ? "正在改开关「" + slotName + "」…" : "正在装「" + slotName + "」…");
            Core.Log("STEP", "只装位置「" + slotName + "」：已在游戏里 " + inPlace.Count + " 个（只切开关，不动文件）"
                + (needCopy.Count > 0 ? "，需要拷入 " + needCopy.Count + " 个" : "，不复制任何文件")
                + (offReal.Count > 0 ? "，同时关掉旧的 " + offReal.Count + " 个" : ""));

            Thread th = new Thread(delegate()
            {
                try
                {
                    Work.Overwrite = overwrite;
                    // 只把「游戏里还没有的」交给复制；已在游戏里的一个都不碰，省得重新搬一遍
                    int copied = 0, skipped = inPlace.Count;
                    List<string> added = new List<string>(), failed = new List<string>();
                    if (needCopy.Count > 0)
                    {
                        int c2 = 0, s2 = 0;
                        List<string> a2, f2;
                        Work.CopyMods(root, needCopy, delegate(long done, long total)
                        {
                            int pct = total > 0 ? (int)(done * 1000 / total) : 0;
                            SetBar(pct);
                        }, out c2, out s2, out a2, out f2);
                        copied = c2; skipped += s2;
                        if (a2 != null) added.AddRange(a2);
                        if (f2 != null) failed.AddRange(f2);
                    }

                    Dictionary<string, bool> st = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    if (extra != null) foreach (KeyValuePair<string, bool> kv in extra) st[kv.Key] = kv.Value;
                    foreach (ModItem it in offReal) st[it.Name] = false;     // 先关旧的
                    foreach (ModItem it in sel) st[it.Name] = true;          // 选中的最后写，永远赢
                    string bak = AddonList.Write(root, st);
                    Core.Log("OK", "启用状态已写入（原文件备份 " + Path.GetFileName(bak) + "）");
                    if (failed.Count > 0)
                        Core.Log("FAIL", "复制失败 " + failed.Count + " 个：" + string.Join("、", failed.ToArray()));

                    SafeBeginInvoke(delegate
                    {
                        // 状态列是现读 addons + addonlist 得出的，装完必须重扫，否则列里还是旧状态
                        try { Core.ScanMods(); } catch (Exception exs) { Core.Log("WARN", "装完重扫失败: " + exs.Message); }
                        if (onDone != null) onDone();      // 界面上攒的开关改动已经连同这次一起写进去了
                        RefreshMods();
                        RefreshConflicts(true);
                        SetBusy(false);
                        SetBar(0);
                        SetStatus("就绪");
                        string tip = "「" + slotName + "」已更新：";
                        if (copied > 0) tip += "拷入 " + copied + " 个";
                        if (skipped > 0) tip += (copied > 0 ? "，" : "") + skipped + " 个已在游戏里，只切了开关";
                        if (offReal.Count > 0) tip += (copied > 0 || skipped > 0 ? "，" : "") + "关掉旧的 " + offReal.Count + " 个";
                        if (copied == 0) tip += "\r\n\r\n没有复制、移动或删除任何文件，只写了 addonlist.txt。";
                        Core.Log(failed.Count == 0 ? "OK" : "WARN", tip);
                        MessageBox.Show(tip
                            + (failed.Count > 0 ? "\r\n\r\n有 " + failed.Count + " 个复制失败，详见日志。" : "")
                            + "\r\n\r\n进游戏就能看到；这个位置的其它 MOD 没动。",
                            failed.Count > 0 ? "部分失败" : "只装这里 · 完成",
                            MessageBoxButtons.OK, failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    });
                }
                catch (Exception ex)
                {
                    Core.Log("FAIL", "只装位置失败: " + ex.Message);
                    SafeBeginInvoke(delegate
                    {
                        SetBusy(false); SetBar(0); SetStatus("就绪");
                        MessageBox.Show("装失败: " + ex.Message, "出错了", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                }
            });
            th.IsBackground = true;
            th.Start();
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
            // 唯一真依赖：MQCam 跑在 L4N 的 neko 框架里。别的（Skeeto / dxvk / MOD）都不需要 L4N
            if (wantMqcam && !wantL4N)
            {
                wantL4N = true;
                Core.Log("WARN", "MQCam 要用 L4N 的 neko 框架，这次把 L4N 一起装上");
            }
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
            Core.Cfg.L4N = wantL4N;
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
                        // 换整套：只翻 addonlist 的开关（这次的打开、上一套的关上），文件一个都不动
                        if (chkSwap.Checked)
                        {
                            Core.Log("STEP", "换整套：把不在这套里的 MOD 关掉");
                            SetStatus("正在切换 MOD 开关…");
                            int onN, offN, keepN;
                            Work.SwitchAddons(root, sel, assetName, out onN, out offN, out keepN);
                        }
                        else
                        {
                            Dictionary<string, bool> st = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                            foreach (ModItem it in sel) st[it.Name] = true;
                            if (assetName != null) st[assetName] = true;
                            string bak = AddonList.Write(root, st);
                            Core.Log("OK", "启用状态已写入（原文件备份 " + Path.GetFileName(bak) + "）");
                        }
                    }
                    else if (chkSwap.Checked)
                    {
                        // 一个 MOD 都没勾、但开着「换整套」：等于这套不要 MOD，全关掉
                        Core.Log("STEP", "换整套：这套没有 MOD，把旧的都关掉");
                        int onN, offN, keepN;
                        Work.SwitchAddons(root, sel, assetName, out onN, out offN, out keepN);
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
                        // 装完必须重扫：状态列（已装·开 / 已装·关）是现读 addons 和 addonlist 得出的，
                        // 不重扫的话回到列表看到的还是装机前的旧状态
                        try { Core.ScanMods(); }
                        catch (Exception exs) { Core.Log("WARN", "装完重扫失败: " + exs.Message); }
                        RefreshMods();
                        RefreshConflicts(true);
                        bool go = MessageBox.Show(
                            "装好了。\r\n\r\n现在启动游戏吗？\r\n\r\n"
                            + "启动就是校验 —— 不用跑两遍：\r\n"
                            + "· 起游戏后顺带盯 2 分钟的崩溃记录\r\n"
                            + "· 没崩 → 这次装的没问题，游戏留着直接玩\r\n"
                            + "· 崩了 → 弹提示，说明这次至少有一个坏包\r\n\r\n"
                            + "全程不改任何文件。不想现在玩就选「否」，游戏不会开。",
                            "完成 · 启动游戏", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                        if (!go) return;
                        Core.Log("STEP", "启动游戏（同时盯崩溃记录，不再二次启动）");
                        SetBusy(true);
                        SetStatus("正在启动游戏…");
                        Thread thTest = new Thread(delegate()
                        {
                            SelfTest.Result sr = null;
                            bool released = false;
                            Action<string> onStep = delegate(string s)
                            {
                                SetStatus(s);
                                // 游戏一旦起来就放开按钮，别让它一直锁着
                                if (!released) { released = true; SetBusy(false); }
                            };
                            try
                            {
                                sr = SelfTest.Run(root, 120, sel.Count, onStep, true,
                                    delegate(SelfTest.Result cr)
                                    {
                                        Core.Log("FAIL", "玩着玩着崩了: " + cr.Message.Replace("\r\n", " "));
                                        SafeBeginInvoke(delegate
                                        {
                                            MessageBox.Show(cr.Message, "游戏崩了", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                        });
                                    });
                            }
                            catch (Exception ex3) { Core.Log("FAIL", "启动出错: " + ex3.Message); }
                            SetBusy(false);
                            SetBar(0);
                            SetStatus("就绪");
                            if (sr == null) return;

                            // 自检前置条件不满足（Steam 没开 / 没 reports 目录 / 起不来）：
                            // 别把「启动游戏」一起吞掉，退化成就直接开
                            if (sr.Kind == "nosteam" || sr.Kind == "noreport" || sr.Kind == "launchfail")
                            {
                                Core.Log("WARN", "这次盯不了崩溃记录（" + sr.Kind + "），改为直接启动游戏");
                                StartGameDirect(root);
                                return;
                            }
                            if (sr.Kind == "busy")
                            {
                                Core.Log("INFO", "游戏已经开着，不用再启动");
                                SetStatus("游戏已经开着");
                                return;
                            }

                            if (appliedProfile.Length > 0 && (sr.Kind == "crash" || sr.Kind == "clean"))
                                ProfileStore.SetSelfTest(appliedProfile, sr.Kind == "crash" ? "FAIL" : "PASS", sr.Seconds);
                            Core.Log(sr.Kind == "crash" ? "FAIL" : "OK", "启动: " + sr.Message.Replace("\r\n", " "));

                            // 自己关掉游戏就别再弹框打扰
                            if (sr.Kind == "exited" || sr.Kind == "clean")
                            {
                                if (sr.Kind == "clean") SetStatus("游戏已启动 · 后台盯着崩溃记录");
                                return;
                            }
                            SafeBeginInvoke(delegate
                            {
                                MessageBox.Show(sr.Message, "启动结果 - " + sr.Kind,
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        /// <summary>直接开游戏：先走 Steam，起不来就直接跑 exe。盯不了崩溃记录时兜底用。</summary>
        private void StartGameDirect(string root)
        {
            try
            {
                System.Diagnostics.Process.Start("steam://rungameid/550");
                Core.Log("OK", "已通过 Steam 启动游戏（这次没盯崩溃记录）");
                SetStatus("游戏已启动（这次没盯崩溃记录）");
                return;
            }
            catch (Exception ex) { Core.Log("WARN", "steam:// 启动失败: " + ex.Message); }
            try
            {
                string exe = Path.Combine(root, "left4dead2.exe");
                if (!File.Exists(exe))
                {
                    SafeBeginInvoke(delegate
                    {
                        MessageBox.Show("找不到 left4dead2.exe，请在 Steam 里手动开游戏。", "启动失败",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                    return;
                }
                System.Diagnostics.Process.Start(exe, "-novid");
                Core.Log("OK", "已直接启动 left4dead2.exe（这次没盯崩溃记录）");
                SetStatus("游戏已启动（这次没盯崩溃记录）");
            }
            catch (Exception ex2)
            {
                Core.Log("WARN", "直接启动失败: " + ex2.Message);
                SafeBeginInvoke(delegate
                {
                    MessageBox.Show("游戏没启动：\r\n" + ex2.Message + "\r\n\r\n请在 Steam 里手动开。", "启动失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
            }
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
        private ListView clb;   // 方案内容清单：带勾选框 + 按大类分组
        private TextBox txtSearch;
        private CheckBox chkOnlyPicked;
        private ComboBox cboCat;
        private Label lblTitle2, lblInfo;
        private MainForm owner;

        private Profile current;
        private readonly List<string> rowKeys = new List<string>();
        private readonly Dictionary<string, ListViewGroup> groupOf = new Dictionary<string, ListViewGroup>(StringComparer.Ordinal);
        private HashSet<string> picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // 磁盘上那一版，用来判断「有没有改动」
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
            chkOnlyPicked.Checked = true;        // 先给「这套里有什么」，要加东西再取消勾
            chkOnlyPicked.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Theme.Dress(chkOnlyPicked);
            chkOnlyPicked.CheckedChanged += delegate { RebuildRows(); };
            Controls.Add(chkOnlyPicked);

            ToolTip tips = new ToolTip();
            tips.SetToolTip(selAll, "把当前筛选出来的行全部勾上（算未保存改动，点「保存」才写回方案）");
            tips.SetToolTip(selNone, "把当前筛选出来的行全部取消（同样要点「保存」）");
            tips.SetToolTip(txtSearch, "按 MOD 文件名或分类筛选；配合「只看已选」可以只看这个方案里的");

            // 方案内容：换成带分组标题的 ListView —— 一长串「名字 · 分类/细分」看着累，
            // 分组后「这套里地图几个、枪械几个」一眼就数得出来
            clb = new ListView();
            clb.SetBounds(248, 94, 560, 326);
            clb.View = View.Details;
            clb.CheckBoxes = true;
            clb.FullRowSelect = true;
            clb.ShowGroups = true;
            clb.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            clb.ShowItemToolTips = true;
            clb.BorderStyle = BorderStyle.FixedSingle; clb.BackColor = Theme.Surface; clb.ForeColor = Theme.Ink;
            clb.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ImageList pad2 = new ImageList(); pad2.ImageSize = new Size(1, 22);
            clb.SmallImageList = pad2;
            clb.Columns.Add("名称", 300);
            clb.Columns.Add("细分", 128);
            clb.Columns.Add("套装", 108);
            clb.ItemChecked += OnItemChecked;
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
            saved = new HashSet<string>(current.Mods, StringComparer.OrdinalIgnoreCase);
            dirty = false;
            MarkTitle();
            RebuildRows();
        }

        internal int DebugRows() { return clb.Items.Count; }

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
                clb.Groups.Clear();
                groupOf.Clear();
                rowKeys.Clear();
                string q = hintOn || txtSearch.Text == null ? "" : txtSearch.Text.Trim();
                CatChoice cc = cboCat.SelectedItem as CatChoice;
                string fcat = cc == null ? "" : cc.Cat;
                string fsub = cc == null ? null : cc.Sub;
                bool only = chkOnlyPicked.Checked;

                List<ModItem> hit = new List<ModItem>();
                foreach (ModItem it in Core.ModList)
                {
                    if (only && !picked.Contains(it.Key)) continue;
                    if (fcat.Length > 0 && !string.Equals(it.Cat, fcat, StringComparison.Ordinal)) continue;
                    if (fsub != null && !string.Equals(it.Sub, fsub, StringComparison.Ordinal)) continue;
                    if (q.Length > 0 && !MatchItem(it, q)) continue;
                    hit.Add(it);
                }
                // 排序按大类固定顺序，别让「地图」跑到「枪械」后面
                hit.Sort(delegate(ModItem a, ModItem b)
                {
                    int x = Array.IndexOf(Catalog.Cats, a.Cat), y = Array.IndexOf(Catalog.Cats, b.Cat);
                    if (x < 0) x = 99; if (y < 0) y = 99;
                    int c = x.CompareTo(y);
                    if (c != 0) return c;
                    c = string.Compare(a.Sub, b.Sub, StringComparison.CurrentCulture);
                    if (c != 0) return c;
                    return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
                });

                // 分组标题：大类 + 这套选了几个 / 这类一共几个（按整库算，切「只看已选」也不会变成 0 / 0）
                Dictionary<string, int> total = new Dictionary<string, int>(StringComparer.Ordinal);
                Dictionary<string, int> take = new Dictionary<string, int>(StringComparer.Ordinal);
                HashSet<string> showCats = new HashSet<string>(StringComparer.Ordinal);
                foreach (ModItem it in hit) showCats.Add(it.Cat);
                foreach (ModItem it in Core.ModList)
                {
                    if (!total.ContainsKey(it.Cat)) { total[it.Cat] = 0; take[it.Cat] = 0; }
                    total[it.Cat] = total[it.Cat] + 1;
                    if (picked.Contains(it.Key)) take[it.Cat] = take[it.Cat] + 1;
                }
                foreach (string c in Catalog.Cats)
                {
                    if (!total.ContainsKey(c) || !showCats.Contains(c)) continue;
                    ListViewGroup vg = new ListViewGroup(c + "　　" + take[c] + " / " + total[c] + " 个已选");
                    vg.HeaderAlignment = HorizontalAlignment.Left;
                    clb.Groups.Add(vg);
                    groupOf[c] = vg;
                }

                foreach (ModItem it in hit)
                {
                    string showName = it.Name.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase)
                        ? it.Name.Substring(0, it.Name.Length - 4) : it.Name;
                    ListViewItem li = new ListViewItem(showName);
                    li.ToolTipText = it.Name;
                    ListViewGroup vg;
                    if (groupOf.TryGetValue(it.Cat, out vg)) li.Group = vg;
                    li.SubItems.Add(MainForm.SubOf(it));                 // 细分：省掉重复的大类名
                    ModGroup g;
                    if (Catalog.GroupByKey.TryGetValue(it.GroupKey, out g) && g.IsSet) li.SubItems.Add(g.Name);
                    else li.SubItems.Add("—");
                    li.SubItems[1].ForeColor = MainForm.CatInk(it.Cat);   // 细分列跟主界面同色，两处对得上
                    if (picked.Contains(it.Key)) li.SubItems[2].ForeColor = Theme.Ok;
                    li.Tag = it.Key;
                    li.Checked = picked.Contains(it.Key);
                    if (it.SubjectLevel == "材质" || li.SubItems[1].Text == "—") li.SubItems[1].ForeColor = Theme.Ink3;
                    clb.Items.Add(li);
                    rowKeys.Add(it.Key);
                }
                clb.EndUpdate();
            }
            finally { filling = false; }
            dirty = Differs();
            MarkTitle();
            UpdateInfo();
        }

        // 和磁盘上那一版比：多一个少一个都算改动
        private bool Differs()
        {
            if (current == null) return picked.Count > 0;
            if (saved.Count != picked.Count) return true;
            foreach (string k in picked) if (!saved.Contains(k)) return true;
            foreach (string k in saved) if (!picked.Contains(k)) return true;
            return false;
        }

        // 搜索：名字、细分、套装名、标签都算
        private bool MatchItem(ModItem it, string q)
        {
            if (it.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if ((it.Cat + " " + it.Sub).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            ModGroup g;
            if (Catalog.GroupByKey.TryGetValue(it.GroupKey, out g) && g.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (filling) return;
            string key = e.Item.Tag as string;
            if (string.IsNullOrEmpty(key)) return;
            if (e.Item.Checked) picked.Add(key); else picked.Remove(key);
            bool d = Differs();
            if (d != dirty) { dirty = d; MarkTitle(); }
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
                foreach (ListViewItem li in clb.Items) li.Checked = on;
            }
            finally { filling = false; }
            dirty = Differs();
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
            saved = new HashSet<string>(fresh.Mods, StringComparer.OrdinalIgnoreCase);
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Installer
{
    /// <summary>
    /// 设置窗口：只放「本机偏好」和路径入口，不重复安装时逐次选择的组件。
    /// U 盘模式可指定主库所在盘；本地模式只扫描游戏目录，忽略所有外部库。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private sealed class DriveChoice
        {
            public readonly string Root;
            public readonly string Label;
            public DriveChoice(string root, string label) { Root = root; Label = label; }
            public override string ToString() { return Label; }
        }

        private readonly MainForm owner;
        private RadioButton rdUsb, rdLocal;
        private CheckBox chkMotion;
        private ComboBox cboGame, cboLibDrive;
        private FlatButton btnGameBrowse, btnGameFind, btnLibBrowse, btnLibManage, btnSave;
        private Label lblGameState, lblLibMain, lblLibExtra, lblLibHint, lblCfg, lblLog;
        private string pendingRoot;
        private bool syncing = true;

        internal bool WantLocal { get { return rdLocal.Checked; } }
        internal bool WantReduceMotion { get { return chkMotion.Checked; } }
        internal string WantLibraryRoot { get { return WantLocal ? Core.ModRoot : pendingRoot; } }

        /// <summary>输入框里的游戏根目录（已去空格和尾部反斜杠）。空串表示用户没填。</summary>
        internal string WantGameRoot
        {
            get
            {
                if (cboGame == null) return "";
                string s = cboGame.Text.Trim().Trim('"').TrimEnd('\\');
                return s;
            }
        }

        public SettingsForm(MainForm owner)
        {
            this.owner = owner;
            pendingRoot = Core.ModRoot;

            Text = "设置 · L4D2 MOD 管理工具";
            ClientSize = new Size(620, 676);
            Font = Theme.UI;
            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;

            Label title = new Label();
            title.Text = "设置";
            title.SetBounds(20, 14, 300, 30);
            title.Font = new Font(Theme.UI.FontFamily, 14F, FontStyle.Bold);
            title.ForeColor = Theme.Ink;
            title.BackColor = Color.Transparent;
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "本机偏好与路径。保存后只改配置，不动已经装进游戏的文件。";
            sub.SetBounds(20, 46, 580, 20);
            sub.Font = Theme.UISmall;
            sub.ForeColor = Theme.Ink3;
            sub.BackColor = Color.Transparent;
            Controls.Add(sub);

            BuildGeneral();
            BuildGameDir();
            BuildLibrary();
            BuildDiagnostics();
            BuildFooter();

            syncing = false;
            SyncModeState();
            UpdateGameState();
        }

        private void BuildGeneral()
        {
            Section s = new Section();
            s.Caption = "常规";
            s.SetBounds(16, 72, 588, 138);
            Controls.Add(s);

            Label mode = new Label();
            mode.Text = "工作模式";
            mode.SetBounds(12, 32, 64, 20);
            Theme.Dress(mode);
            s.Controls.Add(mode);

            rdUsb = new RadioButton();
            rdUsb.Text = "U 盘模式（推荐）";
            rdUsb.SetBounds(84, 30, 170, 22);
            Theme.Dress(rdUsb);
            rdUsb.Checked = !Core.IsLocal;
            rdUsb.CheckedChanged += delegate { if (!syncing) SyncModeState(); };
            s.Controls.Add(rdUsb);

            rdLocal = new RadioButton();
            rdLocal.Text = "本地模式";
            rdLocal.SetBounds(258, 30, 150, 22);
            Theme.Dress(rdLocal);
            rdLocal.Checked = Core.IsLocal;
            rdLocal.CheckedChanged += delegate { if (!syncing) SyncModeState(); };
            s.Controls.Add(rdLocal);

            Label modeHint = new Label();
            modeHint.Text = "U 盘模式扫描所选主库；本地模式只看游戏目录，不扫任何外部库。";
            modeHint.SetBounds(84, 52, 480, 18);
            modeHint.Font = Theme.UISmall;
            modeHint.ForeColor = Theme.Ink3;
            Theme.Dress(modeHint);
            s.Controls.Add(modeHint);

            chkMotion = new CheckBox();
            chkMotion.Text = "减少动态效果";
            chkMotion.SetBounds(12, 82, 560, 22);
            chkMotion.Checked = Core.Cfg.ReduceMotion;
            Theme.Dress(chkMotion);
            s.Controls.Add(chkMotion);

            Label motionHint = new Label();
            motionHint.Text = "关闭按钮和状态切换的轻量过渡，低配机器更轻快。";
            motionHint.SetBounds(32, 104, 536, 18);
            motionHint.Font = Theme.UISmall;
            motionHint.ForeColor = Theme.Ink3;
            Theme.Dress(motionHint);
            s.Controls.Add(motionHint);
        }

        /// <summary>
        /// 游戏目录：两种模式共用。U 盘模式决定装到哪，本地模式还额外决定扫哪里，
        /// 所以放在「常规」下面单独一组，不再只藏在主界面顶部那一行。
        /// </summary>
        private void BuildGameDir()
        {
            Section s = new Section();
            s.Caption = "游戏目录";
            s.SetBounds(16, 214, 588, 104);
            Controls.Add(s);

            Label lab = new Label();
            lab.Text = "游戏根目录";
            lab.SetBounds(12, 32, 72, 20);
            Theme.Dress(lab);
            s.Controls.Add(lab);

            cboGame = new ComboBox();
            cboGame.SetBounds(90, 28, 314, 24);
            cboGame.DropDownStyle = ComboBoxStyle.DropDown;
            Theme.Dress(cboGame);
            cboGame.Text = Core.Cfg.GameRoot;
            cboGame.TextChanged += delegate { if (!syncing) UpdateGameState(); };
            s.Controls.Add(cboGame);

            btnGameBrowse = new FlatButton();
            btnGameBrowse.Text = "浏览…";
            btnGameBrowse.SetBounds(412, 26, 74, Theme.H);
            btnGameBrowse.Click += delegate { BrowseGameRoot(); };
            s.Controls.Add(btnGameBrowse);

            btnGameFind = new FlatButton();
            btnGameFind.Text = "自动找";
            btnGameFind.SetBounds(492, 26, 74, Theme.H);
            btnGameFind.Click += delegate { FindGameRoot(); };
            s.Controls.Add(btnGameFind);

            lblGameState = new Label();
            lblGameState.SetBounds(12, 60, 552, 18);
            lblGameState.Font = Theme.UISmall;
            lblGameState.AutoEllipsis = true;
            s.Controls.Add(lblGameState);

            Label hint = new Label();
            hint.Text = "选里面有 left4dead2.exe 的那一层；本地模式只扫这里的 left4dead2\\addons。";
            hint.SetBounds(12, 80, 552, 18);
            hint.Font = Theme.UISmall;
            hint.ForeColor = Theme.Ink3;
            Theme.Dress(hint);
            s.Controls.Add(hint);
        }

        private void BuildLibrary()
        {
            Section s = new Section();
            s.Caption = "MOD 主库";
            s.SetBounds(16, 326, 588, 148);
            Controls.Add(s);

            Label drive = new Label();
            drive.Text = "库所在盘";
            drive.SetBounds(12, 32, 64, 20);
            Theme.Dress(drive);
            s.Controls.Add(drive);

            cboLibDrive = new ComboBox();
            cboLibDrive.SetBounds(84, 28, 200, 24);
            cboLibDrive.DropDownStyle = ComboBoxStyle.DropDownList;
            Theme.Dress(cboLibDrive);
            cboLibDrive.SelectedIndexChanged += delegate { if (!syncing) OnDriveChanged(); };
            s.Controls.Add(cboLibDrive);
            FillDrives();

            btnLibBrowse = new FlatButton();
            btnLibBrowse.Text = "选择文件夹…";
            btnLibBrowse.SetBounds(292, 26, 112, Theme.H);
            btnLibBrowse.Click += delegate { BrowseLibrary(); };
            s.Controls.Add(btnLibBrowse);

            lblLibMain = new Label();
            lblLibMain.SetBounds(12, 60, 560, 18);
            lblLibMain.Font = Theme.UISmall;
            lblLibMain.ForeColor = Theme.Ink2;
            lblLibMain.AutoEllipsis = true;
            s.Controls.Add(lblLibMain);

            lblLibExtra = new Label();
            lblLibExtra.SetBounds(12, 80, 560, 18);
            lblLibExtra.Font = Theme.UISmall;
            lblLibExtra.ForeColor = Theme.Ink2;
            lblLibExtra.AutoEllipsis = true;
            s.Controls.Add(lblLibExtra);

            btnLibManage = new FlatButton();
            btnLibManage.Text = "管理附加库目录…";
            btnLibManage.SetBounds(12, 106, 136, Theme.H);
            btnLibManage.Click += delegate { ManageLibrary(); };
            s.Controls.Add(btnLibManage);

            lblLibHint = new Label();
            lblLibHint.SetBounds(160, 112, 412, 18);
            lblLibHint.Font = Theme.UISmall;
            lblLibHint.ForeColor = Theme.Ink3;
            lblLibHint.AutoEllipsis = true;
            Theme.Dress(lblLibHint);
            s.Controls.Add(lblLibHint);
        }

        private void BuildDiagnostics()
        {
            Section s = new Section();
            s.Caption = "位置与诊断";
            s.SetBounds(16, 482, 588, 122);
            Controls.Add(s);

            lblCfg = new Label();
            lblCfg.SetBounds(12, 30, 560, 18);
            lblCfg.Font = Theme.UISmall;
            lblCfg.ForeColor = Theme.Ink2;
            lblCfg.AutoEllipsis = true;
            lblCfg.Text = "配置：" + Core.CfgPath;
            s.Controls.Add(lblCfg);

            lblLog = new Label();
            lblLog.SetBounds(12, 52, 560, 18);
            lblLog.Font = Theme.UISmall;
            lblLog.ForeColor = Theme.Ink2;
            lblLog.AutoEllipsis = true;
            lblLog.Text = "日志：" + Core.LogDir;
            s.Controls.Add(lblLog);

            FlatButton openCfg = new FlatButton();
            openCfg.Text = "打开配置文件夹";
            openCfg.SetBounds(12, 82, 132, Theme.H);
            openCfg.Click += delegate { OpenConfigFolder(); };
            s.Controls.Add(openCfg);

            FlatButton openLog = new FlatButton();
            openLog.Text = "打开日志";
            openLog.SetBounds(152, 82, 88, Theme.H);
            openLog.Click += delegate { OpenFolder(Core.LogDir); };
            s.Controls.Add(openLog);

            FlatButton env = new FlatButton();
            env.Text = "环境自检";
            env.SetBounds(248, 82, 96, Theme.H);
            env.Click += delegate { if (owner != null) owner.ShowEnv(); };
            s.Controls.Add(env);
        }

        private void BuildFooter()
        {
            Panel rule = new Panel();
            rule.SetBounds(16, 610, 588, 1);
            rule.BackColor = Theme.Line;
            Controls.Add(rule);

            Label note = new Label();
            note.Text = "游戏目录、工作模式与主库保存后生效，不改已装游戏文件。";
            note.SetBounds(16, 624, 380, 20);
            note.Font = Theme.UISmall;
            note.ForeColor = Theme.Ink3;
            note.BackColor = Color.Transparent;
            Controls.Add(note);

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            cancel.SetBounds(420, 618, 88, Theme.HP);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            btnSave = new FlatButton();
            btnSave.Text = "保存设置";
            btnSave.SetBounds(516, 618, 88, Theme.HP);
            btnSave.Primary = true;
            btnSave.DialogResult = DialogResult.OK;
            Controls.Add(btnSave);

            AcceptButton = btnSave;
            CancelButton = cancel;
        }

        // ---------------------------------------------------------- 游戏目录

        private void UpdateGameState()
        {
            if (lblGameState == null) return;
            string root = WantGameRoot;
            if (root.Length == 0)
            {
                bool hasOld = Core.TestGame(Core.Cfg.GameRoot ?? "");
                lblGameState.ForeColor = hasOld ? Theme.Ink3 : Theme.Accent;
                lblGameState.Text = hasOld
                    ? "未填写：保存时保持当前目录不变"
                    : "未设置：安装前必须先指定游戏根目录";
            }
            else if (Core.TestGame(root))
            {
                lblGameState.ForeColor = Theme.Ok;
                lblGameState.Text = "已识别：left4dead2.exe 在里面，可以扫描和安装";
            }
            else
            {
                lblGameState.ForeColor = Theme.Accent;
                lblGameState.Text = "没认出：这一层找不到 left4dead2.exe";
            }
            UpdateLibraryState();
        }

        private void BrowseGameRoot()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选游戏根目录（里面有 left4dead2.exe 的那个文件夹）";
                string cur = WantGameRoot;
                if (cur.Length > 0 && Directory.Exists(cur)) d.SelectedPath = cur;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                cboGame.Text = d.SelectedPath.TrimEnd('\\');
                cboGame.SelectionStart = cboGame.Text.Length;
                cboGame.SelectionLength = 0;
            }
        }

        private void FindGameRoot()
        {
            List<string> found = Core.FindGames();
            if (found == null || found.Count == 0)
            {
                lblGameState.ForeColor = Theme.Accent;
                lblGameState.Text = "没自动找到游戏，请用「浏览…」手动选";
                return;
            }
            cboGame.Text = found[0];
            if (found.Count > 1)
            {
                lblGameState.ForeColor = Theme.Warn;
                lblGameState.Text = "找到 " + found.Count + " 个，已填第一个；不对就点「浏览…」改";
            }
        }

        private void FillDrives()
        {
            cboLibDrive.Items.Clear();
            cboLibDrive.Items.Add(new DriveChoice("", "跟随程序所在盘（推荐）"));

            DriveInfo[] drives = null;
            try { drives = DriveInfo.GetDrives(); }
            catch { }

            int selected = 0;
            string currentDrive = "";
            try { currentDrive = Path.GetPathRoot(Core.ModRoot).TrimEnd('\\'); }
            catch { }

            if (drives != null)
            {
                foreach (DriveInfo d in drives)
                {
                    try
                    {
                        if (!d.IsReady || d.DriveType == DriveType.CDRom || d.DriveType == DriveType.NoRootDirectory) continue;
                        string root = d.Name.TrimEnd('\\');
                        string type = DriveTypeText(d.DriveType);
                        string free = "";
                        try { free = " · " + FormatSize(d.AvailableFreeSpace) + " 可用"; }
                        catch { }
                        cboLibDrive.Items.Add(new DriveChoice(root, root + "  " + type + free));
                        if (string.Equals(root, currentDrive, StringComparison.OrdinalIgnoreCase))
                            selected = cboLibDrive.Items.Count - 1;
                    }
                    catch { }
                }
            }

            // 空配置 = 跟随程序；有自定义主库就落在那块盘上，主库路径本身仍按 pendingRoot 显示
            if (string.IsNullOrEmpty(Core.Cfg.LibraryRoot)) selected = 0;
            syncing = true;
            cboLibDrive.SelectedIndex = selected;
            syncing = false;
        }

        private static string DriveTypeText(DriveType t)
        {
            if (t == DriveType.Removable) return "可移动磁盘";
            if (t == DriveType.Fixed) return "本地磁盘";
            if (t == DriveType.Network) return "网络盘";
            if (t == DriveType.Ram) return "内存盘";
            return "磁盘";
        }

        private static string FormatSize(long b)
        {
            if (b >= 1073741824L) return (b / 1073741824.0).ToString("N1") + " GB";
            if (b >= 1048576L) return (b / 1048576.0).ToString("N0") + " MB";
            return (b / 1024.0).ToString("N0") + " KB";
        }

        private void OnDriveChanged()
        {
            DriveChoice c = cboLibDrive.SelectedItem as DriveChoice;
            if (c == null) return;
            pendingRoot = string.IsNullOrEmpty(c.Root) ? Core.DefaultModRoot : FindKnownRoot(c.Root);
            UpdateLibraryState();
        }

        /// <summary>选盘时只认常见的库目录，不整盘递归；找不到就让用户明确选文件夹。</summary>
        private static string FindKnownRoot(string drive)
        {
            string[] candidates = new string[]
            {
                Path.Combine(Path.Combine(drive, "游戏内容"), "求生之路2各项MOD"),
                Path.Combine(drive, "求生之路2各项MOD"),
                Path.Combine(drive, "addons")
            };
            foreach (string p in candidates)
                if (Directory.Exists(p)) return p;
            return "";
        }

        private void BrowseLibrary()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择 MOD 主库根目录（里面按分类放 vpk / zip / rar）";
                if (Directory.Exists(pendingRoot)) d.SelectedPath = pendingRoot;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                pendingRoot = d.SelectedPath.TrimEnd('\\');
                SelectDriveFor(pendingRoot);
                UpdateLibraryState();
            }
        }

        private void SelectDriveFor(string path)
        {
            string drive = "";
            try { drive = Path.GetPathRoot(path).TrimEnd('\\'); }
            catch { }
            syncing = true;
            for (int i = 0; i < cboLibDrive.Items.Count; i++)
            {
                DriveChoice c = cboLibDrive.Items[i] as DriveChoice;
                if (c != null && !string.IsNullOrEmpty(c.Root)
                    && string.Equals(c.Root, drive, StringComparison.OrdinalIgnoreCase))
                { cboLibDrive.SelectedIndex = i; break; }
            }
            syncing = false;
        }

        private void SyncModeState()
        {
            bool local = WantLocal;
            bool canPick = !local;

            if (cboLibDrive != null) cboLibDrive.Enabled = canPick;
            if (btnLibBrowse != null) btnLibBrowse.Enabled = canPick;
            if (btnLibManage != null) btnLibManage.Enabled = canPick;
            UpdateLibraryState();
        }

        private void UpdateLibraryState()
        {
            if (WantLocal)
            {
                string addons = null;
                try
                {
                    string g = WantGameRoot;
                    if (g.Length > 0 && Core.TestGame(g))
                        addons = Path.Combine(Path.Combine(g, "left4dead2"), "addons");
                }
                catch { }

                lblLibMain.ForeColor = Theme.Ink2;
                lblLibMain.Text = addons != null
                    ? "本地模式：只扫描 " + addons
                    : "本地模式：只扫描游戏目录 addons（先在上方指定游戏目录）";
                lblLibExtra.ForeColor = Theme.Ink3;
                lblLibExtra.Text = "主库和附加目录都不参与扫描";
                lblLibHint.Text = "游戏目录在上方改；切回 U 盘模式后原来的库仍保留。";
                if (btnSave != null) btnSave.Enabled = true;
                return;
            }

            bool valid = !string.IsNullOrEmpty(pendingRoot) && Directory.Exists(pendingRoot);
            lblLibMain.ForeColor = valid ? Theme.Ink2 : Theme.Accent;
            lblLibMain.Text = valid ? "主库：" + pendingRoot : "主库：未找到有效目录，请重新选盘或选择文件夹";
            if (btnSave != null) btnSave.Enabled = valid;

            int n = Core.Cfg.ModDirs == null ? 0 : Core.Cfg.ModDirs.Count;
            lblLibExtra.ForeColor = Theme.Ink2;
            if (n == 0)
            {
                lblLibExtra.Text = "附加目录：无";
            }
            else
            {
                string sample = "";
                for (int i = 0; i < Core.Cfg.ModDirs.Count && i < 2; i++)
                {
                    if (sample.Length > 0) sample += "；";
                    sample += Core.Cfg.ModDirs[i];
                }
                lblLibExtra.Text = "附加目录：" + n + " 个 · " + sample + (n > 2 ? " 等" : "");
            }
            lblLibHint.Text = "主库和附加目录都会扫描；只读，不复制、不移动。";
        }

        private void ManageLibrary()
        {
            if (owner == null || WantLocal) return;
            owner.ManageModDirs();
            UpdateLibraryState();
        }

        private void OpenConfigFolder()
        {
            try
            {
                if (File.Exists(Core.CfgPath))
                    Process.Start("explorer.exe", "/select,\"" + Core.CfgPath + "\"");
                else
                    OpenFolder(Core.PkgDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打不开配置文件夹：" + ex.Message, "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void OpenFolder(string dir)
        {
            try
            {
                if (!string.IsNullOrEmpty(dir)) Process.Start("explorer.exe", dir);
            }
            catch { }
        }

        internal string LayoutDump()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("设置窗口客户区: " + ClientSize.Width + " x " + ClientSize.Height);
            sb.AppendLine("工作模式: " + (WantLocal ? "本地模式" : "U 盘模式"));
            sb.AppendLine("游戏目录: " + (WantGameRoot.Length == 0 ? "(未填)" : WantGameRoot));
            sb.AppendLine("游戏目录有效: " + Core.TestGame(WantGameRoot));
            sb.AppendLine("主库选择: " + (cboLibDrive.SelectedItem == null ? "(无)" : cboLibDrive.SelectedItem.ToString()));
            sb.AppendLine("主库路径: " + WantLibraryRoot);
            sb.AppendLine("主库有效: " + (WantLocal || (!string.IsNullOrEmpty(pendingRoot) && Directory.Exists(pendingRoot))));
            sb.AppendLine("减少动效: " + (WantReduceMotion ? "开" : "关"));
            sb.AppendLine("附加库目录: " + (Core.Cfg.ModDirs == null ? 0 : Core.Cfg.ModDirs.Count) + " 个");
            sb.AppendLine("配置路径: " + Core.CfgPath);
            sb.AppendLine("日志路径: " + Core.LogDir);
            return sb.ToString();
        }
    }

    public partial class MainForm
    {
        /// <summary>自测：把设置窗口放到屏幕外渲染一张，检查布局和当前值。不会保存任何设置。</summary>
        internal void UiTestSettings()
        {
            using (SettingsForm f = new SettingsForm(this))
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
                    bmp.Save(Path.Combine(Core.LogDir, "uitest_settings.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                File.WriteAllText(Path.Combine(Core.LogDir, "uitest_settings.txt"), f.LayoutDump(), new UTF8Encoding(true));
                Core.Log("INFO", "SETTINGS 设置窗口自测完成");
            }
        }

        private void ShowSettings()
        {
            bool oldLocal = Core.IsLocal;
            bool oldMotion = Core.Cfg.ReduceMotion;
            string oldRoot = Core.ModRoot;
            string oldGame = Core.Cfg.GameRoot ?? "";

            using (SettingsForm f = new SettingsForm(this))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;

                bool newLocal = f.WantLocal;
                bool newMotion = f.WantReduceMotion;
                string newRoot = f.WantLibraryRoot;
                string newGame = f.WantGameRoot;

                if (!newLocal && (string.IsNullOrEmpty(newRoot) || !Directory.Exists(newRoot)))
                {
                    MessageBox.Show("没有找到可用的 MOD 主库，请重新选盘或指定文件夹。", "设置",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 留空 = 不改：避免没填就点保存，把原来能用的游戏目录清掉
                if (newGame.Length > 0 && !Core.TestGame(newGame))
                {
                    MessageBox.Show("这个目录里没有 left4dead2.exe，设置没有保存：\r\n" + newGame, "设置",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                bool gameChanged = newGame.Length > 0
                    && !string.Equals(newGame.TrimEnd('\\'), oldGame.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

                bool rootChanged = !newLocal
                    && !string.Equals(newRoot.TrimEnd('\\'), oldRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

                if (gameChanged)
                {
                    Core.Cfg.GameRoot = newGame;
                    Core.SaveConfig();
                    SetGameRootText(newGame);
                    Core.Log("OK", "设置：游戏目录→" + newGame);
                }

                if (rootChanged)
                {
                    if (!Core.SetLibraryRoot(newRoot, true))
                    {
                        MessageBox.Show("这个主库目录不可用，设置没有保存。", "设置",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    Core.Log("OK", "MOD 主库改为：" + Core.ModRoot);
                }

                if (newMotion != oldMotion)
                {
                    Core.Cfg.ReduceMotion = newMotion;
                    Core.SaveConfig();
                    Core.Log("INFO", newMotion ? "设置：已关闭轻量动效" : "设置：已开启轻量动效");
                }

                if (newLocal != oldLocal && cboMode != null)
                {
                    cboMode.SelectedIndex = newLocal ? 1 : 0;   // 复用原有切换逻辑：保存并后台重扫
                }
                else if (gameChanged || rootChanged)
                {
                    ReloadLibrary(gameChanged && !rootChanged ? "正在按新的游戏目录重扫…" : null);
                }
            }
        }

        /// <summary>设置窗口改了游戏目录后，同步顶部输入框，别让界面还显示旧路径。</summary>
        private void SetGameRootText(string root)
        {
            if (txtGame == null) return;
            txtGame.Text = root;
            txtGame.SelectionStart = txtGame.Text.Length;
            txtGame.SelectionLength = 0;
        }

        /// <summary>主库或模式变更后重扫。extra 为空时状态文案由调用场景决定。</summary>
        private void ReloadLibrary(string extra)
        {
            SetBusy(true);
            SetStatus(extra ?? "正在按新的 MOD 主库重扫…");
            lblStatus.ForeColor = Theme.Ink2;
            bar.Style = ProgressBarStyle.Marquee;

            Thread th = new Thread(delegate()
            {
                string err = null;
                try { Core.ScanMods(); }
                catch (Exception ex) { err = ex.Message; Core.Log("FAIL", "切换主库后重扫失败: " + ex.Message); }
                SafeBeginInvoke(delegate()
                {
                    SetBusy(false);
                    bar.Style = ProgressBarStyle.Blocks;
                    RefreshMods();
                    RefreshConflicts(true);
                    if (treeView != null) treeView.Rebuild();
                    SetStatus(err == null ? "就绪" : "主库已切换，但重扫失败；请看日志");
                    if (err == null) lblStatus.ForeColor = Theme.Ink2;
                });
            });
            th.IsBackground = true;
            th.Start();
        }
    }
}

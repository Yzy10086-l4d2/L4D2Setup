using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Installer
{
    /// <summary>
    /// 参数存档：把机器上调好的 L4N 按键/网络参数（left4dead2\cfg\autoexec.cfg）
    /// 和 Skeeto 设置（skeeto_setting.ini）存到 U 盘，
    /// 换台新机器装机时直接推过去，不用再一项项点一遍。
    /// </summary>
    public static class Tweaks
    {
        public const string AutoexecName = "autoexec.cfg";       // 装在 left4dead2\cfg\
        public const string SkeetoName = "skeeto_setting.ini";   // 装在游戏根目录
        private const string InfoName = "存档信息.txt";

        /// <summary>自测时把存档根指到临时目录，别污染 U 盘上的真存档。</summary>
        public static string RootOverride;

        public static string Root
        {
            get { return RootOverride != null ? RootOverride : Path.Combine(Core.PkgDir, "_参数存档"); }
        }

        public static string AutoexecInGame(string gameRoot)
        {
            return Path.Combine(Path.Combine(Path.Combine(gameRoot, "left4dead2"), "cfg"), AutoexecName);
        }

        public static string SkeetoInGame(string gameRoot)
        {
            return Path.Combine(gameRoot, SkeetoName);
        }

        // ------------------------------------------------------------ 存档列表

        /// <summary>U 盘上已有的存档名（按名字排）。</summary>
        public static List<string> List()
        {
            List<string> names = new List<string>();
            try
            {
                if (!Directory.Exists(Root)) return names;
                foreach (string d in Directory.GetDirectories(Root))
                {
                    string n = Path.GetFileName(d);
                    if (n.StartsWith(".")) continue;
                    if (File.Exists(Path.Combine(d, AutoexecName)) || File.Exists(Path.Combine(d, SkeetoName)))
                        names.Add(n);
                }
            }
            catch (Exception ex) { Core.Log("WARN", "读参数存档失败: " + ex.Message); }
            names.Sort(StringComparer.CurrentCulture);
            return names;
        }

        public static string DirOf(string name) { return Path.Combine(Root, name); }

        public static bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (string n in List()) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>这套存档里有什么：文件名 + 大小 + 保存时间。</summary>
        public static string Info(string name)
        {
            StringBuilder sb = new StringBuilder();
            string dir = DirOf(name);
            sb.AppendLine("存档：" + name);
            sb.AppendLine("位置：" + dir);
            try
            {
                string meta = Path.Combine(dir, InfoName);
                if (File.Exists(meta))
                    foreach (string l in File.ReadAllLines(meta, Encoding.UTF8))
                        if (l.Trim().Length > 0) sb.AppendLine(l);
            }
            catch { }
            sb.AppendLine();
            sb.AppendLine("内容：");
            AppendFileInfo(sb, Path.Combine(dir, AutoexecName), @"→ left4dead2\cfg\autoexec.cfg（L4N 按键 / 网络 / 亮度）");
            AppendFileInfo(sb, Path.Combine(dir, SkeetoName), @"→ skeeto_setting.ini（Skeeto 全部面板设置）");
            return sb.ToString();
        }

        private static void AppendFileInfo(StringBuilder sb, string path, string where)
        {
            if (!File.Exists(path)) { sb.AppendLine("  （没有）" + where); return; }
            FileInfo fi = new FileInfo(path);
            sb.AppendLine("  " + fi.Name + "  " + Core.Human(fi.Length) + "  " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("     " + where);
        }

        // ------------------------------------------------------------ 保存

        /// <summary>把游戏目录里当前生效的两份配置存成一套存档。返回 null 表示成功。</summary>
        public static string Capture(string gameRoot, string name, out List<string> lines)
        {
            lines = new List<string>();
            name = (name ?? "").Trim();
            if (name.Length == 0) return "名字不能为空";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "名字里有不能用作文件名的字符";
            if (name.StartsWith(".")) return "名字不能以点开头";

            string a = AutoexecInGame(gameRoot);
            string s = SkeetoInGame(gameRoot);
            bool hasA = File.Exists(a), hasS = File.Exists(s);
            if (!hasA && !hasS)
                return "游戏目录里既没有 left4dead2\\cfg\\autoexec.cfg，也没有 skeeto_setting.ini。\r\n先把游戏装上、参数调好，再来保存。";

            string dir = DirOf(name);
            try
            {
                if (Directory.Exists(dir))
                {
                    // 旧存档别直接冲掉：整体挪成 .bak-时间戳
                    string bak = dir + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    Directory.Move(dir, bak);
                    lines.Add("原存档已备份为 " + Path.GetFileName(bak));
                }
                Directory.CreateDirectory(dir);
                if (hasA) { File.Copy(a, Path.Combine(dir, AutoexecName), true); lines.Add("已存 autoexec.cfg（" + Core.Human(new FileInfo(a).Length) + "）"); }
                if (hasS) { File.Copy(s, Path.Combine(dir, SkeetoName), true); lines.Add("已存 skeeto_setting.ini（" + Core.Human(new FileInfo(s).Length) + "）"); }

                StringBuilder meta = new StringBuilder();
                meta.AppendLine("保存时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                meta.AppendLine("来源机器：" + Environment.MachineName);
                meta.AppendLine("来源目录：" + gameRoot);
                if (hasA) meta.AppendLine("autoexec.cfg：" + new FileInfo(a).Length + " 字节，" + File.ReadAllLines(a).Length + " 行");
                if (hasS) meta.AppendLine("skeeto_setting.ini：" + new FileInfo(s).Length + " 字节，" + File.ReadAllLines(s).Length + " 行");
                File.WriteAllText(Path.Combine(dir, InfoName), meta.ToString(), new UTF8Encoding(true));
                Core.Log("OK", "参数存档「" + name + "」已保存: " + ((hasA ? "autoexec " : "") + (hasS ? "skeeto " : "")).Trim());
                return null;
            }
            catch (Exception ex)
            {
                Core.Log("FAIL", "保存参数存档失败: " + ex.Message);
                return ex.Message;
            }
        }

        // ------------------------------------------------------------ 套用 / 还原

        /// <summary>把存档推到游戏目录。装之前先备份目标文件。返回 null 表示成功。</summary>
        public static string Apply(string gameRoot, string name, out List<string> lines)
        {
            lines = new List<string>();
            string dir = DirOf(name);
            if (!Directory.Exists(dir)) return "找不到存档「" + name + "」";

            string srcA = Path.Combine(dir, AutoexecName);
            string srcS = Path.Combine(dir, SkeetoName);
            if (!File.Exists(srcA) && !File.Exists(srcS)) return "存档「" + name + "」里没有可用文件";

            try
            {
                if (File.Exists(srcA)) Push(srcA, AutoexecInGame(gameRoot), "left4dead2\\cfg\\autoexec.cfg", lines);
                if (File.Exists(srcS)) Push(srcS, SkeetoInGame(gameRoot), "skeeto_setting.ini", lines);
                Core.Log("OK", "参数已按存档「" + name + "」写入");
                return null;
            }
            catch (Exception ex)
            {
                Core.Log("FAIL", "套用参数失败: " + ex.Message);
                return ex.Message;
            }
        }

        private static void Push(string src, string dst, string label, List<string> lines)
        {
            string dir = Path.GetDirectoryName(dst);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(dst))
            {
                // .装之前 只写一次 —— 它是"这台机器原本的样子"，不能被第二次装机冲掉
                string first = dst + ".装之前";
                if (!File.Exists(first)) File.Copy(dst, first, true);
                File.Copy(dst, dst + ".上一次", true);
            }
            File.Copy(src, dst, true);
            long len = new FileInfo(dst).Length;
            if (len != new FileInfo(src).Length) throw new IOException(label + " 写入后大小不对");
            lines.Add(label + "  已写入 " + Core.Human(len));
            Core.Log("OK", label + " 已按存档写入（原文件备份 " + Path.GetFileName(dst) + ".装之前）");
        }

        // ------------------------------------------------------------ 还原

        /// <summary>把游戏里这两份配置还原成装机之前的样子。返回处理了几项。</summary>
        public static int Restore(string gameRoot, out List<string> lines)
        {
            lines = new List<string>();
            int n = 0;
            n += RestoreOne(AutoexecInGame(gameRoot), "left4dead2\\cfg\\autoexec.cfg", lines) ? 1 : 0;
            n += RestoreOne(SkeetoInGame(gameRoot), "skeeto_setting.ini", lines) ? 1 : 0;
            if (n > 0) Core.Log("OK", "参数已还原 " + n + " 项");
            else Core.Log("WARN", "没有可还原的参数备份");
            return n;
        }

        private static bool RestoreOne(string dst, string label, List<string> lines)
        {
            string first = dst + ".装之前";
            string last = dst + ".上一次";
            string from = File.Exists(first) ? first : (File.Exists(last) ? last : null);
            if (from == null) { lines.Add(label + "（没有备份，跳过）"); return false; }
            try
            {
                // 还原前把"现在这份"留一份，万一还原错了还能拿回来
                if (File.Exists(dst)) File.Copy(dst, dst + ".还原前", true);
                File.Copy(from, dst, true);
                lines.Add(label + "  已还原自 " + Path.GetFileName(from));
                Core.Log("OK", label + " 已还原（来源 " + Path.GetFileName(from) + "）");
                return true;
            }
            catch (Exception ex)
            {
                lines.Add(label + "  还原失败：" + ex.Message);
                Core.Log("FAIL", label + " 还原失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>这台机器上有没有备份可还原（用来决定按钮亮不亮）。</summary>
        public static int RestorableCount(string gameRoot)
        {
            int n = 0;
            if (File.Exists(AutoexecInGame(gameRoot) + ".装之前") || File.Exists(AutoexecInGame(gameRoot) + ".上一次")) n++;
            if (File.Exists(SkeetoInGame(gameRoot) + ".装之前") || File.Exists(SkeetoInGame(gameRoot) + ".上一次")) n++;
            return n;
        }

        public static bool Delete(string name, out string err)
        {
            err = null;
            try
            {
                string dir = DirOf(name);
                if (!Directory.Exists(dir)) { err = "没有这个存档"; return false; }
                string bak = dir + ".删除-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Directory.Move(dir, bak);
                Core.Log("OK", "参数存档「" + name + "」已移走（" + Path.GetFileName(bak) + "，没真删）");
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        // ------------------------------------------------------------ 自测

        /// <summary>
        /// 在临时目录里造一个假游戏，把 保存 → 套用 → 再套用 → 还原 走一遍。
        /// 不碰真游戏目录，也不碰 U 盘上的真存档。返回失败项数。
        /// </summary>
        public static int SelfTest()
        {
            int bad = 0;
            string baseDir = Path.Combine(Path.GetTempPath(), "l4d2-tw-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string game = Path.Combine(baseDir, "game");
            string stock = Path.GetTempPath() + "l4d2-tw-stock-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            RootOverride = stock;
            try
            {
                Directory.CreateDirectory(Path.Combine(Path.Combine(game, "left4dead2"), "cfg"));
                Directory.CreateDirectory(game);
                string cfg = AutoexecInGame(game), sk = SkeetoInGame(game);
                string original = "// 原版\r\nbind tab +showscores\r\n";
                File.WriteAllText(cfg, original, new UTF8Encoding(false));
                File.WriteAllText(sk, "old_setting=1\r\n", new UTF8Encoding(false));

                List<string> lines;
                string err = Capture(game, "自测套", out lines);
                if (err != null) { Core.Log("FAIL", "TWEAKSELF 保存失败: " + err); bad++; }
                if (!File.Exists(Path.Combine(DirOf("自测套"), AutoexecName))) { Core.Log("FAIL", "TWEAKSELF autoexec 没存进去"); bad++; }
                if (!File.Exists(Path.Combine(DirOf("自测套"), SkeetoName))) { Core.Log("FAIL", "TWEAKSELF skeeto 没存进去"); bad++; }

                // 模拟装机后：游戏里两份都被改成"存档里的样子"
                File.WriteAllText(cfg, "// 装机后\r\nbind tab +showscores\r\n", new UTF8Encoding(false));
                File.WriteAllText(sk, "old_setting=0\r\n", new UTF8Encoding(false));

                err = Apply(game, "自测套", out lines);
                if (err != null) { Core.Log("FAIL", "TWEAKSELF 套用失败: " + err); bad++; }
                if (File.ReadAllText(cfg) != original) { Core.Log("FAIL", "TWEAKSELF 套用后 autoexec 内容不对"); bad++; }
                if (File.ReadAllText(sk) != "old_setting=1\r\n") { Core.Log("FAIL", "TWEAKSELF 套用后 skeeto 内容不对"); bad++; }
                if (!File.Exists(cfg + ".装之前")) { Core.Log("FAIL", "TWEAKSELF 没留 .装之前"); bad++; }
                else if (File.ReadAllText(cfg + ".装之前") != "// 装机后\r\nbind tab +showscores\r\n")
                { Core.Log("FAIL", "TWEAKSELF .装之前 内容不对"); bad++; }

                // 第二次套用：.装之前 必须还是第一次的那份（原版），不能被子冲掉
                File.WriteAllText(cfg, "// 第二次装机前\r\n", new UTF8Encoding(false));
                Apply(game, "自测套", out lines);
                if (File.ReadAllText(cfg + ".装之前") != "// 装机后\r\nbind tab +showscores\r\n")
                { Core.Log("FAIL", "TWEAKSELF 第二次套用把 .装之前 冲掉了"); bad++; }

                // 还原
                int n = Restore(game, out lines);
                if (n != 2) { Core.Log("FAIL", "TWEAKSELF 还原项数 " + n + "（应为 2）"); bad++; }
                if (File.ReadAllText(cfg) != "// 装机后\r\nbind tab +showscores\r\n")
                { Core.Log("FAIL", "TWEAKSELF 还原后 autoexec 不对: " + File.ReadAllText(cfg).Trim()); bad++; }
                if (!File.Exists(cfg + ".还原前")) { Core.Log("FAIL", "TWEAKSELF 还原前没留底"); bad++; }
                if (RestorableCount(game) != 2) { Core.Log("FAIL", "TWEAKSELF RestorableCount 不对"); bad++; }

                // 删除
                string derr;
                if (!Delete("自测套", out derr)) { Core.Log("FAIL", "TWEAKSELF 删除失败: " + derr); bad++; }
                if (Exists("自测套")) { Core.Log("FAIL", "TWEAKSELF 删除后还能列出"); bad++; }

                Core.Log(bad == 0 ? "OK" : "FAIL", "TWEAKSELF " + (bad == 0 ? "参数保存/套用/还原 全部正常" : (bad + " 项不对")));
            }
            catch (Exception ex) { Core.Log("FAIL", "TWEAKSELF 异常: " + ex.Message); bad++; }
            finally
            {
                RootOverride = null;
                try { Directory.Delete(baseDir, true); } catch { }
                try { Directory.Delete(stock, true); } catch { }
            }
            return bad;
        }

        /// <summary>
        /// 兼容用户以前手工建的「我的Skeeto设置」：第一次打开时把它导入成一套存档，
        /// 免得他还要再存一遍。
        /// </summary>
        public static void ImportLegacy()
        {
            ImportLegacy(null);
        }

        /// <summary>
        /// 第一次运行时把用户手工建的「我的Skeeto设置」导入成一套存档；
        /// 顺手从当前游戏目录把 autoexec.cfg 补进去 —— 光有 skeeto 的那套是半成品。
        /// </summary>
        public static void ImportLegacy(string gameRoot)
        {
            try
            {
                string legacy = Path.Combine(Core.PkgDir, "我的Skeeto设置");
                string dir = DirOf("我的设置");
                bool fresh = !(Directory.Exists(Root) && Directory.GetDirectories(Root).Length > 0);

                if (fresh && Directory.Exists(legacy))
                {
                    string ini = Path.Combine(legacy, SkeetoName);
                    if (File.Exists(ini))
                    {
                        Directory.CreateDirectory(dir);
                        File.Copy(ini, Path.Combine(dir, SkeetoName), true);
                        StringBuilder meta = new StringBuilder();
                        meta.AppendLine("保存时间：" + new FileInfo(ini).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "（从「我的Skeeto设置」导入）");
                        meta.AppendLine("来源机器：" + Environment.MachineName);
                        File.WriteAllText(Path.Combine(dir, InfoName), meta.ToString(), new UTF8Encoding(true));
                        Core.Log("INFO", "已把「我的Skeeto设置」导入为参数存档「我的设置」");
                        fresh = false;
                    }
                }

                // 这套只有 skeeto 没有 autoexec：从当前游戏目录补一份，否则下次装机 L4N 参数还是空的
                if (gameRoot != null && Directory.Exists(dir))
                {
                    string cfgInGame = AutoexecInGame(gameRoot);
                    string cfgInStock = Path.Combine(dir, AutoexecName);
                    if (File.Exists(cfgInGame) && !File.Exists(cfgInStock))
                    {
                        File.Copy(cfgInGame, cfgInStock, true);
                        try { File.AppendAllText(Path.Combine(dir, InfoName), "autoexec.cfg：" + new FileInfo(cfgInGame).Length + " 字节（从 " + gameRoot + " 补入）\r\n", new UTF8Encoding(true)); } catch { }
                        Core.Log("INFO", "参数存档「我的设置」补上了 autoexec.cfg（" + Core.Human(new FileInfo(cfgInGame).Length) + "）");
                    }
                }
            }
            catch (Exception ex) { Core.Log("WARN", "导入旧设置失败: " + ex.Message); }
        }
    }
}

namespace Installer
{
    /// <summary>
    /// 参数存档管理：存当前机器、套用到本机、还原成装机前、删掉不要的。
    /// 目的只有一个 —— 换台机器装机不用再手调一遍参数。
    /// </summary>
    public class TweakForm : Form
    {
        private string gameRoot;
        private ListBox lb;
        private TextBox txtPreview;
        private Label lblHead;
        private FlatButton btnApply, btnSave, btnRestore, btnDel;

        /// <summary>关窗时选中的存档名（给主界面记忆用）。</summary>
        public string Chosen = "";
        /// <summary>这次有没有真的往游戏目录写过东西。</summary>
        public bool Applied = false;

        public TweakForm(string root, string current)
        {
            gameRoot = root;
            Text = "参数配置（L4N 按键 / Skeeto 设置）";
            ClientSize = new Size(664, 452);
            Font = Theme.UI;
            BackColor = Theme.Bg;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            lblHead = new Label();
            lblHead.SetBounds(12, 10, 640, 34);
            lblHead.Font = Theme.UISmall;
            lblHead.ForeColor = Theme.Ink2;
            lblHead.Text = "调好的参数存在 U 盘上，换台机器装完直接把这一套推过去，不用再一项项设置。\r\n涉及两个文件：left4dead2\\cfg\\autoexec.cfg（L4N 按键/网络/亮度）和 skeeto_setting.ini（Skeeto 面板设置）。";
            Controls.Add(lblHead);

            lb = new ListBox();
            lb.SetBounds(12, 50, 200, 300);
            lb.BorderStyle = BorderStyle.FixedSingle;
            lb.BackColor = Theme.Surface;
            lb.ForeColor = Theme.Ink;
            lb.Font = Theme.UI;
            lb.IntegralHeight = false;
            lb.SelectedIndexChanged += delegate { ShowInfo(); lblHead.Refresh(); };
            Controls.Add(lb);

            txtPreview = new TextBox();
            txtPreview.SetBounds(224, 50, 428, 300);
            txtPreview.Multiline = true;
            txtPreview.ReadOnly = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;
            txtPreview.WordWrap = true;
            txtPreview.BorderStyle = BorderStyle.FixedSingle;
            txtPreview.BackColor = Theme.Surface;
            txtPreview.ForeColor = Theme.Ink2;
            txtPreview.Font = Theme.UISmall;
            Controls.Add(txtPreview);

            btnSave = new FlatButton(); btnSave.Text = "存当前配置为…"; btnSave.SetBounds(12, 362, 130, Theme.H);
            btnSave.Click += delegate { DoSave(); }; Controls.Add(btnSave);

            btnApply = new FlatButton(); btnApply.Text = "套用到本机"; btnApply.Primary = true; btnApply.SetBounds(150, 362, 108, Theme.H);
            btnApply.Click += delegate { DoApply(); }; Controls.Add(btnApply);

            btnRestore = new FlatButton(); btnRestore.Text = "还原本机原配置"; btnRestore.SetBounds(266, 362, 128, Theme.H);
            btnRestore.Click += delegate { DoRestore(); }; Controls.Add(btnRestore);

            btnDel = new FlatButton(); btnDel.Text = "删除存档"; btnDel.SetBounds(402, 362, 88, Theme.H);
            btnDel.Click += delegate { DoDelete(); }; Controls.Add(btnDel);

            FlatButton btnOpen = new FlatButton(); btnOpen.Text = "打开文件夹"; btnOpen.SetBounds(498, 362, 92, Theme.H);
            btnOpen.Click += delegate { try { Directory.CreateDirectory(Tweaks.Root); System.Diagnostics.Process.Start("explorer.exe", Tweaks.Root); } catch { } }; Controls.Add(btnOpen);

            FlatButton btnClose = new FlatButton(); btnClose.Text = "关闭"; btnClose.SetBounds(598, 362, 54, Theme.H);
            btnClose.DialogResult = DialogResult.Cancel; Controls.Add(btnClose);
            CancelButton = btnClose;

            Label tip = new Label();
            tip.SetBounds(12, 400, 640, 40);
            tip.Font = Theme.UISmall;
            tip.ForeColor = Theme.Ink3;
            tip.Text = "「还原本机原配置」只回滚这两个参数文件（用装机时留的 .装之前 备份），不动平台、不动 MOD。\r\n装箱台不会改 U 盘上别的机器的存档；旧存档被覆盖前会自动改名留一份 .bak-时间戳。";
            Controls.Add(tip);

            Reload(current);
        }

        private void Reload(string select)
        {
            Tweaks.ImportLegacy(gameRoot);
            lb.Items.Clear();
            foreach (string n in Tweaks.List()) lb.Items.Add(n);
            if (lb.Items.Count == 0)
            {
                txtPreview.Text = "U 盘上还没有参数存档。\r\n\r\n先把游戏装好、参数调成你想要的样子，\r\n再点下面的「存当前配置为…」存一套。\r\n之后换机器装机时勾上「套用参数配置」就会自动写进去。";
                Selected(null);
                return;
            }
            int idx = 0;
            for (int i = 0; i < lb.Items.Count; i++)
                if (string.Equals(lb.Items[i].ToString(), select, StringComparison.OrdinalIgnoreCase)) idx = i;
            lb.SelectedIndex = idx;
            ShowInfo();
        }

        private string Sel()
        {
            return lb.SelectedItem == null ? null : lb.SelectedItem.ToString();
        }

        private void Selected(string name)
        {
            Chosen = name ?? "";
            bool has = name != null;
            btnApply.Enabled = has;
            btnDel.Enabled = has;
        }

        private void ShowInfo()
        {
            string name = Sel();
            Selected(name);
            if (name == null) return;
            txtPreview.Text = Tweaks.Info(name);
        }

        private void DoSave()
        {
            if (gameRoot == null || gameRoot.Length == 0 || !Core.TestGame(gameRoot))
            {
                MessageBox.Show("先在上面选对游戏目录。", "没有游戏目录");
                return;
            }
            string def = Sel();
            if (def == null || def.Length == 0) def = "机器-" + Environment.MachineName;
            string name = PromptForm.Ask("给这套参数起个名字（例：网吧标准 / 竞技 / 我的）\r\n同名会覆盖（旧的那份自动留 .bak）", "保存参数配置", def);
            if (string.IsNullOrEmpty(name)) return;
            List<string> lines;
            string err = Tweaks.Capture(gameRoot, name.Trim(), out lines);
            if (err != null) { MessageBox.Show(err, "没存成"); return; }
            Reload(name.Trim());
            MessageBox.Show("已保存到：\r\n" + Tweaks.DirOf(name.Trim()) + "\r\n\r\n" + string.Join("\r\n", lines.ToArray()), "存好了");
        }

        private void DoApply()
        {
            string name = Sel();
            if (name == null) return;
            if (gameRoot == null || !Core.TestGame(gameRoot)) { MessageBox.Show("先选对游戏目录。"); return; }
            if (MessageBox.Show("把「" + name + "」里的参数写入这台机器？\r\n\r\nleft4dead2\\cfg\\autoexec.cfg\r\nskeeto_setting.ini\r\n\r\n现在的这两份会备份成 .装之前 / .上一次（不会丢）。", "套用参数",
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            List<string> lines;
            string err = Tweaks.Apply(gameRoot, name, out lines);
            if (err != null) { MessageBox.Show(err, "没套上"); return; }
            Applied = true;
            MessageBox.Show("已套用：\r\n" + string.Join("\r\n", lines.ToArray()), "好了");
            ShowInfo();
        }

        private void DoRestore()
        {
            if (gameRoot == null || !Core.TestGame(gameRoot)) { MessageBox.Show("先选对游戏目录。"); return; }
            int can = Tweaks.RestorableCount(gameRoot);
            if (can == 0)
            {
                MessageBox.Show("这台机器上没有 .装之前 / .上一次 备份，没东西可还原。\r\n（备份是在套用参数、或装机写 cfg 时自动留的）", "没有备份");
                return;
            }
            if (MessageBox.Show("把这两份配置回滚到装机之前的样子？\r\n\r\n" + can + " 项可还原。当前这份会留成 .还原前。", "还原参数",
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            List<string> lines;
            int n = Tweaks.Restore(gameRoot, out lines);
            Applied = true;
            MessageBox.Show((n > 0 ? "已还原 " + n + " 项：\r\n" : "没有还原成功：\r\n") + string.Join("\r\n", lines.ToArray()), "还原");
        }

        private void DoDelete()
        {
            string name = Sel();
            if (name == null) return;
            if (MessageBox.Show("把存档「" + name + "」移走？\r\n（改名成 .删除-时间戳 留在原地，没真删）", "删除", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            string err;
            if (!Tweaks.Delete(name, out err)) { MessageBox.Show(err); return; }
            Reload(null);
            if (lb.Items.Count > 0) lb.SelectedIndex = 0; else Selected(null);
        }
    }
}

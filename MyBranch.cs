using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Installer
{
    // ============================================================
    //  「我的分支」：程序认不出改的是什么的 MOD，用户自己拉一个分组装进去。
    //
    //  只干三件事：记住有哪些分支、每个分支里有哪几个 vpk、某个 vpk 在哪一支。
    //  不碰文件、不改自动分类、不影响 addonlist —— 装机时照常按「勾没勾」走。
    //  一个 MOD 只归一个分支（再放一次就当换分支），免得同一个东西在两个分支里都被勾着。
    // ============================================================

    internal static class MyBranch
    {
        private static bool loaded;
        private static readonly List<string> names = new List<string>();
        private static readonly Dictionary<string, List<string>> items =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ofMod =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string FileOf()
        {
            return string.IsNullOrEmpty(Core.PkgDir) ? "" : Path.Combine(Core.PkgDir, "我的分支.ini");
        }

        public static void Ensure() { if (!loaded) Load(); }

        /// <summary>自测 / 外部改过文件之后重读。</summary>
        public static void Reload() { loaded = false; Ensure(); }

        private static void Load()
        {
            loaded = true;
            names.Clear(); items.Clear(); ofMod.Clear();
            try
            {
                string f = FileOf();
                if (File.Exists(f))
                    foreach (string raw in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        string s = raw.TrimEnd();
                        if (s.Length == 0 || s[0] == '#') continue;
                        if (s.StartsWith("branch=", StringComparison.Ordinal))
                        {
                            string v = s.Substring(7).Trim();
                            if (v.Length > 0 && !names.Contains(v)) names.Add(v);
                        }
                        else if (s.StartsWith("mod=", StringComparison.Ordinal))
                        {
                            string[] p = s.Substring(4).Split('\t');
                            if (p.Length < 2) continue;
                            string b = p[0].Trim(), vpk = p[1].Trim();
                            if (b.Length == 0 || vpk.Length == 0) continue;
                            if (!names.Contains(b)) names.Add(b);
                            ListOf(b).Add(vpk);
                            ofMod[vpk] = b;
                        }
                    }
            }
            catch { }
        }

        private static void Save()
        {
            string f = FileOf();
            if (string.IsNullOrEmpty(f)) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# 我的分支：用户自己拉的分组（装机台自己写，手改也行）");
            sb.AppendLine("# branch=分支名 / mod=分支名 <TAB> vpk 文件名");
            foreach (string n in names) sb.AppendLine("branch=" + n);
            foreach (string n in names)
            {
                List<string> l;
                if (!items.TryGetValue(n, out l)) continue;
                foreach (string v in l) sb.AppendLine("mod=" + n + "\t" + v);
            }
            try { File.WriteAllText(f, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { Core.Log("WARN", "我的分支保存失败: " + ex.Message); }
        }

        private static List<string> ListOf(string branch)
        {
            List<string> l;
            if (!items.TryGetValue(branch, out l)) { l = new List<string>(); items[branch] = l; }
            return l;
        }

        // ---------------------------------------------------------- 查询

        public static List<string> Names()
        {
            Ensure();
            return new List<string>(names);
        }

        public static int Count { get { Ensure(); return names.Count; } }

        public static List<string> ItemsOf(string branch)
        {
            Ensure();
            List<string> l;
            if (!items.TryGetValue(branch ?? "", out l)) return new List<string>();
            return new List<string>(l);
        }

        /// <summary>这个 vpk 在哪个分支里；不在任何分支返回 ""。</summary>
        public static string BranchOf(string vpk)
        {
            Ensure();
            if (string.IsNullOrEmpty(vpk)) return "";
            string b;
            return ofMod.TryGetValue(vpk, out b) ? b : "";
        }

        public static bool Has(string branch)
        {
            Ensure();
            return !string.IsNullOrEmpty(branch) && names.Contains(branch);
        }

        // ---------------------------------------------------------- 修改

        /// <summary>新建分支。名字空 / 重名返回错误文字，成功返回 null。</summary>
        public static string Create(string name)
        {
            Ensure();
            name = CleanName(name);
            if (name.Length == 0) return "分支名不能为空";
            if (names.Contains(name)) return "已经有叫这个名字的分支了";
            names.Add(name);
            ListOf(name);
            Save();
            return null;
        }

        public static string Rename(string oldName, string newName)
        {
            Ensure();
            newName = CleanName(newName);
            if (!names.Contains(oldName)) return "这个分支不在了";
            if (newName.Length == 0) return "分支名不能为空";
            if (newName == oldName) return null;
            if (names.Contains(newName)) return "已经有叫这个名字的分支了";
            int i = names.IndexOf(oldName);
            names[i] = newName;
            List<string> l;
            if (items.TryGetValue(oldName, out l))
            {
                items.Remove(oldName);
                items[newName] = l;
                foreach (string v in l) ofMod[v] = newName;
            }
            Save();
            return null;
        }

        /// <summary>解散分支：只删分组，vpk 文件和勾选一个都不动。</summary>
        public static void Delete(string name)
        {
            Ensure();
            if (!names.Contains(name)) return;
            names.Remove(name);
            List<string> l;
            if (items.TryGetValue(name, out l))
            {
                foreach (string v in l) if (BranchOf(v) == name) ofMod.Remove(v);
                items.Remove(name);
            }
            Save();
        }

        /// <summary>把 vpk 放进分支（原来在别的分支里就搬过来）。</summary>
        public static void Put(string branch, string vpk)
        {
            Ensure();
            if (string.IsNullOrEmpty(branch) || string.IsNullOrEmpty(vpk)) return;
            if (!names.Contains(branch)) { names.Add(branch); ListOf(branch); }
            string old;
            if (ofMod.TryGetValue(vpk, out old))
            {
                if (old == branch) return;
                List<string> o;
                if (items.TryGetValue(old, out o)) o.RemoveAll(delegate(string x) { return string.Equals(x, vpk, StringComparison.OrdinalIgnoreCase); });
            }
            ListOf(branch).Add(vpk);
            ofMod[vpk] = branch;
            Save();
        }

        /// <summary>从分支里移出（不删文件）。</summary>
        public static void Take(string vpk)
        {
            Ensure();
            string b;
            if (!ofMod.TryGetValue(vpk, out b)) return;
            ofMod.Remove(vpk);
            List<string> l;
            if (items.TryGetValue(b, out l))
                l.RemoveAll(delegate(string x) { return string.Equals(x, vpk, StringComparison.OrdinalIgnoreCase); });
            Save();
        }

        /// <summary>分支名清理：去掉 tab / 换行（文件里用 tab 分隔），限长 24 字。</summary>
        public static string CleanName(string s)
        {
            if (s == null) return "";
            s = s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length > 24) s = s.Substring(0, 24);
            return s;
        }
    }

    /// <summary>
    /// 选一个分支（右键「放进我的分支」时弹）。列表最后一行永远是「＋ 新建分支…」，
    /// 返回值用 "+" 开头表示用户要新建，调用方自己去问名字。
    /// </summary>
    internal sealed class BranchPickForm : Form
    {
        private readonly ListBox box;
        public string Picked;                       // 选中的分支名，或 "＋ 新建分支…"

        public BranchPickForm(List<string> names)
        {
            Text = "放进我的分支";
            ClientSize = new Size(360, 340);
            Font = Theme.UI;
            BackColor = Theme.Bg;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;

            Label l = new Label();
            l.Text = "选一个分支（一组里能同时开好几个；文件不会被移动）";
            l.SetBounds(12, 12, 336, 20);
            Theme.Dress(l);
            Controls.Add(l);

            box = new ListBox();
            box.SetBounds(12, 36, 336, 250);
            box.Font = Theme.UI;
            box.BorderStyle = BorderStyle.FixedSingle;
            if (names != null) foreach (string n in names) box.Items.Add(n);
            box.Items.Add("＋ 新建分支…");
            if (box.Items.Count > 0) box.SelectedIndex = 0;
            box.DoubleClick += delegate { Accept(); };
            Controls.Add(box);

            FlatButton ok = new FlatButton();
            ok.Text = "确定"; ok.Primary = true; ok.SetBounds(180, 296, 80, Theme.H);
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);

            FlatButton no = new FlatButton();
            no.Text = "取消"; no.SetBounds(268, 296, 80, Theme.H);
            no.DialogResult = DialogResult.Cancel;
            Controls.Add(no);

            AcceptButton = ok; CancelButton = no;
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (DialogResult == DialogResult.OK && string.IsNullOrEmpty(Picked)) e.Cancel = true;
            };
        }

        private void Accept()
        {
            if (box.SelectedItem == null) return;
            Picked = box.SelectedItem.ToString();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

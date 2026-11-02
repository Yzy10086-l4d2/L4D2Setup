using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Installer
{
    // ---------- addonlist.txt 读写（游戏用它记录“哪些 MOD 开着”） ----------
    public static class AddonList
    {
        private static Regex LineRe = new Regex("^\\s*\"([^\"]+)\"\\s+\"([01])\"\\s*$", RegexOptions.Compiled);

        public static string FileOf(string gameRoot)
        {
            return Path.Combine(Path.Combine(gameRoot, "left4dead2"), "addonlist.txt");
        }

        public static string WriteOrdered(string gameRoot, List<string> order, Dictionary<string, bool> states)
        {
            string p = FileOf(gameRoot);
            string bak = p + ".装之前";
            if (File.Exists(p) && !File.Exists(bak)) File.Copy(p, bak, true);
            List<string> lines = new List<string>();
            if (File.Exists(p)) lines.AddRange(File.ReadAllLines(p, Encoding.Default));
            if (lines.Count == 0) { lines.Add("addons"); lines.Add("{"); lines.Add("}"); }
            int open = lines.FindIndex(delegate(string s) { return s.Trim() == "{"; });
            int close = lines.FindLastIndex(delegate(string s) { return s.Trim() == "}"; });
            if (open < 0 || close < 0 || close <= open) return Write(gameRoot, states);
            List<string> head = lines.GetRange(0, open + 1);
            List<string> tail = lines.GetRange(close, lines.Count - close);
            HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> body = new List<string>();
            foreach (string name in order)
            {
                bool on = states.ContainsKey(name) ? states[name] : true;
                body.Add("\t\"" + name + "\"\t\t\"" + (on ? "1" : "0") + "\"");
                written.Add(name);
            }
            foreach (string raw in lines.GetRange(open + 1, close - open - 1))
            {
                Match mm = LineRe.Match(raw);
                if (mm.Success && written.Contains(mm.Groups[1].Value)) continue;
                body.Add(raw);
            }
            List<string> all = new List<string>();
            all.AddRange(head); all.AddRange(body); all.AddRange(tail);
            File.WriteAllLines(p, all, Encoding.Default);
            return bak;
        }

        public static Dictionary<string, bool> Read(string gameRoot)
        {
            Dictionary<string, bool> d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            string p = FileOf(gameRoot);
            if (!File.Exists(p)) return d;
            foreach (string raw in File.ReadAllLines(p, Encoding.Default))
            {
                Match m = LineRe.Match(raw);
                if (m.Success) d[m.Groups[1].Value] = (m.Groups[2].Value == "1");
            }
            return d;
        }

        public static string Write(string gameRoot, Dictionary<string, bool> changes)
        {
            string p = FileOf(gameRoot);
            string bak = p + ".装之前";
            if (File.Exists(p))
            {
                if (!File.Exists(bak)) File.Copy(p, bak, true);
            }
            List<string> lines = new List<string>();
            if (File.Exists(p)) lines.AddRange(File.ReadAllLines(p, Encoding.Default));
            if (lines.Count == 0)
            {
                lines.Add("\"addons\"");
                lines.Add("{");
                lines.Add("}");
            }
            HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                Match m = LineRe.Match(lines[i]);
                if (!m.Success) continue;
                string name = m.Groups[1].Value;
                if (changes.ContainsKey(name))
                {
                    lines[i] = "\t\"" + name + "\"\t\t\"" + (changes[name] ? "1" : "0") + "\"";
                    done.Add(name);
                }
            }
            List<string> add = new List<string>();
            foreach (KeyValuePair<string, bool> kv in changes)
            {
                if (!done.Contains(kv.Key)) add.Add("\t\"" + kv.Key + "\"\t\t\"" + (kv.Value ? "1" : "0") + "\"");
            }
            if (add.Count > 0)
            {
                int close = lines.FindLastIndex(delegate(string s) { return s.Trim() == "}"; });
                if (close < 0) close = lines.Count;
                lines.InsertRange(close, add);
            }
            File.WriteAllLines(p, lines, Encoding.Default);
            return bak;
        }
    }

    // ---------- 方案预设 ----------
    public static class ProfileStore
    {
        public static string DirOf()
        {
            return Path.Combine(Core.LogDir, "方案");
        }

        public static List<string> ListProfiles()
        {
            List<string> names = new List<string>();
            try
            {
                string d = DirOf();
                if (!Directory.Exists(d)) return names;
                foreach (string f in Directory.GetFiles(d, "*.txt"))
                    names.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch { }
            names.Sort();
            return names;
        }

        public static void Save(string name, List<string> mods, bool dxvk, string plat)
        {
            string d = DirOf();
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# 方案 " + name + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Dxvk=" + (dxvk ? "1" : "0"));
            if (!string.IsNullOrEmpty(plat)) sb.AppendLine("Plat=" + plat);
            foreach (string m in mods) sb.AppendLine("Mod=" + m);
            File.WriteAllText(Path.Combine(d, name + ".txt"), sb.ToString(), new UTF8Encoding(true));
        }

        public static void Delete(string name)
        {
            string f = Path.Combine(DirOf(), name + ".txt");
            if (File.Exists(f)) File.Delete(f);
        }

        public static string Rename(string oldName, string newName)
        {
            string a = Path.Combine(DirOf(), oldName + ".txt");
            string b = Path.Combine(DirOf(), newName + ".txt");
            if (!File.Exists(a)) return "原方案不存在";
            if (File.Exists(b)) return "已经有叫这个名字的方案了";
            File.Move(a, b);
            return null;
        }

        public static List<string> Load(string name, out bool dxvk, out string plat)
        {
            dxvk = true;
            plat = "";
            List<string> mods = new List<string>();
            string f = Path.Combine(DirOf(), name + ".txt");
            if (!File.Exists(f)) return mods;
            foreach (string raw in File.ReadAllLines(f, Encoding.UTF8))
            {
                string s = raw.Trim();
                if (s.StartsWith("Dxvk=")) dxvk = (s.Substring(5) == "1");
                else if (s.StartsWith("Plat=")) plat = s.Substring(5);
                else if (s.StartsWith("Mod=")) mods.Add(s.Substring(4));
            }
            return mods;
        }
    }

    // ---------- 回收站（删除不真删，挪走） ----------
    public static class Bin
    {
        public static string Move(string gameRoot, string file)
        {
            string dir = Path.Combine(Path.Combine(gameRoot, "_回收站_l4d2"), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string dst = Path.Combine(dir, Path.GetFileName(file));
            int n = 1;
            while (File.Exists(dst))
            {
                dst = Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + "_" + n + Path.GetExtension(file));
                n++;
            }
            File.Move(file, dst);
            return dst;
        }
    }

    // ---------- 冲突提示（只做便宜的三类） ----------
    public static class ConflictCheck
    {
        public static List<string> Run(List<ModItem> mods, string gameRoot)
        {
            List<string> msgs = new List<string>();
            Dictionary<string, List<ModItem>> byName = new Dictionary<string, List<ModItem>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<long, List<ModItem>> bySize = new Dictionary<long, List<ModItem>>();
            int already = 0;
            string addons = Path.Combine(Path.Combine(gameRoot, "left4dead2"), "addons");
            foreach (ModItem m in mods)
            {
                if (!byName.ContainsKey(m.Name)) byName[m.Name] = new List<ModItem>();
                byName[m.Name].Add(m);
                if (!bySize.ContainsKey(m.Size)) bySize[m.Size] = new List<ModItem>();
                bySize[m.Size].Add(m);
                try { if (Directory.Exists(addons) && File.Exists(Path.Combine(addons, m.Name))) already++; } catch { }
            }
            foreach (KeyValuePair<string, List<ModItem>> kv in byName)
            {
                if (kv.Value.Count < 2) continue;
                List<string> cats = new List<string>();
                foreach (ModItem m in kv.Value) if (!cats.Contains(m.Category)) cats.Add(m.Category);
                msgs.Add("同名文件 " + kv.Value.Count + " 份: " + kv.Key + "（" + string.Join("、", cats.ToArray()) + "），铺进去会互相覆盖");
            }
            foreach (KeyValuePair<long, List<ModItem>> kv in bySize)
            {
                if (kv.Value.Count < 2 || kv.Key < 1048576L) continue;
                List<string> names = new List<string>();
                foreach (ModItem m in kv.Value) names.Add(m.Name);
                msgs.Add("体积完全相同（可能重复）: " + string.Join("、", names.ToArray()));
            }
            if (already > 0) msgs.Add("游戏 addons 里已存在 " + already + " 个同名文件（会被这次的覆盖）");
            return msgs;
        }

        // 深度检查：真正解包 VPK 目录表，看这些 MOD 之间有没有同路径文件
        public static List<string> Deep(List<ModItem> sel)
        {
            List<string> msgs = new List<string>();
            if (sel == null || sel.Count < 2) return msgs;
            Dictionary<string, List<string>> map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Stopwatch sw = Stopwatch.StartNew();
            foreach (ModItem m in sel)
            {
                foreach (string p in Vpk.Paths(m.FullPath, 300000))
                {
                    // 这些是每个 addon 都有的“身份证”，互相重叠属正常，不算冲突
                    if (p.EndsWith("/addoninfo.txt") || p == "addoninfo.txt") continue;
                    if (p.EndsWith("/addonimage.jpg") || p == "addonimage.jpg") continue;
                    if (p.EndsWith("/sound.cache") || p == "sound/sound.cache") continue;
                {
                    if (!map.ContainsKey(p)) { map[p] = new List<string>(); }
                    map[p].Add(m.Name);
                }
                }
            }
            sw.Stop();
            int overlap = 0;
            List<string> examples = new List<string>();
            List<string> mods = new List<string>();
            foreach (KeyValuePair<string, List<string>> kv in map)
            {
                if (kv.Value.Count < 2) continue;
                overlap++;
                if (examples.Count < 3) examples.Add(kv.Key + "（" + string.Join(" / ", kv.Value.ToArray()) + "）");
                foreach (string n in kv.Value) if (!mods.Contains(n)) mods.Add(n);
            }
            if (overlap > 0)
            {
                msgs.Add("这 " + sel.Count + " 个 MOD 之间有 " + overlap + " 个文件路径重叠，会互相覆盖");
                msgs.Add("涉及 " + mods.Count + " 个 MOD： " + string.Join("、", mods.ToArray()));
                foreach (string e in examples) msgs.Add("例: " + e);
            }
            Core.Log("INFO", "深度冲突检查用时 " + Math.Round(sw.Elapsed.TotalSeconds, 1) + " 秒");
            return msgs;
        }
    }

    // ---------- MOD 标签（Key=相对路径，Value=逗号分隔标签） ----------
    public static class ModTags
    {
        public static string FileOf()
        {
            return Path.Combine(Core.PkgDir, "mod标签.ini");
        }

        public static Dictionary<string, string> Load()
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string f = FileOf();
                if (!File.Exists(f)) return d;
                foreach (string raw in File.ReadAllLines(f, Encoding.UTF8))
                {
                    string s = raw.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;
                    int eq = s.IndexOf('=');
                    if (eq <= 0) continue;
                    d[s.Substring(0, eq).Trim()] = s.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return d;
        }

        public static void Save(Dictionary<string, string> d)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# MOD 标签：相对路径=标签1,标签2");
            foreach (KeyValuePair<string, string> kv in d)
                if (kv.Value.Trim().Length > 0) sb.AppendLine(kv.Key + "=" + kv.Value);
            File.WriteAllText(FileOf(), sb.ToString(), new UTF8Encoding(true));
        }

        public static string Get(Dictionary<string, string> d, string rel)
        {
            return d.ContainsKey(rel) ? d[rel] : "";
        }
    }

    // ---------- 按顺序写 addonlist（顺序=覆盖优先级） ----------    // ---------- VPK 目录表解析（只读文件表，不读内容） ----------
    public static class Vpk
    {
        public static List<string> Paths(string file, int maxEntries)
        {
            List<string> list = new List<string>();
            try
            {
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (fs.Length < 12) return list;
                    uint sig = br.ReadUInt32();
                    if (sig != 0x55AA1234) return list;
                    uint ver = br.ReadUInt32();
                    uint treeSize = br.ReadUInt32();
                    if (ver == 2)
                    {
                        br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
                    }
                    long treeEnd = fs.Position + treeSize;
                    if (treeEnd > fs.Length) treeEnd = fs.Length;
                    while (fs.Position < treeEnd)
                    {
                        string ext = ReadStr(br);
                        if (ext.Length == 0) break;
                        string path = ReadStr(br);
                        while (fs.Position < treeEnd)
                        {
                            string name = ReadStr(br);
                            if (name.Length == 0) break;
                            br.ReadUInt32();
                            br.ReadUInt16();
                            br.ReadUInt16();
                            br.ReadUInt32();
                            br.ReadUInt32();
                            br.ReadUInt16();
                            string full = ((path.Length > 0 ? path + "/" : "") + name + "." + ext).TrimStart('/');
                            list.Add(full.ToLowerInvariant());
                            if (list.Count >= maxEntries) return list;
                        }
                        // 目录块之间可能夹着多个 0 字节，先吃掉它们再读下一个 ext
                        while (fs.Position < treeEnd)
                        {
                            long save = fs.Position;
                            int peek = br.ReadByte();
                            if (peek != 0) { fs.Position = save; break; }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        private static string ReadStr(BinaryReader br)
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                int b = br.ReadByte();
                if (b == 0) break;
                sb.Append((char)b);
                if (sb.Length > 512) break;
            }
            return sb.ToString();
        }
    }

    // ---------- 「已装 MOD 开关」窗口 ----------
    public class AddonSwitchForm : Form
    {
        private readonly string gameRoot;
        private ListView lv;
        private Label lbl;
        private Dictionary<string, bool> state = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public AddonSwitchForm(string root)
        {
            gameRoot = root;
            Text = "已装 MOD 开关 —— 勾上=进游戏时启用";
            ClientSize = new Size(560, 480);
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;

            state = AddonList.Read(gameRoot);
            lv = new ListView();
            lv.SetBounds(12, 12, 536, 380);
            lv.View = View.Details;
            lv.CheckBoxes = true;
            lv.FullRowSelect = true;
            lv.Columns.Add("MOD 文件", 380);
            lv.Columns.Add("状态", 120);
            Controls.Add(lv);

            Button up = new Button(); up.Text = "上移 ↑"; up.SetBounds(12, 360, 90, 28); up.Click += delegate { MoveItem(-1); }; Controls.Add(up);
            Button dn = new Button(); dn.Text = "下移 ↓"; dn.SetBounds(108, 360, 90, 28); dn.Click += delegate { MoveItem(1); }; Controls.Add(dn);
            Label tip = new Label(); tip.Text = "顺序 = 覆盖优先级（下面的覆盖上面的）"; tip.SetBounds(210, 364, 330, 20); Controls.Add(tip);
            Button all = new Button(); all.Text = "全开"; all.SetBounds(12, 402, 80, 30);
            all.Click += delegate { SetAll(true); }; Controls.Add(all);
            Button none = new Button(); none.Text = "全关"; none.SetBounds(98, 402, 80, 30);
            none.Click += delegate { SetAll(false); }; Controls.Add(none);
            Button collect = new Button(); collect.Text = "收回新 MOD"; collect.SetBounds(184, 402, 110, 30);
            collect.Click += delegate { Collect(); }; Controls.Add(collect);
            Button ok = new Button(); ok.Text = "写入并关闭"; ok.SetBounds(410, 402, 138, 30);
            ok.Click += delegate { Apply(); }; Controls.Add(ok);
            lbl = new Label(); lbl.SetBounds(300, 408, 100, 20); lbl.Text = ""; Controls.Add(lbl);

            LoadItems();
        }

        private void LoadItems()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            string addons = Path.Combine(Path.Combine(gameRoot, "left4dead2"), "addons");
            if (!Directory.Exists(addons)) { lv.EndUpdate(); lbl.Text = "游戏里没有 addons 目录"; return; }
            string[] files = Directory.GetFiles(addons, "*.vpk");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                bool on = false;
                if (state.ContainsKey(name)) on = state[name];
                ListViewItem li = new ListViewItem(name);
                li.SubItems.Add(on ? "启用" : "关闭");
                li.Checked = on;
                li.Tag = name;
                lv.Items.Add(li);
            }
            lv.EndUpdate();
            lbl.Text = "共 " + files.Length + " 个已装 MOD";
        }

        private void MoveItem(int dir)
        {
            if (lv.SelectedIndices.Count == 0) return;
            int i = lv.SelectedIndices[0];
            int j = i + dir;
            if (j < 0 || j >= lv.Items.Count) return;
            ListViewItem it = lv.Items[i];
            lv.Items.RemoveAt(i);
            lv.Items.Insert(j, it);
            it.Selected = true;
            lv.Focus();
        }

        private void SetAll(bool on)
        {
            lv.BeginUpdate();
            foreach (ListViewItem li in lv.Items)
            {
                li.Checked = on;
                li.SubItems[1].Text = on ? "启用" : "关闭";
            }
            lv.EndUpdate();
        }

        private void Collect()
        {
            List<string> got = Work.CollectNewMods(gameRoot);
            if (got.Count == 0) { MessageBox.Show("没有新 MOD：游戏里的都在 U 盘库里。"); return; }
            MessageBox.Show("已收回 " + got.Count + " 个到 U 盘 addons回收 目录：\r\n" + string.Join("\r\n", got.ToArray()));
        }

        private void Apply()
        {
            Dictionary<string, bool> changes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            List<string> order = new List<string>();
            foreach (ListViewItem li in lv.Items) { changes[(string)li.Tag] = li.Checked; order.Add((string)li.Tag); }
            try
            {
                string bak = AddonList.WriteOrdered(gameRoot, order, changes);
                int on = 0;
                foreach (KeyValuePair<string, bool> kv in changes) if (kv.Value) on++;
                Core.Log("OK", "addonlist 已更新：" + on + " 个启用 / " + (changes.Count - on) + " 个关闭（备份 " + Path.GetFileName(bak) + "）");
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("写入失败: " + ex.Message);
            }
        }
    }
}
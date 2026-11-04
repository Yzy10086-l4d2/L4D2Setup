using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Installer
{
    // 压缩包里的一个 vpk 条目
    public class ArcEntry
    {
        public string Inner;
        public long Size;
        public string Crc;      // rar 的 CRC32；zip 及旧缓存里为空（判重时用得到）
    }

    // 列表里的一行：一个 mod，可能由多个 vpk 组成
    public class ModGroup
    {
        public string Key;
        public string Name;
        public string Cat = "杂项";     // 大类：地图/人物/枪械/药品/杂项/喷漆
        public string Sub = "其他";     // 细分：AWP / 一代 · 佐伊Zoey / 启示录系列
        public List<ModItem> Parts = new List<ModItem>();
        public long Size;
        public string Subject = "";      // 改了什么（取主体部件的识别结果）
        public string SubjectLevel = "";

        public bool IsSet { get { return Parts.Count > 1; } }

        public ModItem Main
        {
            get
            {
                foreach (ModItem it in Parts) if (it.Role == "主体") return it;
                return Parts.Count > 0 ? Parts[0] : null;
            }
        }

        public string Where { get { return Cat + " · " + Sub; } }
    }

    /// <summary>
    /// 把 U 盘上的东西整理成「一个 mod 一行」：
    /// 独立 vpk、压缩包里的 vpk，统一分类，再把同一个 mod 的多个 vpk 聚成一套。
    /// </summary>
    public static class Catalog
    {
        public const string CAT_MAP = "地图";
        public const string CAT_CHAR = "人物";
        public const string CAT_GUN = "枪械";
        public const string CAT_MED = "药品";
        public const string CAT_MISC = "杂项";
        public const string CAT_SPRAY = "喷漆";

        // 大类的固定顺序，界面上照这个排
        public static readonly string[] Cats = new string[] { CAT_MAP, CAT_CHAR, CAT_GUN, CAT_MED, CAT_SPRAY, CAT_MISC };

        public static List<ModGroup> Groups = new List<ModGroup>();
        public static Dictionary<string, ModGroup> GroupByKey = new Dictionary<string, ModGroup>(StringComparer.OrdinalIgnoreCase);

        // ---------------------------------------------------------------- 压缩包

        static string _rarExe;
        public static string RarExe()
        {
            if (_rarExe != null) return _rarExe;
            List<string> cand = new List<string>();
            cand.Add(@"C://Program Files//WinRAR//Rar.exe");
            cand.Add(@"C://Program Files (x86)//WinRAR//Rar.exe");
            // 注册表里的 exe64 是 WinRAR.exe（图形版），拿它跑命令行会弹窗挂死 —— 只借它所在的目录
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinRAR"))
                {
                    if (k != null)
                        foreach (string v in new string[] { "exe64", "exe32" })
                        {
                            object o = k.GetValue(v);
                            if (o == null) continue;
                            string d = Path.GetDirectoryName(o.ToString());
                            if (!string.IsNullOrEmpty(d)) cand.Add(Path.Combine(d, "Rar.exe"));
                        }
                }
            }
            catch { }
            try { if (Core.UsbRoot != null) cand.Add(Path.Combine(Core.UsbRoot, @"_装机工具\Rar.exe")); } catch { }

            foreach (string c in cand)
            {
                try
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    if (!string.Equals(Path.GetFileName(c), "Rar.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.Exists(c)) { _rarExe = c; return c; }
                }
                catch { }
            }
            _rarExe = "";
            return "";
        }

        public static bool IsArchive(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            return e == ".rar" || e == ".zip" || e == ".7z";
        }

        /// <summary>列出压缩包里的 vpk（带原始大小）。zip 走 .NET，rar 走 WinRAR 命令行。</summary>
        public static List<ArcEntry> ListArchive(string arcPath, out string err)
        {
            err = null;
            List<ArcEntry> list = new List<ArcEntry>();
            string ext = Path.GetExtension(arcPath).ToLowerInvariant();
            if (ext == ".zip")
            {
                try
                {
                    using (System.IO.Compression.ZipArchive za = System.IO.Compression.ZipFile.OpenRead(arcPath))
                        foreach (System.IO.Compression.ZipArchiveEntry e in za.Entries)
                            if (e.FullName.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
                                list.Add(new ArcEntry { Inner = e.FullName.Replace('/', '\\'), Size = e.Length });
                }
                catch (Exception ex) { err = ex.Message; }
                return list;
            }
            if (ext == ".7z") { err = "暂不支持 7z"; return list; }

            string rar = RarExe();
            if (rar.Length == 0) { err = "找不到 WinRAR（Rar.exe）"; return list; }
            string text;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(rar, "vt -idc \"" + arcPath + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                try { psi.StandardOutputEncoding = Encoding.GetEncoding(936); } catch { }   // WinRAR 输出是 GBK
                using (Process p = Process.Start(psi))
                {
                    // stderr 必须一起读掉：WinRAR 对某些包会刷一堆警告，缓冲满了两个进程一起卡死
                    string devNull = null;
                    System.Threading.Thread th = new System.Threading.Thread(delegate()
                    {
                        try { devNull = p.StandardError.ReadToEnd(); } catch { }
                    });
                    th.IsBackground = true;
                    th.Start();
                    text = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } }
                    th.Join(2000);
                }
            }
            catch (Exception ex) { err = ex.Message; return list; }

            // vt 的字段名跟 WinRAR 语言走：中文版「名称/大小」，英文版 Name/Size
            string curName = null;
            ArcEntry last = null;      // CRC32 在 大小 之后才出现，先记着刚加的那条
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r').Trim();
                string name = Field(line, "名称:", "Name:");
                // 每条记录一开头就把 last 断掉：不然紧跟在 vpk 后面的 jpg 会把它的 CRC 写进 vpk 那条
                if (name != null) { curName = name; last = null; continue; }
                string crc = Field(line, "CRC32:", "CRC32:");
                if (crc != null) { if (last != null) last.Crc = crc; continue; }
                string size = Field(line, "大小:", "Size:");
                if (size != null && curName != null)
                {
                    if (curName.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
                    {
                        long n = 0;
                        long.TryParse(size, out n);
                        last = new ArcEntry { Inner = curName.Replace('/', '\\'), Size = n };
                        list.Add(last);
                    }
                    curName = null;
                }
            }
            if (list.Count == 0 && err == null) err = "包里没有 vpk";
            return list;
        }

        private static string Field(string line, string cn, string en)
        {
            if (line.StartsWith(cn, StringComparison.Ordinal)) return line.Substring(cn.Length).Trim();
            if (line.StartsWith(en, StringComparison.OrdinalIgnoreCase)) return line.Substring(en.Length).Trim();
            return null;
        }

        /// <summary>压缩包清单缓存：容器大小+时间没变就不重复调 Rar.exe。</summary>
        static Dictionary<string, List<ArcEntry>> _arcCache;
        static string CachePath { get { return Path.Combine(Core.LogDir, "archive-cache-v1.tsv"); } }

        static void LoadArcCache()
        {
            if (_arcCache != null) return;
            _arcCache = new Dictionary<string, List<ArcEntry>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(CachePath)) return;
                foreach (string raw in File.ReadAllLines(CachePath, Encoding.UTF8))
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Length == 0) continue;
                    string[] p = line.Split('\t');
                    if (p.Length < 4) continue;
                    string sig = p[1] + "|" + p[2];
                    List<ArcEntry> l;
                    if (!_arcCache.TryGetValue(sig, out l)) { l = new List<ArcEntry>(); _arcCache[sig] = l; }
                    long n = 0; long.TryParse(p[3], out n);
                    l.Add(new ArcEntry { Inner = p[0], Size = n });
                }
            }
            catch { }
        }

        static void SaveArcCache()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, List<ArcEntry>> kv in _arcCache)
                {
                    string[] sig = kv.Key.Split('|');
                    foreach (ArcEntry e in kv.Value)
                        sb.Append(e.Inner).Append('\t').Append(sig[0]).Append('\t').Append(sig.Length > 1 ? sig[1] : "0").Append('\t').Append(e.Size).Append("\r\n");
                }
                File.WriteAllText(CachePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        static List<ArcEntry> ListArchiveCached(string arcPath, out bool hit)
        {
            hit = false;
            LoadArcCache();
            string sig;
            try
            {
                FileInfo fi = new FileInfo(arcPath);
                sig = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            }
            catch { sig = "0|0"; }
            List<ArcEntry> l;
            if (_arcCache.TryGetValue(sig, out l)) { hit = true; return l; }
            string err;
            l = ListArchive(arcPath, out err);
            if (err != null) Core.Log("WARN", "读压缩包失败 " + Path.GetFileName(arcPath) + ": " + err);
            if (l.Count > 0 || err == null) _arcCache[sig] = l;
            return l;
        }

        // ---------------------------------------------------------------- 扫描

        // ---------- 压缩包内容分类缓存（rar 只能靠 Rar.exe 流式读前缀，很慢，必须缓存） ----------
        static Dictionary<string, string> _arcContent;                 // key = rel|inner|size|ticks -> "cat|sub"
        static readonly Dictionary<string, string> _arcSigCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string ArcContentPath { get { return Path.Combine(Core.LogDir, "archive-content-v1.tsv"); } }

        static string ArcSig(string fullPath)
        {
            string v;
            if (_arcSigCache.TryGetValue(fullPath, out v)) return v;
            try
            {
                FileInfo fi = new FileInfo(fullPath);
                v = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            }
            catch { v = "0|0"; }
            _arcSigCache[fullPath] = v;
            return v;
        }

        static string ArcKey(string fullPath, string inner)
        {
            return RelOf(fullPath) + "|" + inner + "|" + ArcSig(fullPath);
        }

        static void LoadArcContent()
        {
            if (_arcContent != null) return;
            _arcContent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(ArcContentPath)) return;
                foreach (string raw in File.ReadAllLines(ArcContentPath, Encoding.UTF8))
                {
                    string[] p = raw.TrimEnd((char)13).Split((char)9);
                    if (p.Length >= 4) _arcContent[p[0] + "|" + p[1] + "|" + p[2]] = p[3];
                }
            }
            catch { }
        }

        public static void SaveArcContent()
        {
            try
            {
                if (_arcContent == null) return;
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in _arcContent)
                {
                    string[] k = kv.Key.Split('|');
                    if (k.Length < 4) continue;
                    int n = k.Length;
                    string ticks = k[n - 1];
                    string size = k[n - 2];
                    string rel = k[0];
                    string inner = string.Join("|", k, 1, n - 3);
                    sb.Append(rel).Append((char)9).Append(inner).Append((char)9).Append(size).Append('|').Append(ticks).Append((char)9).Append(kv.Value).Append(Environment.NewLine);
                }
                File.WriteAllText(ArcContentPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        static bool TryArcContent(string fullPath, string inner, out string cat, out string sub)
        {
            cat = ""; sub = "";
            LoadArcContent();
            string v;
            if (!_arcContent.TryGetValue(ArcKey(fullPath, inner), out v)) return false;
            int bar = v.IndexOf('|');
            if (bar <= 0) return false;
            cat = v.Substring(0, bar);
            sub = v.Substring(bar + 1);
            return cat.Length > 0;
        }

        /// <summary>把所有还没缓存的 rar 逐个探一遍（只读前几百 KB）。返回新识别成功的数量。</summary>
        public static int ProbeArchives(Action<string> progress)
        {
            LoadArcContent();
            int done = 0;
            try
            {
                foreach (string dir in Directory.GetDirectories(Core.ModRoot))
                {
                    foreach (string arc in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        if (!arc.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)) continue;
                        string rel = RelOf(arc);
                        string sig = ArcSig(arc);
                        bool hit;
                        List<ArcEntry> list = ListArchiveCached(arc, out hit);
                        foreach (ArcEntry e in list)
                        {
                            string key = rel + "|" + e.Inner + "|" + sig;
                            if (_arcContent.ContainsKey(key)) continue;
                            if (progress != null) progress("识别 " + Path.GetFileName(arc) + " → " + Path.GetFileName(e.Inner));
                            string c, s;
                            if (ModIdentify.ClassifyByContentRar(arc, e.Inner, out c, out s))
                                _arcContent[key] = c + "|" + s;
                            else
                                _arcContent[key] = "|";
                            done++;
                        }
                    }
                }
            }
            catch { }
            SaveArcContent();
            return done;
        }

        public static void ScanMods()
        {
            Core.ModList.Clear();
            Core.ByRel.Clear();
            Core.DirCount.Clear();
            Groups.Clear();
            GroupByKey.Clear();
            try
            {
                if (!Directory.Exists(Core.ModRoot)) { Core.Log("WARN", "MOD 根目录不存在: " + Core.ModRoot); return; }

                Stopwatch sw = Stopwatch.StartNew();
                List<string> vpkFiles = new List<string>();
                List<string> arcs = new List<string>();
                foreach (string dir in Directory.GetDirectories(Core.ModRoot))
                {
                    string cat = Path.GetFileName(dir);
                    if (!cat.StartsWith("addons", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (string f in Directory.GetFiles(dir, "*.vpk", SearchOption.AllDirectories)) vpkFiles.Add(f);
                    foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        if (IsArchive(f)) arcs.Add(f);
                }

                foreach (string f in vpkFiles) AddVpk(f);

                int arcHit = 0, arcMiss = 0;
                foreach (string a in arcs)
                {
                    bool hit;
                    List<ArcEntry> list = ListArchiveCached(a, out hit);
                    if (hit) arcHit++; else arcMiss++;
                    string rel = RelOf(a);
                    foreach (ArcEntry e in list)
                    {
                        ModItem it = new ModItem();
                        it.Name = Path.GetFileName(e.Inner);
                        it.Container = rel;
                        it.Inner = e.Inner;
                        it.RelPath = rel + "\\" + e.Inner;
                        it.FullPath = a;
                        it.Size = e.Size;
                        it.Category = Path.GetFileName(Path.GetDirectoryName(a));   // 兼容旧字段
                        it.Key = "#arc#" + rel + "#" + e.Inner;
                        Classify(it);
                        Add(it);
                    }
                }
                Core.Log("OK", "扫库: 独立 vpk " + vpkFiles.Count + " 个, 压缩包 " + arcs.Count + " 个（缓存命中 " + arcHit + "/" + (arcHit + arcMiss) + "），共 " + Core.ModList.Count + " 个 vpk，" + sw.ElapsedMilliseconds + " ms");
            }
            catch (Exception ex) { Core.Log("FAIL", "扫 MOD 出错: " + ex.Message); }
            ModIdentify.SaveCache();
            SaveArcCache();
        }

        static string RelOf(string full)
        {
            try { return full.Substring(Core.ModRoot.Length).TrimStart('\\'); } catch { return full; }
        }

        static void AddVpk(string f)
        {
            ModItem it = new ModItem();
            it.Name = Path.GetFileName(f);
            it.FullPath = f;
            it.RelPath = RelOf(f);
            it.Key = it.RelPath;
            it.Category = Core.RelDir(it.RelPath).Split('\\')[0];
            try { it.Size = new FileInfo(f).Length; } catch { it.Size = 0; }
            Classify(it);
            ModIdentify.Result ir = ModIdentify.AnalyzeCached(it.RelPath, f);
            it.Subject = ir.Subject;
            it.SubjectLevel = ir.Level;
            it.SubjectKey = ir.Key;
            it.Kinds = ir.Kinds;
            Add(it);
        }

        static void Add(ModItem it)
        {
            Core.ModList.Add(it);
            Core.ByRel[it.Key] = it;
            string rd = Core.RelDir(it.RelPath);
            if (rd.Length > 0)
            {
                Core.BumpDir(rd);
                int bs = rd.IndexOf('\\');
                if (bs > 0) Core.BumpDir(rd.Substring(0, bs));
            }
        }

        // ---------------------------------------------------------------- 分类

        /// <summary>一个 mod 归到哪：先看它放在哪（确定性强），再看 vpk 里改了什么（兜底）。</summary>
        public static void Classify(ModItem it)
        {
            // 0) 内容优先：有把握才用（散装 vpk 直接读；zip 里的读目录表前缀）
            string ccat = "", csub = "";
            bool hitContent = false;
            try
            {
                if (string.IsNullOrEmpty(it.Inner))
                {
                    if (it.FullPath != null && it.FullPath.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
                        hitContent = ModIdentify.ClassifyByContent(it.FullPath, out ccat, out csub);
                }
                else if (it.FullPath != null && it.FullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    hitContent = ModIdentify.ClassifyByContentZip(it.FullPath, it.Inner, out ccat, out csub);
                }
                else if (it.FullPath != null && it.FullPath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                {
                    hitContent = TryArcContent(it.FullPath, it.Inner, out ccat, out csub);
                }
            }
            catch { hitContent = false; }
            if (hitContent)
            {
                it.Cat = ccat;
                it.Sub = csub;
                it.Category = it.Cat;
                return;
            }

            string[] segs = DirSegs(it);

            // 地图只看「包放在哪个系列目录下」，不看包里面套的目录名，
            // 否则每个地图包都会变成自己的一个系列（Dry County、五号城市…），下拉里能列出一百项。
            int iMap = IndexOfContain(segs, "三方地图");
            if (iMap >= 0)
            {
                string[] cs = ContainerSegs(it);
                int cm = IndexOfContain(cs, "三方地图");
                it.Cat = CAT_MAP;
                it.Sub = (cm >= 0 && cm + 1 < cs.Length) ? cs[cm + 1] : "单张地图";
                it.Category = it.Cat;
                return;
            }

            int iChar = IndexOfContain(segs, "人物");
            if (iChar >= 0)
            {
                it.Cat = CAT_CHAR;
                string gen = "";
                int g = -1;
                for (int i = 0; i < segs.Length; i++)
                {
                    if (segs[i].IndexOf("一代", StringComparison.Ordinal) >= 0) { gen = "一代"; g = i; break; }
                    if (segs[i].IndexOf("二代", StringComparison.Ordinal) >= 0) { gen = "二代"; g = i; break; }
                }
                if (gen.Length == 0) it.Sub = "其他";
                else if (g + 1 < segs.Length) it.Sub = gen + " · " + segs[g + 1];
                else it.Sub = gen;
                it.Category = it.Cat;
                return;
            }

            if (IndexOfContain(segs, "喷漆") >= 0)
            {
                it.Cat = CAT_SPRAY; it.Sub = "喷漆"; it.Category = it.Cat;
                return;
            }

            int iMed = IndexOfContain(segs, "医疗物品");
            if (iMed >= 0)
            {
                it.Cat = CAT_MED;
                string s = (iMed + 1 < segs.Length) ? segs[iMed + 1] : "";
                if (s == "包") it.Sub = "医疗包";
                else if (s == "药") it.Sub = "止痛药";
                else if (s == "针") it.Sub = "肾上腺素";
                else it.Sub = "其他";
                it.Category = it.Cat;
                return;
            }

            int iStore = IndexOfContain(segs, "物品储藏库");
            if (iStore >= 0)
            {
                string s = (iStore + 1 < segs.Length) ? segs[iStore + 1] : "";
                if (s.Length == 0) { GuessBySubject(it); return; }
                if (s == "改视角") { it.Cat = CAT_MISC; it.Sub = "改视角"; }
                else if (s.IndexOf("火光", StringComparison.Ordinal) >= 0) { it.Cat = CAT_MISC; it.Sub = "火光"; }
                else if (s.IndexOf("杂项", StringComparison.Ordinal) >= 0) { it.Cat = CAT_MISC; it.Sub = "特效杂项"; }
                else { it.Cat = CAT_GUN; it.Sub = s; }
                it.Category = it.Cat;
                return;
            }

            GuessBySubject(it);
        }

        // 路径里没有可用信息时，退回看识别结果
        private static void GuessBySubject(ModItem it)
        {
            string s = (it.Subject ?? "") + " " + (it.SubjectKey ?? "");
            it.Cat = CAT_MISC; it.Sub = "其他";
            if (ContainsAny(s, "医疗包", "medkit", "止痛药", "pill", "肾上腺素", "adrenaline", "defib"))
            { it.Cat = CAT_MED; it.Sub = "医疗包"; }
            else if (ContainsAny(s, "武士刀", "katana", "斧", "锤", "砍刀", "吉他", "球棒", "machete", "近战"))
            { it.Cat = CAT_GUN; it.Sub = "近战"; }
            else if (ContainsAny(s, "步枪", "冲锋枪", "霰弹枪", "狙击", "手枪", "机枪", "rifle", "smg", "shotgun", "sniper", "pistol", "枪"))
            { it.Cat = CAT_GUN; it.Sub = "其他枪"; }
            it.Category = it.Cat;
        }

        private static bool ContainsAny(string s, params string[] keys)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (string k in keys) if (s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static int IndexOfContain(string[] segs, string key)
        {
            for (int i = 0; i < segs.Length; i++) if (segs[i].IndexOf(key, StringComparison.Ordinal) >= 0) return i;
            return -1;
        }

        /// <summary>只看容器（U 盘上那个文件）所在目录，不含压缩包内部的目录。</summary>
        static string[] ContainerSegs(ModItem it)
        {
            List<string> segs = new List<string>();
            string p = it.IsArchive ? it.Container : it.RelPath;
            string[] a = p.Split('\\');
            for (int i = 0; i < a.Length - 1; i++) if (a[i].Length > 0) segs.Add(a[i]);
            return segs.ToArray();
        }

        /// <summary>这个 mod 落在哪些目录里（独立 vpk 看自己，压缩包看 容器目录 + 包内目录）。</summary>
        static string[] DirSegs(ModItem it)
        {
            List<string> segs = new List<string>();
            string p = it.IsArchive ? it.Container : it.RelPath;
            string[] a = p.Split('\\');
            for (int i = 0; i < a.Length - 1; i++) if (a[i].Length > 0) segs.Add(a[i]);
            if (it.IsArchive)
            {
                string[] b = it.Inner.Split('\\');
                for (int i = 0; i < b.Length - 1; i++) if (b[i].Length > 0) segs.Add(b[i]);
            }
            return segs.ToArray();
        }

        // ---------------------------------------------------------------- 套装

        /// <summary>把 vpk 聚成「一个 mod 一行」。</summary>
        public static void BuildGroups()
        {
            Groups.Clear();
            GroupByKey.Clear();
            Dictionary<string, List<ModItem>> buckets = new Dictionary<string, List<ModItem>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModItem it in Core.ModList)
            {
                string key, name;
                BucketOf(it, out key, out name);
                if (!buckets.ContainsKey(key)) { buckets[key] = new List<ModItem>(); names[key] = name; }
                buckets[key].Add(it);
            }
            foreach (KeyValuePair<string, List<ModItem>> kv in buckets)
                foreach (List<ModItem> c in Cluster(kv.Value, names[kv.Key]))
                    MakeGroup(c);

            Groups.Sort(delegate(ModGroup a, ModGroup b)
            {
                int x = Array.IndexOf(Cats, a.Cat), y = Array.IndexOf(Cats, b.Cat);
                if (x < 0) x = 99; if (y < 0) y = 99;
                if (x != y) return x.CompareTo(y);
                int r = string.Compare(a.Sub, b.Sub, StringComparison.CurrentCulture);
                if (r != 0) return r;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
            });
        }

        static void BucketOf(ModItem it, out string key, out string name)
        {
            if (!it.IsArchive)
            {
                string dir = Core.RelDir(it.RelPath);
                key = dir;
                name = LastSeg(dir);
                return;
            }
            int i = it.Inner.LastIndexOf('\\');
            string innerDir = i > 0 ? it.Inner.Substring(0, i) : "";
            key = it.Container + "\0" + innerDir;
            name = innerDir.Length > 0 ? LastSeg(innerDir) : Path.GetFileNameWithoutExtension(it.Container);
        }

        static string LastSeg(string p)
        {
            int i = p.LastIndexOf('\\');
            return i >= 0 ? p.Substring(i + 1) : p;
        }

        /// <summary>同一个桶里怎么切：目录名本身像 mod 名 → 整桶一个；否则按名字像不像两两合并。</summary>
        static List<List<ModItem>> Cluster(List<ModItem> items, string bucketName)
        {
            List<List<ModItem>> res = new List<List<ModItem>>();
            if (items.Count == 0) return res;
            if (items.Count == 1) { res.Add(items); return res; }

            string bn = Norm(bucketName);
            if (bn.Length >= 3)
                foreach (ModItem it in items)
                    if (Similar(bn, Norm(Path.GetFileNameWithoutExtension(it.Name))))
                    { res.Add(items); return res; }

            int n = items.Count;
            int[] p = new int[n];
            for (int i = 0; i < n; i++) p[i] = i;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    string a = Norm(Path.GetFileNameWithoutExtension(items[i].Name));
                    string b = Norm(Path.GetFileNameWithoutExtension(items[j].Name));
                    if (Similar(a, b)) Union(p, i, j);
                }
            Dictionary<int, List<ModItem>> map = new Dictionary<int, List<ModItem>>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(p, i);
                if (!map.ContainsKey(r)) map[r] = new List<ModItem>();
                map[r].Add(items[i]);
            }
            foreach (KeyValuePair<int, List<ModItem>> kv in map) res.Add(kv.Value);
            return res;
        }

        static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }
        static void Union(int[] p, int a, int b) { a = Find(p, a); b = Find(p, b); if (a != b) p[b] = a; }

        /// <summary>两个名字是不是同一个 mod 的不同部件（编号、"partN"、"(2)"、颜色后缀都算）。</summary>
        static bool Similar(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            string s = a.Length <= b.Length ? a : b;
            string l = a.Length <= b.Length ? b : a;
            if (s.Length >= 6 && l.StartsWith(s, StringComparison.Ordinal)) return true;
            if (s.Length >= 8 && l.IndexOf(s, StringComparison.Ordinal) >= 0) return true;
            int lcp = 0;
            while (lcp < s.Length && s[lcp] == l[lcp]) lcp++;
            return lcp >= 8 && lcp * 10 >= s.Length * 6;
        }

        /// <summary>名字归一化：去掉各种编号，只留下「这是个什么东西」。</summary>
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"^[\s\(\[]*\d+[\s\)\]]*[\.\-_]?\s*", "");      // 1 xxx / 01. xxx
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*[\(\（]\s*\d+\s*[\)\）]\s*$", "");           // xxx (2)
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[\s_\-]*part\s*\d+$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<=[\u4e00-\u9fa5])\d+$", "");                // 冷锋1
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[\s_\-]+\d+$", "");                            // xxx -1
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[\s\.\-_\u3000]+", "");
            return s.ToLowerInvariant();
        }

        static void MakeGroup(List<ModItem> parts)
        {
            ModGroup g = new ModGroup();
            g.Parts = parts;
            g.Key = parts[0].Key;
            g.Cat = parts[0].Cat;
            g.Sub = parts[0].Sub;

            foreach (ModItem it in parts)
            {
                it.GroupKey = g.Key;
                it.Role = RoleOf(it.Name);
                g.Size += it.Size;
            }
            // 主体排最前，其次必装、选配、可选
            parts.Sort(delegate(ModItem a, ModItem b)
            {
                int x = RoleRank(a.Role), y = RoleRank(b.Role);
                if (x != y) return x.CompareTo(y);
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
            });

            string baseName;
            if (parts.Count == 1) baseName = CleanName(parts[0].Name);
            else
            {
                string bn = Norm(LastSeg(Core.RelDir(parts[0].RelPath)));
                baseName = (bn.Length > 0 && Similar(bn, Norm(Path.GetFileNameWithoutExtension(parts[0].Name))))
                    ? CleanName(LastSeg(Core.RelDir(parts[0].RelPath)))
                    : CleanName(parts[0].Name);
            }
            g.Name = baseName.Length > 0 ? baseName : Path.GetFileNameWithoutExtension(parts[0].Name);

            ModItem main = g.Main;
            if (main != null) { g.Subject = main.Subject; g.SubjectLevel = main.SubjectLevel; }

            Groups.Add(g);
            GroupByKey[g.Key] = g;
        }

        static int RoleRank(string role)
        {
            if (role == "主体") return 0;
            if (role == "必装") return 1;
            if (role == "选配") return 2;
            return 3;
        }

        /// <summary>部件角色：主体 / 必装 / 选配（颜色这种互斥的）/ 可选。</summary>
        public static string RoleOf(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (ContainsAny(n, "主体", "主文件", "主件", "main", "core")) return "主体";
            if (ContainsAny(n, "颜色", "配色", "变体", "α", "花纹", "color")) return "选配";
            if (ContainsAny(n, "可选", "选装", "影藏", "隐藏", "配件", "附加", "模块", "扩展", "extra", "optional")) return "可选";
            return "必装";
        }

        /// <summary>显示用的干净名字：去扩展名、去开头编号、去结尾 (2)。</summary>
        public static string CleanName(string fileName)
        {
            string s = Path.GetFileNameWithoutExtension(fileName);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"^[\s\(\[]*\d+[\s\)\]]*[\.\-_]?\s*", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*[\(\（]\s*\d+\s*[\)\）]\s*$", "");
            return s.Trim();
        }

        /// <summary>勾这一行时默认装哪些部件：主体 + 必装，选配只取第一个，可选不装。</summary>
        public static List<ModItem> DefaultParts(ModGroup g)
        {
            List<ModItem> list = new List<ModItem>();
            bool picked = false;
            foreach (ModItem it in g.Parts)
            {
                if (it.Role == "可选") continue;
                if (it.Role == "选配")
                {
                    if (!picked) { list.Add(it); picked = true; }
                    continue;
                }
                list.Add(it);
            }
            if (list.Count == 0 && g.Parts.Count > 0) list.Add(g.Parts[0]);
            return list;
        }
    }
}

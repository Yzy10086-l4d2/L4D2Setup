using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Installer
{
    public class ModItem
    {
        public string Category;
        public string Name;
        public long Size;
        public string RelPath;
        public string FullPath;
        public string Type;
        public string TypeDetail;
    }

    public class Config
    {
        public string GameRoot = "";
        public string LastRun = "";
        public bool L4N = true;
        public bool Skeeto = true;
        public bool AssetPack = true;
        public bool Mqcam = true;
        public bool Dxvk = true;
        public bool Launch = true;
        public string LaunchOptions = "-novid -high";
        public bool CfgFile = true;
        public List<string> Recent = new List<string>();
        public List<string> Mods = new List<string>();
    }

    public static class Core
    {
        public static string Here;
        public static string UsbRoot;
        public static string ModRoot;
        public static string PkgDir;
        public static string CfgPath;
        public static string LogDir;
        public static string LogFile;
        public static Config Cfg = new Config();
        public static List<ModItem> ModList = new List<ModItem>();
        public static Action<string> Sink;

        public static void Init()
        {
            Here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            PkgDir = ResolvePkgDir(Here);
            UsbRoot = WalkUp(Here);
            ModRoot = Path.Combine(Path.Combine(UsbRoot, "游戏内容"), "求生之路2各项MOD");
            CfgPath = Path.Combine(PkgDir, "装机台.配置.ini");
            LogDir = Path.Combine(PkgDir, "_装机记录");
            if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
            LogFile = Path.Combine(LogDir, "log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            LoadConfig();
        }

        // 包目录：exe 旁边就有包就用旁边；否则在 U 盘里找带「防闪退」或「Skeeto+MQCam」的目录
        public static string ResolvePkgDir(string start)
        {
            if (LooksLikePkg(start)) return start;
            string root = WalkUp(start);
            string guess = Path.Combine(Path.Combine(root, "游戏内容"), "求生之路2各项MOD\\扩展平台");
            if (Directory.Exists(guess) && LooksLikePkg(guess)) return guess;
            try
            {
                foreach (string d in Directory.GetDirectories(root))
                {
                    if (LooksLikePkg(d)) return d;
                    foreach (string d2 in Directory.GetDirectories(d))
                    {
                        if (LooksLikePkg(d2)) return d2;
                        foreach (string d3 in Directory.GetDirectories(d2))
                        {
                            if (LooksLikePkg(d3)) return d3;
                        }
                    }
                }
            }
            catch { }
            return start;
        }

        private static bool LooksLikePkg(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                if (Directory.Exists(Path.Combine(dir, "防闪退"))) return true;
                if (Directory.Exists(Path.Combine(dir, "Skeeto"))) return true;
                if (Directory.Exists(Path.Combine(dir, "MQCam"))) return true;
                return false;
            }
            catch { return false; }
        }

        private static string WalkUp(string start)
        {
            string p = start;
            while (p != null && p.Length > 3)
            {
                string leaf = Path.GetFileName(p);
                if (leaf == "游戏内容" || leaf == "工作区" || leaf == "编译用") return Path.GetDirectoryName(p);
                p = Path.GetDirectoryName(p);
            }
            return Path.GetPathRoot(start);
        }

        public static void Log(string level, string text)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + level.PadRight(4) + " " + text;
            try { File.AppendAllText(LogFile, line + "\r\n", Encoding.UTF8); } catch { }
            if (Sink != null) { try { Sink(line); } catch { } }
        }

        public static string Human(long b)
        {
            if (b >= 1073741824L) return (b / 1073741824.0).ToString("N2") + " GB";
            if (b >= 1048576L) return (b / 1048576.0).ToString("N1") + " MB";
            return (b / 1024.0).ToString("N0") + " KB";
        }

        // ---------------- 配置 ----------------
        public static void LoadConfig()
        {
            try
            {
                if (!File.Exists(CfgPath)) return;
                foreach (string raw in File.ReadAllLines(CfgPath, Encoding.UTF8))
                {
                    string s = raw.Trim();
                    if (s.Length == 0 || s.StartsWith("#") || s.StartsWith("[")) continue;
                    int eq = s.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = s.Substring(0, eq).Trim();
                    string v = s.Substring(eq + 1).Trim();
                    if (k == "GameRoot") Cfg.GameRoot = v;
                    else if (k == "LastRun") Cfg.LastRun = v;
                    else if (k == "L4N") Cfg.L4N = (v == "1");
                    else if (k == "Skeeto") Cfg.Skeeto = (v == "1");
                    else if (k == "AssetPack") Cfg.AssetPack = (v == "1");
                    else if (k == "Mqcam") Cfg.Mqcam = (v == "1");
                    else if (k == "Dxvk") Cfg.Dxvk = (v == "1");
                    else if (k == "Launch") Cfg.Launch = (v == "1");
                    else if (k == "LaunchOptions") Cfg.LaunchOptions = v;
                    else if (k == "CfgFile") Cfg.CfgFile = (v == "1");
                    else if (k == "Recent") Cfg.Recent.Add(v);
                    else if (k.StartsWith("Mod:")) Cfg.Mods.Add(k.Substring(4));
                }
            }
            catch { }
        }

        public static void SaveConfig()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 装机台配置（勾选记忆）。删掉这个文件 = 恢复默认。");
                sb.AppendLine("[general]");
                sb.AppendLine("GameRoot=" + Cfg.GameRoot);
                sb.AppendLine("LastRun=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("[platform]");
                sb.AppendLine("L4N=" + (Cfg.L4N ? "1" : "0"));
                sb.AppendLine("Skeeto=" + (Cfg.Skeeto ? "1" : "0"));
                sb.AppendLine("AssetPack=" + (Cfg.AssetPack ? "1" : "0"));
                sb.AppendLine("Mqcam=" + (Cfg.Mqcam ? "1" : "0"));
                sb.AppendLine("Dxvk=" + (Cfg.Dxvk ? "1" : "0"));
                sb.AppendLine("Launch=" + (Cfg.Launch ? "1" : "0"));
                sb.AppendLine("LaunchOptions=" + Cfg.LaunchOptions);
                sb.AppendLine("CfgFile=" + (Cfg.CfgFile ? "1" : "0"));
                foreach (string rp in Cfg.Recent) sb.AppendLine("Recent=" + rp);
                sb.AppendLine("[mods]");
                foreach (string m in Cfg.Mods) sb.AppendLine("Mod:" + m + "=1");
                File.WriteAllText(CfgPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex) { Log("WARN", "配置写不进去: " + ex.Message); }
        }

        // ---------------- 找游戏 ----------------
        public static bool TestGame(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string p = path.Trim().Trim('"').TrimEnd('\\');
                return File.Exists(Path.Combine(p, "left4dead2.exe")) && Directory.Exists(Path.Combine(p, "left4dead2"));
            }
            catch { return false; }
        }

        public static List<string> FindGames()
        {
            List<string> hits = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add = delegate(string p)
            {
                if (string.IsNullOrEmpty(p)) return;
                string t = p.Trim().Trim('"').TrimEnd('\\');
                if (TestGame(t) && seen.Add(t)) hits.Add(t);
            };

            if (TestGame(Cfg.GameRoot)) add(Cfg.GameRoot);

            List<string> steamLibs = new List<string>();
            string[] regs = new string[] {
                "HKEY_CURRENT_USER\\Software\\Valve\\Steam",
                "HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Valve\\Steam",
                "HKEY_LOCAL_MACHINE\\SOFTWARE\\Valve\\Steam"
            };
            foreach (string r in regs)
            {
                foreach (string valName in new string[] { "SteamPath", "InstallPath" })
                {
                    try
                    {
                        object v = Registry.GetValue(r, valName, null);
                        if (v != null && v.ToString().Length > 0)
                        {
                            string s = v.ToString().Replace('/', '\\').TrimEnd('\\');
                            if (Directory.Exists(s) && !steamLibs.Contains(s)) steamLibs.Add(s);
                        }
                    }
                    catch { }
                }
            }
            foreach (string lib in steamLibs)
            {
                add(Path.Combine(lib, "steamapps\\common\\Left 4 Dead 2"));
                try
                {
                    string vdf = Path.Combine(lib, "steamapps\\libraryfolders.vdf");
                    if (File.Exists(vdf))
                    {
                        string txt = File.ReadAllText(vdf);
                        foreach (Match m in Regex.Matches(txt, "\"path\"\\s*\"([^\"]+)\""))
                        {
                            string d = m.Groups[1].Value.Replace("\\\\", "\\").TrimEnd('\\');
                            if (Directory.Exists(d)) add(Path.Combine(d, "steamapps\\common\\Left 4 Dead 2"));
                        }
                    }
                }
                catch { }
            }

            string[] bases = new string[] { "Steam", "SteamLibrary", "storygames", "Games", "" };
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                try
                {
                    if (!di.IsReady) continue;
                    string root = di.RootDirectory.FullName;
                    foreach (string b in bases)
                    {
                        string p1 = (b.Length == 0) ? root : Path.Combine(root, b);
                        add(Path.Combine(p1, "steamapps\\common\\Left 4 Dead 2"));
                        add(Path.Combine(p1, "Left 4 Dead 2"));
                    }
                    // 网维：storygames\<编号>\steamapps\common\...
                    string sg = Path.Combine(root, "storygames");
                    if (Directory.Exists(sg))
                    {
                        foreach (string sub in Directory.GetDirectories(sg))
                        {
                            add(Path.Combine(sub, "steamapps\\common\\Left 4 Dead 2"));
                            foreach (string sub2 in Directory.GetDirectories(sub))
                                add(Path.Combine(sub2, "steamapps\\common\\Left 4 Dead 2"));
                        }
                    }
                }
                catch { }
            }
            return hits;
        }

        // ---------------- MOD 库 ----------------
        public static void ScanMods()
        {
            ModList.Clear();
            try
            {
                if (!Directory.Exists(ModRoot)) { Log("WARN", "MOD 根目录不存在: " + ModRoot); return; }
                foreach (string dir in Directory.GetDirectories(ModRoot))
                {
                    string cat = Path.GetFileName(dir);
                    if (!cat.StartsWith("addons", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (string f in Directory.GetFiles(dir, "*.vpk", SearchOption.AllDirectories))
                    {
                        ModItem it = new ModItem();
                        it.Category = cat;
                        it.Name = Path.GetFileName(f);
                        it.Size = new FileInfo(f).Length;
                        it.FullPath = f;
                        it.RelPath = f.Substring(ModRoot.Length).TrimStart('\\');
                        Classify(it);
                        ModList.Add(it);
                    }
                }
            }
            catch (Exception ex) { Log("FAIL", "扫 MOD 出错: " + ex.Message); }
        }

        // 按真实文件夹列出可筛选的层级（两级：顶层 / 顶层\子层）
        public static List<string> FolderChoices()
        {
            List<string> list = new List<string>();
            try
            {
                if (!Directory.Exists(ModRoot)) return list;
                foreach (string top in Directory.GetDirectories(ModRoot))
                {
                    string n1 = Path.GetFileName(top);
                    if (!n1.StartsWith("addons", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.GetFiles(top, "*.vpk", SearchOption.AllDirectories).Length == 0) continue;
                    list.Add(n1);
                    string[] subs = Directory.GetDirectories(top);
                    Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
                    foreach (string sub in subs)
                    {
                        if (Directory.GetFiles(sub, "*.vpk", SearchOption.AllDirectories).Length == 0) continue;
                        list.Add(n1 + "\\" + Path.GetFileName(sub));
                    }
                }
            }
            catch { }
            return list;
        }

        // 文件所在的相对目录（用于“位置”列）
        public static string RelDir(string relPath)
        {
            int i = relPath.LastIndexOf('\\');
            return i > 0 ? relPath.Substring(0, i) : "";
        }

        // 类型：按目录结构推断（枪械还会带具体枪名）
        private static void Classify(ModItem it)
        {
            string rel = it.RelPath;
            string cat = it.Category;
            it.Type = "其他";
            it.TypeDetail = "其他";
            if (cat.IndexOf("三方地图", StringComparison.Ordinal) >= 0) { it.Type = "地图"; it.TypeDetail = "地图"; return; }
            if (cat.IndexOf("人物", StringComparison.Ordinal) >= 0) { it.Type = "人物"; it.TypeDetail = "人物"; return; }
            if (cat.IndexOf("喷漆", StringComparison.Ordinal) >= 0) { it.Type = "喷漆"; it.TypeDetail = "喷漆"; return; }
            if (rel.IndexOf("医疗物品", StringComparison.Ordinal) >= 0) { it.Type = "道具"; it.TypeDetail = "道具"; return; }
            if (cat.IndexOf("物品储藏库", StringComparison.Ordinal) >= 0)
            {
                string[] parts = rel.Split('\\');
                if (parts.Length >= 3) { it.Type = "枪械"; it.TypeDetail = "枪械·" + parts[1]; }
                else { it.Type = "枪械"; it.TypeDetail = "枪械·其他"; }
                return;
            }
            if (cat.IndexOf("武器类", StringComparison.Ordinal) >= 0) { it.Type = "枪械"; it.TypeDetail = "枪械·其他"; return; }
        }

        // 扫过一遍就为分类
        public static List<string> Types()
        {
            List<string> ts = new List<string>();
            foreach (ModItem m in ModList) if (!ts.Contains(m.Type)) ts.Add(m.Type);
            ts.Sort();
            return ts;
        }

        // ---------------- 包检查 ----------------
        public static List<string> CheckPackages(out bool allOk)
        {
            List<string> lines = new List<string>();
            allOk = true;
            string l4nZip = FirstFile(PkgDir, "L4N_v*.zip");
            string l4nDir = Path.Combine(PkgDir, "L4N平台\\L4N");
            bool hasL4N = (l4nZip != null) || File.Exists(Path.Combine(l4nDir, "left4dead2.exe"));
            lines.Add(hasL4N ? ("L4N      OK  " + (l4nZip != null ? Path.GetFileName(l4nZip) : "L4N平台\\L4N")) : "L4N      缺");
            if (!hasL4N) allOk = false;

            string skDll = Path.Combine(PkgDir, "Skeeto\\Skeeto\\skeeto.dll");
            string skPs1 = Path.Combine(PkgDir, "Skeeto\\Skeeto\\Skeeto\\Maintain.ps1");
            bool hasSk = File.Exists(skDll) || File.Exists(skPs1);
            lines.Add(hasSk ? ("Skeeto   OK  " + SkeetoVersion()) : "Skeeto   缺");
            if (!hasSk) allOk = false;

            string vpk = FirstFile(Path.Combine(PkgDir, "Skeeto\\素材包"), "*.vpk");
            lines.Add(vpk != null ? ("素材包   OK  " + Human(new FileInfo(vpk).Length)) : "素材包   缺");
            if (vpk == null) allOk = false;

            string mq = Path.Combine(PkgDir, "MQCam\\MQCam\\mqcore_package_tool.exe");
            lines.Add(File.Exists(mq) ? ("MQCam    OK  " + File.GetLastWriteTime(mq).ToString("yyyy-MM-dd")) : "MQCam    缺");
            if (!File.Exists(mq)) allOk = false;

            string dx1 = Path.Combine(PkgDir, "防闪退\\d3d9.dll");
            string dx2 = Path.Combine(PkgDir, "防闪退\\dxvk.conf");
            bool hasDx = File.Exists(dx1) && File.Exists(dx2);
            lines.Add(hasDx ? ("dxvk     OK  " + (new FileInfo(dx1).Length / 1024).ToString("N0") + " KB, " + File.GetLastWriteTime(dx1).ToString("yyyy-MM-dd")) : "dxvk     缺");
            if (!hasDx) allOk = false;

            foreach (string l in lines) Log("INFO", l);
            return lines;
        }

        private static string SkeetoVersion()
        {
            try
            {
                string v = Path.Combine(Core.PkgDir, "Skeeto\\Skeeto\\VERSION.txt");
                if (File.Exists(v))
                {
                    string[] lines = File.ReadAllLines(v);
                    if (lines.Length > 0) return lines[0].Trim();
                }
            }
            catch { }
            return "";
        }

        public static string FirstFile(string dir, string pattern)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                string[] f = Directory.GetFiles(dir, pattern);
                if (f.Length == 0) return null;
                Array.Sort(f);
                return f[f.Length - 1];
            }
            catch { return null; }
        }

        // ---------------- 复制（带进度、可跳过） ----------------
        public static bool SameFile(string a, string b)
        {
            try
            {
                if (!File.Exists(b)) return false;
                FileInfo fa = new FileInfo(a);
                FileInfo fb = new FileInfo(b);
                return fa.Length == fb.Length && Math.Abs((fa.LastWriteTimeUtc - fb.LastWriteTimeUtc).TotalSeconds) < 3;
            }
            catch { return false; }
        }

        public static void CopyOne(string src, string dst)
        {
            string d = Path.GetDirectoryName(dst);
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            byte[] buf = new byte[4 * 1024 * 1024];
            using (FileStream fi = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream fo = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int n;
                while ((n = fi.Read(buf, 0, buf.Length)) > 0) fo.Write(buf, 0, n);
            }
            try { File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src)); } catch { }
        }

        public static string HashOf(string path)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] h = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder(h.Length * 2);
                    foreach (byte b in h) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return null; }
        }

        // 复制完做整份校验：大小 + SHA256
        public static bool CopyOneVerified(string src, string dst, out string why)
        {
            why = null;
            try
            {
                CopyOne(src, dst);
                long a = new FileInfo(src).Length, b = new FileInfo(dst).Length;
                if (a != b) { why = "大小不符 " + b + "/" + a; return false; }
                string hs = HashOf(src), hd = HashOf(dst);
                if (hs == null || hd == null) { why = "哈希算不出来"; return false; }
                if (hs != hd) { why = "SHA256 不一致"; return false; }
                return true;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        public static void BackupGame(string gameRoot, out string backupDir)
        {
            backupDir = Path.Combine(gameRoot, "_backup_l4d2suite_" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backupDir);
            string exe = Path.Combine(gameRoot, "left4dead2.exe");
            if (File.Exists(exe))
            {
                File.Copy(exe, Path.Combine(backupDir, "left4dead2.exe.原版备份"), true);
                Log("OK", "备份 left4dead2.exe");
            }
            string cfg = Path.Combine(gameRoot, "left4dead2\\cfg");
            if (Directory.Exists(cfg))
            {
                foreach (string f in Directory.GetFiles(cfg, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(cfg.Length).TrimStart('\\');
                    string d = Path.Combine(Path.Combine(backupDir, "cfg"), rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(d));
                    File.Copy(f, d, true);
                }
                Log("OK", "备份 left4dead2\\cfg");
            }
        }

        public static void SelfTestCore(string gameRoot)
        {
        }
    }
}
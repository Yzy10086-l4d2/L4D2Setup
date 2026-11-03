using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Installer
{
    public static class Work
    {
        /// <summary>
        /// 已装的东西怎么办。false（默认）= 一样就跳过，快；true = 全部重写一遍，修被改坏/被删一半的情况。
        /// 每个安装函数都要照顾这个开关，日志里也要说清楚"跳过了"还是"覆盖了"。
        /// </summary>
        public static bool Overwrite;
        public static int SelfTest(string gameRootHint)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== 装机台 自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("程序位置 : " + Core.Here);
            sb.AppendLine("U 盘根   : " + Core.UsbRoot);
            sb.AppendLine("MOD 根   : " + Core.ModRoot);
            bool okPack;
            sb.AppendLine("--- 包检查 ---");
            foreach (string l in Core.CheckPackages(out okPack)) sb.AppendLine("  " + l);
            sb.AppendLine("包齐全   : " + (okPack ? "是" : "否"));
            sb.AppendLine("--- 找游戏 ---");
            List<string> games = Core.FindGames();
            if (games.Count == 0) sb.AppendLine("  没找到");
            foreach (string g in games) sb.AppendLine("  " + g);
            sb.AppendLine("--- MOD 库 ---");
            Core.ScanMods();
            long total = 0;
            foreach (ModItem it in Core.ModList) total += it.Size;
            sb.AppendLine("  合计 " + Core.ModList.Count + " 个 vpk, " + Core.Human(total));
            sb.AppendLine("--- Steam 用户配置 ---");
            foreach (string f in SteamLocalConfigs()) sb.AppendLine("  " + f);
            File.WriteAllText(Path.Combine(Core.LogDir, "selftest.txt"), sb.ToString(), new UTF8Encoding(true));
            return 0;
        }

        // ---------- 通用 ----------
        public static void CopyTree(string src, string dst, Action<string> onFile)
        {
            foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(src.Length).TrimStart('\\');
                string d = Path.Combine(dst, rel);
                if (Core.SameFile(f, d)) continue;
                Core.CopyOne(f, d);
                if (onFile != null) onFile(rel);
            }
        }

        public static void Unzip(string zip, string dst)
        {
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            Directory.CreateDirectory(dst);
            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, dst);
            }
            catch (Exception ex)
            {
                Core.Log("WARN", "内置解压不可用（" + ex.GetType().Name + "），改用系统解压");
                ExtractWithShell(zip, dst);
            }
        }

        // 系统解压兜底：老系统 / 缺 .NET 4.5 时用资源管理器那套
        private static void ExtractWithShell(string zip, string dst)
        {
            Type shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) throw new Exception("系统解压也不可用");
            object shell = Activator.CreateInstance(shellType);
            object folder = shellType.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { dst });
            object zipNs = shellType.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { zip });
            object items = zipNs.GetType().InvokeMember("Items", System.Reflection.BindingFlags.InvokeMethod, null, zipNs, null);
            folder.GetType().InvokeMember("CopyHere", System.Reflection.BindingFlags.InvokeMethod, null, folder, new object[] { items, 0x14 });
            System.Threading.Thread.Sleep(1500);
        }

        // 解压出来的第一层如果只是个壳目录（里面才是 bin），就往下钻一层
        public static string ResolvePayloadRoot(string dir)
        {
            string hit = FindPayload(dir, 0);
            if (hit != null)
            {
                if (!string.Equals(hit, dir, StringComparison.OrdinalIgnoreCase))
                    Core.Log("INFO", "包里套了一层: " + Path.GetFileName(hit) + "，从这层开始覆盖");
                return hit;
            }
            return dir;
        }

        private static string FindPayload(string dir, int depth)
        {
            if (depth > 3) return null;
            if (File.Exists(Path.Combine(dir, "bin\\left4neko.dll"))) return dir;
            try
            {
                foreach (string s in Directory.GetDirectories(dir))
                {
                    string r = FindPayload(s, depth + 1);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }

        // ---------- L4N ----------
        public static bool InstallL4N(string gameRoot, Action<int, int> progress)
        {
            string zip = Core.FirstFile(Core.PkgDir, "L4N_v*.zip");
            string dir = Path.Combine(Core.PkgDir, "L4N平台\\L4N");
            try
            {
                if (zip != null)
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "l4n_" + DateTime.Now.Ticks);
                    Core.Log("INFO", "解压 " + Path.GetFileName(zip) + " …");
                    Unzip(zip, tmp);
                    string payload = ResolvePayloadRoot(tmp);
                    Core.Log("INFO", "解压到: " + tmp + "  实际内容根: " + payload);
                    CopyPayloadWithProgress(payload, gameRoot, progress);
                    try { Directory.Delete(tmp, true); } catch { }
                }
                else
                {
                    Core.Log("INFO", "用解压好的 L4N平台\\L4N 覆盖");
                    CopyPayloadWithProgress(dir, gameRoot, progress);
                }
                bool ok = File.Exists(Path.Combine(gameRoot, "bin\\left4neko.dll"));
                Core.Log(ok ? "OK" : "FAIL", ok ? "L4N 核心 bin\\left4neko.dll 到位" : "没看到 bin\\left4neko.dll");
                return ok;
            }
            catch (Exception ex) { Core.Log("FAIL", "L4N 装失败: " + ex.Message); return false; }
        }

        private static void CopyPayloadWithProgress(string payload, string gameRoot, Action<int, int> progress)
        {
            string[] files = Directory.GetFiles(payload, "*", SearchOption.AllDirectories);
            int total = files.Length, done = 0, skip = 0, write = 0;
            foreach (string f in files)
            {
                try
                {
                    string rel = f.Substring(payload.Length).TrimStart('\\');
                    string dst = Path.Combine(gameRoot, rel);
                    if (!Core.SameFile(f, dst)) { Core.CopyOne(f, dst); write++; }
                    else if (Overwrite) { Core.CopyOne(f, dst); write++; }   // 一样也照写：覆盖模式就是不信现状
                    else skip++;
                }
                catch (Exception ex) { Core.Log("WARN", "复制 " + Path.GetFileName(f) + " 失败: " + ex.Message); }
                done++;
                if (progress != null && (done % 10 == 0 || done == total)) progress(done, total);
            }
            Core.Log("INFO", Overwrite
                ? ("平台文件：写入 " + write + " 个（覆盖模式，全部重写）")
                : ("平台文件：写入 " + write + " 个，已是最新跳过 " + skip + " 个"));
        }

        // ---------- dxvk ----------
        public static bool InstallDxvk(string gameRoot, string backupDir)
        {
            try
            {
                string srcDir = Path.Combine(Core.PkgDir, "防闪退");
                foreach (string n in new string[] { "d3d9.dll", "dxvk.conf" })
                {
                    string s = Path.Combine(srcDir, n);
                    string d = Path.Combine(gameRoot, n);
                    if (!File.Exists(s)) { Core.Log("FAIL", "缺 " + s); return false; }
                    if (File.Exists(d)) File.Copy(d, Path.Combine(backupDir, n + ".装之前"), true);
                    File.Copy(s, d, true);
                    if (new FileInfo(d).Length != new FileInfo(s).Length) { Core.Log("FAIL", n + " 大小不对"); return false; }
                    Core.Log("OK", n + " 到位");
                }
                return true;
            }
            catch (Exception ex) { Core.Log("FAIL", "dxvk 失败: " + ex.Message); return false; }
        }

        // ---------- Skeeto ----------
        public static bool InstallSkeeto(string gameRoot)
        {
            string ps1 = Path.Combine(Core.PkgDir, "Skeeto\\Skeeto\\Skeeto\\Maintain.ps1");
            string dir = Path.Combine(Core.PkgDir, "Skeeto\\Skeeto");
            bool already = File.Exists(Path.Combine(gameRoot, "skeeto.dll"));
            if (already && !Overwrite)
            {
                Core.Log("OK", "Skeeto 已经装过（skeeto.dll 在），跳过。想重装就在「安装选项」里选「覆盖重装」。");
                if (!File.Exists(Path.Combine(gameRoot, "bin\\wininet.dll")))
                    Core.Log("WARN", "bin\\wininet.dll 不见了 —— 勾「覆盖重装」能把平台文件补回来");
                return true;
            }
            bool ok = false;
            if (File.Exists(ps1))
            {
                foreach (string mode in new string[] { "Migrate", "Apply" })
                {
                    if (ok) break;
                    Core.Log("INFO", "调用 Skeeto 官方安装器（" + mode + "）…");
                    string output = RunCapture("powershell.exe",
                        "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\" -Mode " + mode +
                        " -GameRoot \"" + gameRoot + "\" -PayloadRoot \"" + dir + "\" -NonInteractive",
                        dir, 240000);
                    foreach (string line in output.Split('\n'))
                    {
                        string s2 = line.Trim();
                        if (s2.Length > 0) Core.Log("INFO", "  | " + s2);
                    }
                    if (File.Exists(Path.Combine(gameRoot, "skeeto.dll"))) ok = true;
            if (already) Core.Log("INFO", "覆盖模式：Skeeto 又完整装了一遍");
                    else Core.Log("WARN", mode + " 跑完但 skeeto.dll 还是没有");
                }
            }
            if (!ok)
            {
                Core.Log("WARN", "改用整包复制 Skeeto");
                try { CopyTree(dir, gameRoot, null); } catch (Exception ex) { Core.Log("FAIL", "Skeeto 复制失败: " + ex.Message); }
            }
            bool has = File.Exists(Path.Combine(gameRoot, "skeeto.dll"));
            Core.Log(has ? "OK" : "FAIL", has ? "skeeto.dll 到位" : "没看到 skeeto.dll");
            if (File.Exists(Path.Combine(gameRoot, "bin\\wininet.dll"))) Core.Log("OK", "bin\\wininet.dll（代理）到位");
            else Core.Log("WARN", "没看到 bin\\wininet.dll，Skeeto 可能不会加载");
            return has;
        }

        // ---------- 素材包 ----------
        public static string InstallAssetPack(string gameRoot)
        {
            string vpk = Core.FirstFile(Path.Combine(Core.PkgDir, "Skeeto\\素材包"), "*.vpk");
            if (vpk == null) { Core.Log("FAIL", "找不到素材包 vpk"); return null; }
            string addons = Path.Combine(Path.Combine(gameRoot, "left4dead2"), "addons");
            if (!Directory.Exists(addons)) Directory.CreateDirectory(addons);
            string dst = Path.Combine(addons, Path.GetFileName(vpk));
            long len = new FileInfo(vpk).Length;
            if (!Overwrite && Core.SameFile(vpk, dst)) { Core.Log("OK", "素材包已存在且大小一致，跳过"); return Path.GetFileName(vpk); }
            Core.Log("INFO", "复制素材包 " + Core.Human(len) + " …");
            Core.CopyOne(vpk, dst);
            if (new FileInfo(dst).Length != len) { Core.Log("FAIL", "素材包大小不对"); return null; }
            Core.Log("OK", "素材包到位（" + Core.Human(len) + "）");
            return Path.GetFileName(vpk);
        }

        // ---------- autoexec.cfg ----------
        public static bool InstallCfg(string gameRoot)
        {
            try
            {
                string src = Path.Combine(Core.ModRoot, "autoexec.cfg");
                if (!File.Exists(src)) { Core.Log("WARN", "U 盘上没有 autoexec.cfg，跳过"); return false; }
                string dir = Path.Combine(Path.Combine(gameRoot, "left4dead2"), "cfg");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string dst = Path.Combine(dir, "autoexec.cfg");
                if (!Overwrite && Core.SameFile(src, dst)) { Core.Log("OK", "autoexec.cfg 已是最新，跳过"); return true; }
                // .装之前 只在第一次留：它是"这台机器原本的样子"，第二次装机不该冲掉它
                if (File.Exists(dst) && !File.Exists(dst + ".装之前")) File.Copy(dst, dst + ".装之前", true);
                if (File.Exists(dst)) File.Copy(dst, dst + ".上一次", true);
                Core.CopyOne(src, dst);
                Core.Log("OK", "autoexec.cfg 已放进 left4dead2\\cfg");
                return true;
            }
            catch (Exception ex) { Core.Log("FAIL", "autoexec.cfg 搬运失败: " + ex.Message); return false; }
        }

        // ---------- MQCam ----------
        public static bool InstallMqcam(string gameRoot)
        {
            string dir = Path.Combine(Core.PkgDir, "MQCam\\MQCam");
            string tool = Path.Combine(dir, "mqcore_package_tool.exe");
            string dll = Path.Combine(Path.Combine(Path.Combine(gameRoot, "bin"), "neko"), "mqcore\\mqcam.dll");
            if (File.Exists(dll) && !Overwrite) { Core.Log("OK", "MQCam 已经装过（mqcam.dll 在），跳过，不动相机设置"); return true; }
            if (!File.Exists(tool)) { Core.Log("FAIL", "缺 mqcore_package_tool.exe"); return false; }
            foreach (string cmd in new string[] { "install plan", "install apply" })
            {
                string output = RunCapture(tool, cmd + " \"" + gameRoot + "\"", dir, 120000);
                foreach (string line in output.Split('\n'))
                {
                    string s2 = line.Trim();
                    if (s2.Length > 0) Core.Log("INFO", "  | " + s2);
                }
            }
            bool ok = File.Exists(dll);
            Core.Log(ok ? "OK" : "FAIL", ok ? "mqcam.dll 到位" : "没看到 bin\\neko\\mqcore\\mqcam.dll");
            if (ok) ResetCamState(gameRoot);
            return ok;
        }

        private static void ResetCamState(string gameRoot)
        {
            try
            {
                string clean = Path.Combine(Core.PkgDir, "MQCam\\mqcam_state_clean.ini");
                string dst = Path.Combine(Path.Combine(Path.Combine(gameRoot, "bin\\neko"), "mqcore"), "mqcam_state.ini");
                if (!File.Exists(clean)) { Core.Log("WARN", "没有 mqcam_state_clean.ini，跳过相机归零"); return; }
                if (!Directory.Exists(Path.GetDirectoryName(dst))) return;
                if (File.Exists(dst)) File.Copy(dst, dst + ".装之前", true);
                File.Copy(clean, dst, true);
                Core.Log("OK", "相机偏移已归零（F2/F3 从默认位置开始）");
            }
            catch (Exception ex) { Core.Log("WARN", "相机配置写入失败: " + ex.Message); }
        }

        // ---------- Steam 启动项 ----------
        public static List<string> SteamLocalConfigs()
        {
            List<string> found = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> roots = new List<string>();
            string[] regs = new string[] {
                "HKEY_CURRENT_USER\\Software\\Valve\\Steam",
                "HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Valve\\Steam",
                "HKEY_LOCAL_MACHINE\\SOFTWARE\\Valve\\Steam"
            };
            foreach (string r in regs)
            {
                foreach (string n in new string[] { "SteamPath", "InstallPath" })
                {
                    try
                    {
                        object v = Registry.GetValue(r, n, null);
                        if (v == null) continue;
                        string s = v.ToString().Replace('/', '\\').TrimEnd('\\');
                        if (Directory.Exists(s) && !roots.Contains(s)) roots.Add(s);
                    }
                    catch { }
                }
            }
            foreach (string root in roots)
            {
                string ud = Path.Combine(root, "userdata");
                if (!Directory.Exists(ud)) continue;
                foreach (string u in Directory.GetDirectories(ud))
                {
                    string cfg = Path.Combine(Path.Combine(u, "config"), "localconfig.vdf");
                    if (File.Exists(cfg))
                    {
                        string full = Path.GetFullPath(cfg);
                        if (seen.Add(full)) found.Add(full);
                    }
                }
            }
            return found;
        }

        public static string WriteLaunchOptions(string value)
        {
            List<string> cfgs = SteamLocalConfigs();
            try
            {
                bool running = Process.GetProcessesByName("steam").Length > 0;
                if (running) Core.Log("WARN", "Steam 正在运行，它退出时可能把配置写回去；最稳是退出 Steam 再写、然后重开");
            }
            catch { }
            if (cfgs.Count == 0)
            {
                try { System.Windows.Forms.Clipboard.SetText(value); Core.Log("WARN", "没找到 Steam 用户配置；启动项已复制到剪贴板，请到 Steam 里粘贴"); }
                catch { Core.Log("WARN", "没找到 Steam 用户配置，启动项是: " + value); }
                return "没找到 Steam 用户配置，改用手动粘贴";
            }
            int okCount = 0;
            foreach (string cfg in cfgs)
            {
                try
                {
                    if (PatchVdf(cfg, value)) { okCount++; Core.Log("OK", "启动项已写: " + cfg); }
                    else Core.Log("WARN", "这个文件里找不到 550 或 apps 块: " + cfg);
                }
                catch (Exception ex) { Core.Log("WARN", "写 " + cfg + " 失败: " + ex.Message); }
            }
            if (okCount == 0) return "启动项没写成功，请在 Steam 里手动填";
            return "启动项已写入 " + okCount + " 个 Steam 账号配置（Steam 退出后重开生效）";
        }

        private static bool PatchVdf(string path, string value)
        {
            Core.BackupOnce(path);
            string text = File.ReadAllText(path, Encoding.UTF8);
            int idx = text.IndexOf("\"550\"", StringComparison.Ordinal);
            bool changed = false;
            if (idx < 0)
            {
                int apps = text.IndexOf("\"apps\"", StringComparison.Ordinal);
                if (apps < 0) return false;
                int open = text.IndexOf('{', apps);
                if (open < 0) return false;
                string ins = "\r\n\t\t\t\t\t\"550\"\r\n\t\t\t\t\t{\r\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"" + value + "\"\r\n\t\t\t\t\t}";
                text = text.Insert(open + 1, ins);
                changed = true;
            }
            else
            {
                int open = text.IndexOf('{', idx);
                if (open < 0) return false;
                int depth = 0, close = -1;
                for (int i = open; i < text.Length; i++)
                {
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}')
                    {
                        depth--;
                        if (depth == 0) { close = i; break; }
                    }
                }
                if (close < 0) return false;
                string block = text.Substring(open, close - open);
                Match lo = Regex.Match(block, "\"LaunchOptions\"\\s*\"[^\"]*\"");
                if (lo.Success)
                {
                    string nb = block.Substring(0, lo.Index) + "\"LaunchOptions\"\t\t\"" + value + "\"" + block.Substring(lo.Index + lo.Length);
                    text = text.Substring(0, open) + nb + text.Substring(close);
                }
                else
                {
                    text = text.Insert(open + 1, "\r\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"" + value + "\"");
                }
                changed = true;
            }
            if (changed) File.WriteAllText(path, text, new UTF8Encoding(false));
            return changed;
        }

        // ---------- 校验 ----------
        public static List<string> Verify(string gameRoot)
        {
            List<string> list = new List<string>();
            string[] items = new string[] {
                "left4dead2.exe", "bin\\left4neko.dll", "skeeto.dll", "bin\\wininet.dll",
                "bin\\neko\\mqcore\\mqcam.dll", "d3d9.dll", "dxvk.conf"
            };
            foreach (string it in items)
            {
                bool ok = File.Exists(Path.Combine(gameRoot, it));
                list.Add((ok ? "[OK] " : "[缺] ") + it);
                Core.Log(ok ? "OK" : "WARN", (ok ? "[OK] " : "[缺] ") + it);
            }
            return list;
        }

        // ---------- 铺 MOD ----------
        public static long CopyMods(string gameRoot, List<ModItem> sel, Action<long, long> progress, out int copied, out int skipped, out List<string> added, out List<string> failed)
        {
            int c = 0, s = 0;
            List<string> okList = new List<string>();
            List<string> badList = new List<string>();
            string addons = Path.Combine(Path.Combine(gameRoot, "left4dead2"), "addons");
            if (!Directory.Exists(addons)) Directory.CreateDirectory(addons);

            long total = 0;
            foreach (ModItem it in sel) total += it.Size;
            long done = 0;
            List<ModItem> todo = new List<ModItem>();
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModItem it in sel)
            {
                // 不同分类下可能有同名 vpk：只铺第一个，否则并行时两个线程会同时写同一个目标文件
                if (!used.Add(it.Name))
                {
                    s++; done += it.Size;
                    Core.Log("WARN", "同名跳过（本次已装第一个）: " + it.Name);
                    continue;
                }
                string d = Path.Combine(addons, it.Name);
                // 压缩包里的项没有独立文件可对比，按「已经有个同大小的同名 vpk」算装上
                bool same = it.IsArchive
                    ? (File.Exists(d) && new FileInfo(d).Length == it.Size)
                    : Core.SameFile(it.FullPath, d);
                if (same && !Overwrite) { s++; done += it.Size; }
                else todo.Add(it);
            }
            if (progress != null) progress(done, total);

            object gate = new object();
            int degree = DegreeFor(todo);
            long todoSize = 0;
            foreach (ModItem it in todo) todoSize += it.Size;
            Stopwatch sw = Stopwatch.StartNew();
            long lastReport = 0;
            if (todo.Count > 0)
            {
                System.Threading.Tasks.Parallel.ForEach(todo,
                    new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = degree },
                    it =>
                    {
                        string d = Path.Combine(addons, it.Name);
                        string why;
                        bool good = it.IsArchive
                            ? ExtractOne(it, d, out why)
                            : Core.CopyOneVerified(it.FullPath, d, out why);
                        bool report = false;
                        lock (gate)
                        {
                            if (good) { c++; okList.Add(it.Name); }
                            else { badList.Add(it.Name); Core.Log("FAIL", "复制/校验失败 " + it.Name + "：" + why); }
                            done += it.Size;
                            long now = sw.ElapsedMilliseconds;
                            if (now - lastReport >= 120) { lastReport = now; report = true; }
                        }
                        if (report && progress != null) progress(done, total);   // 别每个文件都跨线程刷一次进度条
                    });
            }
            sw.Stop();
            if (progress != null) progress(done, total);
            double sec = sw.Elapsed.TotalSeconds;
            if (sec > 0.5 && todoSize > 0)
            {
                double rate = todoSize / 1048576.0 / sec;
                if (rate > 0.2) Core.LastRateMBps = Math.Round(rate, 1);
            }
            copied = c; skipped = s; added = okList; failed = badList;
            string tail = "共 " + Core.Human(total) + "，" + Math.Round(sec, 1) + " 秒（" + Core.LastRateMBps + " MB/s）";
            Core.Log("OK", Overwrite
                ? ("MOD: 覆盖重写 " + c + " 个（并行 " + degree + " 路" + (Core.StrictVerify ? " + 全量 SHA256" : " + 大小校验") + "），同名去重跳过 " + s + " 个，" + tail)
                : ("MOD: 新装 " + c + " 个（并行 " + degree + " 路" + (Core.StrictVerify ? " + 全量 SHA256" : " + 大小校验") + "），已装跳过 " + s + " 个，" + tail));
            return total;
        }

        /// <summary>从压缩包里只取出这一个 vpk 放到 addons：不整包解压，也不动 U 盘上的包。</summary>
        public static bool ExtractOne(ModItem it, string dst, out string why)
        {
            why = null;
            string arc = it.FullPath;
            string inner = it.Inner;
            if (inner == null || inner.Length == 0) { why = "包内路径为空"; return false; }
            if (inner.IndexOf('*') >= 0 || inner.IndexOf('?') >= 0) { why = "文件名带通配符，暂不支持"; return false; }

            if (Path.GetExtension(arc).ToLowerInvariant() == ".zip")
            {
                try
                {
                    using (System.IO.Compression.ZipArchive za = System.IO.Compression.ZipFile.OpenRead(arc))
                    {
                        System.IO.Compression.ZipArchiveEntry e = za.GetEntry(inner.Replace('\\', '/'));
                        if (e == null) { why = "包里找不到 " + inner; return false; }
                        using (Stream src = e.Open())
                        using (FileStream fs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                            src.CopyTo(fs, 1 << 20);
                    }
                    return true;
                }
                catch (Exception ex) { why = ex.Message; return false; }
            }

            string rar = Catalog.RarExe();
            if (rar.Length == 0) { why = "找不到 WinRAR（Rar.exe）"; return false; }
            string tmp = Path.Combine(Path.GetTempPath(), "l4d2-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(tmp);
                ProcessStartInfo psi = new ProcessStartInfo(rar, "e -y -idq \"" + arc + "\" \"" + inner + "\" \"" + tmp + "\\\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(300000)) { try { p.Kill(); } catch { } why = "解压超时"; return false; }
                    if (p.ExitCode != 0) { why = "WinRAR 返回 " + p.ExitCode; return false; }
                }
                string got = Path.Combine(tmp, Path.GetFileName(inner));
                if (!File.Exists(got)) { why = "没解出文件"; return false; }
                try { if (File.Exists(dst)) File.Delete(dst); } catch { }
                try { File.Move(got, dst); }
                catch { File.Copy(got, dst, true); }
                return true;
            }
            catch (Exception ex) { why = ex.Message; return false; }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // U 盘上并行读会互相打断顺序读，反而更慢；网络共享才需要多路掩盖延迟
        private static int DegreeFor(List<ModItem> todo)
        {
            int cpu = Environment.ProcessorCount;
            DriveType t = DriveType.Fixed;
            try { if (todo.Count > 0) t = new DriveInfo(Path.GetPathRoot(todo[0].FullPath)).DriveType; }
            catch { }
            if (t == DriveType.Removable || t == DriveType.CDRom) return Math.Min(2, Math.Max(1, cpu));
            if (t == DriveType.Network) return Math.Min(4, Math.Max(2, cpu));
            return Math.Min(3, Math.Max(2, cpu));
        }

        // ---------- MOD 库维护 ----------
        public static string MoveModToRecycle(ModItem m)
        {
            string dir = Path.Combine(Core.ModRoot, "_回收的MOD");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string dst = Path.Combine(dir, m.Name);
            int n = 1;
            while (File.Exists(dst))
            {
                dst = Path.Combine(dir, Path.GetFileNameWithoutExtension(m.Name) + "_" + n + ".vpk");
                n++;
            }
            File.Move(m.FullPath, dst);
            return dst;
        }

        public static string RenameMod(ModItem m, string newName)
        {
            newName = Core.SafeName(newName);
            if (string.IsNullOrEmpty(newName)) return "名字不能为空";
            if (!newName.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase)) newName = newName + ".vpk";
            string dst = Path.Combine(Path.GetDirectoryName(m.FullPath), newName);
            if (File.Exists(dst)) return "同目录已经有 " + newName;
            File.Move(m.FullPath, dst);
            return null;
        }

        public static List<string> CleanDuplicateMods()
        {
            List<string> moved = new List<string>();
            Core.ScanMods();
            Dictionary<string, List<ModItem>> byName = new Dictionary<string, List<ModItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (ModItem m in Core.ModList)
            {
                if (!byName.ContainsKey(m.Name)) byName[m.Name] = new List<ModItem>();
                byName[m.Name].Add(m);
            }
            List<ModItem> gone = new List<ModItem>();
            foreach (KeyValuePair<string, List<ModItem>> kv in byName)
            {
                if (kv.Value.Count < 2) continue;
                for (int i = 1; i < kv.Value.Count; i++)
                {
                    try
                    {
                        moved.Add(Path.GetFileName(MoveModToRecycle(kv.Value[i])) + "  (" + kv.Value[i].Cat + " · " + kv.Value[i].Sub + ")");
                        gone.Add(kv.Value[i]);
                    }
                    catch (Exception ex) { Core.Log("WARN", "清理 " + kv.Value[i].Name + " 失败: " + ex.Message); }
                }
            }
            // 文件已经挪走，直接把它从内存列表里摘掉，不再花几秒重扫全库
            foreach (ModItem g in gone) { Core.ModList.Remove(g); Core.ByRel.Remove(g.Key); }
            return moved;
        }

        // ---------- 换整套 ----------
        public static int SwapToSet(string gameRoot, List<ModItem> sel)
        {
            HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModItem m in sel) keep.Add(m.Name);
            string addons = Path.Combine(Path.Combine(gameRoot, "left4dead2"), "addons");
            if (!Directory.Exists(addons)) return 0;
            int moved = 0;
            foreach (string f in Directory.GetFiles(addons, "*.vpk"))
            {
                string name = Path.GetFileName(f);
                if (keep.Contains(name)) continue;
                try { Bin.Move(gameRoot, f); moved++; } catch (Exception ex) { Core.Log("WARN", "挪走 " + name + " 失败: " + ex.Message); }
            }
            Core.Log("OK", "换整套：旧 MOD 挪进回收站 " + moved + " 个，保留 " + keep.Count + " 个");
            return moved;
        }

        // ---------- 回收新 MOD ----------
        public static List<string> CollectNewMods(string gameRoot)
        {
            List<string> got = new List<string>();
            string addons = Path.Combine(Path.Combine(gameRoot, "left4dead2"), "addons");
            if (!Directory.Exists(addons)) return got;
            Core.ScanMods();
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModItem m in Core.ModList) known.Add(m.Name);
            string dst = Path.Combine(Core.ModRoot, "addons回收");
            foreach (string f in Directory.GetFiles(addons, "*.vpk"))
            {
                string name = Path.GetFileName(f);
                if (known.Contains(name)) continue;
                try
                {
                    if (!Directory.Exists(dst)) Directory.CreateDirectory(dst);
                    string target = Path.Combine(dst, name);
                    if (Core.SameFile(f, target)) continue;
                    Core.CopyOne(f, target);
                    got.Add(name);
                    Core.Log("OK", "已收回: " + name);
                }
                catch (Exception ex) { Core.Log("WARN", "回收 " + name + " 失败: " + ex.Message); }
            }
            return got;
        }

        // ---------- 彻底还原 ----------
        public static List<string> FullRestore(string gameRoot)
        {
            List<string> report = new List<string>();
            List<string> targets = new List<string>();
            targets.AddRange(L4NFiles());
            targets.AddRange(PackageFiles(Path.Combine(Core.PkgDir, "Skeeto\\Skeeto")));
            string vpk = Core.FirstFile(Path.Combine(Core.PkgDir, "Skeeto\\素材包"), "*.vpk");
            if (vpk != null) targets.Add("left4dead2\\addons\\" + Path.GetFileName(vpk));
            targets.Add("d3d9.dll");
            targets.Add("dxvk.conf");
            targets.Add("bin\\neko\\mqcore\\mqcam.dll");
            targets.Add("bin\\neko\\mqcore\\mqcam.ini");
            targets.Add("bin\\neko\\mqcore\\mqcam_state.ini");

            HashSet<string> uniq = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int del = 0;
            foreach (string rel in targets)
            {
                if (string.IsNullOrEmpty(rel)) continue;
                string key = rel.TrimStart('\\');
                if (!uniq.Add(key)) continue;
                try { if (File.Exists(Path.Combine(gameRoot, key))) { File.Delete(Path.Combine(gameRoot, key)); del++; } } catch { }
            }
            report.Add("删掉平台文件 " + del + " 个（L4N / Skeeto / 素材包 / dxvk / MQCam）");

            foreach (string d in new string[] { "bin\\neko\\mqcore", "bin\\neko", "Skeeto", "skeeto_kbact", "skeeto_update" })
            {
                try
                {
                    string full = Path.Combine(gameRoot, d);
                    if (Directory.Exists(full) && Directory.GetFileSystemEntries(full).Length == 0) Directory.Delete(full);
                }
                catch { }
            }

            string mf = Path.Combine(Core.LogDir, "最近一次.ini");
            string backup = "";
            List<string> added = new List<string>();
            if (File.Exists(mf))
            {
                foreach (string raw in File.ReadAllLines(mf, Encoding.UTF8))
                {
                    string t = raw.Trim();
                    if (t.StartsWith("Backup=")) backup = t.Substring(7);
                    else if (t.StartsWith("Added=")) added.Add(t.Substring(6));
                }
            }
            if (backup.Length > 0 && Directory.Exists(backup))
            {
                string exeBak = Path.Combine(backup, "left4dead2.exe.原版备份");
                if (File.Exists(exeBak))
                {
                    try { File.Copy(exeBak, Path.Combine(gameRoot, "left4dead2.exe"), true); report.Add("left4dead2.exe 已恢复原版"); }
                    catch (Exception ex) { report.Add("恢复 exe 失败: " + ex.Message); }
                }
                foreach (string n in new string[] { "d3d9.dll.装之前", "dxvk.conf.装之前" })
                {
                    string b = Path.Combine(backup, n);
                    if (File.Exists(b))
                    {
                        try { File.Copy(b, Path.Combine(gameRoot, n.Replace(".装之前", "")), true); report.Add(n + " 已恢复"); } catch { }
                    }
                }
            }

            string l4d2 = Path.Combine(gameRoot, "left4dead2");
            string alBak = Path.Combine(l4d2, "addonlist.txt.装之前");
            if (File.Exists(alBak)) { try { File.Copy(alBak, Path.Combine(l4d2, "addonlist.txt"), true); report.Add("addonlist.txt 已恢复"); } catch { } }
            string acBak = Path.Combine(l4d2, "cfg\\autoexec.cfg.装之前");
            if (File.Exists(acBak)) { try { File.Copy(acBak, Path.Combine(l4d2, "cfg\\autoexec.cfg"), true); report.Add("autoexec.cfg 已恢复"); } catch { } }

            string addons = Path.Combine(l4d2, "addons");
            int moved = 0;
            foreach (string a in added)
            {
                try { string p = Path.Combine(addons, a); if (File.Exists(p)) { Bin.Move(gameRoot, p); moved++; } } catch { }
            }
            if (added.Count > 0) report.Add("本次铺的 MOD 挪进回收站 " + moved + " 个");
            return report;
        }

        private static List<string> PackageFiles(string pkgDir)
        {
            List<string> rels = new List<string>();
            try
            {
                if (!Directory.Exists(pkgDir)) return rels;
                foreach (string f in Directory.GetFiles(pkgDir, "*", SearchOption.AllDirectories))
                    rels.Add(f.Substring(pkgDir.Length).TrimStart('\\'));
            }
            catch { }
            return rels;
        }

        private static List<string> L4NFiles()
        {
            List<string> rels = new List<string>();
            string zip = Core.FirstFile(Core.PkgDir, "L4N_v*.zip");
            try
            {
                if (zip != null)
                {
                    using (System.IO.Compression.ZipArchive za = System.IO.Compression.ZipFile.OpenRead(zip))
                    {
                        Dictionary<string, bool> names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                        foreach (System.IO.Compression.ZipArchiveEntry e in za.Entries) names[e.FullName] = true;
                        string prefix = null;
                        foreach (string n in names.Keys)
                        {
                            if (n.EndsWith("/bin/left4neko.dll", StringComparison.OrdinalIgnoreCase)) { prefix = n.Substring(0, n.Length - "bin/left4neko.dll".Length); break; }
                            if (string.Equals(n, "bin/left4neko.dll", StringComparison.OrdinalIgnoreCase)) { prefix = ""; break; }
                        }
                        if (prefix == null) return rels;
                        foreach (string n in names.Keys)
                        {
                            if (n.EndsWith("/")) continue;
                            if (!n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                            rels.Add(n.Substring(prefix.Length).Replace('/', '\\'));
                        }
                    }
                }
                else rels.AddRange(PackageFiles(Path.Combine(Core.PkgDir, "L4N平台\\L4N")));
            }
            catch (Exception ex) { Core.Log("WARN", "读 L4N 包清单失败: " + ex.Message); }
            return rels;
        }
        // ---------- 批量导入：把散落的 vpk 收进库并按名字归类 ----------
        public static List<string> ImportVpks(string sourceDir, out int imported, out int skipped)
        {
            imported = 0; skipped = 0;
            List<string> log = new List<string>();
            if (!Directory.Exists(sourceDir)) return log;
            Core.ScanMods();
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModItem m in Core.ModList) known.Add(m.Name);
            foreach (string f in Directory.GetFiles(sourceDir, "*.vpk", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(f);
                if (known.Contains(name)) { skipped++; continue; }
                string relDir = ClassifyByName(name);
                string dstDir = Path.Combine(Core.ModRoot, relDir);
                try
                {
                    Directory.CreateDirectory(dstDir);
                    string dst = Path.Combine(dstDir, name);
                    string why;
                    if (!Core.CopyOneVerified(f, dst, out why)) { log.Add("失败 " + name + "：" + why); continue; }
                    imported++;
                    known.Add(name);
                    AddToMem(dst);
                    log.Add("入库 " + relDir + "\\" + name);
                }
                catch (Exception ex) { log.Add("失败 " + name + "：" + ex.Message); }
            }
            return log;
        }

        // 新入库的文件直接补进内存列表，省掉一次全库重扫
        private static void AddToMem(string fullPath)
        {
            try
            {
                ModItem it = new ModItem();
                it.Name = Path.GetFileName(fullPath);
                it.FullPath = fullPath;
                it.RelPath = fullPath.Substring(Core.ModRoot.Length).TrimStart('\\');
                it.Key = it.RelPath;
                string rd = Core.RelDir(it.RelPath);
                it.Size = new FileInfo(fullPath).Length;
                Catalog.Classify(it);
                Core.ModList.Add(it);
                Core.ByRel[it.Key] = it;
                if (rd.Length > 0)
                {
                    Core.BumpDir(rd);
                    int bs = rd.IndexOf('\\');
                    if (bs > 0) Core.BumpDir(rd.Substring(0, bs));
                }
            }
            catch { }
        }

        // 按文件名猜它该放哪个文件夹（只猜常见枪械，猜不到进「杂项功能」）
        public static string ClassifyByName(string name)
        {
            string n = name.ToLowerInvariant();
            string[][] map = new string[][]
            {
                new string[] { "ak47", "AK47" },
                new string[] { "mp5", "MP5" },
                new string[] { "m16", "M16" },
                new string[] { "scar", "scar" },
                new string[] { "awp", "AWP" },
                new string[] { "鸟狙", "鸟狙" }, new string[] { "scout", "鸟狙" },
                new string[] { "木喷", "木喷" },
                new string[] { "铁喷", "铁喷" },
                new string[] { "一代连喷", "一代连喷" }, new string[] { "二代连喷", "二代连喷" },
                new string[] { "15连", "15发连狙" }, new string[] { "30连", "30发连狙" },
                new string[] { "sg552", "sg552" }, new string[] { "uzi", "uzi" }, new string[] { "mac-10", "MAC-10" },
                new string[] { "m60", "M60" },
                new string[] { "改视角", "改视角" },
                new string[] { "火光", "火光消除" },
                new string[] { "药", "医疗物品\\药" }, new string[] { "针", "医疗物品\\针" }, new string[] { "医疗", "医疗物品\\包" },
                new string[] { "喷漆", "addons喷漆扩展" }, new string[] { "喷图", "addons喷漆扩展" },
                new string[] { "地图", "addons三方地图" }, new string[] { "战役", "addons三方地图" }
            };
            foreach (string[] kv in map) if (n.Contains(kv[0])) return kv[1].StartsWith("addons") ? kv[1] : ("addons各类物品储藏库\\" + kv[1]);
            return "addons各类物品储藏库\\杂项功能";
        }

        // 脚本库冲突已并入 ConflictCheck.DeepAll：同一批 vpk 只解析一遍

        // ---------- 装机记录 ----------
        public static void WriteManifest(string gameRoot, string backupDir, List<string> added)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("GameRoot=" + gameRoot);
                sb.AppendLine("Time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Backup=" + backupDir);
                foreach (string a in added) sb.AppendLine("Added=" + a);
                File.WriteAllText(Path.Combine(Core.LogDir, "最近一次.ini"), sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex) { Core.Log("WARN", "装机记录写失败: " + ex.Message); }
        }

        // ---------- 跑外部程序并抓输出 ----------
        private static string RunCapture(string exe, string args, string workDir, int timeoutMs)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = workDir;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) sb.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                    else p.WaitForExit();
                }
            }
            catch (Exception ex) { sb.AppendLine("调用失败: " + ex.Message); }
            return sb.ToString();
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Installer
{
    // ---------- 从 vpk 目录表反推「这个 MOD 改了什么」 ----------
    // 依据只有一处：包里真的带了哪个模型 / HUD 图标 / 音效目录。
    // 刻意不读 addoninfo.txt 的作者自述 —— 实测作者标题与真实内容经常不符。
    public static class ModIdentify
    {
        public class Result
        {
            public string Subject = "";       // 改了什么，如 "M16"
            public string Level = "";         // 依据强度：模型 / HUD图标 / 音效 / 材质
            public string Key = "";           // 被替换的模型名，如 "v_rifle"（用于同枪冲突检测）
            public string Kinds = "";         // 影响面：模型,音效,HUD图标,地图...
            public int Entries = 0;
            public bool Ok = false;
        }

        // 特征串 -> 显示名。顺序即优先级：越具体的排在越前面。
        private static readonly string[,] Dict = new string[,]
        {
            {"desert_rifle",     "三连发"},
            {"rifle_ak47",       "AK-47"},
            {"rifle_m60",        "M60"},
            {"machinegun_m60",   "M60"},
            {"smg_mp5",          "MP5"},
            {"smg_silenced",     "消音冲锋枪"},
            {"silenced_smg",     "消音冲锋枪"},
            {"sniper_military",  "鸟狙"},
            {"sniper_scout",     "鸟狙"},
            {"sniper_awp",       "AWP"},
            {"snip_awp",         "AWP"},
            {"awp",              "AWP"},
            {"shotgun_chrome",   "铁喷"},
            {"shotgun_spas",     "二代连喷"},
            {"pumpshotgun",      "木喷"},
            {"autoshotgun",      "一代连喷"},
            {"huntingrifle",     "猎枪"},
            {"hunting_rifle",    "猎枪"},
            {"grenade_launcher", "榴弹发射器"},
            {"pistol_magnum",    "马格南"},
            {"machinegun",       "机枪"},
            {"rifle_m16",        "M16"},
            {"mac-10",           "MAC-10"},
            {"mac10",            "MAC-10"},
            {"concrete_spawnchunk", "石头"},
            {"survivor_namvet",  "比尔"},
            {"survivor_teenangst","佐伊"},
            {"survivor_manager", "路易斯"},
            {"survivor_biker",   "弗朗西斯"},
            {"survivor_producer","教练"},
            {"survivor_gambler", "尼克"},
            {"survivor_mechanic","埃利斯"},
            {"survivor",         "幸存者"},
            {"painpills",        "止痛药"},
            {"pain_pills",       "止痛药"},
            {"adrenaline",       "肾上腺素"},
            {"firstaid",         "医疗包"},
            {"medkit",           "医疗包"},
            {"molotov",          "燃烧瓶"},
            {"pipebomb",         "土制炸弹"},
            {"gascan",           "油桶"},
            {"fryingpan",        "平底锅"},
            {"grenade",          "投掷物"},
            {"katana",           "武士刀"},
            {"machete",          "砍刀"},
            {"crowbar",          "撬棍"},
            {"ammo",             "弹药"},
            {"hulk",             "Tank"},
            {"boomer",           "Boomer"},
            {"hunter",           "Hunter"},
            {"smoker",           "Smoker"},
            {"jockey",           "Jockey"},
            {"spitter",          "Spitter"},
            {"charger",          "Charger"},
            {"witch",            "Witch"},
            {"tank",             "Tank"},
            {"v_rifle",          "M16"},
            {"v_smg",            "冲锋枪"},
            {"v_pistol",         "手枪"},
            {"m16",              "M16"},
            {"ak47",             "AK-47"},
            {"m60",              "M60"},
            {"mp5",              "MP5"},
            {"uzi",              "UZI"},
            {"pistol",           "手枪"},
            {"smg",              "冲锋枪"},
            {"shotgun",          "霰弹枪"},
            {"rifle",            "步枪"},
            {"melee",            "近战武器"},
            {"pills",            "药丸"},
        };

        private static string Lookup(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            for (int i = 0; i < Dict.GetLength(0); i++)
                if (s.IndexOf(Dict[i, 0], StringComparison.Ordinal) >= 0) return Dict[i, 1];
            return null;
        }

        // 通用效果贴图不能用来判断对象
        private static bool MatUsable(string p)
        {
            return !(p.StartsWith("materials/particle/", StringComparison.Ordinal)
                  || p.StartsWith("materials/particles/", StringComparison.Ordinal)
                  || p.StartsWith("materials/decals/", StringComparison.Ordinal)
                  || p.StartsWith("materials/vgui/", StringComparison.Ordinal)
                  || p.StartsWith("materials/sprites/", StringComparison.Ordinal)
                  || p.StartsWith("materials/effects/", StringComparison.Ordinal)
                  || p.StartsWith("materials/detail/", StringComparison.Ordinal));
        }

        public static Result Analyze(string vpkFile)
        {
            Result r = new Result();
            if (string.IsNullOrEmpty(vpkFile) || !File.Exists(vpkFile)) return r;

            List<string> mdls = new List<string>();
            List<string> huds = new List<string>();
            List<string> snds = new List<string>();
            List<string> mats = new List<string>();
            bool map = false, script = false, particle = false, hasMat = false;

            try
            {
                Vpk.Walk(vpkFile, 200000, delegate(string p)
                {
                    r.Entries++;
                    if (p.EndsWith(".mdl", StringComparison.Ordinal))
                    {
                        int s = p.LastIndexOf('/');
                        if (s >= 0) mdls.Add(p.Substring(s + 1, p.Length - s - 5));
                    }
                    else if (p.StartsWith("materials/vgui/hud/icon_", StringComparison.Ordinal) && p.EndsWith(".vtf", StringComparison.Ordinal))
                    {
                        huds.Add(p.Substring("materials/vgui/hud/icon_".Length, p.Length - 4 - "materials/vgui/hud/icon_".Length));
                    }
                    else if (p.StartsWith("sound/weapons/", StringComparison.Ordinal))
                    {
                        int a = "sound/weapons/".Length;
                        int b = p.IndexOf('/', a);
                        if (b > a) snds.Add(p.Substring(a, b - a));
                    }
                    else if (p.StartsWith("materials/", StringComparison.Ordinal))
                    {
                        hasMat = true;
                        // 通用效果贴图（血液、贴花、HUD、精灵图）对所有对象生效，不能拿来判断改了什么。
                        // 「彩色血液」的路径里有 smoker_tongue 就被判成 Smoker —— 那是血，不是 smoker。
                        if (MatUsable(p) && mats.Count < 40) mats.Add(p);
                    }
                    else if (p.StartsWith("maps/", StringComparison.Ordinal) && p.EndsWith(".bsp", StringComparison.Ordinal)) map = true;
                    else if (p.StartsWith("scripts/", StringComparison.Ordinal)) script = true;
                    else if (p.StartsWith("particles/", StringComparison.Ordinal)) particle = true;
                });
            }
            catch { return r; }

            // 影响面
            List<string> kinds = new List<string>();
            if (map) kinds.Add("地图");
            if (mdls.Count > 0) kinds.Add("模型");
            if (huds.Count > 0) kinds.Add("HUD");
            if (snds.Count > 0) kinds.Add("音效");
            if (particle) kinds.Add("特效");
            if (script) kinds.Add("脚本");
            if (kinds.Count == 0 && hasMat) kinds.Add("材质");
            r.Kinds = string.Join(",", kinds.ToArray());
            r.Ok = r.Entries > 0;

            // 1) 模型文件名 —— 最硬的证据，换的就是它
            List<string> found = new List<string>();
            foreach (string m in mdls)
            {
                string n = Lookup(m);
                if (n != null)
                {
                    if (!found.Contains(n)) found.Add(n);
                    if (r.Key.Length == 0) r.Key = m;
                    if (r.Level.Length == 0) r.Level = "模型";
                }
            }
            // 2) HUD 图标名 —— 必须指名道姓才换得掉
            if (found.Count == 0)
            {
                foreach (string h in huds)
                {
                    string n = Lookup(h);
                    if (n != null)
                    {
                        if (!found.Contains(n)) found.Add(n);
                        if (r.Key.Length == 0) r.Key = h;
                        if (r.Level.Length == 0) r.Level = "HUD图标";
                    }
                }
            }
            // 3) 音效目录
            if (found.Count == 0)
            {
                foreach (string s2 in snds)
                {
                    string n = Lookup(s2);
                    if (n != null)
                    {
                        if (!found.Contains(n)) found.Add(n);
                        if (r.Key.Length == 0) r.Key = s2;
                        if (r.Level.Length == 0) r.Level = "音效";
                    }
                }
            }
            // 4) 材质路径（弱信号，只在前三级都没中时才用）
            if (found.Count == 0)
            {
                foreach (string mp in mats)
                {
                    string n = Lookup(mp);
                    if (n != null)
                    {
                        if (!found.Contains(n)) found.Add(n);
                        if (r.Level.Length == 0) r.Level = "材质";
                    }
                }
            }

            // 一个包里塞了好几样东西时，别只报第一个 —— 「彩色血液」被报成 Smoker 会误导
            if (found.Count == 0) r.Subject = "";
            else if (found.Count <= 2) r.Subject = string.Join("/", found.ToArray());
            else if (r.Level == "材质") r.Subject = "";          // 材质推断本来就不准，多对象干脆不说
            else r.Subject = found.Count + " 件";
            return r;
        }

        // ---------------- 缓存：按「相对路径 + 大小 + 修改时间」判断是否失效 ----------------
        private static Dictionary<string, string> cache = null;

        private static string CacheFile
        {
            // 文件名带版本号：改了识别规则就把版本 +1，旧缓存自动作废，不会读到过时的结论
            get { return Path.Combine(Core.LogDir, "identify-cache-v5.tsv"); }
        }

        private static void LoadCache()
        {
            if (cache != null) return;
            cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(CacheFile)) return;
                foreach (string line in File.ReadAllLines(CacheFile, Encoding.UTF8))
                {
                    string[] c = line.Split('\t');
                    if (c.Length >= 6) cache[c[0]] = line;
                }
            }
            catch { }
        }

        private static string Stamp(string file)
        {
            try
            {
                FileInfo fi = new FileInfo(file);
                return fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            }
            catch { return "0|0"; }
        }

        // ============ 内容分类：只读目录表，判"大类型" ============
        // 原则：有把握才返回 true；没把握交回目录规则（不猜）
        private static readonly string[,] GunKeys = new string[,]
        {
            {"rifle_ak47","AK47"},{"ak47","AK47"},
            {"smg_mp5","MP5"},{"mp5","MP5"},
            {"rifle_m16","M16"},{"m16","M16"},
            {"rifle_desert","三连发"},{"desert_rifle","三连发"},
            {"scar","scar"},{"sg552","sg552"},{"uzi","uzi"},{"mac10","MAC-10"},{"mac-10","MAC-10"},
            {"m60","M60"},{"machinegun","机枪"},
            {"sniper_awp","AWP"},{"awp","AWP"},
            {"sniper_scout","鸟狙"},{"sniper_military","鸟狙"},{"huntingrifle","鸟狙"},
            {"pumpshotgun","木喷"},{"shotgun_pump","木喷"},
            {"shotgun_chrome","铁喷"},{"chrome","铁喷"},
            {"autoshotgun","一代连喷"},{"shotgun_spas","二代连喷"},
            {"grenade_launcher","榴弹发射器"},{"pistol_magnum","马格南"},
            {"katana","近战"},{"machete","近战"},{"crowbar","近战"},{"fireaxe","近战"}
        };

        private static readonly string[,] CharKeys = new string[,]
        {
            {"survivor_namvet","比尔"},{"survivor_teenangst","佐伊"},{"survivor_manager","路易斯"},
            {"survivor_biker","弗朗西斯"},{"survivor_producer","教练"},{"survivor_gambler","尼克"},
            {"survivor_mechanic","埃利斯"},{"survivor_coach","教练"}
        };

        private static bool ClassifyByPaths(List<string> ps, out string cat, out string sub)
        {
            cat = ""; sub = "";
            if (ps.Count == 0) return false;

            // 1) 地图：有 maps/*.bsp
            foreach (string p in ps)
                if (p.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase) && (p.StartsWith("maps/") || p.IndexOf("/maps/") >= 0))
                { cat = "地图"; sub = "单张地图"; return true; }

            // 2) 人物：幸存者模型
            foreach (string p in ps)
            {
                if (p.IndexOf("survivor_", StringComparison.OrdinalIgnoreCase) < 0) continue;
                for (int i = 0; i < CharKeys.GetLength(0); i++)
                    if (p.IndexOf(CharKeys[i, 0], StringComparison.OrdinalIgnoreCase) >= 0)
                    { cat = "人物"; sub = CharKeys[i, 1]; return true; }
                cat = "人物"; sub = "其他"; return true;
            }

            // 3) 药品：医疗包/止痛药/肾上腺素 的模型或图标
            foreach (string p in ps)
            {
                if (p.IndexOf("medkit", StringComparison.OrdinalIgnoreCase) >= 0) { cat = "药品"; sub = "医疗包"; return true; }
                if (p.IndexOf("painpills", StringComparison.OrdinalIgnoreCase) >= 0) { cat = "药品"; sub = "止痛药"; return true; }
                if (p.IndexOf("adrenaline", StringComparison.OrdinalIgnoreCase) >= 0) { cat = "药品"; sub = "肾上腺素"; return true; }
                if (p.IndexOf("defibrillator", StringComparison.OrdinalIgnoreCase) >= 0) { cat = "药品"; sub = "医疗包"; return true; }
            }

            // 4) 枪械：模型名命中已知枪种
            foreach (string p in ps)
            {
                if (!p.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)) continue;
                int s2 = p.LastIndexOf('/');
                string bn = s2 >= 0 ? p.Substring(s2 + 1) : p;
                for (int i = 0; i < GunKeys.GetLength(0); i++)
                    if (bn.IndexOf(GunKeys[i, 0], StringComparison.OrdinalIgnoreCase) >= 0)
                    { cat = "枪械"; sub = GunKeys[i, 1]; return true; }
            }
            return false;
        }

        /// <summary>散装 vpk：读目录表判类型</summary>
        public static bool ClassifyByContent(string vpkPath, out string cat, out string sub)
        {
            cat = ""; sub = "";
            List<string> ps = new List<string>();
            try
            {
                Vpk.Walk(vpkPath, 30000, delegate(string p) { if (ps.Count < 30000) ps.Add(p.ToLowerInvariant()); });
            }
            catch { return false; }
            return ClassifyByPaths(ps, out cat, out sub);
        }

        /// <summary>zip 里的 vpk：只解压前几 MB 的目录表，不解整包</summary>
        public static bool ClassifyByContentZip(string zipPath, string innerPath, out string cat, out string sub)
        {
            cat = ""; sub = "";
            try
            {
                using (System.IO.Compression.ZipArchive za = System.IO.Compression.ZipFile.OpenRead(zipPath))
                {
                    System.IO.Compression.ZipArchiveEntry hit = null;
                    string want = innerPath.Replace('\\', '/');
                    foreach (System.IO.Compression.ZipArchiveEntry e in za.Entries)
                        if (string.Equals(e.FullName.Replace('\\', '/'), want, StringComparison.OrdinalIgnoreCase)) { hit = e; break; }
                    if (hit == null) return false;
                    using (Stream s = hit.Open())
                    {
                        byte[] hdr = new byte[12];
                        if (!ReadFull(s, hdr, 12)) return false;
                        uint sig = BitConverter.ToUInt32(hdr, 0);
                        uint tree = BitConverter.ToUInt32(hdr, 8);
                        if (sig != 0x55AA1234 || tree < 8 || tree > 16u * 1024 * 1024) return false;
                        byte[] buf = new byte[tree];
                        if (!ReadFull(s, buf, (int)tree)) return false;
                        List<string> ps = new List<string>();
                        Vpk.WalkBuffer(buf, 30000, delegate(string p) { if (ps.Count < 30000) ps.Add(p.ToLowerInvariant()); });
                        return ClassifyByPaths(ps, out cat, out sub);
                    }
                }
            }
            catch { return false; }
        }

        private static bool ReadFull(Stream s, byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(buf, got, n - got);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        public static Result AnalyzeCached(string relPath, string fullPath)
        {
            LoadCache();
            string stamp = Stamp(fullPath);
            string old;
            if (cache.TryGetValue(relPath, out old))
            {
                string[] c = old.Split('\t');
                if (c.Length >= 6 && c[1] == stamp)
                {
                    Result r2 = new Result();
                    r2.Ok = true;
                    r2.Subject = c[2];
                    r2.Level = c[3];
                    r2.Key = c[4];
                    r2.Kinds = c[5];
                    return r2;
                }
            }
            Result r = Analyze(fullPath);
            try
            {
                cache[relPath] = relPath + "\t" + stamp + "\t" + r.Subject + "\t" + r.Level + "\t" + r.Key + "\t" + r.Kinds;
            }
            catch { }
            return r;
        }

        public static void SaveCache()
        {
            if (cache == null) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (string v in cache.Values) sb.AppendLine(v);
                File.WriteAllText(CacheFile, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }
}

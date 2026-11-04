using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            public bool Only1P = false;       // 模型全是第一人称 v_，世界模型没动
            public int Entries = 0;
            public string Assets = "";        // 归组证据：包内模型/材质完整路径，分号分隔
            public bool Ok = false;
        }

        // 特征串 -> 显示名。顺序即优先级：越具体的排在越前面。
        // 名字对齐 L4D2 官方武器名：v_ / w_ 两个前缀靠同一段特征串归到同一件东西，
        // 否则 w_shotgun + v_pumpshotgun 会被当成两件，Subject 变成「霰弹枪/木喷」。
        private static readonly string[,] Dict = new string[,]
        {
            // ---- 特感：先判，免得被下面的人名/枪名抢走
            {"hulk",             "Tank"},
            {"boomer",           "Boomer"},
            {"hunter",           "Hunter"},
            {"smoker",           "Smoker"},
            {"jockey",           "Jockey"},
            {"spitter",          "Spitter"},
            {"charger",          "Charger"},
            {"witch",            "Witch"},
            {"tank",             "Tank"},

            // ---- 幸存者：L4D2 四人 + L4D1 四人。具体模型名必须排在 "survivor" 之前
            {"survivor_namvet",   "比尔"},
            {"survivor_teenangst","佐伊"},
            {"survivor_manager",  "路易斯"},
            {"survivor_biker",    "弗朗西斯"},
            {"survivor_coach",    "教练"},
            {"survivor_producer", "罗谢尔"},     // producer = 新闻制片人 = Rochelle（原来错写成教练）
            {"survivor_mechanic", "埃利斯"},
            {"survivor_gambler",  "尼克"},
            {"survivor",          "幸存者"},

            // ---- 药品
            {"painpills",        "止痛药"},
            {"pain_pills",       "止痛药"},
            {"adrenaline",       "肾上腺素"},
            {"defibrillator",    "电击器"},
            {"firstaid",         "医疗包"},
            {"medkit",           "医疗包"},

            // ---- 投掷物
            {"vomitjar",         "胆汁"},
            {"vomit_jar",        "胆汁"},
            {"molotov",          "燃烧瓶"},
            {"pipebomb",         "土制炸弹"},
            {"grenade_launcher", "榴弹发射器"},

            // ---- 枪械：具体型号在前，泛称在后
            {"rifle_ak47",       "AK-47"},
            {"ak47",             "AK-47"},
            {"rifle_desert",     "SCAR-L"},      // 三连发 = Desert Rifle，玩家都按 SCAR 叫
            {"desert_rifle",     "SCAR-L"},
            {"rif_sg552",        "sg552"},       // v_ 版模型名是缩写 rif_sg552
            {"rifle_sg552",      "sg552"},
            {"sg552",            "sg552"},
            {"rifle_m60",        "M60"},
            {"machinegun_m60",   "M60"},
            {"m60",              "M60"},
            {"smg_mp5",          "MP5"},
            {"mp5",              "MP5"},
            {"smg_silenced",     "MAC-10"},
            {"silenced_smg",     "MAC-10"},
            {"mac-10",           "MAC-10"},
            {"mac10",            "MAC-10"},
            {"smg",              "UZI"},         // 裸 smg = v_smg = UZI
            {"uzi",              "UZI"},
            {"sniper_military",  "30发军用狙"},
            {"huntingrifle",     "15发木狙"},
            {"hunting_rifle",    "15发木狙"},
            {"sniper_scout",     "鸟狙"},
            {"sniper_awp",       "AWP"},
            {"snip_awp",         "AWP"},
            {"awp",              "AWP"},
            {"shotgun_chrome",   "铁喷"},
            {"shotgun_spas",     "二代连喷"},
            {"pumpshotgun",      "木喷"},
            {"autoshotgun",      "一代连喷"},
            {"shotgun",          "木喷"},        // 裸 shotgun = v_shotgun = 一代木喷的模型
            {"rifle",            "M16"},         // 裸 rifle = v_rifle = M16
            {"machinegun",       "M60"},
            {"pistol_magnum",    "马格南"},
            {"magnum",           "马格南"},
            {"pistol",           "手枪"},
            {"melee",            "近战武器"},

            // ---- 近战具体型号
            {"katana",           "武士刀"},
            {"machete",          "砍刀"},
            {"crowbar",          "撬棍"},
            {"fireaxe",          "消防斧"},
            {"fryingpan",        "平底锅"},

            // ---- 其余杂项
            {"concrete_spawnchunk", "石头"},
            {"ammo",             "弹药"},
            {"gascan",           "油桶"},
            {"pills",            "药丸"},
        };

        /// <summary>命中哪条特征串。同一把枪的 v_/w_/light 变体靠它归到一起。</summary>
        private static string Feature(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            for (int i = 0; i < Dict.GetLength(0); i++)
                if (s.IndexOf(Dict[i, 0], StringComparison.Ordinal) >= 0) return Dict[i, 0];
            return null;
        }

        /// <summary>判断「抢的是哪个位置」用的名字：用特征串而不是原始模型名，</summary>
        /// <summary>否则 survivor_biker 和 survivor_biker_light 会被当成两个位置。</summary>
        private static string KeyOf(string raw)
        {
            string f = Feature(raw);
            return f != null ? f : raw;
        }

        private static string Lookup(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            for (int i = 0; i < Dict.GetLength(0); i++)
                if (s.IndexOf(Dict[i, 0], StringComparison.Ordinal) >= 0) return Dict[i, 1];
            return null;
        }

        /// <summary>官方目标路径不是「同一套」的私有证据：所有 AK 皮肤都写同一个 v_rifle_ak47.mdl。</summary>
        internal static bool IsStockAsset(string p)
        {
            if (string.IsNullOrEmpty(p)) return true;
            p = p.Replace('\\', '/').ToLowerInvariant();
            if (p.EndsWith(".mdl", StringComparison.Ordinal))
            {
                string n = Path.GetFileNameWithoutExtension(p);
                return Lookup(n) != null;
            }
            if (!p.StartsWith("materials/", StringComparison.Ordinal)) return false;
            if (!MatUsable(p)) return true;
            // 官方模型材质目录里，只要路径确实带官方对象名，就不能当某一套 mod 的私有证据；
            // materials/tpp_wing_medkit 这类独立自定义根目录不受这个规则影响。
            return p.StartsWith("materials/models/", StringComparison.Ordinal) && Lookup(p) != null;
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

        /// <summary>扫一个 vpk 的目录表，攒出「改了什么」。散装 vpk 和压缩包里的 vpk 走同一套。</summary>
        private sealed class Scan
        {
            public readonly List<string> Mdls = new List<string>();
            public readonly List<string> Huds = new List<string>();
            public readonly List<string> Snds = new List<string>();
            public readonly List<string> Mats = new List<string>();
            public readonly List<string> Assets = new List<string>();
            private readonly HashSet<string> assetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private bool assetOverflow;
            public bool Map, Script, Particle, HasMat;
            public int Entries;

            public void Feed(string p)
            {
                Entries++;
                p = (p ?? "").Replace('\\', '/').ToLowerInvariant();
                // 只留归组需要的模型和材质路径；不读文件内容，也不把整张 VPK 目录塞进缓存。
                // 超过上限就放弃内容归组并走名称兜底，绝不能拿半份路径做「相同/包含」判断。
                if (!assetOverflow && (p.EndsWith(".mdl", StringComparison.Ordinal)
                    || (p.StartsWith("materials/", StringComparison.Ordinal)
                        && (p.EndsWith(".vmt", StringComparison.Ordinal) || p.EndsWith(".vtf", StringComparison.Ordinal)))))
                {
                    if (assetSet.Add(p)) Assets.Add(p);
                    if (Assets.Count > 4000) { assetOverflow = true; Assets.Clear(); assetSet.Clear(); }
                }
                if (p.EndsWith(".mdl", StringComparison.Ordinal))
                {
                    int s = p.LastIndexOf('/');
                    if (s >= 0) Mdls.Add(p.Substring(s + 1, p.Length - s - 5));
                }
                else if (p.StartsWith("materials/vgui/hud/icon_", StringComparison.Ordinal) && p.EndsWith(".vtf", StringComparison.Ordinal))
                {
                    Huds.Add(p.Substring("materials/vgui/hud/icon_".Length, p.Length - 4 - "materials/vgui/hud/icon_".Length));
                }
                else if (p.StartsWith("sound/weapons/", StringComparison.Ordinal))
                {
                    int a = "sound/weapons/".Length;
                    int b = p.IndexOf('/', a);
                    if (b > a) Snds.Add(p.Substring(a, b - a));
                }
                else if (p.StartsWith("materials/", StringComparison.Ordinal))
                {
                    HasMat = true;
                    // 通用效果贴图（血液、贴花、HUD、精灵图）对所有对象生效，不能拿来判断改了什么。
                    // 「彩色血液」的路径里有 smoker_tongue 就被判成 Smoker —— 那是血，不是 smoker。
                    if (MatUsable(p) && Mats.Count < 40) Mats.Add(p);
                }
                else if (p.StartsWith("maps/", StringComparison.Ordinal) && p.EndsWith(".bsp", StringComparison.Ordinal)) Map = true;
                else if (p.StartsWith("scripts/", StringComparison.Ordinal)) Script = true;
                else if (p.StartsWith("particles/", StringComparison.Ordinal)) Particle = true;
            }

            public Result Finish()
            {
                Result r = new Result();
                r.Entries = Entries;
                if (!assetOverflow)
                {
                    Assets.Sort(StringComparer.OrdinalIgnoreCase);
                    r.Assets = string.Join(";", Assets.ToArray());
                }

                // 1) 模型文件名 —— 最硬的证据，换的就是它
                List<string> found = new List<string>();
                foreach (string m in Mdls)
                {
                    string n = Lookup(m);
                    if (n != null)
                    {
                        if (!found.Contains(n)) found.Add(n);
                        if (r.Key.Length == 0) r.Key = KeyOf(m);
                        if (r.Level.Length == 0) r.Level = "模型";
                    }
                }

                // 模型全是 v_（第一人称手持）、一个 w_（世界模型）都没有 = 只改了你看枪的那只手，
                // 世界里的枪还是原版。这条事实单独记在 Only1P 上，不塞进 Kinds ——
                // 「改了什么」要照实说，模型就是模型；能不能叠是另一件事。
                // 必须「全是 v_」：人物、地图包里也常夹一两个 v_ 模型，漏一个 w_ 就不算。
                bool viewOnly = Mdls.Count > 0 && found.Count > 0;
                if (viewOnly)
                    foreach (string m in Mdls)
                        if (!m.StartsWith("v_", StringComparison.OrdinalIgnoreCase)) { viewOnly = false; break; }
                r.Only1P = viewOnly;

                List<string> kinds = new List<string>();
                if (Map) kinds.Add("地图");
                if (Mdls.Count > 0) kinds.Add("模型");
                if (Huds.Count > 0) kinds.Add("HUD");
                if (Snds.Count > 0) kinds.Add("音效");
                if (Particle) kinds.Add("特效");
                if (Script) kinds.Add("脚本");
                if (kinds.Count == 0 && HasMat) kinds.Add("材质");
                r.Kinds = string.Join(",", kinds.ToArray());
                r.Ok = Entries > 0;

                // 2) HUD 图标名 —— 必须指名道姓才换得掉
                if (found.Count == 0)
                {
                    foreach (string h in Huds)
                    {
                        string n = Lookup(h);
                        if (n != null)
                        {
                            if (!found.Contains(n)) found.Add(n);
                            if (r.Key.Length == 0) r.Key = KeyOf(h);
                            if (r.Level.Length == 0) r.Level = "HUD图标";
                        }
                    }
                }
                // 3) 音效目录
                if (found.Count == 0)
                {
                    foreach (string s2 in Snds)
                    {
                        string n = Lookup(s2);
                        if (n != null)
                        {
                            if (!found.Contains(n)) found.Add(n);
                            if (r.Key.Length == 0) r.Key = KeyOf(s2);
                            if (r.Level.Length == 0) r.Level = "音效";
                        }
                    }
                }
                // 4) 材质路径（弱信号，只在前三级都没中时才用）
                if (found.Count == 0)
                {
                    foreach (string mp in Mats)
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
        }

        public static Result Analyze(string vpkFile)
        {
            if (string.IsNullOrEmpty(vpkFile) || !File.Exists(vpkFile)) return new Result();
            Scan sc = new Scan();
            try { Vpk.Walk(vpkFile, 200000, sc.Feed); } catch { return new Result(); }
            return sc.Finish();
        }

        /// <summary>已经拿到的 vpk 目录表（压缩包里的包，只能这样读）。</summary>
        public static Result AnalyzeBuffer(byte[] tree)
        {
            if (tree == null) return new Result();
            Scan sc = new Scan();
            try { Vpk.WalkBuffer(tree, 200000, sc.Feed); } catch { return new Result(); }
            return sc.Finish();
        }

        /// <summary>只把 vpk 的目录表读出来：前 12 字节头 + tree 段，不解压正片。</summary>
        private static byte[] ReadTree(Stream s)
        {
            byte[] hdr = new byte[12];
            if (!ReadFull(s, hdr, 12)) return null;
            uint sig = BitConverter.ToUInt32(hdr, 0);
            uint ver = BitConverter.ToUInt32(hdr, 4);
            uint tree = BitConverter.ToUInt32(hdr, 8);
            if (sig != 0x55AA1234 || tree < 8 || tree > 16u * 1024 * 1024) return null;
            if (ver == 2 && !ReadFull(s, new byte[16], 16)) return null;
            byte[] buf = new byte[tree];
            if (!ReadFull(s, buf, (int)tree)) return null;
            return buf;
        }

        /// <summary>压缩包里的 vpk：把目录表读出来，剩下的跟散装 vpk 一样判。</summary>
        public static byte[] TreeOfArchive(string container, string inner)
        {
            try
            {
                if (container == null || inner == null) return null;
                if (container.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (System.IO.Compression.ZipArchive za = System.IO.Compression.ZipFile.OpenRead(container))
                    {
                        System.IO.Compression.ZipArchiveEntry hit = null;
                        string want = inner.Replace('\\', '/');
                        foreach (System.IO.Compression.ZipArchiveEntry e in za.Entries)
                            if (string.Equals(e.FullName.Replace('\\', '/'), want, StringComparison.OrdinalIgnoreCase)) { hit = e; break; }
                        if (hit == null) return null;
                        using (Stream s = hit.Open()) return ReadTree(s);
                    }
                }
                string rar = Catalog.RarExe();
                if (string.IsNullOrEmpty(rar)) return null;
                ProcessStartInfo psi = new ProcessStartInfo(rar, "p -inul -y \"" + container + "\" \"" + inner + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    // Rar.exe 偶发会卡住（杀软锁包/磁盘停顿）：读目录表放后台线程，15 秒读不完就放弃，
                    // 不然初始化线程会无限期干等，窗口一直是空白。
                    byte[] b = null;
                    System.Threading.Thread rd = new System.Threading.Thread(delegate()
                    { try { b = ReadTree(p.StandardOutput.BaseStream); } catch { } });
                    rd.IsBackground = true;
                    rd.Start();
                    if (!rd.Join(15000)) { Kill(p); return null; }
                    Kill(p);
                    return b;
                }
            }
            catch { return null; }
        }

        /// <summary>压缩包里的 vpk 也走缓存：同一个包没变就不用再解一遍。</summary>
        public static Result AnalyzeArchiveCached(string relPath, string container, string inner)
        {
            LoadCache();
            string stamp = Stamp(container);
            string old;
            if (cache.TryGetValue(relPath, out old))
            {
                string[] c = old.Split('\t');
                if (c.Length >= 9 && c[1] == stamp)
                {
                    Result r2 = ReadResult(c);
                    if (r2 != null) return r2;
                }
            }
            Result r = AnalyzeBuffer(TreeOfArchive(container, inner));
            try { cache[relPath] = CacheLine(relPath, stamp, r); }
            catch { }
            return r;
        }

        // ---------------- 缓存：按「相对路径 + 大小 + 修改时间」判断是否失效 ----------------
        private static Dictionary<string, string> cache = null;

        private static string CacheFile
        {
            // 文件名带版本号：改了识别规则就把版本 +1，旧缓存自动作废，不会读到过时的结论
            get { return Path.Combine(Core.LogDir, "identify-cache-v12.tsv"); }
        }

        private static string PackAssets(string assets)
        {
            try { return Convert.ToBase64String(Encoding.UTF8.GetBytes(assets ?? "")); }
            catch { return ""; }
        }

        private static string UnpackAssets(string packed)
        {
            try { return packed.Length == 0 ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(packed)); }
            catch { return ""; }
        }

        private static Result ReadResult(string[] c)
        {
            if (c.Length < 9) return null;
            Result r = new Result();
            r.Subject = c[2];
            r.Level = c[3];
            r.Key = c[4];
            r.Kinds = c[5];
            r.Only1P = c[6] == "1";
            int entries;
            int.TryParse(c[7], out entries);
            r.Entries = entries;
            r.Assets = UnpackAssets(c[8]);
            r.Ok = entries > 0;
            return r;
        }

        private static string CacheLine(string relPath, string stamp, Result r)
        {
            return relPath + "\t" + stamp + "\t" + r.Subject + "\t" + r.Level + "\t" + r.Key + "\t" + r.Kinds
                 + "\t" + (r.Only1P ? "1" : "0") + "\t" + r.Entries + "\t" + PackAssets(r.Assets);
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
                    if (c.Length >= 9) cache[c[0]] = line;
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
        // 这里只问「是不是枪」，细分名字统一由 ModIdentify.Dict 那套给（Sub 不再各写一份）
        private static readonly string[,] GunKeys = new string[,]
        {
            {"rifle_ak47","AK-47"},{"ak47","AK-47"},
            {"rifle_desert","SCAR-L"},{"desert_rifle","SCAR-L"},{"scar","SCAR-L"},
            {"rif_sg552","sg552"},{"rifle_sg552","sg552"},{"sg552","sg552"},
            {"smg_mp5","MP5"},{"mp5","MP5"},
            {"smg_silenced","MAC-10"},{"mac-10","MAC-10"},{"mac10","MAC-10"},
            {"rifle_m16","M16"},{"m16","M16"},
            {"uzi","UZI"},{"smg","UZI"},
            {"m60","M60"},{"machinegun","M60"},
            {"sniper_awp","AWP"},{"awp","AWP"},
            {"sniper_scout","鸟狙"},{"scout","鸟狙"},
            {"sniper_military","30发军用狙"},{"military","30发军用狙"},
            {"huntingrifle","15发木狙"},
            {"pumpshotgun","木喷"},{"shotgun_pump","木喷"},
            {"shotgun_chrome","铁喷"},
            {"autoshotgun","一代连喷"},{"shotgun_spas","二代连喷"},{"shotgun","木喷"},
            {"grenade_launcher","榴弹发射器"},{"pistol_magnum","马格南"},
            {"katana","近战武器"},{"machete","近战武器"},{"crowbar","近战武器"},
            {"fireaxe","近战武器"},{"melee","近战武器"}
        };

        private static readonly string[,] ThrowKeys = new string[,]
        {
            {"vomitjar","胆汁"},{"vomit_jar","胆汁"},
            {"molotov","燃烧瓶"},{"pipebomb","土制炸弹"},{"grenade","投掷物"}
        };

        private static readonly string[,] CharKeys = new string[,]
        {
            {"survivor_namvet","比尔"},{"survivor_teenangst","佐伊"},{"survivor_manager","路易斯"},
            {"survivor_biker","弗朗西斯"},{"survivor_producer","罗谢尔"},{"survivor_gambler","尼克"},
            {"survivor_mechanic","埃利斯"},{"survivor_coach","教练"}
        };

        // 特感的模型目录就是 models/infected/<名字>/，名字对得上就够
        private static readonly string[,] InfectKeys = new string[,]
        {
            {"hulk","Tank"},{"boomer","Boomer"},{"hunter","Hunter"},{"smoker","Smoker"},
            {"jockey","Jockey"},{"spitter","Spitter"},{"charger","Charger"},{"witch","Witch"},
            {"tank","Tank"}
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

            // 3) 特感：必须有 infected 的模型文件。「特感高亮」那种只改贴图的包不算 ——
            //    它的材质路径里也全是 infected，按路径判会把一堆特效包塞进特感大类。
            foreach (string p in ps)
            {
                if (!p.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)) continue;
                if (p.IndexOf("models/infected/", StringComparison.OrdinalIgnoreCase) < 0) continue;
                for (int i = 0; i < InfectKeys.GetLength(0); i++)
                    if (p.IndexOf(InfectKeys[i, 0], StringComparison.OrdinalIgnoreCase) >= 0)
                    { cat = "特感"; sub = InfectKeys[i, 1]; return true; }
                cat = "特感"; sub = "特感"; return true;
            }

            // 4) 药品：医疗包/止痛药/肾上腺素/电击器 的模型或图标
            foreach (string p in ps)
            {
                if (p.IndexOf("medkit", StringComparison.OrdinalIgnoreCase) >= 0) { cat = "药品"; sub = "医疗包"; return true; }
                if (p.IndexOf("painpills", StringComparison.OrdinalIgnoreCase) >= 0) { cat = "药品"; sub = "止痛药"; return true; }
                if (p.IndexOf("adrenaline", StringComparison.OrdinalIgnoreCase) >= 0) { cat = "药品"; sub = "肾上腺素"; return true; }
                if (p.IndexOf("defibrillator", StringComparison.OrdinalIgnoreCase) >= 0) { cat = "药品"; sub = "电击器"; return true; }
            }

            // 5) 枪械：模型名命中已知枪种
            foreach (string p in ps)
            {
                if (!p.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)) continue;
                int s2 = p.LastIndexOf('/');
                string bn = s2 >= 0 ? p.Substring(s2 + 1) : p;
                for (int i = 0; i < GunKeys.GetLength(0); i++)
                    if (bn.IndexOf(GunKeys[i, 0], StringComparison.OrdinalIgnoreCase) >= 0)
                    { cat = "枪械"; sub = GunKeys[i, 1]; return true; }
            }

            // 6) 投掷物：模型也在武器槽里，算枪械大类下的一个小分类
            foreach (string p in ps)
            {
                if (!p.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)) continue;
                int s3 = p.LastIndexOf('/');
                string bn = s3 >= 0 ? p.Substring(s3 + 1) : p;
                for (int i = 0; i < ThrowKeys.GetLength(0); i++)
                    if (bn.IndexOf(ThrowKeys[i, 0], StringComparison.OrdinalIgnoreCase) >= 0)
                    { cat = "枪械"; sub = ThrowKeys[i, 1]; return true; }
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

        /// <summary>rar 里的 vpk：让 Rar.exe 把文件吐到管道，只读前几百 KB 的目录表就断开（不解整包）</summary>
        public static bool ClassifyByContentRar(string rarPath, string innerPath, out string cat, out string sub)
        {
            cat = ""; sub = "";
            try
            {
                string rar = Catalog.RarExe();
                if (string.IsNullOrEmpty(rar)) return false;
                ProcessStartInfo psi = new ProcessStartInfo(rar, "p -inul -y \"" + rarPath + "\" \"" + innerPath + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    Stream s = p.StandardOutput.BaseStream;
                    byte[] head = new byte[12];
                    if (!ReadFull(s, head, 12)) { Kill(p); return false; }
                    uint sig = BitConverter.ToUInt32(head, 0);
                    uint ver = BitConverter.ToUInt32(head, 4);
                    uint tree = BitConverter.ToUInt32(head, 8);
                    if (sig != 0x55AA1234 || tree < 8 || tree > 16u * 1024 * 1024) { Kill(p); return false; }
                    if (ver == 2 && !ReadFull(s, new byte[16], 16)) { Kill(p); return false; }
                    byte[] buf = new byte[tree];
                    if (!ReadFull(s, buf, (int)tree)) { Kill(p); return false; }
                    Kill(p);
                    List<string> ps = new List<string>();
                    Vpk.WalkBuffer(buf, 30000, delegate(string x) { if (ps.Count < 30000) ps.Add(x.ToLowerInvariant()); });
                    return ClassifyByPaths(ps, out cat, out sub);
                }
            }
            catch { return false; }
        }

        private static void Kill(Process p)
        {
            try { if (!p.HasExited) p.Kill(); } catch { }
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
                        uint ver = BitConverter.ToUInt32(hdr, 4);
                        uint tree = BitConverter.ToUInt32(hdr, 8);
                        if (sig != 0x55AA1234 || tree < 8 || tree > 16u * 1024 * 1024) return false;
                        if (ver == 2 && !ReadFull(s, new byte[16], 16)) return false;
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
                if (c.Length >= 9 && c[1] == stamp)
                {
                    Result r2 = ReadResult(c);
                    if (r2 != null) return r2;
                }
            }
            Result r = Analyze(fullPath);
            try { cache[relPath] = CacheLine(relPath, stamp, r); }
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

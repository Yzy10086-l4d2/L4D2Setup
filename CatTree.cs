using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Installer
{
    // ============================================================
    //  方案树（A 版）
    //  左边按「能替换的东西」列成横向树：枪械 → AK-47 / M16 …
    //  右边是这件东西的候选 MOD，单选。
    //  规则：一个位置只留一个本体，选新的就把同位置原来的取消；
    //        只改第一人称模型 / 音效 / HUD 这类不换本体的，单独一栏，各管各的开关。
    // ============================================================

    /// <summary>一个「能替换的东西」：AK-47 / 医疗包 / 教练 …</summary>
    internal sealed class Slot
    {
        public string Key;                       // 被替换的模型名，如 w_rifle_ak47
        public string Name;                      // 显示名：AK-47
        public string Cat;                       // 大类：枪械 / 医疗 / 人物 …
        public string Sub = "其他";              // 小分类：步枪 / 霰弹枪 / 一代 / 医疗包 …
        public bool Manual;                      // 这个位置是人工指定出来的（可能还要并回自动的同名位置）
        // 换本体的：一个位置只能有一个（同一把枪两套模型会打架）
        public readonly List<ModGroup> Items = new List<ModGroup>();
        // 叠加的扩展：开火音效 / 只改第一人称模型 / HUD 图标 —— 不换本体，可以和上面的同时装
        public readonly List<ModGroup> Extras = new List<ModGroup>();
    }

    /// <summary>把 MOD 库按「改的是什么」聚成槽位。认不出改什么的，不进树。</summary>
    internal static class SlotIndex
    {
        public static List<Slot> Build()
        {
            Dictionary<string, Slot> map = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
            List<Slot> list = new List<Slot>();
            foreach (ModGroup g in Catalog.Groups)
            {
                ModItem m = g.Main;
                if (m == null) continue;
                // 地图能叠好几张、喷漆不占位置 —— 这两种没有「只能一个」的说法，不进树
                if (g.Cat == "地图" || g.Cat == "喷漆") continue;
                // 人工指定过就以人工为准：大类、位置都能改
                string cat = CatPrefs.CatOf(m, g.Cat);
                string hand = CatPrefs.SlotOf(m).Trim();
                string key, nm, hint = "";
                if (hand.Length > 0)
                {
                    key = "slot:" + cat + "/" + hand;
                    nm = hand;
                }
                else
                {
                    key = (m.SubjectKey ?? "").Trim();
                    nm = (m.Subject ?? "").Trim();
                    if (IsCountName(nm)) continue;                 // 「4 件」是「一个包里改了好几样」的上报格式，不是东西
                    nm = SplitKind(nm, out hint);                  // 「霰弹枪/木喷」→ 木喷（枪种当小分类）
                    // 一个包同时改了两件（UZI + MAC-10 共用一套动画，作者常一起换），位置只能占一个：
                    // 用户把它放在哪个目录，就归哪个 —— 目录名比「A/B」这种并列表述有用。
                    if (nm.IndexOf('/') >= 0)
                    {
                        string c0 = Concrete("", g, m);
                        if (c0.Length > 0 && HasHalf(nm, c0)) { nm = c0; key = "c:" + c0; }
                    }
                    // 只靠材质认出来的名字不能定死：铁喷的贴图就放在 materials/.../shotgun/ 下，
                    // 会被认成木喷。这种时候听目录名。
                    if (m.SubjectLevel == "材质" && cat != "杂项" && cat != "地图" && cat != "喷漆")
                    {
                        string c1 = Concrete(nm, g, m);
                        if (c1.Length > 0) { nm = c1; key = "c:" + c1; }
                    }
                    // 只认出「步枪 / 霰弹枪 / 幸存者」这种泛称时，用目录名或 MOD 名里的具体型号补回来。
                    // 用户自己的目录名（sg552 / 铁喷 / 黑胖Coach）比内容识别准得多。
                    if (Vague(nm))
                    {
                        string c = Concrete(nm, g, m);
                        if (c.Length > 0) { nm = c; key = "c:" + c; }
                    }
                    if (key.Length == 0)
                    {
                        // 什么都没认出来：看目录名是不是个具体型号（「铁喷」这种），是就照它归位
                        if (nm.Length == 0)
                        {
                            string c = Concrete("", g, m);
                            if (c.Length > 0) { nm = c; key = "c:" + c; }
                        }
                        // 人物、投掷物这类没有「模型名」，只有认出来的名字 —— 就用名字当位置
                        if (key.Length == 0)
                        {
                            if (nm.Length == 0) continue;              // 名字都认不出：不进树
                            key = "sub:" + nm;
                        }
                    }
                }
                Slot s;
                // 位置键永远带上大类：枪械的「消音冲锋枪」模型和杂项的「UZI 改视角」用的是同一个模型名，
                // 但它们不是「二选一」的关系（改视角是叠加的），混进一个槽就错了。
                string mapKey = cat + "\u0001" + key;
                if (!map.TryGetValue(mapKey, out s))
                {
                    s = new Slot();
                    s.Key = mapKey;
                    s.Name = nm.Length > 0 ? nm : key;
                    s.Cat = cat;
                    s.Manual = hand.Length > 0;
                    s.Sub = SubOf(cat, s.Name, key, hint, g.Sub);
                    map[mapKey] = s;
                    list.Add(s);
                }
                else if (Vague(s.Name) && nm.Length > 0 && !Vague(nm))
                {
                    s.Name = nm;                               // 「步枪」这种泛称，换成更具体的名字
                    s.Sub = SubOf(cat, s.Name, key, hint, g.Sub);
                }
                if (IsExtra(g)) s.Extras.Add(g); else s.Items.Add(g);
            }
            MergeManual(list);
            MergeSame(list);
            Disambiguate(list);
            return list;
        }

        /// <summary>
        /// 人工写的位置名如果和自动认出来的位置同名（同一个大类），并成一行 ——
        /// 否则「AK-47」会分成人工一个、自动一个，看着像两个位置。
        /// </summary>
        private static void MergeManual(List<Slot> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Slot a = list[i];
                if (!a.Manual) continue;
                for (int j = 0; j < list.Count; j++)
                {
                    if (j == i) continue;
                    Slot b = list[j];
                    if (b.Cat != a.Cat || b.Name != a.Name) continue;
                    if (b.Manual) continue;                   // 两个都是人工的：key 相同，不会发生
                    foreach (ModGroup g in a.Items) if (!b.Items.Contains(g)) b.Items.Add(g);
                    foreach (ModGroup g in a.Extras) if (!b.Extras.Contains(g)) b.Extras.Add(g);
                    list.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// 名字洗干净之后可能撞车：「霰弹枪/木喷」洗成「木喷」，就该和另一个「木喷」并成一个位置。
        /// 同一个大类、同一个小分类、同一个名字 —— 并。
        /// </summary>
        private static void MergeSame(List<Slot> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Slot a = list[i];
                for (int j = 0; j < i; j++)
                {
                    Slot b = list[j];
                    if (b.Cat != a.Cat || b.Sub != a.Sub || b.Name != a.Name) continue;
                    foreach (ModGroup g in a.Items) if (!b.Items.Contains(g)) b.Items.Add(g);
                    foreach (ModGroup g in a.Extras) if (!b.Extras.Contains(g)) b.Extras.Add(g);
                    if (a.Manual) b.Manual = true;
                    list.RemoveAt(i);
                    break;
                }
            }
        }

        // ------------------------------------------------ 小分类

        /// <summary>「枪种/具体名」「人物名/泛称」这种识别残渣，拆开只留具体那一半。</summary>
        internal static string SplitKind(string nm, out string hint)
        {
            hint = "";
            if (string.IsNullOrEmpty(nm)) return "";
            int i = nm.IndexOf('/');
            if (i <= 0 || i >= nm.Length - 1) return nm;
            string a = nm.Substring(0, i).Trim();
            string b = nm.Substring(i + 1).Trim();
            bool ka = KindWord(a), kb = KindWord(b);
            if (ka && !kb) { hint = a; return b; }
            if (kb && !ka) { hint = b; return a; }
            return nm;
        }

        private static readonly string[] KindWords =
        {
            "步枪", "冲锋枪", "霰弹枪", "狙击枪", "手枪", "机枪", "近战", "枪械", "爆炸物",
            "幸存者", "特感", "投掷物", "医疗包", "止痛药", "肾上腺素", "药品", "其他"
        };

        private static bool KindWord(string s)
        {
            // 「消音冲锋枪」这种带前缀的也算枪种词，否则「UZI/消音冲锋枪」拆不开
            foreach (string k in KindWords) if (s.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>小分类：大类下面再分一层（枪械 → 步枪 → AK-47）。</summary>
        public static string SubOf(string cat, string name, string key, string hint, string gsub)
        {
            string n = name ?? "";
            switch (cat)
            {
                case "枪械":
                {
                    string k = GunKindOf(n, key);
                    if (k != "其他枪械") return k;
                    if (hint != null && hint.Length > 0) return hint;   // 名字推不出来，才用「/」前面那个枪种
                    // 还是推不出来时，用识别出来的细分兜一层（人工改名之后常这样）
                    if (IsGunKindName(gsub)) return gsub.Trim();
                    return k;
                }
                case "人物": return CharKindOf(n);
                case "药品": return MedKindOf(n, key);
                case "特感": return "特感";        // 一个特感就是一个位置，不再往下分层
            }
            string s = (gsub ?? "").Trim();
            if (s.Length > 0 && s != "其他") return s;
            return "其他杂项";
        }

        internal static string GunKindOf(string nm, string key)
        {
            string s = ((nm ?? "") + " " + (key ?? "")).ToLowerInvariant();
            // 顺序有讲究：带 rifle 的「15发木狙/鸟狙」得先被狙击认走，否则会被步枪抢
            if (HasAny(s, "awp", "scout", "military", "hunting", "sniper", "鸟狙", "猎枪", "狙击", "狙"))
                return "狙击枪";
            if (HasAny(s, "pump", "chrome", "spas", "autoshotgun", "shotgun", "木喷", "铁喷", "连喷", "霰弹"))
                return "霰弹枪";
            if (HasAny(s, "uzi", "mp5", "mac10", "mac-10", "silenced", "消音", "smg", "冲锋"))
                return "冲锋枪";
            if (HasAny(s, "ak47", "ak-47", "m16", "scar", "sg552", "galil", "m4", "ar15", "ar-15", "desert", "rifle", "步枪"))
                return "步枪";
            // 榴弹发射器带 grenade，必须排在投掷物前面，否则会被当成手雷
            if (HasAny(s, "grenade_launcher", "launcher", "榴弹", "m60", "machinegun", "机枪", "特殊武器"))
                return "特殊武器";
            if (HasAny(s, "molotov", "pipebomb", "vomitjar", "vomit", "grenade", "投掷", "燃烧瓶", "土制", "胆汁"))
                return "投掷物";
            if (HasAny(s, "magnum", "pistol", "手枪", "马格南"))
                return "手枪";
            if (HasAny(s, "katana", "machete", "crowbar", "fireaxe", "武士刀", "砍刀", "撬棍", "消防斧",
                          "平底锅", "斧", "锤", "吉他", "球棒", "近战"))
                return "近战";
            return "其他枪械";
        }

        private static string CharKindOf(string nm)
        {
            string n = nm ?? "";
            if (HasAny(n, "tank", "坦克", "witch", "女巫", "hunter", "smoker", "boomer", "spitter",
                          "jockey", "charger", "特感"))
                return "特感";
            if (HasAny(n, "比尔", "佐伊", "路易斯", "弗朗西斯")) return "一代";
            if (HasAny(n, "教练", "尼克", "埃利斯", "罗谢尔")) return "二代";
            return "其他人物";
        }

        private static string MedKindOf(string nm, string key)
        {
            string s = ((nm ?? "") + " " + (key ?? "")).ToLowerInvariant();
            if (HasAny(s, "medkit", "医疗包", "除颤", "电击", "defib")) return "治疗与复活";
            if (HasAny(s, "pill", "止痛", "adrenaline", "肾上腺", "针")) return "暂时增益";
            return "其他药品";
        }

        private static readonly string[] GunKindNames =
        { "步枪", "冲锋枪", "霰弹枪", "狙击枪", "手枪", "特殊武器", "近战", "投掷物" };

        private static bool IsGunKindName(string s)
        {
            s = (s ?? "").Trim();
            foreach (string k in GunKindNames) if (s == k) return true;
            return false;
        }

        private static bool HasAny(string s, params string[] keys)        {
            foreach (string k in keys)
                if (s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static readonly string[] SubOrder =
        {
            "步枪", "冲锋枪", "霰弹枪", "狙击枪", "手枪", "特殊武器", "近战", "投掷物", "其他枪械",
            "一代", "二代", "特感", "其他人物",
            "治疗与复活", "暂时增益", "其他药品"
        };

        public static int SubRank(string sub)
        {
            for (int i = 0; i < SubOrder.Length; i++) if (SubOrder[i] == sub) return i;
            return 500;
        }

        // 同一小分类里谁排前面：照游戏里武器的默认顺序来（用户也是这么记的）。
        private static readonly string[,] PosOrder = new string[,]
        {
            {"步枪","AK-47"},{"步枪","M16"},{"步枪","sg552"},{"步枪","SCAR-L"},
            {"冲锋枪","UZI"},{"冲锋枪","MAC-10"},{"冲锋枪","MP5"},
            {"霰弹枪","木喷"},{"霰弹枪","铁喷"},{"霰弹枪","一代连喷"},{"霰弹枪","二代连喷"},
            {"狙击枪","AWP"},{"狙击枪","鸟狙"},{"狙击枪","15发木狙"},{"狙击枪","30发军用狙"},
            {"特殊武器","M60"},{"特殊武器","榴弹发射器"},
            {"投掷物","燃烧瓶"},{"投掷物","土制炸弹"},{"投掷物","胆汁"},
            {"手枪","手枪"},{"手枪","马格南"},
            {"近战","近战武器"},{"近战","武士刀"},{"近战","砍刀"},{"近战","撬棍"},
            {"近战","消防斧"},{"近战","平底锅"},
            {"治疗与复活","医疗包"},{"治疗与复活","电击器"},
            {"暂时增益","肾上腺素"},{"暂时增益","止痛药"},
            {"一代","比尔"},{"一代","佐伊"},{"一代","路易斯"},{"一代","弗朗西斯"},
            {"二代","教练"},{"二代","埃利斯"},{"二代","尼克"},{"二代","罗谢尔"},
            {"特感","Boomer"},{"特感","Charger"},{"特感","Hunter"},{"特感","Jockey"},
            {"特感","Smoker"},{"特感","Spitter"},{"特感","Tank"},{"特感","Witch"},
        };

        public static int NameRank(string sub, string name)
        {
            for (int i = 0; i < PosOrder.GetLength(0); i++)
                if (PosOrder[i, 0] == sub && PosOrder[i, 1] == name) return i;
            return 1000;
        }

        /// <summary>两把不同的枪可能都叫「鸟狙」，加个尾巴区分开，否则树里看着像重复项。</summary>
        private static void Disambiguate(List<Slot> list)
        {
            Dictionary<string, int> freq = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Slot s in list)
            {
                string k = s.Cat + "\u0001" + s.Name;      // 不同大类下同名不算重复
                int n;
                freq.TryGetValue(k, out n);
                freq[k] = n + 1;
            }
            foreach (Slot s in list)
            {
                int n;
                if (!freq.TryGetValue(s.Cat + "\u0001" + s.Name, out n) || n < 2) continue;
                s.Name = s.Name + " · " + Tag(s.Key);
            }
        }

        private static string Tag(string key)
        {
            string k = key;
            if (k.StartsWith("w_", StringComparison.OrdinalIgnoreCase)) k = k.Substring(2);
            else if (k.StartsWith("v_", StringComparison.OrdinalIgnoreCase)) k = k.Substring(2);
            int us = k.LastIndexOf('_');
            if (us >= 0 && us < k.Length - 1) k = k.Substring(us + 1);
            return k.ToUpperInvariant();
        }

        /// <summary>这个 MOD 替换的位置名（人工优先，其次识别结果）。搜索和树共用一套说法。</summary>
        public static string SlotNameOf(ModGroup g)
        {
            ModItem m = g == null ? null : g.Main;
            if (m == null) return "";
            string hand = CatPrefs.SlotOf(m).Trim();
            if (hand.Length > 0) return hand;
            string nm = (m.Subject ?? "").Trim();
            return IsCountName(nm) ? "" : nm;
        }

        internal static bool IsCountName(string s)
        {
            if (s == null || s.Length < 2 || s[s.Length - 1] != '件') return false;
            for (int i = 0; i < s.Length - 1; i++)
                if (!char.IsDigit(s[i]) && s[i] != ' ') return false;
            return true;
        }

        internal static bool Vague(string s)
        {
            return s == "步枪" || s == "冲锋枪" || s == "霰弹枪" || s == "投掷物"
                || s == "手枪" || s == "机枪" || s == "狙击枪" || s == "特殊武器"
                || s == "爆炸物" || s == "其他枪械" || s == "其他枪"
                || s == "幸存者" || s == "人物" || s == "近战" || s == "近战武器"
                || s == "其他" || s.Length == 0;
        }

        /// <summary>
        /// 这件东西是「叠加的扩展」还是「换本体的」。
        /// 依据是识别出来的「改了什么」，外加一条事实：包里有没有动世界模型。
        ///   只换了第一人称手持模型（v_，世界里的 w_ 一个没动）、音效、HUD 图标 = 可以叠上去；
        ///   模型、材质（贴图）= 换外观，一个位置只能有一套，两套会互相覆盖。
        /// </summary>
        public static bool IsExtra(ModGroup g)
        {
            if (g == null) return false;
            ModItem m = g.Main;
            if (m == null) return false;
            if (m.Only1P) return true;                                         // 只动了第一人称，和整套模型不抢
            string k = (m.Kinds ?? "").Trim();
            if (k.Length == 0) return false;                                   // 什么都读不出来：当本体，别乱叠
            if (k.IndexOf("模型", StringComparison.Ordinal) >= 0) return false;
            if (k.IndexOf("材质", StringComparison.Ordinal) >= 0) return false;  // 贴图替换也是换外观
            if (k.IndexOf("地图", StringComparison.Ordinal) >= 0) return false;
            return k.IndexOf("音效", StringComparison.Ordinal) >= 0
                || k.IndexOf("HUD", StringComparison.Ordinal) >= 0;
        }

        // 泛称补全用：关键词 → 标准位置名。长的排前面，免得 "M4" 抢走 "M4A1"。
        private static readonly string[,] GunPairs = new string[,]
        {
            {"30发连狙","30发军用狙"},{"30连","30发军用狙"},{"军用狙","30发军用狙"},
            {"15发连狙","15发木狙"},{"15连","15发木狙"},{"木狙","15发木狙"},
            {"一代连喷","一代连喷"},{"二代连喷","二代连喷"},
            {"sg552","sg552"},
            {"AK-47","AK-47"},{"AK47","AK-47"},
            {"SCAR","SCAR-L"},{"三连发","SCAR-L"},
            {"AR15","AR15"},{"M4A1","M4"},{"M249","M249"},
            {"M16","M16"},{"AWP","AWP"},{"MP5","MP5"},{"M60","M60"},
            {"UZI","UZI"},{"MAC-10","MAC-10"},{"MAC10","MAC-10"},{"P90","P90"},
            {"铁喷","铁喷"},{"铁盆","铁喷"},{"木喷","木喷"},
            {"鸟狙","鸟狙"},{"猎枪","15发木狙"},{"连狙","30发军用狙"},
            {"榴弹","榴弹发射器"},
            {"燃烧瓶","燃烧瓶"},{"土制","土制炸弹"},{"胆汁","胆汁"},
            {"武士刀","武士刀"},{"砍刀","砍刀"},{"撬棍","撬棍"},{"平底锅","平底锅"},{"马格南","马格南"},
        };

        private static readonly string[,] CharPairs = new string[,]
        {
            {"弗朗西斯","弗朗西斯"},{"Francis","弗朗西斯"},
            {"埃利斯","埃利斯"},{"Ellis","埃利斯"},
            {"路易斯","路易斯"},{"Louis","路易斯"},
            {"罗谢尔","罗谢尔"},{"Rochelle","罗谢尔"},
            {"教练","教练"},{"Coach","教练"},
            {"尼克","尼克"},{"Nick","尼克"},
            {"佐伊","佐伊"},{"Zoey","佐伊"},
            {"比尔","比尔"},{"Bill","比尔"},
        };

        /// <summary>「UZI/MAC-10」里是不是正好有 MAC-10 这一半。</summary>
        private static bool HasHalf(string nm, string one)
        {
            if (string.IsNullOrEmpty(nm) || string.IsNullOrEmpty(one)) return false;
            foreach (string p in nm.Split('/'))
                if (p.Trim() == one) return true;
            return false;
        }

        /// <summary>泛称 → 具体位置名。先看 MOD 所在目录，再看 MOD 自己的名字。</summary>
        private static string Concrete(string vague, ModGroup g, ModItem m)
        {
            string dir = "";
            string rel = m == null ? "" : (m.RelPath ?? "");
            int i = rel.LastIndexOf('\\');
            if (i > 0)
            {
                int j = rel.LastIndexOf('\\', i - 1);
                dir = j >= 0 ? rel.Substring(j + 1, i - j - 1) : rel.Substring(0, i);
            }
            string text = dir + " " + (g.Name ?? "") + " " + (m == null ? "" : (m.Name ?? ""));
            string hit = null;
            if (vague == "幸存者" || vague == "人物") hit = Find(CharPairs, text);
            if (hit == null) hit = Find(GunPairs, text);
            if (hit == null && vague.Length == 0) hit = Find(CharPairs, text);   // A/B 拆不开时再试人物
            return hit ?? "";
        }

        private static string Find(string[,] pairs, string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            for (int i = 0; i < pairs.GetLength(0); i++)
                if (text.IndexOf(pairs[i, 0], StringComparison.OrdinalIgnoreCase) >= 0) return pairs[i, 1];
            return null;
        }

        /// <summary>把方案树摊平成文本，方便不开界面核对分类对不对（--dumptree）。</summary>
        public static string DumpTree()
        {
            List<Slot> slots = Build();
            List<string> order = CatOrder(slots);
            slots.Sort(delegate(Slot a, Slot b)
            {
                int ia = order.IndexOf(a.Cat), ib = order.IndexOf(b.Cat);
                if (ia != ib) return ia.CompareTo(ib);
                int sa = SubRank(a.Sub), sb2 = SubRank(b.Sub);
                if (sa != sb2) return sa.CompareTo(sb2);
                int na = NameRank(a.Sub, a.Name), nb = NameRank(b.Sub, b.Name);
                if (na != nb) return na.CompareTo(nb);
                int c = string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
                return c != 0 ? c : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (string cat in order)
            {
                List<Slot> inCat = new List<Slot>();
                foreach (Slot s in slots) if (s.Cat == cat) inCat.Add(s);
                if (inCat.Count == 0) continue;
                sb.AppendLine();
                sb.AppendLine("【" + cat + "】");
                // 只有一层分组的大类不画那行标题，跟界面里一致
                int subCount = 0;
                string prev = null;
                foreach (Slot s in inCat) if (s.Sub != prev) { prev = s.Sub; subCount++; }
                bool showSub = subCount > 1;
                string lastSub = null;
                foreach (Slot s in inCat)
                {
                    if (showSub && s.Sub != lastSub) { sb.AppendLine("  〈" + s.Sub + "〉"); lastSub = s.Sub; }
                    sb.AppendLine("    " + s.Name + "  (" + s.Items.Count + " 个"
                        + (s.Extras.Count > 0 ? " + 扩展 " + s.Extras.Count : "") + ")"
                        + (s.Manual ? " [人工]" : ""));
                }
            }
            string f = System.IO.Path.Combine(Core.LogDir, "方案树.txt");
            System.IO.File.WriteAllText(f, sb.ToString(), new System.Text.UTF8Encoding(true));
            return f;
        }

        /// <summary>树里的大类顺序：跟着分类顺序走，地图/喷漆不进树。</summary>
        public static List<string> CatOrder(List<Slot> slots)        {
            List<string> order = new List<string>();
            foreach (string c in CatPrefs.Cats())
                if (c != "地图" && c != "喷漆" && !order.Contains(c)) order.Add(c);
            foreach (string c in Catalog.Cats)
                if (c != "地图" && c != "喷漆" && !order.Contains(c)) order.Add(c);
            foreach (Slot s in slots) if (!order.Contains(s.Cat)) order.Add(s.Cat);
            return order;
        }
    }

    /// <summary>树里的一行：大类标题、小分类标题，或者一个槽位。</summary>
    internal sealed class TreeRow
    {
        public bool IsCat;
        public bool IsSub;       // 小分类标题：枪械 →「步枪」
        public bool IsNote;      // 「未归类」那一行：只是个说明入口
        public string Cat;
        public string Sub;
        public Slot Slot;
    }

    // ============================================================
    //  方案树视图
    // ============================================================
    internal sealed class CatTree : UserControl
    {
        private readonly MainForm mf;
        private readonly Panel scroll;
        private readonly SlotTree body;
        private readonly ListView lv;
        private readonly ListView lvExtra;                 // 下半：可叠加的扩展
        private readonly Label lblTitle, lblCur, lblFoot;
        private readonly Label lblExtraTitle;
        private readonly FlatButton btnPick;

        private List<Slot> slots = new List<Slot>();
        private int unmatched;                       // 一个位置都归不上的 MOD 数
        private Label lblHint;
        private readonly HashSet<string> folded = new HashSet<string>(StringComparer.Ordinal);
        private string selKey = "";

        public string SelectedKey { get { return selKey; } }
        public bool IsFolded(string cat) { return folded.Contains(cat); }

        public CatTree(MainForm owner)
        {
            mf = owner;
            BackColor = Theme.Surface;
            Font = Theme.UI;

            // ---- 左：树 ----
            scroll = new Panel();
            scroll.BackColor = Color.FromArgb(247, 249, 251);
            scroll.AutoScroll = true;
            scroll.BorderStyle = BorderStyle.None;
            Controls.Add(scroll);

            body = new SlotTree(this);
            body.BackColor = Theme.SurfaceAlt;
            scroll.Controls.Add(body);

            // ---- 右上：换本体的候选 ----
            lv = NewCandList("MOD");
            lv.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;      // 右键只是弹菜单，别顺手换掉 MOD
                ListViewItem li = lv.GetItemAt(e.X, e.Y);
                if (li == null) return;
                Choose(li.Tag as ModGroup);
            };
            lv.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) SelectRow(lv, e);
            };
            lv.ContextMenuStrip = mf.BuildTreeMenu(lv);
            lv.Resize += delegate { LayoutCols(); };
            Controls.Add(lv);

            // ---- 右下：可叠加的扩展（开火音效 / 只改第一人称模型 / HUD）----
            lvExtra = NewCandList("扩展");
            lvExtra.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                ListViewItem li = lvExtra.GetItemAt(e.X, e.Y);
                if (li == null) return;
                ToggleExtra(li.Tag as ModGroup);
            };
            lvExtra.Resize += delegate { LayoutCols(); };
            Controls.Add(lvExtra);

            lblTitle = new Label();
            lblTitle.Font = Theme.UIBold;
            lblTitle.ForeColor = Theme.Ink;
            lblTitle.BackColor = Color.Transparent;
            lblTitle.AutoEllipsis = true;
            Controls.Add(lblTitle);

            lblCur = new Label();
            lblCur.Font = Theme.UISmall;
            lblCur.ForeColor = Theme.Ink2;
            lblCur.BackColor = Color.Transparent;
            lblCur.AutoEllipsis = true;
            Controls.Add(lblCur);

            lblFoot = new Label();
            lblFoot.Font = Theme.UISmall;
            lblFoot.ForeColor = Theme.Ink3;
            lblFoot.BackColor = Color.Transparent;
            lblFoot.AutoEllipsis = true;
            Controls.Add(lblFoot);

            lblExtraTitle = new Label();
            lblExtraTitle.Font = Theme.UISmall;
            lblExtraTitle.ForeColor = Theme.Ink3;
            lblExtraTitle.BackColor = Color.Transparent;
            lblExtraTitle.AutoEllipsis = true;
            lblExtraTitle.Visible = false;
            Controls.Add(lblExtraTitle);

            lblHint = new Label();
            lblHint.Font = Theme.UI;
            lblHint.ForeColor = Theme.Ink3;
            lblHint.BackColor = Color.Transparent;
            lblHint.Visible = false;
            Controls.Add(lblHint);

            btnPick = new FlatButton();
            btnPick.Text = "留原版";
            btnPick.SetBounds(0, 0, 66, Theme.H);
            btnPick.Click += delegate { Choose(null); };
            btnPick.Visible = false;          // 列表里已经有「留原版」那一行，按钮就多余了
            Controls.Add(btnPick);

            Resize += delegate { LayoutSelf(); };
            LayoutSelf();
        }

        /// <summary>候选列表的共同外观：三列、圆点状态、不显示表头焦点。</summary>
        private ListView NewCandList(string firstCol)
        {
            ListView v = new ListView();
            v.View = View.Details;
            v.FullRowSelect = true;
            v.MultiSelect = false;
            v.BorderStyle = BorderStyle.None;
            v.BackColor = Theme.Surface;
            v.ForeColor = Theme.Ink;
            v.Font = Theme.UI;
            v.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            v.ShowItemToolTips = true;
            v.HideSelection = false;
            ImageList pad = new ImageList();
            pad.ImageSize = new Size(1, 24);
            pad.ColorDepth = ColorDepth.Depth32Bit;
            v.SmallImageList = pad;
            v.StateImageList = Dots();
            v.Columns.Add(firstCol, 380);
            v.Columns.Add("改了什么", 100);
            v.Columns.Add("大小", 74).TextAlign = HorizontalAlignment.Right;
            return v;
        }

        private static void SelectRow(ListView v, MouseEventArgs e)
        {
            ListViewItem hit = v.GetItemAt(e.X, e.Y);
            if (hit == null || hit.Selected) return;
            v.SelectedItems.Clear();
            hit.Selected = true;
            hit.Focused = true;
        }

        private static ImageList Dots()
        {
            ImageList il = new ImageList();
            il.ImageSize = new Size(14, 14);
            il.ColorDepth = ColorDepth.Depth32Bit;
            il.TransparentColor = Color.Transparent;
            il.Images.Add(Dot(false));
            il.Images.Add(Dot(true));
            return il;
        }

        private static Bitmap Dot(bool on)
        {
            Bitmap b = new Bitmap(14, 14);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(on ? Theme.Accent : Color.FromArgb(184, 192, 200), 1.5f))
                    g.DrawEllipse(p, 2.5f, 2.5f, 8f, 8f);
                if (on)
                    using (SolidBrush br = new SolidBrush(Theme.Accent))
                        g.FillEllipse(br, 5f, 5f, 4f, 4f);
            }
            return b;
        }

        // ---------------------------------------------------------- 构建

        /// <summary>重新分组并铺树。库变了才需要调用。</summary>
        public void Rebuild()
        {
            slots = SlotIndex.Build();
            SortSlots();
            int inTree = 0;
            foreach (Slot s0 in slots) inTree += s0.Items.Count + s0.Extras.Count;
            // 地图和喷漆本来就不该占位置，别把它们算成「认不出」
            int eligible = 0;
            foreach (ModGroup g in Catalog.Groups)
                if (g.Cat != "地图" && g.Cat != "喷漆") eligible++;
            unmatched = Math.Max(0, eligible - inTree);
            BuildRows();
            if (selKey.Length > 0 && selKey != "#unmatched")
            {
                bool ok = false;
                foreach (Slot s in slots) if (s.Key == selKey) { ok = true; break; }
                if (!ok) selKey = "";                       // 分类一改，原来那个位置可能没了
            }
            if (selKey.Length == 0 && slots.Count > 0) selKey = slots[0].Key;
            FillDetail();
        }

        private void SortSlots()
        {
            List<string> order = SlotIndex.CatOrder(slots);
            slots.Sort(delegate(Slot a, Slot b)
            {
                int ia = order.IndexOf(a.Cat), ib = order.IndexOf(b.Cat);
                if (ia != ib) return ia.CompareTo(ib);
                int sa = SlotIndex.SubRank(a.Sub), sb = SlotIndex.SubRank(b.Sub);
                if (sa != sb) return sa.CompareTo(sb);
                int na = SlotIndex.NameRank(a.Sub, a.Name), nb = SlotIndex.NameRank(b.Sub, b.Name);
                if (na != nb) return na.CompareTo(nb);
                // 人工排过的位置按人工顺序；没排过的往后
                int ra = CatPrefs.SlotRank(a.Cat, a.Name), rb = CatPrefs.SlotRank(b.Cat, b.Name);
                if (ra != rb) return ra.CompareTo(rb);
                bool x = HasChecked(a), y = HasChecked(b);
                if (x != y) return x ? -1 : 1;
                int c = string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
                return c != 0 ? c : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });
        }

        private void BuildRows()
        {
            List<TreeRow> rows = new List<TreeRow>();
            List<string> order = SlotIndex.CatOrder(slots);
            foreach (string cat in order)
            {
                List<Slot> inCat = new List<Slot>();
                foreach (Slot s in slots) if (s.Cat == cat) inCat.Add(s);
                if (inCat.Count == 0) continue;

                TreeRow h = new TreeRow();
                h.IsCat = true; h.Cat = cat;
                rows.Add(h);
                if (folded.Contains(cat)) continue;

                // 小分类（枪械 → 步枪 → AK-47）。slots 已按小分类排好，这里顺着切段。
                // 一大类底下只有一个分组时不画那行标题 ——「特感 → 特感 → Tank」这种重复层没意义。
                int subCount = 0;
                string prevSub = null;
                foreach (Slot s in inCat) if (s.Sub != prevSub) { prevSub = s.Sub; subCount++; }
                bool showSub = subCount > 1;
                string lastSub = null;
                foreach (Slot s in inCat)
                {
                    if (showSub && s.Sub != lastSub)
                    {
                        lastSub = s.Sub;
                        TreeRow sh = new TreeRow();
                        sh.IsSub = true; sh.Cat = cat; sh.Sub = s.Sub;
                        rows.Add(sh);
                    }
                    if (showSub && folded.Contains(SubKey(cat, s.Sub))) continue;        // 这一段整段收起
                    TreeRow r = new TreeRow();
                    r.Slot = s; r.Cat = cat; r.Sub = s.Sub;
                    rows.Add(r);
                }
            }
            if (unmatched > 0)
            {
                TreeRow h = new TreeRow();
                h.IsCat = true; h.Cat = "未归类";
                rows.Add(h);
                if (!folded.Contains("未归类"))
                {
                    TreeRow n = new TreeRow();
                    n.IsNote = true; n.Cat = "未归类";
                    rows.Add(n);
                }
            }
            body.SetRows(rows);
        }

        // ---------------------------------------------------------- 状态

        internal bool HasChecked(Slot s)
        {
            foreach (ModGroup g in s.Items) if (mf.CountChecked(g) > 0) return true;
            return false;
        }

        /// <summary>这个位置当前勾了几件（正常是 0 或 1；>1 说明用户手工叠了）。</summary>
        internal int CheckedGroups(Slot s)
        {
            int n = 0;
            foreach (ModGroup g in s.Items) if (mf.CountChecked(g) > 0) n++;
            return n;
        }

        /// <summary>这个位置勾了几个可叠加的扩展（音效 / 只改第一人称模型 / HUD），可以和模型同时生效。</summary>
        internal int CheckedExtras(Slot s)
        {
            int n = 0;
            foreach (ModGroup g in s.Extras) if (mf.CountChecked(g) > 0) n++;
            return n;
        }

        /// <summary>点扩展行：开着的关掉，关着的打开 —— 和模型不是二选一，各管各的。</summary>
        internal void ToggleExtra(ModGroup g)
        {
            if (g == null || Current == null) return;
            if (mf.CountChecked(g) > 0) mf.UncheckGroup(g);
            else mf.CheckGroupDefault(g);
            mf.AfterTreePick();
            FillDetail();
        }

        /// <summary>这个位置当前用了哪个 MOD（多件时取第一件）。</summary>
        internal ModGroup ChosenOf(Slot s)
        {
            foreach (ModGroup g in s.Items) if (mf.CountChecked(g) > 0) return g;
            return null;
        }

        internal int Unmatched { get { return unmatched; } }
        internal int RowCount { get { return body == null ? 0 : body.RowCount; } }
        internal int SlotCount { get { return slots.Count; } }
        internal int SlotCheckedCount
        {
            get { int n = 0; foreach (Slot s in slots) if (HasChecked(s)) n++; return n; }
        }

        /// <summary>外部勾选变了：只重画，不重建行。</summary>
        public void Sync()
        {
            body.Invalidate();
            FillDetail();
        }

        public void SelectUnmatched()
        {
            if (selKey == "#unmatched") return;
            selKey = "#unmatched";
            body.Invalidate();
            FillDetail();
        }

        public void SelectSlot(string key)
        {
            if (key == null || key == selKey) return;
            selKey = key;
            body.Invalidate();
            FillDetail();
        }

        /// <summary>自测用：把第 n 个槽位选出来。</summary>
        public Slot SlotAt(int n)
        {
            return (n >= 0 && n < slots.Count) ? slots[n] : null;
        }

        /// <summary>自测用：按显示名找槽位。</summary>
        public Slot SlotByName(string name)
        {
            foreach (Slot s in slots) if (s.Name == name) return s;
            return null;
        }

        public void SetFolded(string cat, bool on)
        {
            if (on) folded.Add(cat); else folded.Remove(cat);
            BuildRows();
        }

        internal static string SubKey(string cat, string sub) { return cat + "\u0001" + sub; }

        public bool IsSubFolded(string cat, string sub) { return folded.Contains(SubKey(cat, sub)); }

        public void SetSubFolded(string cat, string sub, bool on)
        {
            if (on) folded.Add(SubKey(cat, sub)); else folded.Remove(SubKey(cat, sub));
            BuildRows();
        }

        // ---------------------------------------------------------- 右侧

        private Slot Current
        {
            get
            {
                foreach (Slot s in slots) if (s.Key == selKey) return s;
                return null;
            }
        }

        private void FillDetail()
        {
            if (selKey == "#unmatched") { ShowUnmatched(); return; }
            lv.Visible = true; lblHint.Visible = false;
            Slot s = Current;
            lv.BeginUpdate();
            lvExtra.BeginUpdate();
            try
            {
                lv.Items.Clear();
                lvExtra.Items.Clear();
                if (s == null)
                {
                    lblTitle.Text = "选一个位置";
                    lblCur.Text = "左边点一个武器 / 药品 / 人物，这里列它的 MOD";
                    lblExtraTitle.Visible = false;
                    lvExtra.Visible = false;
                    return;
                }
                int n = CheckedGroups(s);
                int ne = CheckedExtras(s);
                lblTitle.Text = s.Name;
                if (n == 0 && ne == 0) lblCur.Text = "还没选 · 游戏里还是原版";
                else if (n == 1)
                {
                    ModGroup c = ChosenOf(s);
                    lblCur.Text = "当前：" + (c == null ? "" : c.Name) + (ne > 0 ? "　+ " + ne + " 个扩展" : "");
                }
                else if (n > 1) lblCur.Text = "这个位置叠了 " + n + " 件（自己确认会不会打架）";
                else lblCur.Text = "模型还是原版 · 只装了 " + ne + " 个扩展";

                foreach (ModGroup g in s.Items)
                {
                    bool on = mf.CountChecked(g) > 0;
                    ListViewItem li = new ListViewItem(g.Name);
                    li.SubItems.Add(KindsOf(g));
                    li.SubItems.Add(Core.Human(g.Size));
                    li.StateImageIndex = on ? 1 : 0;
                    if (on) li.Font = Theme.UIBold;
                    li.Tag = g;
                    if (g.IsSet) li.ToolTipText = g.Parts.Count + " 个 vpk 组成，选它就是选默认部件";
                    lv.Items.Add(li);
                }

                ListViewItem none = new ListViewItem("留原版（不换模型）");
                none.SubItems.Add("");
                none.SubItems.Add("—");
                none.ForeColor = Theme.Ink3;
                none.StateImageIndex = n == 0 ? 1 : 0;
                none.Tag = null;
                lv.Items.Add(none);

                lblFoot.Text = s.Items.Count == 0
                    ? "这个位置没有换模型的包，只有下面那些扩展"
                    : s.Items.Count + " 个模型候选 · 点一个就换掉原来的";

                // ---- 下半：可叠加的扩展 ----
                lblExtraTitle.Visible = true;
                if (s.Extras.Count == 0)
                {
                    lvExtra.Visible = false;
                    lblExtraTitle.Text = "可叠加的扩展：这个位置还没有"
                                        + "（开火音效 / 只改第一人称模型 / HUD 图标，有就会出现在下面）";
                }
                else
                {
                    lvExtra.Visible = true;
                    lblExtraTitle.Text = "可叠加的扩展 · " + s.Extras.Count
                                       + " 个（不换模型，点一下开 / 关，可多选）";
                    foreach (ModGroup g in s.Extras)
                    {
                        bool on = mf.CountChecked(g) > 0;
                        ListViewItem li = new ListViewItem(g.Name);
                        li.SubItems.Add(KindsOf(g));
                        li.SubItems.Add(Core.Human(g.Size));
                        li.StateImageIndex = on ? 1 : 0;
                        if (on) li.Font = Theme.UIBold;
                        li.Tag = g;
                        if (g.IsSet) li.ToolTipText = g.Parts.Count + " 个 vpk 组成，点一下默认部件全开";
                        else li.ToolTipText = "和上面的模型同时生效，互不影响";
                        lvExtra.Items.Add(li);
                    }
                }
            }
            finally { lv.EndUpdate(); lvExtra.EndUpdate(); }
            LayoutSelf();
            LayoutCols();
        }

        /// <summary>未归类：说明白为什么不在这里动它，而不是假装没有。</summary>
        private void ShowUnmatched()
        {
            lv.Visible = false;
            lblHint.Visible = true;
            lblFoot.Text = "";
            lblTitle.Text = "未归类";
            lblCur.Text = unmatched + " 个 MOD 认不出改的是哪件东西";
            lblHint.Text = "它们的 vpk 里没有能和枪械/人物对上的模型名，程序没法判断该占哪个位置，\n"
                         + "所以在这里不参与「一个位置一个」。\n\n"
                         + "不影响装机：在「分类」页里照常勾选，装的时候一模一样。\n"
                         + "要让它们进树：到「分类」页对那一行右键 →「设置 MOD 分类…」，\n"
                         + "指定一次「算哪一类 + 替换什么」就记住了，下次自动排进来。";
        }

        private static string KindsOf(ModGroup g)
        {
            ModItem m = g.Main;
            if (m == null) return "";
            string k = m.Kinds;
            if (string.IsNullOrEmpty(k)) k = m.SubjectLevel;
            return k == null ? "" : k.Replace(",", "·");
        }

        /// <summary>点了一个候选（null = 留原版）：清掉这个位置的旧件，再勾新件。</summary>
        private void Choose(ModGroup g)
        {
            Slot s = Current;
            if (s == null) return;
            foreach (ModGroup o in s.Items) mf.UncheckGroup(o);
            if (g != null) mf.CheckGroupDefault(g);
            mf.AfterTreePick();
            FillDetail();
        }

        /// <summary>右键菜单里的「用这个替换」：拿这一行的 Tag 去换，位置用当前选中的那个。</summary>
        internal void PickRow(ListViewItem li)
        {
            if (li == null) return;
            Choose(li.Tag as ModGroup);
        }

        /// <summary>自测用：方案树右边那张表的右键菜单，一路念出来。</summary>
        internal string TreeMenuText()
        {
            if (lv.ContextMenuStrip == null) return "（没有菜单）";
            string all = "";
            foreach (ToolStripItem it in lv.ContextMenuStrip.Items)
                all += (it is ToolStripSeparator ? " | " : it.Text + " · ");
            return all;
        }

        /// <summary>自测用：直接选某个槽位里的第 i 个候选。</summary>
        public string ChooseAt(int slotIdx, int itemIdx)
        {
            Slot s = SlotAt(slotIdx);
            if (s == null) return "没有槽位";
            selKey = s.Key;
            if (itemIdx < 0) { Choose(null); return "留原版"; }
            if (itemIdx >= s.Items.Count) return "候选越界";
            ModGroup g = s.Items[itemIdx];
            Choose(g);
            return g.Name;
        }

        // ---------------------------------------------------------- 布局

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (scroll == null) return;
            using (Pen p = new Pen(Theme.Line))
                e.Graphics.DrawLine(p, scroll.Right, 0, scroll.Right, Height);
        }

        private void LayoutSelf()
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W <= 0 || H <= 0) return;
            // 三层之后每行多缩了一级，树比以前宽一点，否则物品名和「选了哪个」会挤在一起
            int treeW = Math.Min(430, Math.Max(260, W * 46 / 100));
            if (scroll != null) scroll.SetBounds(0, 0, treeW, H);
            // Dock 让树体宽度自动跟着可视区走（含竖直滚动条出现/消失），不然右边「n 个」会被裁掉
            if (body != null && body.Dock != DockStyle.Top) body.Dock = DockStyle.Top;

            int x = treeW + 1;
            int rightW = Math.Max(120, W - x);
            if (lblTitle != null) lblTitle.SetBounds(x + 16, 12, Math.Max(60, rightW - 32 - 100), 22);
            if (lblCur != null) lblCur.SetBounds(x + 16, 34, Math.Max(60, rightW - 32), 18);
            if (lblHint != null) lblHint.SetBounds(x + 16, 66, Math.Max(80, rightW - 32), Math.Max(60, H - 120));

            // 右半分上下：上半换本体（单选），下半可叠加的扩展（多选）。
            // 没有扩展时下半只留一行说明，上半占满 —— 空列表摆在那儿只是浪费地方。
            int top = 60, bottom = H - 42;
            int avail = Math.Max(120, bottom - top);
            const int gap = 26;                                   // 中间那行小标题占的高度
            bool hasEx = lvExtra != null && lvExtra.Visible;
            int mainH = hasEx ? Math.Max(110, avail * 56 / 100) : Math.Max(110, avail - gap);
            int exTop = top + mainH + gap;
            int exH = Math.Max(0, bottom - exTop);
            if (lv != null) lv.SetBounds(x + 4, top, rightW - 8, mainH);
            if (lblExtraTitle != null)
                lblExtraTitle.SetBounds(x + 16, Math.Max(top + 8, exTop - 20), Math.Max(80, rightW - 32), 18);
            if (lvExtra != null)
            {
                if (exH >= 40) lvExtra.SetBounds(x + 4, exTop, rightW - 8, exH);
                else lvExtra.Visible = false;
            }

            if (btnPick != null) btnPick.SetBounds(x + 16, H - 34, 76, Theme.H);
            if (lblFoot != null) lblFoot.SetBounds(x + 16, H - 30, Math.Max(60, rightW - 32), 18);
            LayoutCols();
        }

        private void LayoutCols()
        {
            Cols(lv);
            Cols(lvExtra);
        }

        private static void Cols(ListView v)
        {
            if (v == null || v.Columns.Count < 3) return;
            int w = v.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8;
            const int kindW = 100, sizeW = 74;
            v.Columns[0].Width = Math.Max(160, w - kindW - sizeW);
            v.Columns[1].Width = kindW;
            v.Columns[2].Width = sizeW;
        }
    }

    // ============================================================
    //  树体：自绘，三层（大类 / 小分类 / 槽位）
    // ============================================================
    internal sealed class SlotTree : Control
    {
        private const int RowH = 26;
        private readonly CatTree owner;
        private readonly List<TreeRow> rows = new List<TreeRow>();
        private int hover = -1;

        public SlotTree(CatTree o)
        {
            owner = o;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.UI;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int RowCount { get { return rows.Count; } }

        public void SetRows(List<TreeRow> r)
        {
            rows.Clear();
            if (r != null) rows.AddRange(r);
            Height = Math.Max(12, rows.Count * RowH + 6);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(247, 249, 251));
            int W = Width;
            if (W <= 0) return;

            using (Pen branch = new Pen(Color.FromArgb(198, 206, 215)))
            using (SolidBrush selBg = new SolidBrush(Color.FromArgb(250, 240, 239)))
            using (SolidBrush hovBg = new SolidBrush(Color.FromArgb(238, 242, 246)))
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    TreeRow r = rows[i];
                    int y = i * RowH + 4;

                    if (r.IsCat)
                    {
                        bool open = !owner.IsFolded(r.Cat);
                        if (i == hover) g.FillRectangle(hovBg, 0, y, W, RowH);
                        DrawTri(g, 10, y + RowH / 2, open);
                        TextRenderer.DrawText(g, r.Cat, Theme.UIBold, new Rectangle(23, y, W - 70, RowH), Theme.Ink,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        int n = 0;
                        foreach (TreeRow x in rows)
                            if (!x.IsCat && !x.IsSub && !x.IsNote && x.Cat == r.Cat) n++;
                        string num = r.Cat == "未归类" ? owner.Unmatched + " 个" : n + " 个";
                        TextRenderer.DrawText(g, num, Theme.UISmall, new Rectangle(W - 62, y, 50, RowH), Theme.Ink3,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        continue;
                    }

                    if (r.IsSub)
                    {
                        bool open = !owner.IsSubFolded(r.Cat, r.Sub);
                        if (i == hover) g.FillRectangle(hovBg, 0, y, W, RowH);
                        g.DrawLine(branch, 17, y, 17, y + RowH);
                        g.DrawLine(branch, 17, y + RowH / 2, 26, y + RowH / 2);
                        DrawTri(g, 32, y + RowH / 2, open);
                        TextRenderer.DrawText(g, r.Sub, Theme.UIBold, new Rectangle(44, y, W - 92, RowH), Theme.Ink2,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        int k = 0;
                        foreach (TreeRow x in rows)
                            if (!x.IsCat && !x.IsSub && !x.IsNote && x.Cat == r.Cat && x.Sub == r.Sub) k++;
                        string num = k + " 个";
                        TextRenderer.DrawText(g, num, Theme.UISmall, new Rectangle(W - 62, y, 50, RowH), Theme.Ink3,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        // 往下连一小段，免得和位置行的竖线断开
                        if (open && k > 0) g.DrawLine(branch, 39, y + RowH / 2, 39, y + RowH);
                        continue;
                    }

                    if (r.IsNote)
                    {
                        if (i == hover) g.FillRectangle(hovBg, 0, y, W, RowH);
                        g.DrawLine(branch, 17, y + RowH / 2, 26, y + RowH / 2);
                        TextRenderer.DrawText(g, "认不出改的是什么", Theme.UI,
                            new Rectangle(33, y, W - 46, RowH), Theme.Ink3,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        continue;
                    }

                    Slot s = r.Slot;
                    if (s == null) continue;
                    bool sel = s.Key == owner.SelectedKey;
                    if (sel) g.FillRectangle(selBg, 0, y, W, RowH);
                    else if (i == hover) g.FillRectangle(hovBg, 0, y, W, RowH);
                    if (sel) using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, y + 3, 2, RowH - 6);

                    // 分支线：竖线 + 一根横线（位置层缩进到小分类下面）
                    bool nextSame = (i + 1 < rows.Count) && !rows[i + 1].IsCat && !rows[i + 1].IsSub
                                    && rows[i + 1].Cat == r.Cat && rows[i + 1].Sub == r.Sub;
                    int bx = 39;
                    g.DrawLine(branch, bx, y, bx, y + (nextSame ? RowH : RowH / 2 + 1));
                    g.DrawLine(branch, bx, y + RowH / 2, bx + 9, y + RowH / 2);

                    // 状态点：绿=选了本体，橙=叠了好几件本体，蓝=只有扩展（音效/只改第一人称模型），灰=空
                    int cnt = owner.CheckedGroups(s);
                    int ecnt = owner.CheckedExtras(s);
                    Color dot = cnt > 1 ? Theme.Warn
                              : cnt == 1 ? Theme.Ok
                              : ecnt > 0 ? Theme.Accent
                              : Color.FromArgb(210, 216, 223);
                    using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, bx + 17, y + RowH / 2 - 3, 6, 6);

                    int nx = bx + 30;
                    int nameW = 86;
                    // 「步枪」小分类下又有个位置叫「步枪」= 认不出具体型号的那一堆，改个说法免得看着像重复
                    string showName = s.Name == s.Sub ? "未分型号" : s.Name;
                    TextRenderer.DrawText(g, showName, sel ? Theme.UIBold : Theme.UI,
                        new Rectangle(nx, y, nameW, RowH), sel ? Theme.Ink : Theme.Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                    ModGroup chosen = owner.ChosenOf(s);
                    string sub = chosen == null ? (ecnt > 0 ? "原版" : "未选") : chosen.Name;
                    if (cnt > 1) sub = "叠了 " + cnt + " 件";
                    if (ecnt > 0) sub += "  +" + ecnt + " 扩展";
                    int subX = nx + nameW + 8;
                    int subW = W - subX - 46;
                    if (subW > 20)
                        TextRenderer.DrawText(g, sub, Theme.UISmall, new Rectangle(subX, y, subW, RowH), Theme.Ink3,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                    // 右端数字：本体候选数；有扩展时写成「5+1」
                    string cand = s.Items.Count.ToString();
                    if (s.Extras.Count > 0) cand += "+" + s.Extras.Count;
                    TextRenderer.DrawText(g, cand, Theme.UISmall,
                        new Rectangle(W - 40, y, 32, RowH), Theme.Ink3,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        private static void DrawTri(Graphics g, int cx, int cy, bool open)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(140, 150, 160)))
            {
                if (open)
                {
                    Point[] p = { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 3) };
                    g.FillPolygon(b, p);
                }
                else
                {
                    Point[] p = { new Point(cx - 2, cy - 4), new Point(cx - 2, cy + 4), new Point(cx + 3, cy) };
                    g.FillPolygon(b, p);
                }
            }
        }

        private int IndexAt(int y)
        {
            int i = y / RowH;
            return (i >= 0 && i < rows.Count) ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i != hover) { hover = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i < 0) return;
            TreeRow r = rows[i];
            if (r.IsCat)
            {
                owner.SetFolded(r.Cat, !owner.IsFolded(r.Cat));
            }
            else if (r.IsSub)
            {
                owner.SetSubFolded(r.Cat, r.Sub, !owner.IsSubFolded(r.Cat, r.Sub));
            }
            else if (r.IsNote)
            {
                owner.SelectUnmatched();
            }
            else if (r.Slot != null)
            {
                owner.SelectSlot(r.Slot.Key);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Panel p = Parent as Panel;
            if (p != null && p.VerticalScroll.Visible)
            {
                int v = p.VerticalScroll.Value - e.Delta;
                v = Math.Max(p.VerticalScroll.Minimum, Math.Min(p.VerticalScroll.Maximum, v));
                p.VerticalScroll.Value = v;
                p.PerformLayout();
                return;
            }
            base.OnMouseWheel(e);
        }
    }
}

namespace Installer
{
    public partial class MainForm
    {
        /// <summary>自测：切到方案树、数槽位、验证「一个位置只留一个」的替换逻辑。</summary>
        internal void UiTestTree()
        {
            if (viewHost == null) { Core.Log("FAIL", "TREE 没有 viewHost"); return; }
            // 自测会改勾选，先留一份底，测完原样放回去 —— 不能把用户的方案改坏
            List<string> backup = new List<string>(checkedKeys);
            ShowTree();
            Application.DoEvents();

            int slots = treeView.SlotCount;
            int used = treeView.SlotCheckedCount;
            Core.Log("INFO", "TREE 槽位 " + slots + " 个，其中已选 " + used + " 个");

            // 分类分布
            System.Collections.Generic.Dictionary<string, int> byCat =
                new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            System.Collections.Generic.Dictionary<string, int> bySub =
                new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < slots; i++)
            {
                Slot s = treeView.SlotAt(i);
                if (s == null) continue;
                int n;
                byCat.TryGetValue(s.Cat, out n);
                byCat[s.Cat] = n + 1;
                string sk = s.Cat + " \u0001 " + s.Sub;
                bySub.TryGetValue(sk, out n);
                bySub[sk] = n + 1;
                Core.Log("INFO", "TREE  " + s.Cat + " / " + s.Sub + " / " + s.Name + "  候选 " + s.Items.Count
                                            + (s.Extras.Count > 0 ? "  扩展 " + ExtrasText(s) : "")
                                            + "  已选 " + (treeView.HasChecked(s) ? "是" : "否"));
            }
            foreach (KeyValuePair<string, int> kv in byCat)
                Core.Log("INFO", "TREE 分类 " + kv.Key + " = " + kv.Value + " 个位置");
            foreach (KeyValuePair<string, int> kv in bySub)
            {
                string[] pp = kv.Key.Split('\u0001');
                Core.Log("INFO", "TREE 小分类 " + pp[0].Trim() + " → " + pp[1].Trim() + " = " + kv.Value + " 个位置");
            }

            // 覆盖诊断：每类有多少个 mod 认得出来
            foreach (string c in Catalog.Cats)
            {
                int tot = 0, known = 0;
                foreach (ModGroup g in Catalog.Groups)
                {
                    if (g.Cat != c) continue;
                    tot++;
                    ModItem mi = g.Main;
                    if (mi != null && ((mi.Subject ?? "").Trim().Length > 0 || (mi.SubjectKey ?? "").Trim().Length > 0)) known++;
                }
                Core.Log("INFO", "TREE 覆盖 " + c + "  " + known + "/" + tot + " 认得出来");
            }

            // 找一个候选多于 3 的槽位来测替换
            int idx = -1;
            for (int i = 0; i < slots; i++)
            {
                Slot s = treeView.SlotAt(i);
                if (s != null && s.Items.Count >= 3) { idx = i; break; }
            }
            if (idx < 0) { Core.Log("FAIL", "TREE 没有候选 >=3 的槽位，测不了替换"); return; }

            Slot t = treeView.SlotAt(idx);
            Core.Log("INFO", "TREE 试验槽位 " + t.Name + "（" + t.Items.Count + " 个候选）");

            int n0 = TotalCheckedParts();
            string pick1 = treeView.ChooseAt(idx, 1);
            Application.DoEvents();
            int n1 = TotalCheckedParts();
            Core.Log("INFO", "TREE 选第 2 个候选「" + pick1 + "」：勾选数 " + n0 + " → " + n1);

            string pick2 = treeView.ChooseAt(idx, 2);
            Application.DoEvents();
            int n2 = TotalCheckedParts();
            Core.Log("INFO", "TREE 换成第 3 个「" + pick2 + "」：勾选数 " + n1 + " → " + n2
                            + "（应该只换不增）");
            if (n2 != n1) Core.Log("FAIL", "TREE 替换后勾选数变了 " + n1 + " → " + n2);

            int groupsInSlot = treeView.CheckedGroups(treeView.SlotAt(idx));
            Core.Log("INFO", "TREE 这个位置现在有 " + groupsInSlot + " 件");
            if (groupsInSlot != 1) Core.Log("FAIL", "TREE 一个位置出现了 " + groupsInSlot + " 件");

            treeView.ChooseAt(idx, -1);
            Application.DoEvents();
            int n3 = TotalCheckedParts();
            Core.Log("INFO", "TREE 点「留原版」：勾选数 " + n2 + " → " + n3);
            if (n3 != n2 - treeView.SlotAt(idx).Items.Count && n3 >= n2) Core.Log("FAIL", "TREE 留原版没清掉");

            // 折叠 / 展开：大类一层、小分类一层都要能收
            int rowsBefore = treeView.RowCount;
            treeView.SetFolded("枪械", true);
            Application.DoEvents();
            int rowsAfter = treeView.RowCount;
            Core.Log("INFO", "TREE 折叠「枪械」：行数 " + rowsBefore + " → " + rowsAfter);
            if (rowsAfter >= rowsBefore) Core.Log("FAIL", "TREE 折叠没生效");
            treeView.SetFolded("枪械", false);
            Application.DoEvents();
            Core.Log("INFO", "TREE 展开回去：行数 " + treeView.RowCount);

            string cat1 = null, sub1 = null;
            for (int i = 0; i < treeView.SlotCount; i++)
            {
                Slot s1 = treeView.SlotAt(i);
                if (s1 == null) continue;
                if (cat1 == null) { cat1 = s1.Cat; sub1 = s1.Sub; }
                if (s1.Cat == cat1 && s1.Sub == sub1) continue;
                break;
            }
            if (cat1 != null && sub1 != null)
            {
                int r0 = treeView.RowCount;
                int inSub = 0;
                for (int i = 0; i < treeView.SlotCount; i++)
                {
                    Slot s1 = treeView.SlotAt(i);
                    if (s1 != null && s1.Cat == cat1 && s1.Sub == sub1) inSub++;
                }
                treeView.SetSubFolded(cat1, sub1, true);
                Application.DoEvents();
                int r1 = treeView.RowCount;
                Core.Log("INFO", "TREE 折叠小分类「" + cat1 + " / " + sub1 + "」（" + inSub + " 个位置）：行数 "
                                + r0 + " → " + r1);
                if (r1 >= r0) Core.Log("FAIL", "TREE 小分类折叠没生效");
                treeView.SetSubFolded(cat1, sub1, false);
                Application.DoEvents();
                if (treeView.RowCount != r0) Core.Log("FAIL", "TREE 小分类展开后行数对不上");
            }

            // 未归类入口
            treeView.SelectUnmatched();
            Application.DoEvents();
            Core.Log("INFO", "TREE 未归类 " + treeView.Unmatched + " 个（这些不进树，但照常能勾）");

            // 扩展（音效 / 只改第一人称模型 / HUD）是叠上去的：自己能开关，换模型时不该被一起关掉
            int exIdx = -1;
            for (int i = 0; i < slots; i++)
            {
                Slot sx = treeView.SlotAt(i);
                if (sx != null && sx.Extras.Count > 0 && sx.Items.Count > 1) { exIdx = i; break; }
            }
            if (exIdx < 0) Core.Log("INFO", "TREE 没有「有扩展且有多个模型」的位置，跳过扩展测试");
            else
            {
                Slot sx = treeView.SlotAt(exIdx);
                ModGroup eg = sx.Extras[0];
                treeView.SelectSlot(sx.Key);
                Application.DoEvents();
                if (CountChecked(eg) == 0) { treeView.ToggleExtra(eg); Application.DoEvents(); }
                int on0 = CountChecked(eg);                         // 先确保是开着的
                treeView.ToggleExtra(eg);
                Application.DoEvents();
                int off1 = CountChecked(eg);
                treeView.ToggleExtra(eg);                           // 再开回来
                Application.DoEvents();
                int on1 = CountChecked(eg);
                Core.Log("INFO", "TREE 扩展「" + eg.Name + "」开关：" + on0 + " → " + off1 + " → " + on1);
                if (off1 != 0 || on1 != on0) Core.Log("FAIL", "TREE 扩展开关不对");

                treeView.ChooseAt(exIdx, 0);                        // 换一个模型
                Application.DoEvents();
                int kept = CountChecked(eg);
                Core.Log("INFO", "TREE 换模型后扩展「" + eg.Name + "」勾选：" + on1 + " → " + kept
                                + "（必须不变）");
                if (kept != on1) Core.Log("FAIL", "TREE 换模型把扩展一起关了");

                treeView.ChooseAt(exIdx, -1);                       // 点「留原版」
                Application.DoEvents();
                int kept2 = CountChecked(eg);
                Core.Log("INFO", "TREE 留原版后扩展还在：" + kept2 + "（必须不变）");
                if (kept2 != on1) Core.Log("FAIL", "TREE 留原版把扩展一起关了");
            }

            // 原样放回：按备份逐条还原，不是「再点一下」—— 点一下只会换成另一个候选
            checkedKeys.Clear();
            foreach (string k in backup) checkedKeys.Add(k);
            AfterTreePick();
            treeView.Sync();
            Application.DoEvents();
            int n4 = TotalCheckedParts();
            Core.Log("INFO", "TREE 还原后勾选数 " + n4 + "（原 " + n0 + "）");
            if (n4 != n0) Core.Log("FAIL", "TREE 还原后勾选数对不上 " + n0 + " → " + n4);

            // 截图停在「有扩展」的位置上，这样右半边的上下两栏都能看清
            Slot shot = null;
            for (int i = 0; i < slots; i++)
            {
                Slot sx = treeView.SlotAt(i);
                if (sx != null && sx.Extras.Count > 0 && sx.Items.Count > 0) { shot = sx; break; }
            }
            if (shot == null) shot = treeView.SlotAt(0);
            if (shot != null) treeView.SelectSlot(shot.Key);
        }

        /// <summary>把勾选恢复成某个方案的样子并存盘（自测把方案改坏时用来救回来）。</summary>
        internal void UiFixProfile(string name)
        {
            ApplyProfileByName(name);
            Application.DoEvents();
            Core.Cfg.Mods = new List<string>(checkedKeys);
            Core.SaveConfig();
            Core.Log("INFO", "FIX 已按方案「" + name + "」恢复勾选，共 " + checkedKeys.Count + " 个文件");
        }

        /// <summary>自测日志用：把一个位置的扩展名字串起来。</summary>
        private static string ExtrasText(Slot s)
        {
            string t = "";
            foreach (ModGroup g in s.Extras) t += (t.Length > 0 ? "、" : "") + g.Name;
            return t;
        }

        private int TotalCheckedParts()        {
            int n = 0;
            foreach (ModGroup g in Catalog.Groups) n += CountChecked(g);
            return n;
        }
    }
}

namespace Installer
{
    public partial class MainForm
    {
        /// <summary>自测：右键菜单文案，以及「只装这个 MOD / 重命名」取的是不是右键那一行（而不是勾选）。</summary>
        internal void UiTestMenu()
        {
            string cat = "枪械";
            ShowCatView(cat);
            Application.DoEvents();
            CatView v;
            if (!catViews.TryGetValue(cat, out v) || v.List == null)
            { Core.Log("FAIL", "MENU 拿不到枪械列表"); return; }
            ListView lv = v.List;
            if (lv.Items.Count < 3) { Core.Log("FAIL", "MENU 行数太少 " + lv.Items.Count); return; }

            int checkedRows = lv.CheckedItems.Count;
            ContextMenuStrip cms = BuildRowMenu(lv);
            string all = "";
            foreach (ToolStripItem it in cms.Items)
                all += (it is ToolStripSeparator ? " | " : it.Text + " · ");
            Core.Log("INFO", "MENU 当时勾了 " + checkedRows + " 行，菜单：" + all);

            // 选中第 3 行，勾选一个都不动 —— 看取值是不是跟着选中行走
            lv.SelectedItems.Clear();
            lv.Items[2].Selected = true;
            lv.Items[2].Focused = true;
            Application.DoEvents();

            ModGroup rowGroup = lv.Items[2].Tag as ModGroup;
            ModItem rowItem = lv.Items[2].Tag as ModItem;
            string nm = rowGroup != null ? rowGroup.Name : lv.Items[2].Text;
            List<ModItem> mine = RowItems(lv);
            string names = "";
            foreach (ModItem m in mine) names += m.Name + "、";
            Core.Log("INFO", "MENU 右键行 = [" + nm + "]，取到 " + mine.Count + " 个 vpk：" + names);

            bool ok = mine.Count > 0;
            foreach (ModItem m in mine)
            {
                bool belong = (rowItem != null && rowItem.Key == m.Key);
                if (rowGroup != null)
                    foreach (ModItem p in rowGroup.Parts) if (p.Key == m.Key) belong = true;
                if (!belong) ok = false;
            }
            if (!ok) Core.Log("FAIL", "MENU 取到的 vpk 不属于右键那一行（勾选优先的老毛病还在）");

            bool hasOne = false, hasBatch = false;
            foreach (ToolStripItem it in cms.Items)
            {
                if (it.Text != null && it.Text.StartsWith("只装这个 MOD")) hasOne = true;
                if (it.Text != null && it.Text.StartsWith("只装勾中的这")) hasBatch = true;
            }
            if (!hasOne) Core.Log("FAIL", "MENU 没有「只装这个 MOD」");
            if (checkedRows > 1 && !hasBatch) Core.Log("FAIL", "MENU 勾了 " + checkedRows + " 行却没有批量项");
            if (checkedRows <= 1 && hasBatch) Core.Log("FAIL", "MENU 只勾 " + checkedRows + " 行却有批量项");
            Core.Log("INFO", "MENU 单项=" + hasOne + " 批量项=" + hasBatch + "，重命名项文案="
                + MenuTextOf(cms, "重命名"));

            // 设置标签 → 设置 MOD 分类（点开是独立窗口，能选分类也能新建）
            Core.Log(MenuTextOf(cms, "设置 MOD 分类") == "（没有）" ? "FAIL" : "OK",
                "MENU 分类项文案=" + MenuTextOf(cms, "设置 MOD 分类"));
            if (MenuTextOf(cms, "设置标签") != "（没有）") Core.Log("FAIL", "MENU 还留着「设置标签」");

            lv.SelectedItems.Clear();
            ShowTree();
            Application.DoEvents();
            Core.Log("INFO", "TREEMENU " + (treeView == null ? "（没有树）" : treeView.TreeMenuText()));
        }

        private static string MenuTextOf(ContextMenuStrip cms, string keyword)
        {
            foreach (ToolStripItem it in cms.Items)
                if (it.Text != null && it.Text.IndexOf(keyword, StringComparison.Ordinal) >= 0) return it.Text;
            return "（没有）";
        }

        /// <summary>自测：「设置 MOD 分类」这条路走一遍 —— 人工归类要真的进树，测完原样还原。</summary>
        internal void UiTestSetCat()
        {
            ShowTree();
            Application.DoEvents();
            if (treeView == null) { Core.Log("FAIL", "SETCAT 没有树"); return; }

            // 分类是持久化的：先留底，自测结束原样写回，不能给用户留一个「测试分类」
            string f = CatPrefs.FileOf();
            string backup = System.IO.File.Exists(f) ? System.IO.File.ReadAllText(f) : null;
            int before = treeView.SlotCount;
            Core.Log("INFO", "SETCAT 起始槽位 " + before + " 个，分类顺序 " + string.Join(" / ", CatPrefs.Cats().ToArray()));

            List<Slot> now = SlotIndex.Build();
            ModGroup miss = null, known = null;
            foreach (ModGroup g in Catalog.Groups)
            {
                if (g.Cat == "地图" || g.Cat == "喷漆" || g.Main == null) continue;
                bool inTree = false;
                foreach (Slot s in now) if (s.Items.Contains(g)) { inTree = true; break; }
                if (!inTree) { if (miss == null) miss = g; }
                else if (known == null && (g.Main.Subject ?? "").Trim().Length > 0) known = g;
            }
            if (miss != null) Core.Log("INFO", "SETCAT 未归类样本：" + miss.Name);
            if (known == null) { Core.Log("FAIL", "SETCAT 找不到已知位置的样本"); return; }

            ModGroup t1 = miss != null ? miss : known;
            string autoCatOfT1 = t1.Cat;
            CatPrefs.Assign(t1, "测试分类", "测试位置");
            Catalog.ApplyCats();                 // 分类落到 ModGroup.Cat 上：页签 / 搜索一起变
            treeView.Rebuild();
            bool inSlot = false, catOk = false;
            foreach (Slot s in SlotIndex.Build())
                if (s.Name == "测试位置" && s.Items.Contains(t1)) { inSlot = true; catOk = s.Cat == "测试分类"; }
            Core.Log(inSlot && catOk ? "OK" : "FAIL", "SETCAT 全人工：" + t1.Name + " → 测试分类 / 测试位置，进树=" + inSlot + " 归类=" + catOk);
            Core.Log(t1.Cat == "测试分类" ? "OK" : "FAIL",
                "SETCAT 分类落到数据上：ModGroup.Cat=" + t1.Cat + "（页签跟着走）");
            Core.Log(Array.IndexOf(Catalog.Cats, "测试分类") >= 0 ? "OK" : "FAIL",
                "SETCAT 页签分类表里有「测试分类」：" + string.Join(" / ", Catalog.Cats));

            List<string> order = CatPrefs.Cats();
            Core.Log(order[order.Count - 1] == "测试分类" ? "OK" : "FAIL",
                "SETCAT 新建分类排在末尾：" + string.Join(" / ", order.ToArray()));
            Core.Log(treeView.SlotCount > before ? "OK" : "FAIL",
                "SETCAT 槽位数 " + before + " → " + treeView.SlotCount + "（未归类的进来了）");

            string autoName = (known.Main.Subject ?? "").Trim();
            CatPrefs.Assign(known, "测试分类", "");
            Catalog.ApplyCats();
            treeView.Rebuild();
            bool moved = false;
            foreach (Slot s in SlotIndex.Build())
                if (s.Items.Contains(known)) moved = s.Cat == "测试分类";   // 位置名可能带后缀，只认「这个 MOD 在哪一类的槽里」
            Core.Log(moved ? "OK" : "FAIL", "SETCAT 只改归类：" + known.Name + " → 测试分类（位置仍是 " + autoName + "）");

            CatPrefs.Reset(t1);
            CatPrefs.Reset(known);
            Catalog.ApplyCats();
            treeView.Rebuild();
            bool gone = true;
            foreach (Slot s in SlotIndex.Build()) if (s.Name == "测试位置") { gone = false; break; }
            Core.Log(gone ? "OK" : "FAIL", "SETCAT 恢复自动后「测试位置」已消失");
            Core.Log(t1.Cat == autoCatOfT1 ? "OK" : "FAIL",
                "SETCAT 恢复自动后 ModGroup.Cat 回到 " + t1.Cat + "（原 " + autoCatOfT1 + "）");
            Core.Log(treeView.SlotCount == before ? "OK" : "FAIL",
                "SETCAT 槽位数回到 " + treeView.SlotCount + "（原 " + before + "）");

            // 大类改名：整类跟着搬，不能因为旧名字从分类表里消失就掉出页签
            int medBefore = 0;
            foreach (ModGroup x in Catalog.Groups) if (x.Cat == "药品") medBefore++;
            CatPrefs.RenameCat("药品", "测试改名类");
            Catalog.AdoptCatRename("药品", "测试改名类");
            int medAfter = 0;
            foreach (ModGroup x in Catalog.Groups) if (x.Cat == "测试改名类") medAfter++;
            bool inTabs = Array.IndexOf(Catalog.Cats, "测试改名类") >= 0 && Array.IndexOf(Catalog.Cats, "药品") < 0;
            Core.Log(medBefore == medAfter && inTabs ? "OK" : "FAIL",
                "SETCAT 大类改名：药品 " + medBefore + " 个 → 测试改名类 " + medAfter + " 个，页签跟上了=" + inTabs);

            if (backup == null) { try { System.IO.File.Delete(f); } catch { } }
            else System.IO.File.WriteAllText(f, backup, new System.Text.UTF8Encoding(true));
            CatPrefs.Reload();
            Catalog.ApplyCats();
            treeView.Rebuild();
            Core.Log(CatPrefs.Cats().Contains("测试分类") ? "FAIL" : "OK",
                "SETCAT 配置已还原：" + string.Join(" / ", CatPrefs.Cats().ToArray()));
        }

        /// <summary>自测：把「设置 MOD 分类」窗口拉起来截一张，人眼过一遍排版。</summary>
        internal void UiTestCatForm()
        {
            ModGroup g = null;
            foreach (ModGroup x in Catalog.Groups)
                if (x.Cat != "地图" && x.Cat != "喷漆" && x.Main != null) { g = x; break; }
            if (g == null) { Core.Log("FAIL", "CATFORM 没有样本"); return; }
            using (ModCatForm f = new ModCatForm(g))
            {
                f.Show();
                Application.DoEvents();
                f.PerformLayout();
                Application.DoEvents();
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(System.IO.Path.Combine(Core.LogDir, "modcat.png"),
                             System.Drawing.Imaging.ImageFormat.Png);
                }
                Core.Log("INFO", "CATFORM " + g.Name + "（" + g.Cat + "）截图已存 modcat.png");
                f.Close();
            }
        }

        /// <summary>自测：拿个假游戏目录打开「已装 MOD 开关」，截图看按钮排版。</summary>
        internal void UiTestAddonSwitch()
        {
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "l4d2-switch-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string addons = System.IO.Path.Combine(tmp, "left4dead2", "addons");
            try
            {
                System.IO.Directory.CreateDirectory(addons);
                List<string> names = new List<string>();
                foreach (ModGroup x in Catalog.Groups)
                {
                    if (x.Main == null || string.IsNullOrEmpty(x.Main.Name)) continue;
                    names.Add(x.Main.Name);
                    if (names.Count >= 6) break;
                }
                int i = 0;
                foreach (string n in names)
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(addons, n), "x");
                    i++;
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("\"AddonList\"");
                sb.AppendLine("{");
                for (int k = 0; k < names.Count; k++)
                    sb.AppendLine("\t\"" + names[k] + "\"\t\t\"" + (k % 3 == 0 ? "1" : "0") + "\"");
                sb.AppendLine("}");
                System.IO.File.WriteAllText(AddonList.FileOf(tmp), sb.ToString(), System.Text.Encoding.Default);

                using (AddonSwitchForm f = new AddonSwitchForm(tmp))
                {
                    f.Show();
                    Application.DoEvents();
                    f.PerformLayout();
                    Application.DoEvents();
                    using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(System.IO.Path.Combine(Core.LogDir, "addonswitch.png"),
                                 System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Core.Log("INFO", "ADDONSW " + i + " 个假 MOD，截图已存 addonswitch.png");
                    f.Close();
                }
            }
            catch (Exception ex) { Core.Log("FAIL", "ADDONSW 截图失败: " + ex.Message); }
            finally { try { System.IO.Directory.Delete(tmp, true); } catch { } }
        }
    }
}

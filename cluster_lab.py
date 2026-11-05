# -*- coding: utf-8 -*-
"""AI 聚类可行性实验：不查任何字典，只看包内路径相似度，检验聚类能否自己分出类别/武器。
数据：identify-cache-v15.tsv（资源指纹列 = base64(路径;路径;...)）
方法：纯 Python，路径 token 化 -> Jaccard 相似度 -> 凝聚式层次聚类。已知标签只用于最后打分，不参与聚类。
"""
import base64, sys, io
from collections import Counter, defaultdict

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

CACHE = r"D:\游戏内容\求生之路2各项MOD\扩展平台\_装机记录\identify-cache-v15.tsv"

def tokens(assets):
    """把路径列表变成特征集合：每一级目录、扩展名、去掉 v_/w_ 前缀的文件名。
    刻意不用任何武器/人物名单 —— 这是'无字典'实验。"""
    out = set()
    for p in assets.split(";"):
        if not p: continue
        parts = p.split("/")
        for i, seg in enumerate(parts):
            if not seg: continue
            if "." in seg:                       # 文件名：留扩展名 + 去 v_/w_ 前缀的名
                stem, ext = seg.rsplit(".", 1)
                out.add("ext:" + ext)
                if stem.startswith(("v_", "w_")):
                    stem = stem[2:]
                out.add("name:" + stem)
            else:
                out.add("dir:" + seg)
        out.add("top:" + parts[0])               # models/ sound/ materials/ ...
    return out

rows = []
with open(CACHE, encoding="utf-8") as f:
    for line in f:
        c = line.rstrip("\n").split("\t")
        if len(c) < 9 or not c[8]: continue
        try: assets = base64.b64decode(c[8]).decode("utf-8", "replace")
        except Exception: continue
        rows.append({"path": c[0], "subject": c[2], "kinds": c[5], "assets": assets})

print(f"样本: {len(rows)} 个 vpk（资源指纹非空）")

# 向量化
vecs = [tokens(r["assets"]) for r in rows]

def jaccard(a, b):
    if not a or not b: return 0.0
    inter = len(a & b)
    return inter / (len(a) + len(b) - inter)

# ---- 实验 A：按相似度聚大类，看能不能分出 模型/音效/HUD/地图 ----
# 凝聚式聚类：>=0.5 相似就合并
n = len(rows)
parent = list(range(n))
def find(x):
    while parent[x] != x: parent[x] = parent[parent[x]]; x = parent[x]
    return x
pairs = []
for i in range(n):
    for j in range(i + 1, n):
        s = jaccard(vecs[i], vecs[j])
        if s >= 0.5: pairs.append((s, i, j))
pairs.sort(reverse=True)
for s, i, j in pairs:
    pi, pj = find(i), find(j)
    if pi != pj: parent[pi] = pj
clusters = defaultdict(list)
for i in range(n): clusters[find(i)].append(i)

print(f"\n== 实验 A: Jaccard>=0.5 聚类 -> {len(clusters)} 个簇")

# 打分：每个簇里 Kinds 的纯度（最高占比）和主导 Kinds
pures, sizes, dom_kinds = [], [], Counter()
for ids in clusters.values():
    ks = Counter()
    for i in ids:
        for k in rows[i]["kinds"].split(","):
            if k: ks[k] += 1
    if not ks: continue
    top, cnt = ks.most_common(1)[0]
    pures.append(cnt / len(ids)); sizes.append(len(ids)); dom_kinds[top] += 1
print(f"簇纯度(多数 Kinds 占比) 平均={sum(pures)/len(pures):.2f}，中位={sorted(pures)[len(pures)//2]:.2f}")
print("各 Kinds 当簇主的簇数:", dict(dom_kinds))

# ---- 实验 B：枪械皮肤聚类 —— 同一武器的皮肤能不能自己聚在一起 ----
# 已知主体（字典法结果）只当参考答案
guns = [i for i in range(n) if rows[i]["subject"] and "模型" in rows[i]["kinds"]
        and not any(t in rows[i]["kinds"] for t in ("地图",))]
gun_subj = [i for i in guns if "/" not in rows[i]["subject"] and "件" not in rows[i]["subject"]]
print(f"\n== 实验 B: 单主体模型包 {len(gun_subj)} 个，按 >=0.6 聚类")
parent = list(range(n))
pairs2 = []
for a, i in enumerate(gun_subj):
    for j in gun_subj[a + 1:]:
        s = jaccard(vecs[i], vecs[j])
        if s >= 0.6: pairs2.append((s, i, j))
pairs2.sort(reverse=True)
for s, i, j in pairs2:
    pi, pj = find(i), find(j)
    if pi != pj: parent[pi] = pj
gcl = defaultdict(list)
for i in gun_subj: gcl[find(i)].append(i)
same = tot = 0
multi_pure = []
for ids in gcl.values():
    if len(ids) < 2: continue
    subs = Counter(rows[i]["subject"] for i in ids)
    top, cnt = subs.most_common(1)[0]
    multi_pure.append((len(ids), top, cnt, dict(subs)))
    same += cnt; tot += len(ids)
multi_pure.sort(reverse=True)
print(f"多成员簇 {len(multi_pure)} 个；簇内同主体占比 = {same}/{tot} = {same/tot:.2f}" if tot else "无多成员簇")
for sz, top, cnt, subs in multi_pure[:12]:
    print(f"  簇({sz}个) 主体={top} 纯度={cnt}/{sz} 成员={list(subs.values())}")

# ---- 实验 C：不预设簇数，看看'一个包最近的邻居'是不是同类（KNN 视角）----
print("\n== 实验 C: 每个包的最近邻居是不是同主体")
hit = miss = 0; miss_ex = []
for i in gun_subj:
    best, bs = -1, 0.0
    for j in gun_subj:
        if i == j: continue
        s = jaccard(vecs[i], vecs[j])
        if s > bs: bs, best = s, j
    if best < 0: continue
    if rows[best]["subject"] == rows[i]["subject"]: hit += 1
    else:
        miss += 1
        if len(miss_ex) < 8 and bs > 0.3: miss_ex.append((rows[i]["subject"], rows[best]["subject"], round(bs,2), rows[i]["path"][-40:]))
print(f"最近邻居同主体: {hit}/{hit+miss} = {hit/(hit+miss):.2f}")
for a, b, s, p in miss_ex: print(f"  误判: {a} -> {b} (相似{s}) {p}")

# make_difficulty_sets.py
import json, os, math, argparse, itertools, numpy as np
from collections import defaultdict
from typing import List, Dict, Tuple

def load_export(export_path: str):
    with open(export_path, "r", encoding="utf-8") as f:
        return json.load(f)

def load_embeddings_json(path: str) -> Dict[str, np.ndarray]:
    with open(path, "r", encoding='utf-8') as f:
        raw = json.load(f)
    out = {}
    for k,v in raw.items():
        out[k] = np.array(v, dtype=np.float32)
    return out

def normalize_vecs(d: Dict[str, np.ndarray]):
    for k,v in d.items():
        norm = np.linalg.norm(v)
        if norm > 0:
            d[k] = v / norm
    return d

def compute_pairwise_cosine_matrix(ids: List[str], emb_map: Dict[str,np.ndarray]) -> np.ndarray:
    n = len(ids)
    M = np.zeros((n,n), dtype=np.float32)
    for i in range(n):
        vi = emb_map.get(ids[i])
        if vi is None: continue
        for j in range(i+1,n):
            vj = emb_map.get(ids[j])
            if vj is None: continue
            sim = float(np.dot(vi, vj))
            M[i,j] = sim
            M[j,i] = sim
    np.fill_diagonal(M, 1.0)
    return M

# greedy hard: seed highest-pair, then add item that maximizes mean pairwise similarity to current set
def greedy_hard_sets(ids: List[str], M: np.ndarray, k: int, num_sets:int) -> List[List[str]]:
    n = len(ids)
    if k > n: return []
    results = []
    used_seeds = set()
    # candidate pairs sorted desc
    pairs = [ (M[i,j], i, j) for i in range(n) for j in range(i+1,n) ]
    pairs.sort(reverse=True, key=lambda x:x[0])
    seed_idx = 0
    while len(results) < num_sets and seed_idx < len(pairs):
        _, i0, j0 = pairs[seed_idx]
        seed_idx += 1
        if (i0,j0) in used_seeds: continue
        used_seeds.add((i0,j0))
        cur = [i0, j0]
        # add greedily
        while len(cur) < k:
            best_score = -1e9
            best_idx = None
            for cand in range(n):
                if cand in cur: continue
                # mean similarity if added
                sims = [M[cand, other] for other in cur]
                mean_sim = sum(sims)/len(sims)
                if mean_sim > best_score:
                    best_score = mean_sim
                    best_idx = cand
            if best_idx is None: break
            cur.append(best_idx)
        groups = [ids[x] for x in cur]
        # avoid duplicates (by set)
        if groups not in results:
            results.append(groups)
    return results

# greedy easy: seed lowest-pair, then add item that minimizes mean pairwise similarity to current set
def greedy_easy_sets(ids: List[str], M: np.ndarray, k: int, num_sets:int) -> List[List[str]]:
    n = len(ids)
    if k > n: return []
    results = []
    pairs = [ (M[i,j], i, j) for i in range(n) for j in range(i+1,n) ]
    pairs.sort(key=lambda x:x[0])  # ascending
    seed_idx = 0
    used_seeds = set()
    while len(results) < num_sets and seed_idx < len(pairs):
        _, i0, j0 = pairs[seed_idx]
        seed_idx += 1
        if (i0,j0) in used_seeds: continue
        used_seeds.add((i0,j0))
        cur = [i0, j0]
        while len(cur) < k:
            best_score = 1e9
            best_idx = None
            for cand in range(n):
                if cand in cur: continue
                sims = [M[cand, other] for other in cur]
                mean_sim = sum(sims)/len(sims)
                if mean_sim < best_score:
                    best_score = mean_sim
                    best_idx = cand
            if best_idx is None: break
            cur.append(best_idx)
        groups = [ids[x] for x in cur]
        if groups not in results:
            results.append(groups)
    return results

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--export", required=True, help="export.json (renders with pool/subpool info)")
    ap.add_argument("--embeddings", required=True, help="embeddings.json mapping object_id -> vector")
    ap.add_argument("--out", default="difficulty_sets.json")
    ap.add_argument("--num_sets", type=int, default=10, help="per size & difficulty")
    ap.add_argument("--sizes", nargs="+", type=int, default=[2,4,6,8,10,12])
    args = ap.parse_args()

    export = load_export(args.export)
    emb_map = load_embeddings_json(args.embeddings)
    emb_map = normalize_vecs(emb_map)

    # build subpool -> list(object_id)
    subpools = defaultdict(list)
    for item in export:
        # export entries had: "pool" (like Estatuas_1) and object_id
        sub = item.get("pool") or item.get("subpool") or item.get("poolName") or item.get("poolId")
        oid = item.get("object_id")
        if sub and oid:
            subpools[sub].append(oid)

    out = {"subpools": []}
    for subname, ids in subpools.items():
        # filter ids that have embeddings
        ids_with_emb = [i for i in ids if i in emb_map]
        if len(ids_with_emb) < 2:
            print(f"skip subpool {subname} (need >=2 embeddings, have {len(ids_with_emb)})")
            continue
        M = compute_pairwise_cosine_matrix(ids_with_emb, emb_map)
        subentry = {"subpoolId": subname, "sets": []}
        for k in args.sizes:
            if k > len(ids_with_emb): 
                continue
            hard = greedy_hard_sets(ids_with_emb, M, k, args.num_sets)
            easy = greedy_easy_sets(ids_with_emb, M, k, args.num_sets)
            subentry["sets"].append({"size": k, "difficulty":"hard", "groups": hard})
            subentry["sets"].append({"size": k, "difficulty":"easy", "groups": easy})
        out["subpools"].append(subentry)

    with open(args.out, "w", encoding='utf-8') as f:
        json.dump(out, f, indent=2, ensure_ascii=False)
    print("written", args.out)

if __name__ == "__main__":
    main()

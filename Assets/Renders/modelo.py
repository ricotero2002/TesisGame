# make_sets_and_viz.py  (con generación de embeddings faltantes)
import os, json, pickle, math, random
from pathlib import Path
import numpy as np
from PIL import Image
from sklearn.cluster import AgglomerativeClustering, KMeans

# ---------- CONFIG ----------
BASE = Path(r"C:\Users\Agustin\Tesis\Assets\Renders")  # AJUSTA si hace falta
EXPORT_JSON = BASE / "export.json"
EMB_DIR = BASE / "embeddings"
OUT_JSON = BASE / "difficulty_sets_with_scores.json"
VIZ_DIR = BASE / "sets_viz"
os.makedirs(VIZ_DIR, exist_ok=True)

SIZES = [2,4,6,8,10,12]
NUM_SETS = 2      # intento por (size,difficulty)
DISJOINT = True
RNG = random.Random(0)

# CLIP config (puedes cambiar modelo si querés)
CLIP_MODEL_NAME = "ViT-B-32"
CLIP_PRETRAIN = "openai"
# ----------------------------

# ---------- try to import open_clip/torch (optional) ----------
_have_clip = True
try:
    import torch
    import open_clip
    DEVICE = "cuda" if torch.cuda.is_available() else "cpu"
except Exception as e:
    _have_clip = False
    DEVICE = "cpu"
    print("[WARN] open_clip/torch no disponibles. El script seguirá pero NO generará embeddings nuevos.")
    print(f"  Detalle import error: {e}")

# ---------- I/O helpers ----------
def load_export(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)

def load_existing_out(path):
    if not path.exists():
        return {}
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}

def save_json(obj, path):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2, ensure_ascii=False)

# ---------- Embedding generation helpers ----------
def _load_clip_model_and_preprocess():
    """Carga modelo open_clip y transform. Retorna (model, preprocess) o (None,None) si falla."""
    if not _have_clip:
        return None, None
    try:
        model, _, preprocess = open_clip.create_model_and_transforms(
            CLIP_MODEL_NAME, pretrained=CLIP_PRETRAIN, device=DEVICE
        )
        model.to(DEVICE)
        model.eval()
        return model, preprocess
    except Exception as e:
        print(f"[WARN] no se pudo crear modelo open_clip: {e}")
        return None, None

def resolve_image_paths_from_entry(entry, base_dir):
    imgs = entry.get("images", []) or []
    resolved = []
    for p in imgs:
        p0 = Path(p)
        if p0.is_absolute() and p0.exists():
            resolved.append(p0); continue
        # try relative to base_dir and some variants
        candidates = [
            base_dir / p,
            base_dir / p0.name,
            base_dir / p0.parts[-2] / p0.name if len(p0.parts) >= 2 else None
        ]
        found = None
        for c in candidates:
            if c and c.exists():
                found = c; break
        if found:
            resolved.append(found)
        else:
            matches = list(base_dir.glob("**/" + p0.name))
            if matches:
                resolved.append(matches[0])
    return resolved

def compute_and_save_embeddings_for_export(export_data, base_renders_dir, emb_dir, model_name=CLIP_MODEL_NAME, pretrained=CLIP_PRETRAIN):
    """
    Genera .pkl con {'object_id':..., 'vector': np.array} para objetos que no tienen embedding.
    Si open_clip/torch no están disponibles, devuelve 0 y solo informa.
    """
    os.makedirs(emb_dir, exist_ok=True)
    if not _have_clip:
        print("[INFO] open_clip/torch no instalados -> no se generarán embeddings automáticamente.")
        return 0

    model, preprocess = _load_clip_model_and_preprocess()
    if model is None or preprocess is None:
        print("[WARN] no se pudo cargar modelo CLIP -> no se generarán embeddings.")
        return 0

    created = 0
    for obj in export_data:
        oid = obj["object_id"]
        emb_fname = emb_dir / (oid.replace("/", "_") + ".pkl")
        if emb_fname.exists():
            continue
        img_paths = resolve_image_paths_from_entry(obj, base_renders_dir)
        if not img_paths:
            print(f"[WARN] no hay imágenes encontradas para {oid} -> no se crea embedding.")
            continue
        vectors = []
        for ip in img_paths:
            try:
                im = Image.open(ip).convert("RGB")
                tensor = preprocess(im).unsqueeze(0).to(DEVICE)
                with torch.no_grad():
                    v = model.encode_image(tensor)
                    v = v / v.norm(dim=-1, keepdim=True)
                    v = v.cpu().numpy().astype(np.float32).reshape(-1)
                    vectors.append(v)
            except Exception as e:
                print(f"[WARN] error procesando imagen {ip} para {oid}: {e}")
        if not vectors:
            print(f"[WARN] no se pudo extraer vector de ninguna imagen de {oid}")
            continue
        emb = np.mean(np.vstack(vectors), axis=0)
        emb = emb / (np.linalg.norm(emb) + 1e-12)
        try:
            with open(emb_fname, "wb") as f:
                pickle.dump({"object_id": oid, "vector": emb}, f)
            created += 1
            print(f"[OK] embedding creado: {emb_fname.name}")
        except Exception as e:
            print(f"[ERROR] no se pudo guardar embedding para {oid}: {e}")
    print(f"[INFO] embeddings creados: {created}")
    return created

# ---------- existing embedding loader ----------
def load_embedding_for_object(obj_id):
    fname = str(obj_id).replace("/", "_") + ".pkl"
    p = EMB_DIR / fname
    if not p.exists(): return None
    try:
        with open(p, "rb") as f: data = pickle.load(f)
        vec = data.get("vector") if isinstance(data, dict) else None
        if vec is None: return None
        v = np.array(vec, dtype=np.float32)
        norm = np.linalg.norm(v)
        if norm > 0: v = v / norm
        return v
    except Exception as e:
        print(f"[WARN] error cargando pickle {p}: {e}")
        return None

def build_emb_map(export_data):
    emb_map = {}
    for obj in export_data:
        oid = obj["object_id"]
        vec = load_embedding_for_object(oid)
        if vec is not None:
            emb_map[oid] = vec
    return emb_map

# ---------- rest of pipeline helpers (unchanged) ----------
def pairwise_cosine(ids, emb_map):
    n = len(ids)
    M = np.eye(n, dtype=np.float32)
    for i in range(n):
        vi = emb_map[ids[i]]
        for j in range(i+1, n):
            vj = emb_map[ids[j]]
            M[i,j] = M[j,i] = float(np.dot(vi, vj))
    return M

def intra_mean_for_group(group_ids, ids_list, M):
    idxs = [ ids_list.index(g) for g in group_ids ]
    if len(idxs) <= 1: return 0.0
    sub = M[np.ix_(idxs, idxs)]
    s = sub.sum() - np.trace(sub)
    denom = (len(idxs)*(len(idxs)-1))
    return float(s/denom) if denom>0 else 0.0

def max_sets_allowed(n, k, requested): return max(1, min(requested, n // k))
def medoid_of_indices(idxs, ids, M):
    best = None; best_score = -1e9
    for ii in idxs:
        sims = [ M[ii, jj] for jj in idxs if jj != ii ]
        score = sum(sims)
        if score > best_score:
            best_score = score; best = ii
    return best

def generate_easy_hard_sets_with_embmap(ids, emb_map, M, k, num_sets, disjoint=True, rng=None):
    rng = rng or random.Random(0)
    n = len(ids)
    if k > n: return [], []
    max_sets = max_sets_allowed(n, k, num_sets)
    V = np.vstack([ emb_map[i] for i in ids ])
    # HARD
    n_clusters = max(1, n // max(1, k))
    try:
        cl = AgglomerativeClustering(n_clusters=n_clusters).fit(V)
        labels = cl.labels_
    except Exception:
        labels = np.zeros(n, dtype=int)
    clusters = {}
    for idx, lab in enumerate(labels):
        clusters.setdefault(lab, []).append(idx)
    hard_candidates = []
    for lab, idxs in clusters.items():
        if len(idxs) < 2: continue
        subM = M[np.ix_(idxs, idxs)]
        pairs = sorted([ (subM[i,j], i, j) for i in range(len(idxs)) for j in range(i+1,len(idxs)) ], reverse=True)
        rng.shuffle(pairs)
        for score, ia, jb in pairs:
            if len(hard_candidates) >= max_sets * 4: break
            cur = [ idxs[ia], idxs[jb] ]
            while len(cur) < k:
                best_idx = None; best_mean = -1e9
                for cand in idxs:
                    if cand in cur: continue
                    sims = [ M[cand, other] for other in cur ]
                    mean_sim = sum(sims)/len(sims)
                    if mean_sim > best_mean:
                        best_mean = mean_sim; best_idx = cand
                if best_idx is None: break
                cur.append(best_idx)
            if len(cur) == k:
                hard_candidates.append([ ids[x] for x in cur ])
    def intra_mean_group(group):
        idxs = [ ids.index(g) for g in group ]
        sub = M[np.ix_(idxs, idxs)]
        s = sub.sum() - np.trace(sub)
        denom = (len(group)*(len(group)-1)); return float(s/denom) if denom>0 else 0.0
    hard_sorted = sorted(hard_candidates, key=lambda g: intra_mean_group(g), reverse=True)
    hard_selected = []; used = set()
    for g in hard_sorted:
        if len(hard_selected) >= max_sets: break
        if disjoint and any(x in used for x in g): continue
        hard_selected.append(g)
        if disjoint: used.update(g)
    # EASY (k-means)
    cluster_count = min(n, k)
    try:
        kmeans = KMeans(n_clusters=cluster_count, random_state=0).fit(V)
        lab_k = kmeans.labels_
    except Exception:
        lab_k = np.zeros(n, dtype=int)
    clusters_k = {}
    for idx, lab in enumerate(lab_k):
        clusters_k.setdefault(lab, []).append(idx)
    easy_selected = []; attempts = 0
    while len(easy_selected) < max_sets and attempts < max_sets * 12:
        attempts += 1
        chosen_clusters = list(clusters_k.keys())
        if len(chosen_clusters) > k:
            rng.shuffle(chosen_clusters); chosen_clusters = chosen_clusters[:k]
        group = []; ok = True
        for lc in chosen_clusters:
            idxs = clusters_k[lc]
            med_idx = medoid_of_indices(idxs, ids, M)
            if med_idx is None: ok = False; break
            med_id = ids[med_idx]
            if disjoint and med_id in used:
                alt = None
                for ii in idxs:
                    cand = ids[ii]
                    if cand not in used: alt = cand; break
                if alt is None: ok = False; break
                med_id = alt
            group.append(med_id)
        if ok and len(group) == k and group not in easy_selected:
            easy_selected.append(group)
            if disjoint: used.update(group)
    # fallback if none
    if not easy_selected:
        pairs = sorted([ (M[i,j], i, j) for i in range(n) for j in range(i+1,n) ], key=lambda x: x[0])
        seed_idx = 0
        while len(easy_selected) < max_sets and seed_idx < len(pairs):
            _, i0, j0 = pairs[seed_idx]; seed_idx += 1
            cur = [i0,j0]
            while len(cur) < k:
                best_score = 1e9; best_idx = None
                for cand in range(n):
                    if cand in cur or (disjoint and ids[cand] in used): continue
                    sims = [ M[cand, other] for other in cur ]
                    mean_sim = sum(sims)/len(sims)
                    if mean_sim < best_score:
                        best_score = mean_sim; best_idx = cand
                if best_idx is None: break
                cur.append(best_idx)
            if len(cur) == k:
                easy_selected.append([ ids[x] for x in cur ])
                if disjoint: used.update([ ids[x] for x in cur ])
    return easy_selected, hard_selected

# image helpers (same logic as antes)
def find_thumbnail_for_object(oid, base_dir):
    folder = oid.split("/")[-1]
    candidates = [
        base_dir / folder,
        base_dir / "Assets" / "Renders" / folder,
        base_dir
    ]
    for fp in candidates[:2]:
        if fp.exists() and fp.is_dir():
            imgs = sorted([p for p in fp.glob("*.png")])
            if imgs:
                for pat in ["*v0_l0*.png","*v0*.png","*.png"]:
                    matched = [p for p in imgs if p.match(pat)]
                    if matched: return matched[0]
                return imgs[0]
    deep = list(base_dir.glob(f"**/{folder}/*v0*png"))
    if deep: return deep[0]
    deep_any = list(base_dir.glob(f"**/{folder}/*.png"))
    if deep_any: return deep_any[0]
    return None

def create_and_save_group_image(group_ids, base_dir, save_path, title=""):
    from PIL import Image, ImageDraw, ImageFont
    thumbs = []
    for oid in group_ids:
        img_path = find_thumbnail_for_object(oid, base_dir)
        if img_path and img_path.exists():
            try:
                im_thumb = Image.open(img_path).convert("RGBA")
                im_thumb.thumbnail((256,256), Image.LANCZOS)
                thumbs.append(im_thumb)
                continue
            except Exception:
                pass
        thumbs.append(None)
    cols = min(6, max(1,len(thumbs)))
    rows = math.ceil(len(thumbs)/cols) if cols>0 else 1
    canvas_w = cols * 256
    canvas_h = rows * 256 + 60
    canvas = Image.new("RGBA", (canvas_w, canvas_h), (255,255,255,255))
    for idx, t in enumerate(thumbs):
        x = (idx % cols) * 256
        y = (idx // cols) * 256
        if t is not None:
            canvas.paste(t, (x,y), t)
        else:
            ph = Image.new("RGBA", (256,256), (220,220,220,255))
            canvas.paste(ph, (x,y))
    try:
        draw = ImageDraw.Draw(canvas)
        try:
            f = ImageFont.truetype("arial.ttf", 14)
        except:
            f = None
        draw.text((6, canvas_h-54), title, fill=(0,0,0), font=f)
    except Exception:
        pass
    canvas.save(save_path)

# ---------- pipeline ----------
export_data = load_export(EXPORT_JSON)

# --- generate embeddings for missing objects before building emb_map ---
print("[INFO] buscando objetos sin embedding (.pkl) y generándolos si es posible...")
new_created = compute_and_save_embeddings_for_export(export_data, BASE, EMB_DIR)
print(f"[INFO] embeddings creados en esta ejecución: {new_created}")

# group export entries by category -> subpool -> members
category_map = {}
for obj in export_data:
    cat = obj.get("category", "Uncategorized")
    sp = obj.get("subpool", "default")
    category_map.setdefault(cat, {}).setdefault(sp, []).append(obj["object_id"])

# load existing results to reuse
existing = load_existing_out(OUT_JSON)

emb_map = build_emb_map(export_data)
print(f"[INFO] embeddings disponibles tras intento de creación: {len(emb_map)} objects")

final = {"categories": []}

def existing_has_sets_for(existing_obj, category, subpool, k, diff):
    if not existing_obj: return False
    for c in existing_obj.get("categories", []):
        if c.get("category") != category: continue
        for sp in c.get("subpools", []):
            if sp.get("subpoolId") != subpool: continue
            for s in sp.get("sets", []):
                if s.get("size") == k and s.get("difficulty") == diff and s.get("group"):
                    return True
    return False

for cat, subs in category_map.items():
    cat_entry = {"category": cat, "subpools": []}
    for sp, ids_all in subs.items():
        ids = [i for i in ids_all if i in emb_map]
        if len(ids) < 2:
            print(f"[INFO] saltando subpool {sp} en categoria {cat}: n_embeddings_validos={len(ids)} (<2)")
            continue
        print(f"[INFO] Processing category={cat} subpool={sp} (n={len(ids)})")
        M = pairwise_cosine(ids, emb_map)
        sp_entry = {"subpoolId": sp, "sets": []}
        for k in SIZES:
            if k > len(ids): continue
            if existing and existing_has_sets_for(existing, cat, sp, k, "hard") and existing_has_sets_for(existing, cat, sp, k, "easy"):
                for c in existing.get("categories", []):
                    if c.get("category") != cat: continue
                    for e_sp in c.get("subpools", []):
                        if e_sp.get("subpoolId") != sp: continue
                        for s in e_sp.get("sets", []):
                            if s.get("size") == k and s.get("group"):
                                sp_entry["sets"].append(s)
                print(f"[INFO] Reused existing sets for size={k} (category={cat} subpool={sp})")
                continue

            easy_sets, hard_sets = generate_easy_hard_sets_with_embmap(ids, emb_map, M, k, NUM_SETS, disjoint=DISJOINT, rng=RNG)
            all_groups = [("easy", g) for g in easy_sets] + [("hard", g) for g in hard_sets]
            intra_vals = [ intra_mean_for_group(g, ids, M) for _, g in all_groups ]
            minv, maxv = (min(intra_vals), max(intra_vals)) if intra_vals else (0.0, 1.0)
            for diff, groups in [("hard", hard_sets), ("easy", easy_sets)]:
                for idx, g in enumerate(groups, start=1):
                    im = intra_mean_for_group(g, ids, M)
                    norm = (im - minv) / (maxv - minv) if (maxv - minv) > 1e-6 else 0.0
                    hardness_pct = float(norm * 100.0)
                    easiness_pct = float((1.0 - norm) * 100.0)
                    viz_fname = f"{cat.replace(' ','_')}_sub_{sp.replace(' ','_')}_size{k}_{diff}_{idx:02d}.png"
                    viz_path = Path(VIZ_DIR) / viz_fname
                    create_and_save_group_image(g, BASE, viz_path, title=f"{cat} | {sp} | size={k} | {diff} | hard%={hardness_pct:.1f}")
                    entry = {
                        "size": k,
                        "difficulty": diff,
                        "group": g,
                        "intra_mean": im,
                        "hardness_pct": hardness_pct,
                        "easiness_pct": easiness_pct,
                        "viz_image": str(viz_path.name)
                    }
                    sp_entry["sets"].append(entry)
        cat_entry["subpools"].append(sp_entry)
    final["categories"].append(cat_entry)

# copy leftover existing categories/subpools not processed (to avoid data loss)
if existing:
    for c in existing.get("categories", []):
        cat = c.get("category")
        if not any(x["category"] == cat for x in final["categories"]):
            final["categories"].append(c)

save_json(final, OUT_JSON)
print(f"[INFO] Saved difficulty sets JSON: {OUT_JSON}")
print(f"[INFO] Visuals in: {VIZ_DIR}")

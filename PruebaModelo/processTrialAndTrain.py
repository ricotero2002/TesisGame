#!/usr/bin/env python3
"""
process_logs_and_features.py - versión extendida

Cambios respecto a la versión previa:
 - si se pasa --difficulty-sets, por cada trial intenta encontrar la "set" que contiene render_group y
   anota set_intra_mean, set_hardness_pct, set_easiness_pct, set_size, set_difficulty, set_subpoolId, set_category
 - guarda un JSON por trial en outdir/trial_jsons/ que incluye sim_* y set_* (auditoría)
 - swap_history ahora puede ser un dict (único swap) o una lista; avg_swaps_per_trial lo cuenta correctamente
 - mantiene la descripción parseada original en columna "_parsed_description" (para escribir el JSON de auditoría)
"""
import argparse
import json
import os
from pathlib import Path
from typing import Dict, Any, List, Optional
import numpy as np
import pandas as pd
import math
import pickle
import sys
import joblib

# ML
from sklearn.ensemble import RandomForestClassifier
from sklearn.model_selection import cross_val_score, StratifiedKFold
from sklearn.metrics import roc_auc_score, accuracy_score

# stats: ppf for d' (try scipy first)
try:
    from scipy.stats import norm
    norm_ppf = norm.ppf
except Exception:
    def norm_ppf(p):
        if p <= 0: p = 1e-10
        if p >= 1: p = 1 - 1e-10
        # Acklam approximation simplified (acceptable)
        from math import sqrt, log
        # use built-in approximation via inverse error function? Keep simple fallback:
        # For our uses this fallback is OK; if precision critical, install scipy.
        import mpmath as mp
        return float(mp.sqrt(2) * mp.erfinv(2*p - 1))

# ---------------------------
# IO helpers
# ---------------------------
def load_logs_file(path: Path) -> List[Dict[str,Any]]:
    text = path.read_text(encoding="utf8")
    obj = json.loads(text)
    logs = obj.get("logs", obj) if isinstance(obj, dict) else obj
    return logs

def try_parse_description_as_json(desc: str):
    try:
        return json.loads(desc)
    except Exception:
        return None

# ---------------------------
# Difficulty sets loader / finder (NEW)
# ---------------------------
def load_difficulty_sets(path: Path):
    try:
        txt = path.read_text(encoding="utf8")
        return json.loads(txt)
    except Exception as e:
        print("[WARN] could not load difficulty sets:", e)
        return None

def find_set_for_group(diff_root: Dict[str,Any], chosen_group: List[str], difficulty_hint: Optional[str]=None, category_hint: Optional[str]=None):
    """
    Busca la set (retorna dict con keys: set_obj, subpoolId, category) que mejor contiene chosen_group.
    Strategy: exact match (order-agnostic) prioritized by category_hint+difficulty_hint,
              then subset match.
    """
    if not diff_root or not chosen_group:
        return None
    chosen_set = set(chosen_group)

    # helpers
    def iter_sets():
        for cat in diff_root.get("categories", []):
            cat_name = cat.get("category")
            for sp in cat.get("subpools", []):
                spid = sp.get("subpoolId")
                for s in sp.get("sets", []):
                    yield cat_name, spid, s

    # 1) exact match prefer category+difficulty
    if category_hint:
        for cat_name, spid, s in iter_sets():
            if cat_name != category_hint: continue
            if difficulty_hint and s.get("difficulty") != difficulty_hint: continue
            if set(s.get("group", [])) == chosen_set:
                return {"set": s, "subpoolId": spid, "category": cat_name}
    # 2) exact match anywhere (prefer difficulty hint)
    for cat_name, spid, s in iter_sets():
        if difficulty_hint and s.get("difficulty") != difficulty_hint: continue
        if set(s.get("group", [])) == chosen_set:
            return {"set": s, "subpoolId": spid, "category": cat_name}
    # 3) subset match (chosen is subset of candidate)
    if category_hint:
        for cat_name, spid, s in iter_sets():
            if cat_name != category_hint: continue
            if difficulty_hint and s.get("difficulty") != difficulty_hint: continue
            if chosen_set.issubset(set(s.get("group", []))):
                return {"set": s, "subpoolId": spid, "category": cat_name}
    for cat_name, spid, s in iter_sets():
        if difficulty_hint and s.get("difficulty") != difficulty_hint: continue
        if chosen_set.issubset(set(s.get("group", []))):
            return {"set": s, "subpoolId": spid, "category": cat_name}
    return None

# ---------------------------
# Embedding helpers (unchanged)
# ---------------------------
def load_embedding_pkl(emb_dir: Path, object_id: str):
    fname = object_id.replace("/", "_") + ".pkl"
    p = emb_dir / fname
    if not p.exists():
        return None
    try:
        with open(p, "rb") as f:
            data = pickle.load(f)
        vec = None
        if isinstance(data, dict):
            vec = data.get("vector") or data.get("emb") or data.get("embedding")
        elif isinstance(data, (list, tuple, np.ndarray)):
            vec = np.array(data, dtype=np.float32)
        if vec is None:
            return None
        v = np.array(vec, dtype=np.float32)
        norm = np.linalg.norm(v)
        if norm > 0:
            v = v / norm
        return v
    except Exception as e:
        print(f"[WARN] error loading embedding {p}: {e}")
        return None

def compute_similarity_aggs_for_trial(trial_row, emb_map: Dict[str,np.ndarray], topk=3, thresh=0.8):
    out = {"sim_max": np.nan, "sim_mean_top3": np.nan, "sim_count_above_0_8": 0, "sim_entropy": np.nan}
    obj = trial_row.get("object_id")
    group = trial_row.get("render_group") or []
    if not obj or not group:
        return out
    if obj not in emb_map:
        return out
    v = emb_map[obj]
    sims = []
    for other in group:
        if other == obj: continue
        if other not in emb_map: continue
        sims.append(float(np.dot(v, emb_map[other])))
    if not sims:
        return out
    sims = np.array(sims, dtype=np.float32)
    out["sim_max"] = float(np.max(sims))
    topk_vals = np.sort(sims)[-min(topk, len(sims)):]
    out["sim_mean_top3"] = float(np.mean(topk_vals))
    out["sim_count_above_0_8"] = int(np.sum(sims > thresh))
    # entropy
    s = sims - sims.min()
    if s.sum() <= 0:
        out["sim_entropy"] = 0.0
    else:
        p = s / s.sum()
        ent = -np.sum(p * np.log(p + 1e-12))
        out["sim_entropy"] = float(ent)
    return out

# ---------------------------
# Feature computations (swap_history handling MODIFICADO)
# ---------------------------
def safe_proportion(count, total):
    return float(count) / float(total) if total and total > 0 else 0.0

def len_sw(x):
    """Cuenta cuántos swaps hubo en 'swap_history'. Acepta dict (1), list (len), str(json), NaN -> 0"""
    if x is None: return 0
    if isinstance(x, (list, tuple)): return len(x)
    if isinstance(x, dict): return 1
    if isinstance(x, str):
        try:
            p = json.loads(x)
            if isinstance(p, list): return len(p)
            if isinstance(p, dict): return 1
        except Exception:
            # fallback: not parseable
            return 1
    return 0

def compute_session_features(trials_df: pd.DataFrame, sim_thresh=0.8):
    s = {}
    n_trials = len(trials_df)
    s["n_trials"] = n_trials
    if "object_actual_moved" in trials_df.columns and "response" in trials_df.columns:
        correct = ((trials_df["object_actual_moved"] == True) & (trials_df["response"] == "different")) | \
                  ((trials_df["object_actual_moved"] == False) & (trials_df["response"] == "same"))
        s["accuracy_overall"] = safe_proportion(correct.sum(), n_trials)
    else:
        s["accuracy_overall"] = np.nan

    s["accuracy_by_similarity"] = {}
    if "object_similarity_label" in trials_df.columns:
        for lab, g in trials_df.groupby("object_similarity_label"):
            corr = ((g["object_actual_moved"] == True) & (g["response"] == "different")) | \
                   ((g["object_actual_moved"] == False) & (g["response"] == "same"))
            s["accuracy_by_similarity"][lab] = safe_proportion(corr.sum(), len(g))

    if "reaction_time_ms" in trials_df.columns:
        rtv = trials_df["reaction_time_ms"].dropna()
        rtv = rtv[rtv >= 0]
        s["reaction_time_mean"] = float(rtv.mean()) if len(rtv)>0 else np.nan
        s["reaction_time_median"] = float(rtv.median()) if len(rtv)>0 else np.nan
        s["reaction_time_std"] = float(rtv.std(ddof=0)) if len(rtv)>1 else 0.0
    else:
        s.update({"reaction_time_mean": np.nan, "reaction_time_median": np.nan, "reaction_time_std": np.nan})

    if "swap_event" in trials_df.columns:
        s["swap_count"] = int(trials_df["swap_event"].sum())
        s["swap_rate"] = float(s["swap_count"]) / n_trials if n_trials>0 else 0.0
        if "swap_history" in trials_df.columns:
            s["avg_swaps_per_trial"] = float(trials_df["swap_history"].apply(len_sw).mean())
        else:
            s["avg_swaps_per_trial"] = np.nan
    else:
        s.update({"swap_count":0,"swap_rate":0.0,"avg_swaps_per_trial":np.nan})

    if "sim_max" in trials_df.columns:
        s["max_similarity_to_any"] = float(trials_df["sim_max"].max(skipna=True))
        s["mean_top3_similarity"] = float(trials_df["sim_mean_top3"].mean(skipna=True))
        s["count_sim_above_0_8"] = int((trials_df["sim_count_above_0_8"] > 0).sum())
        s["similarity_entropy_mean"] = float(trials_df["sim_entropy"].mean(skipna=True))
    else:
        s.update({"max_similarity_to_any":np.nan,"mean_top3_similarity":np.nan,"count_sim_above_0_8":0,"similarity_entropy_mean":np.nan})

    p_diff = {}
    if "object_similarity_label" in trials_df.columns:
        for lab, g in trials_df.groupby("object_similarity_label"):
            p_diff[lab] = safe_proportion((g["response"]=="different").sum(), len(g))
    s["p_diff_by_label"] = p_diff
    s["MDTS_LDI_high"] = p_diff.get("high", np.nan) - p_diff.get("target", np.nan) if ("high" in p_diff and "target" in p_diff) else np.nan
    s["MDTS_LDI_low"] = p_diff.get("low", np.nan) - p_diff.get("target", np.nan) if ("low" in p_diff and "target" in p_diff) else np.nan
    s["MDTS_LDI_mean"] = np.nanmean([v for v in [s["MDTS_LDI_high"], s["MDTS_LDI_low"]] if not pd.isna(v)]) if not (pd.isna(s["MDTS_LDI_high"]) and pd.isna(s["MDTS_LDI_low"])) else np.nan

    if "object_actual_moved" in trials_df.columns and "response" in trials_df.columns:
        targets = trials_df[trials_df["object_actual_moved"]==True]
        foils = trials_df[trials_df["object_actual_moved"]==False]
        hits = ((targets["response"]=="different").sum()) if len(targets)>0 else 0
        fas = ((foils["response"]=="different").sum()) if len(foils)>0 else 0
        hitRate = (hits + 0.5) / (len(targets) + 1.0) if len(targets)>=0 else np.nan
        faRate = (fas + 0.5) / (len(foils) + 1.0) if len(foils)>=0 else np.nan
        try:
            s["dprime"] = float(norm_ppf(hitRate) - norm_ppf(faRate))
        except Exception:
            s["dprime"] = np.nan
    else:
        s["dprime"] = np.nan

    return s

# ---------------------------
# Parsing logs -> DataFrame (MODIFIED: keep parsed description in _parsed_description; don't wrap swap_history into list)
# ---------------------------
def extract_trials_from_logs(logs_list: List[Dict[str,Any]]) -> pd.DataFrame:
    trials = []
    for log in logs_list:
        et = log.get("event_type") or log.get("event")
        desc = log.get("description")
        if et is None or desc is None: continue
        if str(et).lower() == "trial":
            parsed = try_parse_description_as_json(desc)
            if parsed is None:
                try:
                    parsed = json.loads(desc.replace("'", "\""))
                except Exception:
                    parsed = {"raw_description": desc}
            t = {}
            for k,v in parsed.items():
                t[k] = v
            if "response" in t and isinstance(t["response"], str):
                t["response"] = t["response"].strip().lower()
            if "phase" in t and isinstance(t["phase"], str):
                t["phase"] = t["phase"].strip().lower()
            if "object_similarity_label" in t and isinstance(t["object_similarity_label"], str):
                t["object_similarity_label"] = t["object_similarity_label"].strip().lower()
            # KEEP swap_history as-is: may be dict (single swap) or list
            # store original parsed description for audit (used later to write per-trial json)
            trials.append({**t, "_raw_event": log, "_parsed_description": parsed})
    df = pd.DataFrame(trials)
    expected_cols = ["session_id","participant_id","timestamp","trial_index","phase","object_id","object_category",
                     "object_subpool","object_similarity_label","object_actual_moved","participant_said_moved","response",
                     "reaction_time_ms","memorization_time_ms","swap_event","swap_history","render_seed","render_group"]
    for c in expected_cols:
        if c not in df.columns:
            df[c] = pd.NA
    def ensure_list(x):
        if x is None or (isinstance(x, float) and np.isnan(x)): return []
        if isinstance(x, list): return x
        if isinstance(x, str):
            try:
                v = json.loads(x)
                if isinstance(v, list): return v
            except:
                pass
            return [x]
        return x
    df["render_group"] = df["render_group"].apply(ensure_list)
    df["object_actual_moved"] = df["object_actual_moved"].apply(lambda x: bool(x) if pd.notna(x) else False)
    df["swap_event"] = df["swap_event"].apply(lambda x: bool(x) if pd.notna(x) else False)
    df["reaction_time_ms"] = pd.to_numeric(df["reaction_time_ms"], errors="coerce").fillna(-1).astype(int)
    df["memorization_time_ms"] = pd.to_numeric(df["memorization_time_ms"], errors="coerce").fillna(-1).astype(int)
    df["trial_index"] = pd.to_numeric(df["trial_index"], errors="coerce").fillna(-1).astype(int)
    return df

# ---------------------------
# Main flow (integración con difficulty sets y guardado per-trial JSON)
# ---------------------------
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--logs", required=True, help="Path to offline logs JSON")
    parser.add_argument("--emb-dir", default=None, help="Optional: directory with embeddings .pkl")
    parser.add_argument("--difficulty-sets", default=None, help="Optional: difficulty_sets_with_scores.json")
    parser.add_argument("--labels", default=None, help="Optional CSV with columns ['participant_id' or 'session_id','label']")
    parser.add_argument("--outdir", default="out_logs", help="Output folder")
    parser.add_argument("--sim-thresh", type=float, default=0.8)
    parser.add_argument("--rf-train", action="store_true", help="Train RandomForest if labels provided")
    parser.add_argument("--random-seed", type=int, default=0)
    args = parser.parse_args()

    outdir = Path(args.outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    trial_json_dir = outdir / "trial_jsons"
    trial_json_dir.mkdir(parents=True, exist_ok=True)

    logs_path = Path(args.logs)
    logs = load_logs_file(logs_path)
    trials_df = extract_trials_from_logs(logs)
    print(f"[INFO] Extracted {len(trials_df)} trial rows")

    emb_map = {}
    if args.emb_dir:
        emb_dir = Path(args.emb_dir)
        for idx, row in trials_df.iterrows():
            for oid in (row.get("render_group") or []) + ([row.get("object_id")] if row.get("object_id") else []):
                if oid and oid not in emb_map:
                    v = load_embedding_pkl(emb_dir, oid)
                    if v is not None: emb_map[oid] = v
        print(f"[INFO] Embeddings loaded for {len(emb_map)} unique objects")

    # dificultad
    diff_root = None
    if args.difficulty_sets:
        diff_root = load_difficulty_sets(Path(args.difficulty_sets))
        if diff_root:
            print("[INFO] Loaded difficulty sets JSON")

    # compute sim-aggs per trial if emb_map not empty
    if emb_map:
        for i, r in trials_df.iterrows():
            parsed = r.get("_parsed_description") or {}
            ag = compute_similarity_aggs_for_trial(r.to_dict(), emb_map, topk=3, thresh=args.sim_thresh)
            for k,v in ag.items():
                trials_df.at[i, k] = v

    # --- NEW: for each trial, try to find set in diff_root and annotate set_* columns
    if diff_root is not None:
        for i, r in trials_df.iterrows():
            parsed = r.get("_parsed_description") or {}
            rg = r.get("render_group") or []
            difficulty_hint = None
            # optional: infer difficulty hint from chosenGroup? If your system stores it, use it
            res = find_set_for_group(diff_root, rg, difficulty_hint=difficulty_hint, category_hint=r.get("object_category"))
            if res is not None:
                s = res["set"]
                trials_df.at[i, "set_intra_mean"] = s.get("intra_mean")
                trials_df.at[i, "set_hardness_pct"] = s.get("hardness_pct")
                trials_df.at[i, "set_easiness_pct"] = s.get("easiness_pct")
                trials_df.at[i, "set_size"] = s.get("size")
                trials_df.at[i, "set_difficulty"] = s.get("difficulty")
                trials_df.at[i, "set_subpoolId"] = res.get("subpoolId")
                trials_df.at[i, "set_category"] = res.get("category")
            else:
                trials_df.at[i, "set_intra_mean"] = np.nan
                trials_df.at[i, "set_hardness_pct"] = np.nan
                trials_df.at[i, "set_easiness_pct"] = np.nan
                trials_df.at[i, "set_size"] = np.nan
                trials_df.at[i, "set_difficulty"] = None
                trials_df.at[i, "set_subpoolId"] = None
                trials_df.at[i, "set_category"] = None

    # Save trial-by-trial CSV
    trials_out = outdir / "trials.csv"
    trials_df.to_csv(trials_out, index=False)
    print("[INFO] Wrote trials CSV:", trials_out)

    # --- Auditoría: escribir JSON por trial que incluya sim_* y set_* y parsed description original
    for i, r in trials_df.iterrows():
        parsed = r.get("_parsed_description") or {}
        audit = dict(parsed)  # start from parsed description
        # add computed sim fields if present
        for k in ["sim_max","sim_mean_top3","sim_count_above_0_8","sim_entropy",
                  "set_intra_mean","set_hardness_pct","set_easiness_pct","set_size","set_difficulty","set_subpoolId","set_category"]:
            if k in trials_df.columns:
                audit[k] = (None if pd.isna(r.get(k)) else r.get(k))
        # ensure minimal metadata
        audit["_session_id"] = r.get("session_id")
        audit["_trial_index"] = int(r.get("trial_index")) if pd.notna(r.get("trial_index")) else None
        fname = f"{audit.get('_session_id','unknown')}_trial_{audit.get('_trial_index','idx')}.json"
        (trial_json_dir / fname).write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding="utf8")

    # Group by session_id to compute session features
    sessions = []
    for sid, group in trials_df.groupby("session_id"):
        session_meta = {
            "session_id": sid,
            "participant_id": group["participant_id"].iloc[0] if len(group)>0 else None,
            "n_trials": len(group)
        }
        feats = compute_session_features(group, sim_thresh=args.sim_thresh)
        session_meta.update(feats)
        sessions.append(session_meta)
    sessions_df = pd.DataFrame(sessions)
    sessions_out = outdir / "sessions.csv"
    sessions_df.to_csv(sessions_out, index=False)
    print("[INFO] Wrote sessions CSV:", sessions_out)

    # RF training (unchanged from previous version)
    if args.labels and args.rf_train:
        labels_df = pd.read_csv(args.labels)
        merged = sessions_df.merge(labels_df, left_on="participant_id", right_on="participant_id", how="left")
        if "label" not in merged.columns:
            merged = sessions_df.merge(labels_df, left_on="session_id", right_on="session_id", how="left")
        if "label" not in merged.columns:
            print("[WARN] Could not find 'label' after joins; labels CSV must contain 'participant_id' or 'session_id' + 'label' column.")
        else:
            def flatten_row(row):
                flat = {}
                for k,v in row.items():
                    if isinstance(v, (int,float,np.floating,np.integer)):
                        flat[k]=v
                    elif isinstance(v, dict):
                        for kk,vv in v.items():
                            flat[f"{k}__{kk}"] = vv
                return flat
            feature_rows = []
            for _, r in merged.iterrows():
                flat = flatten_row(r.to_dict())
                flat.pop("session_id", None); flat.pop("participant_id", None); flat.pop("label", None)
                feature_rows.append(flat)
            X = pd.DataFrame(feature_rows).fillna(0.0)
            y = merged["label"].values
            rf = RandomForestClassifier(n_estimators=200, oob_score=True, random_state=args.random_seed)
            cv = StratifiedKFold(n_splits=5, shuffle=True, random_state=args.random_seed)
            acc = cross_val_score(rf, X, y, cv=cv, scoring="accuracy")
            try:
                roc = cross_val_score(rf, X, y, cv=cv, scoring="roc_auc")
                roc_mean = float(np.mean(roc))
            except Exception:
                roc_mean = float('nan')
            print(f"[RF] CV accuracy mean: {np.mean(acc):.4f} ± {np.std(acc):.4f}")
            print(f"[RF] CV ROC AUC (if available): {roc_mean}")
            rf.fit(X, y)
            model_path = outdir / "rf_model.joblib"
            joblib.dump({"model":rf, "feature_columns": list(X.columns)}, model_path)
            fi = pd.DataFrame({"feature": X.columns, "importance": rf.feature_importances_}).sort_values("importance", ascending=False)
            fi.to_csv(outdir / "feature_importances.csv", index=False)
            if hasattr(rf, "oob_score_"):
                print("[RF] OOB score:", rf.oob_score_)
    print("[DONE]")

if __name__ == "__main__":
    main()

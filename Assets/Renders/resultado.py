import json
import numpy as np
import matplotlib.pyplot as plt
import os

BASE_DIR = os.path.dirname(__file__)  # carpeta donde está resultado.py
EXPORT_JSON = os.path.join(BASE_DIR, "export.json")

with open(EXPORT_JSON, "r") as f:
    data = json.load(f)

# Supongamos que tienes embeddings en 'features.npy' alineados con data
embeddings = np.load("features.npy")  # shape (N, D)

with open("features_ids.json") as f:
    ids = json.load(f)

print("Embeddings shape:", embeddings.shape)
print("Primer ID:", ids[0])


# Normalizar
embeddings = embeddings / np.linalg.norm(embeddings, axis=1, keepdims=True)

# Matriz de similitudes
mat = embeddings @ embeddings.T

# Histograma
sims = mat[np.triu_indices(len(ids), k=1)]
plt.hist(sims, bins=50)
plt.xlabel("Cosine similarity")
plt.ylabel("Count")
plt.title("Histogram of pairwise similarities")
plt.show()

# Selección greedy
def greedy_most_similar(ids, mat, N=4):
    n = len(ids)
    best_i, best_j, best_val = -1, -1, -1.0
    for i in range(n):
        for j in range(i+1, n):
            if mat[i,j] > best_val:
                best_val = mat[i,j]; best_i=i; best_j=j
    S = [best_i, best_j]
    while len(S) < N:
        best_k = None; best_mean = -1.0
        for k in range(n):
            if k in S: continue
            mean_k = np.mean([mat[k,s] for s in S])
            if mean_k > best_mean:
                best_mean = mean_k; best_k = k
        S.append(best_k)
    return [ids[i] for i in S]

def greedy_most_different(ids, mat, N=4):
    n = len(ids)
    import random
    seed = random.randrange(n)
    S = [seed]
    while len(S) < N:
        best_k = None; best_score = 1e9
        for k in range(n):
            if k in S: continue
            mean_k = np.mean([mat[k,s] for s in S])
            if mean_k < best_score:
                best_score = mean_k; best_k = k
        S.append(best_k)
    return [ids[i] for i in S]

print("Grupo difícil:", greedy_most_similar(ids, mat, N=4))
print("Grupo fácil:", greedy_most_different(ids, mat, N=4))

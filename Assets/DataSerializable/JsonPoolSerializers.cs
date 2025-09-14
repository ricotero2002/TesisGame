using System;
using System.Collections.Generic;

[Serializable]
public class MemberData
{
    public string prefabPath;   // Ruta dentro de Resources o StreamingAssets
    public float scaleFactor;   // El factor calculado en TryAdd
}

[Serializable]
public class PoolData
{
    public string poolName;
    public string category;
    public float sizeThreshold;
    public List<MemberData> members = new List<MemberData>();
}

[Serializable]
public class PoolsContainer
{
    public List<PoolData> pools = new List<PoolData>();
}

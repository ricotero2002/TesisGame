using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

public enum RoomShape { Rectangle } //cross , etc
[CreateAssetMenu(menuName = "Room/RoomInformation")]
public class RoomInformation : ScriptableObject
{
    public int Maxcols;
    public float cellSizeStandar = 2.9f;
    private float aisleWidth = 4.5f;
    private float colSpacing = 1.5f;
    private float wallsSpacing = 1.5f;
    public float wallHeight = 5f;
    public float timeBetweenStandar = 10f;
    public float unityPlaneSize = 10f;
    public float espesor = 0.2f;

    [Header("PrefabsLigths")]
    public GameObject hallLightPrefab;
    public GameObject statueLightPrefab;

    [Header("PoolPrefabs")]
    public ArtPoolSO columnPool;
    public ArtPoolSO barrierPool;
    public ArtPoolSO doorPool;

    [Header("MaterialsPool")]
    public MaterialPoolSO floorMat;
    public MaterialPoolSO wallMat;
    public MaterialPoolSO ceilingMat;
    public MaterialPoolSO tileMat;

    [Header("Art Pool")]
    public ArtPoolGroupSO poolGroup;

    [Header("Corridor / layout tweaks")]
    [Tooltip("Distancia m�nima deseada en metros entre el centro de la puerta generada y la puerta del Hall (eje X).")]
    public float desiredDoorSeparation = 4.0f;


    // Par�metros ajustables desde el Inspector
    [Header("Trigger settings")]
    [Tooltip("Multiplicador para que el trigger sea un poco m�s grande que la tile (ej: 1.05 = 5% m�s).")]
    public float padding = 1.3f;
    [Tooltip("Centro Y offset relativo al centro del renderer (en metros). Si es 0 usa bounds center Y.")]
    public float centerYOffset = 0f;
    [Tooltip("Tama�o m�nimo en X/Z para evitar triggers muy peque�os.")]
    public float desiredHeightMeters = 4.0f;

    [Header("Art Pool")]
    private float multiplier = 1f;

    public void setMultiplier(float m)
    {
        this.multiplier = m / cellSizeStandar;
    }

    public float getWallsSpacing()
    {
        return wallsSpacing * multiplier;
    }

    public float getMultiplier()
    {
        return multiplier;
    }

    public float GetAisleWidth()
    {
        return aisleWidth * multiplier;
    }

    public float GetColSpacing()
    {
        return colSpacing * multiplier;
    }


    public static RoomShape GetRandomShape()
    {
        // Obtiene todos los valores definidos en RoomShape
        var values = System.Enum.GetValues(typeof(RoomShape));
        // Elige un �ndice al azar entre 0 y values.Length-1
        int index = Random.Range(0, values.Length);
        // Devuelve el valor correspondiente
        return (RoomShape)values.GetValue(index);
    }
}
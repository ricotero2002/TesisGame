using UnityEngine;

public class PopupManager : MonoBehaviour
{
    public static PopupManager Instance;
    [SerializeField] private GameObject popupPrefab;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Crea e instancia un popup usando el prefab asignado.
    /// </summary>
    /// <param name="title">Título a mostrar</param>
    /// <param name="message">Mensaje detallado</param>
    public void ShowPopup(string title, string message)
    {
        // Instanciamos debajo de este manager para mantener la jerarquía ordenada
        GameObject go = Instantiate(popupPrefab, transform);
        // Obtenemos el componente que sabe rellenar los campos y configurar botones
        var popup = go.GetComponent<SessionExpiredModal>();
        popup.Configure(title, message);
    }
}

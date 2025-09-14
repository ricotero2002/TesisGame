using UnityEngine;
using UnityEngine.UI;
using TMPro;  // TextMeshPro namespace

public class SessionExpiredModal : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text titleText;     // Referencia al componente de texto del t�tulo 
    [SerializeField] private TMP_Text messageText;   // Referencia al componente de texto del mensaje 
    [SerializeField] private Button closeButton;     // Referencia al bot�n de cerrar 

    /// <summary>
    /// Llama este m�todo justo despu�s de instanciar el prefab
    /// para configurar t�tulo, mensaje y comportamiento del bot�n.
    /// </summary>
    public void Configure(string title, string message)
    {
        titleText.text = title;
        messageText.text = message;

        // Aseguramos no duplicar listeners
        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(Close);
    }

    /// <summary>
    /// Destruye este objeto popup cuando se pulsa el bot�n.
    /// </summary>
    private void Close()
    {
        Destroy(gameObject);  // Destruye el GameObject 
    }
}

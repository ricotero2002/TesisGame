using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class StartGameUIController : MonoBehaviour
{
    [Header("UI Inputs")]
    public TMP_InputField inputColumnas;
    public TMP_InputField inputTime;
    public TMP_InputField inputTimeBetween;

    [Header("Tipo de movimiento")]
    public Toggle toggleCommon;
    public Toggle toggleSwap;

    [Header("Referencias")]
    public Button buttonStartGame;
    public Button buttonSalirGame;
    public GameManagerFin gameManager;
    [Header("Texto")]
    private TextMeshProUGUI errorText;
    public void Init(TMP_InputField inputColumnas, TMP_InputField inputTime, TMP_InputField inputTimeBetween, Toggle toggleCommon, Toggle toggleSwap, Button buttonStartGame, Button buttonSalirGame, TextMeshProUGUI errorText, GameManagerFin gameManager)
    {
        this.inputColumnas = inputColumnas;
        this.inputTime = inputTime;
        this.inputTimeBetween = inputTimeBetween;
        this.toggleCommon = toggleCommon;
        this.toggleSwap = toggleSwap;
        this.buttonStartGame = buttonStartGame;
        this.gameManager = gameManager;
        this.errorText = errorText;
        this.buttonSalirGame = buttonSalirGame;
        // Configurar valores por defecto
        buttonStartGame.onClick.AddListener(OnStartGame);
        buttonSalirGame.onClick.AddListener(OnbACKToMainMenu);
        toggleCommon.onValueChanged.AddListener(OnToggleChanged);
        toggleSwap.onValueChanged.AddListener(OnToggleChanged);
    }

    void OnToggleChanged(bool _)
    {
        // Asegurar que sólo uno esté activo
        if (toggleCommon.isOn) toggleSwap.isOn = false;
        if (toggleSwap.isOn) toggleCommon.isOn = false;
    }

    void OnbACKToMainMenu()
    {
        GameFlowManager.Instance.TerminarJuego();  
    }

    void OnStartGame()
    {
        errorText.text = ""; // Limpiar errores anteriores

        bool valid = true;

        // Validar columnas
        if (!int.TryParse(inputColumnas.text, out int columnas) || columnas < 2)
        {
            errorText.text += "🟥 Ingresá un número de columnas válido (>= 2).\n";
            valid = false;
        }

        // Validar tiempo
        if (!float.TryParse(inputTime.text, out float time) || time <= 0)
        {
            errorText.text += "🟥 Tiempo de memorización debe ser > 0.\n";
            valid = false;
        }

        // Validar tiempo entre fases
        if (!float.TryParse(inputTimeBetween.text, out float timeBetween) || timeBetween <= 0)
        {
            errorText.text += "🟥 Tiempo entre fases debe ser > 0.\n";
            valid = false;
        }

        // Validar que se haya elegido un tipo de movimiento
        if (!toggleCommon.isOn && !toggleSwap.isOn)
        {
            errorText.text += "🟥 Elegí un tipo de movimiento.\n";
            valid = false;
        }

        if (!valid)
        {
            Debug.LogWarning("Faltan o son inválidos algunos campos.");
            return;
        }

        // Estrategia
        IMovementStrategy strategy = toggleSwap.isOn
            ? new SwapMovementStrategy()
            : new CommonMovementStrategy();

        //gameManager.InitRoom(columnas, time, timeBetween, strategy);
    }
}

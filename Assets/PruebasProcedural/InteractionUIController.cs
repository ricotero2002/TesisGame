using System;
using UnityEngine;
using UnityEngine.UI;

public class InteractionUIController : MonoBehaviour
{
    public static InteractionUIController Instance;
    [SerializeField] private GameObject panelPrefabEditor;      // Panel raíz del canvas
    private GameObject panelPrefab;
    private Button yesButton; //sacar de panelPrefab
    private Button noButton;//sacar de panelPrefab
    private TileController currentTile;
    [SerializeField] private GameObject pruebaEfecto;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);


        panelPrefab = Instantiate(panelPrefabEditor);
        yesButton = panelPrefab.transform.Find("Panel/YesButton").GetComponent<Button>();
        noButton = panelPrefab.transform.Find("Panel/NoButton").GetComponent<Button>();

        panelPrefab.SetActive(false);
        yesButton.onClick.AddListener(() => OnAnswer(true));
        noButton.onClick.AddListener(() => OnAnswer(false));
    }
    /// <summary>
    /// Muestra el UI para la tile seleccionada.
    /// </summary>
    public void Show(TileController tile)
    {
        currentTile = tile;
        panelPrefab.SetActive(true);
    }

    private void OnAnswer(bool moved)
    {
        // Registrar en la tile
        currentTile.RegisterResult(moved, GameManagerFin.Instance.GetIndex(), pruebaEfecto);

        panelPrefab.SetActive(false);
        // Notificar al GameManager que puede continuar
        GameManagerFin.Instance.OnUserAnswered();
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI; 

public class MenuPausa : MonoBehaviour
{
    private CanvasGroup canvasGroup;
    private GameObject pauseMenu;  // Panel de pausa
    private Button btnResume;      // Botón de reanudar
    private Button btnMainMenu;    // Botón para ir al menú principal
    private Button btnPause;
    private bool first = true;
    // Start is called before the first frame update
    public void Init()
    {
        pauseMenu = this.gameObject.transform.Find("Panel").gameObject;
        Debug.Log(pauseMenu);
        canvasGroup = pauseMenu.GetComponent<CanvasGroup>();
        Debug.Log(canvasGroup);
        btnPause = this.gameObject.transform.Find("btnPause").GetComponent<Button>();
        btnPause.onClick.AddListener(PauseGame);

        btnResume = pauseMenu.transform.Find("btnResume").GetComponent<Button>();
        btnResume.onClick.AddListener(ResumeGame);
        btnMainMenu = pauseMenu.transform.Find("btnMainMenu").GetComponent<Button>();
        btnMainMenu.onClick.AddListener(ReturnToMainMenu);


    }
    public void Habilitar()
    {
        
        
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        btnPause.gameObject.SetActive(true);
        pauseMenu.SetActive(false);
    }
    public void Desabilitar()
    {
        
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        pauseMenu.SetActive(false);
        btnPause.gameObject.SetActive(false);

    }
    //desabilitarlo

    private void PauseGame()
    {
        pauseMenu.SetActive(true);

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;

        btnPause.gameObject.SetActive(false);
        Time.timeScale = 0f;
    }
    private void ResumeGame()
    {
        pauseMenu.SetActive(false);
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        btnPause.gameObject.SetActive(true);
        Time.timeScale = 1f;
    }
    public void ReturnToMainMenu()
    {
        // Restablece Time.timeScale antes de cambiar de escena
        Time.timeScale = 1f;
        LogManager.Instance.ClearLogs();
        GameManagerFin.Instance.DestruirSala();
        GameFlowManager.Instance.TerminarJuego();
    }

}

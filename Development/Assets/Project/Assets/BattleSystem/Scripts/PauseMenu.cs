using UnityEngine;
using UnityEngine.UI;

public class PauseMenu : MonoBehaviour
{

    [SerializeField] GameObject PauseScreen;
    [SerializeField] private Button resumeButton, exitButton;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Подписываемся на кнопки
        resumeButton.onClick.AddListener(ResumeGame);
        exitButton.onClick.AddListener(ExitGame);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Time.timeScale = Time.timeScale == 0 ? 1 : 0;
            PauseScreen.SetActive(PauseScreen.activeSelf ? false : true);
        }
    }

    void ResumeGame() { Time.timeScale = 1; PauseScreen.SetActive(false); }

    void ExitGame() { Application.Quit(); }
}

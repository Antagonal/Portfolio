using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

public class TacticMode : MonoBehaviour
{
    private Button button;
    private Coroutine timerCoroutine;
    private CanvasGroup canvasGroup;

    [Header("Ссылка на текстовый компонент кнопки")]
    [SerializeField] private TMP_Text buttonText;

    private AllyManager allyManager;

    void Start()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnButtonClick);
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Если текст не назначен в инспекторе, пытаемся найти автоматически
        if (buttonText == null)
        {
            buttonText = GetComponentInChildren<TMP_Text>();
            if (buttonText == null)
            {
                Debug.LogError("На кнопке не найден компонент Text! Перетащите его в поле 'Button Text' в инспекторе.");
            }
        }

        allyManager = FindObjectOfType<AllyManager>();
    }

    void Update()
    {
        bool visible = LocationManager.TacticMode;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;

        if (visible && allyManager != null && buttonText != null)
        {
            buttonText.text = allyManager.IsDeploymentMode ? "Окончить расстановку" : "Ход времени";
        }
    }

    void OnButtonClick()
    {
        if (allyManager != null && allyManager.IsDeploymentMode)
        {
            allyManager.EndDeployment();
            if (buttonText != null)
                buttonText.text = "Ход времени";
        }
        else
        {
            UnityEngine.Debug.Log("TacticMode установлен в false");
            LocationManager.TacticMode = false;

            if (timerCoroutine != null)
                StopCoroutine(timerCoroutine);

            timerCoroutine = StartCoroutine(WaitAndSetTacticMode());
        }
    }

    IEnumerator WaitAndSetTacticMode()
    {
        PlayerAI player = FindObjectOfType<PlayerAI>();
        float duration = player != null ? player.realTimeDuration : 5f;
        UnityEngine.Debug.Log($"Таймер запущен на {duration} сек");

        yield return new WaitForSeconds(duration);

        if (!LocationManager.Instance.AllEnemiesDefeated)
            LocationManager.TacticMode = true;

        timerCoroutine = null;
    }
}
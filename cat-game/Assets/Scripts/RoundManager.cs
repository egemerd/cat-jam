using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 2-dakika round yöneticisi. Süreyi takip eder, max combo'yu kaydeder,
/// süre bitince game over panelini açar ve high score'ları PlayerPrefs'e
/// kalıcı olarak yazar.
///
/// Setup:
///  1. Sahnede boş bir GameObject oluştur, adını "RoundManager" yap.
///  2. Bu component'i ekle.
///  3. Game Over Panel slot'una kapalı duran UI panelini sürükle.
///  4. (Opsiyonel) Timer Text slot'una HUD'daki geri sayım TMP_Text'ini sürükle.
///  5. Main Menu Scene Name'e ana menü sahnesinin adını yaz (Build Settings'de olmalı).
/// </summary>
public class RoundManager : MonoBehaviour
{
    public static RoundManager Instance { get; private set; }

    [Header("Round")]
    [Tooltip("Round süresi saniye. 120 = 2 dakika.")]
    public float roundDuration = 120f;
    [Tooltip("Scene açılır açılmaz round başlasın mı?")]
    public bool autoStart = true;

    [Header("End screen")]
    [Tooltip("Süre bitince aktif edilecek panel. Başta kapalı olsun.")]
    public GameObject gameOverPanel;
    [Tooltip("Round bitince timeScale 0 olsun mu (gameplay dursun mu)?")]
    public bool pauseOnEnd = true;

    [Header("Timer HUD (optional)")]
    [Tooltip("Süreyi MM:SS olarak gösteren TMP_Text. Boş bırakılabilir.")]
    public TMP_Text timerText;
    public string timerFormat = "{0:00}:{1:00}";

    [Header("Scene names")]
    [Tooltip("Ana menü sahnesinin adı. Build Settings → Scenes In Build listesinde olmalı.")]
    public string mainMenuSceneName = "MainMenu";

    [Header("Events")]
    public UnityEvent OnRoundStarted;
    public UnityEvent OnRoundEnded;

    // Runtime state
    public float TimeRemaining { get; private set; }
    public bool RoundActive { get; private set; }
    public float MaxComboThisRound { get; private set; }
    public int FinalScore { get; private set; }
    public bool NewHighScore { get; private set; }
    public bool NewHighCombo { get; private set; }

    // PlayerPrefs keys
    private const string KEY_HIGHSCORE = "HighScore";
    private const string KEY_HIGHCOMBO = "HighCombo";

    public static int HighScore => PlayerPrefs.GetInt(KEY_HIGHSCORE, 0);
    public static float HighCombo => PlayerPrefs.GetFloat(KEY_HIGHCOMBO, 0f);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        TimeRemaining = roundDuration;
        UpdateTimerText();

        if (autoStart) StartRound();
    }

    public void StartRound()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        TimeRemaining = roundDuration;
        MaxComboThisRound = 0f;
        FinalScore = 0;
        NewHighScore = false;
        NewHighCombo = false;
        RoundActive = true;
        Time.timeScale = 1f;
        OnRoundStarted?.Invoke();
    }

    private void Update()
    {
        if (!RoundActive) return;

        // Max combo'yu takip et — her frame'de ComboScoreSystem.CurrentCombo'yu izle.
        if (ComboScoreSystem.Instance != null)
        {
            float c = ComboScoreSystem.Instance.CurrentCombo;
            if (c > MaxComboThisRound) MaxComboThisRound = c;
        }

        TimeRemaining -= Time.deltaTime;
        if (TimeRemaining <= 0f)
        {
            TimeRemaining = 0f;
            UpdateTimerText();
            EndRound();
            return;
        }

        UpdateTimerText();
    }

    private void UpdateTimerText()
    {
        if (timerText == null) return;
        int totalSeconds = Mathf.CeilToInt(TimeRemaining);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        timerText.text = string.Format(timerFormat, minutes, seconds);
    }

    private void EndRound()
    {
        RoundActive = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        // ComboScoreSystem'den final skoru al. Round biterken aktif combo varsa cash-out ettir.
        if (ComboScoreSystem.Instance != null)
        {
            ComboScoreSystem.Instance.ForceEndCombo(); // combo'yu kapat, pending puanları toplama ekle
            FinalScore = ComboScoreSystem.Instance.TotalScore;
        }

        // High score kayıt — ESKI değerleri önce okuyup karşılaştır ki "new record" badge'leri doğru olsun.
        int prevHighScore = HighScore;
        float prevHighCombo = HighCombo;

        NewHighScore = FinalScore > prevHighScore;
        NewHighCombo = MaxComboThisRound > prevHighCombo;

        if (NewHighScore) PlayerPrefs.SetInt(KEY_HIGHSCORE, FinalScore);
        if (NewHighCombo) PlayerPrefs.SetFloat(KEY_HIGHCOMBO, MaxComboThisRound);
        PlayerPrefs.Save();

        if (pauseOnEnd) Time.timeScale = 0f;
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        OnRoundEnded?.Invoke();
    }

    /// <summary>Panel butonlarının çağırdığı metot — ana menüye dön.</summary>
    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>Panel butonu — mevcut sahneyi yeniden yükle (yeni round başlar).</summary>
    public void RestartRound()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Debug: tüm high score'ları sıfırla.</summary>
    [ContextMenu("Reset High Scores")]
    public void ResetHighScores()
    {
        PlayerPrefs.DeleteKey(KEY_HIGHSCORE);
        PlayerPrefs.DeleteKey(KEY_HIGHCOMBO);
        PlayerPrefs.Save();
        Debug.Log("[RoundManager] High scores reset.");
    }
}
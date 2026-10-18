using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Round biten UI paneli. RoundManager.gameOverPanel'e bağlanacak GameObject'in
/// üstünde bu component olmalı. OnEnable'da yazıları doldurur, butonları bağlar.
///
/// Panel yapısı (önerilen):
///   GameOverPanel (bu script burada)
///     ├─ Title ("TIME'S UP!")
///     ├─ Score grubu
///     │    ├─ FinalScoreText    ("SCORE: 12,345")
///     │    └─ MaxComboText      ("MAX COMBO: x4.2")
///     ├─ High grubu
///     │    ├─ HighScoreText     ("HIGH SCORE: 25,000")
///     │    └─ HighComboText     ("HIGH COMBO: x5.8")
///     ├─ NewHighScoreBadge      ("NEW!" sticker — optional)
///     ├─ NewHighComboBadge      ("NEW!" sticker — optional)
///     ├─ MainMenuButton
///     └─ RestartButton          (optional)
///
/// Her text ve buton slot'u opsiyonel — kullanmadığın alanı boş bırakabilirsin.
/// </summary>
public class GameOverPanel : MonoBehaviour
{
    [Header("Current round")]
    public TMP_Text finalScoreText;
    public TMP_Text maxComboText;
    public string scoreFormat = "SCORE: {0:N0}";
    public string comboFormat = "MAX COMBO: x{0:F1}";

    [Header("All-time best")]
    public TMP_Text highScoreText;
    public TMP_Text highComboText;
    public string highScoreFormat = "HIGH SCORE: {0:N0}";
    public string highComboFormat = "HIGH COMBO: x{0:F1}";

    [Header("New record badges (optional)")]
    [Tooltip("Yeni high score kırıldıysa aktif edilir.")]
    public GameObject newHighScoreBadge;
    [Tooltip("Yeni high combo kırıldıysa aktif edilir.")]
    public GameObject newHighComboBadge;

    [Header("Buttons")]
    public Button mainMenuButton;
    public Button restartButton;

    private void OnEnable()
    {
        PopulateTexts();

        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.RemoveListener(OnMainMenu);
            mainMenuButton.onClick.AddListener(OnMainMenu);
        }
        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(OnRestart);
            restartButton.onClick.AddListener(OnRestart);
        }
    }

    private void PopulateTexts()
    {
        var r = RoundManager.Instance;
        int score = r != null ? r.FinalScore : 0;
        float maxCombo = r != null ? r.MaxComboThisRound : 0f;
        bool newScore = r != null && r.NewHighScore;
        bool newCombo = r != null && r.NewHighCombo;

        if (finalScoreText != null) finalScoreText.text = string.Format(scoreFormat, score);
        if (maxComboText != null) maxComboText.text = string.Format(comboFormat, maxCombo);
        if (highScoreText != null) highScoreText.text = string.Format(highScoreFormat, RoundManager.HighScore);
        if (highComboText != null) highComboText.text = string.Format(highComboFormat, RoundManager.HighCombo);

        if (newHighScoreBadge != null) newHighScoreBadge.SetActive(newScore);
        if (newHighComboBadge != null) newHighComboBadge.SetActive(newCombo);
    }

    private void OnMainMenu()
    {
        if (RoundManager.Instance != null) RoundManager.Instance.GoToMainMenu();
    }

    private void OnRestart()
    {
        if (RoundManager.Instance != null) RoundManager.Instance.RestartRound();
    }
}
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using DG.Tweening;

/// <summary>
/// Minimal HUD — only the displays you asked for:
///
///   1. TRICK + POINTS       e.g. "BAT!" / "+50"
///      The last trick's name on top, its base points underneath.
///
///   2. SCORE CALCULATOR     e.g. "1000 x 1.9"
///      Running raw-base this combo, times the current multiplier.
///
///   3. LAST CASH-OUT        e.g. "+1900"
///      The amount from the most recent cash-out (combo end or air land).
///
///   4. TOTAL SCORE          e.g. "12,345"
///      Committed total across the whole session.
///
///   5. COMBO COUNTER        e.g. "x1.9"
///      The live multiplier as a standalone number.
///
/// Any slot left empty is skipped — safe to wire a partial HUD.
/// No external dependencies.
/// </summary>
public class HudController : MonoBehaviour
{
    [Header("1. Trick label + its points")]
    [Tooltip("Big label — 'BAT!', 'WALL BOUNCE', etc.")]
    public TMP_Text trickText;
    [Tooltip("Small line under the label — base points for that trick ('+50').")]
    public TMP_Text trickPointsText;
    [Tooltip("Group that holds both texts. Hidden when no combo is active.")]
    public GameObject trickGroup;

    [Header("2. Score calculator ((air + static) x multiplier)")]
    [Tooltip("Single text line showing air time points, static trick points, and the multiplier. Format is HARDCODED in code so an old inspector value can't shadow it.")]
    public TMP_Text calcText;

    [Header("3. Last cash-out")]
    public TMP_Text lastCashOutText;
    public string lastCashOutFormat = "+{0:N0}";
    [Tooltip("Seconds the last-cash-out text stays visible. 0 = permanent.")]
    public float lastCashOutLingerTime = 3f;

    [Header("4. Total score")]
    public TMP_Text totalScoreText;
    public string totalScoreFormat = "{0:N0}";
    [Tooltip("How many points per second the DISPLAYED total races toward the real total. Big gaps race faster than this (we add 4x the gap on top). Higher = snappier, lower = longer dramatic tick-up.")]
    public float totalCatchUpRate = 1500f;

    [Header("5. Combo counter")]
    public TMP_Text comboCounterText;
    public string comboCounterFormat = "x{0:F1}";

    [Header("6. Goal count (optional)")]
    [Tooltip("Shows how many balls have gone in the goal this session.")]
    public TMP_Text goalCountText;
    public string goalCountFormat = "GOALS: {0}";

    [Header("7. Ring count (optional)")]
    [Tooltip("Shows how many perfect ring passes the cat has done this session.")]
    public TMP_Text ringCountText;
    public string ringCountFormat = "RINGS: {0}";

    [Header("8. Combo timer bar")]
    [Tooltip("Image with 'Image Type: Filled' + 'Fill Method: Horizontal' (or Vertical/Radial).")]
    public Image comboBarFill;
    [Tooltip("Bar'ı tutan group (optional). Combo aktifken aç/kapat, boşken gizle.")]
    public GameObject comboBarGroup;
    [Tooltip("Combo yokken bar'ı tamamen boş mu göstersin (true) yoksa gizlesin mi (false).")]
    public bool keepBarVisibleWhenInactive = false;

    [Header("Punch animations (DOTween)")]
    [Tooltip("Her punch'ta bu kadar fazlalık scale uygulanır. 0.3 = +%30 anlık büyüme.")]
    public float trickPunchStrength = 0.35f;
    public float trickPunchDuration = 0.25f;
    [Tooltip("Cash-out ve total score daha hype — biraz daha güçlü punch.")]
    public float cashOutPunchStrength = 0.45f;
    public float cashOutPunchDuration = 0.30f;
    [Tooltip("Combo bump'ta multiplier text'inin punch'u.")]
    public float comboPunchStrength = 0.3f;
    public float comboPunchDuration = 0.22f;
    [Tooltip("Punch'ın kaç titreşim yapacağı. 1 = tek büyüme-dönme, 3-5 = elastik salınım.")]
    public int punchVibrato = 3;
    [Tooltip("Punch sonrası yay esnekliği (0 = direkt kapanır, 1 = epey elastik salınır).")]
    [Range(0f, 1f)] public float punchElasticity = 0.5f;

    // --- internal state ---
    private int _rawPendingBase;      // running sum of BASE (un-multiplied) TRICK points this combo
    private int _lastTrickBase;       // base points of the most recent trick (for the "+50" line)
    private float _displayedTotal;    // float version of TotalScore that lerps toward the real value
    private Coroutine _cashOutLingerCo;

    private void Start()
    {
        if (trickGroup != null) trickGroup.SetActive(false);
        if (calcText != null) calcText.text = "";
        if (lastCashOutText != null) lastCashOutText.text = "";
        if (comboCounterText != null) comboCounterText.text = "";

        // Seed displayed total so the counter doesn't rush up from 0 on scene start.
        if (ComboScoreSystem.Instance != null)
            _displayedTotal = ComboScoreSystem.Instance.TotalScore;
        if (totalScoreText != null)
            totalScoreText.text = string.Format(totalScoreFormat, Mathf.FloorToInt(_displayedTotal));
    }

    // ===== event subscription =====

    private void OnEnable()
    {
        var s = ComboScoreSystem.Instance;
        if (s == null) return;

        s.OnTrickName.AddListener(HandleTrickName);
        s.OnScoreCommitted.AddListener(HandleScoreCommitted);
        s.OnComboEnded.AddListener(HandleComboEnded);
    }

    private void OnDisable()
    {
        var s = ComboScoreSystem.Instance;
        if (s == null) return;

        s.OnTrickName.RemoveListener(HandleTrickName);
        s.OnScoreCommitted.RemoveListener(HandleScoreCommitted);
        s.OnComboEnded.RemoveListener(HandleComboEnded);
    }

    // ===== per-frame polling =====

    private void Update()
    {
        var s = ComboScoreSystem.Instance;
        if (s == null) return;

        bool comboActive = s.ComboActive;

        // Trick label only while a combo is active.
        if (trickGroup != null && trickGroup.activeSelf != comboActive)
            trickGroup.SetActive(comboActive);

        // Score calculator: (air + static) × multiplier — shown as two separate
        // numbers in parens, NEVER as a precomputed sum. Hardcoded format so an
        // old serialized inspector string can't shadow this.
        if (calcText != null)
        {
            if (comboActive)
            {
                int airPart = Mathf.FloorToInt(s.AirBuffer);
                int staticPart = _rawPendingBase;
                float mult = s.CurrentCombo;
                calcText.text = "(" + airPart + " + " + staticPart + ") x " + mult.ToString("F1");
            }
            else
            {
                calcText.text = "";
            }
        }

        // Combo counter standalone.
        if (comboCounterText != null)
            comboCounterText.text = comboActive ? string.Format(comboCounterFormat, s.CurrentCombo) : "";

        // Goal / ring counters — update every frame, cheap.
        if (goalCountText != null)
            goalCountText.text = string.Format(goalCountFormat, s.GoalsScored);
        if (ringCountText != null)
            ringCountText.text = string.Format(ringCountFormat, s.PerfectRings);

        // Combo timer bar — combo aktifken dolu, bitince boş. Her tick resetlenince
        // tekrar dolar (çünkü ComboTimeRemaining her trick'te comboWindow'a atanıyor).
        bool showBar = comboActive || keepBarVisibleWhenInactive;
        if (comboBarGroup != null && comboBarGroup.activeSelf != showBar)
            comboBarGroup.SetActive(showBar);
        if (comboBarFill != null)
        {
            comboBarFill.fillAmount = comboActive
                ? Mathf.Clamp01(s.ComboTimeRemaining / s.ComboWindowDuration)
                : 0f;
        }

        // Total score — smooth catch-up so cash-outs tick visibly (100 -> 500) instead of snapping.
        // Big gaps race faster than small ones so a huge payout doesn't take forever to arrive.
        float target = s.TotalScore;
        float gap = target - _displayedTotal;
        float step = Mathf.Max(totalCatchUpRate, Mathf.Abs(gap) * 4f) * Time.deltaTime;
        _displayedTotal = Mathf.MoveTowards(_displayedTotal, target, step);
        if (totalScoreText != null)
            totalScoreText.text = string.Format(totalScoreFormat, Mathf.FloorToInt(_displayedTotal));
    }

    // ===== event handlers =====

    /// <summary>
    /// Fired right after ComboScoreSystem sets LastTrickBasePoints, so we read the
    /// base amount from there. One-arg event avoids UnityEvent&lt;string,int&gt;
    /// serialization quirks across Unity versions.
    /// </summary>
    private void HandleTrickName(string label)
    {
        // Group görünür hale gelsin, sonra punch at — kapalı GameObject'te punch görünmez.
        if (trickGroup != null && !trickGroup.activeSelf) trickGroup.SetActive(true);

        if (trickText != null) trickText.text = label;

        int basePoints = ComboScoreSystem.Instance != null
            ? ComboScoreSystem.Instance.LastTrickBasePoints
            : 0;

        if (basePoints > 0)
        {
            _lastTrickBase = basePoints;
            _rawPendingBase += basePoints;
            if (trickPointsText != null) trickPointsText.text = $"+{basePoints}";
        }
        else if (trickPointsText != null)
        {
            trickPointsText.text = "";
        }

        // Punch animations
        Punch(trickText, trickPunchStrength, trickPunchDuration);
        if (basePoints > 0) Punch(trickPointsText, trickPunchStrength * 0.7f, trickPunchDuration);
        Punch(comboCounterText, comboPunchStrength, comboPunchDuration);
        Punch(calcText, comboPunchStrength * 0.6f, comboPunchDuration);
    }

    private void HandleScoreCommitted(int amount)
    {
        if (lastCashOutText != null)
        {
            lastCashOutText.text = string.Format(lastCashOutFormat, amount);
            if (lastCashOutLingerTime > 0f)
            {
                if (_cashOutLingerCo != null) StopCoroutine(_cashOutLingerCo);
                _cashOutLingerCo = StartCoroutine(ClearAfter(lastCashOutText, lastCashOutLingerTime));
            }
        }

        // Punch — hem kazanılan miktar hem toplam skor hype olsun.
        Punch(lastCashOutText, cashOutPunchStrength, cashOutPunchDuration);
        Punch(totalScoreText, cashOutPunchStrength * 0.7f, cashOutPunchDuration);
    }

    private void HandleComboEnded()
    {
        _rawPendingBase = 0;
        _lastTrickBase = 0;
        if (calcText != null) calcText.text = "";
        if (trickPointsText != null) trickPointsText.text = "";
    }

    private static IEnumerator ClearAfter(TMP_Text t, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (t != null) t.text = "";
    }

    /// <summary>
    /// DOTween punch helper. Null-safe, reentrant-safe.
    /// Önceki tween'i öldürür, scale'i resetler, yeni punch başlatır.
    /// Böylece hızlı art arda punch'lar stacklenip scale'i şişirmez.
    /// </summary>
    private void Punch(TMP_Text t, float strength, float duration)
    {
        if (t == null) return;
        var tr = t.transform;
        tr.DOKill();
        tr.localScale = Vector3.one;
        tr.DOPunchScale(Vector3.one * strength, duration, punchVibrato, punchElasticity);
    }
}
using UnityEngine;
using UnityEngine.Events;
using StarterAssets;

/// <summary>
/// Central combo + scoring system.
///
/// SCORING EVENTS (call from anywhere):
///   ComboScoreSystem.Instance.OnCatHitBall();   // cat ↔ ball collision
///   ComboScoreSystem.Instance.OnBallHitWall();  // ball ↔ wall collision
///
/// COMBO:
///   First event   -> combo = 1.0x
///   Each event    -> combo += 0.1x
///   Combo timer   -> resets to comboWindow on every event, counts down otherwise
///   Timer expires -> cash out (PendingScore + AirBuffer -> TotalScore, reset)
///
/// AIR POINTS (airborne):
///   Fill AirBuffer at airPointsPerSecond, multiplied by combo if active.
///   Grounded + no combo -> AirBuffer commits to Total, resets.
///   Grounded + combo    -> AirBuffer HELD (frozen, visible in UI).
///   Airborne again      -> AirBuffer resumes from held value.
///   Combo ends          -> AirBuffer + PendingScore both commit to Total.
///
/// UI binding:
///   Read TotalScore, PendingScore, AirBuffer, CurrentCombo, ComboTimeRemaining
///   from any UI script. Subscribe to OnScoreCommitted / OnComboEnded /
///   OnComboChanged for one-shot events (flash, pop, etc.) instead of polling.
///
/// Setup:
///   - Add this component to any GameObject in the scene (cat, GameManager, whatever).
///   - Assign 'controller' to the cat's ThirdPersonController so this script
///     can read Grounded. Auto-filled on the same object.
/// </summary>
public class ComboScoreSystem : MonoBehaviour
{
    public static ComboScoreSystem Instance { get; private set; }

    [Header("References")]
    [Tooltip("Cat controller — used to read Grounded for air-time scoring.")]
    public ThirdPersonController controller;

    [Header("Score values")]
    [Tooltip("Base points awarded when the cat hits a ball.")]
    public int ballHitPoints = 50;
    [Tooltip("Base points awarded when a ball reflects off a wall.")]
    public int wallBouncePoints = 30;
    [Tooltip("Points per second gained while airborne. Multiplied by combo when combo is active.")]
    public float airPointsPerSecond = 10f;

    [Header("Combo")]
    [Tooltip("Combo value given on the FIRST scoring event (when there is no active combo).")]
    public float comboStart = 1.0f;
    [Tooltip("Combo value added by each subsequent scoring event.")]
    public float comboIncrement = 0.1f;
    [Tooltip("Seconds the combo stays alive after the LAST scoring event. Any new event resets this.")]
    public float comboWindow = 2.5f;

    [Header("Debug")]
    [Tooltip("Print verbose logs to Console for every scoring event, combo change, and cash-out. Turn off when you're confident the system works.")]
    public bool debugLogs = true;

    // --- state (read these from UI) ---
    public int TotalScore { get; private set; }
    public int PendingScore { get; private set; }       // committed to total only on combo end
    public float AirBuffer { get; private set; }        // committed to pending/total depending on combo
    public float CurrentCombo { get; private set; }     // 0 when no combo, >= comboStart when active
    public float ComboTimeRemaining { get; private set; }
    public bool ComboActive => CurrentCombo >= comboStart;
    public float ComboWindowDuration => comboWindow;    // for UI progress bars

    // --- events (hook up in inspector OR via code for UI animations) ---
    [Header("Events (optional, for UI feedback)")]
    public UnityEvent<int> OnScoreCommitted;   // fires with the amount committed to TotalScore
    public UnityEvent<float> OnComboChanged;   // fires with the new combo value (0 means combo just ended)
    public UnityEvent OnComboEnded;            // fires on cash-out

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (controller == null) controller = GetComponent<ThirdPersonController>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ---------- Public API ----------

    /// <summary>Cat hit a ball. Call once per actual hit (not every frame of contact).</summary>
    public void OnCatHitBall()
    {
        if (debugLogs) Debug.Log($"<color=#8ff>[Combo] CAT HIT BALL</color> (+{ballHitPoints} base)");
        AddScoreEvent(ballHitPoints);
    }

    /// <summary>A ball reflected off a wall. Call once per wall bounce.</summary>
    public void OnBallHitWall()
    {
        if (debugLogs) Debug.Log($"<color=#fc8>[Combo] BALL HIT WALL</color> (+{wallBouncePoints} base)");
        AddScoreEvent(wallBouncePoints);
    }

    /// <summary>Manually end the combo now (e.g. on cat death, round end). Commits pending + air buffer.</summary>
    public void ForceEndCombo()
    {
        if (ComboActive) EndCombo();
    }

    // ---------- Internals ----------

    private void AddScoreEvent(int basePoints)
    {
        // First event starts combo at comboStart; every event after that bumps it.
        float before = CurrentCombo;
        CurrentCombo = ComboActive ? CurrentCombo + comboIncrement : comboStart;
        ComboTimeRemaining = comboWindow;
        int added = Mathf.RoundToInt(basePoints * CurrentCombo);
        PendingScore += added;

        if (debugLogs)
        {
            string tag = before < comboStart ? "STARTED" : "BUMPED";
            Debug.Log($"<color=#ff8>[Combo] {tag}</color>  combo: {before:F1}x -> {CurrentCombo:F1}x | +{added} pending | pending total: {PendingScore} | window reset to {comboWindow:F1}s");
        }

        OnComboChanged?.Invoke(CurrentCombo);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        bool airborne = controller != null && !controller.Grounded;

        // --- Air points ---
        if (airborne)
        {
            float mult = ComboActive ? CurrentCombo : 1f;
            AirBuffer += airPointsPerSecond * mult * dt;
        }
        else if (!ComboActive && AirBuffer > 0f)
        {
            // Landed with no combo — commit air buffer straight to Total.
            int airAmount = Mathf.RoundToInt(AirBuffer);
            if (debugLogs) Debug.Log($"<color=#8f8>[Combo] AIR COMMIT (no combo)</color> landed with +{airAmount} air points -> Total");
            CommitToTotal(airAmount);
            AirBuffer = 0f;
        }
        // else: grounded with combo active — HOLD the air buffer, don't commit, don't fill.

        // --- Combo timer ---
        if (ComboActive)
        {
            ComboTimeRemaining -= dt;
            if (ComboTimeRemaining <= 0f) EndCombo();
        }
    }

    private void EndCombo()
    {
        int airPart = Mathf.RoundToInt(AirBuffer);
        int payout = PendingScore + airPart;

        if (debugLogs)
            Debug.Log($"<color=#f8f>[Combo] CASH OUT</color>  final combo: {CurrentCombo:F1}x | pending: {PendingScore} + air: {airPart} = +{payout} -> Total");

        CommitToTotal(payout);

        PendingScore = 0;
        AirBuffer = 0f;
        CurrentCombo = 0f;
        ComboTimeRemaining = 0f;

        OnComboChanged?.Invoke(0f);
        OnComboEnded?.Invoke();
    }

    private void CommitToTotal(int amount)
    {
        if (amount == 0) return;
        TotalScore += amount;
        if (debugLogs) Debug.Log($"<color=#afa>[Combo] TOTAL SCORE</color> +{amount} -> {TotalScore}");
        OnScoreCommitted?.Invoke(amount);
    }
}
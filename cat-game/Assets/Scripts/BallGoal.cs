using UnityEngine;

/// <summary>
/// Trigger that catches balls. When a BounceBall enters, awards:
///   baseGoalScore * max(1, ball.ComboCount) * comboMultiplier
///
/// Score goes through ComboScoreSystem so the trick label, calculator and
/// total all react naturally.
///
/// Setup:
///  - Put this on a GameObject with a Collider marked IS TRIGGER.
///  - Make sure the ball's layer is NOT in the ball's own bounceMask
///    (otherwise the ball would also treat the goal as a wall).
/// </summary>
[RequireComponent(typeof(Collider))]
public class BallGoal : MonoBehaviour
{
    [Header("Score")]
    [Tooltip("Base score before the combo multiplier.")]
    public int baseGoalScore = 500;
    [Tooltip("Multiplier applied to (base * comboCount). 0.5 = half of combo-scaled base.")]
    public float comboMultiplier = 0.5f;

    [Header("Behavior")]
    [Tooltip("Destroy the ball after it scores? If false, the ball stays and keeps bouncing.")]
    public bool destroyBallOnGoal = true;
    [Tooltip("Optional VFX prefab spawned at the ball's entry point.")]
    public ParticleSystem goalVfx;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        BounceBall ball = other.GetComponent<BounceBall>();
        if (ball == null) 
            return;
            Debug.Log($"BallGoal: {other.name} entered goal {name} with combo {ball.ComboCount}");

        // Floor the combo at 1 so a dead ball doesn't score 0.
        int effectiveCombo = Mathf.Max(1, ball.ComboCount);
        int points = Mathf.RoundToInt(baseGoalScore * effectiveCombo * comboMultiplier);

        ComboScoreSystem.Instance?.OnBallInGoal(points);

        if (goalVfx != null) goalVfx.Play();
        if (destroyBallOnGoal) Destroy(ball.gameObject);
    }
}
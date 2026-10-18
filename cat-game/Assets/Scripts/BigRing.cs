using UnityEngine;

/// <summary>
/// Big ring the cat can leap through. On trigger entry, awards a fixed score.
/// With requireAirborne enabled, only an airborne pass counts — grounded
/// walks through just slide by without scoring, so "perfectly" means
/// jumping through it, not strolling.
///
/// Setup:
///  - Put this on a GameObject with a Collider marked IS TRIGGER. A thin
///    disc collider aligned with the ring's hole works best.
///  - Make sure the cat's CharacterController is detectable (either tag
///    the cat 'Player' or let the auto-detect via GetComponentInParent handle it).
/// </summary>
[RequireComponent(typeof(Collider))]
public class BigRing : MonoBehaviour
{
    [Header("Score")]
    [Tooltip("Flat points awarded for a successful pass.")]
    public int ringScore = 300;
    [Tooltip("Label shown in the trick UI.")]
    public string trickName = "PERFECT RING!";

    [Header("Rules")]
    [Tooltip("If true, only an airborne pass counts (cat must be jumping / falling, not grounded).")]
    public bool requireAirborne = true;
    [Tooltip("Minimum seconds between consecutive scores on the same ring. Stops rapid re-triggers.")]
    public float reTriggerCooldown = 1.5f;

    [Header("Visual")]
    [Tooltip("Optional VFX prefab spawned at the ring center on a successful pass.")]
    public ParticleSystem ringVfx;

    private float _lastScoreTime = -999f;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Accept the cat (StarterAssets uses CharacterController). Falls back to
        // tag check so you can tag anything else "Player" if needed.
        CharacterController cc = other.GetComponentInParent<CharacterController>();
        if (cc == null && !other.CompareTag("Player")) return;

        if (Time.time - _lastScoreTime < reTriggerCooldown) return;

        // "Perfectly" = airborne. Grounded walks-through don't count.
        if (requireAirborne)
        {
            var controller = other.GetComponentInParent<StarterAssets.ThirdPersonController>();
            if (controller != null && controller.Grounded) return;
        }

        _lastScoreTime = Time.time;

        ComboScoreSystem.Instance?.OnCatInRing(ringScore, trickName);

        if (ringVfx != null) ringVfx.Play();
    }
}
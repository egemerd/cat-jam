using UnityEngine;
using System.Collections;

/// <summary>
/// A ball the cat knocks around.
///
/// KINEMATIC RIGIDBODY, MANUAL MOTION:
///   We move the transform ourselves. The engine never applies gravity, drag
///   or collision response to this ball — all of that is in this script, so
///   the FEEL (hit-stop, hard kick, burst, decay, reflect angles) is 100%
///   under our control.
///
/// WHY IT NO LONGER FALLS THROUGH THE GROUND:
///   Two things prevented tunneling in the previous version from working:
///     1) the old code threw away SphereCast hits with distance ≈ 0, but
///        that is exactly the hit you get when the ball is already touching
///        the floor — so the ball happily moved straight through it.
///     2) once gravity nudged the sphere even slightly into a surface, there
///        was nothing to push it back out.
///   Fix: every frame we FIRST call ResolveOverlaps() (OverlapSphere +
///   Physics.ComputePenetration) which pops the ball back out of any surface
///   it has clipped into AND reflects velocity if we were pressing in. THEN
///   we do the predictive SphereCast move, now accepting distance-0 hits as
///   real contacts instead of filtering them out.
///
/// BOUNCE FEEL:
///   Walls use wallBounciness and notify CatBallInteraction (chase boost).
///   Floors/ramps use groundBounciness, and a hit softer than
///   groundBounceThreshold cancels the into-surface velocity instead of
///   bouncing — so the ball slides to a stop instead of jittering forever.
///
/// REQUIRES: a Rigidbody (will be forced kinematic) AND a Collider (any
/// shape; a Sphere matched to castRadius is cleanest).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class BounceBall : MonoBehaviour
{
    [Header("Bounce direction (relative to the cat's forward)")]
    [Tooltip("Random left/right spread in degrees around the cat's facing. Chosen randomly in [-spread, +spread].")]
    public float horizontalSpread = 25f;
    [Tooltip("Random UP angle range in degrees. 0 = flat, 90 = straight up.")]
    public float minUpAngle = 10f;
    public float maxUpAngle = 35f;

    [Header("Launch feel (burst + decay)")]
    [Tooltip("Tiny freeze before launching - the 'hit stop'.")]
    public float hitStopDuration = 0.06f;
    [Tooltip("Hard initial speed at the instant of launch.")]
    public float launchSpeed = 14f;
    [Tooltip("Duration of the punchy burst phase, where drag is reduced.")]
    public float burstDuration = 0.18f;
    [Tooltip("Drag multiplier during the burst (0.2 = only 20% of normal drag = punchy).")]
    [Range(0f, 1f)] public float burstDragMultiplier = 0.2f;
    [Tooltip("Horizontal air drag after the burst (exponential, framerate-independent).")]
    public float drag = 1.2f;
    [Tooltip("Manual gravity. More negative = snappier arcs. Engine default is -9.81.")]
    public float gravity = -22f;
    [Tooltip("Safety cap: a single bounce routine can never run longer than this.")]
    public float maxDuration = 8f;

    [Header("Surfaces & reflecting")]
    [Tooltip("EVERYTHING the ball reflects off: walls AND floor/ground. EXCLUDE the Ball layer and the Cat layer.")]
    public LayerMask bounceMask = ~0;
    [Tooltip("The subset of bounceMask that counts as a WALL — triggers the chase boost and uses Wall Bounciness. Floor/ramp layers should NOT be in here.")]
    public LayerMask wallMask = 0;
    [Tooltip("Roughly the ball's radius. Used by the predictive SphereCast and overlap recovery.")]
    public float castRadius = 0.3f;
    [Tooltip("Speed kept after a WALL bounce. 1 = lossless, 0.9 = loses 10%.")]
    [Range(0f, 1.2f)] public float wallBounciness = 0.9f;
    [Tooltip("Speed kept after a GROUND bounce. 0.7 gives 3-4 lively hops before settling.")]
    [Range(0f, 1.2f)] public float groundBounciness = 0.7f;
    [Tooltip("Ground hits slower than this won't bounce — the ball slides instead of jittering.")]
    public float groundBounceThreshold = 1.5f;
    [Tooltip("Below this speed while touching the ground, the ball stops.")]
    public float settleSpeed = 0.5f;
    [Tooltip("The ball must stay slow AND on the ground for this long before it stops. Non-zero so one apex frame beside a wall can't freeze the ball mid-air.")]
    public float settleDwellTime = 0.15f;
    [Tooltip("Extra skin between the ball and surfaces after a bounce (prevents immediate re-overlap).")]
    public float surfaceSkin = 0.01f;

    [Header("Cat re-hit")]
    [Tooltip("Shortest time after a launch before the cat can bat the ball again.")]
    public float reHitCooldown = 0.2f;

    // Public state (handy for VFX / trails)
    public Vector3 CurrentVelocity => _velocity;
    public float CurrentSpeed => _velocity.magnitude;
    public bool IsBouncing => _active;

    /// <summary>Wall bounces accumulated since the LAST cat hit. Resets when the cat bats this ball again. Read by CatBallInteraction to scale the chase boost.</summary>
    public int ComboCount { get; private set; }
    /// <summary>Time the ball last reflected off a wall. Use with wallBounceWindow to decide if the ball is still 'hot'.</summary>
    public float LastWallBounceTime { get; private set; } = -999f;

    private Rigidbody _rb;
    private Collider _col;
    private Vector3 _velocity;
    private bool _active;
    private float _lastLaunchTime = -999f;
    private Coroutine _routine;
    private readonly Collider[] _overlapBuf = new Collider[8];
    EnemySquash enemySquash;
    public GameObject bounceVfx;
    public SoundSO bounceSound;
    private Vector3? _velocityOverride;
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        enemySquash = GetComponent<EnemySquash>();  
    }

    /// <summary>Called by the cat (CatBallInteraction) when it runs into this ball.</summary>
    public bool BounceFromCat(Vector3 catForward , Vector3 hitPoint)
    {
        if (_active && Time.time - _lastLaunchTime < reHitCooldown) return false;

        // Cat hit the ball — combo resets. A new chain starts from the next wall bounce.
        ComboCount = 0;
        LastWallBounceTime = -999f;

        Vector3 dir = BuildBounceDirection(catForward);
        enemySquash.TriggerSquash(-dir);
        VfxSpawner.Play(bounceVfx, hitPoint, Quaternion.LookRotation(-dir));
        AudioManager.Instance.Play(bounceSound);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(BallRoutine(dir));
        return true;
    }

    public void Launch(Vector3 velocity)
    {
        ComboCount = 0;
        LastWallBounceTime = -999f;
        _velocityOverride = velocity;
        if (_routine != null) StopCoroutine(_routine);
        Vector3 dir = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : Vector3.down;
        _routine = StartCoroutine(BallRoutine(dir));
    }
    private Vector3 BuildBounceDirection(Vector3 catForward)
    {
        catForward.y = 0f;
        if (catForward.sqrMagnitude < 0.0001f) catForward = Vector3.forward;
        catForward.Normalize();

        float yaw = Random.Range(-horizontalSpread, horizontalSpread);
        Vector3 horizontal = Quaternion.AngleAxis(yaw, Vector3.up) * catForward;

        float pitch = Random.Range(minUpAngle, maxUpAngle) * Mathf.Deg2Rad;
        Vector3 dir = horizontal * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
        return dir.normalized;
    }

    private IEnumerator BallRoutine(Vector3 dir)
    {
        _active = true;
        _lastLaunchTime = Time.time;

        // 1) Hit stop — freeze mid-air briefly.
        _velocity = Vector3.zero;
        yield return new WaitForSeconds(hitStopDuration);

        // 2) Hard initial kick.
        _velocity = _velocityOverride ?? (dir * launchSpeed);
        _velocityOverride = null;

        float total = 0f, burst = 0f;
        float settleAccum = 0f;

        while (true)
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) { yield return null; continue; }

            total += dt;
            if (total >= maxDuration) break;

            // --- Integrate ---
            _velocity.y += gravity * dt;

            // Horizontal drag; burst phase uses much less drag so the kick lands.
            float d = (burst < burstDuration) ? drag * burstDragMultiplier : drag;
            if (burst < burstDuration) burst += dt;
            float decay = Mathf.Exp(-d * dt);
            _velocity.x *= decay;
            _velocity.z *= decay;

            // --- Collide ---
            // Recovery first: pop out of anything we've clipped into, and
            // reflect if we were pressing INTO that surface. This is what
            // stops the ball sinking through the floor over time.
            ResolveOverlaps();

            // Predictive sweep with up to 2 reflects per frame (handles corners).
            MoveWithBounces(dt);

            // --- Settle ---
            // Must stay slow AND actually on ground for settleDwellTime seconds.
            // Single-frame qualification is NOT enough: at the apex of a tiny
            // bounce velocity.y briefly passes through 0, and if a wall is near
            // the ball at that instant the ball used to freeze in mid-air.
            if (_velocity.sqrMagnitude < settleSpeed * settleSpeed && IsOnGround())
            {
                settleAccum += dt;
                if (settleAccum >= settleDwellTime)
                {
                    _velocity = Vector3.zero;
                    break;
                }
            }
            else
            {
                settleAccum = 0f;
            }

            yield return null;
        }

        _velocity = Vector3.zero;
        _active = false;
        _routine = null;
    }

    private void MoveWithBounces(float dt)
    {
        float timeLeft = dt;
        int bouncesLeft = 2;

        while (bouncesLeft-- > 0 && timeLeft > 0.0001f)
        {
            Vector3 step = _velocity * timeLeft;
            float dist = step.magnitude;
            if (dist < 0.0001f) return;

            Vector3 moveDir = step / dist;

            // NOTE: no `hit.distance > 0.0001f` filter anymore. A zero-distance hit
            // is a legitimate contact (we're touching the surface) and MUST be
            // reflected, not skipped. The self-collision case is handled by the
            // attachedRigidbody check and by keeping the ball on its own layer.
            if (Physics.SphereCast(transform.position, castRadius, moveDir, out RaycastHit hit,
                    dist, bounceMask, QueryTriggerInteraction.Ignore)
                && hit.collider.attachedRigidbody != _rb)
            {
                // Travel up to just before contact, then nudge off the surface.
                float safeDist = Mathf.Max(0f, hit.distance - surfaceSkin);
                transform.position += moveDir * safeDist + hit.normal * surfaceSkin;

                HandleBounce(hit);

                // Consume the portion of the step we actually used.
                float usedT = (dist > 0.0001f) ? (safeDist / dist) * timeLeft : 0f;
                timeLeft -= usedT;
            }
            else
            {
                transform.position += step;
                return;
            }
        }
    }

    private void HandleBounce(RaycastHit hit)
    {
        bool isWall = ((1 << hit.collider.gameObject.layer) & wallMask) != 0;

        if (isWall)
        {
            _velocity = Vector3.Reflect(_velocity, hit.normal) * wallBounciness;
            enemySquash.TriggerSquash(-hit.normal);
            ComboCount++;
            LastWallBounceTime = Time.time;
            if (CatBallInteraction.Instance != null)
                CatBallInteraction.Instance.RegisterWallBounce(this);
            ComboScoreSystem.Instance?.OnBallHitWall();
            // >>> wall-hit VFX / sound hook goes here <<<
        }
        else
        {
            // Floor / ramp.
            float approach = Vector3.Dot(_velocity, -hit.normal); // speed heading INTO the surface
            if (approach > groundBounceThreshold)
            {
                _velocity = Vector3.Reflect(_velocity, hit.normal) * groundBounciness;
            }
            else
            {
                // Too slow to bounce — remove just the into-surface component so the ball slides.
                _velocity -= Vector3.Project(_velocity, hit.normal);
            }
        }
    }

    /// <summary>
    /// Any surface we're currently clipping into gets pushed back out with
    /// Physics.ComputePenetration — the correct, generic way to resolve
    /// kinematic overlap. If velocity is driving INTO that surface, we also
    /// reflect it so the ball reads as a bounce rather than a silent stop.
    /// This is what prevents the ball from gradually sinking into the floor.
    /// </summary>
    private void ResolveOverlaps()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, castRadius, _overlapBuf,
                        bounceMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider other = _overlapBuf[i];
            if (other == null || other.attachedRigidbody == _rb) continue;

            if (Physics.ComputePenetration(
                    _col, transform.position, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 pushDir, out float pushDist))
            {
                transform.position += pushDir * (pushDist + surfaceSkin);

                // Only reflect if we were actually pressing into this surface.
                if (Vector3.Dot(_velocity, pushDir) < 0f)
                {
                    bool isWall = ((1 << other.gameObject.layer) & wallMask) != 0;

                    if (isWall)
                    {
                        _velocity = Vector3.Reflect(_velocity, pushDir) * wallBounciness;
                        ComboCount++;
                        LastWallBounceTime = Time.time;
                        if (CatBallInteraction.Instance != null)
                            CatBallInteraction.Instance.RegisterWallBounce(this);
                        ComboScoreSystem.Instance?.OnBallHitWall();
                    }
                    else
                    {
                        // Same sign convention as HandleBounce: positive = heading INTO the surface.
                        float approach = Vector3.Dot(_velocity, -pushDir);
                        if (approach > groundBounceThreshold)
                            _velocity = Vector3.Reflect(_velocity, pushDir) * groundBounciness;
                        else
                            _velocity -= Vector3.Project(_velocity, pushDir);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Narrow downward probe — a tiny sphere inside the ball volume, not a
    /// wide one that could overlap walls to the side. We also require the hit
    /// surface to actually face UP (so walls / ceilings aren't mistaken for
    /// ground) and reject a zero-distance hit (when SphereCast starts inside a
    /// collider it fills the hit normal with -castDirection, so an overlap-at-
    /// origin case would always look like "surface facing up" — that would
    /// re-create the original mid-air freeze bug).
    /// </summary>
    private bool IsOnGround()
    {
        const float probeRadius = 0.05f;
        if (Physics.SphereCast(transform.position, probeRadius, Vector3.down, out RaycastHit hit,
                               castRadius + 0.05f, bounceMask, QueryTriggerInteraction.Ignore))
        {
            return hit.distance > 0f && hit.normal.y > 0.5f;
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, castRadius);
    }
}
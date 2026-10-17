using UnityEngine;
using System.Collections.Generic;
using StarterAssets;
#if CINEMACHINE_PRESENT
using Unity.Cinemachine;
#endif

/// <summary>
/// Goes on the CAT (next to ThirdPersonController).
///
/// Boost model (per the latest design):
///  - Each ball that has bounced off a wall is a "hot ball" and tracks its own
///    combo (bounces since the last cat touch). The ball object owns the combo
///    count; this script just reads it.
///  - Each hot ball has its OWN boost contribution. When the cat is aiming at
///    that ball (via the main camera's forward, i.e. the middle of the screen),
///    that ball's contribution SNAPS to its combo-based target immediately —
///    the "first contact" instant kick you asked for. When the cat stops looking
///    at that ball, its contribution LERPS down to zero on its own timer.
///  - The applied speed boost is the MAX of every ball's current contribution,
///    so if you build a huge combo on one ball you keep that speed for a moment
///    even if you glance at a smaller ball, and glancing at a higher-combo ball
///    instantly upgrades the boost.
///  - FOV follows the applied boost ratio.
///
/// Aim is the main CAMERA'S forward, projected to the ground — in a third-
/// person setup that's "where the middle of my screen is pointing", not the
/// cat's facing (which lags the camera).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CatBallInteraction : MonoBehaviour
{
    public static CatBallInteraction Instance;

    [Header("References")]
    [Tooltip("The StarterAssets controller to apply the boost to. Auto-filled if on the same object.")]
    public ThirdPersonController controller;
    [Tooltip("The StarterAssets input container. Auto-filled if on the same object. Used to gate the boost on movement input.")]
    public StarterAssetsInputs input;
    [Tooltip("The main gameplay camera. Its forward (projected onto the ground) is 'where the middle of the screen is pointing'. Auto-filled from Camera.main.")]
    public Transform aimCamera;

    [Header("Hot ball window")]
    [Tooltip("How long after its last wall bounce a ball stays eligible for granting boost.")]
    public float wallBounceWindow = 1.5f;

    [Header("Aim gate (camera-based)")]
    [Tooltip("Max distance from the CAT to a hot ball that can grant a boost.")]
    public float boostRange = 12f;
    [Tooltip("How much 'in front of the camera' the ball must be. 1 = dead-center of screen, 0.5 ≈ a 60° cone around screen center.")]
    [Range(0f, 1f)] public float lookDot = 0.5f;
    [Tooltip("If true, the player must also be pressing WASD for the boost to engage. If false, just looking at a hot ball is enough.")]
    public bool requireMovementInput = true;

    [Header("Speed boost (scales with combo)")]
    [Tooltip("The exact boost given on the FIRST contact (combo = 1).")]
    public float baseBoost = 1.5f;
    [Tooltip("Extra boost added per additional wall bounce in the combo.")]
    public float bonusPerCombo = 1.2f;
    [Tooltip("Hard cap on a single ball's contribution, no matter how high the combo gets.")]
    public float maxBoost = 6f;
    [Tooltip("How fast a ball's contribution LERPS DOWN when the cat is no longer aiming at it. (Attack is instant — snap-on.)")]
    public float boostDecay = 4f;

    [Header("FOV punch (Cinemachine)")]
    [Tooltip("Drag the active CinemachineCamera here. Leave empty to skip FOV changes.")]
#if CINEMACHINE_PRESENT
    public CinemachineCamera vcam;
#else
    public MonoBehaviour vcam; // placeholder — assign your Cinemachine vcam once CINEMACHINE_PRESENT is defined
#endif
    [Tooltip("The vcam's resting FOV. Captured on Start if left at 0.")]
    public float baseFov = 0f;
    [Tooltip("FOV added when the applied boost is at full strength (= maxBoost).")]
    public float fovAddAtMaxBoost = 12f;
    [Tooltip("How fast FOV responds. Higher = snappier, lower = smoother.")]
    public float fovLerp = 8f;

    [Header("Speed lines")]
    [Tooltip("Particle system that plays while a boost is active. Leave its GameObject active in the scene — this script toggles its EMISSION, not the whole object, so existing particles fade naturally instead of popping.")]
    public ParticleSystem speedLines;
    [Tooltip("Boost level above which speed lines turn on. Prevents micro-boosts and the fading tail from flickering the effect on/off.")]
    public float speedLinesOnThreshold = 0.5f;
    [Tooltip("Boost level below which speed lines turn off. Keep this LOWER than the on threshold — the gap is a hysteresis window that stops flicker when the boost hovers around the trigger.")]
    public float speedLinesOffThreshold = 0.25f;

    private CharacterController _cc;
    private float _currentBoost;
    private float _currentFov;
    private bool _speedLinesOn;    // cached — we only touch the particle system when this CHANGES
    private bool _speedLinesInit;  // one-time setup flag

    public GameObject bounceVfx;
    private bool vfxPlayed = false;
    private class HotBall
    {
        public BounceBall ball;
        public float expire;
        public float contribution; // this ball's own current boost contribution
    }
    private readonly List<HotBall> _hot = new List<HotBall>();

    private void Awake()
    {
        Instance = this;
        _cc = GetComponent<CharacterController>();
        if (controller == null) controller = GetComponent<ThirdPersonController>();
        if (input == null) input = GetComponent<StarterAssetsInputs>();
        if (aimCamera == null && Camera.main != null) aimCamera = Camera.main.transform;
    }

    private void Start()
    {
#if CINEMACHINE_PRESENT
        if (vcam != null && baseFov <= 0f) baseFov = vcam.Lens.FieldOfView;
#else
        if (baseFov <= 0f) baseFov = 60f;
#endif
        _currentFov = baseFov;
    }

    /// <summary>Called by BounceBall when it reflects off a wall.</summary>
    public void RegisterWallBounce(BounceBall ball)
    {
        for (int i = 0; i < _hot.Count; i++)
        {
            if (_hot[i].ball == ball) { _hot[i].expire = Time.time + wallBounceWindow; return; }
        }
        _hot.Add(new HotBall { ball = ball, expire = Time.time + wallBounceWindow, contribution = 0f });
    }

    // Cat ran into something. If it's a ball, bounce it off the cat's forward.
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        BounceBall ball = hit.collider.GetComponentInParent<BounceBall>();
        if(ball != null)
        {  
            ball.BounceFromCat(transform.forward, hit.point);
        }
        vfxPlayed = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // Drop balls that are expired AND fully faded. We keep expired balls
        // around while their contribution is still > 0 so they finish their
        // lerp-down cleanly instead of popping to 0.
        for (int i = _hot.Count - 1; i >= 0; i--)
        {
            HotBall hb = _hot[i];
            bool dead = hb.ball == null || hb.ball.ComboCount <= 0 || Time.time > hb.expire;
            if (dead && hb.contribution <= 0.001f)
                _hot.RemoveAt(i);
        }

        // Where the camera is pointing on the ground plane.
        Vector3 aim = Vector3.zero;
        Vector3 originXZ = Vector3.zero;
        bool hasAim = false;
        if (aimCamera != null)
        {
            aim = aimCamera.forward; aim.y = 0f;
            if (aim.sqrMagnitude > 0.0001f)
            {
                aim.Normalize();
                originXZ = aimCamera.position; originXZ.y = 0f;
                hasAim = true;
            }
        }
        bool hasInput = input != null && input.move.sqrMagnitude > 0.0001f;
        bool canBoost = hasAim && (!requireMovementInput || hasInput);

        Vector3 catXZ = new Vector3(transform.position.x, 0f, transform.position.z);

        // Per-ball: snap on look, lerp down otherwise. Applied boost = max of contributions.
        float appliedBoost = 0f;
        for (int i = 0; i < _hot.Count; i++)
        {
            HotBall hb = _hot[i];
            if (hb.ball == null) continue;

            bool lookedAt = false;
            if (canBoost && hb.ball.ComboCount > 0 && Time.time <= hb.expire)
                lookedAt = IsAimingAt(hb.ball, catXZ, aim, originXZ);

            if (lookedAt)
            {
                // SNAP. The first-contact instant kick, scaled by this ball's combo.
                float target = Mathf.Min(maxBoost, baseBoost + bonusPerCombo * (hb.ball.ComboCount - 1));
                hb.contribution = target;
            }
            else
            {
                // LERP down. Independent per ball, so you keep a decaying tail
                // when you glance away from a big-combo ball.
                hb.contribution = Mathf.MoveTowards(hb.contribution, 0f, boostDecay * dt);
            }

            if (hb.contribution > appliedBoost) appliedBoost = hb.contribution;
        }

        _currentBoost = appliedBoost;
        if (controller != null) controller.ExternalSpeedBoost = _currentBoost;

        UpdateSpeedLines(_currentBoost);

        // FOV follows the applied boost ratio.
        float targetFov = baseFov + (_currentBoost / Mathf.Max(0.01f, maxBoost)) * fovAddAtMaxBoost;
        _currentFov = Mathf.Lerp(_currentFov, targetFov, 1f - Mathf.Exp(-fovLerp * dt));
#if CINEMACHINE_PRESENT
        if (vcam != null)
        {
            var lens = vcam.Lens;
            lens.FieldOfView = _currentFov;
            vcam.Lens = lens;
        }
#endif
    }

    /// <summary>
    /// Speed lines on/off with hysteresis + state cache. Why:
    ///  - We toggle EMISSION, not the GameObject. SetActive restarts the whole
    ///    particle system every time (expensive, and the lines pop in/out
    ///    instead of fading). Emission.enabled just stops NEW particles —
    ///    existing ones live out their lifetime, so the effect fades naturally.
    ///  - Hysteresis (ON ≠ OFF threshold) prevents flicker when the boost
    ///    hovers around the trigger value during a lerp-down tail.
    ///  - _speedLinesOn caches the last written state so we only call into the
    ///    particle API when it actually CHANGES, not every frame.
    /// </summary>
    private void UpdateSpeedLines(float boost)
    {
        if (speedLines == null) return;

        // One-time: make sure the system is playing so emission.enabled is what
        // actually controls visibility, and ensure the GameObject stays active.
        if (!_speedLinesInit)
        {
            if (!speedLines.gameObject.activeSelf) speedLines.gameObject.SetActive(true);
            if (!speedLines.isPlaying) speedLines.Play(true);
            var em = speedLines.emission;
            em.enabled = false;
            _speedLinesOn = false;
            _speedLinesInit = true;
        }

        bool shouldBeOn = _speedLinesOn
            ? boost > speedLinesOffThreshold   // currently on → stay on until we drop under OFF
            : boost >= speedLinesOnThreshold;  // currently off → wait for ON threshold

        if (shouldBeOn != _speedLinesOn)
        {
            var em = speedLines.emission;
            em.enabled = shouldBeOn;
            _speedLinesOn = shouldBeOn;
        }
    }

    private bool IsAimingAt(BounceBall ball, Vector3 catXZ, Vector3 aim, Vector3 camOriginXZ)
    {
        Vector3 ballXZ = ball.transform.position; ballXZ.y = 0f;

        // Range measured from the CAT (what you're actually chasing).
        float catDist = Vector3.Distance(ballXZ, catXZ);
        if (catDist > boostRange || catDist < 0.01f) return false;

        // "Centered on screen" measured from the CAMERA (what you see).
        Vector3 camToBall = ballXZ - camOriginXZ;
        float camDist = camToBall.magnitude;
        if (camDist < 0.01f) return false;
        camToBall /= camDist;

        return Vector3.Dot(aim, camToBall) >= lookDot;
    }

    private void OnDrawGizmosSelected()
    {
        Transform cam = aimCamera != null ? aimCamera : (Camera.main != null ? Camera.main.transform : null);
        if (cam != null)
        {
            Vector3 fwd = cam.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.0001f)
            {
                fwd.Normalize();
                float halfAngle = Mathf.Acos(Mathf.Clamp(lookDot, -1f, 1f)) * Mathf.Rad2Deg;
                Quaternion l = Quaternion.AngleAxis(-halfAngle, Vector3.up);
                Quaternion r = Quaternion.AngleAxis(halfAngle, Vector3.up);
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
                Gizmos.DrawRay(cam.position, l * fwd * boostRange);
                Gizmos.DrawRay(cam.position, r * fwd * boostRange);
            }
        }

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, boostRange);
    }
}
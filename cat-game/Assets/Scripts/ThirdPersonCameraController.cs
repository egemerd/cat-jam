using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Momentum-style third-person camera for the cat game.
///
/// CORE MODEL (the "like in the games" part):
///   - The camera's ROTATION is driven by the MOUSE ONLY. Nothing else turns it.
///   - WASD moves the cat; the camera just TRACKS the cat's position (it does not
///     rotate to follow the cat). So strafing with A/D never swings your view �
///     only the mouse does.
///   - The camera lives as its own ROOT object. It is NEVER a child of the cat.
///     (If you accidentally parented it, this script detaches it on Awake.)
///
/// Features: smooth positional follow, mouse orbit (yaw + clamped pitch),
/// wall-collision pull-in (SphereCast), speed-driven FOV, contextual shoulder
/// offset for catching, and a master toggle (cameraActive).
///
/// Put this ON the Camera. Set 'target' to the cat. Runs after physics/animation
/// so the view is rock-steady.
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(200)] // run after movement & most other scripts this frame
public class ThirdPersonCameraController : MonoBehaviour
{
    [Header("Master Toggle")]
    [Tooltip("The camera does absolutely nothing until this is true. Flip it in the Inspector or from code.")]
    public bool cameraActive = false;

    [Tooltip("Lock + hide the cursor while the camera is active.")]
    public bool lockCursorWhenActive = true;

    [Header("Safety")]
    [Tooltip("A follow camera must be a root object. If it's parented to anything, detach it on Awake so it can't inherit the cat's rotation.")]
    public bool detachFromParentOnAwake = true;

    [Header("Target")]
    [Tooltip("The cat (or an empty 'look point' parented to the cat).")]
    public Transform target;

    [Tooltip("Where the pivot sits relative to the cat, in WORLD axes. Raise Y to aim at the head/shoulders.")]
    public Vector3 pivotOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Follow (position only � never rotates with the cat)")]
    [Tooltip("Lower = snappier, higher = more lag. Momentum games like a little lag so the camera 'chases'.")]
    [Range(0.01f, 0.5f)] public float followSmoothTime = 0.06f;

    [Header("Mouse Look (the ONLY thing that rotates the camera)")]
    [Tooltip("New Input System reads raw pixel delta, so this is scaled down internally. ~3 feels like the old default.")]
    public float mouseSensitivity = 3f;
    public float minPitch = -35f;
    public float maxPitch = 70f;
    public bool invertY = false;

    [Header("Distance")]
    [Tooltip("How far behind the cat the camera sits when nothing is in the way.")]
    public float defaultDistance = 4.5f;
    [Tooltip("Never get closer than this (stops the camera burying itself in the cat).")]
    public float minDistance = 0.6f;
    [Tooltip("How fast the camera distance eases toward its target length.")]
    [Range(0.01f, 0.5f)] public float distanceSmoothTime = 0.08f;

    [Header("Wall Collision")]
    [Tooltip("Which layers block the camera. Put walls/level geometry here; EXCLUDE the cat's layer.")]
    public LayerMask collisionMask = ~0;
    [Tooltip("Thickness of the camera probe. Bigger = pulls in earlier.")]
    public float cameraRadius = 0.3f;
    [Tooltip("Extra gap kept between the camera and the wall it hit.")]
    public float collisionBuffer = 0.15f;

    [Header("Dynamic FOV")]
    [Tooltip("Optional. If set, speed is read from this. If empty, speed is measured from the target's movement.")]
    public Rigidbody targetRigidbody;
    public float baseFov = 60f;
    public float maxFov = 82f;
    [Tooltip("Speed (units/sec) at which FOV reaches maxFov.")]
    public float speedForMaxFov = 12f;
    [Tooltip("How fast FOV reacts. Lower = punchier acceleration kick.")]
    [Range(0.02f, 0.6f)] public float fovSmoothTime = 0.15f;
    [Tooltip("Ignore vertical speed (jumping/falling) when computing FOV.")]
    public bool fovUsesHorizontalSpeedOnly = true;

    [Header("Contextual Shoulder Offset")]
    [Tooltip("Offset in CAMERA-LOCAL space (x = right, y = up). Applied relative to where you're looking.")]
    public Vector3 defaultShoulder = Vector3.zero;
    [Tooltip("Offset when CatchMode is on. Positive X puts the cat on the LEFT and the view over its right shoulder.")]
    public Vector3 catchShoulder = new Vector3(0.9f, 0.1f, 0f);
    [Range(0.02f, 0.5f)] public float shoulderSmoothTime = 0.18f;
    [Tooltip("Flip this on (from your ball-catching logic) to slide the camera to the right of the cat.")]
    public bool catchMode = false;

    // ---- internals ----
    private Camera _cam;
    private float _yaw;
    private float _pitch;
    private float _currentDistance;
    private Vector3 _smoothedPivot;
    private Vector3 _pivotVel;
    private float _distanceVel;
    private float _fovVel;
    private Vector3 _shoulderVel;
    private Vector3 _currentShoulder;
    private Vector3 _lastTargetPos;
    private bool _initialized;

    private void Awake()
    {
        _cam = GetComponent<Camera>();

        // A follow camera must not be a child of anything, or it inherits the
        // parent's rotation (this is what makes A/D "turn the camera").
        if (detachFromParentOnAwake && transform.parent != null)
        {
            Debug.LogWarning("[ThirdPersonCamera] Camera was parented to '" + transform.parent.name +
                             "'. Detaching � a follow camera should be a root object.", this);
            transform.SetParent(null, true);
        }
    }

    private void OnEnable()
    {
        _initialized = false; // re-sync on enable so it doesn't snap weirdly
    }

    private void Initialize()
    {
        if (target == null) return;

        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = NormalizePitch(e.x);

        _currentDistance = defaultDistance;
        _smoothedPivot = target.position + pivotOffset;
        _currentShoulder = catchMode ? catchShoulder : defaultShoulder;
        _lastTargetPos = target.position;
        _cam.fieldOfView = baseFov;
        _initialized = true;
    }

    private void Update()
    {
        // MOUSE is read here, and it is the ONLY source of camera rotation.
        if (!cameraActive || target == null) return;
        if (Mouse.current == null) return;

        const float deltaScale = 0.05f; // new Input System delta is raw pixels; scale it down
        Vector2 look = Mouse.current.delta.ReadValue() * mouseSensitivity * deltaScale;

        _yaw += look.x;
        _pitch = Mathf.Clamp(_pitch + look.y * (invertY ? 1f : -1f), minPitch, maxPitch);
    }

    private void LateUpdate()
    {
        HandleCursor();

        if (!cameraActive || target == null)
            return;

        if (!_initialized)
            Initialize();

        // 1) Track the cat's position only. (No rotation from the cat � ever.)
        Vector3 targetPivot = target.position + pivotOffset;
        _smoothedPivot = Vector3.SmoothDamp(_smoothedPivot, targetPivot, ref _pivotVel, followSmoothTime);

        // 2) Rotation comes purely from mouse-driven yaw/pitch.
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);

        // 3) Blend shoulder offset (default <-> catch), applied in camera-local space.
        Vector3 targetShoulder = catchMode ? catchShoulder : defaultShoulder;
        _currentShoulder = Vector3.SmoothDamp(_currentShoulder, targetShoulder, ref _shoulderVel, shoulderSmoothTime);
        Vector3 pivotWithShoulder = _smoothedPivot + rot * _currentShoulder;

        // 4) Wall collision: probe from the pivot back toward the camera seat.
        Vector3 backDir = rot * Vector3.back;
        float wantedDistance = defaultDistance;

        if (Physics.SphereCast(pivotWithShoulder, cameraRadius, backDir, out RaycastHit hit,
                defaultDistance, collisionMask, QueryTriggerInteraction.Ignore))
        {
            wantedDistance = Mathf.Max(hit.distance - collisionBuffer, minDistance);
        }

        // Snap IN instantly (never clip), ease OUT smoothly (glide back when clear).
        if (wantedDistance < _currentDistance)
            _currentDistance = wantedDistance;
        else
            _currentDistance = Mathf.SmoothDamp(_currentDistance, wantedDistance, ref _distanceVel, distanceSmoothTime);

        // 5) Place & aim the camera (WORLD space � unaffected by any parent).
        transform.position = pivotWithShoulder + backDir * _currentDistance;
        transform.rotation = rot;

        // 6) Dynamic FOV from speed.
        float speed = GetSpeed();
        float t = Mathf.Clamp01(speed / Mathf.Max(0.01f, speedForMaxFov));
        float targetFov = Mathf.Lerp(baseFov, maxFov, t);
        _cam.fieldOfView = Mathf.SmoothDamp(_cam.fieldOfView, targetFov, ref _fovVel, fovSmoothTime);

        _lastTargetPos = target.position;
    }

    private float GetSpeed()
    {
        Vector3 v;
        if (targetRigidbody != null)
            v = targetRigidbody.linearVelocity;
        else
            v = (target.position - _lastTargetPos) / Mathf.Max(Time.deltaTime, 0.0001f);

        if (fovUsesHorizontalSpeedOnly) v.y = 0f;
        return v.magnitude;
    }

    private void HandleCursor()
    {
        if (!lockCursorWhenActive) return;

        if (cameraActive)
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        else if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private static float NormalizePitch(float raw)
    {
        if (raw > 180f) raw -= 360f; // euler.x is 0..360; convert to -180..180 so clamping works
        return raw;
    }

    // ---- public helpers ----
    public void SetActive(bool value) => cameraActive = value;
    public void SetCatchMode(bool value) => catchMode = value;

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Vector3 pivot = Application.isPlaying ? _smoothedPivot : target.position + pivotOffset;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pivot, 0.1f);
        Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
        Gizmos.DrawWireSphere(pivot + (Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back) * defaultDistance, cameraRadius);
    }
}
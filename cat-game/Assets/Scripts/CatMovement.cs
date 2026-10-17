using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Minimal momentum-style mover for testing the camera.
/// Rigidbody-based, camera-relative WASD, with acceleration ramp so you can
/// feel the FOV kick in. We'll grow this later.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CatMovement : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The camera's transform. Movement is relative to where you're looking.")]
    public Transform cameraTransform;

    [Header("Movement")]
    public float maxSpeed = 8f;
    [Tooltip("How fast you ramp up to max speed. Lower = more slippery momentum.")]
    public float acceleration = 25f;
    [Tooltip("How fast you slow down when there's no input.")]
    public float deceleration = 18f;
    [Tooltip("How fast the cat turns to face its movement direction.")]
    public float turnSpeed = 14f;

    [Header("Jump (optional)")]
    public bool allowJump = true;
    public float jumpForce = 6f;
    public LayerMask groundMask = ~0;
    public float groundCheckDistance = 0.3f;

    private Rigidbody _rb;
    private Vector3 _inputDir;
    private bool _jumpQueued;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate; // smooth against the camera
        _rb.constraints = RigidbodyConstraints.FreezeRotation;   // we rotate manually

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void Update()
    {
        // Read input here (responsive), apply physics in FixedUpdate.
        var kb = Keyboard.current;
        float h = 0f, v = 0f;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
        }

        Vector3 fwd = cameraTransform ? cameraTransform.forward : Vector3.forward;
        Vector3 right = cameraTransform ? cameraTransform.right : Vector3.right;
        fwd.y = 0f; right.y = 0f;
        fwd.Normalize(); right.Normalize();

        _inputDir = (fwd * v + right * h);
        if (_inputDir.sqrMagnitude > 1f) _inputDir.Normalize();

        if (allowJump && kb != null && kb.spaceKey.wasPressedThisFrame && IsGrounded())
            _jumpQueued = true;
    }

    private void FixedUpdate()
    {
        // Horizontal velocity only; leave gravity/jump on Y alone.
        Vector3 vel = _rb.linearVelocity;
        Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);

        Vector3 targetVel = _inputDir * maxSpeed;
        float rate = _inputDir.sqrMagnitude > 0.01f ? acceleration : deceleration;
        horizontal = Vector3.MoveTowards(horizontal, targetVel, rate * Time.fixedDeltaTime);

        _rb.linearVelocity = new Vector3(horizontal.x, vel.y, horizontal.z);

        // Face the direction of travel.
        if (horizontal.sqrMagnitude > 0.1f)
        {
            Quaternion look = Quaternion.LookRotation(horizontal.normalized, Vector3.up);
            _rb.rotation = Quaternion.Slerp(_rb.rotation, look, turnSpeed * Time.fixedDeltaTime);
        }

        if (_jumpQueued)
        {
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            _rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
            _jumpQueued = false;
        }
    }

    private bool IsGrounded()
    {
        return Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down,
            groundCheckDistance + 0.1f, groundMask, QueryTriggerInteraction.Ignore);
    }
}
using UnityEngine;
using System.Collections.Generic;
using StarterAssets; // ThirdPersonController lives in this namespace

/// <summary>
/// Goes on the CAT (the player object, next to ThirdPersonController).
///
/// Two jobs:
///  1) Detect the cat running into a ball and tell that ball to bounce.
///     CharacterController doesn't fire Rigidbody collisions reliably, so we use
///     OnControllerColliderHit — the correct hook for a CharacterController.
///  2) Give the cat a small speed boost while it's chasing a ball that just
///     bounced off a wall — but only if that ball is in the cat's look direction
///     AND the cat is actually moving toward it.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CatBallInteraction : MonoBehaviour
{
    public static CatBallInteraction Instance;

    [Header("References")]
    [Tooltip("The StarterAssets controller to apply the boost to. Auto-filled if on the same object.")]
    public ThirdPersonController controller;

    [Header("Wall-bounce chase boost")]
    [Tooltip("Extra m/s added to the cat's speed while chasing a wall-bounced ball.")]
    public float boostAmount = 2.5f;
    [Tooltip("Only balls within this distance can give a boost.")]
    public float boostRange = 12f;
    [Tooltip("How much 'in front' the ball must be. 1 = dead ahead, 0 = 90 degrees to the side. 0.5 ≈ within a 60° cone.")]
    [Range(0f, 1f)] public float lookDot = 0.5f;
    [Tooltip("How fast the boost ramps up.")]
    public float boostAttack = 10f;
    [Tooltip("How fast the boost fades when you stop qualifying.")]
    public float boostDecay = 6f;
    [Tooltip("How long after a wall bounce a ball stays 'hot' (boost-eligible).")]
    public float wallBounceWindow = 1.5f;

    private CharacterController _cc;
    private float _currentBoost;

    private class HotBall { public Transform t; public float expire; }
    private readonly List<HotBall> _hot = new List<HotBall>();

    private void Awake()
    {
        Instance = this;
        _cc = GetComponent<CharacterController>();
        if (controller == null) controller = GetComponent<ThirdPersonController>();
    }

    /// <summary>Called by BounceBall when it bounces off a wall.</summary>
    public void RegisterWallBounce(Transform ball)
    {
        for (int i = 0; i < _hot.Count; i++)
        {
            if (_hot[i].t == ball) { _hot[i].expire = Time.time + wallBounceWindow; return; }
        }
        _hot.Add(new HotBall { t = ball, expire = Time.time + wallBounceWindow });
    }

    // The cat ran into something. If it's a ball, bounce it off the cat's forward.
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        BounceBall ball = hit.collider.GetComponentInParent<BounceBall>();
        if (ball != null)
            ball.BounceFromCat(transform.forward);
    }

    private void Update()
    {
        // Drop expired / destroyed hot balls.
        for (int i = _hot.Count - 1; i >= 0; i--)
            if (_hot[i].t == null || Time.time > _hot[i].expire) _hot.RemoveAt(i);

        bool qualifies = false;

        Vector3 moveDir = _cc.velocity; moveDir.y = 0f;
        float moveSpeed = moveDir.magnitude;
        if (moveSpeed > 0.1f)
        {
            moveDir /= moveSpeed;
            Vector3 look = transform.forward; look.y = 0f; look.Normalize();

            for (int i = 0; i < _hot.Count; i++)
            {
                Vector3 to = _hot[i].t.position - transform.position; to.y = 0f;
                float dist = to.magnitude;
                if (dist > boostRange || dist < 0.01f) continue;
                to /= dist;

                // Ball is in the look direction AND we're moving toward it.
                if (Vector3.Dot(look, to) >= lookDot && Vector3.Dot(moveDir, to) >= lookDot)
                {
                    qualifies = true;
                    break;
                }
            }
        }

        float target = qualifies ? boostAmount : 0f;
        float rate = qualifies ? boostAttack : boostDecay;
        _currentBoost = Mathf.MoveTowards(_currentBoost, target, rate * Time.deltaTime);

        if (controller != null)
            controller.ExternalSpeedBoost = _currentBoost;
    }

    private void CatchBoostFov()
    {

    }
}
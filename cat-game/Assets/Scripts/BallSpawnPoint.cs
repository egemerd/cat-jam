using UnityEngine;

/// <summary>
/// One pipe mouth. Place one of these at each pipe's exit point and ORIENT it
/// so its local +Z (the blue axis arrow in the Scene view) points OUT of the
/// pipe — that's the direction balls will shoot.
///
/// This component does not spawn on its own. BallSpawnManager picks one at
/// random and calls Spawn(prefab) on it.
///
/// Setup:
///  1. Create an empty GameObject at the pipe's exit.
///  2. Rotate it so the blue Z-axis arrow points out of the pipe.
///  3. Add this component. Tune launch speed and spread in the inspector.
///  4. Make sure BallSpawnManager picks it up (auto-finds if its list is empty).
/// </summary>
public class BallSpawnPoint : MonoBehaviour
{
    [Header("Push velocity (along this transform's forward)")]
    [Tooltip("Minimum speed the ball comes out at. Random between min and max.")]
    public float launchSpeedMin = 5f;
    [Tooltip("Maximum speed the ball comes out at.")]
    public float launchSpeedMax = 8f;

    [Header("Variation")]
    [Tooltip("Max angle (degrees) a shot can deviate from the pipe's forward direction. 0 = perfectly aligned every time.")]
    [Range(0f, 60f)] public float spreadAngle = 8f;

    /// <summary>
    /// Called by BallSpawnManager. Instantiates the prefab at this pipe's
    /// position and launches it along this pipe's forward direction.
    /// </summary>
    public BounceBall Spawn(BounceBall prefab)
    {
        if (prefab == null) return null;

        BounceBall ball = Instantiate(prefab, transform.position, transform.rotation);

        Vector3 dir = ApplyConeSpread(transform.forward, spreadAngle);
        float speed = Random.Range(launchSpeedMin, launchSpeedMax);

        ball.Launch(dir * speed);
        return ball;
    }

    /// <summary>Pick a random direction inside a cone of half-angle 'spreadDeg' around 'forward'.</summary>
    private Vector3 ApplyConeSpread(Vector3 forward, float spreadDeg)
    {
        if (spreadDeg <= 0f) return forward.normalized;

        // Tilt forward by a random angle (0..spread) around a perpendicular axis,
        // then spin that around 'forward' so the tilt direction is random.
        float tiltDeg = Random.Range(0f, spreadDeg);
        float azimuthDeg = Random.Range(0f, 360f);

        Vector3 tilted = Quaternion.AngleAxis(tiltDeg, transform.right) * forward;
        Vector3 result = Quaternion.AngleAxis(azimuthDeg, forward) * tilted;
        return result.normalized;
    }

    private void OnDrawGizmos()
    {
        // Pipe direction (big arrow)
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.2f);
        Vector3 fwd = transform.forward;
        Gizmos.DrawLine(transform.position, transform.position + fwd * 1.5f);

        // Cone edges (approximate — one line per 45° around the cone)
        if (spreadAngle > 0f)
        {
            Gizmos.color = new Color(0.2f, 1f, 1f, 0.35f);
            for (int i = 0; i < 8; i++)
            {
                float az = i * 45f;
                Vector3 tilted = Quaternion.AngleAxis(spreadAngle, transform.right) * fwd;
                Vector3 edge = Quaternion.AngleAxis(az, fwd) * tilted;
                Gizmos.DrawLine(transform.position, transform.position + edge * 1.5f);
            }
        }
    }
}
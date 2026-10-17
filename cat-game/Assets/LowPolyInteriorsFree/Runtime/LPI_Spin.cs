using UnityEngine;

namespace LowPolyInteriors
{
    /// Spins a ceiling fan, an extractor or any other rotationally
    /// symmetric fitting. Add it to the prefab's root.
    [AddComponentMenu("Low Poly Interiors/Spin")]
    public class LPI_Spin : MonoBehaviour
    {
        public enum SpinAxis { X, Y, Z }

        [Tooltip("Degrees per second. Negative reverses.")]
        public float speed = 150f;

        [Tooltip("Local axis to turn about. Models import Y-up.")]
        public SpinAxis axis = SpinAxis.Y;

        [Tooltip("Seconds to reach full speed from rest. 0 starts instantly.")]
        public float spinUp = 1.2f;

        [Tooltip("Turn in unscaled time, so it keeps going while paused.")]
        public bool unscaledTime = false;

        float _rate;

        void OnEnable() { _rate = spinUp <= 0f ? speed : 0f; }

        void Update()
        {
            float dt = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (spinUp > 0f)
                _rate = Mathf.MoveTowards(_rate, speed,
                                          Mathf.Abs(speed) / spinUp * dt);
            else
                _rate = speed;
            transform.Rotate(Axis() * (_rate * dt), Space.Self);
        }

        Vector3 Axis()
        {
            if (axis == SpinAxis.X) return Vector3.right;
            if (axis == SpinAxis.Z) return Vector3.forward;
            return Vector3.up;
        }
    }
}

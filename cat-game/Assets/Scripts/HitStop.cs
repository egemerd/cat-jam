using UnityEngine;

/// <summary>
/// Game-wide hit-stop. One call, one line, from anywhere:
///
///     HitStop.Freeze(0.08f);           // hard freeze for 80ms
///     HitStop.Freeze(0.12f, 0.05f);    // 120ms, slowed to 5% speed instead of full stop
///
/// Features:
///  - Uses UNSCALED time internally, so the freeze itself doesn't get frozen.
///  - Longest-wins: a 0.2s freeze while a 0.05s freeze is already running
///    extends the stop; a shorter call never cuts a longer one short.
///  - Auto-bootstraps — nothing to add to the scene. The first call creates
///    the manager object itself.
///  - Also restores Time.fixedDeltaTime so physics stays stable after the freeze.
///
/// Where to call it:
///  - On a hit: HitStop.Freeze(0.08f);
///  - On a big hit, call with a longer duration — longest-wins handles it.
///  - For a slow-mo moment (not a hard freeze), use the overload:
///      HitStop.Freeze(0.4f, 0.2f);  // 400ms at 20% speed
/// </summary>
public static class HitStop
{
    private class Driver : MonoBehaviour
    {
        public float untilUnscaledTime;
        public float activeScale = 0f;
        private float _savedFixedDelta;
        private bool _frozen;

        private void Awake()
        {
            _savedFixedDelta = Time.fixedDeltaTime;
            DontDestroyOnLoad(gameObject);
        }

        public void Begin(float duration, float scaleDuringFreeze)
        {
            float endTime = Time.unscaledTime + duration;
            // Longest-wins: a short new freeze can't shorten a running longer one.
            // A deeper (slower) scale wins over a shallower one.
            if (endTime > untilUnscaledTime) untilUnscaledTime = endTime;
            if (scaleDuringFreeze < activeScale || !_frozen) activeScale = scaleDuringFreeze;

            if (!_frozen)
            {
                _savedFixedDelta = Time.fixedDeltaTime;
                _frozen = true;
            }

            // Apply immediately so the caller's current frame sees the freeze.
            Time.timeScale = activeScale;
            Time.fixedDeltaTime = _savedFixedDelta * Mathf.Max(activeScale, 0.0001f);
        }

        private void LateUpdate()
        {
            if (!_frozen) return;
            if (Time.unscaledTime >= untilUnscaledTime)
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = _savedFixedDelta;
                _frozen = false;
                activeScale = 0f;
            }
        }
    }

    private static Driver _driver;

    private static Driver GetDriver()
    {
        if (_driver == null)
        {
            var go = new GameObject("[HitStop]");
            _driver = go.AddComponent<Driver>();
        }
        return _driver;
    }

    /// <summary>Hard freeze for <paramref name="duration"/> seconds (unscaled).</summary>
    public static void Freeze(float duration)
        => GetDriver().Begin(Mathf.Max(0f, duration), 0f);

    /// <summary>
    /// Slow time to <paramref name="timeScaleDuringFreeze"/> (0 = full stop, 0.2 = 20% speed)
    /// for <paramref name="duration"/> seconds (unscaled). Use for juicy slow-mo moments.
    /// </summary>
    public static void Freeze(float duration, float timeScaleDuringFreeze)
        => GetDriver().Begin(Mathf.Max(0f, duration), Mathf.Clamp01(timeScaleDuringFreeze));

    /// <summary>Cancel any active freeze right now.</summary>
    public static void CancelNow()
    {
        if (_driver == null) return;
        _driver.untilUnscaledTime = Time.unscaledTime;
    }
}
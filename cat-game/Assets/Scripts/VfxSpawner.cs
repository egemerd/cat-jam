using UnityEngine;

/// <summary>
/// One-call VFX spawner. Pattern:
///   VfxSpawner.Play(myPrefab, position, rotation);
///
/// Does three things so you don't have to:
///  1) Instantiates the prefab at the given pose.
///  2) Plays any particle systems on it (including children — covers nested VFX).
///  3) Auto-destroys the instance once the longest particle finishes, so you
///     never leak objects into the scene and never think about lifetime.
///
/// For VFX that aren't particle systems (an Animator-driven effect, say),
/// pass an explicit lifetime via the overload.
///
/// Usage:
///   [SerializeField] GameObject hitVfx;
///   ...
///   VfxSpawner.Play(hitVfx, hit.point, Quaternion.LookRotation(hit.normal));
///
/// Attach-to-moving-target variant:
///   VfxSpawner.PlayAttached(trailVfx, ball.transform);
/// </summary>
public static class VfxSpawner
{
    /// <summary>Spawn a VFX at a world pose. Returns the instance (usually ignored).</summary>
    public static GameObject Play(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;
        GameObject go = Object.Instantiate(prefab, position, rotation);
        AutoDestroy(go);
        return go;
    }

    /// <summary>Spawn with just a position (identity rotation).</summary>
    public static GameObject Play(GameObject prefab, Vector3 position)
        => Play(prefab, position, Quaternion.identity);

    /// <summary>Spawn as a child of 'parent' so the VFX follows it (good for trails on moving objects).</summary>
    public static GameObject PlayAttached(GameObject prefab, Transform parent, Vector3 localOffset = default)
    {
        if (prefab == null || parent == null) return null;
        GameObject go = Object.Instantiate(prefab, parent);
        go.transform.localPosition = localOffset;
        go.transform.localRotation = Quaternion.identity;
        AutoDestroy(go);
        return go;
    }

    /// <summary>Spawn with an explicit lifetime (for non-particle VFX).</summary>
    public static GameObject Play(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
    {
        if (prefab == null) return null;
        GameObject go = Object.Instantiate(prefab, position, rotation);
        Object.Destroy(go, lifetime);
        return go;
    }

    // --- internals ---

    private static void AutoDestroy(GameObject go)
    {
        // Longest particle lifetime across the whole prefab (self + children).
        // Destroy a hair after it finishes so trailing particles aren't cut off.
        float lifetime = 0f;
        var systems = go.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
        for (int i = 0; i < systems.Length; i++)
        {
            var ps = systems[i];
            var main = ps.main;
            float duration = main.duration + main.startLifetime.constantMax;
            if (duration > lifetime) lifetime = duration;
        }
        if (lifetime <= 0f) lifetime = 2f; // sensible fallback if no particles
        Object.Destroy(go, lifetime + 0.1f);
    }
}
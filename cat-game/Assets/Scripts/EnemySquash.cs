using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Enemy visual root'unu impact anında squash & stretch ile deforme eder.
/// Logic root'un scale'ini ETKİLEMEZ — collider ve fizik intakt kalır.
/// 
/// EnemyCore orchestrator hit event'ini alıp TriggerSquash() çağırır.
/// Attack tarafı isterse HitInfo üzerinden configOverride geçebilir 
/// (ileride modülerlik için hazır).
/// </summary>
public class EnemySquash : MonoBehaviour
{
    [Header("Config")]
    [Tooltip("Default squash preset'i. Attack tarafı override etmezse bu kullanılır.")]
    [SerializeField] private EnemySquashConfigSO defaultConfig;

    [Header("Target")]
    [Tooltip("Ölçeklenecek transform. Enemy prefab'da 'SquashRoot' child'ı olmalı. " +
             "Bu transform'un altında visual mesh yer alır, collider'lar burada OLMAMALIDIR.")]
    [SerializeField] private Transform squashRoot;

    // Cached original state — restore için
    private Vector3 originalLocalScale;
    private Quaternion originalLocalRotation;

    // Runtime
    private Coroutine squashRoutine;

    // Public state — VFX veya diğer sistemler dinleyebilir
    public bool IsSquashing => squashRoutine != null;

    // Events
    public event Action OnSquashStarted;
    public event Action OnSquashEnded;

    private void Awake()
    {
        if (squashRoot == null)
        {
            Debug.LogError($"[EnemySquash] squashRoot atanmamış! {name}", this);
            enabled = false;
            return;
        }

        // Original state'i cache'le — asset ölçekleri farklı olabilir, 
        // (1,1,1) hardcode etmek yanlış
        originalLocalScale = squashRoot.localScale;
        originalLocalRotation = squashRoot.localRotation;
    }

    /// <summary>
    /// Squash tetikler. hitDirection world-space, normalize edilmiş beklenir.
    /// configOverride null ise defaultConfig kullanılır.
    /// </summary>
    public void TriggerSquash(Vector3 hitDirection, EnemySquashConfigSO configOverride = null)
    {
        var activeConfig = configOverride != null ? configOverride : defaultConfig;
        if (activeConfig == null || squashRoot == null) return;

        // Devam eden squash varsa iptal et, yeni hit tazeleyecek
        if (squashRoutine != null)
        {
            StopCoroutine(squashRoutine);
            // NOT: original state'e reset ETMİYORUZ — yeni coroutine mevcut scale'den 
            // doğal geçiş yapar. Ama rotation reset lazım çünkü yeni hit farklı yönden gelebilir.
            squashRoot.localRotation = originalLocalRotation;
        }

        squashRoutine = StartCoroutine(SquashRoutine(hitDirection, activeConfig));
    }

    private IEnumerator SquashRoutine(Vector3 hitDirection, EnemySquashConfigSO cfg)
    {
        OnSquashStarted?.Invoke();

        // Hit direction'ı XZ düzlemine izdüşür (top-down)
        // Y bileşenini at, çünkü squash horizontal
        Vector3 flatHitDir = new Vector3(hitDirection.x, 0f, hitDirection.z);
        if (flatHitDir.sqrMagnitude < 0.0001f)
            flatHitDir = squashRoot.forward; // fallback

        flatHitDir.Normalize();

        // Alignment: pivot'u impact yönüne çevir
        // Böylece pivot'un LOCAL Z ekseni = hit ekseni, LOCAL X = perpendicular
        if (cfg.alignToHitDirection)
        {
            // World-space hit direction'ı parent-space'e çevir 
            // (squashRoot child ise parent'ın rotation'ından etkilenir)
            Transform parent = squashRoot.parent;
            Vector3 localHitDir = parent != null
                ? parent.InverseTransformDirection(flatHitDir)
                : flatHitDir;

            squashRoot.localRotation = Quaternion.LookRotation(localHitDir, Vector3.up);
        }

        // Squashed scale hedefi:
        // Local Z (hit ekseni, pivot LookRotation ile hizalandı) → squashAxisScale
        // Local X (perpendicular)                              → stretchAxisScale  
        // Local Y (vertical)                                   → verticalScale
        Vector3 squashedScale = new Vector3(
            originalLocalScale.x * cfg.stretchAxisScale,
            originalLocalScale.y * cfg.verticalScale,
            originalLocalScale.z * cfg.squashAxisScale
        );

        float elapsed = 0f;
        while (elapsed < cfg.duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / cfg.duration);
            float amount = cfg.squashCurve.Evaluate(t);

            // LerpUnclamped — curve overshoot ederse (negatif veya >1) 
            // scale de overshoot etsin. Bounce hissi burada doğal doğuyor.
            squashRoot.localScale = Vector3.LerpUnclamped(
                originalLocalScale,
                squashedScale,
                amount
            );

            yield return null;
        }

        // Kesin restore — floating point sürüklenmesi olmasın
        squashRoot.localScale = originalLocalScale;
        squashRoot.localRotation = originalLocalRotation;

        squashRoutine = null;
        OnSquashEnded?.Invoke();
    }

    /// <summary>
    /// Squash'ı zorla durdur ve original state'e döndür.
    /// Death, disable veya scene reset senaryolarında çağırılır.
    /// </summary>
    public void StopSquash()
    {
        if (squashRoutine != null)
        {
            StopCoroutine(squashRoutine);
            squashRoutine = null;
        }

        if (squashRoot != null)
        {
            squashRoot.localScale = originalLocalScale;
            squashRoot.localRotation = originalLocalRotation;
        }

        OnSquashEnded?.Invoke();
    }

    private void OnDisable()
    {
        // Enemy disable/pool'a dönerken squash state'i temizle
        StopSquash();
    }
}
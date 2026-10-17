using UnityEngine;

/// <summary>
/// Squash & stretch preset'i. Enemy hit yediğinde visual root'un nasıl 
/// deforme olacağını tanımlar.
/// 
/// İki modda çalışabilir:
/// - alignToHitDirection = true → impact ekseninde bastırır, dikte gerer (directional)
/// - alignToHitDirection = false → yerel Z ekseninde sabit squash (uniform stil için 
///   squashAxis == stretchAxis yapılabilir)
/// 
/// Farklı preset'ler (Light/Heavy/Boss) ayrı SO asset'i olarak yaratılır ve 
/// EnemySquash inspector'ından veya attack tarafından override edilir.
/// </summary>
[CreateAssetMenu(fileName = "SquashProfile", menuName = "Combat/Squash Profile", order = 6)]
public class EnemySquashConfigSO : ScriptableObject
{
    [Header("— Scale —")]
    [Tooltip("Impact ekseninde bastırma miktarı. 1 = squash yok, 0.6 = %40 sıkışma. " +
             "Light/subtle: 0.75-0.85, Heavy/juicy: 0.5-0.65")]
    [Range(0.1f, 1f)]
    public float squashAxisScale = 0.6f;

    [Tooltip("Impact'e dik eksende gerinme. 1 = stretch yok, 1.3 = %30 gerinme. " +
             "Volume-preserving hissi için squash ile ters oranda tut " +
             "(squash 0.6 → stretch ~1.3-1.4).")]
    [Range(1f, 2f)]
    public float stretchAxisScale = 1.3f;

    [Tooltip("Y ekseni (dikey) scale. Top-down'da hafif flatten arcade hissi verir. " +
             "0.85-0.95 = subtle, 1 = dokunma.")]
    [Range(0.5f, 1.2f)]
    public float verticalScale = 0.9f;

    [Header("— Timing —")]
    [Tooltip("Toplam animasyon süresi. Hızlı arcade: 0.15-0.25s, ağır feel: 0.3-0.4s.")]
    [Range(0.05f, 0.8f)]
    public float duration = 0.22f;

    [Header("— Shape —")]
    [Tooltip("Squash miktarını zamanla haritalar. 0 = normal scale, 1 = max squash. " +
             "Tipik: dik yükseliş (fast squash) + yavaş dönüş (slow recovery). " +
             "Curve'ün son kısmı 0'ın altına dipse hafif overshoot/streç olur — cartoony bounce.")]
    public AnimationCurve squashCurve = CreateDefaultSquashCurve();

    [Header("— Alignment —")]
    [Tooltip("True: squash impact yönüne göre hizalanır (directional impact). " +
             "False: squash yerel Z ekseninde sabit uygulanır (yön bağımsız).")]
    public bool alignToHitDirection = true;

    /// <summary>
    /// Default curve: hızlı bastırma (t=0.12'de peak), yumuşak recovery, 
    /// son %10'da hafif overshoot.
    /// </summary>
    private static AnimationCurve CreateDefaultSquashCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 12f),       // Hızlı yükseliş
            new Keyframe(0.12f, 1f, 0f, 0f),      // Peak squash
            new Keyframe(0.85f, -0.1f, 0f, 0f),   // Hafif overshoot (streç yönü)
            new Keyframe(1f, 0f, 0f, 0f)          // Normale otur
        );
    }
}
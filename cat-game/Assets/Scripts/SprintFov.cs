using UnityEngine;
using Unity.Cinemachine;
using StarterAssets;

/// <summary>
/// Hýz boost'u ile FOV punch. vcam'in üzerine (veya herhangi bir yere) ekle,
/// vcam ve controller referanslarýný Inspector'dan ata.
///
/// Çalýþma mantýðý:
///   boost = 0           -> FOV == baseFov
///   boost >= boostAtMax -> FOV == boostedFov
///   arasý              -> ikisi arasýnda lerp
/// Hedef FOV'a geçiþ framerate-independent exponential lerp ile smooth olur.
/// </summary>
public class SprintFov : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Hangi Cinemachine kamerasýnýn FOV'u deðiþsin. Boþ býrakýrsan bu GameObject'in üzerindekini kullanýr.")]
    public CinemachineCamera vcam;
    [Tooltip("Hýz boost'unu okuyacaðýmýz controller. ExternalSpeedBoost field'ýný okur.")]
    public ThirdPersonController controller;

    [Header("FOV")]
    [Tooltip("Boost yokken sabit duran FOV. Start'ta vcam'in mevcut FOV'u otomatik buraya yazýlýr.")]
    public float baseFov = 60f;
    [Tooltip("Boost en yüksekte iken ulaþýlacak FOV.")]
    public float boostedFov = 72f;
    [Tooltip("ExternalSpeedBoost bu deðere ulaþtýðýnda FOV = boostedFov olur. CatBallInteraction'daki maxBoost ile ayný olsun.")]
    public float boostAtMax = 6f;
    [Tooltip("Lerp hýzý. Yüksek = snappier (daha hýzlý tepki), düþük = daha yumuþak geçiþ.")]
    public float lerpSpeed = 8f;

    private void Reset()
    {
        // Component ilk eklendiðinde otomatik doldur
        if (vcam == null) vcam = GetComponent<CinemachineCamera>();
    }

    private void Start()
    {
        // Vcam'in mevcut FOV'unu base olarak yakala — Inspector'da ayrý deðer girmene gerek kalmasýn
        if (vcam != null) baseFov = vcam.Lens.FieldOfView;
    }

    private void Update()
    {
        if (vcam == null || controller == null) return;

        // Boost miktarýna göre hedef FOV'u hesapla
        float t = Mathf.Clamp01(controller.ExternalSpeedBoost / Mathf.Max(0.01f, boostAtMax));
        float targetFov = Mathf.Lerp(baseFov, boostedFov, t);

        // Framerate-independent smooth lerp
        var lens = vcam.Lens;
        float k = 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime);
        lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, targetFov, k);
        vcam.Lens = lens;
    }
}
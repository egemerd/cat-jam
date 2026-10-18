using UnityEngine;
using StarterAssets;

/// <summary>
/// Cat üzerine ekle, ThirdPersonController'ýn yanýna. Grounded durumunu takip
/// eder — havaya kalktýðý aný yakalar ve AudioManager üzerinden jump sesini çalar.
///
/// NOT: Bu script StarterAssets klasörünün DIÞINDA olmalý (örneðin Assets/Scripts/
/// altýnda). StarterAssets paketi kendi asmdef'ini kullanýyor ve o assembly senin
/// global AudioManager'ýný göremiyor. Dýþarýdan dinleyerek bu sýnýrý aþýyoruz.
///
/// Setup:
///  1. Bu scripti Assets/Scripts/ (veya StarterAssets dýþýnda bir yere) koy.
///  2. Cat GameObject'ine ekle (ThirdPersonController ile ayný obje).
///  3. Inspector'da Jump Sound slot'una SoundSO asset'ini sürükle.
///  4. Opsiyonel: Land Sound (yere düþünce) ekleyebilirsin.
/// </summary>
[RequireComponent(typeof(ThirdPersonController))]
public class CatJumpSound : MonoBehaviour
{
    [Header("Sounds")]
    [Tooltip("Zýplama anýnda (yerdeyken havaya kalkýnca) çalýnacak ses.")]
    public SoundSO jumpSound;
    [Tooltip("Opsiyonel: yere inince çalýnacak ses.")]
    public SoundSO landSound;

    private ThirdPersonController _controller;
    private bool _wasGrounded = true;

    private void Awake()
    {
        _controller = GetComponent<ThirdPersonController>();
    }

    private void Update()
    {
        if (_controller == null) return;

        bool nowGrounded = _controller.Grounded;

        // Takeoff: önceki frame yerdeydi, þimdi havada.
        if (_wasGrounded && !nowGrounded)
        {
            if (AudioManager.Instance != null && jumpSound != null)
                AudioManager.Instance.Play(jumpSound);
        }
        // Landing: önceki frame havadaydý, þimdi yerde.
        else if (!_wasGrounded && nowGrounded)
        {
            if (AudioManager.Instance != null && landSound != null)
                AudioManager.Instance.Play(landSound);
        }

        _wasGrounded = nowGrounded;
    }
}
using Unity.Cinemachine; // Cinemachine 2.x namespace (no "Unity." prefix)
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Widens the Cinemachine camera's FOV while sprinting. (Cinemachine 2.x)
///
/// You cannot set Camera.fieldOfView directly with Cinemachine — the Brain
/// overrides it from the vcam's Lens every frame. So this writes to the
/// vcam's m_Lens.FieldOfView instead.
///
/// Put this component ON your Cinemachine Virtual Camera object.
/// </summary>
public class SprintFOV : MonoBehaviour
{
    [Tooltip("The virtual camera to zoom. Auto-filled if this script is on the vcam.")]
    public CinemachineCamera vcam;

    [Header("FOV")]
    public float normalFov = 60f;
    public float sprintFov = 75f;
    [Tooltip("How fast the FOV eases between the two. Higher = snappier.")]
    public float lerpSpeed = 8f;

    [Header("Sprint source")]
    [Tooltip("ON = holding Left Shift counts as sprinting (quick test).\nOFF = you set IsSprinting from your own controller instead.")]
    public bool useLeftShiftAsSprint = true;

    /// <summary>Set this from your character controller if you don't use the Shift key.</summary>
    public bool IsSprinting { get; set; }

    private void Reset()
    {
        vcam = GetComponent<CinemachineCamera>();
    }

    private void Update()
    {
        if (vcam == null) return;

        if (useLeftShiftAsSprint && Keyboard.current != null)
            IsSprinting = Keyboard.current.leftShiftKey.isPressed;

        float target = IsSprinting ? sprintFov : normalFov;

        // 2.x stores le
        vcam.Lens.FieldOfView = Mathf.Lerp(vcam.Lens.FieldOfView, target, lerpSpeed * Time.deltaTime);
    }
}
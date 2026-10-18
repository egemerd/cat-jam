using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "NewSound", menuName = "Audios/Sound", order = 0)]
public class SoundSO : ScriptableObject
{
    [Header("Clips")]
    [Tooltip("Birden fazla verirsen her çalýþta rastgele seçilir (varyasyon için).")]
    public AudioClip[] clips;

    [Header("Volume")]
    [Range(0f, 1f)] public float volume = 1f;
    [Range(0f, 0.5f)] public float volumeVariation = 0f;

    [Header("Pitch")]
    [Range(0.1f, 3f)] public float pitch = 1f;
    [Range(0f, 0.5f)] public float pitchVariation = 0.05f;

    [Header("Mixer")]
    public AudioMixerGroup mixerGroup;

    [Header("Playback")]
    public bool loop = false;
    [Range(0f, 1f)] public float spatialBlend = 0f; // 0 = 2D, 1 = 3D
    [Range(0, 256)] public int priority = 128;

    [Header("Anti-Spam")]
    [Tooltip("Ayný ses en fazla bu kadar sýk çalabilir (sn). 0 = limit yok.")]
    public float minInterval = 0f;

    // Runtime cooldown; asset'e serialize edilmez
    [System.NonSerialized] private float _lastPlayTime = -999f;

    public AudioClip GetClip()
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    public float GetVolume() =>
        Mathf.Clamp01(volume + Random.Range(-volumeVariation, volumeVariation));

    public float GetPitch() =>
        pitch + Random.Range(-pitchVariation, pitchVariation);

    public bool CanPlayNow() =>
        minInterval <= 0f || Time.unscaledTime - _lastPlayTime >= minInterval;

    public void MarkPlayed() => _lastPlayTime = Time.unscaledTime;
}
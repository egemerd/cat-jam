using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Pool")]
    [SerializeField] private int initialPoolSize = 16;
    [SerializeField] private int maxPoolSize = 32;

    [Header("Defaults")]
    [SerializeField] private AudioMixerGroup defaultSfxGroup;

    private readonly Queue<AudioSource> _idle = new Queue<AudioSource>();
    private readonly List<AudioSource> _active = new List<AudioSource>();

    // Loop sahibi kayýtlarý: SoundSO -> hangi AudioSource'ta çalýyor
    private readonly Dictionary<SoundSO, AudioSource> _loops = new Dictionary<SoundSO, AudioSource>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        for (int i = 0; i < initialPoolSize; i++)
            _idle.Enqueue(CreateSource());
    }

    private void Update()
    {
        // Biten (loop olmayan) source'larý havuza geri koy
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var src = _active[i];
            if (!src.isPlaying && !src.loop)
            {
                _active.RemoveAt(i);
                src.gameObject.SetActive(false);
                _idle.Enqueue(src);
            }
        }
    }

    private AudioSource CreateSource()
    {
        var go = new GameObject("PooledAudioSource");
        go.transform.SetParent(transform);
        go.SetActive(false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        return src;
    }

    private AudioSource GetSource()
    {
        if (_idle.Count > 0) return _idle.Dequeue();
        if (_active.Count + _idle.Count < maxPoolSize) return CreateSource();

        // Havuz dolu: en eski, loop olmayan aktif source'u çal
        for (int i = 0; i < _active.Count; i++)
        {
            if (!_active[i].loop)
            {
                var s = _active[i];
                _active.RemoveAt(i);
                s.Stop();
                return s;
            }
        }
        return null; // Hepsi loop, yeni ses için yer yok
    }

    // ---------- Public API ----------

    public void Play(SoundSO sound) => PlayInternal(sound, Vector3.zero, false);
    public void PlayAt(SoundSO sound, Vector3 worldPos) => PlayInternal(sound, worldPos, true);

    private void PlayInternal(SoundSO sound, Vector3 pos, bool usePos)
    {
        if (sound == null || !sound.CanPlayNow()) return;

        var clip = sound.GetClip();
        if (clip == null) return;

        var src = GetSource();
        if (src == null) return;

        ConfigureSource(src, sound, clip, pos, usePos);
        src.loop = false;
        src.Play();

        _active.Add(src);
        sound.MarkPlayed();
    }

    public void PlayLoop(SoundSO sound)
    {
        if (sound == null || _loops.ContainsKey(sound)) return;

        var clip = sound.GetClip();
        if (clip == null) return;

        var src = GetSource();
        if (src == null) return;

        ConfigureSource(src, sound, clip, Vector3.zero, false);
        src.loop = true;
        src.Play();

        _active.Add(src);
        _loops[sound] = src;
    }

    public void StopLoop(SoundSO sound)
    {
        if (sound == null || !_loops.TryGetValue(sound, out var src)) return;
        src.Stop();
        src.loop = false; // Update tick'inde havuza döner
        _loops.Remove(sound);
    }

    private void ConfigureSource(AudioSource src, SoundSO s, AudioClip clip, Vector3 pos, bool usePos)
    {
        src.gameObject.SetActive(true);
        src.transform.position = usePos ? pos : Vector3.zero;

        src.clip = clip;
        src.volume = s.GetVolume();
        src.pitch = s.GetPitch();
        src.spatialBlend = s.spatialBlend;
        src.priority = s.priority;
        src.outputAudioMixerGroup = s.mixerGroup != null ? s.mixerGroup : defaultSfxGroup;
    }
}
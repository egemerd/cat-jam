using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central ball spawner. Every spawnInterval it picks ONE random BallSpawnPoint
/// from its list and tells that pipe to shoot a ball. The cat's pre-placed
/// balls plus everything spawned here are capped by maxBalls.
///
/// Setup:
///  - Add one of these to the scene (empty GameObject is fine).
///  - Drag your Ball prefab in.
///  - Place BallSpawnPoint GameObjects at every pipe mouth. Leave this
///    manager's 'spawn Points' list empty to auto-find them, or drag them in
///    manually if you only want certain pipes active.
///  - Tune maxBalls, spawnInterval, initialDelay.
/// </summary>
public class BallSpawnManager : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("The BounceBall prefab to spawn.")]
    public BounceBall ballPrefab;

    [Header("Pipes")]
    [Tooltip("Spawn points to pick from randomly. LEAVE EMPTY to auto-find every BallSpawnPoint in the scene on Start.")]
    public List<BallSpawnPoint> spawnPoints = new List<BallSpawnPoint>();

    [Header("Total cap (pre-placed + spawned)")]
    [Tooltip("Max balls allowed in the scene at once. Spawning pauses when hit.")]
    public int maxBalls = 10;

    [Header("Timing")]
    [Tooltip("Seconds between spawn attempts.")]
    public float spawnInterval = 2f;
    [Tooltip("Delay before the first spawn.")]
    public float initialDelay = 1f;
    [Tooltip("Begin the spawn timeline on Start. Turn OFF to trigger manually via StartSpawning().")]
    public bool spawnOnStart = true;

    [Header("Debug")]
    public bool debugLogs = false;

    private readonly List<BounceBall> _tracked = new List<BounceBall>();
    private float _spawnTimer;
    private bool _spawning;

    private void Start()
    {
        // If no pipes assigned, grab every BallSpawnPoint in the scene.
        if (spawnPoints.Count == 0)
            spawnPoints.AddRange(FindObjectsOfType<BallSpawnPoint>());

        // Count balls already in the scene — they go toward the cap.
        BounceBall[] existing = FindObjectsOfType<BounceBall>();
        for (int i = 0; i < existing.Length; i++)
            _tracked.Add(existing[i]);

        if (debugLogs)
            Debug.Log($"[BallSpawnManager] {spawnPoints.Count} pipes, {_tracked.Count} pre-placed balls, cap {maxBalls}.", this);

        if (spawnOnStart)
        {
            _spawning = true;
            _spawnTimer = initialDelay;
        }
    }

    private void Update()
    {
        // Prune destroyed balls so cap matches reality.
        for (int i = _tracked.Count - 1; i >= 0; i--)
            if (_tracked[i] == null) _tracked.RemoveAt(i);

        if (!_spawning) return;

        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer > 0f) return;

        if (_tracked.Count >= maxBalls)
        {
            // At cap — poll faster so we spawn right when space opens up.
            _spawnTimer = 0.25f;
            return;
        }

        SpawnOne();
        _spawnTimer = spawnInterval;
    }

    /// <summary>Pick a random pipe and spawn one ball from it. Returns the ball (or null if setup is incomplete).</summary>
    public BounceBall SpawnOne()
    {
        if (ballPrefab == null) { Debug.LogWarning("[BallSpawnManager] ballPrefab is not assigned.", this); return null; }
        if (spawnPoints.Count == 0) { Debug.LogWarning("[BallSpawnManager] No spawn points available.", this); return null; }

        // Random pipe, skipping any that have been destroyed.
        BallSpawnPoint pipe = null;
        for (int tries = 0; tries < spawnPoints.Count * 2 && pipe == null; tries++)
        {
            var candidate = spawnPoints[Random.Range(0, spawnPoints.Count)];
            if (candidate != null) pipe = candidate;
        }
        if (pipe == null) return null;

        BounceBall ball = pipe.Spawn(ballPrefab);
        if (ball != null) _tracked.Add(ball);

        if (debugLogs)
            Debug.Log($"[BallSpawnManager] Spawned from '{pipe.name}'. Count now {_tracked.Count}/{maxBalls}.", this);

        return ball;
    }

    public void StartSpawning()
    {
        _spawning = true;
        if (_spawnTimer <= 0f) _spawnTimer = initialDelay;
    }

    public void StopSpawning() => _spawning = false;

    /// <summary>Destroy every ball this manager knows about (includes pre-placed).</summary>
    public void ClearAll()
    {
        for (int i = _tracked.Count - 1; i >= 0; i--)
            if (_tracked[i] != null) Destroy(_tracked[i].gameObject);
        _tracked.Clear();
    }

    public int CurrentBallCount
    {
        get
        {
            int c = 0;
            for (int i = 0; i < _tracked.Count; i++)
                if (_tracked[i] != null) c++;
            return c;
        }
    }
}
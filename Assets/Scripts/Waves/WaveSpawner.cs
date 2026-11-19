using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// Runs a WaveSet: spawns each wave's enemies at the spawn points, waits for
// all of them to die, then moves on to the next wave. Stops spawning once the
// player is dead.
public sealed class WaveSpawner : MonoBehaviour
{
    [SerializeField] private WaveSet waveSet;
    [SerializeField] private Transform[] spawnPoints = Array.Empty<Transform>();
    [Tooltip("Spawn points closer to the player than this are skipped while " +
        "any farther one is left, so nothing appears on top of the player.")]
    [SerializeField, Min(0f)] private float minPlayerDistance = 8f;
    [Tooltip("How far from a spawn point the NavMesh may be found.")]
    [SerializeField, Min(0.1f)] private float navMeshSearchRadius = 2f;

    private Transform player;
    private Health playerHealth;
    // Time each spawn point was last used, so enemies of one wave spread
    // over the points instead of piling up on the same one.
    private float[] lastUsedTimes = Array.Empty<float>();

    // 0-based. -1 before the first wave starts.
    public int CurrentWaveIndex { get; private set; } = -1;
    public int WaveCount => waveSet != null ? waveSet.waves.Length : 0;
    // Enemies of the current wave not yet killed, including those still to spawn.
    public int RemainingCount { get; private set; }
    // Enemies of the current wave spawned and still alive.
    public int AliveCount { get; private set; }
    public bool IsCleared { get; private set; }

    public event Action<int> WaveStarted;
    public event Action<int> RemainingChanged;
    public event Action AllWavesCleared;

    private void Start()
    {
        if (PlayerReference.Instance != null)
        {
            player = PlayerReference.Instance.Target;
            playerHealth = player.GetComponent<Health>();
        }

        if (playerHealth != null)
        {
            playerHealth.Depleted += OnPlayerDepleted;
        }

        lastUsedTimes = new float[spawnPoints.Length];

        for (int i = 0; i < lastUsedTimes.Length; i++)
        {
            lastUsedTimes[i] = float.NegativeInfinity;
        }

        if (waveSet == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("WaveSpawner needs a wave set and spawn points.", this);
            return;
        }

        StartCoroutine(Run());
    }

    private void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.Depleted -= OnPlayerDepleted;
        }
    }

    private void OnPlayerDepleted()
    {
        StopAllCoroutines();
    }

    private IEnumerator Run()
    {
        if (waveSet.firstWaveDelay > 0f)
        {
            yield return new WaitForSeconds(waveSet.firstWaveDelay);
        }

        for (int i = 0; i < waveSet.waves.Length; i++)
        {
            yield return RunWave(i);

            if (i < waveSet.waves.Length - 1 && waveSet.timeBetweenWaves > 0f)
            {
                yield return new WaitForSeconds(waveSet.timeBetweenWaves);
            }
        }

        IsCleared = true;
        AllWavesCleared?.Invoke();
    }

    private IEnumerator RunWave(int waveIndex)
    {
        WaveSet.Wave wave = waveSet.waves[waveIndex];

        CurrentWaveIndex = waveIndex;
        RemainingCount = wave.EnemyCount;
        AliveCount = 0;
        WaveStarted?.Invoke(waveIndex);
        RemainingChanged?.Invoke(RemainingCount);

        bool first = true;

        for (int g = 0; g < wave.groups.Length; g++)
        {
            WaveSet.Group group = wave.groups[g];

            if (group == null || group.prefab == null)
            {
                continue;
            }

            if (group.startWhenAliveAtMost >= 0)
            {
                while (AliveCount > group.startWhenAliveAtMost)
                {
                    yield return null;
                }
            }

            for (int n = 0; n < group.count; n++)
            {
                if (!first && waveSet.spawnInterval > 0f)
                {
                    yield return new WaitForSeconds(waveSet.spawnInterval);
                }

                first = false;

                while (wave.maxAlive > 0 && AliveCount >= wave.maxAlive)
                {
                    yield return null;
                }

                if (!Spawn(group.prefab))
                {
                    // Counted as gone, or the wave could never be cleared.
                    DecreaseRemaining();
                }
            }
        }

        while (RemainingCount > 0)
        {
            yield return null;
        }
    }

    private bool Spawn(GameObject prefab)
    {
        int pointIndex = ChooseSpawnPoint();
        Vector3 point = spawnPoints[pointIndex].position;

        if (!NavMesh.SamplePosition(point, out NavMeshHit hit, navMeshSearchRadius, NavMesh.AllAreas))
        {
            Debug.LogWarning($"No NavMesh near spawn point {spawnPoints[pointIndex].name}.", this);
            return false;
        }

        lastUsedTimes[pointIndex] = Time.time;

        GameObject enemy = Instantiate(prefab, hit.position, FacePlayer(hit.position), transform);

        if (!enemy.TryGetComponent(out Health health))
        {
            Debug.LogWarning($"{prefab.name} has no Health; it can't be counted.", this);
            return false;
        }

        health.Depleted += DecreaseRemaining;
        health.Depleted += DecreaseAlive;
        AliveCount++;

        // Spawned outside its aggro range, it would otherwise just stand there.
        if (enemy.TryGetComponent(out EnemyAIController ai))
        {
            ai.Engage();
        }

        return true;
    }

    private void DecreaseAlive()
    {
        AliveCount = Mathf.Max(AliveCount - 1, 0);
    }

    private void DecreaseRemaining()
    {
        RemainingCount = Mathf.Max(RemainingCount - 1, 0);
        RemainingChanged?.Invoke(RemainingCount);
    }

    // The least recently used point that is far enough from the player; the
    // farthest point when none is.
    private int ChooseSpawnPoint()
    {
        int best = -1;
        int tieCount = 0;
        int farthest = 0;
        float farthestDistance = float.NegativeInfinity;

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            float distance = GetPlayerDistance(spawnPoints[i].position);

            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthest = i;
            }

            if (distance < minPlayerDistance)
            {
                continue;
            }

            if (best < 0 || lastUsedTimes[i] < lastUsedTimes[best])
            {
                best = i;
                tieCount = 1;
            }
            // Evenly random among equals, so the first wave doesn't always
            // start at the same point.
            else if (lastUsedTimes[i] == lastUsedTimes[best] &&
                UnityEngine.Random.Range(0, ++tieCount) == 0)
            {
                best = i;
            }
        }

        return best >= 0 ? best : farthest;
    }

    private float GetPlayerDistance(Vector3 position)
    {
        if (player == null)
        {
            return float.PositiveInfinity;
        }

        Vector3 offset = position - player.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private Quaternion FacePlayer(Vector3 position)
    {
        if (player == null)
        {
            return Quaternion.identity;
        }

        Vector3 direction = player.position - position;
        direction.y = 0f;

        return direction.sqrMagnitude <= Mathf.Epsilon
            ? Quaternion.identity
            : Quaternion.LookRotation(direction);
    }
}

using System;
using UnityEngine;

// The waves of one run, in order. A wave is cleared once every enemy in it
// is dead; the run is cleared with the last wave.
[CreateAssetMenu(menuName = "Waves/Wave Set", fileName = "WaveSet")]
public sealed class WaveSet : ScriptableObject
{
    [Serializable]
    public sealed class Group
    {
        public GameObject prefab;
        [Min(1)] public int count = 1;
        [Tooltip("Holds this group back until at most this many enemies of " +
            "the wave are alive, so it comes in as the one before thins " +
            "out. -1 starts it straight after the group before.")]
        [Min(-1)] public int startWhenAliveAtMost = -1;
    }

    [Serializable]
    public sealed class Wave
    {
        // Spawned group by group, in the order listed.
        public Group[] groups = Array.Empty<Group>();
        [Tooltip("Most enemies of this wave alive at once; the rest wait and " +
            "come in as others die. 0 = no limit.")]
        [Min(0)] public int maxAlive = 0;

        public int EnemyCount
        {
            get
            {
                int total = 0;

                for (int i = 0; i < groups.Length; i++)
                {
                    if (groups[i] != null && groups[i].prefab != null)
                    {
                        total += groups[i].count;
                    }
                }

                return total;
            }
        }
    }

    [Tooltip("From the start of the run to the first wave.")]
    [Min(0f)] public float firstWaveDelay = 1.5f;
    [Tooltip("From a wave being cleared to the next one starting.")]
    [Min(0f)] public float timeBetweenWaves = 3f;
    [Tooltip("Between two enemies of the same wave appearing.")]
    [Min(0f)] public float spawnInterval = 0.5f;

    public Wave[] waves = Array.Empty<Wave>();
}

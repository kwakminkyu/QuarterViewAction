using System.Collections.Generic;
using UnityEngine;

// Hands out attack tokens so only a few enemies go for the player at a time;
// the rest wait their turn around it. An enemy holds its token from setting off
// toward the player until the attack it went in for reaches its recovery; a
// reaction that cuts in hands it back as it starts.
// Optional: with none in the scene every enemy attacks whenever it likes.
public sealed class EnemyAttackDirector : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxTokens = 2;
    [Tooltip("Priority gained per second spent waiting for a token.")]
    [SerializeField, Min(0f)] private float waitWeight = 1f;
    [Tooltip("Priority lost per metre from the player.")]
    [SerializeField, Min(0f)] private float distanceWeight = 0.3f;

    private readonly List<EnemyAIController> holders = new();
    private readonly List<EnemyAIController> wanting = new();

    public static EnemyAttackDirector Instance { get; private set; }

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        holders.Clear();
        wanting.Clear();
    }

    public bool Holds(EnemyAIController enemy)
    {
        return holders.Contains(enemy);
    }

    // Asked again every frame for as long as the enemy wants one; the
    // tokens are handed out once all of this frame's asks are in.
    public void Request(EnemyAIController enemy)
    {
        if (!holders.Contains(enemy) && !wanting.Contains(enemy))
        {
            wanting.Add(enemy);
        }
    }

    public void Release(EnemyAIController enemy)
    {
        holders.Remove(enemy);
    }

    private void LateUpdate()
    {
        holders.RemoveAll(h => h == null || !h.isActiveAndEnabled);

        while (holders.Count < maxTokens && wanting.Count > 0)
        {
            int best = 0;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < wanting.Count; i++)
            {
                EnemyAIController candidate = wanting[i];

                if (candidate == null || !candidate.isActiveAndEnabled)
                {
                    continue;
                }

                float score = waitWeight * candidate.TokenWaitTime -
                    distanceWeight * candidate.TargetDistance;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (float.IsNegativeInfinity(bestScore))
            {
                break;
            }

            holders.Add(wanting[best]);
            wanting.RemoveAt(best);
        }

        wanting.Clear();
    }
}

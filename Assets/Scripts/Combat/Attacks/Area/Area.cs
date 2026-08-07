using System;
using UnityEngine;

public sealed class Area : MonoBehaviour
{
    private AttackContext attackContext;
    private AreaAttackData attackData;
    private OverlapAttack overlapAttack;
    private float remainingLifetime;
    private float tickElapsedTime;
    private bool isInitialized;
    private SkillDebugView debugView;

    public void Initialize(
        in AttackContext context,
        AreaAttackData data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        attackContext = context;
        attackData = data;
        overlapAttack = new OverlapAttack();
        remainingLifetime = data.lifetime;
        tickElapsedTime = 0f;
        debugView = context.Attacker == null
            ? null
            : context.Attacker.GetComponent<SkillDebugView>();
        isInitialized = true;

        ExecuteTick();
    }

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        if (remainingLifetime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        float elapsedTime = Mathf.Min(
            Time.deltaTime,
            remainingLifetime);
        float tickInterval = Mathf.Max(
            AreaAttackData.MinimumTickInterval,
            attackData.tickInterval);

        remainingLifetime -= elapsedTime;
        tickElapsedTime += elapsedTime;

        while (tickElapsedTime >= tickInterval)
        {
            tickElapsedTime -= tickInterval;
            ExecuteTick();
        }

        if (remainingLifetime <= 0f)
        {
            Destroy(gameObject);
        }
    }

    private void ExecuteTick()
    {
        int hitCount = overlapAttack.Execute(
            in attackContext,
            attackData,
            transform.position,
            transform.rotation);

        debugView?.ReportAreaTick(
            attackData,
            transform.position,
            transform.rotation,
            hitCount);
    }
}

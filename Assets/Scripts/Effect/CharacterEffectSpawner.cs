using System.Collections.Generic;
using UnityEngine;

// Owns every piece of runtime effect state for one character. SkillAction is a
// shared asset, so the actions describe what to play and this component is
// what actually plays it. Monsters get the same behaviour by adding it.
public sealed class CharacterEffectSpawner : MonoBehaviour
{
    [SerializeField] private SlashEffect slashPrefab;
    [SerializeField, Min(1)] private int poolSize = 4;

    // Where a weapon-following slash takes its position from. Authored on the
    // weapon so it sits at the swing pivot rather than the blade.
    [SerializeField] private string slashAnchorName = "SlashAnchor";

    private BladeRibbon bladeRibbon;
    private Transform slashAnchor;
    private Stack<SlashEffect> pool;

    // Slashes have to be reachable after they are handed out so the skill that
    // spawned them can end or cancel them. Without this the effect only ended
    // on its own clock and could outlive the motion.
    private readonly List<SlashEffect> active = new();

    private Transform poolRoot;

    private void Awake()
    {
        bladeRibbon = GetComponentInChildren<BladeRibbon>(true);

        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == slashAnchorName)
            {
                slashAnchor = child;
                break;
            }
        }
    }

    public void PlaySlash(
        in SlashEffectSettings settings,
        Vector3 direction)
    {
        if (!settings.enabled || slashPrefab == null)
        {
            return;
        }

        Vector3 facing = direction;
        facing.y = 0f;

        if (facing.sqrMagnitude <= Mathf.Epsilon)
        {
            facing = transform.forward;
            facing.y = 0f;
        }

        if (facing.sqrMagnitude <= Mathf.Epsilon)
        {
            facing = Vector3.forward;
        }

        facing = facing.normalized;

        // Same placement formula as OverlapSkillAction.ExecuteOverlapAttack so
        // the crescent lands exactly where the hitbox does.
        Quaternion rotation = Quaternion.LookRotation(facing, Vector3.up);
        Vector3 position = transform.position + rotation * settings.offset;

        rotation = ResolveSwingPlaneRotation(facing, in settings);

        SlashEffect slash = Rent();
        Transform slashTransform = slash.transform;

        // Never parented to the weapon: that would drag the anchor's rotation
        // and scale in too. Following is done as a position offset instead, so
        // the authored swing plane survives the blade's roll.
        slashTransform.SetParent(null, false);

        slashTransform.SetPositionAndRotation(
            position,
            rotation * Quaternion.Euler(settings.localEuler));

        active.Add(slash);
        slash.Play(
            this,
            in settings,
            settings.followWeapon ? slashAnchor : null);
    }

    // The active window closed: let every live slash fade out.
    public void EndSlash()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (active[i] != null)
            {
                active[i].End();
            }
        }
    }

    // The motion was cut short, so nothing it spawned may survive it.
    public void CancelSlash()
    {
        // Cancel calls back into Release, which removes from this list, so the
        // walk has to run backwards.
        for (int i = active.Count - 1; i >= 0; i--)
        {
            SlashEffect slash = active[i];

            if (slash == null)
            {
                active.RemoveAt(i);
                continue;
            }

            slash.Cancel();
        }

        active.Clear();
    }

    public void Release(SlashEffect slash)
    {
        if (slash == null)
        {
            return;
        }

        active.Remove(slash);

        pool ??= new Stack<SlashEffect>(poolSize);

        if (pool.Count >= poolSize)
        {
            Destroy(slash.gameObject);
            return;
        }

        slash.transform.SetParent(PoolRoot, false);
        pool.Push(slash);
    }

    public void BeginTrail(in BladeTrailSettings settings)
    {
        if (bladeRibbon != null)
        {
            bladeRibbon.Begin(in settings);
        }
    }

    public void EndTrail()
    {
        if (bladeRibbon != null)
        {
            bladeRibbon.End();
        }
    }

    // End() only starts the ribbon's fade and is ignored once that fade is
    // running, so cancelling needs its own path.
    public void CancelTrail()
    {
        if (bladeRibbon != null)
        {
            bladeRibbon.Cancel();
        }
    }

    // Lays the authored mesh into the plane the blade really sweeps through.
    // The normal is measured per clip and authored on the action, which is
    // what lets an overhead chop read as vertical - the earlier camera-facing
    // version flattened every attack into the screen plane and turned the
    // fourth combo's chop into a horizontal sweep.
    private Quaternion ResolveSwingPlaneRotation(
        Vector3 facing,
        in SlashEffectSettings settings)
    {
        Quaternion facingRotation = Quaternion.LookRotation(facing, Vector3.up);

        if (settings.swingPlaneNormal.sqrMagnitude <= Mathf.Epsilon)
        {
            return facingRotation;
        }

        // The normal is authored in the character's own frame, so it has to be
        // carried into world space through wherever the character is aiming.
        Vector3 worldNormal =
            (facingRotation * settings.swingPlaneNormal).normalized;

        // The mesh lies in its local XY plane, so local +Z has to become the
        // plane normal. Local up is taken from the aim direction projected
        // into the plane, which keeps the arc pointing where the attack does.
        Vector3 inPlaneAim = Vector3.ProjectOnPlane(facing, worldNormal);

        if (inPlaneAim.sqrMagnitude <= Mathf.Epsilon)
        {
            inPlaneAim = Vector3.ProjectOnPlane(Vector3.up, worldNormal);
        }

        if (inPlaneAim.sqrMagnitude <= Mathf.Epsilon)
        {
            return facingRotation;
        }

        return Quaternion.LookRotation(worldNormal, inPlaneAim.normalized);
    }

    private SlashEffect Rent()
    {
        pool ??= new Stack<SlashEffect>(poolSize);

        // Pooled entries can be destroyed out from under us when a detached
        // slash is cleaned up with the scene, so skip the dead ones.
        while (pool.Count > 0)
        {
            SlashEffect pooled = pool.Pop();

            if (pooled != null)
            {
                return pooled;
            }
        }

        return Instantiate(slashPrefab);
    }

    private Transform PoolRoot
    {
        get
        {
            if (poolRoot == null)
            {
                poolRoot = new GameObject("SlashPool").transform;
                poolRoot.SetParent(transform, false);
            }

            return poolRoot;
        }
    }
}

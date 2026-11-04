using System.Collections.Generic;
using UnityEngine;

// Leaves copies of the character's body behind it at a fixed interval while
// the effect emits - the afterimages along a dash. Each copy is the posed
// body frozen where it was, so the trail of them follows the path the
// character really took and the pose it took it in. Part of a prefab effect,
// so it starts, stops and clears with the rest of it.
public sealed class AfterimageEmitter : MonoBehaviour, IEffectPart
{
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    [SerializeField] private Material material;

    // Seconds between copies. A dash covers about 0.4m in 0.03s, which keeps
    // neighbouring copies just touching.
    [SerializeField, Min(0.005f)] private float interval = 0.03f;

    [SerializeField, Min(0.01f)] private float lifetime = 0.25f;

    // Over each copy's own life, 0 as it is left behind, 1 as it goes.
    [SerializeField] private AnimationCurve alphaOverLife =
        AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    // Copies taken at most per effect; the oldest is reused past this.
    [SerializeField, Min(1)] private int maxCopies = 12;

    private struct Copy
    {
        public Mesh mesh;
        public Matrix4x4 matrix;
        public float bornTime;

        // Baked from a skinned body and owned here, rather than a weapon's
        // shared mesh, which must never be destroyed.
        public bool owned;
    }

    private readonly List<Copy> copies = new();
    private readonly Stack<Mesh> spareMeshes = new();
    private readonly List<SkinnedMeshRenderer> bodies = new();
    private readonly List<MeshFilter> props = new();
    private MaterialPropertyBlock block;
    private float nextCopyTime;
    private bool isEmitting;

    public bool IsAlive => isEmitting || copies.Count > 0;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
    }

    public void Play(CharacterEffectSpawner owner)
    {
        ClearCopies();
        bodies.Clear();
        props.Clear();

        if (owner == null || material == null)
        {
            isEmitting = false;
            return;
        }

        foreach (SkinnedMeshRenderer body in
            owner.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (body.enabled && body.sharedMesh != null)
            {
                bodies.Add(body);
            }
        }

        // Rigid meshes carried by the body - the sword.
        foreach (MeshFilter prop in owner.GetComponentsInChildren<MeshFilter>())
        {
            var propRenderer = prop.GetComponent<MeshRenderer>();

            if (propRenderer != null && propRenderer.enabled &&
                prop.sharedMesh != null)
            {
                props.Add(prop);
            }
        }

        isEmitting = true;
        nextCopyTime = Time.time;
    }

    public void StopEmitting()
    {
        isEmitting = false;
    }

    public void Clear()
    {
        isEmitting = false;
        ClearCopies();
    }

    private void OnDisable()
    {
        Clear();
    }

    private void OnDestroy()
    {
        ClearCopies();

        while (spareMeshes.Count > 0)
        {
            Destroy(spareMeshes.Pop());
        }
    }

    // After the Animator has posed the body for the frame, so each copy holds
    // the pose that is on screen.
    private void LateUpdate()
    {
        float now = Time.time;

        if (isEmitting && now >= nextCopyTime)
        {
            TakeCopies(now);

            // From now rather than from the missed slot: a hitch should leave
            // one copy, not a pile of them in the same place.
            nextCopyTime = now + interval;
        }

        for (int i = copies.Count - 1; i >= 0; i--)
        {
            if (now - copies[i].bornTime >= lifetime)
            {
                Drop(i);
            }
        }

        if (copies.Count == 0)
        {
            return;
        }

        var renderParams = new RenderParams(material)
        {
            layer = gameObject.layer,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
            matProps = block,
        };

        for (int i = 0; i < copies.Count; i++)
        {
            Copy copy = copies[i];
            float age = Mathf.Clamp01((now - copy.bornTime) / lifetime);

            // The block is copied into each draw, so one can be reused.
            block.SetFloat(AlphaId, alphaOverLife.Evaluate(age));

            for (int sub = 0; sub < copy.mesh.subMeshCount; sub++)
            {
                Graphics.RenderMesh(in renderParams, copy.mesh, sub, copy.matrix);
            }
        }
    }

    private void TakeCopies(float now)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            SkinnedMeshRenderer body = bodies[i];

            if (body == null)
            {
                continue;
            }

            Mesh mesh = spareMeshes.Count > 0 ? spareMeshes.Pop() : CreateMesh();

            // Baked with the transform's scale, so only its position and
            // rotation are left to place it.
            body.BakeMesh(mesh, true);
            Transform t = body.transform;
            Add(new Copy
            {
                mesh = mesh,
                matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one),
                bornTime = now,
                owned = true,
            });
        }

        for (int i = 0; i < props.Count; i++)
        {
            MeshFilter prop = props[i];

            if (prop == null)
            {
                continue;
            }

            Add(new Copy
            {
                mesh = prop.sharedMesh,
                matrix = prop.transform.localToWorldMatrix,
                bornTime = now,
                owned = false,
            });
        }
    }

    private void Add(Copy copy)
    {
        int limit = maxCopies * Mathf.Max(1, bodies.Count + props.Count);

        if (copies.Count >= limit)
        {
            Drop(0);
        }

        copies.Add(copy);
    }

    private void Drop(int index)
    {
        Copy copy = copies[index];

        if (copy.owned && copy.mesh != null)
        {
            spareMeshes.Push(copy.mesh);
        }

        copies.RemoveAt(index);
    }

    private void ClearCopies()
    {
        for (int i = copies.Count - 1; i >= 0; i--)
        {
            Drop(i);
        }
    }

    private static Mesh CreateMesh()
    {
        var mesh = new Mesh { name = "Afterimage (baked)" };
        mesh.MarkDynamic();
        return mesh;
    }
}

// A piece of a prefab effect that is not a particle system - a trail, an
// afterimage - played by ParticleEffect under the same rules as the particle
// systems beside it, so a prefab can mix both.
public interface IEffectPart
{
    // The effect started. The owner is the character that spawned it, for
    // parts that need its body (afterimages bake its mesh).
    void Play(CharacterEffectSpawner owner);

    // Nothing new appears from now on; what is already out plays out.
    void StopEmitting();

    // Everything is gone at once.
    void Clear();

    // Still showing anything, or about to.
    bool IsAlive { get; }
}

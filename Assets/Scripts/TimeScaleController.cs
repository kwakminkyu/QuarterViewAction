using UnityEngine;

// The only thing that sets Time.timeScale. Anything wanting the game slowed
// asks here, so two requests can never leave the clock stuck at the wrong
// speed. Lives in the scene, created on first use, and puts the clock back to
// normal when the scene goes away - a restart never inherits a slow-down.
public sealed class TimeScaleController : MonoBehaviour
{
    private static TimeScaleController instance;

    private float slowScale = 1f;
    private float holdRemaining;
    private float recoverDuration;
    private float recoverElapsed;
    private bool isSlowed;

    // Slows the game to `scale` for `hold` real seconds, then eases back to
    // normal over `recover` real seconds. A new request while one is running
    // replaces it, keeping whichever speed is slower.
    public static void SlowMotion(float scale, float hold, float recover)
    {
        Instance.Begin(scale, hold, recover);
    }

    private static TimeScaleController Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new GameObject("TimeScale")
                    .AddComponent<TimeScaleController>();
            }

            return instance;
        }
    }

    // Survives the editor's fast enter-play-mode option, which skips the
    // domain reload that would otherwise clear the static.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStatic()
    {
        instance = null;
    }

    private void Begin(float scale, float hold, float recover)
    {
        scale = Mathf.Clamp(scale, 0.01f, 1f);

        slowScale = isSlowed ? Mathf.Min(slowScale, scale) : scale;
        holdRemaining = hold;
        recoverDuration = recover;
        recoverElapsed = 0f;
        isSlowed = true;
        Time.timeScale = slowScale;
    }

    private void Update()
    {
        if (!isSlowed)
        {
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;

        if (holdRemaining > 0f)
        {
            holdRemaining -= deltaTime;
            Time.timeScale = slowScale;
            return;
        }

        recoverElapsed += deltaTime;

        if (recoverElapsed < recoverDuration)
        {
            Time.timeScale = Mathf.Lerp(
                slowScale,
                1f,
                recoverElapsed / recoverDuration);
            return;
        }

        Restore();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            Restore();
            instance = null;
        }
    }

    private void Restore()
    {
        isSlowed = false;
        Time.timeScale = 1f;
    }
}

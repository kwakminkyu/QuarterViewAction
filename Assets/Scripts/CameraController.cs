using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform target;

    [Header("Shake")]
    [SerializeField, Min(0.01f)] private float shakeDuration = 0.2f;
    // How fast the shake wanders; higher is more of a rattle, lower a sway.
    [SerializeField, Min(0f)] private float shakeFrequency = 25f;

    private static CameraController current;

    private Vector3 initialOffset;
    private float shakeAmplitude;
    private float shakeRemaining;

    private void Awake()
    {
        initialOffset = transform.position - target.position;
    }

    private void OnEnable()
    {
        current = this;
    }

    private void OnDisable()
    {
        if (current == this)
        {
            current = null;
        }
    }

    // Shakes the following camera, strongest at once and dying away over the
    // shake duration. A weaker shake arriving mid-shake is ignored, so several
    // hits landing together shake it once.
    public static void Shake(float amplitude)
    {
        if (current != null)
        {
            current.StartShake(amplitude);
        }
    }

    private void StartShake(float amplitude)
    {
        if (amplitude < CurrentShakeStrength())
        {
            return;
        }

        shakeAmplitude = amplitude;
        shakeRemaining = shakeDuration;
    }

    private void LateUpdate()
    {
        transform.position = target.position + initialOffset + UpdateShake();
    }

    private Vector3 UpdateShake()
    {
        if (shakeRemaining <= 0f)
        {
            return Vector3.zero;
        }

        // Real time, so a slow-motion moment doesn't drag the shake out.
        shakeRemaining -= Time.unscaledDeltaTime;
        float strength = CurrentShakeStrength();

        // Smooth noise rather than a random jump per frame, across the view
        // so it reads the same at any camera angle.
        float time = Time.unscaledTime * shakeFrequency;
        float x = Mathf.PerlinNoise(time, 0f) * 2f - 1f;
        float y = Mathf.PerlinNoise(0f, time + 7.3f) * 2f - 1f;

        return (transform.right * x + transform.up * y) * strength;
    }

    private float CurrentShakeStrength()
    {
        return shakeRemaining > 0f
            ? shakeAmplitude * (shakeRemaining / shakeDuration)
            : 0f;
    }
}

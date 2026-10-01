using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Shows which wave is on and how many of its monsters are left, and flashes
// the wave number across the screen as each wave starts.
public sealed class WaveHudView : MonoBehaviour
{
    [SerializeField] private WaveSpawner spawner;
    [SerializeField] private Text waveLabel;
    [SerializeField] private Text remainingLabel;
    [Tooltip("Optional. Shown for a moment as each wave starts.")]
    [SerializeField] private Text banner;
    [SerializeField, Min(0f)] private float bannerDuration = 1.5f;

    private Coroutine bannerRoutine;

    private void Awake()
    {
        // Nothing to show until the first wave starts.
        waveLabel.gameObject.SetActive(false);
        remainingLabel.gameObject.SetActive(false);

        if (banner != null)
        {
            banner.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        spawner.WaveStarted += OnWaveStarted;
        spawner.RemainingChanged += OnRemainingChanged;
    }

    private void OnDisable()
    {
        spawner.WaveStarted -= OnWaveStarted;
        spawner.RemainingChanged -= OnRemainingChanged;
    }

    private void OnWaveStarted(int waveIndex)
    {
        string text = $"WAVE {waveIndex + 1} / {spawner.WaveCount}";

        waveLabel.text = text;
        waveLabel.gameObject.SetActive(true);
        remainingLabel.gameObject.SetActive(true);

        if (banner != null)
        {
            banner.text = waveIndex == spawner.WaveCount - 1
                ? "FINAL WAVE"
                : $"WAVE {waveIndex + 1}";

            if (bannerRoutine != null)
            {
                StopCoroutine(bannerRoutine);
            }

            bannerRoutine = StartCoroutine(ShowBanner());
        }
    }

    private void OnRemainingChanged(int remaining)
    {
        remainingLabel.text = $"ENEMIES {remaining}";
    }

    private IEnumerator ShowBanner()
    {
        banner.gameObject.SetActive(true);
        yield return new WaitForSeconds(bannerDuration);
        banner.gameObject.SetActive(false);
        bannerRoutine = null;
    }
}

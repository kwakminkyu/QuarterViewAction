using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Ends the run: shows the game-over panel a moment after the player dies, or
// the clear panel once every wave is beaten, and restarts the run by
// reloading the scene, which puts every character, bar and pool back as the
// scene was authored.
public sealed class GameOverScreen : MonoBehaviour
{
    [SerializeField] private Health playerHealth;
    [SerializeField] private GameObject panel;
    [Tooltip("Optional. Without it the run only ends when the player dies.")]
    [SerializeField] private WaveSpawner waveSpawner;
    [SerializeField] private GameObject clearPanel;
    [Tooltip("Lets the fall play out before the panel covers it.")]
    [SerializeField, Min(0f)] private float showDelay = 1.5f;

    // Its own action rather than one on the player's PlayerInput, which the
    // player's death switches off.
    [SerializeField] private InputAction restartAction = new(
        "Restart",
        InputActionType.Button,
        "<Keyboard>/r");

    private bool ended;

    private void Awake()
    {
        panel.SetActive(false);

        if (clearPanel != null)
        {
            clearPanel.SetActive(false);
        }
    }

    private void OnEnable()
    {
        playerHealth.Depleted += OnPlayerDepleted;
        restartAction.performed += OnRestart;

        if (waveSpawner != null)
        {
            waveSpawner.AllWavesCleared += OnAllWavesCleared;
        }
    }

    private void OnDisable()
    {
        playerHealth.Depleted -= OnPlayerDepleted;
        restartAction.performed -= OnRestart;
        restartAction.Disable();

        if (waveSpawner != null)
        {
            waveSpawner.AllWavesCleared -= OnAllWavesCleared;
        }
    }

    private void OnPlayerDepleted()
    {
        End(panel);
    }

    private void OnAllWavesCleared()
    {
        End(clearPanel);
    }

    // Whichever comes first is how the run ended.
    private void End(GameObject shownPanel)
    {
        if (ended || shownPanel == null)
        {
            return;
        }

        ended = true;
        StartCoroutine(Show(shownPanel));
    }

    private IEnumerator Show(GameObject shownPanel)
    {
        if (showDelay > 0f)
        {
            yield return new WaitForSeconds(showDelay);
        }

        shownPanel.SetActive(true);

        // Only listened for once the panel is up, so a key pressed in the
        // fight can't restart it.
        restartAction.Enable();
    }

    private void OnRestart(InputAction.CallbackContext context)
    {
        Restart();
    }

    public void Restart()
    {
        restartAction.Disable();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}

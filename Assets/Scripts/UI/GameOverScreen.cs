using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Shows the game-over panel a moment after the player dies and restarts the
// run by reloading the scene, which puts every character, bar and pool back
// as the scene was authored.
public sealed class GameOverScreen : MonoBehaviour
{
    [SerializeField] private Health playerHealth;
    [SerializeField] private GameObject panel;
    [Tooltip("Lets the fall play out before the panel covers it.")]
    [SerializeField, Min(0f)] private float showDelay = 1.5f;

    // Its own action rather than one on the player's PlayerInput, which the
    // player's death switches off.
    [SerializeField] private InputAction restartAction = new(
        "Restart",
        InputActionType.Button,
        "<Keyboard>/r");

    private void Awake()
    {
        panel.SetActive(false);
    }

    private void OnEnable()
    {
        playerHealth.Depleted += OnPlayerDepleted;
        restartAction.performed += OnRestart;
    }

    private void OnDisable()
    {
        playerHealth.Depleted -= OnPlayerDepleted;
        restartAction.performed -= OnRestart;
        restartAction.Disable();
    }

    private void OnPlayerDepleted()
    {
        StartCoroutine(Show());
    }

    private IEnumerator Show()
    {
        if (showDelay > 0f)
        {
            yield return new WaitForSeconds(showDelay);
        }

        panel.SetActive(true);

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

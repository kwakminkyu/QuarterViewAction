using UnityEngine;

public sealed class PlayerReference : MonoBehaviour
{
    public static PlayerReference Instance { get; private set; }

    public Transform Target => transform;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}

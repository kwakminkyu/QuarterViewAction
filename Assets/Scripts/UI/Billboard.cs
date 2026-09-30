using UnityEngine;

// Keeps a world-space UI element square to the camera, so a health bar over a
// character reads the same whichever way the character is facing.
public sealed class Billboard : MonoBehaviour
{
    private Transform cameraTransform;

    private void Start()
    {
        Camera mainCamera = Camera.main;
        cameraTransform = mainCamera != null ? mainCamera.transform : null;
    }

    // After the character has turned and the camera has followed.
    private void LateUpdate()
    {
        if (cameraTransform != null)
        {
            transform.rotation = cameraTransform.rotation;
        }
    }
}

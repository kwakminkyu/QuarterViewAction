using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform target;

    private Vector3 initialOffset;

    private void Awake()
    {
        initialOffset = transform.position - target.position;
    }

    private void LateUpdate()
    {
        transform.position = target.position + initialOffset;
    }
}

using UnityEngine;

public class Billboard : MonoBehaviour
{
    public Transform cameraTransform;

    void LateUpdate()
    {
        cameraTransform = Camera.main.transform;
        // Поворачиваем объект так, чтобы его передняя сторона смотрела на камеру
        transform.LookAt(transform.position + cameraTransform.rotation * Vector3.forward,
                         cameraTransform.rotation * Vector3.up);
    }
}
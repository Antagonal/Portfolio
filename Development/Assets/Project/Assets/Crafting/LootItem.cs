using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class LootItem : MonoBehaviour
{
    public ResourceType resourceType;
    public int amount;
    public float rotationSpeed = 30f;

    void Start()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        // Небольшой случайный толчок при падении
        rb.AddForce(Random.insideUnitSphere * 2f, ForceMode.Impulse);

        // Убеждаемся, что коллайдер физический (не триггер)
        Collider col = GetComponent<Collider>();
        col.isTrigger = false;
    }

    void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
    }

    void OnCollisionEnter(Collision collision)
    {
        PlayerAI player = collision.collider.GetComponent<PlayerAI>();
        if (player != null)
        {
            player.AddResource(resourceType, amount);
            Destroy(gameObject);
        }
    }
}
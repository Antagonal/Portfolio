using UnityEngine;
using System.Collections.Generic;

public class ObjectData : MonoBehaviour
{
    [Header("Размеры объекта в тайлах")]
    public int width = 1;
    public int length = 1;
    public int heightInTiles = 0;
    public int rotation;

    [Header("Тип объекта")]
    public ObjectType type; // добавляем поле для типа

    [Header("Проходимость")]
    public bool canMoveTo = false;
    public bool isDifficultTerrain = false;

    [Header("Разрушение")]
    public bool isDestructible = false;
    public int maxHealth = 1;
    private int currentHealth;

    [Header("Ресурс при разрушении")]
    public ResourceType resourceType = ResourceType.None;
    public int resourceAmount = 0;

    [Header("Лестница")]
    public bool isLadder = false;

    [Header("Ловушка")]
    public bool isTrap = false;
    public int trapDamage = 1;

    // Список занимаемых клеток (заполняется при спавне)
    public List<Vector3Int> occupiedCells = new List<Vector3Int>();

    // Событие вызывается при разрушении
    public System.Action<Vector3, ObjectData> OnDestroyed;

    void Start() => currentHealth = maxHealth;

    public void TakeDamage(int damage)
    {
        if (!isDestructible) return;
        currentHealth -= damage;
        if (currentHealth <= 0) DestroyObject();
    }

    public void Heal(int amount)
    {
        if (!isDestructible) return;
        currentHealth += amount;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
        Debug.Log($"{name} вылечен на {amount}, здоровье {currentHealth}/{maxHealth}");
    }

    void DestroyObject()
    {
        OnDestroyed?.Invoke(transform.position, this);
        Destroy(gameObject);
    }

    public List<Vector3Int> GetOccupiedCells(Vector3Int baseCell, int rotation)
    {
        List<Vector3Int> cells = new List<Vector3Int>();
        for (int dx = 0; dx < width; dx++)
            for (int dy = 0; dy < length; dy++)
            {
                Vector2Int local = new Vector2Int(dx, dy);
                Vector2Int rotated = RotateLocal(local, rotation);
                cells.Add(new Vector3Int(baseCell.x + rotated.x, baseCell.y + rotated.y, 0));
            }
        return cells;
    }

    Vector2Int RotateLocal(Vector2Int local, int rotation)
    {
        switch (rotation)
        {
            case 0: return local;
            case 1: return new Vector2Int(-local.y, local.x);
            case 2: return new Vector2Int(-local.x, -local.y);
            case 3: return new Vector2Int(local.y, -local.x);
            default: return local;
        }
    }
}
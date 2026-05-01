using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class Inventory
{
    [SerializeField] private Dictionary<ResourceType, int> resources = new Dictionary<ResourceType, int>();

    public void AddResource(ResourceType type, int amount)
    {
        if (type == ResourceType.None || amount <= 0) return;
        if (!resources.ContainsKey(type))
            resources[type] = 0;
        resources[type] += amount;
        Debug.Log($"Добавлено {amount} {type}. Теперь: {resources[type]}");
    }

    public int GetResource(ResourceType type)
    {
        return resources.ContainsKey(type) ? resources[type] : 0;
    }

    public string GetInventoryText()
    {
        string result = "";
        foreach (var kv in resources)
        {
            result += $"{kv.Key}: {kv.Value}\n";
        }
        return result;
    }
}
using UnityEngine;
using System.Collections.Generic;

public class GridBuilder : MonoBehaviour
{
    [Header("Префабы тайлов (индекс соответствует TileType)")]
    public GameObject[] tilePrefabs;

    [Header("Префабы объектов (индекс соответствует ObjectType)")]
    public GameObject[] objectPrefabs;

    // Эти поля оставлены для совместимости, но всегда равны 1
    public float stepSize = 1f;
    [SerializeField] public float heightStep = 1f;

    public Dictionary<Vector2Int, GameObject> locationContainers { get; private set; } = new Dictionary<Vector2Int, GameObject>();

    private TileMapData currentData;
    private Vector2Int currentGlobalPos;
    private LocationContainer currentLocationContainer;

    void Start()
    {
        if (LocationManager.Instance != null && LocationManager.Instance.currentMap != null)
            BuildLocation(LocationManager.Instance.currentMap, LocationManager.Instance.currentMap.globalPosition);
    }

    public void BuildLocation(TileMapData data, Vector2Int globalPosition)
    {
        if (data == null) { Debug.LogError("Нет данных!"); return; }

        if (locationContainers.ContainsKey(globalPosition))
        {
            Destroy(locationContainers[globalPosition]);
            locationContainers.Remove(globalPosition);
        }

        // Размер локации в мировых единицах равен количеству клеток (так как клетка 1x1)
        float locationSizeX = data.width;
        float locationSizeZ = data.height;

        Vector3 locationOffset = new Vector3(
            globalPosition.x * locationSizeX,
            data.elevation, // elevation теперь просто смещение по Y (количество блоков)
            globalPosition.y * locationSizeZ
        );

        GameObject container = new GameObject($"Location_{globalPosition.x}_{globalPosition.y}");
        container.transform.SetParent(transform);
        container.transform.position = locationOffset;

        LocationContainer locContainer = container.AddComponent<LocationContainer>();
        locationContainers[globalPosition] = container;

        // Тайлы
        for (int x = 0; x < data.width; x++)
            for (int y = 0; y < data.height; y++)
            {
                TileType type = data.GetTileType(x, y);
                int height = data.GetHeight(x, y);
                CreateTileAt(x, y, 0, type, container, locationOffset);
                for (int h = 1; h <= height; h++)
                    CreateTileAt(x, y, h, type, container, locationOffset);
            }

        // Фундамент (подземные блоки)
        if (data.elevation > 0)
            for (int x = 0; x < data.width; x++)
                for (int y = 0; y < data.height; y++)
                {
                    TileType type = data.GetTileType(x, y);
                    for (int h = -data.elevation; h < 0; h++)
                        CreateTileAt(x, y, h, type, container, locationOffset);
                }

        // Объекты
        SpawnObjects(data.objects, container, locationOffset, data, locContainer);

        if (globalPosition == LocationManager.Instance?.currentMap?.globalPosition)
        {
            currentData = data;
            currentGlobalPos = globalPosition;
            currentLocationContainer = locContainer;
        }
    }

    void CreateTileAt(int x, int y, int height, TileType type, GameObject container, Vector3 locationOffset)
    {
        // Позиция: X = x + offset.x, Y = offset.y + height (так как каждый блок высотой 1), Z = y + offset.z
        Vector3 position = new Vector3(
            x + locationOffset.x,
            locationOffset.y + height,
            y + locationOffset.z
        );

        GameObject prefab = tilePrefabs[(int)type];
        if (prefab == null) { Debug.LogError($"Нет префаба для {type}"); return; }

        GameObject tile = Instantiate(prefab, position, Quaternion.identity);
        tile.name = $"Tile_{type}_{x}_{y}_H{height}";
        tile.transform.SetParent(container.transform);

        if (height > 0)
        {
            Renderer renderer = tile.GetComponent<Renderer>();
            if (renderer != null)
            {
                Color color = renderer.material.color;
                float factor = (height == 1) ? 0.9f : 0.8f;
                renderer.material.color = new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
            }
        }
    }

    void SpawnObjects(List<ObjectPlacement> objects, GameObject container, Vector3 locationOffset, TileMapData data, LocationContainer locContainer)
    {
        foreach (var obj in objects)
        {
            if (obj.x < 0 || obj.x >= data.width || obj.y < 0 || obj.y >= data.height) continue;

            int height = data.GetHeight(obj.x, obj.y);
            Vector3 position = new Vector3(
                obj.x + locationOffset.x,
                locationOffset.y + height + 0.5f, // объект стоит на блоке, центр на полблока выше
                obj.y + locationOffset.z
            );

            GameObject prefab = objectPrefabs[(int)obj.type];
            if (prefab == null) { Debug.LogError($"Нет префаба для {obj.type}"); continue; }

            Quaternion rotation = Quaternion.Euler(0, -obj.rotation * 90, 0);
            GameObject spawned = Instantiate(prefab, position, rotation);
            spawned.name = $"{obj.type}_{obj.x}_{obj.y}";
            spawned.transform.SetParent(container.transform);

            ObjectData objData = spawned.GetComponent<ObjectData>();
            if (objData != null)
            {
                objData.rotation = obj.rotation;
                objData.OnDestroyed += OnObjectDestroyed;
                Vector3Int baseCell = new Vector3Int(obj.x, obj.y, 0);
                List<Vector3Int> cells = objData.GetOccupiedCells(baseCell, obj.rotation);
                objData.occupiedCells = cells;
                foreach (var cell in cells)
                {
                    if (!locContainer.occupiedObjects.ContainsKey(cell))
                        locContainer.occupiedObjects.Add(cell, objData);
                    else
                        Debug.LogWarning($"Клетка {cell} уже занята");
                }
            }
        }
    }

    private void OnObjectDestroyed(Vector3 worldPos, ObjectData objData)
{
    if (currentLocationContainer != null)
        foreach (var cell in objData.occupiedCells)
            currentLocationContainer.occupiedObjects.Remove(cell);

    if (objData.resourceType != ResourceType.None && objData.resourceAmount > 0)
    {
        Vector3Int cell = LocationManager.Instance.WorldToCell(worldPos);
        Vector3 spawnPos = LocationManager.Instance.CellToWorld(cell);
        spawnPos.y += 1.5f; // выше, чтобы упасть
        if (LocationManager.Instance.lootPrefab != null)
        {
            GameObject loot = Instantiate(LocationManager.Instance.lootPrefab, spawnPos, Quaternion.identity);
            LootItem lootItem = loot.GetComponent<LootItem>();
            if (lootItem != null)
            {
                lootItem.resourceType = objData.resourceType;
                lootItem.amount = objData.resourceAmount;
            }
        }
    }
}

public Vector3 CellToWorld(Vector3Int cellPosition) => LocationManager.Instance.CellToWorld(cellPosition);
public Vector3Int WorldToCell(Vector3 worldPosition) => LocationManager.Instance.WorldToCell(worldPosition);
public ObjectData GetObjectAt(Vector3Int cell) => LocationManager.Instance.GetObjectAt(cell);
public bool IsInBounds(Vector3Int cell) => LocationManager.Instance.IsInBounds(cell);
public int GetHeightAt(int x, int y) => LocationManager.Instance.GetHeightAt(x, y);
public float GetTileHeight(Vector3Int cell) => LocationManager.Instance.GetTileHeight(cell);
public bool CanMoveBetweenCells(Vector3Int fromCell, Vector3Int toCell) => LocationManager.Instance.CanMoveBetweenCells(fromCell, toCell);

    public TileMapData CurrentData => currentData;
}
using UnityEngine;
using System.Collections.Generic;

public class EnemySpawner : MonoBehaviour
{
    [Header("Префабы врагов (индекс соответствует EnemyType)")]
    public GameObject[] enemyPrefabs;

    private GridBuilder gridBuilder;
    private List<GameObject> spawnedEnemies = new List<GameObject>();

    void Start()
    {
        gridBuilder = FindObjectOfType<GridBuilder>();
        if (LocationManager.Instance != null)
            LocationManager.Instance.OnLocationLoaded += OnLocationLoaded;
    }

    void OnDestroy()
    {
        if (LocationManager.Instance != null)
            LocationManager.Instance.OnLocationLoaded -= OnLocationLoaded;
    }

    void OnLocationLoaded(TileMapData mapData)
    {
        if (mapData != LocationManager.Instance.currentMap)
            return;

        // Уничтожаем старых врагов
        foreach (var enemy in spawnedEnemies)
            if (enemy != null) Destroy(enemy);
        spawnedEnemies.Clear();

        // Сбрасываем счётчик (враги ещё не заспавнены, поэтому 0)
        LocationManager.Instance.ResetEnemyCount();

        // Если локация уже зачищена – ничего не делаем
        if (mapData.encounterCleared)
        {
            Debug.Log($"Локация {mapData.locationId} уже зачищена");
            return;
        }

        // Спавн не вызываем – он будет после входа игрока
    }

    // Публичный метод для спавна врагов в текущей локации
    public void SpawnEncounterForCurrentLocation()
    {
        TileMapData mapData = LocationManager.Instance.currentMap;
        if (mapData == null) return;
        if (mapData.encounterCleared)
        {
            Debug.Log($"Локация {mapData.locationId} уже зачищена, спавн не требуется");
            return;
        }
        if (mapData.encounter != null)
            SpawnEnemiesFromEncounter(mapData);
        LocationManager.Instance?.OnEnemiesSpawned();
    }

    void SpawnEnemiesFromEncounter(TileMapData mapData)
    {
        if (gridBuilder == null) return;

        if (!gridBuilder.locationContainers.TryGetValue(mapData.globalPosition, out GameObject container))
        {
            Debug.LogError($"Контейнер для локации {mapData.globalPosition} не найден!");
            return;
        }

        HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();
        foreach (var obj in mapData.objects)
            occupied.Add(new Vector2Int(obj.x, obj.y));

        PlayerAI player = FindObjectOfType<PlayerAI>();
        if (player != null)
        {
            Vector3Int playerCell = player.GetCurrentCell();
            occupied.Add(new Vector2Int(playerCell.x, playerCell.y));
        }

        int mapWidth = mapData.width;
        int mapHeight = mapData.height;

        foreach (var enemyEntry in mapData.encounter.enemies)
        {
            Vector3Int? freeCell = FindFreeCell(occupied, mapWidth, mapHeight);
            if (freeCell.HasValue)
            {
                if (freeCell.Value.x < 0 || freeCell.Value.x >= mapData.width || freeCell.Value.y < 0 || freeCell.Value.y >= mapData.height)
                {
                    Debug.LogError($"SpawnEnemiesFromEncounter: freeCell {freeCell.Value} вне границ карты {mapData.width}x{mapData.height}");
                    continue;
                }
                int height = mapData.GetHeight(freeCell.Value.x, freeCell.Value.y);
                Vector3 localPos = new Vector3(
                    freeCell.Value.x * gridBuilder.stepSize,
                    height * gridBuilder.heightStep + 1f,
                    freeCell.Value.y * gridBuilder.stepSize
                );

                GameObject prefab = GetEnemyPrefab(enemyEntry.type);
                if (prefab == null) continue;

                Vector3 worldPos = container.transform.position + localPos;
                GameObject enemy = Instantiate(prefab, worldPos, Quaternion.identity);
                enemy.name = $"Enemy_{enemyEntry.type}_{freeCell.Value.x}_{freeCell.Value.y}";
                EnemyAI ai = enemy.GetComponent<EnemyAI>();
                if (ai != null)
                {
                    ai.InitializeAtCell(freeCell.Value);
                    ai.SetHealth(enemyEntry.health);
                    LocationManager.Instance?.RegisterEnemySpawned();
                }
                spawnedEnemies.Add(enemy);
                occupied.Add(new Vector2Int(freeCell.Value.x, freeCell.Value.y));
            }
            else
            {
                Debug.LogWarning($"Нет свободной клетки для врага {enemyEntry.type} в локации {mapData.globalPosition}");
            }
        }
    }

    Vector3Int? FindFreeCell(HashSet<Vector2Int> occupied, int width, int height)
{
    int attempts = 50;
    for (int i = 0; i < attempts; i++)
    {
        int x = Random.Range(0, width);
        int y = Random.Range(0, height);
        // Исключаем клетки на границах чанка (первый и последний ряд)
        if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
            continue;
        Vector2Int cell = new Vector2Int(x, y);
        if (!occupied.Contains(cell) && PawnAI.GetOccupant(new Vector3Int(x, y, 0)) == null)
            return new Vector3Int(x, y, 0);
    }
    return null;
}

    GameObject GetEnemyPrefab(EnemyType type)
    {
        int idx = (int)type;
        return (idx >= 0 && idx < enemyPrefabs.Length) ? enemyPrefabs[idx] : null;
    }
}
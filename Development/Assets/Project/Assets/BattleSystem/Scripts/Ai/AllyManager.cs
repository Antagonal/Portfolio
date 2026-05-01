using UnityEngine;
using System.Collections.Generic;

public class AllyManager : MonoBehaviour
{
    public static AllyManager Instance { get; private set; }

    [Header("Префабы союзников (набор доступных типов)")]
    public GameObject[] allyPrefabs;

    [Header("Настройки")]
    public int spawnRadius = 3;

    private List<AllyAI> activeAllies = new List<AllyAI>();
    private PlayerAI player;
    private bool alliesSpawned = false;
    private bool wasTacticMode = false;

    private bool deploymentMode = false;
    private List<GameObject> unplacedPrefabs;
    private GridBuilder cachedGridBuilder;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        player = FindObjectOfType<PlayerAI>();
        cachedGridBuilder = FindObjectOfType<GridBuilder>();
        if (LocationManager.Instance != null)
            LocationManager.Instance.OnLocationLoaded += OnLocationLoaded;
    }

    void OnDestroy()
    {
        if (LocationManager.Instance != null)
            LocationManager.Instance.OnLocationLoaded -= OnLocationLoaded;
        if (Instance == this) Instance = null;
    }

    void OnLocationLoaded(TileMapData mapData)
    {
        DespawnAllAllies();
        alliesSpawned = false;
        deploymentMode = false;
        unplacedPrefabs = null;
    }

    void Update()
    {
        if (player == null) return;

        bool battleActive = !LocationManager.Instance.AllEnemiesDefeated && !LocationManager.Instance.currentMap.encounterCleared;

        if (battleActive && !alliesSpawned && !deploymentMode)
        {
            StartDeployment();
        }

        if (!LocationManager.TacticMode)
            DeselectAll();

        if (LocationManager.TacticMode && !wasTacticMode)
        {
            foreach (var ally in activeAllies)
            {
                ally.ResetAP();
                ally.ClearOrders();
            }
        }
        wasTacticMode = LocationManager.TacticMode;
    }

    void StartDeployment()
    {
        if (allyPrefabs.Length == 0) return;
        unplacedPrefabs = new List<GameObject>(allyPrefabs);
        deploymentMode = true;
        Debug.Log($"Режим расстановки. Осталось разместить: {unplacedPrefabs.Count} союзников. Клик ЛКМ по клетке для размещения, ПКМ по союзнику для возврата.");
    }

    public bool TryPlaceAlly(Vector3Int cell)
    {
        if (!deploymentMode) return false;
        if (unplacedPrefabs == null || unplacedPrefabs.Count == 0) return false;

        if (cachedGridBuilder == null)
            cachedGridBuilder = FindObjectOfType<GridBuilder>();
        if (cachedGridBuilder == null) return false;

        if (!LocationManager.Instance.IsInBounds(cell))
        {
            Debug.Log("Клетка вне загруженной карты");
            return false;
        }

        if (PawnAI.GetOccupant(cell) != null)
        {
            Debug.Log("Клетка занята другим персонажем");
            return false;
        }

        Vector3Int playerCell = player.GetCurrentCell();
        int dx = Mathf.Abs(cell.x - playerCell.x);
        int dy = Mathf.Abs(cell.y - playerCell.y);
        if (dx > spawnRadius || dy > spawnRadius)
        {
            Debug.Log("Клетка вне зоны расстановки");
            return false;
        }

        Vector2Int playerChunk = LocationManager.Instance.GetChunkFromGlobalCell(playerCell);
        Vector2Int targetChunk = LocationManager.Instance.GetChunkFromGlobalCell(cell);
        if (playerChunk != targetChunk)
        {
            Debug.Log("Нельзя расставлять союзников за пределами текущего чанка");
            return false;
        }

        // Проверка на планы врагов
        HashSet<Vector3Int> enemyPlannedCells = new HashSet<Vector3Int>();
        EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();
        foreach (var enemy in enemies)
        {
            enemyPlannedCells.UnionWith(enemy.GetAllTargetCells());
        }
        if (enemyPlannedCells.Contains(cell))
        {
            Debug.Log("Клетка уже запланирована врагом");
            return false;
        }

        GameObject prefab = unplacedPrefabs[0];
        unplacedPrefabs.RemoveAt(0);

        Vector3 worldPos = cachedGridBuilder.CellToWorld(cell);
        GameObject allyObj = Instantiate(prefab, worldPos, Quaternion.identity);
        AllyAI ally = allyObj.GetComponent<AllyAI>();
        if (ally != null)
        {
            ally.InitializeWithPrefab(player, prefab);
            ally.InitializeAtCell(cell);
            activeAllies.Add(ally);
            Debug.Log($"Союзник размещён. Осталось: {unplacedPrefabs.Count}");
            return true;
        }
        else
        {
            unplacedPrefabs.Insert(0, prefab);
            Destroy(allyObj);
            return false;
        }
    }

    public bool RemoveAlly(AllyAI ally)
    {
        if (!deploymentMode) return false;
        if (unplacedPrefabs == null) return false;
        if (ally == null || !activeAllies.Contains(ally)) return false;

        if (ally.sourcePrefab != null)
        {
            unplacedPrefabs.Add(ally.sourcePrefab);
        }
        activeAllies.Remove(ally);
        Destroy(ally.gameObject);
        Debug.Log($"Союзник удалён. Осталось разместить: {unplacedPrefabs.Count}");
        return true;
    }

    public void EndDeployment()
    {
        if (!deploymentMode) return;
        deploymentMode = false;
        alliesSpawned = true;
        if (unplacedPrefabs != null)
            unplacedPrefabs.Clear();
        Debug.Log("Расстановка завершена.");
    }

    void DespawnAllAllies()
    {
        foreach (var ally in activeAllies)
        {
            if (ally != null)
                Destroy(ally.gameObject);
        }
        activeAllies.Clear();
    }

    public AllyAI GetSelectedAlly()
    {
        foreach (var ally in activeAllies)
            if (ally.IsSelected)
                return ally;
        return null;
    }

    public void DeselectAll()
    {
        foreach (var ally in activeAllies)
            ally.IsSelected = false;
    }

    public bool IsDeploymentMode => deploymentMode;

    public void UnregisterAlly(AllyAI ally)
    {
        if (activeAllies.Contains(ally))
        {
            activeAllies.Remove(ally);
        }
        // Если этот союзник был выбран, снимаем выделение
        PlayerAI player = FindObjectOfType<PlayerAI>();
        if (player != null && player.GetSelectedAlly() == ally)
        {
            player.DeselectAlly();
        }
    }
}
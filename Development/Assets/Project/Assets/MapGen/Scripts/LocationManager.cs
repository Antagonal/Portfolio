using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

public class LocationManager : MonoBehaviour
{
    public static LocationManager Instance { get; private set; }

    [Header("Все доступные карты")]
    public List<TileMapData> allMaps;

    [Header("Текущая локация")]
    public TileMapData currentMap;

    [Header("Камера (для перемещения)")]
    public Transform cameraTransform;

    public Dictionary<Vector2Int, TileMapData> mapsByGlobalPos { get; private set; }
    private Dictionary<string, TileMapData> mapsById;
    private GridBuilder gridBuilder;

    [Header("Лут")]
    public GameObject lootPrefab;

    private int enemiesAlive = 0;
    public bool AllEnemiesDefeated => enemiesAlive == 0;
    public static bool TacticMode { get; set; } = false;
    [HideInInspector] public bool justTransitioned = false;

    public System.Action<TileMapData> OnLocationLoaded;
    private Coroutine cameraMoveCoroutine;
    private PlayerAI player;

    // Для вращения камеры
    private float currentRotationAngle = 0f;
    private float targetRotationAngle = 0f;
    private bool isRotating = false;
    public bool IsRotating => isRotating;

    [Header("Настройки вращения камеры")]
    [SerializeField] private float rotationDuration = 0.5f;
    [SerializeField] private float bounceHeight = 2f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        BuildDictionaries();
        ResetAllEncounters();
        gridBuilder = FindObjectOfType<GridBuilder>();

        if (cameraTransform == null)
            cameraTransform = Camera.main?.transform;

        if (currentMap != null)
            LoadMap(currentMap);
    }

    void Update()
    {
        HandleCameraRotationInput();
    }

    void LateUpdate()
    {
        justTransitioned = false;
    }

    private void HandleCameraRotationInput()
{
    if (cameraTransform == null || gridBuilder == null || currentMap == null || isRotating)
        return;

    if (cameraMoveCoroutine != null)
        return;

    if (player == null)
        player = FindObjectOfType<PlayerAI>();
    if (player == null || player.inputBlockTimer > 0)
        return;

    if (Keyboard.current.qKey.wasPressedThisFrame)
    {
        targetRotationAngle = currentRotationAngle + 90f;
        StartCoroutine(RotateCameraSmoothly());
    }
    if (Keyboard.current.eKey.wasPressedThisFrame)
    {
        targetRotationAngle = currentRotationAngle - 90f;
        StartCoroutine(RotateCameraSmoothly());
    }
}

    private IEnumerator RotateCameraSmoothly()
    {
        isRotating = true;

        float startAngle = currentRotationAngle;
        float elapsed = 0f;

        float groundLevel = currentMap.elevation * gridBuilder.heightStep;
        float centerX = (currentMap.globalPosition.x * 10) + 5f;
        float centerZ = (currentMap.globalPosition.y * 10) + 5f;
        Vector3 locationCenter = new Vector3(centerX, groundLevel, centerZ);

        Vector3 offset = cameraTransform.position - locationCenter;
        float horizontalDistance = new Vector3(offset.x, 0, offset.z).magnitude;
        float verticalOffset = offset.y;

        Vector3 initialHorizontalDir = new Vector3(offset.x, 0, offset.z).normalized;
        if (initialHorizontalDir.magnitude < 0.001f)
            initialHorizontalDir = Vector3.back;

        while (elapsed < rotationDuration)
        {
            float t = elapsed / rotationDuration;
            float smoothT = t * t * (3f - 2f * t);

            float currentAngle = Mathf.Lerp(startAngle, targetRotationAngle, smoothT);

            Quaternion rot = Quaternion.Euler(0, currentAngle - startAngle, 0);
            Vector3 currentHorizontalDir = rot * initialHorizontalDir;

            float bounceOffset = Mathf.Sin(t * Mathf.PI) * bounceHeight;

            cameraTransform.position = locationCenter + currentHorizontalDir * horizontalDistance + Vector3.up * (verticalOffset + bounceOffset);
            cameraTransform.LookAt(locationCenter + Vector3.up * bounceOffset * 0.5f);

            elapsed += Time.deltaTime;
            yield return null;
        }

        currentRotationAngle = targetRotationAngle;

        float roundedAngle = Mathf.Round(currentRotationAngle / 90f) * 90f;
        currentRotationAngle = roundedAngle;

        Quaternion finalRot = Quaternion.Euler(0, roundedAngle - startAngle, 0);
        Vector3 finalHorizontalDir = finalRot * initialHorizontalDir;

        Vector3 finalPos = locationCenter + finalHorizontalDir * horizontalDistance + Vector3.up * verticalOffset;

        finalPos.x = Mathf.Round(finalPos.x * 1000f) / 1000f;
        finalPos.y = Mathf.Round(finalPos.y * 1000f) / 1000f;
        finalPos.z = Mathf.Round(finalPos.z * 1000f) / 1000f;

        cameraTransform.position = finalPos;
        cameraTransform.LookAt(locationCenter);

        Vector3 angles = cameraTransform.eulerAngles;
        angles.x = Mathf.Round(angles.x);
        angles.y = Mathf.Round(angles.y);
        angles.z = Mathf.Round(angles.z);
        cameraTransform.eulerAngles = angles;

        isRotating = false;
    }

    public void ResetAllEncounters()
    {
        foreach (var map in allMaps)
        {
            map.encounterCleared = false;
        }
        UnityEngine.Debug.Log("Все энкаунтеры сброшены");
    }

    void BuildDictionaries()
    {
        mapsById = new Dictionary<string, TileMapData>();
        mapsByGlobalPos = new Dictionary<Vector2Int, TileMapData>();

        foreach (var map in allMaps)
        {
            if (!mapsById.ContainsKey(map.locationId))
                mapsById.Add(map.locationId, map);
            else
                UnityEngine.Debug.LogWarning($"Дубликат ID: {map.locationId}");

            if (!mapsByGlobalPos.ContainsKey(map.globalPosition))
                mapsByGlobalPos.Add(map.globalPosition, map);
            else
                UnityEngine.Debug.LogWarning($"Дубликат глобальной позиции: {map.globalPosition}");
        }
    }

    public void LoadSurroundingMaps(Vector2Int centerGlobalPos, int radius = 1)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                Vector2Int neighborPos = new Vector2Int(centerGlobalPos.x + dx, centerGlobalPos.y + dy);
                if (mapsByGlobalPos.TryGetValue(neighborPos, out TileMapData neighborMap))
                {
                    if (gridBuilder != null && !gridBuilder.locationContainers.ContainsKey(neighborPos))
                    {
                        BuildAndNotify(neighborMap, neighborPos);
                    }
                }
            }
        }
    }

    private void BuildAndNotify(TileMapData map, Vector2Int globalPos)
    {
        if (gridBuilder == null) return;
        gridBuilder.BuildLocation(map, globalPos);
        OnLocationLoaded?.Invoke(map);
        if (enemiesAlive > 0)
        {
            TacticMode = true;
        }
    }

    public void LoadMap(TileMapData map)
    {
        if (map == null) return;
        currentMap = map;
        BuildAndNotify(map, map.globalPosition);
        LoadSurroundingMaps(map.globalPosition, 1);

        currentRotationAngle = 0f;
        targetRotationAngle = 0f;
        isRotating = false;
    }

    /// <summary>
    /// Переход в новую локацию. Вызывается из MovePlayer, когда игрок выходит за границы текущей карты.
    /// Метод проверяет, можно ли покинуть текущую локацию (нет врагов), загружает новую карту,
    /// перемещает камеру и устанавливает тактический режим.
    /// </summary>
    public bool TryTransition(TileMapData nextMap, Vector3Int lookDirection)
{
    if (!AllEnemiesDefeated)
    {
        Debug.Log("Нельзя покинуть локацию: враги ещё есть");
        return false;
    }

    if (nextMap == null) return false;

    float deltaY = (nextMap.elevation - currentMap.elevation) * gridBuilder.heightStep;
    MoveCameraToLocation(lookDirection, deltaY);
    LoadMap(nextMap);

    EnemySpawner spawner = FindObjectOfType<EnemySpawner>();
    if (spawner != null)
        spawner.SpawnEncounterForCurrentLocation();

    bool hasActiveEncounter = nextMap.encounter != null && !nextMap.encounterCleared && nextMap.encounter.enemies != null && nextMap.encounter.enemies.Count > 0;
    TacticMode = hasActiveEncounter;

    PlayerAI player = FindObjectOfType<PlayerAI>();
    player.inputBlockTimer = 0.21f;
    justTransitioned = true;

    return true;
}

    void MoveCameraToLocation(Vector3Int direction, float deltaY)
    {
        if (cameraTransform == null || gridBuilder == null) return;

        Vector3 offset = new Vector3(
            direction.x * 10,
            deltaY,
            direction.y * 10
        );
        cameraMoveCoroutine = StartCoroutine(MoveCameraSmoothly(offset));
    }

    private IEnumerator MoveCameraSmoothly(Vector3 delta)
    {
        Vector3 startPos = cameraTransform.position;
        Vector3 targetPos = startPos + delta;
        float duration = 0.3f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float eased = 1 - (1 - t) * (1 - t);

            cameraTransform.position = Vector3.Lerp(startPos, targetPos, eased);
            yield return null;
        }
        cameraTransform.position = targetPos;
        cameraMoveCoroutine = null;
    }

    public void ResetEnemyCount()
    {
        enemiesAlive = 0;
    }

    public void RegisterEnemySpawned() => enemiesAlive++;
    public void RegisterEnemyDied()
    {
        enemiesAlive--;
        if (enemiesAlive < 0) enemiesAlive = 0;
        if (enemiesAlive == 0 && currentMap != null)
        {
            currentMap.encounterCleared = true;
            UnityEngine.Debug.Log($"Локация {currentMap.locationId} полностью зачищена!");
        }
    }

    public void OnEnemiesSpawned()
    {
        if (!AllEnemiesDefeated)
        {
            TacticMode = true;
            UnityEngine.Debug.Log($"TacticMode {TacticMode}, так как в локации есть враги");
        }
    }

    public Vector3 CellToWorld(Vector3Int globalCell)
{
    int chunkX = Mathf.FloorToInt(globalCell.x / 10f);
    int chunkY = Mathf.FloorToInt(globalCell.y / 10f);
    Vector2Int chunkPos = new Vector2Int(chunkX, chunkY);

    if (!gridBuilder.locationContainers.TryGetValue(chunkPos, out GameObject container))
    {
        Debug.LogError($"Локация {chunkPos} не загружена!");
        return Vector3.zero;
    }

    if (!mapsByGlobalPos.TryGetValue(chunkPos, out TileMapData data))
    {
        Debug.LogError($"Нет данных для локации {chunkPos}");
        return Vector3.zero;
    }

    int lx = globalCell.x - chunkX * 10;
    int ly = globalCell.y - chunkY * 10;

    if (lx < 0 || lx >= data.width || ly < 0 || ly >= data.height)
    {
        Debug.LogError($"Локальные координаты ({lx},{ly}) вне границ чанка {data.width}x{data.height}");
        return Vector3.zero;
    }

    Vector3 basePos = container.transform.position + new Vector3(lx, 0, ly);
    int tileHeight = data.GetHeight(lx, ly);
    ObjectData obj = GetObjectAt(globalCell);
    int totalHeight = tileHeight;
    if (obj != null && obj.canMoveTo)
        totalHeight += obj.heightInTiles;

    return basePos + new Vector3(0, totalHeight + 0.5f, 0);
}

public Vector3Int WorldToCell(Vector3 worldPosition)
{
    foreach (var kv in gridBuilder.locationContainers)
    {
        GameObject container = kv.Value;
        TileMapData data = mapsByGlobalPos[kv.Key];
        float minX = container.transform.position.x;
        float maxX = minX + data.width;
        float minZ = container.transform.position.z;
        float maxZ = minZ + data.height;

        if (worldPosition.x >= minX && worldPosition.x < maxX && worldPosition.z >= minZ && worldPosition.z < maxZ)
        {
            int lx = Mathf.FloorToInt(worldPosition.x - minX);
            int ly = Mathf.FloorToInt(worldPosition.z - minZ);
            int gx = kv.Key.x * 10 + lx;
            int gy = kv.Key.y * 10 + ly;
            return new Vector3Int(gx, gy, 0);
        }
    }

    Debug.LogWarning($"Точка {worldPosition} не попадает ни в одну загруженную локацию");
    return Vector3Int.zero;
}

public ObjectData GetObjectAt(Vector3Int globalCell)
{
    int chunkX = Mathf.FloorToInt(globalCell.x / 10f);
    int chunkY = Mathf.FloorToInt(globalCell.y / 10f);
    Vector2Int chunkPos = new Vector2Int(chunkX, chunkY);

    if (!gridBuilder.locationContainers.TryGetValue(chunkPos, out GameObject container))
        return null;

    LocationContainer locContainer = container.GetComponent<LocationContainer>();
    if (locContainer == null) return null;

    int lx = globalCell.x - chunkX * 10;
    int ly = globalCell.y - chunkY * 10;
    Vector3Int localCell = new Vector3Int(lx, ly, 0);

    if (locContainer.occupiedObjects.TryGetValue(localCell, out ObjectData obj))
        return obj;
    return null;
}

public Vector2Int GetChunkFromGlobalCell(Vector3Int globalCell)
{
    int chunkX = Mathf.FloorToInt(globalCell.x / 10f);
    int chunkY = Mathf.FloorToInt(globalCell.y / 10f);
    return new Vector2Int(chunkX, chunkY);
}

public bool IsInBounds(Vector3Int globalCell)
{
    int chunkX = Mathf.FloorToInt(globalCell.x / 10f);
    int chunkY = Mathf.FloorToInt(globalCell.y / 10f);
    Vector2Int chunkPos = new Vector2Int(chunkX, chunkY);

    if (!gridBuilder.locationContainers.ContainsKey(chunkPos))
        return false;

    if (!mapsByGlobalPos.TryGetValue(chunkPos, out TileMapData data))
        return false;

    int lx = globalCell.x - chunkX * 10;
    int ly = globalCell.y - chunkY * 10;
    return lx >= 0 && lx < data.width && ly >= 0 && ly < data.height;
}

public int GetHeightAt(int gx, int gy)
{
    int chunkX = Mathf.FloorToInt(gx / 10f);
    int chunkY = Mathf.FloorToInt(gy / 10f);
    Vector2Int chunkPos = new Vector2Int(chunkX, chunkY);

    if (!mapsByGlobalPos.TryGetValue(chunkPos, out TileMapData data))
        return 0;

    int lx = gx - chunkX * 10;
    int ly = gy - chunkY * 10;
    if (lx < 0 || lx >= data.width || ly < 0 || ly >= data.height)
        return 0;

    return data.GetHeight(lx, ly);
}

public float GetTileHeight(Vector3Int globalCell)
{
    int chunkX = Mathf.FloorToInt(globalCell.x / 10f);
    int chunkY = Mathf.FloorToInt(globalCell.y / 10f);
    Vector2Int chunkPos = new Vector2Int(chunkX, chunkY);

    if (!gridBuilder.locationContainers.TryGetValue(chunkPos, out GameObject container))
        return 0;

    if (!mapsByGlobalPos.TryGetValue(chunkPos, out TileMapData data))
        return 0;

    int lx = globalCell.x - chunkX * 10;
    int ly = globalCell.y - chunkY * 10;

    if (lx < 0 || lx >= data.width || ly < 0 || ly >= data.height)
        return 0;

    int tileHeight = data.GetHeight(lx, ly);
    float baseY = container.transform.position.y;
    return baseY + tileHeight + 0.5f;
}

public bool CanMoveBetweenCells(Vector3Int fromCell, Vector3Int toCell)
{
    int fromHeight = GetHeightAt(fromCell.x, fromCell.y);
    int toHeight = GetHeightAt(toCell.x, toCell.y);
    return Mathf.Abs(toHeight - fromHeight) <= 1;
}

public TileMapData GetLocationAt(Vector3Int globalCell)
{
    int chunkX = Mathf.FloorToInt(globalCell.x / 10);
    int chunkY = Mathf.FloorToInt(globalCell.y / 10);
    Vector2Int chunkPos = new Vector2Int(chunkX, chunkY);
    mapsByGlobalPos.TryGetValue(chunkPos, out TileMapData data);
    return data;
}

public bool TryGetTargetLocation(Vector3Int fromGlobalCell, Vector3Int direction, out TileMapData nextMap, out Vector3Int entranceCell)
{
    nextMap = null;
    entranceCell = Vector3Int.zero;

    int chunkX = Mathf.FloorToInt(fromGlobalCell.x / 10f);
    int chunkY = Mathf.FloorToInt(fromGlobalCell.y / 10f);
    Vector2Int currentChunk = new Vector2Int(chunkX, chunkY);

    Vector3Int targetGlobal = fromGlobalCell + direction;
    int targetChunkX = Mathf.FloorToInt(targetGlobal.x / 10f);
    int targetChunkY = Mathf.FloorToInt(targetGlobal.y / 10f);
    Vector2Int targetChunk = new Vector2Int(targetChunkX, targetChunkY);

    if (currentChunk == targetChunk) return false;

    if (!mapsByGlobalPos.TryGetValue(targetChunk, out nextMap)) return false;

    int lx = targetGlobal.x - targetChunkX * 10;
    int ly = targetGlobal.y - targetChunkY * 10;

    // Если целевая клетка выходит за границы нового чанка, корректируем
    if (lx < 0) lx = 0;
    else if (lx >= nextMap.width) lx = nextMap.width - 1;
    if (ly < 0) ly = 0;
    else if (ly >= nextMap.height) ly = nextMap.height - 1;

    entranceCell = new Vector3Int(
        targetChunkX * 10 + lx,
        targetChunkY * 10 + ly,
        0
    );
    return true;
}
}
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class EnemyAI : PawnAI
{
    [Header("Настройки врага")]
    [SerializeField] private float moveCooldown;
    [SerializeField] private float attackCooldown;
    [SerializeField] private float attackRange;
    [SerializeField] private int attackDamage;

    private float moveTimer;
    private float attackTimer;
    private PlayerAI player;
    private bool isActive = false;

    private Queue<Vector3Int> moveQueue = new Queue<Vector3Int>();

    // Для отслеживания входа в тактический режим
    private bool wasTacticMode = false;

    [Header("UI Здоровья")]
    [SerializeField] private GameObject healthUIPrefab;
    [SerializeField] private GameObject heartPrefab;
    [SerializeField] private float healthUIOffsetY = 1.5f;

    [Header("Loot")]
    [SerializeField] private ResourceType lootType = ResourceType.None;
    [SerializeField] private int lootAmount = 0;

    private GameObject healthUIInstance;
    private List<Image> heartImages = new List<Image>();
    private Vector3Int lastMoveDirection = Vector3Int.zero;

    protected override void Start()
    {
        base.Start();
        ActionTimer = 0;
        moveTimer = 0;
        attackTimer = 0;
        player = FindAnyObjectByType<PlayerAI>();
        PlayerAI.OnPlayerAction += OnPlayerAction;
        AllyAI.OnAllyAction += OnPlayerAction;
    }

    void Update()
    {
        if (LocationManager.TacticMode && !wasTacticMode)
        {
            RefillMoveQueue();
        }
        if (!LocationManager.TacticMode) ActionTimer += Time.deltaTime;

        wasTacticMode = LocationManager.TacticMode;

        UpdateMovement();
        if ( ActionTimer >= ActionSpeed ) //isActive)
        {
            InwokeAction();
            isActive = false;
            ActionTimer = 0;
        }
    }

    public override void InitializeAtCell(Vector3Int startCell)
{
    base.InitializeAtCell(startCell);
    lastMoveDirection = Vector3Int.zero;
    RefillMoveQueue();
}

    public override void SetHealth(int health)
    {
        base.SetHealth(health);
        CreateHealthUI();
        UpdateHealthUI();
    }

    protected override void UpdateHealthUI()
    {
        if (heartImages == null) return;
        for (int i = 0; i < heartImages.Count; i++)
            heartImages[i].enabled = i < currentHealth;
    }

    protected override void OnDeath()
{
    if (lootType != ResourceType.None && lootAmount > 0)
    {
        Vector3Int cell = GetCurrentCell();
        Vector3 spawnPos = LocationManager.Instance.CellToWorld(cell);
        spawnPos.y += 1.5f; // выше, чтобы упасть
        if (LocationManager.Instance.lootPrefab != null)
        {
            GameObject loot = Instantiate(LocationManager.Instance.lootPrefab, spawnPos, Quaternion.identity);
            LootItem lootItem = loot.GetComponent<LootItem>();
            if (lootItem != null)
            {
                lootItem.resourceType = lootType;
                lootItem.amount = lootAmount;
            }
        }
    }
    LocationManager.Instance?.RegisterEnemyDied();
    Destroy(gameObject);
}

    void CreateHealthUI()
    {
        if (healthUIPrefab == null || heartPrefab == null) return;
        if (healthUIInstance != null) Destroy(healthUIInstance);

        healthUIInstance = Instantiate(healthUIPrefab, transform);
        healthUIInstance.transform.localPosition = new Vector3(0, healthUIOffsetY, 0);
        healthUIInstance.name = "HealthUI";

        Transform heartsContainer = healthUIInstance.transform.Find("HeartsContainer");
        if (heartsContainer == null)
        {
            heartsContainer = new GameObject("HeartsContainer").transform;
            heartsContainer.SetParent(healthUIInstance.transform);
            heartsContainer.localPosition = Vector3.zero;
        }

        foreach (Transform child in heartsContainer) Destroy(child.gameObject);
        heartImages.Clear();

        for (int i = 0; i < maxHealth; i++)
        {
            GameObject heartObj = Instantiate(heartPrefab, heartsContainer);
            heartObj.name = $"Heart_{i}";
            Image heartImage = heartObj.GetComponent<Image>();
            if (heartImage != null) heartImages.Add(heartImage);
        }

        HorizontalLayoutGroup layout = heartsContainer.GetComponent<HorizontalLayoutGroup>();
        if (layout == null)
        {
            layout = heartsContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 2;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
        }

        ContentSizeFitter fitter = heartsContainer.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = heartsContainer.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    void OnPlayerAction() => isActive = true;

    public void InwokeAction()
    {
        if (player == null || !player.IsAlive()) return;

        moveTimer += 1;

        // Поиск цели для атаки (игрок или союзник)
        PawnAI target = FindAttackTarget();
        if (target != null)
        {
            attackTimer += 1;
            if (attackTimer >= attackCooldown)
            {
                AttackTarget(target);
                attackTimer = 0f;
            }
        }
        else
        {
            if (moveTimer >= moveCooldown && moveQueue.Count > 0)
            {
                Vector3Int direction = moveQueue.Peek();
                Vector3Int newPosition = GetCurrentCell() + direction;

                if (CanMoveToCell(newPosition))
                {
                    moveQueue.Dequeue();
                    lastMoveDirection = direction; // запоминаем, куда пошли
                    MoveToCell(newPosition);
                }
                else
                {
                    // Не можем двигаться – убираем текущее направление из очереди и переходим к следующему
                    moveQueue.Dequeue();
                }
                moveTimer = 0f;
            }
        }
    }

    // Возвращает подходящую цель для атаки (ближайшую или первую)
    private PawnAI FindAttackTarget()
    {
        List<PawnAI> potentialTargets = new List<PawnAI>();

        // Проверяем игрока
        if (player != null && player.IsAlive() && CanAttackPawn(player))
            potentialTargets.Add(player);

        // Проверяем всех союзников
        AllyAI[] allies = FindObjectsOfType<AllyAI>();
        foreach (AllyAI ally in allies)
        {
            if (ally != null && ally.IsAlive() && CanAttackPawn(ally))
                potentialTargets.Add(ally);
        }

        if (potentialTargets.Count == 0) return null;

        // Выбираем случайную цель (можно изменить на ближайшую)
        return potentialTargets[Random.Range(0, potentialTargets.Count)];
    }

    private bool CanAttackPawn(PawnAI target)
    {
        if (target == null || !target.IsAlive()) return false;

        Vector3Int targetPos = target.GetCurrentCell();
        Vector3Int enemyPos = GetCurrentCell();
        int dx = Mathf.Abs(enemyPos.x - targetPos.x);
        int dy = Mathf.Abs(enemyPos.y - targetPos.y);

        // Проверяем, что цель в соседней клетке (по горизонтали или вертикали)
        if ((dx == 1 && dy == 0) || (dx == 0 && dy == 1))
        {
            float currentHeight = CurrentHeight;
            float targetHeight = target.CurrentHeight;
            if (Mathf.Abs(targetHeight - currentHeight) > gridBuilder.heightStep * 1.1f)
                return false;
            return true;
        }
        return false;
    }

    private void AttackTarget(PawnAI target)
    {
        Vector3Int directionToTarget = target.GetCurrentCell() - GetCurrentCell();
        RotateTowards(directionToTarget);
        target.TakeDamage(attackDamage, directionToTarget);

    }

    private void RefillMoveQueue()
{
    moveQueue.Clear();
    Vector3Int currentPos = GetCurrentCell();
    Vector2Int currentChunk = LocationManager.Instance.GetChunkFromGlobalCell(currentPos);

    Vector3Int[] directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };
    Vector3Int simulatedPos = currentPos;

    for (int i = 0; i < 10; i++)
    {
        List<Vector3Int> possibleDirs = new List<Vector3Int>();
        int currentHeight = LocationManager.Instance.GetHeightAt(simulatedPos.x, simulatedPos.y);

        foreach (var dir in directions)
        {
            Vector3Int nextCell = simulatedPos + dir;
            if (LocationManager.Instance.GetChunkFromGlobalCell(nextCell) != currentChunk)
                continue;
            if (!LocationManager.Instance.IsInBounds(nextCell))
                continue;
            int nextHeight = LocationManager.Instance.GetHeightAt(nextCell.x, nextCell.y);
            if (Mathf.Abs(nextHeight - currentHeight) > 1)
                continue;
            if (lastMoveDirection != Vector3Int.zero && dir == -lastMoveDirection)
                continue;

            possibleDirs.Add(dir);
        }

        Vector3Int chosenDir;

        if (possibleDirs.Count == 0)
        {
            // Разрешаем все направления, кроме явно невозможных (но без запрета назад)
            foreach (var dir in directions)
            {
                Vector3Int nextCell = simulatedPos + dir;
                if (LocationManager.Instance.GetChunkFromGlobalCell(nextCell) != currentChunk)
                    continue;
                if (!LocationManager.Instance.IsInBounds(nextCell))
                    continue;
                int nextHeight = LocationManager.Instance.GetHeightAt(nextCell.x, nextCell.y);
                if (Mathf.Abs(nextHeight - currentHeight) > 1)
                    continue;
                possibleDirs.Add(dir);
            }
        }

        if (possibleDirs.Count > 0)
        {
            chosenDir = possibleDirs[Random.Range(0, possibleDirs.Count)];
        }
        else
        {
            chosenDir = directions[Random.Range(0, directions.Length)];
        }

        moveQueue.Enqueue(chosenDir);
        simulatedPos += chosenDir;
    }
    Debug.Log($"{name}: очередь движений перезаполнена (10 направлений)");
}

    public List<Vector3Int> GetAllTargetCells()
    {
        List<Vector3Int> cells = new List<Vector3Int>();
        Vector3Int current = GetCurrentCell();
        foreach (Vector3Int dir in moveQueue)
        {
            current += dir;
            cells.Add(current);
        }
        return cells;
    }

    public Vector3Int? GetNextTargetCell()
    {
        if (moveQueue.Count == 0) return null;
        Vector3Int direction = moveQueue.Peek();
        return GetCurrentCell() + direction;
    }

    void OnDestroy()
    {
        PlayerAI.OnPlayerAction -= OnPlayerAction;
        AllyAI.OnAllyAction -= OnPlayerAction;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange * (gridBuilder?.stepSize ?? 1f));
    }
}
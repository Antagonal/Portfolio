using UnityEngine;
using System;

public abstract class PawnAI : MonoBehaviour
{
    [Header("Настройки")]
    [SerializeField] protected float moveSpeed = 5f;

    // Здоровье
    [Header("Здоровье")]
    [SerializeField] protected int maxHealth = 1;
    protected int currentHealth;
    protected bool isDead = false;

    [Header("Защита")]
    [SerializeField] protected int blockAmount = 2;
    protected bool isDefending = false;
    protected Vector3Int defendDirection = Vector3Int.zero;

    // Состояние
    [HideInInspector] public float ActionTimer = 0;
    public float ActionSpeed;
    [HideInInspector] public bool isMoving = false;
    [HideInInspector] protected bool isAttacking = false;
    protected bool isInitialized = false;
    protected GridBuilder gridBuilder;

    [SerializeField] protected float arcHeight = 0.5f;
    protected Vector3 startPosition;
    protected Vector3 targetPosition;
    protected Vector3 controlPoint;
    protected float moveProgress = 0f;
    [HideInInspector] public float inputBlockTimer = 0f;
    public GridBuilder GetGridBuilder() => gridBuilder;

    // Публичное свойство для доступа к targetPosition (используется в LocationManager)
    public Vector3 TargetPosition
    {
        get { return targetPosition; }
        set { targetPosition = value; }
    }

    // Абстрактные методы
    protected abstract void UpdateHealthUI();
    protected abstract void OnDeath();

    protected virtual void Start()
    {
        if (gridBuilder == null)
            gridBuilder = FindAnyObjectByType<GridBuilder>();
    }

    // ========== УНИВЕРСАЛЬНЫЕ МЕТОДЫ ==========

    public Vector3Int GetCurrentCell()
    {
        // Если gridBuilder ещё не найден, пытаемся найти его сейчас
        if (gridBuilder == null)
        {
            gridBuilder = FindAnyObjectByType<GridBuilder>();
            if (gridBuilder == null)
            {
                Debug.LogError($"{name}: GridBuilder не найден в GetCurrentCell!");
                return Vector3Int.zero;
            }
        }
        return gridBuilder.WorldToCell(transform.position);
    }

    public float CurrentHeight => transform.position.y;

    public static GameObject GetOccupant(Vector3Int cell)
    {
        PawnAI[] allPawns = FindObjectsOfType<PawnAI>();
        foreach (PawnAI pawn in allPawns)
        {
            if (!pawn.isInitialized || pawn.isDead) continue;
            if (pawn.GetCurrentCell() == cell) return pawn.gameObject;
        }
        return null;
    }

    public bool CanMoveToCell(Vector3Int targetCell, bool ignoreBattle = false)
{
    if (LocationManager.Instance == null || !LocationManager.Instance.IsInBounds(targetCell))
        return false;

    GameObject occupant = GetOccupant(targetCell);
    if (occupant != null && occupant != this.gameObject)
        return false;

    ObjectData targetObj = LocationManager.Instance.GetObjectAt(targetCell);
    if (targetObj != null && !targetObj.canMoveTo)
        return false;

    if (targetObj != null && targetObj.isLadder)
        return true;

    float currentHeight = CurrentHeight;
    float targetHeight = LocationManager.Instance.CellToWorld(targetCell).y;
    if (Mathf.Abs(targetHeight - currentHeight) > gridBuilder.heightStep * 1.1f)
        return false;

    // Ограничение на выход за пределы чанка во время боя
    if (!ignoreBattle && !LocationManager.Instance.AllEnemiesDefeated)
    {
        Vector2Int currentChunk = LocationManager.Instance.GetChunkFromGlobalCell(GetCurrentCell());
        Vector2Int targetChunk = LocationManager.Instance.GetChunkFromGlobalCell(targetCell);
        if (currentChunk != targetChunk)
        {
            Debug.Log($"{name}: нельзя покинуть текущий чанк во время боя");
            return false;
        }
    }

    return true;
}

    public void MoveToCell(Vector3Int targetCell, Vector3Int? forcedDirection = null, bool ignoreHeightCheck = false, bool ignoreChunkRestriction = false)
{
    if (!ignoreHeightCheck)
    {
        if (!CanMoveToCell(targetCell, ignoreChunkRestriction))
            return;
    }
    else
    {
        if (gridBuilder == null || !gridBuilder.IsInBounds(targetCell))
            return;
        GameObject occupant = GetOccupant(targetCell);
        if (occupant != null && occupant != this.gameObject)
            return;
    }

    startPosition = transform.position;

    ObjectData targetObj = gridBuilder.GetObjectAt(targetCell);
    if (targetObj != null && targetObj.isLadder)
    {
        float baseY = gridBuilder.GetTileHeight(targetCell);
        float maxY = baseY + targetObj.heightInTiles;
        float currentY = transform.position.y;
        float targetY = Mathf.Clamp(currentY, baseY, maxY);
        Vector3 worldPos = gridBuilder.CellToWorld(targetCell);
        targetPosition = new Vector3(worldPos.x, targetY, worldPos.z);
    }
    else
    {
        targetPosition = gridBuilder.CellToWorld(targetCell);
    }

    Vector3Int direction;
    if (forcedDirection.HasValue)
    {
        direction = forcedDirection.Value;
    }
    else
    {
        Vector3Int currentCell = GetCurrentCell();
        direction = targetCell - currentCell;
    }

    if (direction.x != 0 || direction.y != 0)
        RotateTowards(direction);

    Vector3 midPoint = Vector3.Lerp(startPosition, targetPosition, 0.5f);
    controlPoint = new Vector3(midPoint.x, midPoint.y + arcHeight, midPoint.z);

    isMoving = true;
    moveProgress = 0f;
}

    public void TeleportToCell(Vector3Int cell)
    {
        if (gridBuilder == null)
        {
            Debug.LogError($"{name}: TeleportToCell: gridBuilder == null");
            return;
        }
        transform.position = gridBuilder.CellToWorld(cell);
        targetPosition = transform.position;
        inputBlockTimer = 0.1f;
    }

    protected void UpdateMovement()
    {
        if (isMoving)
        {
            float distance = Vector3.Distance(startPosition, targetPosition);
            moveProgress += Time.deltaTime * moveSpeed / distance;
            moveProgress = Mathf.Clamp01(moveProgress);

            Vector3 arcPosition = Mathf.Pow(1 - moveProgress, 2) * startPosition +
                                   2 * (1 - moveProgress) * moveProgress * controlPoint +
                                   Mathf.Pow(moveProgress, 2) * targetPosition;

            transform.position = Vector3.MoveTowards(transform.position, arcPosition, moveSpeed * Time.deltaTime);

            if (moveProgress >= 1f && Vector3.Distance(transform.position, targetPosition) < 0.01f)
            {
                transform.position = targetPosition;
                isMoving = false;
                CheckTrap(); // После завершения движения проверяем ловушку
            }
        }
    }

    protected int GetTotalHeight(Vector3Int cell)
{
    int tileHeight = LocationManager.Instance.GetHeightAt(cell.x, cell.y);
    ObjectData obj = LocationManager.Instance.GetObjectAt(cell);
    if (obj != null && obj.canMoveTo)
        return tileHeight + obj.heightInTiles;
    return tileHeight;
}

protected int GetBaseHeightAt(Vector3Int cell)
{
    int tileHeight = LocationManager.Instance.GetHeightAt(cell.x, cell.y);
    ObjectData obj = LocationManager.Instance.GetObjectAt(cell);
    if (obj != null && obj.canMoveTo && !obj.isLadder)
        tileHeight += obj.heightInTiles;
    return tileHeight;
}

    protected void CheckTrap()
    {
        if (gridBuilder == null) return;
        Vector3Int currentCell = GetCurrentCell();
        ObjectData obj = gridBuilder.GetObjectAt(currentCell);
        if (obj != null && obj.isTrap)
        {
            // Наносим урон персонажу
            TakeDamage(obj.trapDamage, Vector3Int.zero);
            Debug.Log($"{name} попал в ловушку и получил {obj.trapDamage} урона");
        }
    }

    protected void RotateTowards(Vector3Int direction)
    {
        if (direction.x != 0 || direction.y != 0)
        {
            Vector3 lookDirection = new Vector3(direction.x, 0, direction.y);
            transform.rotation = Quaternion.LookRotation(lookDirection);
        }
    }

    public virtual void InitializeAtCell(Vector3Int startCell)
    {
        isInitialized = true;
        if (gridBuilder != null)
        {
            transform.position = gridBuilder.CellToWorld(startCell);
            targetPosition = transform.position;
            startPosition = transform.position;
        }
    }

    public bool IsInitialized() => isInitialized;

    // ========== МЕТОДЫ ЗДОРОВЬЯ ==========

    public virtual void SetHealth(int health)
    {
        maxHealth = health;
        currentHealth = health;
        UpdateHealthUI();
    }

    public virtual void TakeDamage(int damage, Vector3Int attackDirection)
    {
        if (isDead) return;

        int finalDamage = damage;
        if (isDefending && attackDirection == defendDirection)
            finalDamage = Mathf.Max(0, damage - blockAmount);

        currentHealth -= finalDamage;
        currentHealth = Mathf.Max(0, currentHealth);
        UpdateHealthUI();

        if (currentHealth <= 0) Die();
    }

    public void TakeDamage(int damage) => TakeDamage(damage, Vector3Int.zero);

    protected virtual void Die()
    {
        isDead = true;
        OnDeath();
    }

    public virtual void Heal(int amount)
    {
        if (isDead) return;
        currentHealth += amount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        UpdateHealthUI();
    }

    public bool IsAlive() => !isDead;
    public int GetCurrentHealth() => currentHealth;
    public int GetMaxHealth() => maxHealth;
}
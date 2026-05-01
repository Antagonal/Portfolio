using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI; // ADDED: для Image

public class AllyAI : PawnAI
{
    [Header("Настройки союзника")]
    [SerializeField] private int maxAP = 10;
    private int currentAP;
    private Queue<Order> pendingOrders = new Queue<Order>();

    public int GetCurrentAP() => currentAP;
    public int GetMaxAP() => maxAP;

    private PlayerAI player;
    private bool isSelected = false;
    public static System.Action OnAllyAction;

    private const int MAX_ORDERS = 10;

    public GameObject sourcePrefab;

    private Renderer rend;
    private Color originalColor;
    private Coroutine blinkCoroutine;
    [Header("Подсветка")]
    [SerializeField] private float brightnessMultiplier = 1.5f;
    [SerializeField] private float blinkSpeed = 0.5f;

    // ADDED: UI для здоровья
    [Header("UI Здоровья")]
    [SerializeField] private GameObject healthUIPrefab;
    [SerializeField] private GameObject heartPrefab;
    [SerializeField] private float healthUIOffsetY = 1.5f;
    private GameObject healthUIInstance;
    private List<Image> heartImages = new List<Image>();

    public enum OrderType { Move, Skill, Wait, None }

    private struct Order
    {
        public OrderType type;
        public Skill skill;          // для типа Skill
        public Vector3Int direction;  // для Move
        public Vector3Int targetCell; // для Skill (целевая клетка)
        public int cost;              // стоимость AP
    }

    public void InitializeWithPrefab(PlayerAI playerRef, GameObject prefab)
    {
        player = playerRef;
        sourcePrefab = prefab;
    }

    protected override void Start()
    {
        base.Start();
        currentAP = maxAP;
        // ADDED: инициализация здоровья и создание UI
        SetHealth(maxHealth);
        rend = GetComponentInChildren<Renderer>();
        if (rend == null)
        {
            Debug.LogError($"{name}: Не найден Renderer! Подсветка не будет работать.");
        }
        else
        {
            originalColor = rend.material.color;
        }
    }

    void Update()
    {
        if (isDead || player == null) return;

        if (LocationManager.TacticMode)
            return;

        ActionTimer += Time.deltaTime;
        if (ActionTimer >= ActionSpeed && !isMoving && !isAttacking)
        {
            ActionTimer = 0f;
            ExecuteNextOrder();
        }

        UpdateMovement();
    }

    // ADDED: переопределение SetHealth
    public override void SetHealth(int health)
    {
        base.SetHealth(health);
        CreateHealthUI();
        UpdateHealthUI();
    }

    // ADDED: переопределение UpdateHealthUI
    protected override void UpdateHealthUI()
    {
        if (heartImages == null) return;
        for (int i = 0; i < heartImages.Count; i++)
            heartImages[i].enabled = i < currentHealth;
    }

    // ADDED: создание UI с сердечками (как у врагов)
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

    // Добавить приказ на движение
    public void GiveMoveOrder(Vector3Int direction, int cost)
    {
        if (currentAP < cost)
        {
            Debug.Log($"{name}: недостаточно AP для движения");
            return;
        }

        if (pendingOrders.Count >= MAX_ORDERS)
        {
            Debug.LogWarning($"{name}: очередь приказов переполнена (макс. {MAX_ORDERS}), приказ не добавлен");
            return;
        }

        currentAP -= cost;

        Order order;
        order.type = OrderType.Move;
        order.skill = null;
        order.direction = direction;
        order.targetCell = Vector3Int.zero;
        order.cost = cost;
        pendingOrders.Enqueue(order);
        Debug.Log($"{name}: приказ движения добавлен. Очередь: {pendingOrders.Count}/{MAX_ORDERS}, AP осталось: {currentAP}");
    }

    // Добавить приказ на использование умения
    public void GiveSkillOrder(Skill skill, Vector3Int targetCell, int cost)
    {
        if (currentAP < cost)
        {
            Debug.Log($"{name}: недостаточно AP для умения {skill.skillName}");
            return;
        }

        if (pendingOrders.Count >= MAX_ORDERS)
        {
            Debug.LogWarning($"{name}: очередь приказов переполнена (макс. {MAX_ORDERS}), приказ не добавлен");
            return;
        }

        currentAP -= cost;

        Order order;
        order.type = OrderType.Skill;
        order.skill = skill;
        order.targetCell = targetCell;
        order.direction = Vector3Int.zero; // не используется
        order.cost = cost;
        pendingOrders.Enqueue(order);
        Debug.Log($"{name}: приказ умения {skill.skillName} на клетку {targetCell} добавлен. Очередь: {pendingOrders.Count}/{MAX_ORDERS}, AP осталось: {currentAP}");
    }

    public void GiveWaitOrder()
    {
        if (pendingOrders.Count >= MAX_ORDERS)
        {
            Debug.LogWarning($"{name}: очередь приказов переполнена (макс. {MAX_ORDERS}), приказ не добавлен");
            return;
        }

        // Восстанавливаем 2 AP
        currentAP += 2;

        Order order;
        order.type = OrderType.Wait;
        order.skill = null;
        order.direction = Vector3Int.zero;
        order.targetCell = Vector3Int.zero;
        order.cost = -2; // отрицательная стоимость для информации (не списывается, а восстанавливается)
        pendingOrders.Enqueue(order);
        Debug.Log($"{name}: приказ ожидания добавлен. Очередь: {pendingOrders.Count}/{MAX_ORDERS}, AP осталось: {currentAP}");
    }

    // Отмена последнего приказа
    public void CancelLastOrder()
    {
        if (pendingOrders.Count == 0)
        {
            Debug.Log($"{name}: нет приказов для отмены");
            return;
        }

        List<Order> list = new List<Order>(pendingOrders);
        Order last = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        pendingOrders = new Queue<Order>(list);
        currentAP += last.cost; // возвращаем AP (для Move/Skill cost положительный)
        Debug.Log($"{name}: отменён последний приказ ({last.type}), восстановлено {last.cost} AP. Осталось AP: {currentAP}");
    }

    private void ExecuteNextOrder()
    {
        if (pendingOrders.Count == 0) return;

        Order order = pendingOrders.Peek();

        if (order.type == OrderType.Move)
        {
            Vector3Int targetCell = GetCurrentCell() + order.direction;
            if (CanMoveToCell(targetCell))
            {
                pendingOrders.Dequeue();
                MoveToCell(targetCell);
                Debug.Log($"{name}: движение на {targetCell}, осталось приказов: {pendingOrders.Count}, AP осталось: {currentAP}");
                OnAllyAction?.Invoke();
            }
            else
            {
                pendingOrders.Dequeue();
                Debug.Log($"{name}: не может выполнить движение, приказ удалён. Очередь: {pendingOrders.Count}");
            }
            return;
        }
        else if (order.type == OrderType.Wait)
        {
            pendingOrders.Dequeue();
            Debug.Log($"{name}: выполнен приказ ожидания, AP осталось: {currentAP}");
            OnAllyAction?.Invoke();
            return;
        }

        // Обработка умений (Skill) – без изменений
        Skill skill = order.skill;
        Vector3Int startCell = GetCurrentCell();
        Vector3Int orderTargetCell = order.targetCell;

        if (skill.areaType != SkillAreaType.Line && skill.areaType != SkillAreaType.Cone)
        {
            if (!gridBuilder.IsInBounds(orderTargetCell) && skill.areaType != SkillAreaType.Self)
            {
                Debug.Log($"{name}: цель вне карты");
                pendingOrders.Dequeue();
                return;
            }
        }

        Vector3Int direction = Vector3Int.zero;
        if (skill.requiresDirection)
        {
            int dx = Mathf.Clamp(orderTargetCell.x - startCell.x, -1, 1);
            int dy = Mathf.Clamp(orderTargetCell.y - startCell.y, -1, 1);
            direction = new Vector3Int(dx, dy, 0);
        }

        Vector3Int? actualTargetCell = null;
        if (skill.requiresDirection && skill.areaType != SkillAreaType.Line && skill.areaType != SkillAreaType.Cone)
        {
            actualTargetCell = FindNearestTargetInDirection(startCell, direction, skill.range, skill);
            if (!actualTargetCell.HasValue)
            {
                // Если цели нет, но умение может применяться на пустую клетку (например, для объектов), используем клетку на максимальной дальности
                actualTargetCell = startCell + direction * skill.range;
            }
        }

        Vector3Int centerCell = skill.requiresDirection ? (actualTargetCell ?? startCell) : orderTargetCell;

        if (!skill.requiresDirection)
        {
            int distance = Mathf.Max(Mathf.Abs(centerCell.x - startCell.x), Mathf.Abs(centerCell.y - startCell.y));
            if (distance > skill.range)
            {
                Debug.Log($"{name}: центр вне досягаемости умения (нужно {skill.range}, расстояние {distance})");
                pendingOrders.Dequeue();
                return;
            }
        }

        bool executed = false;

        switch (skill.areaType)
        {
            case SkillAreaType.Single:
                executed = ApplySkillEffectAtCell(skill, centerCell, direction);
                break;
            case SkillAreaType.Line:
                for (int i = 1; i <= skill.range; i++)
                {
                    Vector3Int cell = startCell + direction * i;
                    if (!gridBuilder.IsInBounds(cell)) break;
                    ApplySkillEffectAtCell(skill, cell, direction);
                }
                executed = true;
                break;
            case SkillAreaType.Cross:
                {
                    HashSet<Vector3Int> cellsSet = new HashSet<Vector3Int>();
                    Vector3Int[] crossDirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
                    for (int r = 0; r <= skill.areaRange; r++)
                    {
                        foreach (var dir in crossDirs)
                        {
                            Vector3Int cell = centerCell + dir * r;
                            if (!gridBuilder.IsInBounds(cell)) continue;
                            cellsSet.Add(cell);
                        }
                    }
                    foreach (var cell in cellsSet)
                    {
                        // Передаём направление от центра к клетке (может быть (0,0) для центра)
                        ApplySkillEffectAtCell(skill, cell, cell - centerCell);
                    }
                    executed = true;
                    break;
                }
            case SkillAreaType.Circle:
                {
                    HashSet<Vector3Int> cellsSet = new HashSet<Vector3Int>();
                    for (int dx = -skill.areaRange; dx <= skill.areaRange; dx++)
                    {
                        for (int dy = -skill.areaRange; dy <= skill.areaRange; dy++)
                        {
                            Vector3Int cell = centerCell + new Vector3Int(dx, dy, 0);
                            if (!gridBuilder.IsInBounds(cell)) continue;
                            cellsSet.Add(cell);
                        }
                    }
                    foreach (var cell in cellsSet)
                    {
                        // Направление не имеет значения для круга, передаём (0,0)
                        ApplySkillEffectAtCell(skill, cell, Vector3Int.zero);
                    }
                    executed = true;
                    break;
                }
            case SkillAreaType.Cone:
                {
                    for (int dx = -skill.range; dx <= skill.range; dx++)
                    {
                        for (int dy = -skill.range; dy <= skill.range; dy++)
                        {
                            Vector3Int offset = new Vector3Int(dx, dy, 0);
                            if (offset == Vector3Int.zero) continue;
                            Vector3Int cell = startCell + offset;
                            if (!gridBuilder.IsInBounds(cell)) continue;
                            int dist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                            if (dist > skill.range) continue;
                            Vector2 dirVec = new Vector2(direction.x, direction.y);
                            Vector2 offsetVec = new Vector2(offset.x, offset.y);
                            if (dirVec == Vector2.zero) continue;
                            float angle = Vector2.Angle(dirVec, offsetVec);
                            if (angle <= 45f)
                            {
                                ApplySkillEffectAtCell(skill, cell, direction);
                            }
                        }
                    }
                    executed = true;
                    break;
                }
            case SkillAreaType.Self:
                ApplySkillEffectAtCell(skill, startCell, Vector3Int.zero);
                executed = true;
                break;
            default:
                Debug.Log($"Тип области {skill.areaType} не поддерживается");
                break;
        }

        if (executed)
        {
            pendingOrders.Dequeue();
            Debug.Log($"{name}: использовал умение {skill.skillName}, осталось приказов: {pendingOrders.Count}, AP осталось: {currentAP}");
            OnAllyAction?.Invoke();
        }
        else
        {
            pendingOrders.Dequeue();
            Debug.Log($"{name}: не удалось применить умение {skill.skillName}, приказ удалён");
        }
    }

    private Vector3Int? FindNearestTargetInDirection(Vector3Int start, Vector3Int direction, int maxRange, Skill skill)
    {
        for (int i = 1; i <= maxRange; i++)
        {
            Vector3Int cell = start + direction * i;
            if (!gridBuilder.IsInBounds(cell)) break;

            GameObject occupant = GetOccupant(cell);
            if (occupant != null)
            {
                PawnAI pawn = occupant.GetComponent<PawnAI>();
                if (pawn != null)
                {
                    if (skill.targetsEnemies && (pawn is EnemyAI))
                        return cell;
                    if (skill.targetsAllies && (pawn is PlayerAI || pawn is AllyAI))
                        return cell;
                }
                if (skill.targetsObjects)
                {
                    ObjectData obj = occupant.GetComponent<ObjectData>();
                    if (obj != null)
                        return cell;
                }
            }
        }
        return null;
    }

    private bool ApplySkillEffectAtCell(Skill skill, Vector3Int cell, Vector3Int direction)
    {
        bool applied = false;

        GameObject occupant = GetOccupant(cell);
        if (occupant != null)
        {
            PawnAI pawn = occupant.GetComponent<PawnAI>();
            if (pawn != null)
            {
                bool isEnemy = pawn is EnemyAI;
                bool isAlly = pawn is PlayerAI || pawn is AllyAI;

                // Урон
                if (skill.damage > 0)
                {
                    bool canDamage = (skill.targetsEnemies && isEnemy) || (skill.targetsAllies && isAlly);
                    if (canDamage)
                    {
                        pawn.TakeDamage(skill.damage, direction);
                        applied = true;
                    }
                }

                // Лечение
                if (skill.heal > 0)
                {
                    bool canHeal = (skill.targetsEnemies && isEnemy) || (skill.targetsAllies && isAlly);
                    if (canHeal)
                    {
                        pawn.Heal(skill.heal);
                        applied = true;
                    }
                }
            }
        }

        // Объекты
        if (skill.targetsObjects)
        {
            ObjectData obj = gridBuilder.GetObjectAt(cell);
            if (obj != null)
            {
                // Урон по объекту
                if (skill.damage > 0 && obj.isDestructible)
                {
                    obj.TakeDamage(skill.damage);
                    applied = true;
                }
                // Лечение объекта
                if (skill.heal > 0 && obj.isDestructible)
                {
                    obj.Heal(skill.heal);
                    applied = true;
                }
            }
        }

        // Самолечение (если клетка пуста и это сам кастер)
        if (!applied && cell == GetCurrentCell() && skill.heal > 0)
        {
            Heal(skill.heal);
            applied = true;
        }

        return applied;
    }

    // Метод для лечения (вызывается из умений)
    public void Heal(int amount)
    {
        currentHealth += amount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        UpdateHealthUI(); // ADDED: обновление сердечек
        Debug.Log($"{name} вылечен на {amount}");
    }

    // Возвращает все клетки, которые союзник планирует занять (последовательность движений)
    public List<Vector3Int> GetAllMoveTargets()
    {
        List<Vector3Int> cells = new List<Vector3Int>();
        Vector3Int current = GetCurrentCell();
        foreach (Order order in pendingOrders)
        {
            if (order.type == OrderType.Move)
            {
                current += order.direction;
                cells.Add(current);
            }
            // умения не меняют позицию
        }
        return cells;
    }

    public Vector3Int GetFinalPosition()
    {
        List<Vector3Int> targets = GetAllMoveTargets();
        if (targets.Count > 0)
            return targets[targets.Count - 1];
        else
            return GetCurrentCell();
    }

    public Vector3Int? GetNextMoveTarget()
    {
        if (pendingOrders.Count == 0) return null;
        Order next = pendingOrders.Peek();
        if (next.type != OrderType.Move) return null;
        return GetCurrentCell() + next.direction;
    }

    private IEnumerator AttackEffect()
    {
        isAttacking = true;
        if (rend != null)
        {
            Color original = rend.material.color;
            rend.material.color = Color.red;
            yield return new WaitForSeconds(0.2f);
            rend.material.color = original;
        }
        isAttacking = false;
    }

    public void ResetAP()
    {
        currentAP = maxAP;
    }

    public void ClearOrders()
    {
        pendingOrders.Clear();
        Debug.Log($"{name}: очередь приказов очищена");
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            if (isSelected)
            {
                if (blinkCoroutine != null) StopCoroutine(blinkCoroutine);
                blinkCoroutine = StartCoroutine(BlinkRoutine());
            }
            else
            {
                if (blinkCoroutine != null)
                {
                    StopCoroutine(blinkCoroutine);
                    blinkCoroutine = null;
                }
                if (rend != null)
                    rend.material.color = originalColor;
            }
        }
    }

    private IEnumerator BlinkRoutine()
    {
        if (rend == null) yield break;

        Color brightColor = originalColor * brightnessMultiplier;
        brightColor.r = Mathf.Min(brightColor.r, 1f);
        brightColor.g = Mathf.Min(brightColor.g, 1f);
        brightColor.b = Mathf.Min(brightColor.b, 1f);

        float halfPeriod = blinkSpeed / 2f;

        while (true)
        {
            float elapsed = 0f;
            while (elapsed < halfPeriod)
            {
                float t = elapsed / halfPeriod;
                rend.material.color = Color.Lerp(originalColor, brightColor, t);
                elapsed += Time.deltaTime;
                yield return null;
            }
            rend.material.color = brightColor;
            yield return new WaitForSeconds(0.05f);

            elapsed = 0f;
            while (elapsed < halfPeriod)
            {
                float t = elapsed / halfPeriod;
                rend.material.color = Color.Lerp(brightColor, originalColor, t);
                elapsed += Time.deltaTime;
                yield return null;
            }
            rend.material.color = originalColor;
            yield return new WaitForSeconds(0.05f);
        }
    }

    public List<Vector3Int> GetPlannedSkillTargets()
    {
        List<Vector3Int> cells = new List<Vector3Int>();
        Vector3Int currentPos = GetCurrentCell(); // начальная позиция до выполнения приказов
        foreach (Order order in pendingOrders)
        {
            if (order.type == OrderType.Skill && order.skill != null)
            {
                // Определяем направление умения (для направленных)
                Vector3Int direction = order.targetCell - currentPos;
                int dx = Mathf.Clamp(direction.x, -1, 1);
                int dy = Mathf.Clamp(direction.y, -1, 1);
                direction = new Vector3Int(dx, dy, 0);

                // Получаем все клетки, на которые повлияет умение
                List<Vector3Int> affected = GetSkillAffectedCells(order.skill, currentPos, direction, order.targetCell);
                cells.AddRange(affected);
            }
            // Если приказ движения, обновляем позицию для следующих приказов
            if (order.type == OrderType.Move)
            {
                currentPos += order.direction;
            }
            // Wait не меняет позицию
        }
        return cells;
    }

    public List<Vector3Int> GetSkillAffectedCells(Skill skill, Vector3Int casterPos, Vector3Int direction, Vector3Int targetCell)
{
    List<Vector3Int> result = new List<Vector3Int>();
    if (gridBuilder == null) return result;

    // Определяем чанк-якорь в зависимости от типа умения
    Vector2Int anchorChunk;
    if (skill.areaType == SkillAreaType.Line || skill.areaType == SkillAreaType.Cone || skill.areaType == SkillAreaType.Self)
        anchorChunk = LocationManager.Instance.GetChunkFromGlobalCell(casterPos);
    else
        anchorChunk = LocationManager.Instance.GetChunkFromGlobalCell(targetCell);

    switch (skill.areaType)
    {
        case SkillAreaType.Single:
        {
            if (gridBuilder.IsInBounds(targetCell) && 
                LocationManager.Instance.GetChunkFromGlobalCell(targetCell) == anchorChunk)
            {
                int targetHeight = gridBuilder.GetHeightAt(targetCell.x, targetCell.y);
                int casterHeight = gridBuilder.GetHeightAt(casterPos.x, casterPos.y);
                if (Mathf.Abs(targetHeight - casterHeight) <= 1)
                    result.Add(targetCell);
            }
            break;
        }

        case SkillAreaType.Line:
        {
            int lastHeight = gridBuilder.GetHeightAt(casterPos.x, casterPos.y);
            for (int i = 1; i <= skill.range; i++)
            {
                Vector3Int cell = casterPos + direction * i;
                if (!gridBuilder.IsInBounds(cell)) break;
                if (LocationManager.Instance.GetChunkFromGlobalCell(cell) != anchorChunk) break; // выход за чанк
                int cellHeight = gridBuilder.GetHeightAt(cell.x, cell.y);
                if (Mathf.Abs(cellHeight - lastHeight) > 1) break;
                lastHeight = cellHeight;
                result.Add(cell);
            }
            break;
        }

        case SkillAreaType.Cross:
        {
            int targetHeight = gridBuilder.GetHeightAt(targetCell.x, targetCell.y);
            Vector3Int[] crossDirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
            for (int r = 0; r <= skill.areaRange; r++)
            {
                foreach (var dir in crossDirs)
                {
                    Vector3Int cell = targetCell + dir * r;
                    if (!gridBuilder.IsInBounds(cell)) continue;
                    if (LocationManager.Instance.GetChunkFromGlobalCell(cell) != anchorChunk) continue;
                    int cellHeight = gridBuilder.GetHeightAt(cell.x, cell.y);
                    if (Mathf.Abs(cellHeight - targetHeight) <= 1)
                        result.Add(cell);
                }
            }
            break;
        }

        case SkillAreaType.Circle:
        {
            int targetHeight = gridBuilder.GetHeightAt(targetCell.x, targetCell.y);
            for (int dx = -skill.areaRange; dx <= skill.areaRange; dx++)
            {
                for (int dy = -skill.areaRange; dy <= skill.areaRange; dy++)
                {
                    Vector3Int cell = targetCell + new Vector3Int(dx, dy, 0);
                    if (!gridBuilder.IsInBounds(cell)) continue;
                    if (LocationManager.Instance.GetChunkFromGlobalCell(cell) != anchorChunk) continue;
                    int cellHeight = gridBuilder.GetHeightAt(cell.x, cell.y);
                    if (Mathf.Abs(cellHeight - targetHeight) <= 1)
                        result.Add(cell);
                }
            }
            break;
        }

        case SkillAreaType.Cone:
        {
            int casterHeight = gridBuilder.GetHeightAt(casterPos.x, casterPos.y);
            for (int dx = -skill.range; dx <= skill.range; dx++)
            {
                for (int dy = -skill.range; dy <= skill.range; dy++)
                {
                    Vector3Int offset = new Vector3Int(dx, dy, 0);
                    if (offset == Vector3Int.zero) continue;
                    Vector3Int cell = casterPos + offset;
                    if (!gridBuilder.IsInBounds(cell)) continue;
                    if (LocationManager.Instance.GetChunkFromGlobalCell(cell) != anchorChunk) continue;

                    int dist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    if (dist > skill.range) continue;

                    Vector2 dirVec = new Vector2(direction.x, direction.y).normalized;
                    Vector2 offsetVec = new Vector2(offset.x, offset.y).normalized;
                    float angle = Vector2.Angle(dirVec, offsetVec);
                    if (angle <= 45f)
                    {
                        int cellHeight = gridBuilder.GetHeightAt(cell.x, cell.y);
                        if (Mathf.Abs(cellHeight - casterHeight) <= 1)
                            result.Add(cell);
                    }
                }
            }
            break;
        }

        case SkillAreaType.Self:
            result.Add(casterPos);
            break;

        default:
            break;
    }
    return result;
}

public List<Vector3Int> GetSkillAffectedCellsForTarget(Skill skill, Vector3Int casterPos, Vector3Int targetCell)
{
    return GetSkillAffectedCells(skill, casterPos, Vector3Int.zero, targetCell);
}

    public OrderType GetNextOrderType()
    {
        if (pendingOrders.Count == 0) return OrderType.None;
        return pendingOrders.Peek().type;
    }

    public (Skill skill, Vector3Int targetCell)? GetNextSkillInfo()
    {
        if (pendingOrders.Count == 0) return null;
        Order next = pendingOrders.Peek();
        if (next.type != OrderType.Skill) return null;
        return (next.skill, next.targetCell);
    }

    protected override void OnDeath()
    {
        if (AllyManager.Instance != null)
            AllyManager.Instance.UnregisterAlly(this);
        // ADDED: уничтожаем UI здоровья
        if (healthUIInstance != null) Destroy(healthUIInstance);
        Destroy(gameObject);
    }
}
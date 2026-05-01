using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Collections.Generic;



public class PlayerAI : PawnAI
{
    [Header("Система очков действия (AP)")]
    [SerializeField] private int maxAP = 10;
    private int currentAP;

    [Header("Стоимость действий")]
    [SerializeField] private int moveCost = 1;
    [SerializeField] private int attackCost = 5; // используется только вне тактики
    [SerializeField] private int defenceCost = 3;

    public int MoveCost => moveCost;
    public int AttackCost => attackCost;
    public int DefenceCost => defenceCost;

    [Header("Детекторы")]
    [SerializeField] private Collider frontCollider;  // маленький коллайдер впереди (для обнаружения лестниц и других объектов)
    [SerializeField] private Collider bodyCollider;   // капсульный коллайдер вокруг тела (для нахождения на лестнице)

    private bool isInFrontOfLadder = false;
    private bool isOnLadder = false;
    private ObjectData currentLadder = null;

    [Header("UI Ссылки")]
    [SerializeField] private Image apFillImage;
    [SerializeField] private Image healthFillImage;
    [SerializeField] private Text healthText;

    [Header("Инвентарь")]
    public Inventory inventory = new Inventory();
    [SerializeField] private Text inventoryText;

    private PlayerControls controls;
    public InputAction moveAction;
    private bool XMovePref = true;

    [Header("Контроль союзников")]
    [SerializeField] private int maxHistoryLength = 5;
    private Queue<Vector3Int> positionHistory = new Queue<Vector3Int>();
    public Queue<Vector3Int> GetPositionHistory() => positionHistory;
    private AllyManager allyManager;
    private AllyAI selectedAlly = null;
    public AllyAI GetSelectedAlly() => selectedAlly;
    public int GetSelectedSkillIndex() => selectedSkillIndex;
    public Vector3Int? HoveredCell { get; private set; }
    private bool wasMoving = false;

    private InputAction attackAction;
    private bool attackPressed = false;
    private float attackCooldown = 0f;
    private Animation anim;

    private InputAction defendAction;
    private bool defendPressed = false;
    private float defendWearing = 0f;

    public static System.Action OnPlayerAction;
    public float realTimeDuration;

    private Vector3 targetWorldPos; // целевая позиция (с плавным изменением Y)
    private float currentHeight;    // текущая высота (для интерполяции)

    private int selectedSkillIndex = 0; // 0 = ничего не выбрано, 0 = атака, 1,2,3 = другие умения

    protected override void Start()
    {
        base.Start();
        currentAP = maxAP;
        SetHealth(maxHealth);
        allyManager = FindObjectOfType<AllyManager>();
        StartCoroutine(SetStartPositionAfterFrame());

        anim = gameObject.GetComponent<Animation>();
        anim["sj001_skill1"].speed = 2.25f;
        anim["sj001_hurt"].speed = 1.5f;
    }

    private System.Collections.IEnumerator SetStartPositionAfterFrame()
    {
        yield return null;
        SetStartPosition();
    }

    void Update()
{
    if (isDead) return;
    UpdateHoveredCell();

    attackPressed = attackAction.IsPressed();
    defendPressed = defendAction.IsPressed();

    if (LocationManager.Instance != null && LocationManager.Instance.IsRotating) return;

    if (LocationManager.TacticMode)
    {
        if (allyManager != null && allyManager.IsDeploymentMode)
        {
            HandleDeploymentMode();
        }
        else
        {
            HandleTacticMode();
        }
    }
    else
    {
        HandleRealTimeMode();
    }

    // Уменьшаем таймер блокировки (общий для всех режимов)
    if (inputBlockTimer > 0) inputBlockTimer -= Time.deltaTime;

    // Сохраняем состояние движения до обновления
    bool wasMovingPrev = isMoving;
    UpdateMovement();
    // Если движение только что закончилось, проверяем лут
    if (!isMoving && wasMovingPrev)
    {
        CheckForLootAtCurrentCell();
    }

    UpdateUI();
}

private void HandleDeploymentMode()
{
    if (Mouse.current.leftButton.wasPressedThisFrame)
    {
        var cell = GetCellUnderCursor();
        if (cell.HasValue)
            allyManager.TryPlaceAlly(cell.Value);
    }
    else if (Mouse.current.rightButton.wasPressedThisFrame)
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            AllyAI ally = hit.collider.GetComponent<AllyAI>();
            if (ally != null)
            {
                allyManager.RemoveAlly(ally);
            }
        }
    }
}

private void HandleTacticMode()
{
    // Выбор союзника и применение умений по клику
    if (Mouse.current.leftButton.wasPressedThisFrame)
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            AllyAI clickedAlly = hit.collider.GetComponent<AllyAI>();

            if (selectedAlly != null && selectedSkillIndex != 0)
            {
                Skill selectedSkill = selectedAlly.GetComponent<SkillInstance>()?.GetSkill(selectedSkillIndex);
                if (selectedSkill != null && !selectedSkill.requiresDirection)
                {
                    Vector3Int targetCell;
                    if (clickedAlly != null)
                        targetCell = clickedAlly.GetCurrentCell();
                    else
                    {
                        var cell = GetCellUnderCursor();
                        if (!cell.HasValue) return;
                        targetCell = cell.Value;
                    }
                    if (gridBuilder.IsInBounds(targetCell))
                    {
                        ApplySkill(selectedAlly, selectedSkillIndex, targetCell);
                    }
                    return;
                }
            }

            if (clickedAlly != null)
            {
                UnityEngine.Debug.Log($"Союзник {clickedAlly} выбран");
                allyManager.DeselectAll();
                clickedAlly.IsSelected = true;
                selectedAlly = clickedAlly;
                selectedSkillIndex = 0;
                UpdateUI(); // обновляем UI при смене выделенного союзника
            }
        }
    }

    // Обработка правой кнопки: сначала сброс выбранного умения (если не атака), иначе отмена последнего приказа
    if (Mouse.current.rightButton.wasPressedThisFrame)
    {
        if (selectedSkillIndex != 0)
        {
            selectedSkillIndex = 0;
            Debug.Log("Выбор умения сброшен на атаку");
            UpdateUI();
            return;
        }
        else if (selectedAlly != null)
        {
            selectedAlly.CancelLastOrder();
            UpdateUI(); // обновляем UI после отмены приказа (AP восстановились)
            return;
        }
    }

    // Выбор умения по цифровым клавишам
    if (selectedAlly != null)
    {
        if (Keyboard.current.digit0Key.wasPressedThisFrame) ToggleSkillSelection(0);
        else if (Keyboard.current.digit1Key.wasPressedThisFrame) ToggleSkillSelection(1);
        else if (Keyboard.current.digit2Key.wasPressedThisFrame) ToggleSkillSelection(2);
        else if (Keyboard.current.digit3Key.wasPressedThisFrame) ToggleSkillSelection(3);
        else if (Keyboard.current.digit4Key.wasPressedThisFrame) ToggleSkillSelection(4);
        else if (Keyboard.current.digit5Key.wasPressedThisFrame) ToggleSkillSelection(5);
        else if (Keyboard.current.digit6Key.wasPressedThisFrame) ToggleSkillSelection(6);
        else if (Keyboard.current.digit7Key.wasPressedThisFrame) ToggleSkillSelection(7);
        else if (Keyboard.current.digit8Key.wasPressedThisFrame) ToggleSkillSelection(8);
        else if (Keyboard.current.digit9Key.wasPressedThisFrame) ToggleSkillSelection(9);
    }

    // Действия с выделенным союзником
    if (selectedAlly != null)
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            selectedAlly.GiveWaitOrder();
            UpdateUI(); // обновляем UI после ожидания (AP восстановились)
            return;
        }

        Vector3Int startPos = selectedAlly.GetFinalPosition();
        Skill selectedSkill = selectedAlly.GetComponent<SkillInstance>()?.GetSkill(selectedSkillIndex);

        // Ненаправленные умения по клику мыши
        if (selectedSkill != null && !selectedSkill.requiresDirection && Mouse.current.leftButton.wasPressedThisFrame)
        {
            var targetCell = GetCellUnderCursor();
            if (targetCell.HasValue)
            {
                if (ApplySkill(selectedAlly, selectedSkillIndex, targetCell.Value))
                {
                    UpdateUI();
                }
            }
        }

        // Обработка направленных действий (стрелки)
        Vector2 moveInput = moveAction.ReadValue<Vector2>();
        bool hasInput = moveInput.magnitude > 0.5f;

        if (hasInput && !wasMoving)
        {
            Vector3Int direction = GetMoveDirection(moveInput);
            if (direction != Vector3Int.zero)
            {
                if (selectedSkill != null && selectedSkill.requiresDirection && attackPressed)
                {
                    Vector3Int targetCell = startPos + direction * selectedSkill.range;
                    if (ApplySkill(selectedAlly, selectedSkillIndex, targetCell))
                    {
                        UpdateUI(); // обновляем UI после применения умения
                    }
                }
                else if (!attackPressed)
                {
                    Vector3Int targetCell = startPos + direction;
                    if (CanGiveMoveOrder(selectedAlly, startPos, direction, targetCell, out int actualCost))
                    {
                        selectedAlly.GiveMoveOrder(direction, actualCost);
                        UnityEngine.Debug.Log($"Приказ отдан союзнику {selectedAlly.name}: движение {direction}, стоимость {actualCost}");
                        UpdateUI(); // обновляем UI после выдачи приказа на движение
                    }
                }
            }
        }
        wasMoving = hasInput;
    }
}

private void HandleRealTimeMode()
{
    ActionTimer += Time.deltaTime;
    if (attackCooldown < ActionSpeed) attackCooldown += Time.deltaTime;

    Vector2 moveInput = moveAction.ReadValue<Vector2>();

    if (ActionTimer >= ActionSpeed)
    {
        UpdateAP();
        ActionTimer = 0f;
    }

    if (inputBlockTimer <= 0 && !isMoving && !isAttacking && moveInput.magnitude > 0.5f)
    {
        Vector3Int direction = GetMoveDirection(moveInput);
        if (direction != Vector3Int.zero)
        {
            if (attackPressed)
            {
                if (attackCooldown >= ActionSpeed && CanAffordAction(attackCost))
                {
                    anim.Play("sj001_skill1");
                    isAttacking = true;
                    Attack(direction);
                    attackCooldown = 0f;
                    defendWearing = 0f;
                    isDefending = false;
                }
            }
            else if (defendPressed)
            {
                if (defendWearing <= 0 && defendDirection == Vector3Int.zero && CanAffordAction(defenceCost))
                {
                    if (!LocationManager.Instance.AllEnemiesDefeated) currentAP -= defenceCost;
                    UnityEngine.Debug.Log("Защита поднята");
                    isDefending = true;
                    defendDirection = direction;
                    RotateTowards(direction);
                    defendWearing = 3;
                    UpdateUI();
                }
            }
            else if (CanAffordAction(moveCost))
            {
                MovePlayer(direction);
                defendWearing = 0f;
                isDefending = false;
            }
        }
    }

    if (!isMoving && !isAttacking && !anim.IsPlaying("sj001_skill1") && !anim.IsPlaying("sj001_hurt")) anim.Play("sj001_wait");
    
    if (defendWearing <= 0)
    {
        defendDirection = Vector3Int.zero;
        isDefending = false;
    }
    else defendWearing -= Time.deltaTime;
}

private bool CanGiveMoveOrder(AllyAI ally, Vector3Int startPos, Vector3Int direction, Vector3Int targetCell, out int actualCost)
{
    actualCost = 0;

    // Проверка границ карты
    if (!gridBuilder.IsInBounds(targetCell))
    {
        UnityEngine.Debug.Log("Клетка вне карты");
        return false;
    }

    // Проверка на смену чанка (запрещено во время боя)
    Vector2Int startChunk = LocationManager.Instance.GetChunkFromGlobalCell(startPos);
    Vector2Int targetChunk = LocationManager.Instance.GetChunkFromGlobalCell(targetCell);
    if (startChunk != targetChunk)
    {
        UnityEngine.Debug.Log("Нельзя отдавать приказы за пределами текущего чанка во время боя");
        return false;
    }

    // Используем общий метод проверки возможности движения из PawnAI
    // Он проверяет высоту, объекты, занятость и лестницы
    if (!ally.CanMoveToCell(targetCell, false))
    {
        // CanMoveToCell уже выводит своё сообщение, можно не дублировать
        return false;
    }

    // Планы других юнитов
    HashSet<Vector3Int> plannedCells = GetAllPlannedCellsExcept(ally);
    if (plannedCells.Contains(targetCell))
    {
        UnityEngine.Debug.Log("Клетка уже запланирована другим юнитом");
        return false;
    }

    // Учёт труднопроходимой местности на стартовой позиции
    ObjectData startObj = gridBuilder.GetObjectAt(startPos);
    bool onDifficult = startObj != null && startObj.isDifficultTerrain;
    int extraCost = onDifficult ? moveCost : 0;
    actualCost = moveCost + extraCost;

    return true;
}

private bool ApplySkill(AllyAI ally, int skillIndex, Vector3Int targetCell)
{
    SkillInstance skillInst = ally.GetComponent<SkillInstance>();
    if (skillInst == null) return false;
    Skill skill = skillInst.GetSkill(skillIndex);
    if (skill == null) return false;

    Vector3Int startPos = ally.GetFinalPosition();
    ObjectData startObj = gridBuilder.GetObjectAt(startPos);
    bool onDifficult = startObj != null && startObj.isDifficultTerrain;
    int extraCost = onDifficult ? moveCost : 0;
    int totalCost = skill.apCost + extraCost;

    // Проверка дальности для нацеленных умений
    if (!skill.requiresDirection)
    {
        int distance = Mathf.Max(Mathf.Abs(targetCell.x - startPos.x), Mathf.Abs(targetCell.y - startPos.y));
        if (distance > skill.range)
        {
            UnityEngine.Debug.Log($"Цель вне досягаемости умения (нужно {skill.range}, расстояние {distance})");
            return false;
        }
    }

    ally.GiveSkillOrder(skill, targetCell, totalCost);
    UnityEngine.Debug.Log($"Приказ на умение {skill.skillName} отдан союзнику {ally.name} на клетку {targetCell}, стоимость {totalCost}");
    return true;
}

    // Переключение выбора умения
    private void ToggleSkillSelection(int index)
    {
        if (selectedAlly == null) return;
        SkillInstance skillInst = selectedAlly.GetComponent<SkillInstance>();
        if (skillInst == null)
        {
            Debug.Log("У союзника нет компонента SkillInstance");
            return;
        }
        if (!skillInst.HasSkill(index))
        {
            Debug.Log($"У союзника нет навыка с индексом {index}");
            return;
        }
        if (selectedSkillIndex == index)
        {
            // Повторное нажатие на ту же цифру – выбираем атаку (индекс 0)
            selectedSkillIndex = 0;
            Debug.Log($"Выбран навык атаки (индекс 0) для союзника {selectedAlly.name}");
        }
        else
        {
            selectedSkillIndex = index;
            Debug.Log($"Выбран навык {index} для союзника {selectedAlly.name}");
        }
    }

    // Собирает все клетки, которые планируют занять юниты, кроме указанного
    private HashSet<Vector3Int> GetAllPlannedCellsExcept(PawnAI except)
    {
        HashSet<Vector3Int> cells = new HashSet<Vector3Int>();

        foreach (var enemy in FindObjectsOfType<EnemyAI>())
        {
            if (enemy == except) continue;
            foreach (var cell in enemy.GetAllTargetCells())
                cells.Add(cell);
        }

        foreach (var ally in FindObjectsOfType<AllyAI>())
        {
            if (ally == except) continue;
            foreach (var cell in ally.GetAllMoveTargets())
                cells.Add(cell);
        }

        return cells;
    }

    private void SetStartPosition()
    {


        Vector3Int startCell = new Vector3Int(5, 5, 0);
        InitializeAtCell(startCell);
        AddPositionToHistory(GetCurrentCell());
        UnityEngine.Debug.Log($"Игрок стартует с {currentHealth} HP и {currentAP} AP в клетке {startCell}");
    }

    public void AddPositionToHistory(Vector3Int pos)
    {
        positionHistory.Enqueue(pos);
        if (positionHistory.Count > maxHistoryLength) positionHistory.Dequeue();
    }

    protected override void UpdateHealthUI()
    {
        if (healthFillImage != null) healthFillImage.fillAmount = (float)currentHealth / maxHealth;
        if (healthText != null) healthText.text = $"{currentHealth}/{maxHealth}";
    }

    protected override void OnDeath()
    {
        controls.Gameplay.Disable();
        anim.Play("sj001_die");
        UnityEngine.Debug.Log("Игрок умер!");
    }

    public override void TakeDamage(int damage, Vector3Int attackDirection)
    {
        anim.Play("sj001_hurt");
        base.TakeDamage(damage, attackDirection);
    }

    void Awake()
    {
        controls = new PlayerControls();
        moveAction = controls.Gameplay.Move;
        attackAction = controls.Gameplay.Attack;
        defendAction = controls.Gameplay.Defend;
    }

    void OnEnable() => controls.Gameplay.Enable();
    void OnDisable() => controls.Gameplay.Disable();

    void UpdateAP()
    {
        if (currentAP < maxAP && !isMoving && !attackPressed && !defendPressed)
            currentAP++;
        UpdateUI();
    }

    void UpdateUI()
{
    if (apFillImage == null) return;

    if (LocationManager.TacticMode && selectedAlly != null)
    {
        // В тактическом режиме и при выделенном союзнике показываем его AP
        apFillImage.fillAmount = (float)selectedAlly.GetCurrentAP() / selectedAlly.GetMaxAP();
    }
    else
    {
        // В реальном времени или без выделения показываем AP игрока
        apFillImage.fillAmount = (float)currentAP / maxAP;
    }
}

    Vector3Int GetMoveDirection(Vector2 input)
    {
        int x = Mathf.RoundToInt(input.x);
        int y = Mathf.RoundToInt(input.y);

        if (x != 0 && y != 0)
        {
            if (XMovePref) y = 0;
            else x = 0;
            XMovePref = !XMovePref;
        }

        if (x == 0 && y == 0) return Vector3Int.zero;

        float cameraAngle = LocationManager.Instance.cameraTransform.eulerAngles.y;
        Vector3 localDir = new Vector3(x, 0, y).normalized;
        Vector3 worldDir = Quaternion.Euler(0, cameraAngle, 0) * localDir;

        float absX = Mathf.Abs(worldDir.x);
        float absZ = Mathf.Abs(worldDir.z);
        int moveX = 0, moveY = 0;

        if (absX > absZ) moveX = (int)Mathf.Sign(worldDir.x);
        else if (absZ > absX) moveY = (int)Mathf.Sign(worldDir.z);
        else if (Random.value < 0.5f) moveX = (int)Mathf.Sign(worldDir.x);
        else moveY = (int)Mathf.Sign(worldDir.z);

        return new Vector3Int(moveX, moveY, 0);
    }

    bool CanAffordAction(int cost) => currentAP >= cost;

    void MovePlayer(Vector3Int direction)
{
    if (LocationManager.Instance != null && LocationManager.Instance.justTransitioned) return;

    Vector3Int currentCell = GetCurrentCell();
    ObjectData currentObj = LocationManager.Instance.GetObjectAt(currentCell);

    // Определяем, стои́м ли мы на труднопроходимой местности (например, луже) – увеличивает стоимость движения
    bool onDifficultTerrain = currentObj != null && currentObj.isDifficultTerrain;
    int effectiveMoveCost = moveCost * (onDifficultTerrain ? 2 : 1);

    // Лестница: подъём/спуск
    if (currentObj != null && currentObj.isLadder)
    {
        Vector3Int ladderDir = GetDirectionFromRotation(currentObj.rotation);
        float baseHeight = LocationManager.Instance.GetTileHeight(currentCell);
        float maxHeight = baseHeight + currentObj.heightInTiles;

        if (direction == ladderDir && CurrentHeight < maxHeight)
        {
            RotateTowards(ladderDir);
            if (!LocationManager.Instance.AllEnemiesDefeated) currentAP -= effectiveMoveCost;
            startPosition = transform.position;
            targetPosition = transform.position + Vector3.up;
            controlPoint = startPosition;
            isMoving = true;
            moveProgress = 0f;
            UpdateUI();
            OnPlayerAction?.Invoke();
            return;
        }
        else if (direction == -ladderDir && CurrentHeight > baseHeight)
        {
            RotateTowards(ladderDir);
            if (!LocationManager.Instance.AllEnemiesDefeated) currentAP -= effectiveMoveCost;
            startPosition = transform.position;
            targetPosition = transform.position - Vector3.up;
            controlPoint = startPosition;
            isMoving = true;
            moveProgress = 0f;
            UpdateUI();
            OnPlayerAction?.Invoke();
            return;
        }
    }

    Vector3Int targetCell = currentCell + direction;

    // Проверка существования локации для целевой клетки
    TileMapData targetLocation = LocationManager.Instance.GetLocationAt(targetCell);
    if (targetLocation == null)
    {
        Debug.Log("В этом направлении нет локации");
        return;
    }

    // Определяем, меняется ли чанк
    Vector2Int currentChunk = LocationManager.Instance.GetChunkFromGlobalCell(currentCell);
    Vector2Int targetChunk = LocationManager.Instance.GetChunkFromGlobalCell(targetCell);

    bool isChangingChunk = currentChunk != targetChunk;

    // Проверка AP с учётом повышенной стоимости
    if (!CanAffordAction(effectiveMoveCost)) return;

    if (isChangingChunk)
    {
        // // Переход в другой чанк – разрешён только если все враги в текущей локации мертвы
        // if (!LocationManager.Instance.AllEnemiesDefeated)
        // {
        //     Debug.Log("Нельзя покинуть локацию: враги ещё есть");
        //     return;
        // }

        // Проверяем возможность войти в целевую клетку (игнорируя ограничение на чанк)
        if (!CanMoveToCell(targetCell, true))
        {
            Debug.Log("Невозможно войти в целевую клетку");
            return;
        }

        // Выполняем переход (камера, загрузка новой локации, тактический режим)
        if (LocationManager.Instance.TryTransition(targetLocation, new Vector3Int(direction.x, direction.y, 0)))
        {
            // После успешного перехода двигаемся на целевую клетку, игнорируя ограничение на чанк
            MoveToCell(targetCell, new Vector3Int(direction.x, direction.y, 0), false, true);
        }
    }
    else
    {
        // Обычное движение внутри локации
        if (!CanMoveToCell(targetCell, false))
        {
            Debug.Log($"Смена чанка: {currentChunk} на {targetChunk}, не могу перейти");
            return;
        }

        if (!LocationManager.Instance.AllEnemiesDefeated) currentAP -= effectiveMoveCost;
        MoveToCell(targetCell);

        positionHistory.Enqueue(GetCurrentCell());
        if (positionHistory.Count > maxHistoryLength) positionHistory.Dequeue();

        UpdateUI();
        OnPlayerAction?.Invoke();
    }
    anim.Play("sj001_run");
    Debug.Log("Бегу>");
    AddPositionToHistory(GetCurrentCell());
}

    void Attack(Vector3Int direction)
    {
        if (!CanAffordAction(attackCost)) { UnityEngine.Debug.Log("Недостаточно AP для атаки!"); return; }

        if (!LocationManager.Instance.AllEnemiesDefeated) currentAP -= attackCost;
        RotateTowards(direction);

        Vector3Int attackCell = GetCurrentCell() + direction;
        CheckHit(attackCell);

        isAttacking = false;

        UpdateUI();
        OnPlayerAction?.Invoke();
    }

    void CheckHit(Vector3Int targetCell)
    {
        GameObject occupant = GetOccupant(targetCell);
        if (occupant != null && occupant != this.gameObject)
        {
            EnemyAI enemy = occupant.GetComponent<EnemyAI>();
            if (enemy != null) { enemy.TakeDamage(1); return; }
        }
        ObjectData obj = gridBuilder.GetObjectAt(targetCell);
        if (obj != null && obj.isDestructible) obj.TakeDamage(1);
    }

    public int GetCurrentAP() => currentAP;

    private void UpdateInventoryUI()
    {
        if (inventoryText != null) inventoryText.text = inventory.GetInventoryText();
    }

    public void AddResource(ResourceType type, int amount)
    {
        inventory.AddResource(type, amount);
        UpdateInventoryUI();
    }

    private Vector3Int GetDirectionFromRotation(int rotation)
    {
        switch (rotation)
        {
            case 0: return Vector3Int.right;
            case 1: return Vector3Int.up;
            case 2: return Vector3Int.left;
            case 3: return Vector3Int.down;
            default: return Vector3Int.zero;
        }
    }

    private void UpdateHoveredCell()
    {
        HoveredCell = GetCellUnderCursor();
    }

    public void DeselectAlly()
    {
        if (selectedAlly != null)
        {
            selectedAlly.IsSelected = false;
            selectedAlly = null;
            UpdateUI(); // сразу переключаем на AP игрока
        }
    }

    private Vector3Int? GetCellUnderCursor()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            // Смещаем точку попадания на половину тайла, чтобы ближе к центру клетки
            Vector3 correctedPoint = hit.point + new Vector3(0.5f, 0f, 0.5f);
            Vector3Int cell = gridBuilder.WorldToCell(correctedPoint);
            if (gridBuilder.IsInBounds(cell))
                return cell;
        }
        return null;
    }

    private void CheckForLootAtCurrentCell()
    {
        Vector3Int cell = GetCurrentCell();
        Vector3 worldPos = LocationManager.Instance.CellToWorld(cell);
        Collider[] hits = Physics.OverlapSphere(worldPos, 0.3f);
        foreach (var hit in hits)
        {
            LootItem loot = hit.GetComponent<LootItem>();
            if (loot != null)
            {
                AddResource(loot.resourceType, loot.amount);
                Destroy(loot.gameObject);
                break;
            }
        }
    }
}
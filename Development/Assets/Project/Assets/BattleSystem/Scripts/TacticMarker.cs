using UnityEngine;
using System.Collections.Generic;

public class TacticMarker : MonoBehaviour
{
    [Header("Префабы маркеров")]
    public GameObject moveMarkerPrefab;      // кружок для движения
    public GameObject skillMarkerPrefab;     // квадрат для областей умений

    [Header("Цвета движения")]
    public Color allyMoveColor = Color.green;
    public Color enemyMoveColor = Color.red;

    [Header("Цвета умений")]
    public Color availableColor = new Color(1f, 1f, 1f, 0.3f);
    public Color queuedSkillColor = new Color(1f, 1f, 1f, 0.8f);
    public Color hoverColor = Color.yellow;

    [Header("Настройки маркеров")]
    public float markerHeightOffset = 0.2f;

    [Header("Настройки рамки")]
    public Material borderMaterial;
    public float borderWidth = 0.1f;
    public float borderHeightOffset = 0.05f;
    public Color borderColor = Color.white;

    [Header("Пульсация")]
    public float pulseSpeed = 1f;

    // Пул для маркеров движения
    private Queue<GameObject> moveMarkerPool = new Queue<GameObject>();
    private List<GameObject> activeMoveMarkers = new List<GameObject>();

    // Пул для маркеров умений
    private Queue<GameObject> skillMarkerPool = new Queue<GameObject>();
    private List<GameObject> activeSkillMarkers = new List<GameObject>();
    private List<GameObject> activeHoverMarkers = new List<GameObject>();

    // Пул для кубов границ
    private Queue<GameObject> borderCubePool = new Queue<GameObject>();
    private List<GameObject> activeBorderCubes = new List<GameObject>();

    void LateUpdate()
    {
        // Возвращаем все маркеры и кубы в пул
        ReturnAllMarkersToPool();

        PlayerAI player = FindObjectOfType<PlayerAI>();
        if (player == null) return;

        // В тактическом режиме показываем все запланированные действия
        if (LocationManager.TacticMode)
        {
            // Враги – все целевые клетки движения
            EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();
            foreach (var enemy in enemies)
            {
                if (!enemy.isMoving)
                {
                    List<Vector3Int> targets = enemy.GetAllTargetCells();
                    foreach (var cell in targets)
                        CreateMoveMarker(enemy, cell, enemyMoveColor);
                }
            }

            // Союзники – все запланированные движения и умения
            AllyAI[] allies = FindObjectsOfType<AllyAI>();
            HashSet<Vector3Int> plannedSkillCells = new HashSet<Vector3Int>();

            foreach (var ally in allies)
            {
                if (!ally.isMoving)
                {
                    List<Vector3Int> moveTargets = ally.GetAllMoveTargets();
                    foreach (var cell in moveTargets)
                        CreateMoveMarker(ally, cell, allyMoveColor);

                    List<Vector3Int> skillTargets = ally.GetPlannedSkillTargets();
                    foreach (var cell in skillTargets)
                    {
                        plannedSkillCells.Add(cell);
                        CreateSkillMarker(cell, queuedSkillColor, activeSkillMarkers);
                    }
                }
            }

            if (plannedSkillCells.Count > 0)
                DrawBorderEdges(plannedSkillCells, queuedSkillColor);

            // Переменные для текущего выделенного союзника и умения
            AllyAI selectedAlly = player.GetSelectedAlly();
            int skillIndex = player.GetSelectedSkillIndex();
            Vector3Int? hoveredCell = player.HoveredCell;

            // Множество клеток, которые будут поражены при наведении
            HashSet<Vector3Int> hoverCells = new HashSet<Vector3Int>();

            // Подсветка при наведении (создаёт маркеры области поражения)
            if (selectedAlly != null && skillIndex != -1 && hoveredCell.HasValue && !selectedAlly.isMoving)
            {
                SkillInstance skillInst = selectedAlly.GetComponent<SkillInstance>();
                Skill skill = skillInst?.GetSkill(skillIndex);
                if (skill != null && !skill.requiresDirection)
                {
                    Vector3Int casterPos = selectedAlly.GetFinalPosition();
                    int distance = Mathf.Max(Mathf.Abs(hoveredCell.Value.x - casterPos.x), Mathf.Abs(hoveredCell.Value.y - casterPos.y));
                    if (distance <= skill.range)
                    {
                        List<Vector3Int> affectedCells;
                        if (skill.areaType == SkillAreaType.Line || skill.areaType == SkillAreaType.Cone)
                        {
                            // Для направленных умений вычисляем направление
                            Vector3Int direction = new Vector3Int(
                                Mathf.Clamp(hoveredCell.Value.x - casterPos.x, -1, 1),
                                Mathf.Clamp(hoveredCell.Value.y - casterPos.y, -1, 1),
                                0);
                            affectedCells = selectedAlly.GetSkillAffectedCells(skill, casterPos, direction, hoveredCell.Value);
                        }
                        else
                        {
                            affectedCells = selectedAlly.GetSkillAffectedCellsForTarget(skill, casterPos, hoveredCell.Value);
                        }

                        hoverCells.UnionWith(affectedCells);
                        float pulse = Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f;
                        Color currentHoverColor = Color.Lerp(Color.white, hoverColor, pulse);
                        foreach (var cell in affectedCells)
                            CreateSkillMarker(cell, currentHoverColor, activeHoverMarkers);
                        DrawBorderEdges(hoverCells, borderColor);
                    }
                }
            }

            // Доступные цели для выбранного умения (все клетки в радиусе)
            if (selectedAlly != null && skillIndex != -1)
            {
                SkillInstance skillInst = selectedAlly.GetComponent<SkillInstance>();
                Skill skill = skillInst?.GetSkill(skillIndex);
                if (skill != null && !skill.requiresDirection)
                {
                    Vector3Int startPos = selectedAlly.GetFinalPosition();
                    List<Vector3Int> possibleTargets = GetPossibleTargets(selectedAlly, skill, startPos);
                    foreach (var cell in possibleTargets)
                    {
                        if (!plannedSkillCells.Contains(cell) && !hoverCells.Contains(cell))
                            CreateSkillMarker(cell, availableColor, activeSkillMarkers);
                    }
                }
            }
        }
        else // Режим реального времени – только следующее действие каждого юнита
        {
            // Враги – следующий шаг движения
            EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();
            foreach (var enemy in enemies)
            {
                if (!enemy.isMoving)
                {
                    Vector3Int? target = enemy.GetNextTargetCell();
                    if (target.HasValue)
                        CreateMoveMarker(enemy, target.Value, enemyMoveColor);
                }
            }

            // Союзники – следующее действие
            AllyAI[] allies = FindObjectsOfType<AllyAI>();
            foreach (var ally in allies)
            {
                if (!ally.isMoving)
                {
                    AllyAI.OrderType nextType = ally.GetNextOrderType();
                    if (nextType == AllyAI.OrderType.Move)
                    {
                        Vector3Int? target = ally.GetNextMoveTarget();
                        if (target.HasValue)
                            CreateMoveMarker(ally, target.Value, allyMoveColor);
                    }
                    else if (nextType == AllyAI.OrderType.Skill)
                    {
                        var skillInfo = ally.GetNextSkillInfo();
                        if (skillInfo.HasValue)
                        {
                            Skill skill = skillInfo.Value.skill;
                            Vector3Int targetCell = skillInfo.Value.targetCell;
                            Vector3Int casterPos = ally.GetFinalPosition();
                            List<Vector3Int> affected = ally.GetSkillAffectedCellsForTarget(skill, casterPos, targetCell);
                            foreach (var cell in affected)
                                CreateSkillMarker(cell, queuedSkillColor, activeSkillMarkers);
                            HashSet<Vector3Int> areaSet = new HashSet<Vector3Int>(affected);
                            DrawBorderEdges(areaSet, queuedSkillColor);
                        }
                    }
                }
            }
        }
    }

    // Получение маркера движения из пула или создание нового
    private GameObject GetMoveMarker()
    {
        if (moveMarkerPool.Count > 0)
        {
            GameObject marker = moveMarkerPool.Dequeue();
            marker.SetActive(true);
            return marker;
        }
        else
        {
            return Instantiate(moveMarkerPrefab, transform);
        }
    }

    // Получение маркера умения из пула или создание нового
    private GameObject GetSkillMarker()
    {
        if (skillMarkerPool.Count > 0)
        {
            GameObject marker = skillMarkerPool.Dequeue();
            marker.SetActive(true);
            return marker;
        }
        else
        {
            return Instantiate(skillMarkerPrefab, transform);
        }
    }

    // Получение куба границы из пула или создание нового
    private GameObject GetBorderCube()
    {
        if (borderCubePool.Count > 0)
        {
            GameObject cube = borderCubePool.Dequeue();
            cube.SetActive(true);
            return cube;
        }
        else
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(transform);
            Destroy(cube.GetComponent<Collider>()); // убираем коллайдер
            return cube;
        }
    }

    private void CreateMoveMarker(PawnAI owner, Vector3Int cell, Color color)
    {
        if (moveMarkerPrefab == null) return;
        GridBuilder gridBuilder = owner.GetGridBuilder();
        if (gridBuilder == null || !gridBuilder.IsInBounds(cell)) return;

        Vector3 worldPos = gridBuilder.CellToWorld(cell);
        worldPos.y += markerHeightOffset;

        GameObject marker = GetMoveMarker();
        marker.transform.position = worldPos;
        marker.transform.rotation = Quaternion.identity;

        Renderer rend = marker.GetComponent<Renderer>();
        if (rend != null) rend.material.color = color;

        activeMoveMarkers.Add(marker);
    }

    private void CreateSkillMarker(Vector3Int cell, Color color, List<GameObject> targetList)
    {
        if (skillMarkerPrefab == null) return;
        GridBuilder gridBuilder = FindObjectOfType<GridBuilder>();
        if (gridBuilder == null || !gridBuilder.IsInBounds(cell)) return;

        Vector3 worldPos = gridBuilder.CellToWorld(cell);
        worldPos.y = gridBuilder.GetTileHeight(cell) + markerHeightOffset;

        GameObject marker = GetSkillMarker();
        marker.transform.position = worldPos;
        marker.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        Renderer rend = marker.GetComponent<Renderer>();
        if (rend != null) rend.material.color = color;

        targetList.Add(marker);
    }

    private List<Vector3Int> GetPossibleTargets(AllyAI ally, Skill skill, Vector3Int startPos)
    {
        List<Vector3Int> cells = new List<Vector3Int>();
        GridBuilder gridBuilder = ally.GetGridBuilder();
        if (gridBuilder == null) return cells;

        for (int dx = -skill.range; dx <= skill.range; dx++)
        {
            for (int dy = -skill.range; dy <= skill.range; dy++)
            {
                Vector3Int cell = startPos + new Vector3Int(dx, dy, 0);
                if (!gridBuilder.IsInBounds(cell)) continue;
                cells.Add(cell);
            }
        }
        return cells;
    }

    private List<(Vector3Int position, Vector3Int direction)> GetBorderEdges(HashSet<Vector3Int> cells)
    {
        var edges = new List<(Vector3Int, Vector3Int)>();
        Vector3Int[] directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };

        foreach (var cell in cells)
        {
            foreach (var dir in directions)
            {
                Vector3Int neighbor = cell + dir;
                if (!cells.Contains(neighbor))
                {
                    // Внешняя граница
                    edges.Add((cell, dir));
                }
                else
                {
                    // Внутренняя граница по перепаду высот
                    int cellHeight = LocationManager.Instance.GetHeightAt(cell.x, cell.y);
                    int neighborHeight = LocationManager.Instance.GetHeightAt(neighbor.x, neighbor.y);
                    if (cellHeight != neighborHeight)
                    {
                        edges.Add((cell, dir));
                    }
                }
            }
        }
        return edges;
    }

    private void DrawBorderEdges(HashSet<Vector3Int> cells, Color color)
    {
        if (borderMaterial == null)
        {
            borderMaterial = new Material(Shader.Find("Standard"));
            borderMaterial.color = color;
        }

        var edges = GetBorderEdges(cells);
        foreach (var edge in edges)
        {
            Vector3 cellCenter = LocationManager.Instance.CellToWorld(edge.position);
            float tileY = LocationManager.Instance.GetTileHeight(edge.position) + borderHeightOffset;

            Vector3 cubePos;
            Vector3 cubeScale;

            if (edge.direction.x != 0) // горизонтальное ребро (вдоль Z)
            {
                cubePos = new Vector3(
                    cellCenter.x + edge.direction.x * 0.5f,
                    tileY,
                    cellCenter.z
                );
                cubeScale = new Vector3(0.1f, 0.05f, 1.0f);
            }
            else // вертикальное ребро (вдоль X)
            {
                cubePos = new Vector3(
                    cellCenter.x,
                    tileY,
                    cellCenter.z + edge.direction.y * 0.5f
                );
                cubeScale = new Vector3(1.0f, 0.05f, 0.1f);
            }

            GameObject cube = GetBorderCube();
            cube.transform.position = cubePos;
            cube.transform.localScale = cubeScale;
            cube.GetComponent<Renderer>().material = borderMaterial;
            cube.SetActive(true);

            activeBorderCubes.Add(cube);
        }
    }

    // Возврат всех маркеров и кубов в пул (деактивация)
    private void ReturnAllMarkersToPool()
    {
        // Маркеры движения
        foreach (var marker in activeMoveMarkers)
        {
            marker.SetActive(false);
            moveMarkerPool.Enqueue(marker);
        }
        activeMoveMarkers.Clear();

        // Маркеры умений (обычные)
        foreach (var marker in activeSkillMarkers)
        {
            marker.SetActive(false);
            skillMarkerPool.Enqueue(marker);
        }
        activeSkillMarkers.Clear();

        // Маркеры наведения
        foreach (var marker in activeHoverMarkers)
        {
            marker.SetActive(false);
            skillMarkerPool.Enqueue(marker);
        }
        activeHoverMarkers.Clear();

        // Кубы границ
        foreach (var cube in activeBorderCubes)
        {
            cube.SetActive(false);
            borderCubePool.Enqueue(cube);
        }
        activeBorderCubes.Clear();
    }
}
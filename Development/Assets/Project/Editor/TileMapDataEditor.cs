using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(TileMapData))]
public class TileMapDataEditor : Editor
{
    private TileMapData data;
    private int selectedTileType = 0;
    private int selectedObjectType = 0;
    private int selectedRotation = 0;
    private bool showObjects = true;
    private bool showHeights = true;
    private Vector2 scrollPosition;

    private string[] tileTypeNames;
    private string[] objectTypeNames;

    private GridBuilder[] gridBuilders;
    private int selectedGridBuilderIndex = -1;
    private GridBuilder selectedGridBuilder = null;

    // Для предпросмотра в сцене
    private GameObject previewContainer;
    private const string PREVIEW_CONTAINER_NAME = "TileMapPreview";

    // Статический конструктор для удаления превью при запуске игры
    [InitializeOnLoadMethod]
    private static void InitializeOnLoad()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            // Перед выходом из режима редактора удаляем все превью-локации
            GameObject preview = GameObject.Find(PREVIEW_CONTAINER_NAME);
            if (preview != null)
            {
                Debug.Log("Удаление предпросмотра локации перед запуском игры");
                Object.DestroyImmediate(preview);
            }
        }
    }

    private void OnEnable()
    {
        data = (TileMapData)target;
        tileTypeNames = System.Enum.GetNames(typeof(TileType));
        objectTypeNames = System.Enum.GetNames(typeof(ObjectType));

        // Поиск всех GridBuilder в сцене
        gridBuilders = FindObjectsOfType<GridBuilder>();
        if (gridBuilders.Length == 1)
        {
            selectedGridBuilder = gridBuilders[0];
            selectedGridBuilderIndex = 0;
        }
        else if (gridBuilders.Length > 1)
        {
            // Если несколько, предлагаем выбрать
            selectedGridBuilderIndex = 0; // по умолчанию первый
            selectedGridBuilder = gridBuilders[0];
        }
        else
        {
            selectedGridBuilder = null;
            selectedGridBuilderIndex = -1;
        }

        previewContainer = GameObject.Find(PREVIEW_CONTAINER_NAME);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Стандартные поля
        EditorGUILayout.PropertyField(serializedObject.FindProperty("locationId"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("globalPosition"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("elevation"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("connectedLocations"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("encounter"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("encounterCleared"));

        // Выбор GridBuilder
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Grid Builder", EditorStyles.boldLabel);
        if (gridBuilders.Length == 0)
        {
            EditorGUILayout.HelpBox("В сцене не найден GridBuilder. Пожалуйста, добавьте GridBuilder на сцену.", MessageType.Warning);
            selectedGridBuilder = null;
        }
        else if (gridBuilders.Length == 1)
        {
            EditorGUILayout.LabelField("Используется GridBuilder: " + gridBuilders[0].name);
            selectedGridBuilder = gridBuilders[0];
        }
        else
        {
            // Создаем список имен для popup
            string[] builderNames = new string[gridBuilders.Length];
            for (int i = 0; i < gridBuilders.Length; i++)
            {
                builderNames[i] = gridBuilders[i].name;
            }
            int newIndex = EditorGUILayout.Popup("Grid Builder", selectedGridBuilderIndex, builderNames);
            if (newIndex != selectedGridBuilderIndex)
            {
                selectedGridBuilderIndex = newIndex;
                selectedGridBuilder = gridBuilders[selectedGridBuilderIndex];
            }
        }

        // Размеры локации
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Размеры локации", EditorStyles.boldLabel);
        int newWidth = EditorGUILayout.IntField("Width", data.width);
        int newHeight = EditorGUILayout.IntField("Height", data.height);
        if (newWidth != data.width || newHeight != data.height)
        {
            if (newWidth > 0 && newHeight > 0)
            {
                ResizeArrays(newWidth, newHeight);
            }
        }

        // Инструменты рисования
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Инструменты рисования", EditorStyles.boldLabel);
        selectedTileType = EditorGUILayout.Popup("Тип тайла", selectedTileType, tileTypeNames);
        selectedObjectType = EditorGUILayout.Popup("Тип объекта", selectedObjectType, objectTypeNames);

        // Поворот с пояснением
        EditorGUILayout.BeginHorizontal();
        selectedRotation = EditorGUILayout.IntSlider("Поворот объекта", selectedRotation, 0, 3);
        string[] rotHints = { "→ вправо", "↑ вверх", "← влево", "↓ вниз" };
        EditorGUILayout.LabelField(rotHints[selectedRotation], GUILayout.Width(80));
        EditorGUILayout.EndHorizontal();

        showHeights = EditorGUILayout.Toggle("Показывать высоты", showHeights);
        showObjects = EditorGUILayout.Toggle("Показывать объекты", showObjects);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Редактор сетки", EditorStyles.boldLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        DrawGrid();
        EditorGUILayout.EndScrollView();

        // ---- Предпросмотр в сцене ----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Предпросмотр в сцене", EditorStyles.boldLabel);
        if (selectedGridBuilder == null)
        {
            EditorGUILayout.HelpBox("Для предпросмотра необходим GridBuilder в сцене.", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Построить предпросмотр"))
            {
                BuildPreview();
            }
            if (GUILayout.Button("Очистить предпросмотр"))
            {
                ClearPreview();
            }
            EditorGUILayout.EndHorizontal();
        }

        serializedObject.ApplyModifiedProperties();
        if (GUI.changed)
        {
            EditorUtility.SetDirty(data);
        }
    }

    private void BuildPreview()
    {
        if (selectedGridBuilder == null)
        {
            Debug.LogError("GridBuilder не выбран!");
            return;
        }
        if (data == null) return;

        // Очищаем старый предпросмотр
        ClearPreview();

        // Строим локацию в позиции (0,0)
        selectedGridBuilder.BuildLocation(data, Vector2Int.zero);

        // Получаем созданный контейнер и переименовываем его
        if (selectedGridBuilder.locationContainers.TryGetValue(Vector2Int.zero, out previewContainer))
        {
            previewContainer.name = PREVIEW_CONTAINER_NAME;
            Debug.Log($"Предпросмотр локации {data.locationId} построен в сцене");
        }
        else
        {
            Debug.LogError("Не удалось получить контейнер после построения");
        }
    }

    private void ClearPreview()
    {
        if (previewContainer != null)
        {
            DestroyImmediate(previewContainer);
            previewContainer = null;
            Debug.Log("Предпросмотр удалён");
        }
        else
        {
            // Если ссылка потеряна, ищем по имени
            GameObject existing = GameObject.Find(PREVIEW_CONTAINER_NAME);
            if (existing != null)
            {
                DestroyImmediate(existing);
                Debug.Log("Предпросмотр удалён (найден по имени)");
            }
        }
    }

    private void DrawGrid()
    {
        if (data.tileTypes == null || data.tileTypes.Length != data.width * data.height)
        {
            EditorGUILayout.HelpBox("Массивы не инициализированы. Нажмите 'Resize' для создания.", MessageType.Warning);
            return;
        }

        // --- Вычисляем все клетки, занятые объектами (для подсветки) ---
        HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
        if (selectedGridBuilder != null && selectedGridBuilder.objectPrefabs != null)
        {
            foreach (var obj in data.objects)
            {
                int typeIdx = (int)obj.type;
                if (typeIdx < 0 || typeIdx >= selectedGridBuilder.objectPrefabs.Length) continue;
                GameObject prefab = selectedGridBuilder.objectPrefabs[typeIdx];
                if (prefab == null) continue;
                ObjectData objData = prefab.GetComponent<ObjectData>();
                if (objData == null) continue;
                Vector3Int baseCell = new Vector3Int(obj.x, obj.y, 0);
                List<Vector3Int> cells = objData.GetOccupiedCells(baseCell, obj.rotation);
                foreach (var cell in cells)
                {
                    occupiedCells.Add(new Vector2Int(cell.x, cell.y));
                }
            }
        }

        // --- Стиль кнопки ---
        GUIStyle cellStyle = new GUIStyle(GUI.skin.button);
        cellStyle.margin = new RectOffset(2, 2, 2, 2);
        cellStyle.padding = new RectOffset(2, 2, 2, 2);
        cellStyle.alignment = TextAnchor.MiddleCenter;
        cellStyle.wordWrap = true;
        cellStyle.fontSize = 9;

        float cellSize = 30;

        for (int y = data.height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < data.width; x++)
            {
                int index = y * data.width + x;
                TileType tileType = data.tileTypes[index];
                int height = data.heights[index];

                Color baseColor = GetTileColorFromPrefab(tileType);
                float brightness = Mathf.Lerp(1f, 0.6f, height / 2f);
                Color cellColor = baseColor * brightness;

                if (occupiedCells.Contains(new Vector2Int(x, y)))
                {
                    cellColor = Color.Lerp(cellColor, Color.yellow, 0.3f);
                }

                GUIStyle coloredStyle = new GUIStyle(cellStyle);
                coloredStyle.normal.background = MakeTex(2, 2, cellColor);

                string label = "";
                if (showHeights) label = height.ToString();

                int objIndex = data.objects.FindIndex(o => o.x == x && o.y == y);
                if (objIndex >= 0 && showObjects)
                {
                    string fullName = objectTypeNames[(int)data.objects[objIndex].type];
                    string shortName = fullName.Length > 4 ? fullName.Substring(0, 4) : fullName;
                    int rot = data.objects[objIndex].rotation;
                    string rotSymbol = rot == 0 ? "→" : rot == 1 ? "↑" : rot == 2 ? "←" : "↓";
                    if (!string.IsNullOrEmpty(label))
                        label += "\n" + shortName + " " + rotSymbol;
                    else
                        label = shortName + " " + rotSymbol;
                }

                if (GUILayout.Button(label, coloredStyle, GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
                {
                    HandleCellClick(x, y, Event.current);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.HelpBox(
            "ЛКМ: установить выбранный тип тайла\n" +
            "Shift+ЛКМ: изменить высоту (увеличить)\n" +
            "Ctrl+ЛКМ: разместить объект с выбранным поворотом\n" +
            "ПКМ: удалить объект",
            MessageType.Info);
    }

    private Color GetTileColorFromPrefab(TileType type)
    {
        if (selectedGridBuilder != null && selectedGridBuilder.tilePrefabs != null && (int)type < selectedGridBuilder.tilePrefabs.Length && selectedGridBuilder.tilePrefabs[(int)type] != null)
        {
            GameObject prefab = selectedGridBuilder.tilePrefabs[(int)type];
            Renderer renderer = prefab.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
                return renderer.sharedMaterial.color;
        }
        switch (type)
        {
            case TileType.Grass: return new Color(0.2f, 0.6f, 0.2f);
            case TileType.Rock: return new Color(0.4f, 0.4f, 0.4f);
            case TileType.Sand: return new Color(0.8f, 0.7f, 0.4f);
            default: return Color.gray;
        }
    }

    private void HandleCellClick(int x, int y, Event e)
    {
        int index = y * data.width + x;

        if (e.button == 0) // ЛКМ
        {
            if (e.shift)
            {
                int newHeight = (data.heights[index] + 1) % 3;
                data.heights[index] = newHeight;
            }
            else if (e.control)
            {
                ObjectPlacement obj = new ObjectPlacement
                {
                    type = (ObjectType)selectedObjectType,
                    x = x,
                    y = y,
                    rotation = selectedRotation
                };
                data.objects.RemoveAll(o => o.x == x && o.y == y);
                data.objects.Add(obj);
            }
            else
            {
                data.tileTypes[index] = (TileType)selectedTileType;
            }
        }
        else if (e.button == 1) // ПКМ
        {
            data.objects.RemoveAll(o => o.x == x && o.y == y);
        }
        EditorUtility.SetDirty(data);
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    private void ResizeArrays(int newWidth, int newHeight)
    {
        TileType[] newTileTypes = new TileType[newWidth * newHeight];
        int[] newHeights = new int[newWidth * newHeight];

        if (data.tileTypes != null)
        {
            for (int y = 0; y < Mathf.Min(data.height, newHeight); y++)
            {
                for (int x = 0; x < Mathf.Min(data.width, newWidth); x++)
                {
                    int oldIndex = y * data.width + x;
                    int newIndex = y * newWidth + x;
                    newTileTypes[newIndex] = data.tileTypes[oldIndex];
                    newHeights[newIndex] = data.heights[oldIndex];
                }
            }
        }

        for (int i = 0; i < newTileTypes.Length; i++)
        {
            if (newTileTypes[i] == TileType.Grass && i >= (data.tileTypes?.Length ?? 0))
                newTileTypes[i] = TileType.Grass;
        }

        data.tileTypes = newTileTypes;
        data.heights = newHeights;
        data.width = newWidth;
        data.height = newHeight;

        data.objects.RemoveAll(obj => obj.x >= newWidth || obj.y >= newHeight);

        EditorUtility.SetDirty(data);
    }
}
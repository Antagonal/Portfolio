using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewTileMap", menuName = "Game/TileMap Data")]
public class TileMapData : ScriptableObject
{
    public string locationId;          // уникальный идентификатор (Например, "forest_01")
    public Vector2Int globalPosition;
    public List<string> connectedLocations;
    public EncounterData encounter;
    public bool encounterCleared = false;

    public int width = 10;
    public int height = 10;
    public int elevation = 0;

    public TileType[] tileTypes;
    public int[] heights;

    public List<ObjectPlacement> objects = new List<ObjectPlacement>();

    public TileType GetTileType(int x, int y)
    {
        return tileTypes[y * width + x];
    }

    public int GetHeight(int x, int y)
    {
        return heights[y * width + x];
    }
}
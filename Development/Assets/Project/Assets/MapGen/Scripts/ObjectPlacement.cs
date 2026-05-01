using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct ObjectPlacement
{
    public ObjectType type;
    public int x;
    public int y;
    public int rotation; // 0, 1, 2, 3
}
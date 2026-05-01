using UnityEngine;
using System.Collections.Generic;

public class LocationContainer : MonoBehaviour
{
    public Dictionary<Vector3Int, ObjectData> occupiedObjects = new Dictionary<Vector3Int, ObjectData>();
}
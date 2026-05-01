using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewEncounter", menuName = "Game/Encounter Data")]
public class EncounterData : ScriptableObject
{
    [System.Serializable]
    public struct EnemyEntry
    {
        public EnemyType type;
        // можно добавить количество, если нужно несколько одного типа
        public int health; // здоровье этого врага
    }

    public List<EnemyEntry> enemies;
}
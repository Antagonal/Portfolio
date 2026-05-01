using UnityEngine;

[CreateAssetMenu(fileName = "NewSkill", menuName = "Game/Skill")]
public class Skill : ScriptableObject
{
    [Header("Основные параметры")]
    public string skillName = "Skill";
    public int apCost = 1;
    public int range = 1;               // дальность в клетках (для нацеленных и направленных)
    public int areaRange = 0;            // радиус области (для Circle, Cross, Cone)
    public int damage = 0;
    public int heal = 0;
    public SkillAreaType areaType = SkillAreaType.Single;

    [Header("Направление")]
    public bool requiresDirection = false;  // true = применяется по стрелке, false = по клику мыши

    [Header("Цели")]
    public bool targetsEnemies = true;      // может ли поражать врагов
    public bool targetsAllies = false;      // может ли поражать союзников
    public bool targetsObjects = false;     // может ли поражать объекты
}
using UnityEngine;
using System.Collections.Generic;

public class SkillInstance : MonoBehaviour
{
    [SerializeField] private List<Skill> skills = new List<Skill>();

    public int SkillCount => skills.Count;

    public Skill GetSkill(int index)
    {
        if (index >= 0 && index < skills.Count)
            return skills[index];
        return null;
    }

    public bool HasSkill(int index) => index >= 0 && index < skills.Count && skills[index] != null;
}
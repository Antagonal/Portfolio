public enum SkillAreaType
{
    Single,   // одна клетка (цель)
    Line,     // линия длиной range в направлении
    Cross,    // крест радиусом areaRange вокруг центра
    Self,     // на себя
    Circle,   // квадрат (чебышёвский круг) радиусом areaRange вокруг центра
    Cone      // конус 90° от кастера длиной range
}
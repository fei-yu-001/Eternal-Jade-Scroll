namespace TwelveJade.Core
{
    // 离开青石镇与正式入门缺一不可；旧修行账本本身不授予修行资格。
    public static class PrologueRules
    {
        public const string DepartureFlag = "chapter-1-left-qingshi";
        public const string InitiationFlag = "cultivation-initiation";
        public const string Refusal = "尚未踏入修行之门。";

        public static bool CanCultivate(SaveData save) => save != null &&
            save.HasFlag(DepartureFlag) && save.HasFlag(InitiationFlag);
    }
}

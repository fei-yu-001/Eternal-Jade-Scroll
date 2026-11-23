using UnityEngine;

namespace TwelveJade.Presentation
{
    [CreateAssetMenu(menuName = "Twelve Jade/Character preset")]
    public sealed class CharacterPreset : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public string detail;
        public Color accent = Color.white;
        // South / east / north / west. West can reuse the east view mirrored.
        public Texture2D front;
        public Texture2D side;
        public Texture2D back;

        public Texture2D Facing(int direction) => Facing(direction, "male", 0);

        // 青石镇统一使用凡俗粗布行装；旧面容字段保留，旧图仅作资源缺失时回退。
        public Texture2D Facing(int direction, string gender, int faceStyle)
        {
            var suffix = direction == 0 ? "front" : direction == 2 ? "back" : "side";
            var rural = TownCharacterArt.Hero(gender, suffix, false);
            if (rural != null) return rural;
            if (gender != "male" || faceStyle != 0)
            {
                var variant = Resources.Load<Texture2D>($"Art/Characters/Stand/{id}-{gender}-{faceStyle}-{suffix}");
                if (variant != null) return variant;
            }
            var assigned = direction == 0 ? front : direction == 2 ? back : side;
            return assigned != null ? assigned : Resources.Load<Texture2D>($"Art/Characters/Stand/{id}-male-0-{suffix}");
        }

        public static string FaceStyleName(string gender, int style)
        {
            if (gender == "female") return style == 0 ? "挽髻" : "双髻";
            return style == 0 ? "束发" : "裹巾";
        }

        public static string GenderName(string gender) => gender == "female" ? "女" : "男";
    }
}

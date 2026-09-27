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

        // 面容款式 v1 = 性别 + 发型基础款。旧档与默认外观继续走 legacy 路径，
        // 变体画稿按 Art/{id}-{gender}-{style}-{view} 命名，缺失时回退默认图。
        public Texture2D Facing(int direction, string gender, int faceStyle)
        {
            var suffix = direction == 0 ? "front" : direction == 2 ? "back" : "side";
            if (gender != "male" || faceStyle != 0)
            {
                var variant = Resources.Load<Texture2D>($"Art/{id}-{gender}-{faceStyle}-{suffix}");
                if (variant != null) return variant;
            }
            var assigned = direction == 0 ? front : direction == 2 ? back : side;
            return assigned != null ? assigned : Resources.Load<Texture2D>($"Art/{id}-male-0-{suffix}");
        }

        public static string FaceStyleName(string gender, int style)
        {
            if (gender == "female") return style == 0 ? "挽髻" : "双髻";
            return style == 0 ? "束发" : "裹巾";
        }

        public static string GenderName(string gender) => gender == "female" ? "女" : "男";
    }
}

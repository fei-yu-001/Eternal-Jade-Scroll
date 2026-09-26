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

        public Texture2D Facing(int direction)
        {
            var suffix = direction == 0 ? "front" : direction == 2 ? "back" : "side";
            var assigned = direction == 0 ? front : direction == 2 ? back : side;
            return assigned != null ? assigned : Resources.Load<Texture2D>($"Art/{id}-{suffix}");
        }
    }
}

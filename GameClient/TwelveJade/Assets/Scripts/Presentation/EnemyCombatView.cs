using TwelveJade.Core;
using TwelveJade.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Real painted frames. No whole-image rocking is used to pretend the enemy is walking.
    [DefaultExecutionOrder(190)]
    public sealed class EnemyCombatView : MonoBehaviour
    {
        readonly EnemyCombatFrameClock clock = new EnemyCombatFrameClock();
        RawImage image;
        Texture2D stand, walk, strikes;
        RectTransform root;
        float size, standFootInset;
        bool sheetActive;
        int selectedStrike = -1;
        public int WalkFrame => SheetVisible && image.texture == walk ? clock.WalkFrame : -1;
        public int StrikeFrame => SheetVisible && image.texture == strikes ? selectedStrike : -1;
        public float DisplayedFootInset => sheetActive ? size * EnemyCombatFrameClock.FootInset / EnemyCombatFrameClock.CellPixels : standFootInset;
        public float StrideDistance => clock.Distance;
        public bool WalkArtReady => walk != null;
        public bool StrikeArtReady => strikes != null;
        public bool SheetVisible => sheetActive && image != null && image.enabled && image.gameObject.activeInHierarchy;
        public void Configure(RawImage target, Texture2D standing, Texture2D walking, Texture2D attacking, float drawSize, float oldFootInset)
        {
            image = target; root = target.rectTransform; stand = standing; size = drawSize; standFootInset = oldFootInset;
            walk = ValidSheet(walking); strikes = ValidSheet(attacking);
        }
        Texture2D ValidSheet(Texture2D texture)
        {
            if (texture == null) return null;
            if (texture.width == EnemyCombatFrameClock.CellPixels * 4 && texture.height == EnemyCombatFrameClock.CellPixels) return texture;
            Debug.LogError("[EnemyCombat] Invalid four-cell sheet " + texture.name + ": expected 2048x512. Keeping authored portrait.");
            return null;
        }
        public void Sample(Vector2 actualFoot, int facing, CombatPhase phase, BossSkill skill, float progress,
            float travelled, bool walking, bool reduced, bool fallback)
        {
            if (image == null) return;
            clock.Move(travelled, walking && !fallback);
            selectedStrike = strikes == null || fallback ? -1 : EnemyCombatFrameClock.AttackFrame(phase, skill, progress);
            int frame = selectedStrike >= 0 ? selectedStrike : walk != null ? clock.WalkFrame : -1;
            Texture2D texture = selectedStrike >= 0 ? strikes : frame >= 0 ? walk : stand;
            sheetActive = frame >= 0;
            if (!fallback)
            {
                image.texture = texture; image.enabled = texture != null;
                if (sheetActive)
                {
                    root.pivot = new Vector2(.5f, EnemyCombatFrameClock.FootInset / EnemyCombatFrameClock.CellPixels);
                    image.uvRect = facing < 0 ? new Rect((frame + 1) / 4f, 0, -.25f, 1) : new Rect(frame / 4f, 0, .25f, 1);
                    root.anchoredPosition = actualFoot;
                }
                else
                {
                    root.pivot = new Vector2(.5f, 0);
                    image.uvRect = facing < 0 ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1);
                    root.anchoredPosition = actualFoot + Vector2.down * standFootInset;
                }
            }
            else { root.pivot = new Vector2(.5f, 0); root.anchoredPosition = actualFoot + Vector2.down * standFootInset; }
            float aspect = sheetActive || texture == null ? 1 : texture.width / (float)texture.height;
            root.sizeDelta = new Vector2(size * aspect, size);
            root.localScale = Vector3.one;
            root.localRotation = reduced || phase != CombatPhase.Dead ? Quaternion.identity : Quaternion.Euler(0, 0, -facing * 55);
            image.color = phase == CombatPhase.Dead ? new Color(.65f, .65f, .65f, .45f) : Color.white;
        }
        public Vector2 RegisteredFootInParent()
        {
            if (root == null || root.parent == null) return new Vector2(float.NaN, float.NaN);
            var local = sheetActive ? Vector3.zero : new Vector3(0, standFootInset, 0);
            var p = root.parent.InverseTransformPoint(root.TransformPoint(local)); return new Vector2(p.x, p.y);
        }
    }
}

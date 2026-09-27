using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // 纸偶式程序动画：以单张立绘为整体，按动作状态做位移/旋转/挤压拉伸，
    // 配合脚底阴影表达起伏。无需骨骼与逐帧素材，对所有角色外观通用，
    // 也能贴合水墨纸偶的美术气质。
    public sealed class CharacterActor : MonoBehaviour
    {
        public enum Motion { Idle, Walk, Run, Sneak, Crouch, Jump, Swim }
        public static readonly string[] MotionNames = { "静立", "行走", "奔跑", "潜行", "下蹲", "跳跃", "游泳" };

        RectTransform body, shadow;
        RawImage bodyImage, shadowImage;
        Motion motion = Motion.Idle;
        float time;

        public static CharacterActor Create(Transform parent, Texture2D view, bool mirror, Vector2 bottomCenter, Vector2 size)
        {
            var root = new GameObject("Character actor", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(.5f, 0f);
            root.anchoredPosition = bottomCenter;
            root.sizeDelta = size;
            var actor = root.gameObject.AddComponent<CharacterActor>();

            var shadowRect = new GameObject("Shadow", typeof(RectTransform)).GetComponent<RectTransform>();
            shadowRect.SetParent(root, false);
            shadowRect.anchorMin = shadowRect.anchorMax = new Vector2(.5f, 0f);
            shadowRect.pivot = new Vector2(.5f, .5f);
            shadowRect.anchoredPosition = new Vector2(0, -8);
            shadowRect.sizeDelta = new Vector2(size.x * .8f, 26);
            actor.shadowImage = shadowRect.gameObject.AddComponent<RawImage>();
            actor.shadowImage.texture = ShadowTexture(); actor.shadowImage.raycastTarget = false;
            actor.shadowImage.color = new Color(.04f, .09f, .08f, .32f);
            actor.shadow = shadowRect;

            var bodyRect = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            bodyRect.SetParent(root, false);
            bodyRect.anchorMin = Vector2.zero; bodyRect.anchorMax = Vector2.one;
            bodyRect.pivot = new Vector2(.5f, 0f);
            bodyRect.offsetMin = Vector2.zero; bodyRect.offsetMax = Vector2.zero;
            actor.bodyImage = bodyRect.gameObject.AddComponent<RawImage>();
            actor.bodyImage.raycastTarget = false;
            actor.body = bodyRect;

            actor.SetView(view, mirror);
            return actor;
        }

        public void SetView(Texture2D view, bool mirror)
        {
            if (bodyImage == null || view == null) return;
            bodyImage.texture = view;
            bodyImage.uvRect = mirror ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1);
        }

        public void SetMotion(Motion value)
        {
            motion = value;
            time = 0f;
        }

        public Motion CurrentMotion => motion;

        static Texture2D shadowTexture;
        static Texture2D ShadowTexture()
        {
            if (shadowTexture != null) return shadowTexture;
            const int w = 64, h = 24;
            shadowTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = (x + .5f) / w - .5f;
                var dy = (y + .5f) / h - .5f;
                shadowTexture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx * 4f + dy * dy * 8f))));
            }
            shadowTexture.Apply(false, true);
            return shadowTexture;
        }

        void Update()
        {
            time += Time.unscaledDeltaTime;
            var position = Vector2.zero;
            var rotation = 0f;
            var scaleX = 1f;
            var scaleY = 1f;
            var shadowScale = 1f;
            var shadowAlpha = .32f;
            var shadowJade = false;
            switch (motion)
            {
                case Motion.Idle:
                    scaleY = 1f + Mathf.Sin(time * 2.1f) * .008f;
                    break;
                case Motion.Walk:
                {
                    var phase = time / .72f * Mathf.PI * 2f;
                    position.y = Mathf.Abs(Mathf.Sin(phase)) * 7f;
                    rotation = Mathf.Sin(phase) * 1.6f;
                    position.x = Mathf.Sin(phase * .5f) * 3f;
                    break;
                }
                case Motion.Run:
                {
                    var phase = time / .42f * Mathf.PI * 2f;
                    position.y = Mathf.Abs(Mathf.Sin(phase)) * 13f;
                    rotation = -5f + Mathf.Sin(phase) * 3f;
                    position.x = Mathf.Sin(phase * .5f) * 6f;
                    scaleY = 1f + Mathf.Abs(Mathf.Sin(phase)) * .02f;
                    shadowScale = 1f - Mathf.Abs(Mathf.Sin(phase)) * .18f;
                    break;
                }
                case Motion.Sneak:
                {
                    var phase = time / 1.15f * Mathf.PI * 2f;
                    position.y = 12f + Mathf.Abs(Mathf.Sin(phase)) * 3f;
                    rotation = -4f + Mathf.Sin(phase) * .8f;
                    shadowAlpha = .38f;
                    break;
                }
                case Motion.Crouch:
                    position.y = 2f;
                    scaleY = .8f;
                    scaleX = 1.06f;
                    shadowScale = 1.08f;
                    break;
                case Motion.Jump:
                {
                    const float cycle = 1.15f;
                    var u = Mathf.Repeat(time, cycle) / cycle;
                    var arc = Mathf.Sin(u * Mathf.PI);
                    position.y = arc * 64f;
                    scaleY = 1f + arc * .05f - (u > .9f ? .1f : 0f);
                    scaleX = 1f - arc * .03f;
                    shadowScale = 1f - arc * .3f;
                    shadowAlpha = .32f - arc * .16f;
                    break;
                }
                case Motion.Swim:
                    position.y = 6f + Mathf.Sin(time * 1.6f) * 5f;
                    rotation = -68f + Mathf.Sin(time * 1.2f) * 4f;
                    shadowAlpha = .22f;
                    shadowJade = true;
                    shadowScale = 1.35f;
                    break;
            }
            body.anchoredPosition = position;
            body.localRotation = Quaternion.Euler(0, 0, rotation);
            body.localScale = new Vector3(scaleX, scaleY, 1f);
            shadow.localScale = new Vector3(shadowScale, shadowScale, 1f);
            shadowImage.color = shadowJade
                ? new Color(.16f, .38f, .32f, shadowAlpha)
                : new Color(.04f, .09f, .08f, shadowAlpha);
        }
    }
}

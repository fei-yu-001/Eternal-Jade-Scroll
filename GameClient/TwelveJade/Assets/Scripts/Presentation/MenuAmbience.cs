using System;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // 主菜单动态氛围：三层程序化柔雾横向漂移、浮尘如余烬上行、背景呼吸式缓动。
    // 全部用运行时生成的贴图，无外部依赖；reduceMotion 为真时整帧静止。
    public sealed class MenuAmbience : MonoBehaviour
    {
        RawImage backdrop;
        Func<bool> reduceMotion;
        RectTransform[] fogRects;
        float[] fogSpeeds;
        float[] motePhases;
        RectTransform[] motes;
        float[] moteSpeeds;
        float elapsed;

        const float WrapX = 2600f;

        public void Configure(RawImage backdropImage, Func<bool> reduceMotionEnabled)
        {
            backdrop = backdropImage;
            reduceMotion = reduceMotionEnabled;

            var fogTexture = MakeFogTexture();
            fogRects = new RectTransform[3];
            fogSpeeds = new[] { 14f, -9f, 21f };
            var specs = new[] { (300f, .14f, 190f), (430f, .10f, 620f), (260f, .12f, 940f) };
            for (var i = 0; i < 3; i++)
            {
                var (height, alpha, y) = specs[i];
                var image = NewSprite("Mist " + i, fogTexture, new Color(.94f, .93f, .87f, alpha));
                image.rectTransform.SetParent(transform, false);
                AnchorTopLeft(image.rectTransform);
                image.rectTransform.sizeDelta = new Vector2(WrapX, height);
                image.rectTransform.anchoredPosition = new Vector2(0, -y);
                fogRects[i] = image.rectTransform;
            }

            var dot = MakeDotTexture();
            var random = new System.Random(20260927);
            motes = new RectTransform[16];
            moteSpeeds = new float[16];
            motePhases = new float[16];
            var tints = new[] { new Color(.92f, .84f, .62f, .5f), new Color(.84f, .88f, .82f, .42f) };
            for (var i = 0; i < motes.Length; i++)
            {
                var image = NewSprite("Mote " + i, dot, tints[i % tints.Length]);
                image.rectTransform.SetParent(transform, false);
                AnchorTopLeft(image.rectTransform);
                var size = 3 + (float)random.NextDouble() * 5;
                image.rectTransform.sizeDelta = new Vector2(size, size);
                var x = (float)random.NextDouble() * 1920;
                var y = (float)random.NextDouble() * 1080;
                image.rectTransform.anchoredPosition = new Vector2(x, -y);
                moteSpeeds[i] = 10 + (float)random.NextDouble() * 18;
                motePhases[i] = (float)random.NextDouble() * Mathf.PI * 2;
                motes[i] = image.rectTransform;
            }
        }

        static void AnchorTopLeft(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
        }

        static RawImage NewSprite(string name, Texture2D texture, Color color)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture; image.color = color; image.raycastTarget = false;
            return image;
        }

        // 多个高斯团块叠出一条软雾带；alpha 从中心向外衰减，四边再整体羽化到 0，
        // 保证雾层矩形在任何背景下都看不出边界。
        static Texture2D MakeFogTexture()
        {
            const int size = 512;
            var height = size / 2;
            var tex = new Texture2D(size, height, TextureFormat.RGBA32, false);
            var blobs = new (float cx, float cy, float rx, float ry)[]
            {
                (.24f, .55f, .20f, .34f), (.45f, .40f, .26f, .40f), (.68f, .58f, .22f, .36f), (.86f, .44f, .18f, .30f)
            };
            for (var y = 0; y < height; y++)
            for (var x = 0; x < size; x++)
            {
                float alpha = 0;
                foreach (var (cx, cy, rx, ry) in blobs)
                {
                    var dx = (x / (float)size - cx) / rx;
                    var dy = (y / (float)height - cy) / ry;
                    alpha = Mathf.Max(alpha, Mathf.Exp(-(dx * dx + dy * dy) * 1.6f));
                }
                var edgeX = Mathf.Min(x, size - 1 - x) / (size * .12f);
                var edgeY = Mathf.Min(y, height - 1 - y) / (height * .22f);
                alpha *= Mathf.Clamp01(Mathf.Min(edgeX, edgeY));
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        static Texture2D MakeDotTexture()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + .5f) / size - .5f;
                var dy = (y + .5f) / size - .5f;
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2.4f)));
            }
            tex.Apply(false, true);
            return tex;
        }

        void Update()
        {
            if (reduceMotion != null && reduceMotion()) return;
            elapsed += Time.unscaledDeltaTime;
            for (var i = 0; i < fogRects.Length; i++)
            {
                var position = fogRects[i].anchoredPosition;
                position.x += fogSpeeds[i] * Time.unscaledDeltaTime;
                if (position.x > 320) position.x = -920;
                if (position.x < -920) position.x = 320;
                fogRects[i].anchoredPosition = position;
            }
            for (var i = 0; i < motes.Length; i++)
            {
                var position = motes[i].anchoredPosition;
                position.y += moteSpeeds[i] * Time.unscaledDeltaTime;
                position.x += Mathf.Sin(elapsed * .8f + motePhases[i]) * 6f * Time.unscaledDeltaTime;
                if (position.y > 40) position.y = -1120;
                motes[i].anchoredPosition = position;
            }
            if (backdrop != null)
            {
                // Ken Burns：极缓慢的呼吸缩放 + 右下向漂移，让静止的画活起来（缩放从左上锚点出发，等效轻微平移）。
                var breath = (Mathf.Sin(elapsed * .05f) + 1f) * .5f;
                var scale = 1f + .035f * breath;
                backdrop.rectTransform.localScale = new Vector3(scale, scale, 1);
            }
        }
    }
}

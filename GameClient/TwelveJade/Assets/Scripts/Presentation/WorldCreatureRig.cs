using UnityEngine;
using UnityEngine.UI;
using TwelveJade.Core;

namespace TwelveJade.Presentation
{
    // A small skinning mesh keeps the approved creature artwork while articulating
    // the four feet, shoulders and head. Gait advances only by real ground distance.
    [RequireComponent(typeof(RawImage))]
    public sealed class WorldCreatureRig : BaseMeshEffect
    {
        const int Columns = 40, Rows = 32;
        readonly float[] feet = new float[4], hips = new float[4];
        float phase, stride, attack, flinch;
        int facing = 1;
        bool heavy;
        public float GaitPhase => phase;
        public bool Walking => stride > 0;
        public void Configure(string enemy)
        {
            heavy = enemy == "qingshi-mountain-lord";
            float[] f, h;
            if (enemy == "boar-demon") { f = new[] { .12f, .37f, .55f, .71f }; h = new[] { .25f, .36f, .56f, .60f }; }
            else if (enemy == "nether-wolf") { f = new[] { .16f, .38f, .52f, .82f }; h = new[] { .29f, .34f, .61f, .65f }; }
            else { f = new[] { .14f, .36f, .60f, .88f }; h = new[] { .29f, .36f, .56f, .77f }; }
            for (int i = 0; i < 4; i++) { feet[i] = f[i]; hips[i] = h[i]; }
        }
        public void Walk(float cycles, bool moving, int direction)
        {
            phase = cycles * Mathf.PI * 2; stride = moving ? 1 : 0; facing = direction;
            graphic.SetVerticesDirty();
        }
        public void Combat(CombatPhase state, float progress, bool reduced)
        {
            attack = state == CombatPhase.Startup ? -.42f : state == CombatPhase.Active
                ? Mathf.Lerp(1, .4f, progress) : state == CombatPhase.Recovery ? .15f : 0;
            flinch = state == CombatPhase.Hitstun ? -.35f : 0;
            if (reduced) { attack *= .45f; flinch = 0; }
            graphic.SetVerticesDirty();
        }
        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive()) return;
            var image = (RawImage)graphic;
            Rect box = image.rectTransform.rect, uv = image.uvRect;
            Color32 tint = image.color;
            mesh.Clear();
            for (int y = 0; y <= Rows; y++)
            for (int x = 0; x <= Columns; x++)
            {
                float u = x / (float)Columns, v = y / (float)Rows;
                float sourceX = facing < 0 ? 1 - u : u;
                Vector2 d = Deform(sourceX, v);
                mesh.AddVert(new Vector3(box.xMin + (u + d.x * facing) * box.width,
                    box.yMin + (v + d.y) * box.height), tint,
                    new Vector2(uv.x + u * uv.width, uv.y + v * uv.height));
            }
            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Columns; x++)
            {
                int i = y * (Columns + 1) + x;
                mesh.AddTriangle(i, i + Columns + 1, i + 1);
                mesh.AddTriangle(i + 1, i + Columns + 1, i + Columns + 2);
            }
        }
        Vector2 Deform(float x, float y)
        {
            float leg = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.07f, .40f, y));
            float dx = 0, dy = 0, weights = 0;
            for (int i = 0; i < 4; i++)
            {
                float center = Mathf.Lerp(feet[i], hips[i], Mathf.InverseLerp(.055f, .40f, y));
                float offset = (x - center) / .075f;
                float weight = Mathf.Exp(-offset * offset * 2);
                float beat = phase + (i == 1 || i == 2 ? Mathf.PI : 0);
                dx += Mathf.Sin(beat) * weight;
                dy += Mathf.Max(0, Mathf.Cos(beat)) * weight;
                weights += weight;
            }
            float normalization = Mathf.Max(1, weights);
            float reach = heavy ? .033f : .062f;
            dx = dx / normalization * leg * reach * stride;
            dy = dy / normalization * leg * (heavy ? .017f : .034f) * stride;
            // Keep support feet at ground level; only hips and shoulders take the weight shift.
            float body = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.09f, .39f, y));
            dy += body * Mathf.Sin(phase * 2) * (heavy ? .003f : .007f) * stride;
            float front = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.36f, .84f, x));
            dx += (attack + flinch) * body * (.025f + front * .055f);
            dy -= attack * body * front * .023f;
            // Tail and mane follow the step, without translating the collision footprint.
            dx += Mathf.Max(0, .25f - x) * body * Mathf.Sin(phase + 1) * .055f * stride;
            return new Vector2(dx, dy);
        }
    }
}

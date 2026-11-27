using UnityEngine;
using UnityEngine.UI;
using TwelveJade.Core;

namespace TwelveJade.Presentation
{
    // State-specific silhouettes: a cinnabar written talisman, a jade cloud pendant, a dim bound slip.
    // Pure UGUI geometry, no text-glyph icon, per-frame texture load, owned material or raycast surface.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class QuestMarkerGraphic : MaskableGraphic
    {
        QuestOverheadState state;
        bool mainStory;
        public bool IsStory => mainStory;
        public QuestOverheadState State => state;
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        public void SetState(QuestOverheadState value, bool story = false) { if (state == value && mainStory == story) return; state = value; mainStory = story; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (!NpcIdentityRules.TaskVisible(state)) return;
            if (mainStory) StorySign(mesh);
            else if (state == QuestOverheadState.Completable) Pendant(mesh); else Talisman(mesh, state == QuestOverheadState.InProgress);
        }
        Vector2 Point(float x, float y)
        {
            var r = rectTransform.rect; return new Vector2(r.xMin + x * r.width, r.yMin + y * r.height);
        }
        void Quad(VertexHelper mesh, float x, float y, float w, float h, Color tint)
        {
            int n = mesh.currentVertCount; var c = (Color32)tint;
            mesh.AddVert(Point(x, y), c, Vector2.zero); mesh.AddVert(Point(x, y + h), c, Vector2.zero);
            mesh.AddVert(Point(x + w, y + h), c, Vector2.zero); mesh.AddVert(Point(x + w, y), c, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n + 2, n + 3, n);
        }
        void Line(VertexHelper mesh, float ax, float ay, float bx, float by, float width, Color tint)
        {
            Vector2 a = Point(ax, ay), b = Point(bx, by); var normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            int n = mesh.currentVertCount; var c = (Color32)tint;
            mesh.AddVert(a - normal, c, Vector2.zero); mesh.AddVert(a + normal, c, Vector2.zero);
            mesh.AddVert(b + normal, c, Vector2.zero); mesh.AddVert(b - normal, c, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n + 2, n + 3, n);
        }
        void Arc(VertexHelper mesh, float cx, float cy, float rx, float ry, float from, float to, float width, Color color)
        {
            const int segments = 20;
            float lastX = cx + Mathf.Cos(from) * rx, lastY = cy + Mathf.Sin(from) * ry;
            for (int i = 1; i <= segments; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)segments), x = cx + Mathf.Cos(a) * rx, y = cy + Mathf.Sin(a) * ry;
                Line(mesh, lastX, lastY, x, y, width, color); lastX = x; lastY = y;
            }
        }
        // A paper hanging sign with an unmistakable painted ! / ? silhouette. Main-story
        // punctuation is real geometry, not a hidden TMP string used only by screenshots.
        void StorySign(VertexHelper mesh)
        {
            var paper = new Color(.98f, .89f, .66f, 1); var ink = new Color(.48f, .12f, .075f, 1);
            Quad(mesh, .18f, .10f, .67f, .83f, new Color(.08f, .06f, .04f, .55f));
            Quad(mesh, .14f, .14f, .67f, .83f, paper);
            Line(mesh, .17f, .94f, .78f, .94f, 1.4f, ink);
            Line(mesh, .17f, .17f, .78f, .17f, 1.4f, ink);
            Line(mesh, .17f, .17f, .17f, .94f, 1.4f, ink);
            Line(mesh, .78f, .17f, .78f, .94f, 1.4f, ink);
            if (state == QuestOverheadState.Completable)
            {
                Arc(mesh, .48f, .69f, .17f, .15f, Mathf.PI, -Mathf.PI * .50f, 4.6f, ink);
                Line(mesh, .48f, .54f, .48f, .43f, 4.6f, ink);
            }
            else Line(mesh, .48f, .82f, .48f, .44f, 5.5f, ink);
            Quad(mesh, .43f, .28f, .10f, .09f, ink);
        }
        void Talisman(VertexHelper mesh, bool inProgress)
        {
            Color paper = inProgress ? new Color(.37f, .40f, .34f, .82f) : new Color(.89f, .73f, .40f, .98f);
            Color edge = inProgress ? new Color(.60f, .61f, .49f, .7f) : new Color(.99f, .86f, .55f, 1);
            Color ink = inProgress ? new Color(.48f, .32f, .25f, .85f) : new Color(.59f, .13f, .08f, 1);
            Quad(mesh, .27f, .21f, .46f, .64f, new Color(.08f, .10f, .07f, .55f));
            Quad(mesh, .30f, .24f, .40f, .59f, paper);
            Line(mesh, .30f, .83f, .70f, .83f, 1.5f, edge); Line(mesh, .30f, .24f, .30f, .83f, 1, edge); Line(mesh, .70f, .24f, .70f, .83f, 1, edge);
            // Asymmetric brush strokes suggest a seal-script talisman without a punctuation glyph.
            Line(mesh, .39f, .73f, .62f, .75f, 2, ink); Line(mesh, .48f, .78f, .50f, .43f, 2.2f, ink);
            Line(mesh, .36f, .62f, .64f, .60f, 1.7f, ink); Line(mesh, .49f, .60f, .38f, .48f, 1.8f, ink);
            Line(mesh, .50f, .56f, .63f, .44f, 1.8f, ink); Line(mesh, .39f, .37f, .59f, .39f, 2, ink);
            Line(mesh, .41f, .40f, .41f, .31f, 1.3f, ink); Line(mesh, .59f, .40f, .59f, .31f, 1.3f, ink); Line(mesh, .41f, .31f, .59f, .31f, 1.3f, ink);
            Line(mesh, .50f, .85f, .50f, .94f, 1.3f, ink);
            if (inProgress) { Line(mesh, .24f, .48f, .76f, .48f, 3, edge); Line(mesh, .43f, .48f, .56f, .38f, 2, edge); }
            else { Line(mesh, .44f, .24f, .40f, .08f, 1.5f, ink); Line(mesh, .54f, .24f, .60f, .11f, 1.5f, ink); }
        }
        void Pendant(VertexHelper mesh)
        {
            var jade = new Color(.23f, .61f, .46f, .98f); var rim = new Color(.68f, .91f, .73f, 1); var line = new Color(.12f, .33f, .26f, 1);
            // Thick hollow ring preserves the unmistakable carved jade silhouette.
            Arc(mesh, .50f, .54f, .23f, .25f, 0, Mathf.PI * 2, 10, jade);
            Arc(mesh, .50f, .54f, .29f, .30f, 0, Mathf.PI * 2, 1.4f, rim);
            Arc(mesh, .45f, .57f, .115f, .085f, -.3f, Mathf.PI * 1.5f, 1.8f, rim);
            Arc(mesh, .56f, .49f, .11f, .08f, Mathf.PI * .7f, Mathf.PI * 2.3f, 1.6f, rim);
            Line(mesh, .50f, .86f, .50f, .97f, 1.8f, line);
            Quad(mesh, .43f, .82f, .14f, .06f, rim);
            Line(mesh, .50f, .25f, .50f, .10f, 1.8f, line); Line(mesh, .50f, .13f, .42f, .03f, 1.5f, jade); Line(mesh, .50f, .13f, .58f, .03f, 1.5f, jade);
        }
    }
}

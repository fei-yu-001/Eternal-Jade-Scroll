using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // 分层纸偶 v5：单张立绘按地平线切成四块（头 / 躯干含双袖 / 左腿 / 右腿），
    // 步态相位由**真实位移**驱动，待机由三条加性时间层（呼吸 + 重心上下 + 左右漂移）驱动。
    //
    // v2 的教训（Tools/split-puppet-layers.py 量过）：现有国风立绘的袖子与躯干在腋下是
    // 连成一片的，沿腋下切出"独立手臂"等于把整条袖子从身上搬走，一摆就露底色。
    // 所以 v3 起不再追求六关节的解剖学正确，改为"少切、稳住"：只有腿真的分得开才分腿。
    // v4 的教训：切区按"叠印"而不是"剪影"，条带取全宽，接缝处才不会露出背景方块。
    // v5 的教训（P-Character P2）：**时间驱动的相位会在玩家站着不动时继续摆腿**，
    // 那是"纸偶僵硬"里最刺眼的一条——腿在走、人没走。相位从此只认位移。
    public sealed class PuppetActor : MonoBehaviour
    {
        public enum Motion { Idle, Walk, Run }
        public static readonly string[] MotionNames = { "静立", "行走", "奔跑" };

        // M4-05 战斗短动作：一次性触发，靠 countdown 自己走完。表现层专用，不回写 Core 状态。
        public enum Action { Light, Heavy, Dodge, Flinch }

        /// <summary>渲染方式（W-A）：
        /// `Slices` = 旧的 4 条刚性色带（**默认，兜底**，现有夹具与线上表现都走它）；
        /// `MeshDeform` = 单图网格形变（膝盖能屈、脚掌能滚；像素仍是原图，身份闸恒等通过）；
        /// `Frames` = B-03 flipbook：逐帧 sheet 按**累计位移**切帧（**默认关**，等 B-11 素材到位）。</summary>
        public enum GaitMode { Slices, MeshDeform, Frames }

        float actionUntil;
        Action activeAction = Action.Light;
        float actionClock;

        // 触发一次短动作；Core 的阶段没到（硬直/收招）时不播放，表现跟着规则走。
        public void PlayAction(Action action, float now, bool coreReady)
        {
            if (!coreReady) return;
            activeAction = action;
            actionClock = 0f;
            actionUntil = now + ActionDuration(action);
        }

        // T100：这两个原来都是 public。ActionDuration 只有本文件在用（上面 PlayAction），
        // InAction 全仓零调用点——留着一个公开的状态查询，下一个人会以为有人按它做过冷却。
        static float ActionDuration(Action action) =>
            action == Action.Heavy ? .55f : action == Action.Light ? .34f :
            action == Action.Dodge ? .30f : .28f;

        RectTransform root;
        readonly RectTransform[] joints = new RectTransform[6];
        readonly (float x0, float x1, float v0, float v1)[] regions =
            new (float, float, float, float)[6];
        readonly RawImage[] slices = new RawImage[6];
        Motion motion = Motion.Idle;
        // `time` 只喂**与位移无关**的那几层：入场淡入 ramp、待机呼吸、转身缓冲。
        // 腿/躯干/颈的**步态相位**改由 strideDistance 驱动（见 P2），两者不再共用一个时钟。
        float time;
        float strideDistance;           // 已累计的真实位移（世界单位）
        float strideScale = 1f;         // 步幅倍率（旧"步频"参数的位移版）
        bool mirror;
        Vector2 position;
        Vector3 baseScale = Vector3.one;

        // ---- 幅度参数（P4：全部从字面量抽出来，可调而不用改代码）----
        // 默认值 = 抽出来之前的实测值，**这次重构不改变任何一帧的姿态**。
        [Header("步态（度 / 像素）")]
        [SerializeField] float walkLegDeg = 5.2f;
        [SerializeField] float runLegDeg = 7.2f;
        [SerializeField] float walkTorsoDeg = 1.5f;
        [SerializeField] float walkTorsoBiasDeg = -1f;
        [SerializeField] float runTorsoDeg = 2.2f;
        [SerializeField] float runTorsoBiasDeg = -3.4f;
        [SerializeField] float walkNeckDeg = 1.1f;
        [SerializeField] float runNeckDeg = 1.4f;
        [SerializeField] float runNeckBiasDeg = 1.1f;
        [SerializeField] float walkBobPx = 3.2f;
        [SerializeField] float runBobPx = 5.6f;
        [SerializeField] float walkSwayPx = 1.4f;
        [SerializeField] float runSwayPx = 2.2f;
        [SerializeField] float torsoPhaseLag = .55f;
        [SerializeField] float neckPhaseLag = 1.05f;
        [SerializeField] float swayPhaseRate = .5f;
        [Header("步频（半步时长，秒；配合速度换算成\"一个半步走多远\"）")]
        [SerializeField] float walkHalfStepSeconds = .34f;
        [SerializeField] float runHalfStepSeconds = .26f;
        [Header("待机（P5：idle 不等于静止）")]
        [SerializeField] float idleNeckDeg = .9f;
        [SerializeField] float idleBobPx = 1.6f;
        [SerializeField] float idleBreathRate = 1.6f;
        [SerializeField] float idleBobRate = 1.9f;
        [SerializeField] float idleSwayDeg = .55f;
        [SerializeField] float idleSwayRate = 1.05f;
        [SerializeField] float idleSwayPx = 1.1f;
        [Header("过渡")]
        [SerializeField] float rampSeconds = .25f;
        [SerializeField] float turnSeconds = .08f;
        [SerializeField] float turnSquash = .06f;
        [Header("战斗短动作（像素 / 度；时长见 ActionDuration）")]
        [SerializeField] float lightLungePx = 26f;
        [SerializeField] float lightLeanDeg = 4f;
        [SerializeField] float heavyWindupPx = 10f;
        [SerializeField] float heavyLungePx = 22f;
        [SerializeField] float heavyWindupLeanDeg = 8f;
        [SerializeField] float heavyLungeLeanDeg = 7f;
        [SerializeField] float dodgeSlidePx = 44f;
        [SerializeField] float dodgeLeanDeg = 6f;
        [SerializeField] float flinchPx = 16f;
        [SerializeField] float flinchLeanDeg = 9f;

        // ---- W-A 网格形变（默认关：gaitMode = Slices 时下面的参数一个都不参与运算）----
        // 骨架与步态全部可调，**不许留魔数**；默认值取自对 farmer-male-0-front.png 的实测，
        // 标定过程与判据见 Tools/logs/v-06-meshdeform-report.md。
        [Header("渲染方式（W-A 网格形变；默认 Slices = 旧色带）")]
        [SerializeField] GaitMode gaitMode = GaitMode.Slices;

        // ---- B-03 flipbook（默认关：`gaitMode = Slices` 时下面四个参数一个都不参与运算）----
        // 规矩（任务书 B-03）：帧索引**只由累计位移算**，位移为 0 ⇒ 索引不动（P2 的索引版）；
        // `frameCount` / `frameCycleDistance` **不许写死魔数**，一律进 [SerializeField]；
        // 默认硬切以避免新旧帧叠鞋；显式配置正值时保留 ≤60ms 交叉淡化，不做模糊处理。
        [SerializeField] Texture2D frameSheet;            // 逐帧 sheet（等距、底边对齐；B-04 的工具产）
        [SerializeField] int frameCount = 8;              // 一周期几帧
        [SerializeField] float frameCycleDistance = 240f; // 走完一整个周期要走的世界单位
        [SerializeField] float frameCrossFadeSeconds = 0f;  // 默认硬切；显式正值保留交叉淡化，硬上限 60ms

        /// <summary>B-03/B-11：喂 flipbook 走帧 sheet（B-04 拼装器产物：等距、底边对齐=脚底）。
        /// 喂入后立即重建渲染物；切不切 Frames 模式由宿主按视图决定（帧素材只有侧面档）。</summary>
        public void SetFrameSheet(Texture2D sheet, int frames = 8, float cycleDistance = 240f)
        {
            if (sheet == null || frames < 1 || sheet.width % frames != 0 || cycleDistance <= 0) return;
            if (sheet == frameSheet && frameCount == frames && Mathf.Approximately(frameCycleDistance, cycleDistance)) return;
            frameCount = frames; frameCycleDistance = cycleDistance;
            frameSheet = sheet;
            BuildRenderers();
        }
        [SerializeField] PuppetRigDefinition rigDefinition = PuppetRigDefinition.Default;
        [SerializeField] PuppetGaitParams walkFrontGait = PuppetGaitParams.Walk(true);
        [SerializeField] PuppetGaitParams walkSideGait = PuppetGaitParams.Walk(false);
        [SerializeField] PuppetGaitParams runFrontGait = PuppetGaitParams.Run(true);
        [SerializeField] PuppetGaitParams runSideGait = PuppetGaitParams.Run(false);

        // 关节索引。
        const int LegL = 0, LegR = 1, Torso = 2, ArmL = 3, ArmR = 4, Head = 5;

        // 夹具用的公开别名：断言要按"哪一块"取值，不该猜内部下标。
        public const int JointLegLeft = LegL, JointLegRight = LegR, JointTorso = Torso, JointHead = Head;

        // Rest uses the authored planted stance; walking distance is never advanced or reset to settle the feet.
        const float RestSettleSeconds = .14f;
        bool frameAtRest; float restFadeLeft;
        public bool ReducedMotionForRest { get; set; }
        public bool StandingFrameForCheck => frameAtRest && frameImage != null && frameImage.texture == view;
        public Motion CurrentMotion => motion;
        public Texture2D WalkingSheet => frameSheet;
        bool renderSuppressed;
        public bool RenderSuppressed => renderSuppressed;

        // The owner applies visibility to its current renderers, including those rebuilt on a turn.
        public void SetRenderSuppressed(bool suppressed)
        {
            if (renderSuppressed == suppressed) return;
            renderSuppressed = suppressed;
            ApplyRendererVisibility();
            if (!suppressed && motion == Motion.Idle && frameImage != null && view != null)
            {
                frameAtRest = true; restFadeLeft = 0; frameFadeLeft = 0; framePrevIndex = -1;
                ApplyFrame();
                if (frameFadeImage != null) frameFadeImage.color = new Color(1, 1, 1, 0);
            }
        }
        void ApplyRendererVisibility()
        {
            if (frameImage != null) frameImage.gameObject.SetActive(!renderSuppressed);
            if (frameFadeImage != null) frameFadeImage.gameObject.SetActive(!renderSuppressed);
            if (meshGraphic != null) meshGraphic.gameObject.SetActive(!renderSuppressed);
            foreach (var image in slices) if (image != null) image.gameObject.SetActive(!renderSuppressed);
        }
        public RectTransform Root => root;
        // T100 删掉的两条 public：`Position`（全仓零调用，读的一直是外部自己算的世界坐标）与
        // `Footfalls`（"脚步尘点"的消费者从来不存在，计数器写了没人读——写数不等于有功能）。
        // 真要落地尘，连producer带consumer一起回来，不留一个只在类内自增的数在这里装样子。

        // ---- P2：步态相位的对外口径（位移驱动，可被夹具直接断言）----
        /// <summary>把"这一帧真的挪动了多少世界单位"记进步态。位移为 0 时**相位不动**。</summary>
        public void AdvanceGait(float movedWorldUnits)
        {
            if (movedWorldUnits <= 0f) return;
            strideDistance += movedWorldUnits;
        }

        float CycleDistance => (motion == Motion.Run ? runHalfStepSeconds : walkHalfStepSeconds) /
                               strideScale * Mathf.Max(1f, GaitSpeedForCycle);
        /// <summary>换算 cycleDistance 用的速度基准（世界单位/秒）。城镇页会把自己当前的
        /// 移动速度喂进来；没喂过时用这个基准，保证样张页等旧调用点行为不变。</summary>
        public float GaitSpeedForCycle = 260f;

        /// <summary>当前步态相位（弧度）。**只由累计位移决定**，与运行了多久无关。</summary>
        public float GaitPhaseForCheck => strideDistance / Mathf.Max(1f, CycleDistance) * Mathf.PI;
        public float StrideDistanceForCheck => strideDistance;
        // T100：删掉 `JointForCheck(int)`——夹具要的是关节角度与数量（JointAngleForCheck /
        // BuiltJointCountForCheck，两条都有真调用点），拿 RectTransform 出去没人接。
        /// <summary>某一关节当前的 z 轴旋转角（度）。没建这块返回 NaN，让夹具报红而不是当成 0° 蒙过去。</summary>
        public float JointAngleForCheck(int index) =>
            index >= 0 && index < joints.Length && joints[index] != null
                ? joints[index].localEulerAngles.z : float.NaN;
        public int BuiltJointCountForCheck
        {
            get
            {
                var n = 0;
                foreach (var j in joints) if (j != null) n++;
                return n;
            }
        }
        public bool HasArmJointForCheck => joints[ArmL] != null || joints[ArmR] != null;
        public bool MirrorForCheck => mirror;
        /// <summary>验收用（WI-3）：Frames 模式下当前帧图是否真的被镜像了。
        /// 只读 uvRect，不改任何状态；非 Frames 模式返回 false（没渲染物可验，让夹具红而不是蒙混）。</summary>
        public bool FrameMirroredForCheck => frameImage != null && frameImage.uvRect.width < 0f;

        // ---- W-A：网格形变的对外口径（夹具按这两条钉住"接上了且站着不动"）----
        public GaitMode GaitModeForCheck => gaitMode;
        // B-03 夹具用：当前帧索引（-1 = 还没算出过；见 PuppetGait.FrameIndexAt 的非法参数约定）
        public int FrameIndexForCheck => frameIndex;
        public int FrameCountForCheck => frameCount;
        public bool FramesActiveForCheck => frameImage != null;
        /// <summary>网格模式的顶点数；色带模式恒为 0（= 没走网格），让夹具能分辨两种模式。</summary>
        public int DeformVertexCountForCheck => meshGraphic != null ? meshGraphic.VertexCountForCheck : 0;
        /// <summary>当前帧离静止姿态最远的顶点位移（图像比例）。站着不动必须是 0。</summary>
        public float DeformMaxDisplacementForCheck => meshGraphic != null ? meshGraphic.MaxDisplacementForCheck : 0f;
        // WALK-FIX：网格到底有没有进 VertexHelper / rect 是不是空的，只有这个口能看。
        // 单开一个访问器而不是五个转发属性 —— 数值由夹具 WalkFixShot 直接读，有真调用点。
        public PuppetMeshGraphic MeshForCheck => meshGraphic;

        bool DeformActive => meshGraphic != null;

        /// <summary>切换渲染方式并**立刻重建**（换档必须在当帧生效，否则夹具抓到的是旧模式）。
        /// 默认不调用它 = 全仓行为零变化；`FrontEndWorld` 的档位开关是目前的唯一运行期调用点。</summary>
        public void SetGaitMode(GaitMode mode)
        {
            if (gaitMode == mode && ((mode == GaitMode.MeshDeform) == DeformActive)) return;
            gaitMode = mode;
            BuildRenderers();
        }

        // basePoint = 脚底（枢轴取底边中点）：人物站在哪条地平线上，即是谁站在前面。
        public static PuppetActor Create(Transform parent, Texture2D view, Vector2 basePoint, Vector2 size)
        {
            var go = new GameObject("Puppet actor", typeof(RectTransform));
            var root = go.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0, 1);
            root.pivot = new Vector2(.5f, 0f);
            root.sizeDelta = size;
            var actor = go.AddComponent<PuppetActor>();
            actor.root = root;
            actor.BuildJoints(view, size);
            actor.position = basePoint;
            root.anchoredPosition = basePoint;
            return actor;
        }

        // 切区（人物比例，x 自左、v 自顶）。
        //
        // v4 的教训（Tools/split-puppet-layers.py 与逐行测量）：这批立绘的袖子与躯干在
        // 腋下粘连，**切不出独立手臂**；若硬把躯干收窄成中带，中带两侧就有几十像素透明，
        // 一摆动就露出底色方块。
        //
        // 纸偶的正确分层是"叠印"而不是"剪影"：头/躯干/腿各取**全宽**条带，
        // 条带内该透的地方本来就透，拼接后严丝合缝；接缝处永远不会出现"不属于人物的像素"，
        // 也不会出现"一个脑袋变两个"。
        // 代价：袖子不做解剖学摆臂——这批立绘本来就没有能独立摆的手臂，
        // 袍子的生气由躯干前倾 + 颈跟随 + 整体上下起伏三项合成（见 Update）。
        void BuildJoints(Texture2D texture, Vector2 size)
        {
            view = texture;
            rectSize = size;
            // 四块，各占自己的条带，**互不重叠**：
            //   头   0 – 0.22
            //   躯干 0.22 – 0.76（含双袖；下摆到腰下仍连成一片，所以一直连到真正分腿处）
            //   左腿 / 右腿 0.76 – 1（中线在此才分开，见 split-puppet-layers.py 测量）
            // ArmL/ArmR 不再切独立块：这批立绘腋下与躯干粘连，量过没有可切的缝；
            // 强行切就会在同一处画出第二个上半身（叠印）或背景方块（剪影）。袖子随躯干整体摆。
            regions[Head] = (0f, 1f, 0f, .22f);
            regions[Torso] = (0f, 1f, .22f, .76f);
            regions[LegL] = (0f, .50f, .76f, 1f);
            regions[LegR] = (.50f, 1f, .76f, 1f);
            BuildRenderers();
        }

        /// <summary>按当前 `gaitMode` 重建渲染对象。换档时**先清掉旧的**，否则两套会同时挂在 root 上
        /// （色带的 4 块 + 网格，叠着画出来就是两个上半身）。</summary>
        void BuildRenderers()
        {
            foreach (var i in new[] { LegL, LegR, Torso, Head })
            {
                if (joints[i] == null) continue;
                Kill(joints[i].gameObject);
                joints[i] = null;
                slices[i] = null;
            }
            if (meshGraphic != null) { Kill(meshGraphic.gameObject); meshGraphic = null; }
            if (frameImage != null) { Kill(frameImage.gameObject); frameImage = null; }
            if (frameFadeImage != null) { Kill(frameFadeImage.gameObject); frameFadeImage = null; }
            frameIndex = -1;
            framePrevIndex = -1;
            frameFadeLeft = 0f; frameAtRest = false; restFadeLeft = 0f;
            if (gaitMode == GaitMode.MeshDeform) BuildMesh();
            else if (gaitMode == GaitMode.Frames) BuildFrames();
            else BuildSliceRenderers();
            ApplyMirror();
            ApplyRendererVisibility();
        }

        // 运行期用 Destroy、编辑器里用 DestroyImmediate：批量夹具跑在 play 模式，
        // 两种都要能立刻生效（夹具换档后当帧就要读到新模式）。
        // 先 SetActive(false)：Destroy 要到帧末才真正移除，不先关掉就会**多画一帧**旧渲染物。
        static void Kill(Object target)
        {
            if (target == null) return;
            if (target is GameObject go) go.SetActive(false);
            else if (target is Component comp && comp != null) comp.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        /// <summary>B-03：flipbook 渲染物 = 两张等大的 RawImage（当前帧 + 上一帧）。
        /// 默认硬切，上一帧保持透明；仅显式配置正值时让上一帧 alpha 1→0 衰减（≤60ms）。
        /// 两张图直接使用原 sheet 像素，不生成中间帧或对素材做模糊处理。</summary>
        void BuildFrames()
        {
            frameImage = MakeFrameImage("Puppet frame");
            frameFadeImage = MakeFrameImage("Puppet frame fade");
            frameFadeImage.color = new Color(1f, 1f, 1f, 0f);
            frameIndex = -1;
            framePrevIndex = -1;
            frameFadeLeft = 0f;
            ApplyFrame();
        }

        RawImage MakeFrameImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            // 走帧 sheet 是**侧面档**：单帧宽高比与正面立绘的 rectSize 不同，按 sheet 单帧比例
            // 重算尺寸并**脚底贴 root 底边**（走帧已底边对齐=脚底，B-04 拼装器保证）；
            // 否则用正面立绘的 rectSize 会横向压扁 ~22%，一眼看出变形。
            var frameAspect = frameSheet != null && frameCount > 0
                ? (frameSheet.width / (float)frameCount) / frameSheet.height
                : rectSize.x / rectSize.y;
            var frameSize = new Vector2(rectSize.y * frameAspect, rectSize.y);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);   // 底边中央对齐（= 脚底）
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = frameSize;
            rect.anchoredPosition = Vector2.zero;
            var image = go.AddComponent<RawImage>();
            image.texture = frameSheet;
            image.raycastTarget = false;
            return image;
        }

        // 按索引切 uvRect：sheet 横向等距，第 i 帧占 [i/n, (i+1)/n]。
        // 镜像时窗口右端起、宽取负（与 ApplyMirror 里 slices 的翻法同式），帧序不变、画面水平翻转。
        void SetFrameSlice(RawImage image, int index)
        {
            if (image == null || frameSheet == null || frameCount <= 0) return;
            var w = 1f / frameCount;
            image.uvRect = mirror ? new Rect((index + 1) * w, 0f, -w, 1f) : new Rect(index * w, 0f, w, 1f);
        }

        void ApplyFrame()
        {
            if (frameImage == null) return;
            if (frameAtRest && view != null)
            {
                frameImage.texture = view;
                frameImage.rectTransform.sizeDelta = new Vector2(rectSize.y * view.width / view.height, rectSize.y);
                frameImage.uvRect = mirror ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1);
                return;
            }
            frameImage.texture = frameSheet;
            if (frameSheet != null && frameCount > 0)
                frameImage.rectTransform.sizeDelta = new Vector2(rectSize.y * frameSheet.width / (frameCount * frameSheet.height), rectSize.y);
            SetFrameSlice(frameImage, frameIndex < 0 ? 0 : frameIndex);
            if (frameFadeImage != null && !(framePrevIndex >= 0 && frameFadeLeft > 0f))
                frameFadeImage.color = new Color(1f, 1f, 1f, 0f);
            else if (frameFadeImage != null)
                SetFrameSlice(frameFadeImage, framePrevIndex);
        }

        /// <summary>每帧推进 flipbook。**索引只由 `strideDistance` 算** ⇒ 位移为 0 时索引必然不动（P2）。</summary>
        void UpdateFrame(float dt)
        {
            if (frameImage == null) return;
            if (motion == Motion.Idle && conveyorSpeed <= 0 && view != null)
            {
                if (!frameAtRest)
                {
                    frameAtRest = true;
                    // A stopped character has one planted body, never two opaque overlapping poses.
                    restFadeLeft = 0;
                    if (frameFadeImage != null)
                    {
                        frameFadeImage.texture = frameSheet;
                        SetFrameSlice(frameFadeImage, Mathf.Max(0, frameIndex));
                    }
                    framePrevIndex = -1; frameFadeLeft = 0;
                }
                restFadeLeft = ReducedMotionForRest ? 0 : Mathf.Max(0, restFadeLeft - dt);
                ApplyFrame();
                if (frameFadeImage != null) frameFadeImage.color = new Color(1, 1, 1, restFadeLeft / RestSettleSeconds);
                return;
            }
            if (frameAtRest)
            {
                frameAtRest = false; restFadeLeft = 0; framePrevIndex = -1; frameFadeLeft = 0;
                if (frameFadeImage != null) frameFadeImage.color = new Color(1, 1, 1, 0);
            }
            var idx = PuppetGait.FrameIndexAt(strideDistance, frameCycleDistance, frameCount);
            if (idx < 0)
            {
                if (!frameWarned)
                {
                    frameWarned = true;
                    Debug.LogWarning("[B-03] Frames 模式参数非法：frameCount=" + frameCount +
                        " / frameCycleDistance=" + frameCycleDistance +
                        " ⇒ 切帧停用（有这一条日志，不是静默回退）。");
                }
                return;
            }
            if (idx != frameIndex)
            {
                framePrevIndex = frameIndex;
                frameIndex = idx;
                frameFadeLeft = framePrevIndex >= 0 ? Mathf.Clamp(frameCrossFadeSeconds, 0f, .06f) : 0f;   // 硬上限 60ms
            }
            else if (frameFadeLeft > 0f)
            {
                frameFadeLeft = Mathf.Max(0f, frameFadeLeft - dt);
            }
            ApplyFrame();
            if (frameFadeImage != null)
            {
                var fade = Mathf.Clamp(frameCrossFadeSeconds, 0f, .06f);
                // A rebuilt actor has no previous frame; its default UV still spans the whole sheet.
                frameFadeImage.color = new Color(1f, 1f, 1f, framePrevIndex >= 0 && fade > 0f ? frameFadeLeft / fade : 0f);
            }
        }

        /// <summary>W-A：整张立绘一个网格（不再切块），顶点由 `PuppetRig` 形变。</summary>
        void BuildMesh()
        {
            var go = new GameObject("Puppet mesh", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);   // 与 root 左上角对齐
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = rectSize;
            rect.anchoredPosition = Vector2.zero;
            meshGraphic = go.AddComponent<PuppetMeshGraphic>();
            meshGraphic.Configure(view, rigDefinition, mirror, legsSplit);
        }

        void BuildSliceRenderers()
        {
            // 枢轴写在自己 Rect 的归一化空间：pivot.y = 0 是**底边**、1 是顶边。
            //   腿块（v 0.76–1）：髋在条带**顶** → 0.96；绕 0.04 就变成绕脚踝当钟摆甩，整条腿会甩离身体。
            //   躯干块（v 0.22–0.76）：胯在条带**底** → 0.04。
            //   头块（v 0–0.22）：颈在条带**底** → 0.12。
            // 枢轴（在自己 Rect 的归一化空间，y=0 底边 / y=1 顶边）：
            //   头  绕颈 → 颈在条带底，0.12
            //   躯干 绕胯 → 胯在条带底，0.04
            //   腿  绕髋 → **髋在条带顶，取 1.0**；曾误取 0.96/0.04，等于绕脚踝当钟摆甩，
            //   整条腿会甩离身体（战斗页 230px 下最显眼）。1.0 时枢轴恰在条带顶边＝胯。
            var pivots = new[]
            {
                new Vector2(.5f, 1f), new Vector2(.5f, 1f),        // 腿：绕髋（条带顶边）
                new Vector2(.5f, .04f),                            // 躯干：绕胯
                Vector2.zero, Vector2.zero,                         // 袖位（3/4）：不建对象，仅占位保持下标对齐
                new Vector2(.5f, .12f),                            // 头：绕颈
            };
            // 只建 4 块：头、躯干、左右腿。袖位保留在索引里但不建对象（见上）。
            foreach (var i in new[] { LegL, LegR, Torso, Head })
            {
                var (x0, x1, v0, v1) = regions[i];
                var rect = new GameObject("Puppet joint " + i, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(root, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.pivot = pivots[i];
                rect.sizeDelta = new Vector2((x1 - x0) * rectSize.x, (v1 - v0) * rectSize.y);
                // anchoredPosition 定位的是**枢轴点**，不是矩形左上角；且坐标从底边起算。
                // 两个搞错都不会报错，只会整块错位——小图看不出来，230px 的战斗页一目了然。
                // 坐标一律**从底边量**（root.pivot = (.5,0)，anchoredPosition 即脚底）。
                // v 是"自图顶向下"的比例，所以块底距 root 底 = -(v1)*rectSize.y；
                // x 向：块左缘在 x0*rectSize.x（root 局部原点在左上角），枢轴再往块内偏 pivot.x 个块宽。
                // 旧公式 -x0*size.x 把全宽块整体左移半身（彼此仍对齐、看不出），却把两条腿
                // 各甩出左边缘——右腿整个出体、左腿截半，就是玩家截图里的"腿身错位"。
                var blockBottom = -v1 * rectSize.y;
                rect.anchoredPosition = new Vector2(
                    x0 * rectSize.x + pivots[i].x * rect.sizeDelta.x,
                    blockBottom + pivots[i].y * rect.sizeDelta.y);
                var image = rect.gameObject.AddComponent<RawImage>();
                image.texture = view;
                image.raycastTarget = false;
                joints[i] = rect;
                slices[i] = image;
            }
            ApplyMirror();
        }

        void ApplyMirror()
        {
            if (meshGraphic != null) meshGraphic.SetMirror(mirror);
            foreach (var i in new[] { LegL, LegR, Torso, Head })
            {
                if (slices[i] == null) continue;
                var (x0, x1, v0, v1) = regions[i];
                slices[i].uvRect = new Rect(mirror ? x1 : x0, 1f - v1, mirror ? -(x1 - x0) : (x1 - x0), v1 - v0);
            }
            // Frames 模式也要跟着翻：不修这条，左走播的是右走的同向帧（10-07 自记的观感债，WI-3 收口）。
            // 与 slices 同式：uvRect 宽取负即水平镜像。这里**只改 uv、不动 color**——
            // 保留显式正值的淡化状态；默认硬切时上一帧维持透明，镜像仍只改 UV。
            if (frameImage != null) { if (frameAtRest) ApplyFrame(); else SetFrameSlice(frameImage, frameIndex < 0 ? 0 : frameIndex); }
            if (frameFadeImage != null && framePrevIndex >= 0) SetFrameSlice(frameFadeImage, framePrevIndex);
        }

        Texture2D view;
        Vector2 rectSize;
        PuppetMeshGraphic meshGraphic;   // 只在 GaitMode.MeshDeform 下非空（见 BuildRenderers）
        // B-03 flipbook：当前帧 + 上一帧兼容淡化层（默认透明；显式正值时 ≤60ms）；只在 Frames 模式下非空。
        RawImage frameImage, frameFadeImage;
        int frameIndex = -1, framePrevIndex = -1;
        float frameFadeLeft;
        bool frameWarned;
        // 侧身档不切腿（见 SetLegSplit 的注释）。
        bool legsSplit = true;

        /// <summary>切不切腿由**朝向**决定：正面/背面两条腿对称分布在中线两侧，切开是对的；
        /// 侧身图的两条腿在画里前后重叠，按 x 中线切开等于把两条腿各劈一半——
        /// 两块腿的枢轴落在 .25 / .75（各自半带的中心），一 ±5° 就把腿片甩离身体
        /// （Q1 目视实图 `p3-2-walk-west-b.png` 为证：上半身在原位，两条腿变成两片碎片）。
        /// 侧身档改成两块腿共用与躯干同宽的同一条带（叠印成一块，v4 的老办法）且不旋转，
        /// 走动由躯干前倾 + 颈跟随 + 整体起伏承担。宁可少一个关节，也不要在玩家眼前露馅。</summary>
        public void SetLegSplit(bool split)
        {
            if (legsSplit == split) return;
            legsSplit = split;
            LayoutLegs();
        }

        // 按当前 legsSplit 重排两块腿：区域、尺寸、枢轴位置、uv 一起改，缺一处就错位。
        void LayoutLegs()
        {
            regions[LegL] = legsSplit ? (0f, .50f, .76f, 1f) : (0f, 1f, .76f, 1f);
            regions[LegR] = legsSplit ? (.50f, 1f, .76f, 1f) : (0f, 1f, .76f, 1f);
            foreach (var i in new[] { LegL, LegR })
            {
                if (joints[i] == null) continue;
                var (x0, x1, v0, v1) = regions[i];
                joints[i].sizeDelta = new Vector2((x1 - x0) * rectSize.x, (v1 - v0) * rectSize.y);
                joints[i].anchoredPosition = new Vector2(
                    x0 * rectSize.x + joints[i].pivot.x * joints[i].sizeDelta.x,
                    -v1 * rectSize.y + joints[i].pivot.y * joints[i].sizeDelta.y);
            }
            ApplyMirror();
        }

        // P3：素材只有 3 向（front/side/back），左右共用 side 靠 uv 镜像，所以换朝向**不重建关节**、
        // 只换贴图。重建会丢相位与当前姿态，走两步就抽一下。
        public void SetView(Texture2D next)
        {
            if (next == null || next == view) return;
            view = next;
            if (meshGraphic != null) meshGraphic.SetSource(next);
            foreach (var i in new[] { LegL, LegR, Torso, Head })
                if (slices[i] != null) slices[i].texture = next;
        }

        public Texture2D View => view;

        public void SetMotion(Motion value)
        {
            if (motion == value) return;
            motion = value;
            // 不再重置 `time`：相位已改由位移驱动，换档时把时钟清零等于把 ramp 清零，
            // 腿会在切换那一帧弹回 0°（走→停最显眼）。ramp 现在只在创建后走一次。
        }

        // 展示台用的"传送带"：预览页的行走样张没有真实位移可记（人站在原地给玩家看），
        // 由宿主显式声明"按这个速度空转"。城镇页**不用**它——那里喂真实位移，
        // 位移为 0 时相位必须不动（P2 的硬判据）。
        float conveyorSpeed;
        public void SetGaitConveyor(float worldUnitsPerSecond)
        {
            conveyorSpeed = Mathf.Max(0f, worldUnitsPerSecond);
        }

        // T100：删掉 `SetStepScale`。P2 之后步幅倍率不再是外部可调的参数（位移驱动的相位里
        // "走得快 = 一步迈得更远"，倍率折进了 CycleDistance），没有任何调用点调过它，
        // 留着只会让下一个人以为城镇页可以按 NPC 调步频。
        // `SetScale` 保留并且这一次真的接上了消费者：镇页主角的呈现档位（Q1.7 第 4 条）走它。

        // 鸟瞰透视：角色越往北（画的上端）越小。
        public void SetScale(float scale)
        {
            baseScale = Vector3.one * scale;
            root.localScale = baseScale;
        }

        public void SetPosition(Vector2 value)
        {
            position = value;
            root.anchoredPosition = value;
        }

        public void SetMirror(bool value)
        {
            if (mirror == value) return;
            mirror = value;
            ApplyMirror();
            // 转向缓冲：镜像不做硬切，给 80ms 的挤压过渡（不做 180° 翻面）。
            turnTime = 0f;
        }

        float turnTime = 1f;

        void Update()
        {
            time += Time.unscaledDeltaTime;
            turnTime += Time.unscaledDeltaTime;
            if (conveyorSpeed > 0f) strideDistance += conveyorSpeed * Time.unscaledDeltaTime;
            var w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / Mathf.Max(.01f, rampSeconds)));
            var turn = Mathf.Clamp01(turnTime / Mathf.Max(.01f, turnSeconds));
            var squash = Mathf.Sin(turn * Mathf.PI) * turnSquash;
            root.localScale = Vector3.Scale(baseScale, new Vector3(1f + squash, 1f - squash, 1f));
            float legL = 0f, legR = 0f, body = 0f, neck = 0f, bob = 0f, sway = 0f, gaitPhase = 0f;
            var deform = DeformActive;
            // B-03：flipbook 每帧推进（只在本模式下有渲染物，其余模式这一行是空转）。
            if (gaitMode == GaitMode.Frames) UpdateFrame(Time.unscaledDeltaTime);
            switch (motion)
            {
                case Motion.Idle:
                {
                    // P5：idle 不等于"什么都不动"。呼吸（颈 + 躯干微仰 + 重心上下）与
                    // 重心左右漂移是**两条不同频率的加性层**，都叠在基础姿态上；
                    // 它们按时间走是对的——人站着不动胸腔也在起伏，这与"腿该不该摆"是两件事。
                    neck = Mathf.Sin(time * idleBreathRate) * idleNeckDeg * w;
                    body = Mathf.Sin(time * idleBreathRate + .9f) * idleSwayDeg * w;
                    bob = Mathf.Sin(time * idleBobRate) * idleBobPx * w;
                    sway = Mathf.Sin(time * idleSwayRate) * idleSwayPx * w;
                    if(gaitMode==GaitMode.Frames)bob=sway=0f; // Planted rest artwork keeps both soles on the ground.
                    break;
                }
                case Motion.Walk:
                case Motion.Run:
                {
                    var run = motion == Motion.Run;
                    // **P2 核心**：相位只来自累计位移，不再来自 `time`。
                    // 站着不动（哪怕被墙挡住、哪怕批处理里空转几百帧）→ 腿停在当前角度不动。
                    var ph = GaitPhaseForCheck % (Mathf.PI * 2f);
                    // T100：原来这里还有一行"跨过半步就 footfalls++"。计数器有写没读
                    // （唯一的出口是那个零调用的 public Footfalls），所以整条边沿检测一起删了——
                    // 留着它等于留着一个"脚步落地已被处理"的假象。
                    // 腿是唯一真关节：绕髋前后摆。躯干/颈各慢半拍，形成"甩一下袍子"的跟随感。
                    if (deform)
                    {
                        // 网格模式：色带那套腿/躯干角度一概**不算**（算出来不写进任何 transform = 假代码），
                        // 只把相位交给 PuppetGait；起伏与左右摆**烘在姿态里**（PuppetRigPose.HipBob/HipSway），
                        // 所以下面根节点不再叠加 bob/sway，否则会算两遍。
                        gaitPhase = ph;
                    }
                    else
                    {
                        legL = Mathf.Sin(ph) * (run ? runLegDeg : walkLegDeg) * w;
                        legR = -Mathf.Sin(ph) * (run ? runLegDeg : walkLegDeg) * w;
                        body = Mathf.Sin(ph - torsoPhaseLag) * (run ? runTorsoDeg : walkTorsoDeg) * w +
                               (run ? runTorsoBiasDeg : walkTorsoBiasDeg) * w;
                        neck = Mathf.Sin(ph - neckPhaseLag) * (run ? runNeckDeg : walkNeckDeg) * w +
                               (run ? runNeckBiasDeg : 0f) * w;
                        bob = Mathf.Abs(Mathf.Sin(ph)) * (run ? runBobPx : walkBobPx) * w;
                        sway = Mathf.Sin(ph * swayPhaseRate) * (run ? runSwayPx : walkSwayPx) * w;
                    }
                    break;
                }
            }
            // 侧身档不摆腿（见 SetLegSplit）：两块腿是同一条件带的叠印，一转就劈成两片。
            // ⚠ 本句说的是**色带模式 Slices**（下面 `if (!legsSplit) { legL = 0f; legR = 0f; }`）。
            //   **网格形变模式 MeshDeform 不是这样**：那一支走 `PuppetGait.Pose(..., legsSplit:false)`
            //   → 姿态 `CollapseToSingleLeg()` ⇒ **一条共用链照样走完整走周期**，
            //   侧身档抬脚量实测 0.0415 图高（30px @720，见 Tools/PuppetRigChecks 那条断言）。
            //   两种模式行为不同，**不许把这一句当成侧身档的全部真相**（B-02 口径统一）。
            if (deform)
            {
                // W-A：网格模式。idle 用呼吸/漂移那几条加性层（它们在上面已经算好），
                // 走/跑用 PuppetGait 的姿态。**不再写任何关节旋转**（网格模式没有关节对象）。
                PuppetRigPose pose;
                if (motion == Motion.Idle)
                {
                    // 站着不动**绝不取走周期里的相位**（走周期里没有"两腿都中立"的相位，见 PuppetGait.Idle）。
                    // ⚠ 单位：色带模式的 `idleBobPx` / `idleSwayPx` 是**像素**，而
                    // `PuppetRigPose.HipBob/HipSway` 是**图像比例**（1 = 整张图高/宽）。
                    // 这里必须换算 —— 不换算的话 1.6px 会被当成 **1.6 个图高**，人物直接飞出画面。
                    // （写这行时就是这么错的；离线判据抓不到单位，靠夹具那条"站立位移 ≤5% 图高"兜住。）
                    pose = PuppetGait.Idle(neck, body,
                        rectSize.y > 0f ? bob / rectSize.y : 0f,
                        rectSize.x > 0f ? sway / rectSize.x : 0f);
                }
                else
                {
                    var gp = motion == Motion.Run
                        ? (legsSplit ? runFrontGait : runSideGait)
                        : (legsSplit ? walkFrontGait : walkSideGait);
                    pose = PuppetGait.Pose(gaitPhase, gp, legsSplit);
                }
                meshGraphic.ApplyPose(pose, legsSplit);
            }
            else if (gaitMode != GaitMode.Frames)
            {
                if (!legsSplit) { legL = 0f; legR = 0f; }
                joints[LegL].localRotation = Quaternion.Euler(0, 0, legL);
                joints[LegR].localRotation = Quaternion.Euler(0, 0, legR);
                joints[Torso].localRotation = Quaternion.Euler(0, 0, body);
                joints[Head].localRotation = Quaternion.Euler(0, 0, neck);
            }
            // P4：原来这里算着 armL/armR 两路 sin，但**从不写进任何 transform**——
            // 因为袖块根本没建对象（见 BuildJoints 的 v2 教训：这批立绘腋下与躯干粘连，
            // 量过没有可切的缝）。算而不用是假代码：读代码的人以为有双臂关节，实际没有。
            // 本轮选择"删除 + 断言"：上半身的动由躯干前倾与颈两项真实承担（上面两行），
            // HasArmJointForCheck 恒为 false 由夹具钉住，将来真灌进能独立摆的手臂素材时，
            // 建第 5/6 块关节并删掉这条断言，而不是再留一段算出来没人用的角度。
            // 短动作叠加：出拳前冲、闪避侧移、受击后仰、蓄力下沉。
            var actionOffset = Vector2.zero;
            var actionLean = 0f;
            if (Time.unscaledTime < actionUntil)
            {
                actionClock = Mathf.Min(actionClock + Time.unscaledDeltaTime, ActionDuration(activeAction));
                var t = actionClock / Mathf.Max(.0001f, ActionDuration(activeAction));
                switch (activeAction)
                {
                    case Action.Light:
                        actionOffset = new Vector2(Mathf.Sin(t * Mathf.PI) * lightLungePx, 0f);
                        actionLean = Mathf.Sin(t * Mathf.PI) * lightLeanDeg;
                        break;
                    case Action.Heavy:
                        // 前段下沉蓄力，后段冲出。
                        actionOffset = new Vector2(-Mathf.Sin(t * Mathf.PI) * heavyWindupPx +
                            Mathf.Sin((t - .5f) * Mathf.PI * 2f) * heavyLungePx,
                            t < .4f ? -Mathf.Sin(t / .4f * Mathf.PI) * heavyWindupPx : 0f);
                        actionLean = t < .4f ? -heavyWindupLeanDeg : heavyLungeLeanDeg;
                        break;
                    case Action.Dodge:
                        actionOffset = new Vector2(Mathf.Sin(t * Mathf.PI) * dodgeSlidePx, 0f);
                        actionLean = -Mathf.Sin(t * Mathf.PI) * dodgeLeanDeg;
                        break;
                    case Action.Flinch:
                        actionOffset = new Vector2(-Mathf.Sin(t * Mathf.PI) * flinchPx, 0f);
                        actionLean = Mathf.Sin(t * Mathf.PI) * flinchLeanDeg;
                        break;
                }
            }
            root.anchoredPosition = position +
                (deform ? Vector2.zero : new Vector2(sway, -bob) * baseScale.y) + actionOffset;
            // 收招后必须把倾斜写回 0：旧代码用 `else if (!mirror)` 短路，于是**朝左**的人物
            // 闪避完之后会永远歪着最后一个 lean 角度（城镇页开始用镜像后就会露出来）。
            root.localRotation = actionLean != 0f ? Quaternion.Euler(0f, 0f, actionLean) : Quaternion.identity;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 低代价飞船二维剪影烘焙器 (Vessel Silhouette Baker)
    /// 核心特性：
    /// 1. 零常驻开销：平时飞行 0.0ms CPU / 0 DrawCall，完全不运行 Camera。
    /// 2. 灵动动态捕获：在分级、受损解体、对接分离等飞船结构变动时，以 15 FPS 运行约 3 秒动态剪影捕获，
    ///    直观呈现助推器脱离、整流罩分离或太阳翼展开的动态过程，随后彻底休眠。
    /// 3. 显存直通：直接保留在 128x128 硬件抗锯齿 RenderTexture，无 CPU Readback 掉帧阻塞。
    /// 4. 视锥隔离：离屏相机 cullingMask = 0，仅通过 CommandBuffer 定向绘制飞船部件网格，杜绝场景与天体背景污染。
    /// 5. 权威俯视正交投影：基于 ActiveVessel.ReferenceTransform 建立视图矩阵并自适应居中缩放，
    ///    机头始终对齐 +Y，与滚转角（Roll）严格契合。
    /// </summary>
    public class VesselSilhouetteBaker : MonoBehaviour, IVesselSilhouetteProvider
    {
        private static VesselSilhouetteBaker _instance;
        public static VesselSilhouetteBaker Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("VesselSilhouetteBaker_Auto");
                    _instance = go.AddComponent<VesselSilhouetteBaker>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        public const int TextureResolution = 512;
        public const float DefaultBurstFps = 15f;
        public const float DefaultBurstDuration = 3.0f;

        private RenderTexture _renderTexture;
        public Texture SilhouetteTexture => _renderTexture;

        public event Action<Texture> OnSilhouetteUpdated;

        private Camera _offscreenCamera;
        private Material _silhouetteMaterial;

        // 动态突发烘焙状态
        private float _burstRemainingTime = 0f;
        private float _burstAccumulator = 0f;
        private float _burstInterval = 1f / DefaultBurstFps;

        // 飞船状态跟踪 (用于备用触发)
        private Vessel _lastTrackedVessel = null;
        private int _lastPartCount = -1;

        // 动态分离动画：脱离部件追踪与平滑视口过渡
        private readonly HashSet<Part> _lastVesselParts = new HashSet<Part>();
        private readonly List<Part> _detachedParts = new List<Part>();
        private float _smoothedViewExtent = 0f;
        private Vector3 _smoothedWorldCenter = Vector3.zero;
        private bool _hasInitializedBounds = false;

        // 预分配列表以避免 GC 垃圾回收压力
        private readonly List<MeshFilter> _cachedMeshFilters = new List<MeshFilter>(128);
        private readonly List<SkinnedMeshRenderer> _cachedSkinnedMeshes = new List<SkinnedMeshRenderer>(16);

        // 几何包围盒数据
        public float NormalizedNoseTipY { get; private set; } = 1.0f;
        public float NormalizedEngineBottomY { get; private set; } = -1.0f;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            InitializeRenderPipeline();
            RegisterEvents();
            VesselSilhouetteService.Provider = this;
        }

        private void InitializeRenderPipeline()
        {
            if (_renderTexture == null)
            {
                _renderTexture = new RenderTexture(TextureResolution, TextureResolution, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Vessel_Silhouette_RT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 8, // 启用 8x 硬件抗锯齿，彻底消除飞船 3D 部件与桁架边缘阶梯锯齿
                    useMipMap = false,
                    autoGenerateMips = false
                };
                _renderTexture.Create();
            }

            if (_offscreenCamera == null)
            {
                GameObject camObj = new GameObject("Silhouette_Offscreen_Cam", typeof(Camera));
                camObj.transform.SetParent(transform, false);

                _offscreenCamera = camObj.GetComponent<Camera>();
                _offscreenCamera.enabled = false; // 严禁每帧自动渲染
                _offscreenCamera.cullingMask = 0; // 剔除一切场景物体
                _offscreenCamera.clearFlags = CameraClearFlags.SolidColor;
                _offscreenCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _offscreenCamera.targetTexture = _renderTexture;
                _offscreenCamera.orthographic = true;
                _offscreenCamera.aspect = 1.0f;
                _offscreenCamera.nearClipPlane = 0.5f;
                _offscreenCamera.farClipPlane = 500f;
            }

            if (_silhouetteMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default")
                             ?? Shader.Find("Unlit/Color")
                             ?? Shader.Find("UI/Default")
                             ?? Shader.Find("Hidden/Internal-Colored");

                _silhouetteMaterial = new Material(shader)
                {
                    color = Color.white
                };
            }
        }

        private void RegisterEvents()
        {
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
            GameEvents.onStageActivate.Add(OnStageActivate);
            GameEvents.onPartJointBreak.Add(OnPartJointBreak);
            GameEvents.onPartDie.Add(OnPartDie);
            GameEvents.onPartUndock.Add(OnPartUndock);
            GameEvents.onPartCouple.Add(OnPartCouple);
            GameEvents.onVesselLoaded.Add(OnVesselLoaded);
        }

        private void UnregisterEvents()
        {
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
            GameEvents.onStageActivate.Remove(OnStageActivate);
            GameEvents.onPartJointBreak.Remove(OnPartJointBreak);
            GameEvents.onPartDie.Remove(OnPartDie);
            GameEvents.onPartUndock.Remove(OnPartUndock);
            GameEvents.onPartCouple.Remove(OnPartCouple);
            GameEvents.onVesselLoaded.Remove(OnVesselLoaded);
        }

        private void Start()
        {
            // 启动时触发一次突发捕获，确保初次就绪
            TriggerBurst(1.5f);
        }

        private void Update()
        {
            if (MFPProfiler.IsMasterBypassed) return;

            Vessel active = FlightGlobals.ActiveVessel;
            if (active == null || !active.loaded || active.packed)
            {
                return;
            }

            MFPProfiler.BeginSample(ProfilerSection.Silhouette);
            try
            {
                // 备用状态监测：若检测到载具切换或部件数量变化，自动激活动态捕获
                if (active != _lastTrackedVessel)
                {
                    _lastTrackedVessel = active;
                    _lastPartCount = active.parts != null ? active.parts.Count : 0;
                    TriggerBurst(2.5f);
                }
                else if (active.parts != null && active.parts.Count != _lastPartCount)
                {
                    _lastPartCount = active.parts.Count;
                    TriggerBurst(DefaultBurstDuration);
                }

                // 执行 15 FPS 动态剪影突发捕获
                if (_burstRemainingTime > 0f)
                {
                    float dt = Time.unscaledDeltaTime;
                    _burstRemainingTime -= dt;
                    _burstAccumulator += dt;

                    if (_burstAccumulator >= _burstInterval)
                    {
                        _burstAccumulator = 0f;
                        BakeNow();
                    }

                    // 捕获阶段结束前执行最后一次权威校准并彻底休眠
                    if (_burstRemainingTime <= 0f)
                    {
                        _burstRemainingTime = 0f;
                        BakeNow();
                        _detachedParts.Clear();
                    }
                }
                else
                {
                    // 静止飞行阶段保持部件列表最新，用于检测下一次分离 (仅当部件数变化或首次进入时刷新，杜绝每帧数万次 HashSet 分配)
                    _detachedParts.Clear();
                    if (active.parts != null && _lastVesselParts.Count != active.parts.Count)
                    {
                        _lastVesselParts.Clear();
                        for (int i = 0; i < active.parts.Count; i++)
                        {
                            _lastVesselParts.Add(active.parts[i]);
                        }
                    }
                }
            }
            finally
            {
                MFPProfiler.EndSample(ProfilerSection.Silhouette);
            }
        }

        /// <summary>
        /// 触发指定时长的 15 FPS 动态剪影捕获 (例如分级或变形时)
        /// </summary>
        public void TriggerBurst(float duration = DefaultBurstDuration)
        {
            Vessel active = FlightGlobals.ActiveVessel;
            if (active != null && active.parts != null)
            {
                // 找出哪些部件刚从当前飞船脱落 (如助推器、整流罩)，将其纳入动态追踪，展示飞离动画
                foreach (Part p in _lastVesselParts)
                {
                    if (p != null && !active.parts.Contains(p) && p.gameObject != null && p.gameObject.activeInHierarchy)
                    {
                        if (!_detachedParts.Contains(p))
                        {
                            _detachedParts.Add(p);
                        }
                    }
                }

                _lastVesselParts.Clear();
                for (int i = 0; i < active.parts.Count; i++)
                {
                    _lastVesselParts.Add(active.parts[i]);
                }
            }

            _burstRemainingTime = Mathf.Max(_burstRemainingTime, duration);
            _burstAccumulator = _burstInterval; // 保证首帧立即执行一次绘制
        }

        /// <summary>
        /// 立即执行一次正交俯视剪影离屏烘焙
        /// </summary>
        public void BakeNow()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.parts == null || vessel.parts.Count == 0)
            {
                return;
            }

            if (_renderTexture == null || !_renderTexture.IsCreated())
            {
                InitializeRenderPipeline();
            }

            // 1. 获取权威控制基准坐标系 (Control Transform)
            Transform refT = vessel.ReferenceTransform;
            if (refT == null)
            {
                refT = vessel.vesselTransform ?? (vessel.rootPart != null ? vessel.rootPart.transform : null);
            }
            if (refT == null) return;

            Vector3 forward = refT.up;       // 对应机头朝向 (图像正上方 +Y)
            Vector3 right = refT.right;      // 对应机身右侧 (图像正右方 +X)
            Vector3 dorsal = refT.forward;   // 对应座舱顶部背侧 (摄像机向下俯视方向)
            Vector3 origin = vessel.CoM;

            // 2. 收集所有有效可见部件网格并计算视图包围盒
            _cachedMeshFilters.Clear();
            _cachedSkinnedMeshes.Clear();

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            int validCount = 0;

            // 0. 底层显存彻底清屏，杜绝上一帧残影/拖尾 (Ghosting)
            RenderTexture oldRt = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = oldRt;

            for (int i = 0; i < vessel.parts.Count; i++)
            {
                Part p = vessel.parts[i];
                if (p == null || !p.gameObject.activeInHierarchy) continue;

                // 过滤发射架/地面支撑结构 (避免地勤发射塔拉长飞船包围盒)
                if ((p.Modules != null && p.Modules.Contains("LaunchClamp")) || 
                    (p.partInfo != null && p.partInfo.name.IndexOf("launchclamp", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                // 静态网格
                List<MeshFilter> mfs = p.FindModelComponents<MeshFilter>();
                if (mfs != null)
                {
                    for (int m = 0; m < mfs.Count; m++)
                    {
                        MeshFilter mf = mfs[m];
                        if (mf == null || mf.sharedMesh == null) continue;
                        MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                        if (IsExcludedComponent(mr, p)) continue;

                        _cachedMeshFilters.Add(mf);
                        validCount++;

                        ExpandBounds(mr.bounds, origin, right, forward, dorsal,
                            ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
                    }
                }

                // 蒙皮/动画网格 (如展开式太阳能帆板、充气隔热罩等)
                List<SkinnedMeshRenderer> smrs = p.FindModelComponents<SkinnedMeshRenderer>();
                if (smrs != null)
                {
                    for (int s = 0; s < smrs.Count; s++)
                    {
                        SkinnedMeshRenderer smr = smrs[s];
                        if (smr == null || smr.sharedMesh == null) continue;
                        if (IsExcludedComponent(smr, p)) continue;

                        _cachedSkinnedMeshes.Add(smr);
                        validCount++;

                        ExpandBounds(smr.bounds, origin, right, forward, dorsal,
                            ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
                    }
                }
            }

            // 2.5. 收集刚分离的脱落部件网格 (助推器/整流罩)，并在几秒内动态呈现飞离效果
            if (_detachedParts.Count > 0)
            {
                for (int i = _detachedParts.Count - 1; i >= 0; i--)
                {
                    Part dp = _detachedParts[i];
                    if (dp == null || dp.gameObject == null || !dp.gameObject.activeInHierarchy)
                    {
                        _detachedParts.RemoveAt(i);
                        continue;
                    }

                    if (Vector3.Distance(dp.transform.position, origin) > 50f)
                    {
                        _detachedParts.RemoveAt(i);
                        continue;
                    }

                    List<MeshFilter> dmfs = dp.FindModelComponents<MeshFilter>();
                    if (dmfs != null)
                    {
                        for (int m = 0; m < dmfs.Count; m++)
                        {
                            MeshFilter mf = dmfs[m];
                            if (mf == null || mf.sharedMesh == null) continue;
                            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                            if (IsExcludedComponent(mr, dp)) continue;

                            _cachedMeshFilters.Add(mf);
                        }
                    }

                    List<SkinnedMeshRenderer> dsmrs = dp.FindModelComponents<SkinnedMeshRenderer>();
                    if (dsmrs != null)
                    {
                        for (int s = 0; s < dsmrs.Count; s++)
                        {
                            SkinnedMeshRenderer smr = dsmrs[s];
                            if (smr == null || smr.sharedMesh == null) continue;
                            if (IsExcludedComponent(smr, dp)) continue;

                            _cachedSkinnedMeshes.Add(smr);
                        }
                    }
                }
            }

            if (validCount == 0)
            {
                return;
            }

            // 3. 计算自适应居中与尺寸外接缩放
            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;

            float spanX = maxX - minX;
            float spanY = maxY - minY;
            float spanZ = maxZ - minZ;

            float maxSpan = Mathf.Max(spanX, spanY);
            if (maxSpan < 0.2f) maxSpan = 1.0f;

            // 预留 15% 安全边距，防止飞船边缘紧贴边缘
            float viewExtent = maxSpan * 1.15f * 0.5f;
            NormalizedNoseTipY = Mathf.Clamp((maxY - centerY) / viewExtent, -1.0f, 1.0f);
            NormalizedEngineBottomY = Mathf.Clamp((minY - centerY) / viewExtent, -1.0f, 1.0f);

            // 4. 定位与校准离屏摄像机 (平滑视口过渡，消除分离瞬间镜头突然弹跳缩放)
            Vector3 targetCenter = origin + right * centerX + forward * centerY + dorsal * centerZ;
            float targetExtent = viewExtent;

            if (!_hasInitializedBounds || vessel != _lastTrackedVessel)
            {
                _smoothedViewExtent = targetExtent;
                _smoothedWorldCenter = targetCenter;
                _hasInitializedBounds = true;
            }
            else if (_burstRemainingTime > 0f)
            {
                float t = Mathf.Clamp01(Time.unscaledDeltaTime * 4.0f);
                _smoothedViewExtent = Mathf.Lerp(_smoothedViewExtent, targetExtent, t);
                _smoothedWorldCenter = Vector3.Lerp(_smoothedWorldCenter, targetCenter, t);
            }
            else
            {
                _smoothedViewExtent = targetExtent;
                _smoothedWorldCenter = targetCenter;
            }

            float camDistance = Mathf.Max(spanZ * 2f + 15f, 25f);

            _offscreenCamera.transform.position = _smoothedWorldCenter + dorsal * camDistance;
            _offscreenCamera.transform.rotation = Quaternion.LookRotation(-dorsal, forward);
            _offscreenCamera.orthographicSize = _smoothedViewExtent;
            _offscreenCamera.nearClipPlane = 0.5f;
            _offscreenCamera.farClipPlane = camDistance * 2f + spanZ + 50f;

            // 5. 构筑并执行极轻量 CommandBuffer (GPU 直接显存渲染，无任何场景管线开销)
            // 注意：renderIntoTexture 设为 false，确保 DirectX 下输出的纹理与标准 UI RawImage 贴图坐标系完全一致（不产生上下颠倒反转）
            Matrix4x4 viewMatrix = _offscreenCamera.worldToCameraMatrix;
            Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(_offscreenCamera.projectionMatrix, false);

            CommandBuffer cb = new CommandBuffer();
            cb.name = "Vessel_Silhouette_Capture";
            cb.SetRenderTarget(_renderTexture);
            cb.ClearRenderTarget(true, true, Color.clear);
            cb.SetViewProjectionMatrices(viewMatrix, projMatrix);

            for (int i = 0; i < _cachedMeshFilters.Count; i++)
            {
                MeshFilter mf = _cachedMeshFilters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                int subCount = mf.sharedMesh.subMeshCount;
                for (int s = 0; s < subCount; s++)
                {
                    cb.DrawMesh(mf.sharedMesh, mf.transform.localToWorldMatrix, _silhouetteMaterial, s, 0);
                }
            }

            for (int i = 0; i < _cachedSkinnedMeshes.Count; i++)
            {
                SkinnedMeshRenderer smr = _cachedSkinnedMeshes[i];
                if (smr == null || smr.sharedMesh == null) continue;
                int subCount = smr.sharedMesh.subMeshCount;
                for (int s = 0; s < subCount; s++)
                {
                    cb.DrawMesh(smr.sharedMesh, smr.transform.localToWorldMatrix, _silhouetteMaterial, s, 0);
                }
            }

            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();

            OnSilhouetteUpdated?.Invoke(_renderTexture);
        }

        private static void ExpandBounds(Bounds b, Vector3 origin, Vector3 r, Vector3 f, Vector3 d,
            ref float minX, ref float maxX, ref float minY, ref float maxY, ref float minZ, ref float maxZ)
        {
            Vector3 c = b.center;
            Vector3 e = b.extents;

            for (int dx = -1; dx <= 1; dx += 2)
            {
                for (int dy = -1; dy <= 1; dy += 2)
                {
                    for (int dz = -1; dz <= 1; dz += 2)
                    {
                        Vector3 corner = c + new Vector3(dx * e.x, dy * e.y, dz * e.z);
                        Vector3 delta = corner - origin;
                        float vx = Vector3.Dot(delta, r);
                        float vy = Vector3.Dot(delta, f);
                        float vz = Vector3.Dot(delta, d);

                        if (vx < minX) minX = vx;
                        if (vx > maxX) maxX = vx;
                        if (vy < minY) minY = vy;
                        if (vy > maxY) maxY = vy;
                        if (vz < minZ) minZ = vz;
                        if (vz > maxZ) maxZ = vz;
                    }
                }
            }
        }

        private static bool IsExcludedComponent(Renderer r, Part part)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
            {
                return true;
            }

            // 1. 检查物体图层 (过滤 TransparentFX / 粒子图层)
            if (r.gameObject.layer == 1) // 1 = TransparentFX
            {
                return true;
            }

            // 2. 过滤粒子系统、拖尾渲染器与线条
            if (r is TrailRenderer || r is LineRenderer || r.GetType().Name == "ParticleSystemRenderer")
            {
                return true;
            }

            // 3. 检查游戏对象命名与祖先命名 (彻底过滤尾焰、羽流、烟雾、粒子等非机体网格)
            Transform curr = r.transform;
            while (curr != null && curr != part.transform)
            {
                string name = curr.gameObject.name.ToLower();
                if (name.Contains("fx") ||
                    name.Contains("flame") ||
                    name.Contains("exhaust") ||
                    name.Contains("plume") ||
                    name.Contains("smoke") ||
                    name.Contains("trail") ||
                    name.Contains("waterfall") ||
                    name.Contains("particle") ||
                    name.Contains("sparks") ||
                    name.Contains("thrusttransform") ||
                    name.Contains("shockdiamond") ||
                    name.Contains("streamer") ||
                    name.Contains("vapor") ||
                    name.Contains("reentry") ||
                    name.Contains("glow"))
                {
                    return true;
                }
                curr = curr.parent;
            }

            // 4. 检查材质与着色器名称 (防止尾焰贴图网格被绘制为实体)
            Material[] mats = r.sharedMaterials;
            if (mats != null)
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;

                    string matName = m.name.ToLower();
                    if (matName.Contains("flame") ||
                        matName.Contains("exhaust") ||
                        matName.Contains("plume") ||
                        matName.Contains("smoke") ||
                        matName.Contains("waterfall") ||
                        matName.Contains("particle"))
                    {
                        return true;
                    }

                    if (m.shader != null)
                    {
                        string shaderName = m.shader.name.ToLower();
                        if (shaderName.Contains("particle") ||
                            shaderName.Contains("additive") ||
                            shaderName.Contains("waterfall") ||
                            shaderName.Contains("fx"))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        #region KSP 事件回调
        private void OnVesselChange(Vessel v) => TriggerBurst(DefaultBurstDuration);
        private void OnVesselWasModified(Vessel v) => TriggerBurst(DefaultBurstDuration);
        private void OnStageActivate(int stage) => TriggerBurst(DefaultBurstDuration);
        private void OnPartJointBreak(PartJoint pj, float breakForce) => TriggerBurst(DefaultBurstDuration);
        private void OnPartDie(Part p) => TriggerBurst(DefaultBurstDuration);
        private void OnPartUndock(Part p) => TriggerBurst(DefaultBurstDuration);
        private void OnPartCouple(GameEvents.FromToAction<Part, Part> action) => TriggerBurst(DefaultBurstDuration);
        private void OnVesselLoaded(Vessel v) => TriggerBurst(1.5f);
        #endregion

        private void OnDestroy()
        {
            UnregisterEvents();

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_silhouetteMaterial != null)
            {
                Destroy(_silhouetteMaterial);
                _silhouetteMaterial = null;
            }

            if (_offscreenCamera != null)
            {
                Destroy(_offscreenCamera.gameObject);
                _offscreenCamera = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}

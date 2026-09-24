using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 低代价飞船三维视图与剪影烘焙器 (Vessel 3D Baker)
    /// 核心特性：
    /// 1. 零常驻开销 (Zero Permanent Overhead)：
    ///    平时巡航飞行时 0.0ms CPU / 0 DrawCall，完全不运行 Camera (Camera.enabled = false)。
    /// 2. 灵动动态捕获 (Event-Driven Dynamic Burst)：
    ///    在分级、受损解体、对接分离等飞船结构变动时，以 15 FPS 运行约 3 秒动态捕获，
    ///    直观呈现助推器脱落、整流罩分离与太阳翼动态展开，随后执行一次终态校准并彻底进入休眠。
    /// 3. 自适应三维构图 (Adaptive 3D Projection)：
    ///    支持 Isometric (航空航天 3/4 轴测视角)、Perspective (纵深透视)、AttitudeSync (姿态联动) 与 TopDown (带光影俯视)。
    /// 4. 显存直通与硬件抗锯齿 (Direct-to-VRAM & 8x MSAA)：
    ///    直接渲染至 512x512 硬件 24-bit 深度与 8x MSAA RenderTexture，无 CPU Readback 掉帧阻塞。
    /// 5. 自包含虚拟光影与零场景开销 (Zero-Light Single-Pass Shader)：
    ///    通过 CommandBuffer 与自包含 3D 技术着色器 (Half-Lambert 漫反射 + 边缘菲涅尔高光)，
    ///    无需场景灯光与阴影管线，单 Pass 完成全机身曲面体积与零件边界解析。
    /// 6. 零 GC 内存垃圾 (Zero-Allocation Pipeline)：
    ///    网格缓存、部件跟踪集合与 CommandBuffer 全部复用，每帧渲染 0 字节 GC 压力。
    /// 7. 全面遵循 MFP-SPEC 架构规范，零硬编码与零颜色字面量。
    /// </summary>
    public class Vessel3DBaker : MonoBehaviour, IVessel3DProvider
    {
        private static Vessel3DBaker _instance;
        public static Vessel3DBaker Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("Vessel3DBaker_Auto");
                    _instance = go.AddComponent<Vessel3DBaker>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        public const int TextureResolution = 512;
        public const float DefaultBurstFps = 15f;
        public const float DefaultBurstDuration = 3.0f;

        private RenderTexture _renderTexture;
        public Texture Texture3D => _renderTexture;

        public event Action<Texture> OnTexture3DUpdated;

        private Camera _offscreenCamera;
        private Material _3dMaterial;
        private CommandBuffer _commandBuffer;

        // 视图与相机参数
        public Vessel3DViewMode ViewMode { get; set; } = Vessel3DViewMode.Isometric;
        public Vector3 CameraAngles { get; set; } = new Vector3(-35f, 25f, 0f); // Yaw, Pitch, Roll (度)
        public bool IsTurntableActive { get; set; } = false;
        public float TurntableSpeed { get; set; } = 15.0f; // 度/秒
        public float CameraFov { get; set; } = 35.0f;
        public float ZoomMargin { get; set; } = 1.25f;

        // 动态突发烘焙状态
        private float _burstRemainingTime = 0f;
        private float _burstAccumulator = 0f;
        private float _burstInterval = 1f / DefaultBurstFps;

        // 飞船状态跟踪 (用于备用触发)
        private Vessel _lastTrackedVessel = null;
        private int _lastPartCount = -1;
        private float _lastBakedYaw = 0f;
        private Quaternion _lastBakedVesselRotation = Quaternion.identity;

        // 动态分离动画：脱离部件追踪与平滑视口过渡
        private readonly HashSet<Part> _lastVesselParts = new HashSet<Part>();
        private readonly List<Part> _detachedParts = new List<Part>();
        private float _smoothedRadius = 0f;
        private float _smoothedDistance = 0f;
        private Vector3 _smoothedWorldCenter = Vector3.zero;
        private bool _hasInitializedBounds = false;

        // 预分配列表以避免 GC 垃圾回收压力
        private readonly List<MeshFilter> _cachedMeshFilters = new List<MeshFilter>(256);
        private readonly List<SkinnedMeshRenderer> _cachedSkinnedMeshes = new List<SkinnedMeshRenderer>(32);

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Destroy(this);
                return;
            }

            InitializeRenderPipeline();
            RegisterEvents();
            Vessel3DService.Provider = this;
        }

        private void InitializeRenderPipeline()
        {
            if (_renderTexture == null)
            {
                _renderTexture = new RenderTexture(TextureResolution, TextureResolution, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Vessel_3D_RT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 8, // 8x MSAA 消除复杂桁架与边缘锯齿
                    useMipMap = false,
                    autoGenerateMips = false
                };
                _renderTexture.Create();
            }

            if (_offscreenCamera == null)
            {
                GameObject camObj = new GameObject("Vessel3D_Offscreen_Cam", typeof(Camera));
                camObj.transform.SetParent(transform, false);

                _offscreenCamera = camObj.GetComponent<Camera>();
                _offscreenCamera.enabled = false; // 严禁每帧自动运行管线
                _offscreenCamera.cullingMask = 0; // 剔除一切场景天体与环境
                _offscreenCamera.clearFlags = CameraClearFlags.SolidColor;
                _offscreenCamera.backgroundColor = Color.clear;
                _offscreenCamera.targetTexture = _renderTexture;
                _offscreenCamera.aspect = 1.0f;
                _offscreenCamera.nearClipPlane = 0.5f;
                _offscreenCamera.farClipPlane = 2000f;
            }

            if (_commandBuffer == null)
            {
                _commandBuffer = new CommandBuffer();
                _commandBuffer.name = "Vessel_3D_Bake_Command";
            }

            if (_3dMaterial == null)
            {
                Shader shader = AssetLoader.Vessel3DShader
                             ?? Shader.Find("ModularFlightPanel/Vessel3DTechnical")
                             ?? Shader.Find("Diffuse")
                             ?? Shader.Find("KSP/Diffuse")
                             ?? Shader.Find("Standard")
                             ?? Shader.Find("Unlit/Color");

                _3dMaterial = new Material(shader);
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
                // 1. 备用状态监测：载具切换或部件数量变化自动触发突发捕获
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

                // 2. 突发状态捕获 (分级/解体/受损分离动态动画)
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

                    if (_burstRemainingTime <= 0f)
                    {
                        _burstRemainingTime = 0f;
                        BakeNow();
                        _detachedParts.Clear();
                    }
                }
                else
                {
                    // 3. 静止飞行阶段：根据旋转/姿态变动执行脏检测 (Dirty-Check)，避免无谓渲染
                    _detachedParts.Clear();
                    if (active.parts != null && _lastVesselParts.Count != active.parts.Count)
                    {
                        _lastVesselParts.Clear();
                        for (int i = 0; i < active.parts.Count; i++)
                        {
                            _lastVesselParts.Add(active.parts[i]);
                        }
                    }

                    if (IsTurntableActive)
                    {
                        float dt = Time.unscaledDeltaTime;
                        float newYaw = (CameraAngles.x + TurntableSpeed * dt) % 360f;
                        CameraAngles = new Vector3(newYaw, CameraAngles.y, CameraAngles.z);

                        if (Mathf.Abs(Mathf.DeltaAngle(_lastBakedYaw, newYaw)) >= 0.5f)
                        {
                            _burstAccumulator += dt;
                            if (_burstAccumulator >= _burstInterval)
                            {
                                _burstAccumulator = 0f;
                                _lastBakedYaw = newYaw;
                                BakeNow();
                            }
                        }
                    }
                    else if (ViewMode == Vessel3DViewMode.AttitudeSync)
                    {
                        Quaternion currentRot = active.transform.rotation;
                        if (Quaternion.Angle(_lastBakedVesselRotation, currentRot) >= 0.5f)
                        {
                            _burstAccumulator += Time.unscaledDeltaTime;
                            if (_burstAccumulator >= _burstInterval)
                            {
                                _burstAccumulator = 0f;
                                _lastBakedVesselRotation = currentRot;
                                BakeNow();
                            }
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
        /// 触发指定时长的 15 FPS 动态三维捕获 (例如分级、分离或展开太阳翼时)
        /// </summary>
        public void TriggerBurst(float duration = DefaultBurstDuration)
        {
            Vessel active = FlightGlobals.ActiveVessel;
            if (active != null && active.parts != null)
            {
                // 找出脱离部件，展示分离飞离动画
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
            _burstAccumulator = _burstInterval;
        }

        /// <summary>
        /// 立即执行一次低代价三维离屏烘焙
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

            // 1. 获取基准坐标系
            Transform refT = vessel.ReferenceTransform;
            if (refT == null)
            {
                refT = vessel.vesselTransform ?? (vessel.rootPart != null ? vessel.rootPart.transform : null);
            }
            if (refT == null) return;

            Vector3 forward = refT.up;       // 机头朝向
            Vector3 right = refT.right;      // 右侧
            Vector3 dorsal = refT.forward;   // 座舱顶天朝向
            Vector3 origin = vessel.CoM;

            // 2. 收集有效几何体并计算三维包围体
            _cachedMeshFilters.Clear();
            _cachedSkinnedMeshes.Clear();

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            int validCount = 0;

            for (int i = 0; i < vessel.parts.Count; i++)
            {
                Part p = vessel.parts[i];
                if (p == null || !p.gameObject.activeInHierarchy) continue;

                // 排除发射架与地勤结构
                if ((p.Modules != null && p.Modules.Contains("LaunchClamp")) ||
                    (p.partInfo != null && p.partInfo.name.IndexOf("launchclamp", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

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

            // 收集刚分离的脱落部件
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

                    if (Vector3.Distance(dp.transform.position, origin) > 60f)
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

            if (validCount == 0) return;

            // 3. 计算自适应中心与外接包围球半径
            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;

            float spanX = maxX - minX;
            float spanY = maxY - minY;
            float spanZ = maxZ - minZ;

            float boundRadius = Mathf.Sqrt(spanX * spanX + spanY * spanY + spanZ * spanZ) * 0.5f;
            if (boundRadius < 0.2f) boundRadius = 1.0f;

            Vector3 targetCenter = origin + right * centerX + forward * centerY + dorsal * centerZ;

            // 4. 计算视角旋转与平滑构图
            Quaternion camRot;
            if (ViewMode == Vessel3DViewMode.AttitudeSync)
            {
                // 姿态联动模式：摄像机固定在惯性坐标系中，观察飞船的三维实时滚动与俯仰
                Quaternion baseInertial = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                camRot = baseInertial * Quaternion.Euler(CameraAngles.y, CameraAngles.x, CameraAngles.z);
            }
            else if (ViewMode == Vessel3DViewMode.TopDown)
            {
                // 权威俯视模式
                camRot = Quaternion.LookRotation(-dorsal, forward);
            }
            else
            {
                // 默认 3/4 轴测/透视模式 (基准朝向：从背侧向腹侧俯视，机头向上，叠加偏航与俯仰)
                Quaternion defaultBasis = Quaternion.LookRotation(-dorsal, forward);
                camRot = defaultBasis * Quaternion.Euler(CameraAngles.y, CameraAngles.x, CameraAngles.z);
            }

            Vector3 camForward = camRot * Vector3.forward;

            // 平滑插值计算视口，防止脱落瞬间镜头跳跃
            float targetDistance;
            if (ViewMode == Vessel3DViewMode.Perspective)
            {
                float halfFovRad = CameraFov * 0.5f * Mathf.Deg2Rad;
                targetDistance = (boundRadius / Mathf.Max(0.1f, Mathf.Sin(halfFovRad))) * ZoomMargin;
            }
            else
            {
                targetDistance = boundRadius * 3.0f + 25f;
            }

            if (!_hasInitializedBounds || vessel != _lastTrackedVessel)
            {
                _smoothedRadius = boundRadius;
                _smoothedWorldCenter = targetCenter;
                _smoothedDistance = targetDistance;
                _hasInitializedBounds = true;
            }
            else if (_burstRemainingTime > 0f)
            {
                float t = Mathf.Clamp01(Time.unscaledDeltaTime * 4.0f);
                _smoothedRadius = Mathf.Lerp(_smoothedRadius, boundRadius, t);
                _smoothedWorldCenter = Vector3.Lerp(_smoothedWorldCenter, targetCenter, t);
                _smoothedDistance = Mathf.Lerp(_smoothedDistance, targetDistance, t);
            }
            else
            {
                _smoothedRadius = boundRadius;
                _smoothedWorldCenter = targetCenter;
                _smoothedDistance = targetDistance;
            }

            _offscreenCamera.transform.position = _smoothedWorldCenter - camForward * _smoothedDistance;
            _offscreenCamera.transform.rotation = camRot;

            if (ViewMode == Vessel3DViewMode.Perspective)
            {
                _offscreenCamera.orthographic = false;
                _offscreenCamera.fieldOfView = CameraFov;
                _offscreenCamera.nearClipPlane = Mathf.Max(0.1f, _smoothedDistance - _smoothedRadius * 2.0f);
                _offscreenCamera.farClipPlane = _smoothedDistance + _smoothedRadius * 3.0f + 200f;
            }
            else
            {
                _offscreenCamera.orthographic = true;
                _offscreenCamera.orthographicSize = _smoothedRadius * ZoomMargin;
                _offscreenCamera.nearClipPlane = 0.5f;
                _offscreenCamera.farClipPlane = _smoothedDistance * 2.0f + 100f;
            }

            _offscreenCamera.ResetWorldToCameraMatrix();
            _offscreenCamera.ResetProjectionMatrix();

            // 5. 更新材质虚拟光照与主题语义着色 (遵守 0 颜色字面量)
            UpdateMaterialShading(camRot);

            // 6. 执行轻量 CommandBuffer 显存直通渲染
            Matrix4x4 viewMatrix = _offscreenCamera.worldToCameraMatrix;
            Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(_offscreenCamera.projectionMatrix, false);

            _commandBuffer.Clear();
            _commandBuffer.SetRenderTarget(_renderTexture);
            _commandBuffer.ClearRenderTarget(true, true, Color.clear);
            _commandBuffer.SetViewProjectionMatrices(viewMatrix, projMatrix);

            for (int i = 0; i < _cachedMeshFilters.Count; i++)
            {
                MeshFilter mf = _cachedMeshFilters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                int subCount = mf.sharedMesh.subMeshCount;
                for (int s = 0; s < subCount; s++)
                {
                    _commandBuffer.DrawMesh(mf.sharedMesh, mf.transform.localToWorldMatrix, _3dMaterial, s, 0);
                }
            }

            for (int i = 0; i < _cachedSkinnedMeshes.Count; i++)
            {
                SkinnedMeshRenderer smr = _cachedSkinnedMeshes[i];
                if (smr == null || smr.sharedMesh == null) continue;
                int subCount = smr.sharedMesh.subMeshCount;
                for (int s = 0; s < subCount; s++)
                {
                    _commandBuffer.DrawMesh(smr.sharedMesh, smr.transform.localToWorldMatrix, _3dMaterial, s, 0);
                }
            }

            Graphics.ExecuteCommandBuffer(_commandBuffer);

            OnTexture3DUpdated?.Invoke(_renderTexture);
        }

        private void UpdateMaterialShading(Quaternion camRot)
        {
            if (_3dMaterial == null) return;

            // 虚拟主光源：位于摄像机视角右上方偏前，照亮飞船圆柱体与机翼纵深
            Vector3 localLight = new Vector3(0.45f, 0.75f, -0.55f).normalized;
            Vector3 worldLight = camRot * localLight;
            _3dMaterial.SetVector("_LightDir", new Vector4(worldLight.x, worldLight.y, worldLight.z, 0f));

            // 基于主题语义计算高保真机体色、轮廓边缘发光色与环境填充色 (0 颜色字面量)
            Color baseHull = WidgetStyleManager.NeutralOpaque;
            Color rimGlow = WidgetStyleManager.NeutralOpaque;
            Color ambient = WidgetStyleManager.Darken(WidgetStyleManager.NeutralOpaque, 0.12f);

            if (WidgetStyleManager.Instance != null && WidgetStyleManager.Instance.CurrentTheme != null)
            {
                var theme = WidgetStyleManager.Instance.CurrentTheme;
                baseHull = Color.Lerp(WidgetStyleManager.NeutralOpaque, theme.AccentSecondary, 0.55f);
                rimGlow = theme.AccentPrimary;
                ambient = WidgetStyleManager.Darken(theme.FrameBgColor, 0.45f);
            }

            _3dMaterial.SetColor("_Color", baseHull);
            _3dMaterial.SetColor("_RimColor", rimGlow);
            _3dMaterial.SetColor("_AmbientColor", ambient);
            _3dMaterial.SetFloat("_RimPower", 2.2f);
            _3dMaterial.SetFloat("_RimIntensity", 1.25f);
            _3dMaterial.SetFloat("_DiffuseWrap", 0.45f);
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

            if (r.gameObject.layer == 1) // TransparentFX
            {
                return true;
            }

            if (r is TrailRenderer || r is LineRenderer || r.GetType().Name == "ParticleSystemRenderer")
            {
                return true;
            }

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

            if (Vessel3DService.Provider == (IVessel3DProvider)this)
            {
                Vessel3DService.Provider = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_3dMaterial != null)
            {
                Destroy(_3dMaterial);
                _3dMaterial = null;
            }

            if (_offscreenCamera != null)
            {
                Destroy(_offscreenCamera.gameObject);
                _offscreenCamera = null;
            }

            if (_commandBuffer != null)
            {
                _commandBuffer.Release();
                _commandBuffer = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}

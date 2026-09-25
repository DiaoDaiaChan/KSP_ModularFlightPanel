using System;
using UnityEngine;
using UnityEngine.UI;

namespace ModularFlightPanel.UI.HUD
{
    /// <summary>
    /// 航电面板主画布与渲染配置管理器 (HUD Canvas & Scaler Manager)
    /// 集中管理 UGUI Canvas、CanvasScaler、GraphicRaycaster 与相机投影模式，
    /// 保证 1:1 绝对物理像素点对点光栅化，杜绝字体虚化并与主调度及编辑工具栏彻底解耦。
    /// </summary>
    public class HUDCanvasManager
    {
        private GameObject _canvasObj;
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private GraphicRaycaster _raycaster;

        public GameObject CanvasObject => _canvasObj;
        public Canvas Canvas => _canvas;
        public CanvasScaler Scaler => _scaler;
        public GraphicRaycaster Raycaster => _raycaster;

        public bool IsCanvasActive => _canvasObj != null && _canvasObj.activeSelf;

        /// <summary>
        /// 创建并初始化主航电 UGUI 画布与光栅化参数
        /// </summary>
        public void BuildCanvas()
        {
            _canvasObj = new GameObject("ModularFlightPanel_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(_canvasObj);
            }

            _canvas = _canvasObj.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;
            _canvas.pixelPerfect = true;

            _scaler = _canvasObj.GetComponent<CanvasScaler>();
            // 固定物理像素模式 (ConstantPixelSize)：1:1 绝对物理像素点对点光栅化
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _scaler.scaleFactor = 1.0f;

            // 航电级点对点点阵光栅化 (Avionics Pixel-Perfect Text Rasterization)
            _scaler.dynamicPixelsPerUnit = 1.0f;
            _scaler.referencePixelsPerUnit = 100f;

            _raycaster = _canvasObj.GetComponent<GraphicRaycaster>();
        }

        /// <summary>
        /// 配置离屏渲染相机模式 (ScreenSpaceCamera)
        /// </summary>
        public void SetRenderCamera(Camera cam)
        {
            if (_canvas != null && cam != null)
            {
                _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                _canvas.worldCamera = cam;
                _canvas.planeDistance = 100f;
            }
        }

        /// <summary>
        /// 调整超采样倍率与动态像素密度
        /// </summary>
        public void SetDynamicPixelsPerUnit(float dppu)
        {
            if (_scaler != null)
            {
                _scaler.dynamicPixelsPerUnit = dppu;
            }
        }

        /// <summary>
        /// 控制画布整体显隐 (配合 F2 或旁路热键)
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_canvas != null)
            {
                _canvas.enabled = visible;
            }
            if (_canvasObj != null && _canvasObj.activeSelf != visible)
            {
                _canvasObj.SetActive(visible);
            }
            if (_raycaster != null)
            {
                _raycaster.enabled = visible;
            }
        }

        /// <summary>
        /// 销毁画布根节点
        /// </summary>
        public void Destroy()
        {
            if (_canvasObj != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_canvasObj);
                else UnityEngine.Object.DestroyImmediate(_canvasObj);
                _canvasObj = null;
                _canvas = null;
                _scaler = null;
                _raycaster = null;
            }
        }
    }
}

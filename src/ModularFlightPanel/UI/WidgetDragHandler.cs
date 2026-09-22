using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 挂载在任意组件根物体上的通用自由拖拽与吸附交互组件
    /// </summary>
    public class WidgetDragHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler
    {
        public static bool IsEditModeActive { get; set; } = false;

        private RectTransform _rectTransform;
        private Canvas _canvas;
        private BaseFlightWidget _ownerWidget;

        private GameObject _editOverlay;
        private Outline _editOutline;
        private Text _editTitleText;

        public void Initialize(BaseFlightWidget owner, Canvas canvas)
        {
            _ownerWidget = owner;
            _canvas = canvas;
            _rectTransform = GetComponent<RectTransform>();

            CreateEditVisuals();
            UpdateEditVisuals();
        }

        private void CreateEditVisuals()
        {
            // 创建编辑模式专用的高亮提示边框与标题把手
            _editOverlay = new GameObject("EditOverlay", typeof(RectTransform), typeof(Image));
            _editOverlay.transform.SetParent(transform, false);

            RectTransform rt = _editOverlay.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            Image img = _editOverlay.GetComponent<Image>();
            img.color = new Color(0f, 0.8f, 1f, 0.08f); // 微弱半透明填充便以拾取
            img.raycastTarget = true;

            bool isNavball = _ownerWidget is Widgets.NavballSphereWidget;
            if (isNavball)
            {
                // 姿态球专用：圆形半透明遮罩拾取 + 圆形高亮描边环
                img.sprite = Core.NavballMarkerFactory.GetCircleMaskSprite();

                GameObject ringObj = new GameObject("CircleEditRing", typeof(RectTransform), typeof(Image));
                ringObj.transform.SetParent(_editOverlay.transform, false);
                RectTransform ringRt = ringObj.GetComponent<RectTransform>();
                ringRt.anchorMin = Vector2.zero;
                ringRt.anchorMax = Vector2.one;
                ringRt.sizeDelta = Vector2.zero;
                ringRt.anchoredPosition = Vector2.zero;

                Image ringImg = ringObj.GetComponent<Image>();
                ringImg.sprite = Core.NavballMarkerFactory.GetCircleRingSprite();
                ringImg.color = new Color(0f, 1f, 0.8f, 0.9f);
                ringImg.raycastTarget = false;
            }
            else
            {
                _editOutline = _editOverlay.AddComponent<Outline>();
                _editOutline.effectColor = new Color(0f, 1f, 0.8f, 0.9f);
                _editOutline.effectDistance = new Vector2(2f, 2f);
            }

            _editTitleText = UIFactory.CreateText(_editOverlay.transform, "Title", $"[拖拽] {_ownerWidget.DisplayName}", 11, TextAnchor.MiddleCenter, Color.yellow);
            RectTransform trt = _editTitleText.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 1f);
            trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.sizeDelta = new Vector2(160f, 20f);
            trt.anchoredPosition = new Vector2(0f, 4f);
        }

        private void Update()
        {
            // 实时同步编辑视觉可见性
            UpdateEditVisuals();
        }

        private void UpdateEditVisuals()
        {
            if (_editOverlay != null)
            {
                if (_editOverlay.activeSelf != IsEditModeActive)
                {
                    _editOverlay.SetActive(IsEditModeActive);
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsEditModeActive) return;
            // 拖拽激活时置于顶层
            transform.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsEditModeActive || _rectTransform == null || _canvas == null) return;

            // 根据 Canvas 缩放比例精准位移
            _rectTransform.anchoredPosition += eventData.delta / _canvas.scaleFactor;

            // 10 像素网格对齐
            float snapX = Mathf.Round(_rectTransform.anchoredPosition.x / 10f) * 10f;
            float snapY = Mathf.Round(_rectTransform.anchoredPosition.y / 10f) * 10f;
            _rectTransform.anchoredPosition = new Vector2(snapX, snapY);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!IsEditModeActive) return;

            // 保存最新坐标到配置对象
            if (_ownerWidget?.Config != null)
            {
                _ownerWidget.Config.PositionX = _rectTransform.anchoredPosition.x;
                _ownerWidget.Config.PositionY = _rectTransform.anchoredPosition.y;
                Debug.Log($"[ModularFlightPanel] Saved widget '{_ownerWidget.DisplayName}' position: ({_ownerWidget.Config.PositionX}, {_ownerWidget.Config.PositionY})");
            }
        }
    }
}

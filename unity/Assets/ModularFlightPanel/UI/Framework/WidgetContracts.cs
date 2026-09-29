using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Diagnostics;
using ModularFlightPanel.UI.Settings;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 轻量全局 Toast 提示桥接器。
    /// 允许纯 UGUI 交互组件（如历史记录、对齐辅助线、网格等）与游戏内 IMGUI 提示层解耦通信，
    /// 避免在无头 Unity 预览工程中产生对 Settings/* 的硬依赖。
    /// </summary>
    public static class MFPToastBridge
    {
        public static Action<string> OnShowToast;

        public static void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            OnShowToast?.Invoke(message);
        }
    }
}

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 声明式自适应物理尺寸契约接口 (Adaptive Physical Sizing Contract)
    /// 用于解决小组件在非等比拉伸 (ScaleX / ScaleY) 时单纯使用 localScale 仿射拉伸导致文字与矢量畸变模糊的问题。
    /// 实现本接口的组件由基类直接将 ScaleX/ScaleY 映射为物理 sizeDelta 像素尺寸，保持 localScale 为 1:1 原生无畸变，
    /// 并触发 OnAdaptiveResize 回调执行视口、刻度池与排版布局的原生点对点重绘。
    /// </summary>
    public interface IAdaptiveSizeWidget
    {
        /// <summary>
        /// 是否允许非等比拉伸 (若返回 false 则 Gizmo 自动隐藏 4 边中点拉伸手柄，仅允许 4 角等比缩放，防止姿态球等被拉成椭圆)
        /// </summary>
        bool AllowNonUniformScale { get; }

        /// <summary>
        /// 最小物理基准尺寸 (防止拉伸过小发生布局崩溃)
        /// </summary>
        Vector2 MinBaseSize { get; }

        /// <summary>
        /// 最大物理基准尺寸 (防止拉伸超出安全视口)
        /// </summary>
        Vector2 MaxBaseSize { get; }

        /// <summary>
        /// 当组件物理像素尺寸发生改变时触发，通知组件自适应调整视口、导轨与子控件
        /// </summary>
        /// <param name="pixelSize">经过 DPI 与非等比系数换算后的实际 sizeDelta 物理像素尺寸</param>
        void OnAdaptiveResize(Vector2 pixelSize);
    }

    /// <summary>
    /// 动态数据槽位元数据描述符 (Dynamic Data Slot Descriptor)
    /// 统一抽象多列/多行横幅与数据卡片的单元格定义
    /// </summary>
    public class DynamicSlotDescriptor
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Token { get; set; }
        public bool HasSeparator { get; set; }
        public float CustomWidth { get; set; }
        public string SlotType { get; set; }

        public DynamicSlotDescriptor() { }

        public DynamicSlotDescriptor(string id, string title, string token, bool hasSeparator = true, float customWidth = 0f, string slotType = "readout")
        {
            Id = id;
            Title = title;
            Token = token;
            HasSeparator = hasSeparator;
            CustomWidth = customWidth;
            SlotType = slotType;
        }
    }

    /// <summary>
    /// 声明式动态增删行列/槽位组件契约接口 (Dynamic Slot & Column Flow Contract)
    /// 用于支持用户在编辑模式自由增删数据列、调整顺序、开关分隔线与自定义装配 736+ 参数的组件。
    /// 解耦具体组件实现，避免单一组件上帝类化，使顶栏、底栏、侧边仪表柱等均可复用统一的槽位编排协议。
    /// </summary>
    public interface IDynamicSlotWidget
    {
        /// <summary>
        /// 槽位编排器描述标题 (如 "动态槽位横幅模式: 可自由加减数据列、调整顺序与分隔线")
        /// </summary>
        string SlotOrchestratorTitle { get; }

        /// <summary>
        /// 当前激活的动态槽位只读列表
        /// </summary>
        IReadOnlyList<DynamicSlotDescriptor> DynamicSlots { get; }

        /// <summary>
        /// 添加新数据槽位/列
        /// </summary>
        void AddDynamicSlot(string token, string title = null);

        /// <summary>
        /// 移除指定索引的槽位/列
        /// </summary>
        void RemoveDynamicSlot(int index);

        /// <summary>
        /// 调整槽位顺序
        /// </summary>
        void MoveDynamicSlot(int fromIndex, int toIndex);

        /// <summary>
        /// 切换指定槽位的分隔线显隐
        /// </summary>
        void ToggleDynamicSlotSeparator(int index);

        /// <summary>
        /// 更新指定槽位绑定的遥测通配符 Token
        /// </summary>
        void UpdateDynamicSlotToken(int index, string newToken);

        /// <summary>
        /// 更新指定槽位的显示标题
        /// </summary>
        void UpdateDynamicSlotTitle(int index, string newTitle);

        /// <summary>
        /// 恢复出厂默认槽位布局
        /// </summary>
        void ResetToDefaultDynamicSlots();
    }

    /// <summary>
    /// 全局 IMGUI 安全网关与防漏锁沙箱 (Safe IMGUI Gateway & Input Lock Guard)
    /// 核心设计目标：
    /// 1. 统一拦截所有 IMGUI 根入口 (OnGUI, GUILayout.Window, Floating Toolbar) 的未捕获异常；
    /// 2. 发生异常时，底层无条件安全释放 MFP 占用的全部键盘与鼠标控制锁，绝不造成玩家操控失灵；
    /// 3. 限流日志记录，防止每帧重复打印 60 次导致游戏卡顿；
    /// 4. 异常自动隔离展示：当窗口内部发生渲染异常时，在窗口内呈现优雅的自愈错误卡片，而不是打垮 Unity GUI 引擎。
    /// </summary>
    public static class SafeGUIGateway
    {
        private static long _lastLogTick = 0;
        private static string _lastErrorMessage = null;

        /// <summary>
        /// 执行顶层 OnGUI 根沙箱
        /// </summary>
        public static void ExecuteRoot(Action onGuiAction, string contextName = "RootGUI")
        {
            if (onGuiAction == null) return;

            try
            {
                onGuiAction();
            }
            catch (Exception ex)
            {
                HandleGuiException(contextName, ex);
                MFPInputLock.ReleaseAllLocks();
            }
        }

        /// <summary>
        /// 执行带有窗口状态保护的 Window 沙箱
        /// </summary>
        public static void ExecuteWindowContent(int windowId, Action contentAction, Action onRecover = null, string windowName = "Window")
        {
            if (contentAction == null) return;

            try
            {
                contentAction();
            }
            catch (Exception ex)
            {
                HandleGuiException(windowName, ex);

                // 呈现窗口内自愈卡片，保障窗口仍然可操作、可关闭、可重置
                GUILayout.BeginVertical();
                MFPGuiSkin.BeginCard();
                GUILayout.Label($"<color=#FF5555><b>{I18n.Tr("ERR_GUI_RENDER_ERROR", "⚠️ 界面绘制遇到异常 (GUI Render Error)")}</b></color>");
                GUILayout.Label($"<color=#CCCCCC><size=11>{ex.GetType().Name}: {ex.Message}</size></color>");
                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                if (onRecover != null && GUILayout.Button(I18n.Tr("ERR_BTN_RESET_GUI", "🔄 尝试重置界面"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    try { onRecover(); } catch { }
                }
                if (GUILayout.Button(I18n.Tr("ERR_BTN_RELEASE_LOCKS", "✖ 释放控制锁"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(120f), GUILayout.Height(24f)))
                {
                    MFPInputLock.ReleaseAllLocks();
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndCard();
                GUILayout.EndVertical();
            }
        }

        private static void HandleGuiException(string contextName, Exception ex)
        {
            long now = DateTime.UtcNow.Ticks;
            // 节流：同类错误 2 秒内仅记录一次，彻底防止 60FPS 刷屏卡死
            if (now - _lastLogTick > TimeSpan.FromSeconds(2).Ticks || _lastErrorMessage != ex.Message)
            {
                _lastLogTick = now;
                _lastErrorMessage = ex.Message;
                MFPLogger.Error(MFPLogger.CatUI, $"[SafeGUIGateway] IMGUI 异常捕获 ({contextName}): {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}

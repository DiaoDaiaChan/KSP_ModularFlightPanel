using System;
using UnityEngine;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// KSP 游戏输入穿透阻断与控制锁中枢 (Input Lock Shielding Manager)
    /// 彻底解决 IMGUI 面板下点击穿透（相机乱转、误点零件、误分级）以及打字时热键泄露（如按 M 进地图、按 X 熄火、按空格分级）的经典顽疾。
    /// </summary>
    public static class MFPInputLock
    {
        private const string WindowLockId = "MFP_WORKBENCH_WINDOW_LOCK";
        private const string KeyboardLockId = "MFP_WORKBENCH_KEYBOARD_LOCK";

        private static bool _isWindowLockActive = false;
        private static bool _isKeyboardLockActive = false;

        /// <summary>
        /// 当鼠标光标进入设置工作台或屏幕悬浮编辑工具栏时，锁定可能引发误触的飞行控制与相机控制
        /// </summary>
        public static void SetWindowHoverLock(bool shouldLock)
        {
#if KSP_RUNTIME
            try
            {
                if (shouldLock && !_isWindowLockActive)
                {
                    ControlTypes mask = ControlTypes.CAMERACONTROLS 
                                      | ControlTypes.THROTTLE 
                                      | ControlTypes.STAGING 
                                      | ControlTypes.CUSTOM_ACTION_GROUPS 
                                      | ControlTypes.ALL_SHIP_CONTROLS 
                                      | ControlTypes.GROUPS_ALL
                                      | ControlTypes.QUICKSAVE;

                    InputLockManager.SetControlLock(mask, WindowLockId);
                    _isWindowLockActive = true;
                }
                else if (!shouldLock && _isWindowLockActive)
                {
                    InputLockManager.RemoveControlLock(WindowLockId);
                    _isWindowLockActive = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] SetWindowHoverLock error: {ex.Message}");
            }
#else
            _isWindowLockActive = shouldLock;
#endif
        }

        /// <summary>
        /// 当用户聚焦文本框（如搜索遥测、修改组件名称、编辑卡片模板）时，锁定全键盘输入以防止误触 KSP 全局热键
        /// </summary>
        public static void SetKeyboardFocusLock(bool shouldLock)
        {
#if KSP_RUNTIME
            try
            {
                if (shouldLock && !_isKeyboardLockActive)
                {
                    InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT, KeyboardLockId);
                    _isKeyboardLockActive = true;
                }
                else if (!shouldLock && _isKeyboardLockActive)
                {
                    InputLockManager.RemoveControlLock(KeyboardLockId);
                    _isKeyboardLockActive = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] SetKeyboardFocusLock error: {ex.Message}");
            }
#else
            _isKeyboardLockActive = shouldLock;
#endif
        }

        /// <summary>
        /// 彻底释放所有 MFP 占用的控制锁（在窗口关闭、场景切换或异常时调用）
        /// </summary>
        public static void ReleaseAllLocks()
        {
#if KSP_RUNTIME
            try
            {
                InputLockManager.RemoveControlLock(WindowLockId);
                InputLockManager.RemoveControlLock(KeyboardLockId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] ReleaseAllLocks error: {ex.Message}");
            }
            finally
            {
                _isWindowLockActive = false;
                _isKeyboardLockActive = false;
            }
#else
            _isWindowLockActive = false;
            _isKeyboardLockActive = false;
#endif
        }
    }
}

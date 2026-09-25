using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ModularFlightPanel.HeadlessValidator
{
    public static class I18nMissingKeysMerger
    {
        private static readonly Dictionary<string, (string cn, string en)> MissingKeyMap = new()
        {
            // ==========================================
            // 1. 错误提示与校验信息 (Error & Validation)
            // ==========================================
            { "ERR_INPUT_EMPTY", ("输入内容为空", "Input content is empty") },
            { "ERR_FILE_NO_WIDGETS", ("文件「{0}」存在但未包含有效组件配置", "File \"{0}\" exists but contains no valid widget configuration") },
            { "ERR_READ_CONFIG_FAIL", ("读取配置文件失败: {0}", "Failed to read configuration file: {0}") },
            { "ERR_JSON_NO_WIDGETS", ("JSON 解析成功，但未发现有效小组件配置 (Widgets 列表为空)", "JSON parsed successfully, but no valid widget configuration found (Widgets list is empty)") },
            { "ERR_JSON_PARSE_FAIL", ("JSON 解析失败: {0}", "JSON parse error: {0}") },
            { "ERR_INVALID_CONFIG_FORMAT", ("无效的配置格式 (支持 MFP:v1: 分享码、原始 JSON 文本或本地 .json 文件名)", "Invalid configuration format (supports MFP:v1: share codes, raw JSON, or local .json filenames)") },
            { "ERR_PARSE_NO_WIDGETS", ("解析成功但未发现有效小组件配置", "Parsed successfully but no valid widget configuration found") },
            { "ERR_DECODE_FAIL", ("解码失败: {0}", "Decoding failed: {0}") },
            { "ERR_PRESET_NAME_EMPTY", ("预设名称不能为空", "Preset name cannot be empty") },
            { "ERR_LAYOUT_EMPTY_INTERCEPTED", ("当前小组件布局为空，已自动拦截保存以保护现有模板", "Current widget layout is empty; save intercepted to protect existing template") },
            { "ERR_THEME_CODE_EMPTY", ("主题分享码为空", "Theme share code is empty") },
            { "ERR_THEME_CODE_INVALID", ("无效的主题分享码格式 (必须以 MFP-THEME:v1: 开头)", "Invalid theme share code format (must start with MFP-THEME:v1:)") },
            { "ERR_THEME_NO_ID", ("解析成功但未发现有效主题 ID", "Parsed successfully but no valid Theme ID found") },
            { "ERR_THEME_DECODE_FAIL", ("解码失败: {0}", "Theme decoding failed: {0}") },

            // ==========================================
            // 2. 预设名称与描述 (Presets)
            // ==========================================
            { "PRESET_NAME_DEFAULT", ("双翼标准航电", "Default Dual-Wing Avionics") },
            { "PRESET_DESC_DEFAULT", ("经典双翼工效学布局，中央姿态球与伴生仪表，两侧速度/高度标尺带与大动压/G力表", "Classic dual-wing ergonomic layout: central navball with companion gauges, flanked by airspeed/altitude tapes and dynamic pressure/G-load dials.") },
            { "PRESET_NAME_MODERN", ("现代全玻璃化座舱", "Modern Glass Cockpit") },
            { "PRESET_DESC_MODERN", ("高信息密度玻璃化中控，包含 ELEC 电力分布图与 ROCKET 2D 多级推进栈", "High-density glass cockpit suite featuring ELEC power distribution matrix and ROCKET 2D multistage propulsion stack.") },
            { "PRESET_NAME_APOLLO", ("阿波罗复古登月", "Apollo Retro Lunar Landing") },
            { "PRESET_DESC_APOLLO", ("聚焦登月降落推重比、雷达真高、阶段燃料余量与姿控网格", "Lunar landing mission panel focusing on descent TWR, radar altitude, stage propellant margins, and RCS attitude grid.") },
            { "PRESET_NAME_DEEP_SPACE", ("深空无人远征探测", "Deep Space Robotic Expedition") },
            { "PRESET_DESC_DEEP_SPACE", ("深空探测器专用：太阳能净充电监测、CommNet 长波天线阵列与轨道力学参数", "Deep space robotic probe suite: solar net charging telemetry, CommNet long-range array status, and orbital dynamics readouts.") },
            { "PRESET_NAME_SPACEX", ("SpaceX 载人龙飞船", "SpaceX Crew Dragon Glass") },
            { "PRESET_DESC_SPACEX", ("极简全息触控座舱：顶部全景遥测横幅、中央对接与姿态准星 HUD、侧边 ECLSS 维生面板与底部药丸触控条", "Minimalist holographic touch cockpit: top panoramic telemetry header, central docking & attitude HUD reticle, lateral ECLSS life-support panel, and bottom pill control bar.") },
            { "PRESET_NAME_STARSHIP", ("SpaceX 星舰抬头显示", "SpaceX Starship HUD") },
            { "PRESET_DESC_STARSHIP", ("极简 Starship HUD：弧形过载指示带、对接准星与全景姿态", "Minimalist Starship HUD: arched G-meter tape, docking crosshair, and panoramic attitude.") },
            { "PRESET_DESC_LOCAL", ("本地预设文件 ({0})", "Local Preset File ({0})") },

            // ==========================================
            // 3. 主题名称 (Themes)
            // ==========================================
            { "THEME_MODERN_GLASS", ("现代极简全息玻璃座舱", "Modern Glass Cockpit") },
            { "THEME_CYBER_NEON", ("赛博霓虹点阵", "Cyber Neon") },
            { "THEME_SPACEX_DRAGON", ("SpaceX 龙飞船全息", "SpaceX Crew Dragon") },
            { "THEME_CLASSIC_AERO", ("经典航空蓝棕", "Classic Aero") },
            { "THEME_APOLLO_1969", ("阿波罗 1969 AGC 复古", "Apollo 1969 AGC") },
            { "THEME_APOLLO_DSKY", ("阿波罗 DSKY 荧光绿点阵", "Apollo DSKY Phosphor Green") },
            { "THEME_F16_DIFFRACTIVE_HUD", ("F-16 衍射全息冰蓝 HUD", "F-16 Diffractive HUD") },
            { "THEME_VINTAGE_AMBER_CRT", ("复古等离子琥珀金 CRT", "Vintage Amber CRT") },
            { "THEME_CYBER_MATRIX", ("赛博黑客矩阵", "Cyber Matrix") },
            { "THEME_STARSHIP_MARS", ("星舰火星开拓者", "Starship Mars Pioneer") },
            { "THEME_DEEP_SPACE_VOYAGER", ("深空旅行者金黄", "Deep Space Voyager") },
            { "THEME_SR71_BLACKBIRD", ("SR-71 黑鸟战术暗红", "SR-71 Blackbird Tactical Red") },
            { "THEME_VOSTOK_1961", ("东方一号苏联机械青", "Vostok 1961 Soviet Teal") },
            { "THEME_MATRIX_TERMINAL", ("矩阵终端绿点阵", "Matrix Terminal Green") },
            { "THEME_EVA_UNIT01", ("EVA 初号机暴走电光点阵", "EVA Unit-01 Berserk Neon") },

            // ==========================================
            // 4. 基础配置与文件状态 (Config & File Status)
            // ==========================================
            { "WIDGET_DEFAULT_NAME", ("未命名组件", "Unnamed Widget") },
            { "CFG_FILE_NOT_FOUND", ("不存在", "Not Found") },
            { "CFG_FILE_NO_BACKUP", ("无备份", "No Backup") },
            { "CFG_LAYOUT_NOT_EXISTS_MEM", ("不存在 (使用内存默认)", "Not Found (Using In-Memory Default)") },
            { "CFG_FILE_SIZE_MODIFIED_FMT", ("大小: {0:F1} KB | 修改: {1}", "Size: {0:F1} KB | Modified: {1}") },
            { "CFG_LAYOUT_NO_BACKUP_COPY", ("无备份副本", "No Backup Snapshot") },
            { "CFG_DUPLICATE_SUFFIX", (" (副本)", " (Copy)") },

            // ==========================================
            // 5. 性能分析器 (Avionics Profiler)
            // ==========================================
            { "PROF_WINDOW_TITLE", ("MFP 航电性能探针", "MFP Avionics Performance Profiler") },
            { "PROF_TIME_COST", ("MFP 耗时:", "MFP Frame Time:") },
            { "PROF_BTN_RESUME", ("▶ 恢复 MFP", "▶ Resume MFP") },
            { "PROF_BTN_BYPASS", ("⏸ 完全旁路 (F11)", "⏸ Complete Bypass (F11)") },
            { "PROF_BTN_COLLAPSE", ("▲ 收起", "▲ Collapse") },
            { "PROF_BTN_DETAILS", ("▼ 详情", "▼ Details") },
            { "PROF_ROW_TELEMETRY", ("遥测核心:", "Telemetry Hub Core:") },
            { "PROF_ROW_PROBES", ("外部探针 (FAR/RA/MJ):", "External Probes (FAR/RA/MJ):") },
            { "PROF_ROW_WIDGETS", ("组件管线呈现:", "Widget Pipeline Update:") },
            { "PROF_ROW_SILHOUETTE", ("飞船剪影烘焙:", "Vessel Silhouette Baker:") },
            { "PROF_ROW_HOOKS", ("原版界面挂钩:", "Stock KSP Hooks:") },

            // ==========================================
            // 6. 画布网格、编辑历史与变换手柄 (Canvas, History, Gizmo)
            // ==========================================
            { "GRID_AXIS_X0", ("⌖ X = 0 对称轴", "⌖ X = 0 Symmetry Axis") },
            { "GRID_BLUEPRINT_ON", ("▦ 蓝图辅助网格: [开启]", "▦ Blueprint Grid: [ON]") },
            { "GRID_BLUEPRINT_OFF", ("▦ 蓝图辅助网格: [关闭]", "▦ Blueprint Grid: [OFF]") },
            { "DRAG_TITLE_FMT", ("[拖拽] {0}", "[Dragging] {0}") },
            { "DRAG_TOAST_INSPECT_FMT", ("🛠️ 正在装配台检视: {0}", "🛠️ Inspecting in Assembler: {0}") },
            { "DRAG_HIST_MOVE_FMT", ("移动 {0}", "Move {0}") },
            { "HIST_TOAST_UNDO_FMT", ("↶ 撤销: {0}", "↶ Undo: {0}") },
            { "HIST_TOAST_REDO_FMT", ("↷ 重做: {0}", "↷ Redo: {0}") },
            { "HIST_ALIGN_LEFT", ("左对齐", "Align Left") },
            { "HIST_ALIGN_HCENTER", ("水平居中", "Align Horizontal Center") },
            { "HIST_ALIGN_RIGHT", ("右对齐", "Align Right") },
            { "HIST_ALIGN_TOP", ("顶对齐", "Align Top") },
            { "HIST_ALIGN_VCENTER", ("垂直居中", "Align Vertical Center") },
            { "HIST_ALIGN_BOTTOM", ("底对齐", "Align Bottom") },
            { "HIST_DISTRIBUTE_H", ("水平等距分布", "Distribute Horizontally") },
            { "HIST_DISTRIBUTE_V", ("垂直等距分布", "Distribute Vertically") },
            { "HIST_ALIGN_AXIS_X0", ("对齐至机体对称轴(X=0)", "Align to Vessel Symmetry Axis (X=0)") },
            { "HIST_KEY_NUDGE", ("键盘微调位移", "Keyboard Nudge") },
            { "HIST_BRING_FRONT", ("置于顶层", "Bring to Front") },
            { "TOAST_BRING_FRONT", ("⤒ 已置于顶层", "⤒ Brought to Front") },
            { "HIST_SEND_BACK", ("置于底层", "Send to Back") },
            { "TOAST_SEND_BACK", ("⤓ 已置于底层", "⤓ Sent to Back") },
            { "HIST_HIDE_WIDGETS_FMT", ("隐藏 {0} 个组件", "Hide {0} Widgets") },
            { "TOAST_HIDDEN_CTRLZ", ("已隐藏选中的组件 (Ctrl+Z 可撤销)", "Selected widgets hidden (Ctrl+Z to undo)") },
            { "HIST_RESET_ROT", ("复位旋转至0°", "Reset Rotation to 0°") },
            { "HIST_RESET_SCALE", ("复位缩放至1.0x", "Reset Scale to 1.0x") },
            { "TOAST_SELECT_ALL", ("已全选所有小组件", "All widgets selected") },
            { "HIST_SCROLL_SCALE", ("滚轮缩放", "Scroll Wheel Scale") },
            { "HIST_SCROLL_ROT", ("滚轮旋转", "Scroll Wheel Rotate") },
            { "GIZMO_GROUP_COUNT_FMT", ("多选群组 ({0} 项)", "Multi-selection Group ({0} items)") },
            { "GIZMO_ACTION_ROTATE", ("手柄旋转", "Gizmo Rotate") },
            { "GIZMO_ACTION_SCALE", ("手柄缩放", "Gizmo Scale") },
            { "WIDGET_FALLBACK_NAME", ("组件", "Widget") },
            { "GUI_SEARCH_PLACEHOLDER", ("搜索...", "Search...") },

            // ==========================================
            // 7. 装配台中枢词条 (Assembler Tab)
            // ==========================================
            { "ASM_NO_WIDGETS", ("当前没有任何组件，请在「航电库」中先添加组件。", "No widgets mounted yet. Please add widgets from the Library tab first.") },
            { "ASM_HEADER_NAV", ("组件导航", "Widget Navigator") },
            { "ASM_TOTAL_COUNT", ("共 {0} 项", "{0} total") },
            { "ASM_NO_MATCH", ("未搜索到匹配项", "No matching widgets found") },
            { "ASM_TAG_TAPE", ("PFD 滚动标尺带", "PFD Tape Gauge") },
            { "ASM_TAG_ECAM", ("ECAM 圆弧仪表", "ECAM Dial Gauge") },
            { "ASM_TAG_SPACEX", ("SpaceX 航电组件", "SpaceX Avionics") },
            { "ASM_TAG_CARD", ("遥测卡片", "Telemetry Card") },
            { "ASM_TAG_CORE", ("原生核心组件", "Core Avionics") },
            { "ASM_STATUS_RUNNING", ("● 运行显示", "● Running") },
            { "ASM_STATUS_SUSPENDED", ("○ 挂起隐藏", "○ Suspended") },
            { "ASM_POS_X", ("坐标 X (px):", "Pos X (px):") },
            { "ASM_POS_Y", ("坐标 Y (px):", "Pos Y (px):") },
            { "ASM_QUICK_ALIGN", ("快速对齐:", "Quick Align:") },
            { "ASM_SCALE_LABEL", ("缩放比例: <b>{0:F2}x</b>", "Scale: <b>{0:F2}x</b>") },
            { "ASM_ROTATION_LABEL", ("旋转角: <b>{0:F0}°</b>", "Rotation: <b>{0:F0}°</b>") },
            { "ASM_CARD_CALIBRATION", ("📊 仪表数据驱动与量程标定", "📊 Data Drive & Scale Calibration") },
            { "ASM_TOKEN_UNBOUND", ("<未绑定>", "<Unbound>") },
            { "ASM_TOKEN_UNBIND", ("解绑数据源", "Unbind Telemetry") },
            { "ASM_TAPE_STEP", ("标尺步长:", "Tape Step:") },
            { "ASM_CARD_TEMPLATE", ("📝 遥测监控卡片模板", "📝 Telemetry Monitor Card Template") },
            { "ASM_TPL_DELIM", ("+ ' | ' 分隔符", "+ ' | ' Delimiter") },
            { "ASM_TPL_NEWLINE", ("+ 换行 \\n", "+ Newline \\n") },
            { "ASM_TPL_CLEAR", ("清空模板", "Clear Template") },
            { "ASM_TPL_EMPTY", ("<空模板>", "<Empty Template>") },
            { "ASM_LIVE_PREVIEW", ("<b>🌟 航电真实遥测实时解算预览:</b>", "<b>🌟 Live Avionics Telemetry Preview:</b>") },
            { "ASM_CARD_CORE_TITLE", ("⚙️ 核心内建飞行仪表组件", "⚙️ Built-in Core Avionics Widgets") },
            { "ASM_CORE_WIDGET_ID", ("• 组件标识: ", "• Widget ID: ") },
            { "ASM_CORE_STATUS", ("• 运行状态: ", "• State: ") },
            { "ASM_CORE_RUNNING", ("● 正在运行", "● Active") },
            { "ASM_CORE_SUSPENDED", ("○ 已挂起隐藏", "○ Suspended") },
            { "ASM_CORE_DESC", ("核心组件包含底层管线逻辑 (如 3D 姿态球、滑动罗盘、SAS底座、工具栏坞)，位置与缩放可在上方直接调整或在屏幕拖拽。", "Core widgets host underlying pipelines (3D Navball, SAS Compass, Docking Toolbar). Transform and scale can be adjusted above or dragged directly on screen.") },
            { "ASM_CATALOG_MATCHED", ("匹配 {0} 项", "{0} matched") },
            { "ASM_CATALOG_NO_PARAM", ("未找到匹配的参数", "No matching parameters found") },
            { "ASM_PAGE_PREV", ("◀ 上页", "◀ Prev") },
            { "ASM_PAGE_INDICATOR", ("第 {0} / {1} 页", "Page {0} / {1}") },
            { "ASM_PAGE_NEXT", ("下页 ▶", "Next ▶") },
            { "ASM_BTN_BOUND", ("✔ 已绑定", "✔ Bound") },
            { "ASM_BTN_BIND", ("⚡ 绑定至此表盘", "⚡ Bind to Gauge") },
            { "ASM_TOAST_BOUND", ("已绑定「{0}」至仪表！", "Bound \"{0}\" to gauge!") },
            { "ASM_BTN_INSERT_TPL", ("+ 插入模板", "+ Insert Template") },
            { "ASM_TOAST_INSERTED", ("已插入「{0}」到模板！", "Inserted \"{0}\" into template!") },
            { "ASM_BTN_COPY", ("📋 复制", "📋 Copy") },
            { "ASM_TOAST_COPIED", ("已复制 {0}", "Copied {0}") },
            { "ASM_LIVE_READOUT", ("当前实时读数: ", "Live Readout: ") },
            { "ASM_CARD_PERF", ("⚡ 绘制性能单独调优", "⚡ Drawing Performance Tuning") },
            { "ASM_CANVAS_ENABLED", ("已开启画布隔离", "Canvas Isolation: ENABLED") },
            { "ASM_CANVAS_DISABLED", ("已关闭画布隔离", "Canvas Isolation: DISABLED") },
            { "ASM_PERF_REFRESH_MODE", ("刷新分频模式:", "Refresh Tier Mode:") },
            { "ASM_PERF_60HZ", ("60Hz 满血", "60Hz Full") },
            { "ASM_PERF_20HZ", ("20Hz 标称", "20Hz Nominal") },
            { "ASM_PERF_10HZ", ("10Hz 舒缓", "10Hz Relaxed") },
            { "ASM_PERF_2HZ", ("2Hz 节能", "2Hz Eco") },
            { "ASM_TPL_SYNTAX_ERR", ("[语法错误]: {0}", "[Syntax Error]: {0}") },
            { "ASM_HZ_60_PLUS", ("60Hz+ (每帧)", "60Hz+ (Every Frame)") },
            { "ASM_TOAST_HZ_60", ("已设为 60Hz 满帧刷新", "Set to 60Hz Full Frame Rate") },
            { "ASM_TOAST_HZ_20", ("已设为 20Hz", "Set to 20Hz Refresh Rate") },
            { "ASM_TOAST_HZ_10", ("已设为 10Hz", "Set to 10Hz Refresh Rate") },
            { "ASM_TOAST_HZ_2", ("已设为 2Hz 节能", "Set to 2Hz Eco Mode") },

            // ==========================================
            // 8. 航电库组件条目与描述 (Library Items & Descriptions)
            // ==========================================
            { "LIB_SOFT_LIMIT", ("软上限爆表", "Soft Limit") },
            { "LIB_HARD_LIMIT", ("硬限幅", "Hard Clamp") },
            { "LIB_ADD_TO_PANEL", ("+ 添加到面板", "+ Add to Panel") },
            { "LIB_LEFT_TAPE", ("左侧标尺", "Left Tape") },
            { "LIB_RIGHT_TAPE", ("右侧标尺", "Right Tape") },
            { "LIB_DYNAMIC_CARD", ("动态卡片", "Dynamic Card") },
            { "LIB_RUNNING_HIDE", ("● 运行中 (点击隐藏)", "● Running (Click to Hide)") },
            { "LIB_TOAST_HIDDEN", ("已隐藏「{0}」！", "Hidden \"{0}\"!") },
            { "LIB_ENABLE_CORE", ("+ 开启此核心组件", "+ Enable Core Widget") },
            { "LIB_ENABLE_SUBSYS", ("+ 开启此子系统", "+ Enable Subsystem") },
            { "LIB_TOAST_SPAWNED", ("✔ 已生成并聚焦「{0}」！", "✔ Spawned and focused on \"{0}\"!") },
            { "LIB_ITEM_ECAM_SPD", ("ECAM 圆弧通用仪表", "ECAM Dial Generic Gauge") },
            { "LIB_DESC_ECAM_SPD", ("🛠️ 270° 马蹄形高对比度圆弧表盘，支持动态指针、数显与软上限爆表模式。可在装配台绑定任意遥测通配符。", "🛠️ 270° horseshoe high-contrast dial gauge featuring needle animation, digital readout, and soft/hard limiter modes. Freely bind any telemetry token in the Assembler.") },
            { "LIB_ITEM_TAPE_SPD", ("PFD 垂直动态标尺带 (左侧/速度)", "PFD Vertical Tape Gauge (Left / Speed)") },
            { "LIB_DESC_TAPE_SPD", ("🛠️ PFD 风格平滑滚动动态标尺带（左侧布局），支持任意物理数据与步长。", "🛠️ PFD-style smooth vertical rolling tape gauge (left layout), supporting arbitrary physical data bindings and tick step calibration.") },
            { "LIB_ITEM_TAPE_ALT", ("PFD 垂直动态标尺带 (右侧/高度)", "PFD Vertical Tape Gauge (Right / Altitude)") },
            { "LIB_DESC_TAPE_ALT", ("🛠️ PFD 风格平滑滚动动态标尺带（右侧布局），支持真高/海高自由标定。", "🛠️ PFD-style smooth vertical rolling tape gauge (right layout), supporting ASL/AGL radar calibration.") },
            { "LIB_ITEM_CARD_MULTI", ("多通道遥测卡片", "Multi-Channel Telemetry Card") },
            { "LIB_DESC_CARD_MULTI", ("📝 多参数高对比度技术卡片，可在装配台内自由编写任意遥测通配符模板（如 {Q:F2}、{MACH}、{TWR} 等）。", "📝 Multi-channel high-contrast technical card. Freely compose arbitrary telemetry wildcard templates (e.g. {Q:F2}, {MACH}, {TWR}) in the Assembler.") },
            { "LIB_ITEM_CORE_NAVBALL", ("3D 姿态球", "3D Modular Navball Core") },
            { "LIB_DESC_CORE_NAVBALL", ("🌐 现代超清矢量/贴图 3D 姿态球核心，支持无极缩放、姿态导引十字与全量机动矢量。", "🌐 Modern crisp vector/texture 3D navball core supporting stepless scaling, flight director crosshair, and full orbital maneuver vectors.") },
            { "LIB_ITEM_VESSEL_NAVBALL", ("3D 飞船球形姿态仪", "Vessel 3D Navball Instrument") },
            { "LIB_DESC_VESSEL_NAVBALL", ("🚀 全新球形姿态仪：以真实 3D 飞船为中心，外层环绕 3D 姿态球体、人工地平标尺、SAS 目标飞行指引仪与全量导航矢量。", "🚀 Spherical attitude instrument centered on real-time 3D vessel model, surrounded by attitude sphere, artificial horizon ladder, SAS flight director, and navigation vectors.") },
            { "LIB_ITEM_HEADING_ARC", ("PFD 航向指示标尺弧", "PFD Heading Indicator Arc") },
            { "LIB_DESC_HEADING_ARC", ("🧭 主飞行仪表（PFD）顶部平滑滚动机体罗盘弧，带航向数显与度数刻度。", "🧭 Primary Flight Display (PFD) smooth rolling heading tape arc with numerical readout and degree tick ladder.") },
            { "LIB_ITEM_ND_NAV", ("AERO ND 综合水平态势导航屏", "AERO Navigation Display (ND)") },
            { "LIB_DESC_ND_NAV", ("🧭 飞机航电综合水平态势显示器 (ND)，包含罗盘弧、测距环、飞机微标与航点航路。", "🧭 Aircraft avionics Navigation Display (ND) featuring compass arc, range rings, aircraft symbol, and waypoint flightplan.") },
            { "LIB_ITEM_MANEUVER", ("MANEUVER 轨道机动节点指示器", "MANEUVER Node Burn Indicator") },
            { "LIB_DESC_MANEUVER", ("🎯 实时机动节点指示器：剩余 Delta-V 进度条、节点倒计时、燃烧时长与一键推演。", "🎯 Live maneuver node indicator: remaining Delta-V progress bar, node countdown, burn duration, and instant prediction.") },
            { "LIB_ITEM_MANEUVER_TL", ("MANEUVER 轨道机动时序与三轴矢量轴", "MANEUVER Timeline & Tri-Axis Vectors") },
            { "LIB_DESC_MANEUVER_TL", ("⏱️ 横排时间轴形式机动节点指示器：点火窗口时序轨、T0 节点与 Prograde/Normal/Radial 三轴矢量分解。", "⏱️ Horizontal timeline maneuver indicator: ignition window track, T0 node point, and Prograde/Normal/Radial vector decomposition.") },
            { "LIB_ITEM_ORBITAL_INFO", ("ORBITAL 轨道动力学面板", "ORBITAL Mechanics Telemetry Panel") },
            { "LIB_DESC_ORBITAL_INFO", ("🌐 轨道力学四项精简读数面板：远地点 (AP)、近地点 (PE)、到达时间与轨道偏心率。", "🌐 Orbital mechanics concise telemetry panel: Apoapsis (AP), Periapsis (PE), Time-to-Apoapsis/Periapsis, and Eccentricity.") },
            { "LIB_ITEM_B747_EICAS", ("B747 EICAS 主发动机与机组告警显示", "B747 Primary Engine EICAS Display") },
            { "LIB_DESC_B747_EICAS", ("✈️ 经典波音 747 四发主发动机 CRT：EPR/N1/EGT 四发柱状表、数字框显、TAT/推力模式与起落架状态。", "✈️ Classic Boeing 747 quad-engine primary CRT: EPR/N1/EGT engine bar gauges, digital readouts, TAT/thrust rating, and gear status.") },
            { "LIB_ITEM_B747_LOWER", ("B747 下部辅助发动机 EICAS", "B747 Secondary Auxiliary EICAS") },
            { "LIB_DESC_B747_LOWER", ("✈️ 经典波音 747 四发下部系统 CRT：N2/N3 转速表条、燃油流量 FF、滑油压力/温度双轴游标表与震动监控。", "✈️ Classic Boeing 747 quad-engine lower CRT: N2/N3 tachometer bars, Fuel Flow (FF), oil pressure/temperature dual bugs, and vibration monitor.") },
            { "LIB_ITEM_STAGE_SEQ", ("STAGE 垂直分级时序序列仪", "STAGE Vertical Staging Sequence Widget") },
            { "LIB_DESC_STAGE_SEQ", ("🚀 垂直火箭分级序列仪：逐级剩余 ΔV、燃烧时间、推重比与单级推进剂微量程，重构原版左侧分级。", "🚀 Vertical rocket staging sequencer: per-stage remaining ΔV, burn time, TWR, and propellant gauge micro-bars, modernizing stock staging stack.") },
            { "LIB_ITEM_ROCKET_2D", ("ROCKET 2D 垂直推进栈姿态卡", "ROCKET 2D Propulsion Stack Card") },
            { "LIB_DESC_ROCKET_2D", ("🚀 多级火箭垂直推进栈、推进剂实时耗尽进度条、发动机工况与本级 dV。", "🚀 Multistage rocket vertical propulsion stack with live propellant consumption gauges, engine status indicators, and stage dV.") },
            { "LIB_ITEM_ELECTRICAL", ("ELEC 电力分配与电网系统", "ELEC Power Distribution System") },
            { "LIB_DESC_ELECTRICAL", ("⚡ 蓄电池电压、DC ESS 总线负荷、太阳能帆板与即时净充放电率 (EC/s)。", "⚡ Battery voltage, DC ESS bus load, solar panel generation, and instantaneous net charge/discharge rate (EC/s).") },
            { "LIB_ITEM_LIFE_SUPPORT", ("LIFE SUPPORT 维生消耗品监控", "LIFE SUPPORT Telemetry Monitor") },
            { "LIB_DESC_LIFE_SUPPORT", ("🌱 乘员居住舱压环境、氧气/电力/RCS/维生消耗品 2x2 进度仪表。", "🌱 Crew habitat cabin pressure, oxygen, electric charge, RCS, and life-support consumables 2x2 matrix gauges.") },
            { "LIB_ITEM_COMMNET", ("COMMNET 天线通信网络", "COMMNET Antenna Network Status") },
            { "LIB_DESC_COMMNET", ("📡 原版 CommNet 连接状态、控制权级别、天线阵列规格与 5 格信号计量柱。", "📡 Stock CommNet link status, control level, antenna array specs, and 5-bar signal strength meter.") },
            { "LIB_ITEM_PERF_MON", ("SYS PERF 航电性能探针监控屏", "SYS PERF Avionics Profiler Monitor") },
            { "LIB_DESC_PERF_MON", ("⚡ 实时监控 MFP 遥测、外部探针、组件渲染耗时与帧率 FPS，支持一键主干旁路。", "⚡ Real-time monitor for MFP telemetry, external probes, widget rendering overhead, and FPS, with 1-click full bypass.") },
            { "LIB_ITEM_MASTER_WARN", ("中央主告警光字牌", "Central Master Warning Annunciator") },
            { "LIB_DESC_MASTER_WARN", ("🚨 双等级航电警告光字牌：黄色注意与红色危急双通道轮播，支持拉起、失速、低油、低电、缺氧全量监测，点击可消警。", "🚨 Dual-tier avionics annunciator: yellow Caution and red Warning dual-channel rotation with pull-up, stall, low-fuel, low-power, and hypoxia alerts; clickable to acknowledge.") },
            { "LIB_ITEM_SPACEX_HDR", ("SpaceX 任务遥测顶栏", "SpaceX Mission Telemetry Header") },
            { "LIB_DESC_SPACEX_HDR", ("🐉 SpaceX 顶部贯通式航电状态栏：主动飞行阶段胶囊徽章、倒计时与 5 组高对比度轨道数显列。", "🐉 SpaceX full-width top avionics status bar: active flight phase badge, mission timer, and 5 high-contrast orbital telemetry columns.") },
            { "LIB_ITEM_SPACEX_DOCK", ("SpaceX 空间站对接与姿态准星", "SpaceX Docking & Attitude Reticle") },
            { "LIB_DESC_SPACEX_DOCK", ("🎯 SpaceX ISS 空间站对接瞄准器：同心双环准星、3 轴姿态偏差角与角速度、测距接近率与 RCS 点亮。", "🎯 SpaceX ISS docking reticle: concentric dual-ring boresight, 3-axis attitude deviation & rate, range closing rate, and RCS firing indicators.") },
            { "LIB_ITEM_SPACEX_VIEW", ("SpaceX 综合工况与 ECLSS 面板", "SpaceX Vehicle Overview & ECLSS Panel") },
            { "LIB_DESC_SPACEX_VIEW", ("🌱 飞船综合工况与维生监控：客舱压力、氧分压、客舱温度、电网功率与气闸/推进剂/热控状态。", "🌱 Vehicle overview & ECLSS monitor: cabin pressure, ppO2, cabin temp, bus power, airlock, propellant, and thermal status.") },
            { "LIB_ITEM_SPACEX_BTM", ("SpaceX 底部控制与链路操作栏", "SpaceX Bottom Controls & Link Pill Bar") },
            { "LIB_DESC_SPACEX_BTM", ("🎮 SpaceX 底部药丸触控条：RCS/SAS/参考系/精细控制开关、指向模式与通信链路矩阵。", "🎮 SpaceX bottom touch pill bar: RCS/SAS/frame/fine control switches, attitude hold modes, and comm link matrix.") },
            { "LIB_ITEM_SPACEX_TL", ("SpaceX 飞行关键时序甘特轴", "SpaceX Flight Events Timeline Gantt") },
            { "LIB_DESC_SPACEX_TL", ("⏱️ 横排甘特式任务阶段进度标尺：MECO、分级、入轨、对接窗口各节点动态光标推进。", "⏱️ Horizontal Gantt mission timeline bar: dynamic cursor progression across MECO, staging, orbital insertion, and docking windows.") },
            { "LIB_ITEM_UI_MGR", ("UI 航电控制中枢", "UI Avionics Manager HUD Widget") },
            { "LIB_DESC_UI_MGR", ("❖ 原生挂载在飞行屏幕上的 UGUI 高度集成管理仪表：实时组件列表、快速分类、一键显隐与自由拖拽联动。", "❖ Flight HUD integrated UGUI manager widget: live widget roster, quick categories, 1-click visibility, and drag-and-drop orchestration.") },
            { "LIB_ITEM_SAS_DIAL", ("环形 SAS 模式选择罗盘", "Circular SAS Mode Selector Compass") },
            { "LIB_DESC_SAS_DIAL", ("🧭 10 向全功能快速 SAS 模式选择罗盘，带飞船实时滚转与级间剪影。", "🧭 10-way full-featured rapid SAS mode selector dial with real-time vessel roll and stage silhouette.") },
            { "LIB_ITEM_TOOLBAR", ("AVIONICS 现代折叠工具栏收纳坞", "AVIONICS Modern Folding Toolbar Dock") },
            { "LIB_DESC_TOOLBAR", ("📦 接管原版 20+ MOD 图标的超现代黑晶抽屉坞，彻底消灭屏幕长龙。", "📦 Ultramodern obsidian folding dock hosting 20+ stock and mod toolbar buttons, eliminating screen clutter.") },
            { "LIB_ITEM_STAGE_CTRL", ("操纵量指示与分级锁控制台", "Control Deflection & Staging Lock Console") },
            { "LIB_DESC_STAGE_CTRL", ("🎮 Pitch/Roll/Yaw 实时舵量标尺与分级安全锁定 (Alt+L) 防误触操作台。", "🎮 Real-time Pitch/Roll/Yaw deflection indicators with staging safety lock (Alt+L) guard console.") },
            { "LIB_ITEM_TIMEWARP", ("平滑时间加速控制器", "Smooth Time Warp Controller") },
            { "LIB_DESC_TIMEWARP", ("⏩ 物理/轨道时间加速等级指示器与一键平滑倍率切换条。", "⏩ Physics and orbital warp level indicator with 1-click smooth rate selector bar.") },
            { "LIB_ITEM_BTM_CTRL", ("底部快捷操纵条", "Bottom Quick Controls Pill Bar") },
            { "LIB_DESC_BTM_CTRL", ("⚙ RCS/SAS/刹车/起落架/车灯综合药丸式状态切换条。", "⚙ Integrated quick pill bar for RCS, SAS, Brakes, Gear, and Lights toggles.") },
            { "LIB_RANGE_FMT", (" (量程: {0:F0}~{1:F0}{2})", " (Range: {0:F0}~{1:F0}{2})") },
            { "LIB_TEMPLATE_LABEL", ("模板: {0}", "Template: {0}") },

            // ==========================================
            // 9. 预设、载具与主题调色板 (Profiles & Palette)
            // ==========================================
            { "UI_LANG_TIP_TO_EN", ("点击切换至英文", "Click to switch to English") },
            { "UI_LANG_TIP_TO_ZH", ("点击切换至中文", "Click to switch to Chinese") },
            { "UI_LANG_ZH", ("🇨🇳 中文", "🇨🇳 Chinese") },
            { "UI_LANG_EN", ("🇺🇸 EN", "🇺🇸 EN") },
            { "UI_TOAST_LANG_EN", ("已切换为英文", "Language switched to English") },
            { "UI_TOAST_LANG_ZH", ("已切换为简体中文", "Language switched to Simplified Chinese") },
            { "THM_LANG_AUTO_DETECT", ("🔄 自动检测语言", "🔄 Auto Detect Language") },
            { "PRF_HEADER_LANG", ("🌐 语言与国际化偏好", "🌐 Language & Localization Preference") },
            { "PRF_SUBHEADER_LANG", ("实时切换航电工作台与全量飞行仪表的显示语言", "Realtime display language switching for avionics workbench and all flight widgets") },
            { "PRF_TOAST_LANG_CHANGED", ("✔ 语言已成功切换！", "✔ Language successfully changed!") },
            { "PRF_LANG_AUTO_DETECT", ("🔄 自动检测语言", "🔄 Auto Detect Language") },
            { "PRF_TOAST_LANG_DETECTED", ("✔ 已自动设定为检测到的语言: {0}", "✔ Automatically configured to detected language: {0}") },
            { "PRF_MAIN_CONFIG", ("主配置文件:", "Main Configuration:") },
            { "PRF_MOUNTED_BADGE", ("已挂载 {0}/{1} 组件", "{0}/{1} Widgets Mounted") },
            { "PRF_BACKUP_STATUS", ("• 自动备份状态:", "• Auto Backup State:") },
            { "PRF_PRESET_SCALES", ("推荐快捷尺寸:", "Preset Scales:") },
            { "PRF_SCALE_100", ("1.0x (原生紧凑)", "1.0x (Native Compact)") },
            { "PRF_SCALE_125", ("1.25x (推荐清晰★)", "1.25x (Recommended Crisp ★)") },
            { "PRF_SCALE_150", ("1.5x (视网膜大字)", "1.5x (Retina Large)") },
            { "PRF_SCALE_175", ("1.75x (大屏)", "1.75x (Large Display)") },
            { "PRF_SCALE_200", ("2.0x (巨幕)", "2.0x (Ultra Wide)") },
            { "PRF_NOT_IN_FLIGHT", ("未处于飞行状态", "Not In Flight") },
            { "COMMON_NONE", ("无", "None") },
            { "PRF_VESSEL_ISOLATED_PATH", ("独立配置文件: PluginData/Vessels/{0}.json", "Dedicated Profile: PluginData/Vessels/{0}.json") },
            { "PRF_VESSEL_USE_MAIN_DESC", ("当前使用主配置 layout.json，点击下方按钮可为此飞船脱钩保存独立配置。", "Currently using master layout.json. Click below to unbind and save a dedicated cockpit layout for this vessel.") },
            { "PRF_TOAST_SAVED", ("✔ 布局已成功保存至磁盘 (含自动备份副本 layout.backup.json)！", "✔ Layout successfully saved to disk (with backup layout.backup.json)!") },
            { "PRF_TOAST_RELOADED", ("✔ 已成功从磁盘热重载 layout.json！", "✔ Layout successfully hot-reloaded from disk!") },
            { "PRF_TOAST_RELOAD_FAIL", ("<color=#FF4444>重载失败: 磁盘文件不存在或损坏</color>", "<color=#FF4444>Reload Failed: File missing or corrupt</color>") },
            { "PRF_TOAST_BACKUP_OK", ("✔ 已成功生成独立备份副本 layout.backup.json！", "✔ Backup snapshot created (layout.backup.json)!") },
            { "PRF_TOAST_BACKUP_FAIL", ("<color=#FF4444>创建备份失败</color>", "<color=#FF4444>Failed to create backup</color>") },
            { "PRF_TOAST_RESTORE_OK", ("✔ 已成功从备份文件恢复并刷新界面！", "✔ Restored layout from backup snapshot!") },
            { "PRF_TOAST_RESTORE_FAIL", ("<color=#FF4444>未找到有效的备份文件 layout.backup.json</color>", "<color=#FF4444>No valid backup file found</color>") },
            { "PRF_TOAST_RESET_OK", ("已恢复出厂默认布局配置！", "Factory default layout restored!") },
            { "PRF_SUBHEADER_PER_VESSEL", ("支持为不同飞船保存专属座舱，切船自动响应", "Save dedicated cockpits per vessel, auto-switched on vessel change") },
            { "PRF_CURRENT_VESSEL", ("当前载具名称:", "Current Vessel:") },
            { "PRF_VESSEL_GROUND", ("地面整备 / 航天中心", "Space Center / Ground Station") },
            { "PRF_VESSEL_BOUND", ("已绑定载具专属配置", "Dedicated Vessel Layout Bound") },
            { "PRF_VESSEL_DEFAULT", ("使用通用主配置", "Using Default Master Layout") },
            { "PRF_BTN_SAVE_VESSEL", ("📌 为当前载具保存独立布局", "📌 Save Layout for Current Vessel") },
            { "PRF_TOAST_VESSEL_SAVED", ("✔ 已为「{0}」生成专属独立配置！切船将自动载入。", "✔ Dedicated layout saved for \"{0}\"!") },
            { "PRF_BTN_RELOAD_VESSEL", ("🔄 重新载入载具专属配置", "🔄 Reload Dedicated Vessel Layout") },
            { "PRF_TOAST_VESSEL_LOADED", ("✔ 已成功加载「{0}」专属配置！", "✔ Loaded dedicated layout for \"{0}\"!") },
            { "PRF_BTN_DELETE_VESSEL", ("🗑 解绑并恢复通用配置", "🗑 Unbind & Revert to Master") },
            { "PRF_TOAST_VESSEL_RESET", ("已解除「{0}」独立配置，恢复使用通用主布局！", "Unbound dedicated layout for \"{0}\", reverted to master!") },
            { "PRF_TOAST_SAVE_VESSEL_FAIL", ("<color=#FF4444>保存载具专属配置失败</color>", "<color=#FF4444>Failed to save dedicated vessel layout</color>") },
            { "PRF_TOAST_NO_VESSEL_PROFILE", ("<color=#FF4444>该载具尚无独立配置，已维持通用布局</color>", "<color=#FF4444>No dedicated profile found for this vessel, using master layout</color>") },
            { "PRF_SUBHEADER_PRESETS", ("一键套用调校好的工效学座舱与本地模板", "1-Click apply calibrated ergonomic cockpits and local presets") },
            { "PRF_TAG_BUILTIN", ("[出厂预置]", "[Built-in]") },
            { "PRF_TAG_LOCAL", ("[本地模板]", "[Local Preset]") },
            { "PRF_BTN_APPLY_PRESET", ("⚡ 一键套用此预设", "⚡ Apply Preset") },
            { "PRF_TOAST_PRESET_APPLIED", ("✔ 已成功套用预设「{0}」！(共 {1} 个组件)", "✔ Applied preset \"{0}\"! ({1} widgets)") },
            { "PRF_TOAST_APPLY_PRESET_FAIL", ("<color=#FF4444>套用失败: 预设「{0}」为空或格式损坏，已自动拦截保护！</color>", "<color=#FF4444>Apply failed: Preset \"{0}\" is empty or corrupt; intercepted for safety!</color>") },
            { "PRF_LABEL_SAVE_PRESET", ("另存新预设:", "Save As Preset:") },
            { "PRF_BTN_SAVE_PRESET", ("💾 保存到本地 Presets 文件夹", "💾 Save to Presets Folder") },
            { "PRF_TOAST_PRESET_SAVED", ("✔ 已保存预设「{0}」至 Presets 目录！", "✔ Preset \"{0}\" saved to Presets folder!") },
            { "PRF_TOAST_SAVE_PRESET_FAIL", ("<color=#FF4444>保存失败: {0}</color>", "<color=#FF4444>Save failed: {0}</color>") },
            { "PRF_SUBHEADER_SHARE", ("支持 MFP:v1: 分享码、原始 JSON 文本或本地预设文件名", "Supports MFP:v1: share codes, raw JSON, or local preset filenames") },
            { "PRF_BTN_COPY_SHARE", ("📋 复制当前布局分享码到剪贴板", "📋 Copy Share Code to Clipboard") },
            { "PRF_TOAST_SHARE_COPIED", ("✔ 已成功复制分享码至剪贴板！可直接粘贴发送给社区好友 (Ctrl+V)", "✔ Share code copied to clipboard! (Ctrl+V to paste)") },
            { "PRF_LABEL_SHARE_INPUT", ("配置代码 / 路径:", "Code / Path:") },
            { "PRF_BTN_PASTE_CLIPBOARD", ("粘贴剪贴板", "Paste Clipboard") },
            { "PRF_BTN_IMPORT_APPLY", ("📥 导入并套用", "📥 Import & Apply") },
            { "PRF_TOAST_IMPORT_OK", ("✔ 成功导入并套用布局！(共加载 {0} 个组件)", "✔ Layout imported and applied! ({0} widgets loaded)") },
            { "PRF_TOAST_IMPORT_FAIL", ("<color=#FF4444>导入失败: {0}</color>", "<color=#FF4444>Import failed: {0}</color>") },
            { "PRF_SHARE_HINT", ("• 多合一智能导入：支持粘贴「MFP:v1:」分享码、原始 JSON 文本结构、或本地 Presets 文件名 (如 01_Default_Avionics.json)。", "• Multi-format Import: Paste \"MFP:v1:\" share code, raw JSON, or preset filename (e.g. 01_Default_Avionics.json).") },
            { "PRF_HEADER_THEME_CFG", ("🎨 主题配置与调色板", "🎨 Theme Settings & Palette") },
            { "PRF_SUBHEADER_THEME_CFG", ("管理 theme_settings.json 存储与色彩微调", "Manage theme_settings.json storage and color calibration") },
            { "PRF_LABEL_THEME_FILE", ("主题配置文件:", "Theme File:") },
            { "PRF_THEME_FILE_NOT_CREATED", ("文件未创建 (使用内置默认)", "File not created (Using built-in defaults)") },
            { "PRF_BTN_SAVE_THEME", ("💾 保存主题设置到磁盘", "💾 Save Theme Settings") },
            { "PRF_TOAST_THEME_SAVED", ("✔ 主题偏好设置已成功保存至 theme_settings.json！", "✔ Theme settings saved to theme_settings.json!") },
            { "PRF_THEME_WORKSHOP", ("自定义调色板微调工坊", "Theme Palette Workshop") },
            { "THM_COLOR_ACCENT_PRI", ("主强调色", "Accent Primary") },
            { "THM_COLOR_ACCENT_SEC", ("副强调色", "Accent Secondary") },
            { "THM_COLOR_FRAME_BG", ("面板底板色", "Frame Background") },
            { "THM_COLOR_FRAME_BORDER", ("面板边框色", "Frame Border") },
            { "THM_COLOR_TEXT_PRI", ("主读数文字色", "Text Primary") },
            { "PRF_BTN_APPLY_PALETTE", ("应用调色板并保存", "Apply & Save Palette") },
            { "PRF_TOAST_PALETTE_SAVED", ("✔ 主题颜色微调已保存并应用！", "✔ Palette adjustments applied and saved!") },

            // ==========================================
            // 10. 遥测仿真器 (Simulation Engine)
            // ==========================================
            { "SIM_BTN_ACTIVE", ("▶ [仿真模式已激活] 正在喂送高保真物理仿真流", "▶ [Simulation Active] Feeding high-fidelity physics stream") },
            { "SIM_BTN_INACTIVE", ("▶ [开启遥测仿真模式] 原地测试全量动态仪表", "▶ [Enable Telemetry Simulation] In-situ testing of all dynamic widgets") },
            { "SIM_SCENARIO_MAXQ", ("💥 Max-Q 极限\n  <size=10>Q=34kPa | 3.5G</size>", "💥 Max-Q Limit\n  <size=10>Q=34kPa | 3.5G</size>") },
            { "SIM_SCENARIO_MECO", ("🔄 关机分级\n  <size=10>下级点火 | 级间切分</size>", "🔄 MECO & Staging\n  <size=10>Ignition | Interstage</size>") },
            { "SIM_SCENARIO_POWER", ("🌑 暗面断电\n  <size=10>无光照 | 电压跌破</size>", "🌑 Darkside Blackout\n  <size=10>Eclipse | Bus Drop</size>") },
            { "SIM_HEADER_TIMELINE", ("⏱ 连续物理推演时序控制器", "⏱ Physics Timeline Player") },
            { "SIM_BTN_PAUSE", ("❚❚ 暂停推演", "❚❚ Pause") },
            { "SIM_BTN_PLAY", ("▶ 继续播放", "▶ Play") },
            { "SIM_LABEL_PROGRESS", ("推演进度:", "Progress:") },
            { "SIM_LABEL_SPEED", ("倍速:", "Speed:") },
            { "SIM_HEADER_MONITOR", ("📊 仿真物理参量即时监视器", "📊 Live Telemetry Monitor") },
            { "SIM_MON_SPD", ("地表速度:", "Surface Speed:") },
            { "SIM_MON_ALT", ("显示高度:", "Altitude:") },
            { "SIM_MON_VSPD", ("垂直速度:", "Vertical Speed:") },
            { "SIM_MON_Q", ("大气动压:", "Dynamic Pressure:") },
            { "SIM_MON_G", ("过载 G力:", "G-Force:") },
            { "SIM_MON_TWR", ("推重比:", "TWR:") },
            { "SIM_MON_EC", ("蓄电量:", "Electric Charge:") },
            { "SIM_MON_VOLT", ("母线电压:", "Bus Voltage:") },
            { "SIM_MON_EC_RATE", ("净电荷率:", "Net Charge Rate:") },
            { "SIM_MON_PROP", ("推进剂余量:", "Propellant Level:") },
            { "SIM_MON_COMM", ("通信信号:", "Comm Signal:") },
            { "SIM_MON_CREW", ("乘员数:", "Crew Count:") },
            { "SIM_INITIALIZING_HUB", ("<color=#7088A8>正在初始化 TelemetryHub...</color>", "<color=#7088A8>Initializing TelemetryHub...</color>") },

            // ==========================================
            // 11. 主题与视网膜超采样设置 (Theme & Navball Resolution)
            // ==========================================
            { "THM_SPEC_NOT_RUN", ("未运行 (含源码级规范审计)", "Not Executed (Includes Source Specification Audit)") },
            { "THM_MODULAR_NAVBALL", ("自定义 3D 姿态球:", "Custom 3D Navball:") },
            { "THM_NAVBALL_SHOWING", ("● [显示中] 点击隐藏自定义姿态球", "● [Visible] Click to hide custom navball") },
            { "THM_NAVBALL_HIDDEN", ("○ [已隐藏] 点击开启自定义姿态球", "○ [Hidden] Click to enable custom navball") },
            { "THM_STOCK_NAVBALL", ("KSP 原生底栏导航球:", "Stock Bottom Navball:") },
            { "THM_STOCK_NAVBALL_HIDE", ("已屏蔽原生导航球", "Stock Navball Hidden") },
            { "THM_STOCK_NAVBALL_SHOW", ("显示原生导航球", "Stock Navball Shown") },
            { "THM_STOCK_ALTI", ("KSP 原生顶部高度计盒:", "Stock Top Altimeter Box:") },
            { "THM_STOCK_ALTI_HIDE", ("已屏蔽原生高度计", "Stock Altimeter Hidden") },
            { "THM_STOCK_ALTI_SHOW", ("显示原生高度计", "Stock Altimeter Shown") },
            { "THM_STOCK_STAGE", ("KSP 原生左下操纵分级台:", "Stock Bottom-Left Staging Console:") },
            { "THM_STOCK_STAGE_HIDE", ("已屏蔽原生分级操纵台", "Stock Staging Console Hidden") },
            { "THM_STOCK_STAGE_SHOW", ("显示原生分级操纵台", "Stock Staging Console Shown") },
            { "THM_STOCK_TIME", ("KSP 原生时间加速/时钟:", "Stock Time Warp & MET Clock:") },
            { "THM_STOCK_TIME_HIDE", ("已屏蔽原生加速与时钟", "Stock Time Warp Hidden") },
            { "THM_STOCK_TIME_SHOW", ("显示原生加速与时钟", "Stock Time Warp Shown") },
            { "THM_STOCK_COMM", ("KSP 原生通信信号栏:", "Stock CommNet Signal Bar:") },
            { "THM_STOCK_COMM_HIDE", ("已屏蔽原生 CommNet", "Stock CommNet Hidden") },
            { "THM_STOCK_COMM_SHOW", ("显示原生 CommNet", "Stock CommNet Shown") },
            { "THM_TOOLBAR_MODE", ("右侧工具栏接管模式:", "Right Toolbar Takeover Mode:") },
            { "THM_TB_CLASSIC_ON", ("● 原版经典 (0)", "● Stock Classic (0)") },
            { "THM_TB_CLASSIC_OFF", ("○ 原版经典 (0)", "○ Stock Classic (0)") },
            { "THM_TB_SKIN_ON", ("● 黑晶重肤 (1)", "● Obsidian Reskin (1)") },
            { "THM_TB_SKIN_OFF", ("○ 黑晶重肤 (1)", "○ Obsidian Reskin (1)") },
            { "THM_TB_DOCK_ON", ("● 折叠收纳坞 (2)", "● Folding Dock (2)") },
            { "THM_TB_DOCK_OFF", ("○ 折叠收纳坞 (2)", "○ Folding Dock (2)") },
            { "THM_DOCK_FILTER", ("折叠收纳坞按钮显隐过滤: (共 {0} 项)", "Folding Dock Button Filter: ({0} items total)") },
            { "THM_BTN_SHOW_ALL", ("全显", "Show All") },
            { "THM_BTN_HIDE_ALL", ("全隐", "Hide All") },
            { "THM_HEADER_QUALITY", ("🎯 姿态球生成模式与视网膜超采样", "🎯 Navball Generation & Supersampling") },
            { "THM_NAVBALL_ENGINE", ("姿态球渲染引擎:", "Navball Render Engine:") },
            { "THM_PROC_ENGINE", ("全量矢量程序化解算", "Full Procedural Vector Engine") },
            { "THM_ENGINE_DESC", ("• 彻底脱离原版 2D 贴图依赖，消除极点 UV 挤压畸变，实现无极视网膜矢量精度。", "• Completely eliminates stock 2D texture dependency and polar UV distortion, delivering infinite retina vector precision.") },
            { "THM_RENDER_SCALE", ("渲染倍率:", "Render Scale:") },
            { "THM_SCALE_08", ("0.8x 节能", "0.8x Eco") },
            { "THM_SCALE_10", ("1.0x 原生", "1.0x Native") },
            { "THM_SCALE_125", ("1.25x 细腻", "1.25x Crisp") },
            { "THM_SCALE_15", ("1.5x 视网膜", "1.5x Retina") },
            { "THM_SCALE_20", ("2.0x 极致", "2.0x Ultra") },
            { "THM_DIAG_SCREEN", ("• 物理屏幕: <color=#00FF88>{0} x {1} px</color> (Canvas: <color=#00E5FF>{2:F2}x</color>) | 3D 姿态球屏幕占用: <color=#FFAA00>~{3:F0} x {4:F0} px</color>", "• Physical Screen: <color=#00FF88>{0} x {1} px</color> (Canvas: <color=#00E5FF>{2:F2}x</color>) | 3D Navball Screen Area: <color=#FFAA00>~{3:F0} x {4:F0} px</color>") },
            { "THM_DIAG_TEX", ("• 动态分配贴图: <color=#00FF88>{0} x {1} px</color> (显存占用: ~{2:F2} MB) | 其余 34 个航电小组件为原生 UGUI 1:1 满血矢量输出。", "• Dynamically Allocated Texture: <color=#00FF88>{0} x {1} px</color> (VRAM: ~{2:F2} MB) | Remaining 34 avionics widgets are 1:1 crisp native UGUI vectors.") },
            { "THM_BYPASS_ON", ("● [已完全旁路] 所有 MFP 逻辑/渲染已关闭 (0.00ms)", "● [Fully Bypassed] All MFP logic and rendering disabled (0.00ms)") },
            { "THM_BYPASS_OFF", ("○ [正常运行中] 点击完全 Bypass (或按 F11) 查看原生纯净性能", "○ [Running Normally] Click to fully bypass (or press F11) to check baseline stock overhead") },
            { "THM_HIDE_PROFILER", ("✔ 隐藏性能探针 HUD (F10)", "✔ Hide Performance Profiler HUD (F10)") },
            { "THM_SHOW_PROFILER", ("显示性能探针 HUD (F10)", "Show Performance Profiler HUD (F10)") },
            { "THM_BTN_RUN_SPEC", ("运行 MFP-SPEC 规范审计", "Run MFP-SPEC Architecture Compliance Audit") },
            { "THM_SPEC_OK", ("✔ 合规 ({0} 组件 / {1} 项检查 / 告警 {2})", "✔ Compliant ({0} widgets / {1} checks / warnings {2})") },
            { "THM_SPEC_FAIL", ("✘ 违规 {0} 项 / 告警 {1} 项", "✘ {0} Violations / {1} Warnings") },

            // ==========================================
            // 12. 组件管理面板 (Widget Manager Tab)
            // ==========================================
            { "MGR_FILTER_RUNNING", ("运行中", "Active") },
            { "MGR_FILTER_SUSPENDED", ("已挂起", "Suspended") },
            { "MGR_BTN_SHOW_ALL", ("✔ 全部显示", "✔ Show All") },
            { "MGR_TOAST_SHOW_ALL", ("已全部启用显示！", "All widgets enabled and shown!") },
            { "MGR_BTN_HIDE_ALL", ("○ 全部隐藏", "○ Hide All") },
            { "MGR_TOAST_HIDE_ALL", ("已全部挂起隐藏！", "All widgets suspended and hidden!") },
            { "MGR_SNAP_GRID", ("🧲 全量吸附 10px 网格", "🧲 Snap All to 10px Grid") },
            { "MGR_TOAST_GRID_SNAP", ("已完成全量组件网格对齐！", "All widgets aligned to 10px grid!") },
            { "MGR_NO_MATCH", ("未找到符合条件的组件条目", "No matching widget entries found") },
            { "MGR_CONFIRM_RESET", ("⚠ 确定要恢复出厂默认布局吗？当前排版将被覆盖！", "⚠ Reset to factory default layout? Current layout will be overwritten!") },
            { "MGR_BTN_CONFIRM_RESET", ("✔ 确认覆盖恢复", "✔ Confirm Overwrite & Reset") },
            { "MGR_TOAST_RESET_DONE", ("已恢复出厂默认布局！", "Restored factory default layout!") },
            { "COMMON_CANCEL", ("取消", "Cancel") },
            { "MGR_CONFIRM_CLEAR", ("⚠ 确定清空所有自定义/扩展组件吗？", "⚠ Clear all custom and extension widgets?") },
            { "MGR_BTN_CONFIRM_CLEAR", ("✔ 确认全部清空", "✔ Confirm Clear All") },
            { "MGR_TOAST_CLEAR_DONE", ("已清空全部自定义扩展组件！", "Cleared all custom extension widgets!") },
            { "MGR_BTN_RESET_LAYOUT", ("↺ 恢复出厂默认布局", "↺ Reset to Factory Default Layout") },
            { "MGR_BTN_CLEAR_EXT", ("🗑️ 清空所有扩展组件", "🗑️ Clear All Extension Widgets") },
            { "MGR_RESET_GUARD_HINT", ("重置操作设有二次确认安全守卫", "Reset actions protected by two-step confirmation guard") },
            { "MGR_LED_SHOW", ("● 显", "● Vis") },
            { "MGR_LED_HIDE", ("○ 隐", "○ Hid") },
            { "MGR_TYPE_TAPE", ("PFD 标尺", "PFD Tape") },
            { "MGR_TYPE_ECAM", ("ECAM 表盘", "ECAM Dial") },
            { "MGR_TYPE_SPACEX", ("SPX 龙船", "SPX Dragon") },
            { "MGR_TYPE_CARD", ("遥测卡片", "Telemetry Card") },
            { "MGR_TYPE_CORE", ("原生核心", "Core Avionics") },
            { "MGR_BTN_COPY", ("➕ 复制", "➕ Duplicate") },
            { "MGR_TOAST_COPIED", ("已创建「{0}」副本！", "Created copy of \"{0}\"!") },
            { "MGR_TOAST_REMOVED", ("已移除「{0}」！", "Removed \"{0}\"!") },

            // ==========================================
            // 13. 控件与微组件 (Controls & Floating UI Widget)
            // ==========================================
            { "MOD_TOOLBAR_COLLAPSE", ("▲ 收起", "▲ Collapse") },
            { "MOD_TOOLBAR_MORE_N", ("▼ 更多 ({0})", "▼ More ({0})") },
            { "STG_TOOLTIP_HINT", ("[拖拽跨级 · 悬停高亮]", "[Drag to Reposition · Hover to Highlight]") },
            { "STG_DRAG_MOVE_HINT", ("[拖拽跨级移动]", "[Drag Across Stages]") },
            { "UIW_CAT_ALL", ("全部", "ALL") },
            { "UIW_CAT_GAUGES", ("仪表", "GAUGES") },
            { "UIW_CAT_SYSTEMS", ("系统", "SYSTEMS") },
            { "UIW_CAT_CONTROLS", ("控制", "CONTROLS") },
            { "UIW_BTN_SHOW_ALL", ("👁 全显", "👁 Show All") },
            { "UIW_BTN_HIDE_ALL", ("🚫 全隐", "🚫 Hide All") },
            { "UIW_BTN_SNAP_GRID", ("🧲 网格", "🧲 Snap Grid") },
            { "UIW_BTN_RESET_DEFAULT", ("↺ 默认", "↺ Default") },
            { "SUFFIX_TAPE_CHAR", ("带", "Tape") },
            { "SUFFIX_DIAL_MONITOR", ("监控表", "Monitor") },
            { "SUFFIX_DIAL_G", ("过载表", "G-Meter") },
            { "SUFFIX_DIAL_THRUST", ("推力表", "Thrust") },
            { "SUFFIX_DIAL_CHAR", ("表", "Gauge") },
            { "SUFFIX_SCALE_TAPE", ("标尺带", "Tape") },
            { "WARN_BANNER_STAGE", ("分  离", "STAGE SEP") },

            // ==========================================
            // 14. 核心组件运行时状态 (Core Widget Runtime Status)
            // ==========================================
            { "SAS_STATUS_OFF", ("SAS: OFF", "SAS: OFF") },
            { "SAS_STATUS_LOCK", (" [LOCK]", " [LOCK]") },
            { "SAS_MODE_STABILITY", ("STABILITY", "STABILITY") },
            { "SAS_MODE_PROGRADE", ("PROGRADE", "PROGRADE") },
            { "SAS_MODE_RETROGRADE", ("RETROGRADE", "RETROGRADE") },
            { "SAS_MODE_NORMAL", ("NORMAL", "NORMAL") },
            { "SAS_MODE_ANTINORMAL", ("ANTINORMAL", "ANTINORMAL") },
            { "SAS_MODE_RADIAL_IN", ("RADIAL IN", "RADIAL IN") },
            { "SAS_MODE_RADIAL_OUT", ("RADIAL OUT", "RADIAL OUT") },
            { "SAS_MODE_TARGET", ("TARGET", "TARGET") },
            { "SAS_MODE_MANEUVER", ("MANEUVER", "MANEUVER") },
            { "WIDGET_STAGE_HIDE_STOCK", ("HIDE STOCK", "HIDE STOCK") },
            { "WIDGET_STAGE_SHOW_STOCK", ("SHOW STOCK", "SHOW STOCK") },
            { "WIDGET_TIMEWARP_PAUSED", ("PAUSED", "PAUSED") },
            { "WIDGET_SIGNAL_NONE", ("NONE", "NONE") },
            { "WIDGET_SIGNAL_PART", ("PART", "PART") },
            { "WIDGET_SIGNAL_FULL", ("FULL", "FULL") },
            { "WIDGET_SIGNAL_DSN", ("DSN", "DSN") },
            { "WIDGET_SIGNAL_RELAY", ("RELAY", "RELAY") },
            { "WIDGET_SIGNAL_FOOTER", ("● {0} ACTIVE LINKS  |  REALANTENNAS PROBE", "● {0} ACTIVE LINKS  |  REALANTENNAS PROBE") },
            { "WIDGET_SIGNAL_TITLE", ("REALANTENNAS / COMMNET", "REALANTENNAS / COMMNET") }
        };

        public static int Merge(string repoRoot)
        {
            string locDir = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "Localization");
            string zhPath = Path.Combine(locDir, "zh-CN.json");
            string enPath = Path.Combine(locDir, "en-US.json");

            if (!File.Exists(zhPath) || !File.Exists(enPath))
            {
                Console.WriteLine("Localization files missing!");
                return 1;
            }

            var zhDict = LoadDictionary(zhPath, out string zhCode, out string zhDisp, out string zhNat);
            var enDict = LoadDictionary(enPath, out string enCode, out string enDisp, out string enNat);

            int mergedCount = 0;
            foreach (var kvp in MissingKeyMap)
            {
                string key = kvp.Key;
                var (cnVal, enVal) = kvp.Value;

                if (!zhDict.ContainsKey(key))
                {
                    zhDict[key] = cnVal;
                    enDict[key] = enVal;
                    mergedCount++;
                }
            }

            SaveDictionary(zhPath, zhCode, zhDisp, zhNat, zhDict);
            SaveDictionary(enPath, enCode, enDisp, enNat, enDict);

            Console.WriteLine($"✔ 成功合并 {mergedCount} 个遗漏词条到 zh-CN.json 和 en-US.json! (当前总词条数: {zhDict.Count})");
            return 0;
        }

        private static Dictionary<string, string> LoadDictionary(string path, out string code, out string display, out string native)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            code = root.GetProperty("code").GetString();
            display = root.GetProperty("displayName").GetString();
            native = root.GetProperty("nativeName").GetString();

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in root.GetProperty("translations").EnumerateObject())
            {
                dict[prop.Name] = prop.Value.GetString();
            }
            return dict;
        }

        private static void SaveDictionary(string path, string code, string display, string native, Dictionary<string, string> dict)
        {
            using var stream = new MemoryStream();
            var options = new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            using (var writer = new Utf8JsonWriter(stream, options))
            {
                writer.WriteStartObject();
                writer.WriteString("code", code);
                writer.WriteString("displayName", display);
                writer.WriteString("nativeName", native);
                writer.WriteStartObject("translations");

                foreach (var kvp in dict)
                {
                    writer.WriteString(kvp.Key, kvp.Value);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            File.WriteAllBytes(path, stream.ToArray());
        }
    }
}

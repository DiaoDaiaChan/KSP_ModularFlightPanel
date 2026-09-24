# -*- coding: utf-8 -*-
"""
MFP-SPEC-006 存量迁移 —— 第二阶段：绝对色值 -> 语义色映射（人工判定，脚本执行）
================================================================
每条替换都是人工确认过语义的映射，不是正则通配：
  - 面板/行/单元格/磁贴/LED 底色 -> SurfaceStyleRole
  - 状态徽标底 / 状态面板底        -> StatusSurfaceRole
  - 警告/危险/强调等前景色         -> TextStyleRole
  - new Color(c.r,c.g,c.b,a) 里的魔法透明度 -> LineWeight 档位
  - 剪影/遮罩贴图像素与图标直通     -> WidgetStyleManager.NeutralOpaque（亮度通道，非调色板）

用法: python tools/migrate_semantics.py
"""
import io
import os
import re
import sys

WIDGETS = 'src/ModularFlightPanel/UI/Widgets'

# ---- 通用：魔法透明度 -> 视觉权重档位 ----
ALPHA_TO_WEIGHT = [
    ('0.15f', 'Ghost'), ('0.18f', 'Faint'), ('0.2f', 'Faint'), ('0.20f', 'Faint'),
    ('0.22f', 'Subtle'), ('0.25f', 'Light'), ('0.28f', 'Light'), ('0.3f', 'Light'),
    ('0.30f', 'Light'), ('0.35f', 'Normal'), ('0.4f', 'Strong'), ('0.40f', 'Strong'),
    ('0.45f', 'Strong'), ('0.5f', 'Strong'), ('0.55f', 'Bold'), ('0.6f', 'Bold'),
    ('0.60f', 'Bold'), ('0.65f', 'Bold'), ('0.70f', 'Heavy'), ('0.75f', 'Heavy'),
    ('0.85f', 'Solid'), ('0.90f', 'Solid'), ('0.95f', 'Solid'),
]

REPLACEMENTS = {
    'AvionicsBarGaugeWidget.cs': [
        ('_fillBarImage.color = new Color(0.15f, 0.58f, 0.95f, 0.92f);',
         '_fillBarImage.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.92f);'),
        ('new Color(0.15f, 0.58f, 0.95f, 0.92f)',
         'WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.92f)'),
        ('if (_caretImage != null) _caretImage.color = Color.white;',
         'if (_caretImage != null) _caretImage.color = WidgetStyleManager.Text(TextStyleRole.PrimaryValue);'),
        ('barCol = new Color(1f, 0.25f, 0.25f, 1f); // 结构警示红',
         'barCol = WidgetStyleManager.Text(TextStyleRole.Danger); // 结构警示红'),
        ('_caretImage.color = (_kind == BarGaugeKind.AtmosphericPressure) ? Color.white : (Color)theme.AccentPrimary;',
         '_caretImage.color = (_kind == BarGaugeKind.AtmosphericPressure)\n'
         '                    ? WidgetStyleManager.Text(TextStyleRole.PrimaryValue)\n'
         '                    : WidgetStyleManager.Text(TextStyleRole.Accent);'),
    ],
    'BottomControlsWidget.cs': [
        ('_frameImg.color = new Color(0.06f, 0.09f, 0.14f, 0.95f);',
         '_frameImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);'),
        ('Color inactiveBg = new Color(0.05f, 0.08f, 0.12f, 0.90f);',
         'Color inactiveBg = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Inactive);'),
    ],
    'CommSignalWidget.cs': [
        ('new Color(0.08f, 0.16f, 0.12f, 0.95f),',
         'WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success),'),
        ('_expandBtn.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 0.95f);',
         '_expandBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);'),
        ('_stockBtn.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 0.95f);',
         '_stockBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);'),
        ('Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.85f));',
         'Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.SecondaryValue));'),
        ('ui.RowObj = UIFactory.CreatePanel(parent, name, size, pos, new Color(0.06f, 0.10f, 0.16f, 0.65f));',
         'ui.RowObj = UIFactory.CreatePanel(parent, name, size, pos, WidgetStyleManager.Surface(SurfaceStyleRole.Slot));'),
        ('Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, Color.white);',
         'Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));'),
    ],
    'ElectricalSystemWidget.cs': [
        ('Color cellBg = new Color(0.06f, 0.09f, 0.14f, 0.45f);',
         'Color cellBg = WidgetStyleManager.Surface(SurfaceStyleRole.Slot);'),
        ('TextAnchor.MiddleCenter, Color.white);',
         'TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));'),
        ('_loadValText.color = Color.white;',
         '_loadValText.color = WidgetStyleManager.Text(TextStyleRole.PrimaryValue);'),
    ],
    'HeadingArcWidget.cs': [
        ('BaseLineColor = Color.white',
         'BaseLineColor = WidgetStyleManager.Text(TextStyleRole.PrimaryValue)'),
        ('_bubbleBg.color = new Color(0.04f, 0.07f, 0.12f, 0.95f);',
         '_bubbleBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Panel);'),
    ],
    'LifeSupportWidget.cs': [
        ('Color cellBg = new Color(0.06f, 0.09f, 0.14f, 0.45f);',
         'Color cellBg = WidgetStyleManager.Surface(SurfaceStyleRole.Slot);'),
        ('TextAnchor.MiddleRight, Color.white);',
         'TextAnchor.MiddleRight, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));'),
    ],
    'ModernToolbarWidget.cs': [
        ('new Color(0.08f, 0.14f, 0.22f, 0.9f), borderCol, primaryAccent,',
         'WidgetStyleManager.Surface(SurfaceStyleRole.SlotActive), borderCol, primaryAccent,'),
        ('Color[] mockColors = { Color.cyan, Color.green, Color.yellow, Color.magenta, Color.cyan, new Color(0.2f, 0.6f, 1f), Color.red, Color.green };',
         'ThemeConfig mockTheme = WidgetStyleManager.Instance.CurrentTheme;\n'
         '            Color[] mockColors =\n'
         '            {\n'
         '                mockTheme.AccentSecondary, mockTheme.AccentPrimary, mockTheme.WarningColor, mockTheme.AccentMagenta,\n'
         '                mockTheme.AccentSecondary, WidgetStyleManager.Lighten(mockTheme.AccentSecondary, 0.25f),\n'
         '                mockTheme.DangerColor, mockTheme.AccentPrimary\n'
         '            };'),
        ('Color tileBg = new Color(0.06f, 0.11f, 0.18f, 0.88f);',
         'Color tileBg = WidgetStyleManager.Surface(SurfaceStyleRole.Tile);'),
        ('rawImg.color = Color.white;',
         'rawImg.color = WidgetStyleManager.NeutralOpaque; // 直通原始图标贴图，不做任何着色'),
        ('Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, Color.white);',
         'Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));'),
        ('initialActive ? ledCol : new Color(0.2f, 0.25f, 0.3f, 0.45f));',
         'initialActive ? ledCol : WidgetStyleManager.Surface(SurfaceStyleRole.LedOff));'),
        ('Color ledOff = new Color(0.2f, 0.25f, 0.3f, 0.45f);',
         'Color ledOff = WidgetStyleManager.Surface(SurfaceStyleRole.LedOff);'),
    ],
    'NavballSphereWidget.cs': [
        ('_ballCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);',
         '_ballCamera.backgroundColor = WidgetStyleManager.NeutralTransparent;'),
        ('new Color(0.06f, 0.09f, 0.14f, 0.45f));',
         'WidgetStyleManager.Surface(SurfaceStyleRole.Slot));'),
    ],
    'Rocket2DWidget.cs': [
        ('Color rowBg = isActive ? new Color(0.08f, 0.14f, 0.22f, 0.55f) : new Color(0.06f, 0.09f, 0.14f, 0.45f);',
         'Color rowBg = WidgetStyleManager.Surface(isActive ? SurfaceStyleRole.SlotActive : SurfaceStyleRole.Slot);'),
    ],
    'SASDialWidget.cs': [
        ('img.color = new Color(0.04f, 0.08f, 0.13f, 0.85f);',
         'img.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);'),
        ('Color inactiveBg = new Color(0.04f, 0.08f, 0.13f, 0.85f);',
         'Color inactiveBg = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);'),
        ('Color glassDark = new Color(0.04f, 0.07f, 0.12f, 0.88f);',
         'Color glassDark = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);'),
        ('Color rimColor = new Color(0.20f, 0.45f, 0.70f, 0.70f);',
         'Color rimColor = WidgetStyleManager.Line(\n'
         '                WidgetStyleManager.Darken(WidgetStyleManager.CardBorder(CardStyleRole.Normal), 0.25f), LineWeight.Heavy);'),
        ('Color ringLaser = new Color(0.0f, 0.85f, 1.0f, 0.35f);',
         'Color ringLaser = WidgetStyleManager.Ring(LineWeight.Normal);'),
        ('Color tickColor = new Color(0.40f, 0.70f, 0.95f, 0.60f);',
         'Color tickColor = WidgetStyleManager.Ring(LineWeight.Bold);'),
        ('c = isRidgeLine ? new Color(1f, 1f, 1f, 0.65f) : Color.white;',
         'c = isRidgeLine\n'
         '                        ? WidgetStyleManager.Weighted(WidgetStyleManager.NeutralOpaque, LineWeight.Bold)\n'
         '                        : WidgetStyleManager.NeutralOpaque;'),
    ],
    'SignalStatusWidget.cs': [
        ('Color rowBg = new Color(0.06f, 0.09f, 0.14f, 0.45f);',
         'Color rowBg = WidgetStyleManager.Surface(SurfaceStyleRole.Slot);'),
    ],
    'StageControlWidget.cs': [
        ('new Color(0.95f, 0.65f, 0.10f, 0.85f)).GetComponent<Image>();',
         'WidgetStyleManager.Tinted(TextStyleRole.Warning, LineWeight.Solid)).GetComponent<Image>();'),
        ('_lockBtn.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.18f, 0.95f);',
         '_lockBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);'),
        ('_fireBtn.GetComponent<Image>().color = new Color(0.18f, 0.12f, 0.05f, 0.95f);',
         '_fireBtn.GetComponent<Image>().color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution);'),
        ('fireOut.effectColor = new Color(0.95f, 0.65f, 0.10f, 0.65f);',
         'fireOut.effectColor = WidgetStyleManager.Tinted(TextStyleRole.Warning, LineWeight.Bold);'),
        ('Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, new Color(1.0f, 0.75f, 0.20f));',
         'Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.Warning));'),
        ('new Color(0.02f, 0.04f, 0.08f, 0.95f), secondaryAccent, 1f * s);',
         'WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep), secondaryAccent, 1f * s);'),
        ('new Color(0.06f, 0.09f, 0.14f, 0.90f), WidgetStyleManager.WithAlpha(borderCol, 0.35f), 1f * s);',
         'WidgetStyleManager.Surface(SurfaceStyleRole.Slot), WidgetStyleManager.Weighted(borderCol, LineWeight.Normal), 1f * s);'),
        ('_precImg.color = new Color(0.06f, 0.09f, 0.14f, 0.95f);',
         '_precImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);'),
        ('_modeImg.color = new Color(0.06f, 0.09f, 0.14f, 0.95f);',
         '_modeImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);'),
        ('_stockToggleBtn.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.14f, 0.95f);',
         '_stockToggleBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);'),
        ('Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.85f));',
         'Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.SecondaryValue));'),
        ('new Color(0.05f, 0.08f, 0.13f, 0.95f), WidgetStyleManager.WithAlpha(accent, 0.25f), 1f * s);',
         'WidgetStyleManager.Surface(SurfaceStyleRole.Control), WidgetStyleManager.Weighted(accent, LineWeight.Light), 1f * s);'),
        ('new Vector2(2f * s, 8f * s), Vector2.zero, new Color(1.0f, 0.75f, 0.20f, 0.95f));',
         'new Vector2(2f * s, 8f * s), Vector2.zero, WidgetStyleManager.Tinted(TextStyleRole.Warning, LineWeight.Solid));'),
        ('Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, Color.white);',
         'Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));'),
        ('Color cautionCol = new Color(0.95f, 0.75f, 0.15f);',
         'Color cautionCol = WidgetStyleManager.Text(TextStyleRole.Warning);'),
        ('Color dangerCol = new Color(0.95f, 0.22f, 0.22f);',
         'Color dangerCol = WidgetStyleManager.Text(TextStyleRole.Danger);'),
        ('else _stageLed.color = new Color(0.3f, 0.35f, 0.4f, 0.6f);',
         'else _stageLed.color = WidgetStyleManager.Surface(SurfaceStyleRole.LedStandby);'),
    ],
    'StageDeltaVWidget.cs': [
        ('new Vector2(bayX, bayY), new Color(0.02f, 0.05f, 0.09f, 0.85f),',
         'new Vector2(bayX, bayY), WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep),'),
        ('Vector2.zero, new Color(0.12f, 0.18f, 0.28f, 0.85f),',
         'Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.Inset),'),
        ('Vector2.zero, new Color(0.02f, 0.04f, 0.08f, 0.80f),',
         'Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep),'),
        ('caretImg.color = Color.white;',
         'caretImg.color = WidgetStyleManager.Text(TextStyleRole.PrimaryValue);'),
        ('row.StageBadgeText.color = Color.black;',
         'row.StageBadgeText.color = WidgetStyleManager.Text(TextStyleRole.InverseOnAccent);'),
        ('row.StageBadgeBg.color = new Color(0.12f, 0.18f, 0.28f, 0.85f);',
         'row.StageBadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset);'),
        ('row.CaretImg.color = Color.white;',
         'row.CaretImg.color = WidgetStyleManager.Text(TextStyleRole.PrimaryValue);'),
        ('c = isLine ? new Color(1f, 1f, 1f, 0.40f) : Color.white;',
         'c = isLine\n'
         '                            ? WidgetStyleManager.Weighted(WidgetStyleManager.NeutralOpaque, LineWeight.Strong)\n'
         '                            : WidgetStyleManager.NeutralOpaque;'),
    ],
    'TapeGaugeWidget.cs': [
        ('_centerBoxBg.color = new Color(0.02f, 0.04f, 0.06f, 0.95f);',
         '_centerBoxBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);'),
        ('item.Line.color = Color.white;',
         'item.Line.color = WidgetStyleManager.Text(TextStyleRole.PrimaryValue);'),
        ('item.Line.color = new Color(0.7f, 0.7f, 0.7f, 0.6f);',
         'item.Line.color = WidgetStyleManager.Weighted(WidgetStyleManager.Text(TextStyleRole.Muted), LineWeight.Bold);'),
    ],
    'TimeWarpWidget.cs': [
        ('new Color(0.08f, 0.13f, 0.20f, 0.95f)',
         'WidgetStyleManager.Surface(SurfaceStyleRole.Control)'),
        ('Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.85f));',
         'Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.SecondaryValue));'),
        ('_cancelBtn.GetComponent<Image>().color = new Color(0.16f, 0.10f, 0.05f, 0.95f);',
         '_cancelBtn.GetComponent<Image>().color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution);'),
        ('cancelOut.effectColor = new Color(0.95f, 0.65f, 0.10f, 0.65f);',
         'cancelOut.effectColor = WidgetStyleManager.Tinted(TextStyleRole.Warning, LineWeight.Bold);'),
        ('Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, new Color(1.0f, 0.75f, 0.20f));',
         'Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.Warning));'),
    ],
}

# ---- 姿态球：四个抽象参考系的内联调色板 -> 身份色 + 分层派生 ----
NAVBALL_FRAME_IDENTITY = {
    'INERTIAL': ('AccentSecondary', '惯性系'), 'BARYCENTRIC': ('AccentMagenta', '质心系'),
    'TARGET': ('DangerColor', '目标系'), 'BODY_DIRECTION': ('WarningColor', '机体方向系'),
}

NAVBALL_STRUCT_RE = re.compile(
    r'        private struct NavballFramePalette\n        \{.*?\n        \}\n\n?', re.DOTALL)

NAVBALL_CASE_RE = re.compile(
    r'                case "(%s)":\n                    return new NavballFramePalette\n                    \{.*?\n                    \};\n'
    % '|'.join(NAVBALL_FRAME_IDENTITY), re.DOTALL)

NAVBALL_LERP_RE = re.compile(
    r'        private static NavballFramePalette LerpPalette\(NavballFramePalette from, NavballFramePalette to, float t\)\n        \{\n.*?\n        \}\n',
    re.DOTALL)

LIT = re.compile(
    r'\bnew\s+(?:[A-Za-z_]\w*\.)*Color(?:32)?\s*\(|\b(?:[A-Za-z_]\w*\.)*Color\.(?!clear\b)(?:white|black|red|green|blue|yellow|cyan|magenta|gray|grey)\b')


def count_literals(text):
    n = 0
    for ln in text.split('\n'):
        t = ln.strip()
        if not t or t.startswith('//') or t.startswith('/*') or t.startswith('*'):
            continue
        i = ln.find('//')
        if LIT.search(ln[:i] if i >= 0 else ln):
            n += 1
    return n


def migrate_navball(path):
    with io.open(path, encoding='utf-8', newline='') as f:
        s = f.read()
    before = count_literals(s)

    # 先整体重命名私有结构体（此文件中 FramePalette 仅指本组件私有定义），
    # 再删除定义、改写四个参考系与插值函数
    s = re.sub(r'\bFramePalette\b', 'NavballFramePalette', s)

    s, n1 = NAVBALL_STRUCT_RE.subn('', s)
    s, n2 = NAVBALL_CASE_RE.subn(
        lambda m: '                case "%s":\n'
                  '                    return WidgetStyleManager.Instance.GetNavballFramePalette(theme.%s, theme);\n'
                  % (m.group(1), NAVBALL_FRAME_IDENTITY[m.group(1)][0]), s)
    s, n3 = NAVBALL_LERP_RE.subn(
        '        private static NavballFramePalette LerpPalette(NavballFramePalette from, NavballFramePalette to, float t)\n'
        '        {\n'
        '            return WidgetStyleManager.LerpFramePalette(from, to, t);\n'
        '        }\n', s)

    # 通用魔法透明度 -> 权重档位
    s = apply_alpha_weights(s)
    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(s)
    print('%-32s 结构删除=%d 参考系改写=%d 插值改写=%d | %3d -> %3d' % (
        os.path.basename(path), n1, n2, n3, before, count_literals(s)))


def apply_alpha_weights(s):
    for num, weight in ALPHA_TO_WEIGHT:
        s = re.sub(
            r'WidgetStyleManager\.WithAlpha\(([^,()]+(?:\([^()]*\))?[^,()]*), %s\)' % re.escape(num),
            lambda m, w=weight: 'WidgetStyleManager.Weighted(%s, LineWeight.%s)' % (m.group(1).strip(), w), s)
    return s


def main():
    total = 0
    for name, pairs in REPLACEMENTS.items():
        path = os.path.join(WIDGETS, name)
        with io.open(path, encoding='utf-8', newline='') as f:
            s = f.read()
        before = count_literals(s)
        for old, new in pairs:
            if old not in s:
                print('  !! MISS  %s :: %s' % (name, old[:70]))
            s = s.replace(old, new)
        s = apply_alpha_weights(s)
        after = count_literals(s)
        total += after
        with io.open(path, 'w', encoding='utf-8', newline='') as f:
            f.write(s)
        print('%-32s %3d -> %3d' % (name, before, after))

    migrate_navball(os.path.join(WIDGETS, 'NavballSphereWidget.cs'))


if __name__ == '__main__':
    main()

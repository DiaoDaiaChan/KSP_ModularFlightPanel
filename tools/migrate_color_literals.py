# -*- coding: utf-8 -*-
"""
MFP-SPEC-006 存量迁移辅助脚本（机械变换部分）
================================================================
只做语义完全等价的语法级改写，不做任何配色决策：

  A. 主题空值兜底字面量       theme != null ? (Color)theme.X : <字面量>  ->  theme.X
  B. 派生透明色              new Color(c.r, c.g, c.b, a)               ->  WidgetStyleManager.WithAlpha(c, a)
  C. 派生压暗色              new Color(c.r*m, c.g*m, c.b*m, a)         ->  WithAlpha(Darken(c, 1-m), a)
  D. 向黑插值                Color.Lerp(c, Color.black, a)             ->  WidgetStyleManager.Darken(c, a)
  E. 主题来源解析化          ThemeManager.Instance.CurrentTheme        ->  WidgetStyleManager.Instance.CurrentTheme
                            _currentTheme ?? ...                      ->  WidgetStyleManager.ResolveTheme(_currentTheme)
  F. OnInitialize 入口注入   theme = WidgetStyleManager.ResolveTheme(theme);

剩余无法机械判定的"绝对色值"（各组件私有的面板/徽标调色板）一律不动，
由人工按 SurfaceStyleRole / StatusSurfaceRole / LineWeight 语义映射处理。

用法: python tools/migrate_color_literals.py <file...>
"""
import re
import sys

LITERAL_FALLBACK = r'(?:new\s+Color\([^()]*\)|Color\.\w+)'

PAT_TERNARY = re.compile(
    r'(theme|_currentTheme)\s*!=\s*null\s*\?\s*\(Color\)\1\.(\w+)\s*:\s*' + LITERAL_FALLBACK
)
# 必须先处理 "(X != null) ? ..." 这种自带括号的写法，否则会与上面的规则交错出括号失衡
PAT_TERNARY_PAREN = re.compile(
    r'\(\s*(theme|_currentTheme)\s*!=\s*null\s*\)\s*\?\s*\(Color\)\1\.(\w+)\s*:\s*' + LITERAL_FALLBACK
)
PAT_DERIVED_DIM = re.compile(
    r'new\s+Color\(\s*([\w\.]+)\.r\s*\*\s*([0-9.]+f)\s*,\s*\1\.g\s*\*\s*\2\s*,\s*\1\.b\s*\*\s*\2\s*,\s*([0-9.]+f)\s*\)'
)
PAT_DERIVED = re.compile(
    r'new\s+Color\(\s*([\w\.]+)\.r\s*,\s*\1\.g\s*,\s*\1\.b\s*,\s*([0-9.]+f)\s*\)'
)
PAT_LERP_BLACK = re.compile(
    r'Color\.Lerp\(\s*([\w\.]+)\s*,\s*Color\.black\s*,\s*([0-9.]+f)\s*\)'
)

PAT_DECL = re.compile(r'ThemeConfig\s+theme\s*=\s*_currentTheme\s*\?\?\s*ThemeManager\.Instance\.CurrentTheme\s*;')
PAT_DECL_PLAIN = re.compile(r'ThemeConfig\s+theme\s*=\s*ThemeManager\.Instance\.CurrentTheme\s*;')
PAT_ONINIT = re.compile(
    r'(protected\s+override\s+void\s+OnInitialize\s*\(\s*WidgetConfig\s+config\s*,\s*ThemeConfig\s+theme\s*\)\s*\{\n)'
)

COLOR_LITERAL = re.compile(
    r'\bnew\s+(?:[A-Za-z_]\w*\.)*Color(?:32)?\s*\(|\b(?:[A-Za-z_]\w*\.)*Color\.(?!clear\b)(?:white|black|red|green|blue|yellow|cyan|magenta|gray|grey)\b'
)


def strip_comments(line):
    t = line.strip()
    if not t or t.startswith('//') or t.startswith('/*') or t.startswith('*'):
        return ''
    i = line.find('//')
    return line[:i] if i >= 0 else line


def count_literals(text):
    return sum(1 for ln in text.split('\n') if COLOR_LITERAL.search(strip_comments(ln)))


def main(paths):
    for path in paths:
        with open(path, encoding='utf-8', newline='') as f:
            original = f.read()

        before = count_literals(original)
        text = original

        text, n_tern_p = PAT_TERNARY_PAREN.subn(lambda m: '%s.%s' % (m.group(1), m.group(2)), text)
        text, n_tern = PAT_TERNARY.subn(lambda m: '%s.%s' % (m.group(1), m.group(2)), text)
        n_tern += n_tern_p
        text, n_dim = PAT_DERIVED_DIM.subn(
            lambda m: 'WidgetStyleManager.WithAlpha(WidgetStyleManager.Darken(%s, %.2ff), %s)'
                      % (m.group(1), 1.0 - float(m.group(2).rstrip('f')), m.group(3)), text)
        text, n_der = PAT_DERIVED.subn(
            lambda m: 'WidgetStyleManager.WithAlpha(%s, %s)' % (m.group(1), m.group(2)), text)
        text, n_lrp = PAT_LERP_BLACK.subn(
            lambda m: 'WidgetStyleManager.Darken(%s, %s)' % (m.group(1), m.group(2)), text)

        text, n_d1 = PAT_DECL.subn('ThemeConfig theme = WidgetStyleManager.ResolveTheme(_currentTheme);', text)
        text, n_d2 = PAT_DECL_PLAIN.subn('ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;', text)
        text, n_ini = PAT_ONINIT.subn(
            r'\1            theme = WidgetStyleManager.ResolveTheme(theme);\n', text)

        after = count_literals(text)
        if text != original:
            with open(path, 'w', encoding='utf-8', newline='') as f:
                f.write(text)
        print('%-32s 兜底%3d 派生%2d 压暗%2d 插值%2d 声明%2d 注入%2d | %3d -> %3d' % (
            path.replace('\\', '/').split('/')[-1],
            n_tern, n_der + n_dim, n_dim, n_lrp, n_d1 + n_d2, n_ini, before, after))


if __name__ == '__main__':
    main(sys.argv[1:])

import { existsSync } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..');
const sourcePath = resolve(root, 'src/widget-schema.json');
const repoOutputPath = resolve(root, '..', 'GameData/ModularFlightPanel/PluginData/layout.json');
const defaultKsp = process.env.KSP_ROOT || 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Kerbal Space Program_newmod';
const kspOutputPath = resolve(defaultKsp, 'GameData/ModularFlightPanel/PluginData/layout.json');

const source = JSON.parse(await readFile(sourcePath, 'utf8'));

if (source.schemaVersion !== 1 || !Array.isArray(source.widgets)) throw new Error('Unsupported widget schema');

const widgets = source.widgets.map((widget, index) => {
  if (!widget.id || !widget.name || !widget.type) throw new Error(`Widget #${index} is missing id, name or type`);
  if (widget.token && !/^\{[^}]+\}/.test(widget.token)) throw new Error(`${widget.id}: token must be a wildcard such as {SPD}`);
  const mode = widget.limitMode || (widget.softLimit ? 'soft' : 'hard');
  if (!['hard', 'soft', 'none'].includes(mode)) throw new Error(`${widget.id}: invalid limitMode`);
  return {
    WidgetId: widget.id,
    DisplayName: widget.name,
    IsEnabled: widget.enabled !== false,
    PositionX: widget.x || 0,
    PositionY: widget.y || 0,
    Scale: widget.scale || 1,
    CustomTemplate: widget.template || '',
    WidgetType: widget.type,
    NumericToken: widget.token || '{SPD}',
    MinValue: widget.min || 0,
    MaxValue: widget.max ?? 100,
    CautionThreshold: widget.caution ?? 80,
    WarningThreshold: widget.warning ?? 95,
    IsSoftLimit: mode === 'soft',
    LimitMode: mode,
    UnitLabel: widget.unit || '',
    StepInterval: widget.step || 10,
    IsLeftOrientation: widget.left !== false
  };
});

const output = JSON.stringify({ GlobalScale: source.globalScale || 1, Widgets: widgets }, null, 2) + '\n';

// 1. Always export to repo GameData
await mkdir(dirname(repoOutputPath), { recursive: true });
await writeFile(repoOutputPath, output, 'utf8');
console.log(`[Export] Wrote ${widgets.length} widgets to repository: ${repoOutputPath}`);

// 2. Also export to target KSP instance if directory exists
if (existsSync(defaultKsp)) {
  await mkdir(dirname(kspOutputPath), { recursive: true });
  await writeFile(kspOutputPath, output, 'utf8');
  console.log(`[Export] Synced ${widgets.length} widgets to KSP installation: ${kspOutputPath}`);
}


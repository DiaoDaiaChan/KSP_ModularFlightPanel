import { StrictMode, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';

const colors = { green: '#2bff7b', blue: '#7184ff', pink: '#ff43b9', yellow: '#ffe34f' };

function useDragPosition() {
  const [position, setPosition] = useState({ x: 0, y: 0 });
  const drag = useRef(null);
  useEffect(() => {
    const move = event => { if (drag.current) setPosition({ x: event.clientX - drag.current.x, y: event.clientY - drag.current.y }); };
    const up = () => { drag.current = null; };
    window.addEventListener('pointermove', move); window.addEventListener('pointerup', up);
    return () => { window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', up); };
  }, []);
  return [position, event => { event.preventDefault(); drag.current = { x: event.clientX - position.x, y: event.clientY - position.y }; }];
}

function DragHandle({ onPointerDown, title }) {
  return <button className="drag-handle" title={`Drag ${title}`} onPointerDown={onPointerDown}>⠿</button>;
}

function polar(cx, cy, r, deg) {
  const a = (deg - 90) * Math.PI / 180;
  return [cx + r * Math.cos(a), cy + r * Math.sin(a)];
}

function arcPath(cx, cy, r, start, end) {
  const [sx, sy] = polar(cx, cy, r, start);
  const [ex, ey] = polar(cx, cy, r, end);
  const large = Math.abs(end - start) > 180 ? 1 : 0;
  return `M ${sx} ${sy} A ${r} ${r} 0 ${large} 1 ${ex} ${ey}`;
}

function SegmentedArc({ value, color, start = 220, end = 320, label, unit }) {
  const [position, startDrag] = useDragPosition();
  const count = 14;
  const span = end - start;
  return <div className="arc-gauge" style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title={label} />
    <svg viewBox="0 0 180 180" aria-label={label}>
      {Array.from({ length: count }, (_, i) => {
        const a0 = start + i * span / count + 1.2;
        const a1 = start + (i + 1) * span / count - 1.2;
        const active = i < Math.ceil(value * count);
        return <path key={i} d={arcPath(90, 90, 72, a0, a1)} className={active ? 'arc-active' : 'arc-idle'} style={active ? { stroke: color } : undefined} />;
      })}
    </svg>
    <div className="arc-center"><span>{label}</span><strong>{Math.round(value * 100)}</strong><small>{unit}</small></div>
    <span className="arc-min">0</span><span className="arc-max">100</span>
  </div>;
}

function Navball() {
  const [position, startDrag] = useDragPosition();
  return <div className="navball-wrap"><div className="draggable-navball" style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="NAVBALL" />
    <div className="heading-chip">189°<small>SURFACE</small></div>
    <svg className="navball" viewBox="0 0 320 320" aria-label="Navball">
      <defs><radialGradient id="sky"><stop stopColor="#b9f4ff"/><stop offset=".62" stopColor="#38b9d8"/><stop offset="1" stopColor="#08758e"/></radialGradient></defs>
      <circle cx="160" cy="160" r="147" fill="url(#sky)" stroke="#7184ff" strokeWidth="5" />
      <path d="M18 175 Q160 126 302 175 L302 310 L18 310Z" fill="#79441c" opacity=".96" />
      <g className="ball-grid" fill="none" stroke="#d9fbff" strokeWidth="2" opacity=".8">
        <ellipse cx="160" cy="160" rx="137" ry="48"/><ellipse cx="160" cy="160" rx="137" ry="92"/><ellipse cx="160" cy="160" rx="46" ry="137"/><ellipse cx="160" cy="160" rx="92" ry="137"/><path d="M23 160H297M160 18V302"/>
      </g>
      <path d="M113 160H207M160 113V207" stroke="#ffe34f" strokeWidth="4" />
      <circle cx="160" cy="160" r="8" fill="#ffe34f" stroke="#10162e" strokeWidth="3" />
    </svg>
  </div></div>;
}

function TapeGauge({ label, value, unit, color, side, verticalSpeed = 0, shape = 'slim', onToggleShape }) {
  const [position, startDrag] = useDragPosition();
  const ticks = Array.from({ length: 9 }, (_, i) => Math.round(value + (i - 4) * 10));
  const isArc = shape === 'arc';
  return <div className={`tape ${side} ${isArc ? 'tape-arc' : 'tape-slim'}`} style={{ '--tape-color': color, transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title={label} />
    <div className="tape-title"><span>{label}</span><small>{unit}</small>{onToggleShape && <button className="tape-shape-toggle" onClick={onToggleShape}>{isArc ? 'SLIM' : 'ARC'}</button>}</div>
    {!isArc && <div className="tape-body">{ticks.map((tick, i) => <div className={i === 4 ? 'tape-tick selected' : 'tape-tick'} key={i}><span>{tick}</span><i /></div>)}</div>}
    {isArc && <div className="arc-band">{ticks.map((tick, i) => { const angle = (i - 4) * 15; return <div className={i === 4 ? 'arc-tick selected' : 'arc-tick'} key={i} style={{ '--arc-angle': `${angle}deg` }}><span style={{ transform: `rotate(${-angle}deg)` }}>{tick}</span><i /></div>; })}</div>}
    <div className="tape-value">{value.toLocaleString()}<small>{unit}</small></div>
    <div className={`trend-indicator ${verticalSpeed >= 0 ? 'trend-up' : 'trend-down'}`} style={{ '--trend-length': `${Math.max(18, Math.min(92, Math.abs(verticalSpeed) * 2.2))}px` }}>
      <span>{verticalSpeed >= 0 ? '+' : '−'}{Math.abs(verticalSpeed)}</span><i /><b>{verticalSpeed >= 0 ? '▲' : '▼'}</b>
    </div>
  </div>;
}

function EcamDial({ title, value, max, unit, color, warning }) {
  const [position, startDrag] = useDragPosition();
  const percent = Math.min(100, value / max * 100);
  return <div className="ecam-dial panel" style={{ '--dial-color': color, transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title={title} />
    <div className="dial-title">{title}</div>
    <div className="dial-ring" style={{ '--dial-fill': `${percent * 2.7}deg` }}><div className="dial-needle" /></div>
    <div className={warning ? 'dial-value warning' : 'dial-value'}>{value}<small>{unit}</small></div>
    <div className="dial-scale"><span>0</span><span>{max}</span></div>
  </div>;
}

function StatusStrip() {
  const [position, startDrag] = useDragPosition();
  return <div className="status-strip panel" style={{ transform: `translate(calc(-50% + ${position.x}px), ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="SYSTEM STATUS" /><div><span>SYSTEM STATUS</span><strong><i /> NOMINAL</strong></div><div><span>RCS</span><b>OFF</b></div><div><span>PROP</span><b className="good">78.4%</b></div><div><span>FRAME</span><b>SURFACE</b></div></div>;
}

function FooterControls() {
  const [position, startDrag] = useDragPosition();
  return <footer className="footer" style={{ transform: `translate(calc(-50% + ${position.x}px), ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="BOTTOM CONTROLS" /><button className="footer-btn">RCS</button><div className="footer-state"><span>FLIGHT STATUS</span><strong>SYSTEMS NOMINAL</strong></div><button className="footer-btn active">SAS</button></footer>;
}

function ComponentBar({ visibility, onToggle }) {
  const buttons = [['elec', 'ELEC'], ['xy', 'XY'], ['orbit', 'ORBIT'], ['rocket', 'ROCKET'], ['life', 'LIFE'], ['signal', 'SIGNAL'], ['stage', 'STAGE'], ['staging', 'STACK'], ['action', 'F/CTL']];
  return <nav className="component-bar" aria-label="Component visibility controls"><span className="component-bar-label">DISPLAY</span>{buttons.map(([id, label]) => <button key={id} className={visibility[id] ? 'component-toggle active' : 'component-toggle'} onClick={() => onToggle(id)}><i />{label}</button>)}</nav>;
}

function ActionScale({ label, value, vertical = false, color = colors.green }) {
  const percent = Math.round((value + 1) * 50);
  return <div className={`action-scale ${vertical ? 'vertical' : ''}`} style={{ '--action-color': color }}>
    <span className="action-label">{label}</span>
    <div className="scale-track"><i className="scale-center" /><i className="scale-pointer" style={vertical ? { bottom: `${percent}%` } : { left: `${percent}%` }} /></div>
    <div className="scale-readout">{value > 0 ? '+' : ''}{value.toFixed(2)}</div>
  </div>;
}

function ActionIndicator() {
  const [roll, setRoll] = useState(0.16);
  const [pitch, setPitch] = useState(0.28);
  const [yaw, setYaw] = useState(-0.22);
  const [position, startDrag] = useDragPosition();
  return <section className="action-indicator panel" style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="FLIGHT CONTROLS" />
    <div className="action-header"><span>FLIGHT CONTROLS</span><strong>STG 001</strong><i className="armed-dot" /></div>
    <div className="action-body">
      <div className="action-column">
        <ActionScale label="ROLL" value={roll} color={colors.yellow} />
        <input aria-label="Roll input" type="range" min="-1" max="1" step=".01" value={roll} onChange={e => setRoll(Number(e.target.value))} />
        <ActionScale label="YAW / RUD" value={yaw} color={colors.green} />
        <input aria-label="Yaw input" type="range" min="-1" max="1" step=".01" value={yaw} onChange={e => setYaw(Number(e.target.value))} />
      </div>
      <div className="pitch-column"><ActionScale label="PITCH" value={pitch} vertical color={colors.blue} /><input aria-label="Pitch input" type="range" min="-1" max="1" step=".01" value={pitch} onChange={e => setPitch(Number(e.target.value))} /></div>
      <div className="action-mode-list"><button className="mode-active">RCS</button><button>SAS</button><button>TRIM</button></div>
    </div>
  </section>;
}

function PowerNode({ title, values, type = 'source', x, y }) {
  return <div className={`power-node ${type}`} style={{ left: x, top: y }}><span>{title}</span>{values.map((value, index) => <strong key={index}>{value}</strong>)}</div>;
}

function ElectricalSystem() {
  const [position, startDrag] = useDragPosition();
  return <section className="electrical-system panel" style={{ transform: `translate(calc(-50% + ${position.x}px), ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="ELEC" />
    <div className="electrical-header"><span>ELEC</span><small>POWER DISTRIBUTION</small><b><i /> LIVE</b></div>
    <div className="power-canvas">
      <svg className="power-lines" viewBox="0 0 550 238" preserveAspectRatio="none">
        <path d="M104 49H220M446 49H330M275 63V82M104 133H220M446 133H330M275 104V132M180 190V155H245M370 190V155H305M68 190V155H60V133M482 190V155H490V133" />
        <path className="active-line" d="M104 49H220M275 63V82M275 104V132M180 190V155H245" />
      </svg>
      <PowerNode title="BAT 1" values={['27 V', '0 A']} type="battery" x="70px" y="18px" />
      <PowerNode title="BAT 2" values={['27 V', '0 A']} type="battery" x="365px" y="18px" />
      <PowerNode title="DC BAT" values={['BUS ONLINE']} type="bus" x="220px" y="39px" />
      <PowerNode title="DC ESS" values={['27 V', '200 A']} type="bus" x="220px" y="82px" />
      <PowerNode title="TR 1" values={['28 V', '200 A']} type="load" x="18px" y="105px" />
      <PowerNode title="TR 2" values={['28 V', '200 A']} type="load" x="422px" y="105px" />
      <PowerNode title="AC ESS" values={['115 V', '400 Hz']} type="bus" x="220px" y="132px" />
      <PowerNode title="GEN 1" values={['115 V', '400 Hz']} type="source" x="0px" y="184px" />
      <PowerNode title="APU GEN" values={['115 V', '400 Hz']} type="source active" x="150px" y="184px" />
      <PowerNode title="EXT GEN" values={['115 V', '400 Hz']} type="source" x="300px" y="184px" />
      <PowerNode title="GEN 2" values={['115 V', '400 Hz']} type="source" x="450px" y="184px" />
      <div className="power-load-label">AVIONICS / LIFE SUPPORT / ACTUATORS</div>
    </div>
  </section>;
}

function XYChartWidget() {
  const [position, startDrag] = useDragPosition();
  const [extraAction, setExtraAction] = useState(false);
  const points = [[0, 0], [2, 80], [4, 260], [6, 620], [8, 980], [10, 1280], [12, 1510], [14, 1730], [16, 1980], [18, 2140], [20, 2260]];
  const actions = [
    { t: 4, h: 260, label: 'LIFTOFF', color: colors.green },
    { t: 9, h: 1120, label: 'PITCH', color: colors.yellow },
    { t: 13, h: 1610, label: 'STAGE', color: colors.pink },
    ...(extraAction ? [{ t: 17, h: 2070, label: 'RCS', color: colors.blue }] : [])
  ];
  const x = t => 34 + t / 20 * 276;
  const y = h => 105 - h / 2400 * 82;
  const path = points.map(([t, h], index) => `${index ? 'L' : 'M'} ${x(t)} ${y(h)}`).join(' ');
  return <section className="xy-widget panel" style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="XY PLOT" />
    <div className="xy-header"><span>CUSTOM / XY PLOT</span><strong>ALTITUDE PROFILE</strong><button onClick={() => setExtraAction(value => !value)}>{extraAction ? 'CLEAR' : '+ ACTION'}</button></div>
    <svg className="xy-plot" viewBox="0 0 330 136" role="img" aria-label="Altitude over time chart">
      {[0, 600, 1200, 1800, 2400].map(value => <g key={value}><line x1="34" x2="310" y1={y(value)} y2={y(value)} className="xy-grid" /><text x="0" y={y(value) + 3} className="xy-axis-label">{value}</text></g>)}
      {[0, 5, 10, 15, 20].map(value => <text key={value} x={x(value) - 4} y="124" className="xy-axis-label">{value}</text>)}
      <path d={path} className="xy-line" />
      {actions.map(action => <g key={action.label} className="xy-action"><line x1={x(action.t)} x2={x(action.t)} y1="16" y2={y(action.h)} style={{ stroke: action.color }} /><circle cx={x(action.t)} cy={y(action.h)} r="4" style={{ fill: action.color }} /><text x={x(action.t) + 5} y="18" style={{ fill: action.color }}>{action.label}</text></g>)}
      <line x1="34" x2="310" y1="105" y2="105" className="xy-axis" /><line x1="34" x2="34" y1="16" y2="105" className="xy-axis" />
      <text x="274" y="134" className="xy-unit">TIME / s</text><text x="2" y="12" className="xy-unit">ALT / m</text>
    </svg>
    <div className="xy-legend"><i /> FLIGHT PATH <b>●</b> ACTION POINTS</div>
  </section>;
}

function Orbit3DWidget() {
  const [position, startDrag] = useDragPosition();
  const elements = [
    ['a', '7,054', 'km'], ['e', '0.0124', ''], ['i', '51.64', '°'],
    ['Ω', '132.8', '°'], ['ω', '84.2', '°'], ['ν', '27.5', '°']
  ];
  return <section className="orbit3d panel" style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="ORBIT 3D" />
    <div className="orbit3d-header"><span>ORBIT / 3D</span><small>KEPLERIAN ELEMENTS</small><b><i /> TRACKED</b></div>
    <div className="orbit-scene">
      <div className="orbit-sphere"><span className="equator" /><span className="prime-meridian" /></div>
      <svg className="orbit-plane" viewBox="0 0 240 150" aria-label="3D orbital plane"><ellipse cx="120" cy="75" rx="100" ry="45" /><path d="M20 75H220" /><circle cx="154" cy="53" r="4" className="orbit-vessel" /><path d="M154 53l17-12" className="orbit-radius" /></svg>
      <span className="orbit-node ascending">Ω</span><span className="orbit-node periapsis">ω</span>
    </div>
    <div className="orbit-elements">{elements.map(([name, value, unit]) => <div key={name}><span>{name}</span><strong>{value}<small>{unit}</small></strong></div>)}</div>
    <div className="orbit-caption">VSL 27.5° <b>•</b> ASCENDING NODE 132.8°</div>
  </section>;
}

function Rocket2DWidget() {
  const [position, startDrag] = useDragPosition();
  const [activeStage, setActiveStage] = useState(0);
  const [separating, setSeparating] = useState(false);
  const [pulse, setPulse] = useState(0);
  useEffect(() => {
    const timer = window.setInterval(() => setPulse(value => (value + 1) % 100), 80);
    return () => window.clearInterval(timer);
  }, []);
  const stages = [
    { id: 3, name: 'CORE + BOOSTERS', prop: 78.4, dv: 2310, thrust: 1240, engines: '4 × LR-87', feed: 'LFO / CRYO' },
    { id: 2, name: 'UPPER CORE', prop: 42.7, dv: 1480, thrust: 640, engines: '2 × VECTOR', feed: 'LFO' },
    { id: 1, name: 'ORBITAL INSERTION', prop: 12.1, dv: 620, thrust: 220, engines: '1 × TERRIER', feed: 'LFO' }
  ];
  const separate = () => {
    if (separating || activeStage >= stages.length - 1) return;
    setSeparating(true);
    window.setTimeout(() => { setActiveStage(value => value + 1); setSeparating(false); }, 900);
  };
  return <section className="rocket2d panel" style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="ROCKET 2D" />
    <div className="rocket2d-header"><span>ROCKET / 2D</span><small>MECHJEB TELEMETRY</small><b><i /> LIVE</b></div>
    <div className="rocket-stack">
      {stages.map((stage, index) => <div key={stage.id} className={`rocket-stage ${index === activeStage ? 'active' : ''} ${separating && index === activeStage ? 'rocket-separating' : ''}`}>
        <div className="rocket-stage-id"><strong>STG {stage.id}</strong><span>{index === activeStage ? 'ACTIVE' : 'STANDBY'}</span></div>
        <div className="rocket-mini"><div className="mini-fuselage"><i style={{ height: `${18 + Math.sin(pulse / 8) * 4}px` }} /></div><div className="mini-engine"><b /><b /><b /></div></div>
        <div className="rocket-stage-data"><strong>{stage.name}</strong><span>{stage.engines} · {stage.feed}</span><div className="rocket-metrics"><label>PROP <b>{stage.prop.toFixed(1)}%</b></label><label>THR <b>{stage.thrust.toLocaleString()} kN</b></label><label>ΔV <b>{stage.dv.toLocaleString()} m/s</b></label></div><div className="rocket-fuel"><i style={{ width: `${stage.prop}%` }} /></div></div>
        {index < stages.length - 1 && <div className="stage-separator">↓ <span>STAGE SEPARATION</span></div>}
      </div>)}
    </div>
    <div className="rocket2d-footer"><span>MJ / VESSEL MASS 42.8 t</span><button onClick={separate} disabled={separating || activeStage >= stages.length - 1}>{separating ? 'SEPARATING…' : 'SIMULATE STAGE'}</button></div>
  </section>;
}

function LifeSupportWidget() {
  const [position, startDrag] = useDragPosition();
  const resources = [
    { name: 'O₂', label: 'OXYGEN', amount: '92.4', unit: '%', rate: '−0.42 kg/h', time: '3d 18h', color: colors.blue, state: 'NOMINAL' },
    { name: 'FOOD', label: 'FOOD STORES', amount: '76.1', unit: '%', rate: '−0.18 kg/h', time: '5d 04h', color: colors.green, state: 'NOMINAL' },
    { name: 'H₂O', label: 'WATER', amount: '84.3', unit: '%', rate: '−0.31 kg/h', time: '4d 12h', color: colors.blue, state: 'NOMINAL' },
    { name: 'CO₂', label: 'CARBON DIOXIDE', amount: '18.7', unit: '%', rate: '+0.12 kg/h', time: 'LIOH 81%', color: colors.yellow, state: 'SCRUBBING' },
    { name: 'WASTE', label: 'SOLID WASTE', amount: '12.0', unit: '%', rate: '+0.06 kg/h', time: 'CAP 12.4 kg', color: colors.pink, state: 'NOMINAL' },
    { name: 'W/W', label: 'WASTE WATER', amount: '9.4', unit: '%', rate: '+0.11 kg/h', time: 'CAP 18.0 L', color: colors.pink, state: 'NOMINAL' }
  ];
  return <section className="life-support panel" style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="LIFE SUPPORT" />
    <div className="life-header"><span>LIFE SUPPORT</span><small>KERBALISM / HABITAT</small><b><i /> NOMINAL</b></div>
    <div className="life-summary"><strong>CREW 03</strong><span>PRESSURE 101.3 kPa</span><span>TEMP 22.4 °C</span></div>
    <div className="life-grid">{resources.map(resource => <div className="life-resource" key={resource.name} style={{ '--life-color': resource.color }}><div className="life-resource-title"><strong>{resource.name}</strong><span>{resource.label}</span><b>{resource.state}</b></div><div className="life-bar"><i style={{ width: `${resource.amount}%` }} /></div><div className="life-values"><strong>{resource.amount}<small>{resource.unit}</small></strong><span>{resource.rate}</span><em>{resource.time}</em></div></div>)}</div>
    <div className="life-footer"><span>SCRUBBER</span><strong>LIOH / ACTIVE</strong><span>WASTE ROUTE</span><strong className="good">SEALED</strong></div>
  </section>;
}

function SignalListWidget() {
  const [position, startDrag] = useDragPosition();
  const [selected, setSelected] = useState(0);
  const antennas = [
    { name: 'DTS-M1', kind: 'DIRECT / S-BAND', band: 'S', freq: '2.2 GHz', bandwidth: '1.0 MHz', network: 'KSC RELAY', strength: 88, range: '1,240 km', state: 'LINKED', color: colors.green },
    { name: 'HG-5', kind: 'RELAY / HIGH GAIN', band: 'X', freq: '8.4 GHz', bandwidth: '250 kHz', network: 'DEEP SPACE', strength: 64, range: '4.82 Mm', state: 'SEARCHING', color: colors.yellow },
    { name: 'COMM-16', kind: 'OMNI / LOW GAIN', band: 'U', freq: '401 MHz', bandwidth: '32 kHz', network: 'LOCAL BUS', strength: 96, range: '18.2 km', state: 'LINKED', color: colors.blue }
  ];
  return <section className="signal-list panel" style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="SIGNAL LIST" />
    <div className="signal-header"><span>SIGNAL</span><small>RP-1 / ANTENNA NETWORK</small><b>?</b></div>
    <div className="signal-summary"><strong>ANTENNAS {antennas.length}</strong><span><i /> NETWORK NOMINAL</span><em>{antennas.filter(antenna => antenna.state === 'LINKED').length} LINKED</em></div>
    <div className="signal-rows">{antennas.map((antenna, index) => <button key={antenna.name} className={selected === index ? 'signal-row selected' : 'signal-row'} onClick={() => setSelected(index)}><div className="signal-icon" style={{ '--signal-color': antenna.color }}>◉</div><div className="signal-row-main"><strong>{antenna.name}</strong><span>{antenna.kind}</span><div className="signal-bars">{[1, 2, 3, 4, 5].map(bar => <i key={bar} className={bar <= Math.ceil(antenna.strength / 20) ? 'on' : ''} style={{ background: bar <= Math.ceil(antenna.strength / 20) ? antenna.color : undefined }} />)}</div></div><div className="signal-state" style={{ color: antenna.color }}>{antenna.state}</div></button>)}</div>
    <div className="signal-detail"><div><span>BAND / FREQ</span><strong>{antennas[selected].band} · {antennas[selected].freq}</strong></div><div><span>BW</span><strong>{antennas[selected].bandwidth}</strong></div><div><span>RANGE</span><strong>{antennas[selected].range}</strong></div><div><span>NET</span><strong>{antennas[selected].network}</strong></div></div>
  </section>;
}

const stageData = [
  { number: 3, dv: 2310, fuel: 78.4, thrust: '1,240 kN', layout: [[1, 1], [0, 1], [2, 1], [1, 0]], shape: 'cluster' },
  { number: 2, dv: 1480, fuel: 42.7, thrust: '640 kN', layout: [[1, 0], [0, 1], [1, 1], [2, 1]], shape: 'asym' },
  { number: 1, dv: 620, fuel: 12.1, thrust: '220 kN', layout: [[1, 0], [1, 1]], shape: 'single' }
];

function StageIndicator() {
  const [stageIndex, setStageIndex] = useState(0);
  const [separating, setSeparating] = useState(false);
  const [position, startDrag] = useDragPosition();
  const stage = stageData[stageIndex];
  const separate = () => {
    if (separating || stageIndex >= stageData.length - 1) return;
    setSeparating(true);
    window.setTimeout(() => { setStageIndex(index => index + 1); setSeparating(false); }, 850);
  };
  return <section className={`stage-indicator panel ${separating ? 'stage-separating' : ''}`} style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="STAGE SYSTEM" />
    <div className="stage-head"><span>STAGE SYSTEM</span><strong>STG {stage.number}</strong><button onClick={separate} disabled={separating || stageIndex >= stageData.length - 1}>{separating ? 'SEPARATING' : 'SEPARATE'}</button></div>
    <div className="stage-content">
      <div className="stage-rocket">
        <div className="rocket-body"><i /><i /><i /></div>
        <div className="engine-array">{stage.layout.map(([x, y], i) => <b key={i} className={`engine engine-${stage.shape}`} style={{ left: `${x * 26 + 2}px`, top: `${y * 22 + 2}px` }} />)}</div>
      </div>
      <div className="stage-data"><div><span>DV</span><strong>{stage.dv.toLocaleString()} <small>m/s</small></strong></div><div><span>FUEL</span><strong>{stage.fuel.toFixed(1)}<small>%</small></strong></div><div><span>THRUST</span><strong>{stage.thrust}</strong></div><div className="stage-meter"><i style={{ width: `${stage.fuel}%` }} /></div></div>
    </div>
    <div className="stage-steps">{stageData.map((item, index) => <i key={item.number} className={index === stageIndex ? 'current' : index < stageIndex ? 'spent' : ''}>{item.number}</i>)}</div>
  </section>;
}

function StagingSequence() {
  const [position, startDrag] = useDragPosition();
  const [active, setActive] = useState(1);
  const [firing, setFiring] = useState(false);
  const stages = [
    { id: 2, dv: '0 m/s', engines: ['SEPR', 'SEPR'], resources: [] },
    { id: 1, dv: '62 m/s', engines: ['ENGINE', 'ENGINE', 'ENGINE', 'ENGINE'], resources: ['LIQUID FUEL', 'OXIDIZER', 'LIQUID FUEL', 'MONOPROPELLANT'] },
    { id: 0, dv: '1,480 m/s', engines: ['ENGINE'], resources: ['LIQUID FUEL', 'OXIDIZER'] }
  ];
  const activate = stageId => { if (firing) return; setFiring(true); window.setTimeout(() => { setActive(stageId); setFiring(false); }, 650); };
  return <section className="staging-sequence panel" style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="STAGING SEQUENCE" />
    <div className="staging-header"><span>STAGING</span><small>SEQUENCE</small><b>{firing ? 'TRIGGERING' : 'ARMED'}</b></div>
    <div className="staging-list">{stages.map(stage => <div key={stage.id} className={`staging-card ${active === stage.id ? 'active' : ''} ${firing && active === stage.id ? 'staging-fire' : ''}`}>
      <div className="staging-card-head"><strong>{stage.id}</strong><span>{stage.dv}</span><button onClick={() => activate(stage.id)}>▶</button></div>
      <div className="staging-card-body"><div className="staging-engines">{stage.engines.map((engine, index) => <button key={index} className={active === stage.id ? 'engine-button active' : 'engine-button'} title={engine}>◒</button>)}</div>{stage.resources.length > 0 && <div className="staging-resources">{stage.resources.map((resource, index) => <span key={`${resource}-${index}`}><i />{resource}</span>)}</div>}</div>
    </div>)}</div>
    <div className="staging-footer"><span>STAGE ORDER</span><strong>3 → 2 → 1 → 0</strong></div>
  </section>;
}

function SasDial({ active, onSelect }) {
  const [position, startDrag] = useDragPosition();
  const modes = ['N', 'PRO', 'RET', 'S', 'ANT', 'TGT'];
  return <section className="sas-dial panel" style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title="SAS CONTROL" />
    <div className="panel-title">SAS CONTROL</div>
    <div className="sas-wheel">
      {modes.map((mode, i) => <button key={mode} className={active === mode ? 'mode active' : 'mode'} style={{ '--i': i }} onClick={() => onSelect(mode)}>{mode}</button>)}
      <div className="ship">△</div>
    </div>
  </section>;
}

function App() {
  const [throttle, setThrottle] = useState(.64);
  const [sas, setSas] = useState('PRO');
  const [tapeShape, setTapeShape] = useState('slim');
  const [visibility, setVisibility] = useState({ elec: true, xy: true, orbit: true, rocket: true, life: true, signal: true, stage: true, staging: true, action: true });
  const toggleComponent = id => setVisibility(value => ({ ...value, [id]: !value[id] }));
  const status = useMemo(() => sas === 'PRO' ? 'SAS / PROGRADE' : 'SAS / ' + sas, [sas]);
  return <main className="cockpit">
    <header className="topbar"><span className="eyebrow">MODULAR FLIGHT PANEL</span><span className="status"><i /> {status}</span><span className="clock">MET 00:14:32</span></header>
    <ComponentBar visibility={visibility} onToggle={toggleComponent} />
    {visibility.elec && <ElectricalSystem />}
    {visibility.xy && <XYChartWidget />}
    {visibility.orbit && <Orbit3DWidget />}
    {visibility.rocket && <Rocket2DWidget />}
    {visibility.life && <LifeSupportWidget />}
    {visibility.signal && <SignalListWidget />}
    {visibility.staging && <StagingSequence />}
    <div className="layout">
      <aside className="left-rail">
        <div className="rail-label">THRUST</div>
        <SegmentedArc value={throttle} color={colors.green} start={220} end={320} label="THR" unit="%" />
        <label className="slider-label">THROTTLE <output>{Math.round(throttle * 100)}%</output><input type="range" min="0" max="1" step=".01" value={throttle} onChange={e => setThrottle(Number(e.target.value))} /></label>
        <div className="mini-panel"><span>STAGE FUEL</span><strong>78.4%</strong><div className="meter"><i style={{ width: '78.4%' }} /></div></div>
        <EcamDial title="G-FORCE" value="2.36" max="15" unit="G" color={colors.green} />
      </aside>
      <section className="center-stage">
        <TapeGauge label="SURFACE" value={356} unit="SPD / m/s" color={colors.yellow} side="speed-tape" verticalSpeed={18} shape={tapeShape} onToggleShape={() => setTapeShape(shape => shape === 'slim' ? 'arc' : 'slim')} />
        <Navball />
        <div className="orbital panel"><span>AP</span> 16,177 m <em>in T-00:00:38</em><br/><span>PE</span> -598,308 m <em>in T-00:32:43</em><b>ORBITAL.INFO</b></div>
        {visibility.stage && <StageIndicator />}
      </section>
      <aside className="right-rail">
        <div className="rail-label">FLIGHT DATA</div>
        <SegmentedArc value={.42} color={colors.blue} start={40} end={140} label="VSI" unit="m/s" />
        <TapeGauge label="GROUND" value={9222} unit="ALT / m" color={colors.pink} side="alt-tape" verticalSpeed={-12} shape={tapeShape} onToggleShape={() => setTapeShape(shape => shape === 'slim' ? 'arc' : 'slim')} />
        <div className="readout panel"><small>ALTITUDE / GROUND</small><strong>9,222</strong><span>m</span></div>
        <div className="readout panel"><small>DYNAMIC PRESSURE</small><strong>11.2</strong><span>kPa</span></div>
        <EcamDial title="DYNAMIC PRESSURE" value="11.2" max="40" unit="kPa" color={colors.blue} />
      </aside>
      <SasDial active={sas} onSelect={setSas} />
    </div>
    <StatusStrip />
    {visibility.action && <ActionIndicator />}
    <FooterControls />
  </main>;
}

createRoot(document.getElementById('root')).render(<StrictMode><App /></StrictMode>);

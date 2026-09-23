import { StrictMode, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';
import { colors } from './components/shared.jsx';
import { ActionIndicator } from './components/ActionIndicator.jsx';
import { ElectricalSystem } from './components/ElectricalSystem.jsx';
import { ComponentBar } from './components/ComponentBar.jsx';
import { FooterControls } from './components/FooterControls.jsx';
import { SasDial } from './components/SasDial.jsx';
import { StatusStrip } from './components/StatusStrip.jsx';
import { EcamDial } from './components/EcamDial.jsx';
import { Navball } from './components/Navball.jsx';
import { SegmentedArc } from './components/SegmentedArc.jsx';
import { TapeGauge } from './components/TapeGauge.jsx';
import { LifeSupportWidget } from './components/LifeSupportWidget.jsx';
import { Orbit3DWidget } from './components/Orbit3DWidget.jsx';
import { Rocket2DWidget } from './components/Rocket2DWidget.jsx';
import { SignalListWidget } from './components/SignalListWidget.jsx';
import { XYChartWidget } from './components/XYChartWidget.jsx';
import { StageIndicator } from './components/StageIndicator.jsx';
import { StagingSequence } from './components/StagingSequence.jsx';

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
        <EcamDial title="G-FORCE" value="2.36" max="15" unit="G" color={colors.green} limitType="soft" />
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
        <EcamDial title="DYNAMIC PRESSURE" value="11.2" max="40" unit="kPa" color={colors.blue} limitType="hard" />
      </aside>
      <SasDial active={sas} onSelect={setSas} />
    </div>
    <StatusStrip />
    {visibility.action && <ActionIndicator />}
    <FooterControls />
  </main>;
}

createRoot(document.getElementById('root')).render(<StrictMode><App /></StrictMode>);

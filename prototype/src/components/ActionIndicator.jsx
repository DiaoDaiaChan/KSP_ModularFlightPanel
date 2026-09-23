import { useState } from 'react';
import { colors, Panel } from './shared.jsx';
import { ActionScale } from './ActionScale.jsx';
export function ActionIndicator() {
  const [roll, setRoll] = useState(.16), [pitch, setPitch] = useState(.28), [yaw, setYaw] = useState(-.22);
  return <Panel className="action-indicator" title="FLIGHT CONTROLS"><div className="action-header"><span>FLIGHT CONTROLS</span><strong>STG 001</strong><i className="armed-dot" /></div><div className="action-body"><div className="action-column"><ActionScale label="ROLL" value={roll} color={colors.yellow} /><input aria-label="Roll input" type="range" min="-1" max="1" step=".01" value={roll} onChange={e => setRoll(Number(e.target.value))} /><ActionScale label="YAW / RUD" value={yaw} /><input aria-label="Yaw input" type="range" min="-1" max="1" step=".01" value={yaw} onChange={e => setYaw(Number(e.target.value))} /></div><div className="pitch-column"><ActionScale label="PITCH" value={pitch} vertical color={colors.blue} /><input aria-label="Pitch input" type="range" min="-1" max="1" step=".01" value={pitch} onChange={e => setPitch(Number(e.target.value))} /></div><div className="action-mode-list"><button className="mode-active">RCS</button><button>SAS</button><button>TRIM</button></div></div></Panel>;
}

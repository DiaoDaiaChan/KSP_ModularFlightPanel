import { colors } from './shared.jsx';
export function ActionScale({ label, value, vertical = false, color = colors.green }) {
  const percent = Math.round((value + 1) * 50);
  return <div className={`action-scale ${vertical ? 'vertical' : ''}`} style={{ '--action-color': color }}><span className="action-label">{label}</span><div className="scale-track"><i className="scale-center" /><i className="scale-pointer" style={vertical ? { bottom: percent + '%' } : { left: percent + '%' }} /></div><div className="scale-readout">{value > 0 ? '+' : ''}{value.toFixed(2)}</div></div>;
}

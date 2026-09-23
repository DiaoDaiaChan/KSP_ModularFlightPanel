import { useEffect, useRef, useState } from 'react';

export const colors = { green: '#2bff7b', blue: '#7184ff', pink: '#ff43b9', yellow: '#ffe34f' };

export function useDragPosition() {
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

export function DragHandle({ onPointerDown, title }) {
  return <button className="drag-handle" title={`Drag ${title}`} onPointerDown={onPointerDown}>⠿</button>;
}

export function polar(cx, cy, r, deg) {
  const a = (deg - 90) * Math.PI / 180;
  return [cx + r * Math.cos(a), cy + r * Math.sin(a)];
}

export function arcPath(cx, cy, r, start, end) {
  const [sx, sy] = polar(cx, cy, r, start);
  const [ex, ey] = polar(cx, cy, r, end);
  const large = Math.abs(end - start) > 180 ? 1 : 0;
  return `M ${sx} ${sy} A ${r} ${r} 0 ${large} 1 ${ex} ${ey}`;
}

export function Panel({ className, title, children }) {
  const [position, startDrag] = useDragPosition();
  return <section className={`${className} panel`} style={{ transform: `translate(${position.x}px, ${position.y}px)` }}><DragHandle onPointerDown={startDrag} title={title} />{children}</section>;
}


import { useCallback } from 'react';
import type { PointerEvent as ReactPointerEvent } from 'react';

/** A draggable, keyboard-resizable divider between the rails and the centre pane. */
export function Splitter({
  label,
  onDrag,
  onNudge,
}: {
  label: string;
  onDrag: (clientX: number) => void;
  onNudge: (delta: number) => void;
}) {
  const handlePointerDown = useCallback(
    (event: ReactPointerEvent<HTMLDivElement>) => {
      event.preventDefault();
      const move = (moveEvent: PointerEvent) => onDrag(moveEvent.clientX);
      const up = () => {
        window.removeEventListener('pointermove', move);
        window.removeEventListener('pointerup', up);
        document.body.style.cursor = '';
      };

      document.body.style.cursor = 'col-resize';
      window.addEventListener('pointermove', move);
      window.addEventListener('pointerup', up);
    },
    [onDrag],
  );

  return (
    <div
      className="splitter"
      role="separator"
      aria-orientation="vertical"
      aria-label={label}
      tabIndex={0}
      onPointerDown={handlePointerDown}
      onKeyDown={(event) => {
        if (event.key === 'ArrowLeft') {
          event.preventDefault();
          onNudge(-16);
        } else if (event.key === 'ArrowRight') {
          event.preventDefault();
          onNudge(16);
        }
      }}
    />
  );
}

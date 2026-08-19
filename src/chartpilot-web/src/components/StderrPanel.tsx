import { parseHelmStderr } from '../lib/helmStderr';

/**
 * Helm's stderr, verbatim, with `template.yaml:12:3` locations turned into
 * links when ChartPilot can actually show that file.
 */
export function StderrPanel({
  title,
  stderr,
  canNavigate,
  onNavigate,
}: {
  title: string;
  stderr: string;
  canNavigate: (file: string) => boolean;
  onNavigate: (file: string, line: number) => void;
}) {
  const lines = parseHelmStderr(stderr.trimEnd());

  return (
    <div className="error-panel" role="alert">
      <h3>{title}</h3>
      <pre className="stderr">
        {lines.map((segments, lineIndex) => (
          <span key={lineIndex}>
            {segments.map((segment, segmentIndex) => {
              if (segment.link && canNavigate(segment.link.file)) {
                const link = segment.link;
                return (
                  <button
                    key={segmentIndex}
                    type="button"
                    className="stderr-link"
                    onClick={() => onNavigate(link.file, link.line)}
                  >
                    {segment.text}
                  </button>
                );
              }

              return <span key={segmentIndex}>{segment.text}</span>;
            })}
            {'\n'}
          </span>
        ))}
      </pre>
    </div>
  );
}

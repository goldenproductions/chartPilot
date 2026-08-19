/**
 * Helm reports template failures as `template: chart/templates/x.yaml:23:14: ...`.
 * Turning that into a clickable link is what makes the error panel actionable
 * rather than a wall of text.
 */

export interface StderrSegment {
  text: string;
  link?: {
    file: string;
    line: number;
    column?: number;
  };
}

const LOCATION = /([A-Za-z0-9_./\-]+\.(?:yaml|yml|tpl|json)):(\d+)(?::(\d+))?/g;

export function parseHelmStderr(stderr: string): StderrSegment[][] {
  return stderr.replace(/\r\n/g, '\n').split('\n').map(parseLine);
}

function parseLine(line: string): StderrSegment[] {
  const segments: StderrSegment[] = [];
  let cursor = 0;

  LOCATION.lastIndex = 0;
  let match: RegExpExecArray | null;
  while ((match = LOCATION.exec(line)) !== null) {
    if (match.index > cursor) {
      segments.push({ text: line.slice(cursor, match.index) });
    }

    segments.push({
      text: match[0],
      link: {
        file: match[1],
        line: Number.parseInt(match[2], 10),
        column: match[3] ? Number.parseInt(match[3], 10) : undefined,
      },
    });

    cursor = match.index + match[0].length;
  }

  if (cursor < line.length) {
    segments.push({ text: line.slice(cursor) });
  }

  return segments.length > 0 ? segments : [{ text: line }];
}

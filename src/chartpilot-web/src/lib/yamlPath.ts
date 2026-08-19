/**
 * Resolves a Core `YamlPath` (for example
 * `spec.template.spec.containers[0].image`) to a line/column in a YAML
 * document, so clicking a finding can scroll Monaco to the offending node.
 *
 * The walk uses the `yaml` package's document model together with a
 * LineCounter, which keeps the mapping exact even with anchors, block scalars
 * and comments in the text.
 */
import { LineCounter, isMap, isNode, isPair, isScalar, isSeq, parseDocument } from 'yaml';

export type PathSegment = string | number;

export interface YamlPosition {
  line: number;
  column: number;
}

/** Splits `a.b[0].c` into `['a', 'b', 0, 'c']`, mirroring ManifestNavigator. */
export function parseYamlPath(path: string): PathSegment[] {
  const segments: PathSegment[] = [];

  for (const rawPart of path.split('.')) {
    const part = rawPart.trim();
    if (part.length === 0) {
      continue;
    }

    const bracket = part.indexOf('[');
    const name = bracket === -1 ? part : part.slice(0, bracket);
    if (name.length > 0) {
      segments.push(name);
    }

    if (bracket === -1) {
      continue;
    }

    const indexPattern = /\[(\d+)\]/g;
    let match: RegExpExecArray | null;
    while ((match = indexPattern.exec(part)) !== null) {
      segments.push(Number.parseInt(match[1], 10));
    }
  }

  return segments;
}

type Doc = ReturnType<typeof parseDocument>;

function offsetOfExactPath(doc: Doc, segments: PathSegment[]): number | null {
  if (segments.length === 0) {
    const root = doc.contents;
    return isNode(root) && root.range ? root.range[0] : null;
  }

  const last = segments[segments.length - 1];
  const parentPath = segments.slice(0, -1);
  const parent = parentPath.length === 0 ? doc.contents : doc.getIn(parentPath, true);

  // Prefer the *key* position for mapping entries — that is the line a reader
  // expects to land on, not the first line of a nested block.
  if (isMap(parent) && typeof last === 'string') {
    for (const item of parent.items) {
      if (isPair(item) && isScalar(item.key) && String(item.key.value) === last) {
        const key = item.key;
        if (key.range) {
          return key.range[0];
        }
      }
    }
  }

  if (isSeq(parent) && typeof last === 'number') {
    const item = parent.items[last];
    if (isNode(item) && item.range) {
      return item.range[0];
    }
  }

  const node = doc.getIn(segments, true);
  return isNode(node) && node.range ? node.range[0] : null;
}

/**
 * Resolves the deepest prefix of `path` that exists in `yamlText`.
 * Returns null when the document does not parse or the path has no prefix at
 * all — callers then leave the editor where it is instead of jumping to line 1.
 */
export function resolveYamlPath(yamlText: string, path: string | null | undefined): YamlPosition | null {
  if (!yamlText || !path) {
    return null;
  }

  const lineCounter = new LineCounter();
  let doc: Doc;
  try {
    doc = parseDocument(yamlText, { lineCounter, keepSourceTokens: true });
  } catch {
    return null;
  }

  const segments = parseYamlPath(path);

  for (let length = segments.length; length >= 1; length--) {
    let offset: number | null = null;
    try {
      offset = offsetOfExactPath(doc, segments.slice(0, length));
    } catch {
      offset = null;
    }

    if (offset !== null) {
      const position = lineCounter.linePos(offset);
      return { line: position.line, column: position.col };
    }
  }

  return null;
}

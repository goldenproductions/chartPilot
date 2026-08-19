import { useEffect, useState } from 'react';
import { copyToClipboard } from '../lib/hooks';
import { REPORT_MODEL_URI, VALUES_EXPORT_MODEL_URI, WORKFLOW_MODEL_URI } from '../lib/monacoSetup';
import { InlineError, Skeleton } from './common';
import { YamlEditor } from './YamlEditor';

export type ExportKind = 'report' | 'workflow' | 'values';

const EXPORTS: Record<
  ExportKind,
  { title: string; fileName: string; language: 'markdown' | 'yaml'; uri: string }
> = {
  report: {
    title: 'Review report (Markdown)',
    fileName: 'chartpilot-review.md',
    language: 'markdown',
    uri: REPORT_MODEL_URI,
  },
  workflow: {
    title: 'GitHub Actions workflow',
    fileName: 'chartpilot-deploy.yml',
    language: 'yaml',
    uri: WORKFLOW_MODEL_URI,
  },
  values: {
    title: 'Edited values (YAML)',
    fileName: 'values.yaml',
    language: 'yaml',
    uri: VALUES_EXPORT_MODEL_URI,
  },
};

/**
 * Features 8, 9 and 13 — the Markdown review report, the edited values.yaml and
 * the generated GitHub Actions workflow, each shown read-only with copy and
 * download actions so they can go straight into a repository or a pull request.
 */
export function TextExportDialog({
  kind,
  text,
  isLoading,
  error,
  onClose,
  onRetry,
}: {
  kind: ExportKind;
  text: string | undefined;
  isLoading: boolean;
  error: unknown;
  onClose: () => void;
  onRetry: () => void;
}) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  useEffect(() => {
    if (!copied) {
      return;
    }

    const timer = window.setTimeout(() => setCopied(false), 1800);
    return () => window.clearTimeout(timer);
  }, [copied]);

  const { title, fileName, language, uri } = EXPORTS[kind];

  const download = () => {
    if (!text) {
      return;
    }

    const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label={title}>
      <div className="dialog">
        <header>
          <h2>{title}</h2>
          <span style={{ marginLeft: 'auto' }} />
          <button type="button" className="btn" onClick={onClose}>
            Close
          </button>
        </header>

        <div className="content">
          {error ? (
            <InlineError error={error} retry={onRetry} />
          ) : isLoading || text === undefined ? (
            <Skeleton lines={14} />
          ) : (
            <div className="editor-host">
              <YamlEditor value={text} path={uri} language={language} readOnly />
            </div>
          )}
        </div>

        <footer>
          {copied ? (
            <span style={{ marginRight: 'auto', color: 'var(--passed)' }}>Copied</span>
          ) : null}
          <button type="button" className="btn" onClick={download} disabled={!text}>
            Download
          </button>
          <button
            type="button"
            className="btn btn-primary"
            disabled={!text}
            onClick={async () => {
              if (text) {
                setCopied(await copyToClipboard(text));
              }
            }}
          >
            Copy to clipboard
          </button>
        </footer>
      </div>
    </div>
  );
}

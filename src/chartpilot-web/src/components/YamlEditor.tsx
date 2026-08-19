import Editor, { type OnMount } from '@monaco-editor/react';
import type { editor } from 'monaco-editor';
import { useCallback, useEffect, useRef, useState } from 'react';
import { monacoThemeFor } from '../lib/monacoSetup';

export interface YamlEditorProps {
  value: string;
  /** Monaco model URI. Drives which JSON schema the YAML service applies. */
  path: string;
  language?: 'yaml' | 'markdown';
  readOnly?: boolean;
  onChange?: (value: string) => void;
  /** 1-based line to scroll to and highlight; changes take effect on nonce bumps. */
  revealLine?: number | null;
  revealNonce?: number;
}

function usePrefersDark(): boolean {
  const [dark, setDark] = useState(
    () => window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false,
  );

  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const listener = (event: MediaQueryListEvent) => setDark(event.matches);
    media.addEventListener('change', listener);
    return () => media.removeEventListener('change', listener);
  }, []);

  return dark;
}

export function YamlEditor(props: YamlEditorProps) {
  const {
    value,
    path,
    language = 'yaml',
    readOnly = false,
    onChange,
    revealLine = null,
    revealNonce = 0,
  } = props;

  const editorRef = useRef<editor.IStandaloneCodeEditor | null>(null);
  const decorationsRef = useRef<editor.IEditorDecorationsCollection | null>(null);
  const prefersDark = usePrefersDark();

  const revealRef = useRef({ line: revealLine, readOnly });
  revealRef.current = { line: revealLine, readOnly };

  // Applied both when the reveal request changes and when the editor finishes
  // mounting, because Monaco loads asynchronously and may not exist yet when a
  // finding is clicked.
  const applyReveal = useCallback(() => {
    const instance = editorRef.current;
    const decorations = decorationsRef.current;
    if (!instance || !decorations) {
      return;
    }

    const { line: targetLine, readOnly: isReadOnly } = revealRef.current;

    if (targetLine === null || targetLine < 1) {
      decorations.clear();
      return;
    }

    const model = instance.getModel();
    const maxLine = model ? model.getLineCount() : targetLine;
    const line = Math.min(targetLine, maxLine);

    instance.revealLineInCenter(line);
    instance.setPosition({ lineNumber: line, column: 1 });
    decorations.set([
      {
        range: { startLineNumber: line, startColumn: 1, endLineNumber: line, endColumn: 1 },
        options: {
          isWholeLine: true,
          className: 'cp-highlight-line',
          glyphMarginClassName: 'cp-highlight-glyph',
        },
      },
    ]);

    if (!isReadOnly) {
      instance.focus();
    }
  }, []);

  const handleMount: OnMount = (instance) => {
    editorRef.current = instance;
    decorationsRef.current = instance.createDecorationsCollection([]);
    applyReveal();
  };

  // The nonce makes a repeated click on the same finding re-scroll the editor.
  useEffect(() => {
    applyReveal();
  }, [revealLine, revealNonce, value, applyReveal]);

  return (
    <Editor
      value={value}
      path={path}
      language={language}
      theme={monacoThemeFor(prefersDark)}
      onMount={handleMount}
      onChange={(next) => onChange?.(next ?? '')}
      loading={<div className="empty">Loading editor&hellip;</div>}
      options={{
        readOnly,
        domReadOnly: readOnly,
        automaticLayout: true,
        minimap: { enabled: false },
        fontSize: 12.5,
        fontFamily: 'var(--font-mono)',
        lineNumbersMinChars: 3,
        glyphMargin: true,
        tabSize: 2,
        insertSpaces: true,
        renderWhitespace: 'selection',
        scrollBeyondLastLine: false,
        smoothScrolling: true,
        wordWrap: 'off',
        stickyScroll: { enabled: false },
        quickSuggestions: { other: true, comments: false, strings: true },
      }}
    />
  );
}

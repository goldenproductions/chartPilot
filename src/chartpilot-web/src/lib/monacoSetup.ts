/**
 * Monaco is loaded from the local `monaco-editor` package, never from a CDN —
 * ChartPilot is an offline, loopback-only tool.
 *
 * `monaco-yaml` supplies the YAML language service, which is what turns a
 * chart's `values.schema.json` into completion, hover documentation and
 * inline validation in the values editor.
 */
import { loader } from '@monaco-editor/react';
import * as monaco from 'monaco-editor';
import editorWorker from 'monaco-editor/esm/vs/editor/editor.worker?worker';
import jsonWorker from 'monaco-editor/esm/vs/language/json/json.worker?worker';
import { configureMonacoYaml, type MonacoYaml } from 'monaco-yaml';
import yamlWorker from './yaml.worker?worker';

export const VALUES_MODEL_URI = 'file:///chartpilot/values.yaml';
export const MANIFEST_MODEL_URI = 'file:///chartpilot/manifest.yaml';
export const REPORT_MODEL_URI = 'file:///chartpilot/report.md';
export const WORKFLOW_MODEL_URI = 'file:///chartpilot/workflow.yml';
export const VALUES_EXPORT_MODEL_URI = 'file:///chartpilot/values-export.yaml';

const VALUES_SCHEMA_URI = 'file:///chartpilot/values.schema.json';

declare global {
  interface Window {
    MonacoEnvironment?: monaco.Environment;
  }
}

let initialised = false;
let yamlSupport: MonacoYaml | null = null;
let activeSchemaJson: string | null = null;

export function initMonaco(): typeof monaco {
  if (initialised) {
    return monaco;
  }

  window.MonacoEnvironment = {
    getWorker(_moduleId: string, label: string) {
      if (label === 'yaml') {
        return new yamlWorker();
      }

      if (label === 'json') {
        return new jsonWorker();
      }

      return new editorWorker();
    },
  };

  loader.config({ monaco });

  yamlSupport = configureMonacoYaml(monaco as unknown as Parameters<typeof configureMonacoYaml>[0], {
    enableSchemaRequest: false,
    completion: true,
    hover: true,
    validate: true,
    format: { singleQuote: false },
    schemas: [],
  });

  initialised = true;
  return monaco;
}

/**
 * Points the YAML language service at the chart's `values.schema.json`.
 * Passing null removes the schema again (chart without one, or workspace closed).
 */
export async function applyValuesSchema(schemaJson: string | null | undefined): Promise<void> {
  initMonaco();

  const normalised = schemaJson && schemaJson.trim().length > 0 ? schemaJson : null;
  if (!yamlSupport || normalised === activeSchemaJson) {
    return;
  }

  activeSchemaJson = normalised;

  let parsed: unknown = null;
  if (normalised) {
    try {
      parsed = JSON.parse(normalised);
    } catch {
      parsed = null;
    }
  }

  const schemas =
    parsed && typeof parsed === 'object'
      ? [
          {
            uri: VALUES_SCHEMA_URI,
            fileMatch: [VALUES_MODEL_URI, '**/values.yaml'],
            schema: parsed as Record<string, unknown>,
          },
        ]
      : [];

  await yamlSupport.update({
    enableSchemaRequest: false,
    completion: true,
    hover: true,
    validate: true,
    format: { singleQuote: false },
    schemas,
  });
}

export function monacoThemeFor(prefersDark: boolean): string {
  return prefersDark ? 'vs-dark' : 'vs';
}

export type { editor } from 'monaco-editor';

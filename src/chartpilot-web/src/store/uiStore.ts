/**
 * Zustand holds UI state ONLY (architecture.md section 8): which resource is
 * selected, which environment/profile is active, the editor toggle, panel
 * sizes and the findings filter. Server data lives in TanStack Query.
 */
import { create } from 'zustand';
import type { Severity } from '../api/types';

export type CenterView = 'values' | 'manifest';
export type MainView = 'review' | 'diff';

export interface RevealRequest {
  /** Which pane the reveal applies to. */
  target: CenterView;
  resourceKey: string | null;
  yamlPath: string | null;
  /** Pre-resolved line, used when the source already knows it (helm stderr). */
  line: number | null;
  /** Bumped on every request so repeat clicks re-trigger the reveal. */
  nonce: number;
}

export interface FindingsFilter {
  severities: Record<Severity, boolean>;
  showPassed: boolean;
  query: string;
}

interface UiState {
  workspaceId: string | null;
  chartPath: string | null;
  selectedResourceKey: string | null;
  activeEnvironment: string | null;
  activeProfileId: string | null;
  centerView: CenterView;
  mainView: MainView;
  leftWidth: number;
  rightWidth: number;
  diffDifferencesOnly: boolean;
  findingsFilter: FindingsFilter;
  reveal: RevealRequest | null;

  openWorkspace: (workspaceId: string, chartPath: string) => void;
  closeWorkspace: () => void;
  selectResource: (key: string | null) => void;
  setEnvironment: (environment: string) => void;
  setProfile: (profileId: string) => void;
  setCenterView: (view: CenterView) => void;
  setMainView: (view: MainView) => void;
  setLeftWidth: (width: number) => void;
  setRightWidth: (width: number) => void;
  setDiffDifferencesOnly: (value: boolean) => void;
  toggleSeverity: (severity: Severity) => void;
  setShowPassed: (value: boolean) => void;
  setFindingsQuery: (query: string) => void;
  requestReveal: (resourceKey: string | null, yamlPath: string | null) => void;
  requestRevealLine: (target: CenterView, line: number) => void;
  clearReveal: () => void;
}

const DEFAULT_FILTER: FindingsFilter = {
  severities: { Critical: true, Warning: true, Info: true },
  showPassed: true,
  query: '',
};

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

export const useUiStore = create<UiState>()((set) => ({
  workspaceId: null,
  chartPath: null,
  selectedResourceKey: null,
  activeEnvironment: null,
  activeProfileId: null,
  centerView: 'values',
  mainView: 'review',
  leftWidth: 280,
  rightWidth: 360,
  diffDifferencesOnly: true,
  findingsFilter: DEFAULT_FILTER,
  reveal: null,

  openWorkspace: (workspaceId, chartPath) =>
    set({
      workspaceId,
      chartPath,
      selectedResourceKey: null,
      activeEnvironment: null,
      centerView: 'values',
      mainView: 'review',
      reveal: null,
    }),

  closeWorkspace: () =>
    set({
      workspaceId: null,
      chartPath: null,
      selectedResourceKey: null,
      activeEnvironment: null,
      reveal: null,
      mainView: 'review',
      centerView: 'values',
    }),

  selectResource: (key) => set({ selectedResourceKey: key }),
  setEnvironment: (environment) => set({ activeEnvironment: environment, reveal: null }),
  setProfile: (profileId) => set({ activeProfileId: profileId }),
  setCenterView: (view) => set({ centerView: view }),
  setMainView: (view) => set({ mainView: view }),
  setLeftWidth: (width) => set({ leftWidth: clamp(width, 200, 520) }),
  setRightWidth: (width) => set({ rightWidth: clamp(width, 260, 640) }),
  setDiffDifferencesOnly: (value) => set({ diffDifferencesOnly: value }),

  toggleSeverity: (severity) =>
    set((state) => ({
      findingsFilter: {
        ...state.findingsFilter,
        severities: {
          ...state.findingsFilter.severities,
          [severity]: !state.findingsFilter.severities[severity],
        },
      },
    })),

  setShowPassed: (value) =>
    set((state) => ({ findingsFilter: { ...state.findingsFilter, showPassed: value } })),

  setFindingsQuery: (query) =>
    set((state) => ({ findingsFilter: { ...state.findingsFilter, query } })),

  requestReveal: (resourceKey, yamlPath) =>
    set((state) => ({
      selectedResourceKey: resourceKey ?? state.selectedResourceKey,
      centerView: resourceKey ? 'manifest' : 'values',
      mainView: 'review',
      reveal: {
        target: resourceKey ? 'manifest' : 'values',
        resourceKey,
        yamlPath,
        line: null,
        nonce: (state.reveal?.nonce ?? 0) + 1,
      },
    })),

  requestRevealLine: (target, line) =>
    set((state) => ({
      centerView: target,
      mainView: 'review',
      reveal: {
        target,
        resourceKey: target === 'manifest' ? state.selectedResourceKey : null,
        yamlPath: null,
        line,
        nonce: (state.reveal?.nonce ?? 0) + 1,
      },
    })),

  clearReveal: () => set({ reveal: null }),
}));

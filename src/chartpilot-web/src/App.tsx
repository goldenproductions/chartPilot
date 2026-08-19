import { useEffect, useRef, useState } from 'react';
import {
  useChart,
  useChecks,
  useDiff,
  useEnvironment,
  useOpenWorkspace,
  useProfiles,
  useReport,
  useReview,
  useValuesExport,
  useValuesFile,
  useWorkflow,
} from './api/queries';
import type { FindingDto, ReviewDto } from './api/types';
import { CentrePane } from './components/CentrePane';
import { ChartOverviewCard } from './components/ChartOverviewCard';
import { DiffView } from './components/DiffView';
import { FindingsPanel } from './components/FindingsPanel';
import { HeaderBar } from './components/HeaderBar';
import { HelmBanner } from './components/HelmBanner';
import { OpenChartDialog } from './components/OpenChartDialog';
import { ResourceExplorer } from './components/ResourceExplorer';
import { ScoreCard } from './components/ScoreCard';
import { Splitter } from './components/Splitter';
import { TextExportDialog, type ExportKind } from './components/TextExportDialog';
import { RENDER_DEBOUNCE_MS, useDebouncedValue } from './lib/hooks';
import { applyValuesSchema } from './lib/monacoSetup';
import { useUiStore } from './store/uiStore';

export function App() {
  const workspaceId = useUiStore((state) => state.workspaceId);
  const chartPath = useUiStore((state) => state.chartPath);
  const activeEnvironment = useUiStore((state) => state.activeEnvironment);
  const activeProfileId = useUiStore((state) => state.activeProfileId);
  const centerView = useUiStore((state) => state.centerView);
  const mainView = useUiStore((state) => state.mainView);
  const selectedResourceKey = useUiStore((state) => state.selectedResourceKey);
  const leftWidth = useUiStore((state) => state.leftWidth);
  const rightWidth = useUiStore((state) => state.rightWidth);
  const diffDifferencesOnly = useUiStore((state) => state.diffDifferencesOnly);
  const findingsFilter = useUiStore((state) => state.findingsFilter);
  const reveal = useUiStore((state) => state.reveal);

  const openWorkspaceInStore = useUiStore((state) => state.openWorkspace);
  const selectResource = useUiStore((state) => state.selectResource);
  const setEnvironment = useUiStore((state) => state.setEnvironment);
  const setProfile = useUiStore((state) => state.setProfile);
  const setCenterView = useUiStore((state) => state.setCenterView);
  const setMainView = useUiStore((state) => state.setMainView);
  const setLeftWidth = useUiStore((state) => state.setLeftWidth);
  const setRightWidth = useUiStore((state) => state.setRightWidth);
  const setDiffDifferencesOnly = useUiStore((state) => state.setDiffDifferencesOnly);
  const toggleSeverity = useUiStore((state) => state.toggleSeverity);
  const setShowPassed = useUiStore((state) => state.setShowPassed);
  const setFindingsQuery = useUiStore((state) => state.setFindingsQuery);
  const requestReveal = useUiStore((state) => state.requestReveal);
  const requestRevealLine = useUiStore((state) => state.requestRevealLine);

  const environmentQuery = useEnvironment();
  const profilesQuery = useProfiles();
  const checksQuery = useChecks(true);
  const chartQuery = useChart(workspaceId);
  const valuesQuery = useValuesFile(workspaceId, activeEnvironment);
  const openMutation = useOpenWorkspace();
  const reportMutation = useReport();
  const workflowMutation = useWorkflow();
  const valuesExportMutation = useValuesExport();

  const [draft, setDraft] = useState('');
  const [seededKey, setSeededKey] = useState<string | null>(null);
  const [openDialogVisible, setOpenDialogVisible] = useState(false);
  const [exportKind, setExportKind] = useState<ExportKind | null>(null);

  const gridRef = useRef<HTMLDivElement | null>(null);
  const lastGoodReview = useRef<ReviewDto | null>(null);

  const chart = chartQuery.data;
  const profiles = profilesQuery.data ?? [];
  const valuesFiles = chart?.valuesFiles ?? [];
  const helmAvailable = environmentQuery.data?.helmAvailable === true;

  // Default the environment to the chart's default values file.
  useEffect(() => {
    if (!chart || activeEnvironment !== null) {
      return;
    }

    const files = chart.valuesFiles ?? [];
    const preferred = files.find((file) => file.isDefault) ?? files[0];
    if (preferred) {
      setEnvironment(preferred.fileName);
    }
  }, [chart, activeEnvironment, setEnvironment]);

  // Default the profile to the first one the server offers.
  useEffect(() => {
    if (activeProfileId === null && profiles.length > 0) {
      setProfile(profiles[0].id);
    }
  }, [activeProfileId, profiles, setProfile]);

  // Seed the editor buffer from the selected values file exactly once per file.
  const seedKey = `${workspaceId ?? ''}:${activeEnvironment ?? ''}`;
  useEffect(() => {
    if (valuesQuery.data && seededKey !== seedKey) {
      setDraft(valuesQuery.data.yaml);
      setSeededKey(seedKey);
    }
  }, [valuesQuery.data, seedKey, seededKey]);

  // Feed the chart's values.schema.json to Monaco's YAML language service.
  useEffect(() => {
    void applyValuesSchema(chart?.valuesSchemaJson ?? null);
  }, [chart?.valuesSchemaJson]);

  useEffect(() => {
    lastGoodReview.current = null;
  }, [workspaceId]);

  const debouncedDraft = useDebouncedValue(draft, RENDER_DEBOUNCE_MS);
  const isSeeded = seededKey === seedKey && workspaceId !== null;

  const reviewQuery = useReview({
    workspaceId,
    environment: activeEnvironment ?? '',
    profileId: activeProfileId ?? '',
    draftValues: debouncedDraft,
    enabled: isSeeded && helmAvailable && activeEnvironment !== null && activeProfileId !== null,
  });

  useEffect(() => {
    if (reviewQuery.data && !reviewQuery.isPlaceholderData) {
      lastGoodReview.current = reviewQuery.data;
    }
  }, [reviewQuery.data, reviewQuery.isPlaceholderData]);

  const review = reviewQuery.data ?? lastGoodReview.current ?? undefined;
  const resources = review?.resources ?? [];
  const findings = review?.findings ?? [];

  const isDebouncing = draft !== debouncedDraft;
  const isRendering = reviewQuery.isFetching || isDebouncing;
  const isStale =
    isRendering || reviewQuery.isPlaceholderData || (reviewQuery.isError && review !== undefined);

  const diffQuery = useDiff(workspaceId, diffDifferencesOnly, mainView === 'diff');

  const handleSelectResource = (key: string) => {
    selectResource(key.length > 0 ? key : null);
    if (key.length > 0) {
      setCenterView('manifest');
    }
  };

  const handleSelectFinding = (finding: FindingDto) => {
    requestReveal(finding.resource ?? null, finding.yamlPath ?? null);
  };

  const runExport = (kind: ExportKind) => {
    if (!workspaceId || !activeEnvironment || !activeProfileId) {
      return;
    }

    setExportKind(kind);
    const input = {
      workspaceId,
      environment: activeEnvironment,
      profileId: activeProfileId,
      chartName: chart?.name,
      environmentNames: valuesFiles
        .map((file) => file.environmentName)
        .filter((name): name is string => Boolean(name)),
    };
    if (kind === 'report') {
      reportMutation.mutate(input);
    } else if (kind === 'workflow') {
      workflowMutation.mutate(input);
    } else {
      valuesExportMutation.mutate(input);
    }
  };

  const exportMutation =
    exportKind === 'workflow'
      ? workflowMutation
      : exportKind === 'values'
        ? valuesExportMutation
        : reportMutation;

  const activeProfile = profiles.find((profile) => profile.id === activeProfileId);

  return (
    <div className="app">
      <HeaderBar
        chart={chart}
        profiles={profiles}
        activeProfileId={activeProfileId}
        onProfileChange={setProfile}
        valuesFiles={valuesFiles}
        activeEnvironment={activeEnvironment}
        onEnvironmentChange={setEnvironment}
        score={review?.score}
        scoreStale={isStale}
        mainView={mainView}
        onMainViewChange={setMainView}
        onOpenChart={() => setOpenDialogVisible(true)}
        onReport={() => runExport('report')}
        onWorkflow={() => runExport('workflow')}
        onExportValues={() => runExport('values')}
        actionsEnabled={workspaceId !== null && activeProfileId !== null}
        helmVersion={review?.helmVersion ?? environmentQuery.data?.helmVersion}
      />

      <HelmBanner
        environment={environmentQuery.data}
        error={environmentQuery.error}
        onRetry={() => void environmentQuery.refetch()}
      />

      <div className="body-grid" ref={gridRef}>
        <aside
          className="rail"
          style={{ width: leftWidth, flex: '0 0 auto' }}
          aria-label="Chart and resources"
        >
          {workspaceId === null ? (
            <div className="empty">
              <h2>No chart</h2>
              <p>Open a chart directory to begin.</p>
            </div>
          ) : (
            <>
              <ChartOverviewCard chart={chart} isLoading={chartQuery.isLoading} />
              <ResourceExplorer
                resources={resources}
                findings={findings}
                selectedKey={selectedResourceKey}
                onSelect={handleSelectResource}
                isLoading={reviewQuery.isLoading && review === undefined}
                isStale={isStale}
              />
            </>
          )}
        </aside>

        <Splitter
          label="Resize the chart rail"
          onDrag={(clientX) => {
            const rect = gridRef.current?.getBoundingClientRect();
            if (rect) {
              setLeftWidth(clientX - rect.left);
            }
          }}
          onNudge={(delta) => setLeftWidth(leftWidth + delta)}
        />

        {mainView === 'diff' ? (
          <DiffView
            diff={diffQuery.data}
            isLoading={diffQuery.isLoading}
            isFetching={diffQuery.isFetching}
            error={diffQuery.error}
            differencesOnly={diffDifferencesOnly}
            onDifferencesOnlyChange={setDiffDifferencesOnly}
            onRetry={() => void diffQuery.refetch()}
          />
        ) : (
          <CentrePane
            centerView={centerView}
            onCenterViewChange={setCenterView}
            environmentName={activeEnvironment}
            draft={draft}
            onDraftChange={setDraft}
            valuesLoading={valuesQuery.isLoading}
            resources={resources}
            selectedResourceKey={selectedResourceKey}
            onSelectResource={handleSelectResource}
            isFetching={isRendering}
            isStale={isStale}
            error={reviewQuery.error ?? valuesQuery.error}
            reveal={reveal}
            onRevealLine={requestRevealLine}
            hasWorkspace={workspaceId !== null}
            helmAvailable={helmAvailable}
          />
        )}

        <Splitter
          label="Resize the findings rail"
          onDrag={(clientX) => {
            const rect = gridRef.current?.getBoundingClientRect();
            if (rect) {
              setRightWidth(rect.right - clientX);
            }
          }}
          onNudge={(delta) => setRightWidth(rightWidth - delta)}
        />

        <aside
          className="rail"
          style={{ width: rightWidth, flex: '0 0 auto' }}
          aria-label="Findings and score"
        >
          <FindingsPanel
            findings={findings}
            passed={review?.passed ?? []}
            suppressed={review?.suppressed ?? []}
            checks={checksQuery.data ?? []}
            filter={findingsFilter}
            onToggleSeverity={toggleSeverity}
            onShowPassed={setShowPassed}
            onQueryChange={setFindingsQuery}
            onSelectFinding={handleSelectFinding}
            isLoading={reviewQuery.isLoading && review === undefined}
            isStale={isStale}
            hasReview={review !== undefined}
          />
          <ScoreCard
            score={review?.score}
            isLoading={reviewQuery.isLoading && review === undefined}
            isStale={isStale}
            profileName={activeProfile?.name}
            classification={review?.classification}
            environment={review?.environment ?? activeEnvironment ?? undefined}
          />
        </aside>
      </div>

      {openDialogVisible ? (
        <OpenChartDialog
          initialPath={chartPath}
          isSubmitting={openMutation.isPending}
          error={openMutation.error}
          allowlistRoot={environmentQuery.data?.allowlistRoot}
          onClose={() => {
            openMutation.reset();
            setOpenDialogVisible(false);
          }}
          onSubmit={(path) =>
            openMutation.mutate(path, {
              onSuccess: (workspace) => {
                openWorkspaceInStore(workspace.workspaceId, path);
                setSeededKey(null);
                setDraft('');
                setOpenDialogVisible(false);
              },
            })
          }
        />
      ) : null}

      {exportKind ? (
        <TextExportDialog
          kind={exportKind}
          text={exportMutation.data}
          isLoading={exportMutation.isPending}
          error={exportMutation.error}
          onClose={() => {
            setExportKind(null);
            exportMutation.reset();
          }}
          onRetry={() => runExport(exportKind)}
        />
      ) : null}
    </div>
  );
}

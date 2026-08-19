/**
 * TanStack Query owns ALL server state (architecture.md section 8).
 * Nothing returned from these hooks may be copied into the Zustand store.
 */
import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query';
import * as api from './endpoints';
import type {
  ChartDto,
  CheckDto,
  DiffDto,
  EnvironmentDto,
  ProfileDto,
  ReviewDto,
  ValuesDto,
} from './types';

export const queryKeys = {
  environment: ['environment'] as const,
  profiles: ['profiles'] as const,
  checks: ['checks'] as const,
  chart: (workspaceId: string) => ['workspace', workspaceId, 'chart'] as const,
  values: (workspaceId: string, file: string | null) =>
    ['workspace', workspaceId, 'values', file ?? '(draft)'] as const,
  review: (workspaceId: string, environment: string, profileId: string, draft: string) =>
    ['workspace', workspaceId, 'review', environment, profileId, draft] as const,
  diff: (workspaceId: string, differencesOnly: boolean) =>
    ['workspace', workspaceId, 'diff', differencesOnly] as const,
};

export function useEnvironment(): UseQueryResult<EnvironmentDto> {
  return useQuery({
    queryKey: queryKeys.environment,
    queryFn: ({ signal }) => api.getEnvironment(signal),
    staleTime: 60_000,
    retry: false,
  });
}

export function useProfiles(): UseQueryResult<ProfileDto[]> {
  return useQuery({
    queryKey: queryKeys.profiles,
    queryFn: ({ signal }) => api.getProfiles(signal),
    staleTime: Infinity,
    retry: false,
  });
}

export function useChecks(enabled: boolean): UseQueryResult<CheckDto[]> {
  return useQuery({
    queryKey: queryKeys.checks,
    queryFn: ({ signal }) => api.getChecks(signal),
    staleTime: Infinity,
    retry: false,
    enabled,
  });
}

export function useOpenWorkspace(): UseMutationResult<ChartDto, Error, string> {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (chartPath: string) => api.openWorkspace(chartPath),
    onSuccess: (chart) => {
      client.setQueryData(queryKeys.chart(chart.workspaceId), chart);
    },
  });
}

export function useChart(workspaceId: string | null): UseQueryResult<ChartDto> {
  return useQuery({
    queryKey: queryKeys.chart(workspaceId ?? ''),
    queryFn: ({ signal }) => api.getChart(workspaceId as string, signal),
    enabled: workspaceId !== null,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** The on-disk content of a values file — the seed for the editor buffer. */
export function useValuesFile(
  workspaceId: string | null,
  file: string | null,
): UseQueryResult<ValuesDto> {
  return useQuery({
    queryKey: queryKeys.values(workspaceId ?? '', file),
    queryFn: ({ signal }) => api.getValues(workspaceId as string, file, signal),
    enabled: workspaceId !== null,
    staleTime: Infinity,
    retry: false,
  });
}

export interface PipelineInput {
  workspaceId: string | null;
  /** The values file the user picked in the environment dropdown. */
  environment: string;
  profileId: string;
  /** Debounced editor buffer. A change to this key cancels the in-flight run. */
  draftValues: string;
  enabled: boolean;
}

/**
 * The live pipeline. The review request carries the draft it belongs to, so the
 * result can never describe a different buffer than the one in the query key:
 * aborting a superseded request does not roll back a PUT that already landed, so
 * relying on workspace state here made fast typing race with itself. The PUT is
 * still issued, because it is what validates the buffer against
 * `values.schema.json` — but it no longer decides what gets reviewed.
 */
export function useReview(input: PipelineInput): UseQueryResult<ReviewDto> {
  const { workspaceId, environment, profileId, draftValues, enabled } = input;

  return useQuery({
    queryKey: queryKeys.review(workspaceId ?? '', environment, profileId, draftValues),
    queryFn: async ({ signal }) => {
      const id = workspaceId as string;
      await api.putValues(id, draftValues, signal);
      return api.reviewChart(
        id,
        {
          valuesFiles: environment ? [environment] : undefined,
          profileId,
          draftValues,
        },
        signal,
      );
    },
    enabled: enabled && workspaceId !== null,
    placeholderData: keepPreviousData,
    staleTime: Infinity,
    gcTime: 60_000,
    retry: false,
  });
}

export function useDiff(
  workspaceId: string | null,
  differencesOnly: boolean,
  enabled: boolean,
): UseQueryResult<DiffDto> {
  return useQuery({
    queryKey: queryKeys.diff(workspaceId ?? '', differencesOnly),
    queryFn: ({ signal }) => api.getDiff(workspaceId as string, differencesOnly, signal),
    enabled: enabled && workspaceId !== null,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export interface TextExportInput {
  workspaceId: string;
  /** The active values file. */
  environment: string;
  profileId: string;
  /** Environment names discovered on the chart, for the workflow's choice input. */
  environmentNames?: string[];
  chartName?: string;
}

export function useReport(): UseMutationResult<string, Error, TextExportInput> {
  return useMutation({
    mutationFn: ({ workspaceId, environment, profileId }) =>
      api.getReport(workspaceId, {
        valuesFiles: environment ? [environment] : undefined,
        profileId,
      }),
  });
}

/** Feature 9 — hand the edited values.yaml back to the user as a file. */
export function useValuesExport(): UseMutationResult<string, Error, TextExportInput> {
  return useMutation({
    mutationFn: ({ workspaceId }) => api.exportValues(workspaceId),
  });
}

export function useWorkflow(): UseMutationResult<string, Error, TextExportInput> {
  return useMutation({
    mutationFn: ({ workspaceId, profileId, environmentNames, chartName }) =>
      api.getWorkflow(workspaceId, {
        profileId,
        environments: environmentNames,
        chartName,
        chartPath: chartName ? `./${chartName}` : undefined,
      }),
  });
}

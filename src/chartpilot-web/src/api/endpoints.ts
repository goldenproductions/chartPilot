/**
 * One typed function per route in the architecture.md section 7 table.
 * Nothing in here caches — that is TanStack Query's job.
 */
import { request } from './client';
import type {
  ChartDto,
  CheckDto,
  DiffDto,
  DirectoryListingDto,
  EnvironmentDto,
  ProfileDto,
  RenderDto,
  RenderRequest,
  ReviewDto,
  ReviewRequest,
  ValuesDto,
  ValuesUpdateDto,
  WorkflowRequest,
} from './types';

export function getEnvironment(signal?: AbortSignal): Promise<EnvironmentDto> {
  return request<EnvironmentDto>('/environment', { signal });
}

export function browseDirectory(
  path: string | null,
  signal?: AbortSignal,
): Promise<DirectoryListingDto> {
  const query = path ? `?path=${encodeURIComponent(path)}` : '';
  return request<DirectoryListingDto>(`/browse${query}`, { signal });
}

export function openWorkspace(chartPath: string, signal?: AbortSignal): Promise<ChartDto> {
  return request<ChartDto>('/workspaces', {
    method: 'POST',
    body: { chartPath },
    signal,
  });
}

export function getChart(workspaceId: string, signal?: AbortSignal): Promise<ChartDto> {
  return request<ChartDto>(`/workspaces/${encodeURIComponent(workspaceId)}`, { signal });
}

export function getValues(
  workspaceId: string,
  file?: string | null,
  signal?: AbortSignal,
): Promise<ValuesDto> {
  return request<ValuesDto>(`/workspaces/${encodeURIComponent(workspaceId)}/values`, {
    // Asking for a named file explicitly means "the file on disk", never the draft.
    query: { file: file ?? undefined, draft: file ? false : undefined },
    signal,
  });
}

export function putValues(
  workspaceId: string,
  yaml: string,
  signal?: AbortSignal,
): Promise<ValuesUpdateDto> {
  return request<ValuesUpdateDto>(`/workspaces/${encodeURIComponent(workspaceId)}/values`, {
    method: 'PUT',
    body: { yaml },
    signal,
  });
}

/** The edited draft (or a named file) as YAML text, ready to be saved next to the chart. */
export function exportValues(
  workspaceId: string,
  file?: string | null,
  signal?: AbortSignal,
): Promise<string> {
  return request<string>(`/workspaces/${encodeURIComponent(workspaceId)}/values/export`, {
    query: { file: file ?? undefined },
    text: true,
    signal,
  });
}

export function renderChart(
  workspaceId: string,
  body: RenderRequest,
  signal?: AbortSignal,
): Promise<RenderDto> {
  return request<RenderDto>(`/workspaces/${encodeURIComponent(workspaceId)}/render`, {
    method: 'POST',
    body,
    signal,
  });
}

export function reviewChart(
  workspaceId: string,
  body: ReviewRequest,
  signal?: AbortSignal,
): Promise<ReviewDto> {
  return request<ReviewDto>(`/workspaces/${encodeURIComponent(workspaceId)}/review`, {
    method: 'POST',
    body,
    signal,
  });
}

export function getDiff(
  workspaceId: string,
  differencesOnly: boolean,
  signal?: AbortSignal,
): Promise<DiffDto> {
  return request<DiffDto>(`/workspaces/${encodeURIComponent(workspaceId)}/diff`, {
    query: { differencesOnly },
    signal,
  });
}

export function getReport(
  workspaceId: string,
  body: ReviewRequest,
  signal?: AbortSignal,
): Promise<string> {
  return request<string>(`/workspaces/${encodeURIComponent(workspaceId)}/report`, {
    method: 'POST',
    body,
    text: true,
    signal,
  });
}

export function getWorkflow(
  workspaceId: string,
  body: WorkflowRequest,
  signal?: AbortSignal,
): Promise<string> {
  return request<string>(`/workspaces/${encodeURIComponent(workspaceId)}/workflow`, {
    method: 'POST',
    body,
    text: true,
    signal,
  });
}

export function getProfiles(signal?: AbortSignal): Promise<ProfileDto[]> {
  return request<ProfileDto[]>('/profiles', { signal });
}

export function getChecks(signal?: AbortSignal): Promise<CheckDto[]> {
  return request<CheckDto[]>('/checks', { signal });
}

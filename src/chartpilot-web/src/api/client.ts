/**
 * Low-level HTTP plumbing: one fetch wrapper that understands RFC 7807
 * ProblemDetails responses, including ChartPilot's `helmStderr` extension
 * member (architecture.md section 7).
 */

export const API_BASE = '/api/v1';

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  /** ChartPilot extension: verbatim stderr from `helm template` / `helm lint`. */
  helmStderr?: string;
  errors?: Record<string, string[]>;
  [extension: string]: unknown;
}

/** A typed error carrying everything the UI needs to render a useful panel. */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail?: string;
  readonly helmStderr?: string;
  readonly validationErrors?: Record<string, string[]>;
  readonly problem?: ProblemDetails;

  constructor(init: {
    status: number;
    title: string;
    detail?: string;
    helmStderr?: string;
    validationErrors?: Record<string, string[]>;
    problem?: ProblemDetails;
  }) {
    super(init.detail?.trim() ? init.detail : init.title);
    this.name = 'ApiError';
    this.status = init.status;
    this.title = init.title;
    this.detail = init.detail;
    this.helmStderr = init.helmStderr;
    this.validationErrors = init.validationErrors;
    this.problem = init.problem;
  }

  /** True when the API is simply not reachable (dotnet not running). */
  get isOffline(): boolean {
    return this.status === 0;
  }
}

function asString(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

export function toProblem(status: number, body: unknown, fallbackTitle: string): ApiError {
  if (body && typeof body === 'object') {
    const problem = body as ProblemDetails;
    const validationErrors =
      problem.errors && typeof problem.errors === 'object' ? problem.errors : undefined;

    return new ApiError({
      status,
      title: asString(problem.title) ?? fallbackTitle,
      detail: asString(problem.detail),
      helmStderr: asString(problem.helmStderr),
      validationErrors,
      problem,
    });
  }

  return new ApiError({
    status,
    title: fallbackTitle,
    detail: asString(body),
  });
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  signal?: AbortSignal;
  /** Read the response as plain text instead of JSON (report / workflow). */
  text?: boolean;
  query?: Record<string, string | number | boolean | undefined | null>;
}

function buildUrl(path: string, query: RequestOptions['query']): string {
  const url = `${API_BASE}${path}`;
  if (!query) {
    return url;
  }

  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      params.set(key, String(value));
    }
  }

  const qs = params.toString();
  return qs ? `${url}?${qs}` : url;
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, signal, text = false, query } = options;

  let response: Response;
  try {
    response = await fetch(buildUrl(path, query), {
      method,
      signal,
      headers: {
        Accept: text ? 'text/plain, text/markdown, application/json' : 'application/json',
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }

    throw new ApiError({
      status: 0,
      title: 'Cannot reach the ChartPilot API',
      detail:
        'The API did not respond on http://127.0.0.1:5080. Start it with ' +
        '`dotnet run --project src/ChartPilot.Api`.',
    });
  }

  const contentType = response.headers.get('content-type') ?? '';
  const isJson = contentType.includes('json');
  const payload = isJson
    ? await response.json().catch(() => undefined)
    : await response.text().catch(() => '');

  if (!response.ok) {
    throw toProblem(response.status, payload, `${response.status} ${response.statusText}`.trim());
  }

  if (text) {
    if (typeof payload === 'string') {
      return payload as unknown as T;
    }

    // Some endpoints wrap generated text in a small JSON envelope.
    if (payload && typeof payload === 'object') {
      const envelope = payload as Record<string, unknown>;
      const value = envelope.content ?? envelope.markdown ?? envelope.yaml ?? envelope.text;
      if (typeof value === 'string') {
        return value as unknown as T;
      }
    }

    return String(payload ?? '') as unknown as T;
  }

  return payload as T;
}

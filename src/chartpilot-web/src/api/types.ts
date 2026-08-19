/**
 * TypeScript mirrors of the ChartPilot API DTOs (src/ChartPilot.Api/Contracts/Dtos.cs).
 *
 * The API serializes .NET enums as strings and property names as camelCase, so every
 * enum below is a string union. Nothing here invents data the server does not send.
 */

export type Severity = 'Info' | 'Warning' | 'Critical';

export type CheckCategory = 'Security' | 'Reliability' | 'Operability' | 'Governance';

export type ResourceCategory =
  | 'Workloads'
  | 'Networking'
  | 'Security'
  | 'Certificates'
  | 'Configuration'
  | 'Scaling'
  | 'Other';

export type DataClassification =
  | 'Unclassified'
  | 'Public'
  | 'Internal'
  | 'Confidential'
  | 'SensitivePersonalData';

export type Exposure = 'Unknown' | 'Internal' | 'Public';

export const SEVERITY_ORDER: Severity[] = ['Critical', 'Warning', 'Info'];

export const CHECK_CATEGORIES: CheckCategory[] = [
  'Security',
  'Reliability',
  'Operability',
  'Governance',
];

export const RESOURCE_CATEGORY_ORDER: ResourceCategory[] = [
  'Workloads',
  'Networking',
  'Security',
  'Certificates',
  'Configuration',
  'Scaling',
  'Other',
];

/** `GET /environment` */
export interface EnvironmentDto {
  helmAvailable: boolean;
  helmPath?: string | null;
  helmVersion?: string | null;
  helmError?: string | null;
  resolutionSource?: string | null;
  allowlistRoot?: string | null;
  chartPilotVersion?: string | null;
}

export interface ChartMaintainerDto {
  name: string;
  email?: string | null;
  url?: string | null;
}

export interface ChartDependencyDto {
  name: string;
  version?: string | null;
  repository?: string | null;
  condition?: string | null;
  tags?: string[];
  isVersionPinned?: boolean;
}

export interface ValuesFileDto {
  fileName: string;
  environmentName?: string | null;
  isDefault: boolean;
}

export interface TemplateFileDto {
  relativePath: string;
  detectedKinds?: string[];
}

/**
 * `POST /workspaces` and `GET /workspaces/{id}`.
 * The API returns the chart overview with the workspace id folded in — there is no
 * separate workspace envelope.
 */
export interface ChartDto {
  workspaceId: string;
  chartPath: string;
  name: string;
  version: string;
  appVersion?: string | null;
  description?: string | null;
  type?: string | null;
  kubeVersion?: string | null;
  maintainers?: ChartMaintainerDto[];
  dependencies?: ChartDependencyDto[];
  valuesFiles?: ValuesFileDto[];
  hasValuesSchema: boolean;
  valuesSchemaJson?: string | null;
  templates?: TemplateFileDto[];
  detectedKinds?: string[];
  hasSuppressionsFile?: boolean;
  hasDraft?: boolean;
  selectedValuesFiles?: string[];
  createdAt?: string;
}

/** One rendered Kubernetes resource. */
export interface ResourceDto {
  apiVersion: string;
  apiGroup?: string;
  kind: string;
  name: string;
  namespace?: string | null;
  sourceTemplate?: string | null;
  category: ResourceCategory;
  yaml: string;
}

/**
 * A finding. `resource` is the `Kind/Name` key of the offending resource, or null for a
 * chart-level finding.
 */
export interface FindingDto {
  checkId: string;
  title?: string | null;
  category?: CheckCategory | null;
  severity: Severity;
  resource?: string | null;
  kind?: string | null;
  name?: string | null;
  message: string;
  remediation: string;
  yamlPath?: string | null;
  sourceTemplate?: string | null;
  /** Why the rule exists at all. */
  rationale?: string | null;
  /** The finding restated without jargon. */
  whatItMeans?: string | null;
  /** Why this severity, when the profile or classification moved it. */
  severityReason?: string | null;
  /** Ways to resolve it, most-recommended first. */
  options?: FixOptionDto[];
}

export interface FixOptionDto {
  title: string;
  summary: string;
  yaml: string;
  tradeoff: string;
  isRecommended: boolean;
}

export interface PassedCheckDto {
  checkId: string;
  title: string;
  category: CheckCategory;
  resource?: string | null;
}

export interface SuppressedFindingDto {
  finding: FindingDto;
  reason: string;
  expires?: string | null;
}

export interface CategoryScoreDto {
  category: CheckCategory;
  score: number;
  criticalCount: number;
  warningCount: number;
  infoCount: number;
  passedCount: number;
}

export interface ScoreDto {
  overall: number;
  categories: CategoryScoreDto[];
}

/** `POST /workspaces/{id}/render` */
export interface RenderDto {
  workspaceId: string;
  resourceCount: number;
  resources: ResourceDto[];
  rawManifests: string;
  helmStdErr?: string | null;
}

/** `POST /workspaces/{id}/review` */
export interface ReviewDto {
  workspaceId: string;
  chartName: string;
  chartVersion: string;
  environment: string;
  profileId: string;
  classification: DataClassification;
  score: ScoreDto;
  criticalCount: number;
  warningCount: number;
  infoCount: number;
  resources: ResourceDto[];
  findings: FindingDto[];
  passed: PassedCheckDto[];
  suppressed: SuppressedFindingDto[];
  helmVersion?: string | null;
  generatedAt: string;
}

export interface DiffCellDto {
  source: string;
  value?: string | null;
  present: boolean;
}

export interface DiffRowDto {
  path: string;
  cells: DiffCellDto[];
  isDifferent: boolean;
}

/** `GET /workspaces/{id}/diff` */
export interface DiffDto {
  sources: string[];
  rows: DiffRowDto[];
}

export interface ProfileRequirementsDto {
  [requirement: string]: boolean | number | string | undefined;
}

/** `GET /profiles` */
export interface ProfileDto {
  id: string;
  name: string;
  description: string;
  requirements?: ProfileRequirementsDto;
  severityOverrides?: Record<string, Severity>;
  disabledChecks?: string[];
  isDefault?: boolean;
}

/** `GET /checks` */
export interface CheckDto {
  id: string;
  title: string;
  category: CheckCategory;
  defaultSeverity: Severity;
  rationale: string;
  remediation: string;
  docsUrl?: string | null;
}

/** `GET /workspaces/{id}/values` */
export interface ValuesDto {
  /** The values file name, or `draft` when the editor buffer is returned. */
  source: string;
  yaml: string;
  isDraft: boolean;
}

export interface ValuesIssueDto {
  path: string;
  message: string;
  keyword?: string | null;
}

/** `PUT /workspaces/{id}/values` */
export interface ValuesUpdateDto {
  stored: boolean;
  isValid: boolean;
  issues: ValuesIssueDto[];
}

/** `POST /workspaces/{id}/render` body. */
export interface RenderRequest {
  releaseName?: string | null;
  valuesFiles?: string[];
  dependencyUpdate?: boolean;
}

/** `POST /workspaces/{id}/review` and `/report` body. */
export interface ReviewRequest extends RenderRequest {
  profileId?: string | null;
  environment?: string | null;
  runLint?: boolean;
  /** The editor buffer this review is of. Omitted means "whatever draft the workspace holds". */
  draftValues?: string | null;
}

/** `POST /workspaces/{id}/workflow` body. */
export interface WorkflowRequest {
  environments?: string[];
  profileId?: string | null;
  failOn?: string | null;
  namespace?: string | null;
  chartPath?: string | null;
  chartName?: string | null;
}

/**
 * The stable identity of a rendered resource, matching `ResourceRef.Key` on the server
 * (`Kind/Name`) so a finding's `resource` string can be compared directly.
 */
export function resourceKey(resource: { kind: string; name: string }): string {
  return `${resource.kind}/${resource.name}`;
}

export interface DirectoryEntryDto {
  name: string;
  path: string;
  isChart: boolean;
}

export interface DirectorySegmentDto {
  name: string;
  path: string;
}

/** GET /browse — paths are relative to the allowlist root, ready to post to /workspaces. */
export interface DirectoryListingDto {
  path: string;
  absolutePath: string;
  allowlistRoot: string;
  parentPath?: string | null;
  isAllowlistRoot: boolean;
  isChart: boolean;
  segments: DirectorySegmentDto[];
  entries: DirectoryEntryDto[];
}

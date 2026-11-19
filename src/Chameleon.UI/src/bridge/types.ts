/**
 * Wire types of the UI ↔ host bridge. Mirrors the C# DTOs in src/Chameleon.App/Bridge/Dto and
 * docs/plan/bridge-protocol.md; change all three together.
 */

export type Language = 'zh' | 'en';
export type Platform = 'win' | 'mac' | 'dev';

/** Error codes reported by the host, plus `disconnected`, which only the UI produces. */
export type ErrorCode =
  | 'invalid_request'
  | 'auth_failed'
  | 'not_ready'
  | 'insufficient_credits'
  | 'rate_limited'
  | 'content_blocked'
  | 'model_unavailable'
  | 'timeout'
  | 'network'
  | 'provider_error'
  | 'no_image'
  | 'cancelled'
  | 'rhino_error'
  | 'not_found'
  | 'internal'
  | 'conflict'
  | 'gone'
  | 'disconnected';

export interface RpcError {
  code: ErrorCode;
  message: string;
  detail?: string;
  retryable?: boolean;
}

export interface AssetRef {
  assetId: string;
  mimeType: string;
  width: number;
  height: number;
  bytes: number;
}

export interface Capabilities {
  supportsMask: boolean;
  /** 0 means unknown / unlimited. */
  maxInputImages: number;
  /** Empty means any. */
  imageSizes: string[];
  /** Empty means any. */
  aspectRatios: string[];
  maxConcurrency: number;
}

export interface ModelInfo {
  id: string;
  displayName: string;
  capabilities: Capabilities;
}

export type BackendKind = 'direct' | 'cloud' | 'dev';

/** Id of the account-mode backend (bridge-protocol.md §4.7). */
export const CLOUD_BACKEND_ID = 'cloud';

export interface BackendInfo {
  id: string;
  kind: BackendKind;
  displayName: string;
  ready: boolean;
  models: ModelInfo[];
  defaultModel: string;
  /** Direct backends whose provider reports a balance through "providers.balance". */
  supportsBalance?: boolean;
  topUpUrl?: string;
  /** Direct backends whose account can be signed in to with "providers.signIn". */
  supportsSignIn?: boolean;
  /** Account name while the stored key came from signing in. */
  signedInAs?: string | null;
}

export interface BackendSelection {
  backends: BackendInfo[];
  activeBackendId: string;
  activeModelId: string;
}

export interface LocalizedText {
  zh: string;
  en: string;
}

export interface StyleTemplate {
  id: string;
  name: LocalizedText;
  prompt: string;
}

export interface Catalog {
  styleTemplates: StyleTemplate[];
  defaultSystemPrompt: string;
  imageSizes: string[];
  aspectRatios: string[];
  defaultImageSize: string;
}

export type BatchMode = 'serial' | 'anchorParallel' | 'independent';

export interface UiSettings {
  language: Language;
  systemPrompt: string | null;
  batch: { mode: BatchMode; maxConsistencyRefs: number };
}

export interface LoginState {
  state: 'pending' | 'failed';
  userCode: string;
  verificationUrl: string;
  expiresIn: number;
  error?: RpcError;
}

/** Account mode state; never carries tokens. */
export interface AccountInfo {
  /** A gateway is configured, so account mode can be used. */
  available: boolean;
  signedIn: boolean;
  email?: string;
  credits?: number;
  login?: LoginState;
}

export interface LedgerItem {
  id: number;
  delta: number;
  balanceAfter: number;
  type: 'redeem' | 'reserve' | 'refund' | 'admin_adjust';
  note?: string;
  createdAt: string;
}

export interface AppState {
  protocolVersion: number;
  appVersion: string;
  platform: Platform;
  rhinoVersion: string;
  /** Folder of the host's log files. */
  logsDirectory: string;
  language: Language;
  backends: BackendInfo[];
  activeBackendId: string;
  activeModelId: string;
  catalog: Catalog;
  account: AccountInfo;
  settings: UiSettings;
}

export type ApiFormat = 'gemini' | 'openai';
export type AuthType = 'bearer' | 'goog';

export interface ProviderDto {
  id: string;
  displayName: string;
  baseUrl: string;
  apiFormat: ApiFormat;
  authType: AuthType;
  models: string[];
  defaultModel: string;
  apiKeyUrl?: string;
  isBuiltIn: boolean;
  isOverridden: boolean;
  enableSearchGrounding: boolean;
  hasKey: boolean;
  /** How the remaining balance is queried; only providers the user adds can set it. */
  balanceApi?: BalanceApi | null;
  /** https page where the user tops up or redeems credit. */
  topUpUrl?: string | null;
  /** True when the user can sign in to the provider's account instead of pasting a key. */
  supportsSignIn?: boolean;
  /** Account name while the stored key came from signing in. */
  signedInAs?: string | null;
}

/** Outcome of "providers.signIn" / "providers.signInVerify" (bridge-protocol.md §4.2). */
export type SignInResult =
  | { status: 'signedIn'; username: string }
  | { status: 'needsTwoFactor'; flowToken: string };

/** Balance protocols the host understands: "oneapi" is One API / new-api's dashboard endpoints. */
export type BalanceApi = 'oneapi';

/** What is left on a provider key (amounts in `currency`); null total/remaining: no limit. */
export interface ProviderBalance {
  remaining: number | null;
  used: number;
  total: number | null;
  currency: string;
}

export type ReferenceRole = 'style' | 'material' | 'object' | 'consistency';

export interface ReferenceParams {
  assetId: string;
  role: ReferenceRole;
  note?: string;
}

export interface GenerateParams {
  backendId: string;
  modelId: string;
  prompt: string;
  addonPrompt?: string;
  systemPrompt?: string | null;
  sourceAssetId?: string;
  maskAssetId?: string;
  references?: ReferenceParams[];
  aspectRatio?: string;
  imageSize: string;
  groupId?: string;
  dependsOn?: string[];
  label?: string;
  parentNodeId?: string;
  deferNode?: boolean;
}

/** Camera of a viewport at capture time (P3); `signature` identifies it. */
export interface CameraState {
  location: [number, number, number];
  target: [number, number, number];
  up: [number, number, number];
  lensLength: number;
  parallel: boolean;
  signature: string;
}

/** How a history node was produced; the source image is the parent node's image. */
export interface Recipe {
  backendId: string;
  modelId: string;
  prompt: string;
  systemPrompt?: string;
  aspectRatio?: string;
  imageSize: string;
  maskAssetId?: string;
  references: { assetId: string; role: ReferenceRole; note?: string }[];
  /** 'inpaint' | 'quick:{id}' | … */
  editKind?: string;
}

export type NodeKind = 'capture' | 'generation' | 'edit' | 'import';

/** One image of the history tree. */
export interface NodeInfo {
  id: string;
  parentId?: string;
  kind: NodeKind;
  asset: AssetRef;
  createdAt: string;
  label?: string;
  starred: boolean;
  recipe?: Recipe;
  camera?: CameraState;
}

/** The current project's history, nodes in the order they were added. */
export interface SessionInfo {
  projectId: string;
  currentNodeId?: string;
  nodes: NodeInfo[];
}

export type JobStateName = 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled';

export interface JobResult {
  assets: AssetRef[];
  text?: string;
  creditsCharged?: number;
  nodeIds?: string[];
}

export interface JobInfo {
  id: string;
  groupId?: string;
  label?: string;
  state: JobStateName;
  createdAt: string;
  result?: JobResult;
  error?: RpcError;
}

export interface BatchResult {
  groupId: string;
  jobs: JobInfo[];
}

/** Every RPC method: its parameters and its result. */
export interface RpcMethods {
  'app.getState': { params: void; result: AppState };
  'settings.update': {
    params: Partial<Pick<UiSettings, 'language' | 'systemPrompt'>> & { batch?: Partial<UiSettings['batch']> };
    result: UiSettings;
  };
  'backend.select': { params: { backendId: string; modelId?: string }; result: BackendSelection };
  'shell.openUrl': { params: { url: string }; result: null };
  'log.write': { params: { level: 'info' | 'warn' | 'error'; message: string }; result: null };
  'providers.list': { params: void; result: ProviderDto[] };
  'providers.save': {
    params: Partial<ProviderDto> & Pick<ProviderDto, 'displayName' | 'baseUrl' | 'models'>;
    result: ProviderDto;
  };
  'providers.delete': { params: { id: string }; result: null };
  'providers.resetBuiltIn': { params: { id: string }; result: ProviderDto };
  'providers.setKey': { params: { id: string; key: string }; result: { hasKey: boolean } };
  'providers.testKey': { params: { id: string; key?: string; modelId?: string }; result: { ok: true } };
  'providers.balance': { params: { id: string }; result: ProviderBalance };
  'providers.signIn': { params: { id: string; username: string; password: string }; result: SignInResult };
  'providers.signInVerify': { params: { id: string; flowToken: string; code: string }; result: SignInResult };
  'providers.signOut': { params: { id: string }; result: null };
  'providers.discoverModels': { params: { id: string }; result: { models: string[]; added: string[]; removed: string[] } };
  'rhino.captureActiveView': { params: { width?: number; height?: number } | void; result: AssetRef };
  'rhino.listNamedViews': { params: void; result: { name: string }[] };
  'rhino.captureNamedView': { params: { name: string; width?: number; height?: number }; result: AssetRef };
  'jobs.submit': { params: GenerateParams; result: JobInfo };
  'jobs.submitBatch': { params: { items: GenerateParams[]; mode: BatchMode }; result: BatchResult };
  'jobs.cancel': { params: { jobId: string } | { groupId: string }; result: null };
  'jobs.list': { params: void; result: JobInfo[] };
  'files.saveImage': { params: { assetId: string; suggestedName?: string }; result: { path: string } | null };
  'files.saveMany': {
    params: { items: { assetId: string; fileName: string }[] };
    result: { folder: string; saved: number } | null;
  };
  'files.pickImages': { params: { multiple: boolean }; result: AssetRef[] };
  'session.get': { params: void; result: SessionInfo };
  'session.addNode': {
    params: { parentId?: string; kind: NodeKind; assetId: string; label?: string; recipe?: Recipe; camera?: CameraState };
    result: NodeInfo;
  };
  'session.updateNode': { params: { id: string; label?: string; starred?: boolean }; result: NodeInfo };
  'session.deleteNode': { params: { id: string; withDescendants: boolean }; result: null };
  'session.setCurrent': { params: { id: string | null }; result: null };
  'session.clear': { params: void; result: null };
  'auth.startLogin': { params: void; result: LoginState };
  'auth.cancelLogin': { params: void; result: null };
  'auth.logout': { params: void; result: null };
  'account.get': { params: { refresh?: boolean } | void; result: AccountInfo };
  'account.redeem': { params: { code: string }; result: { creditsAdded: number; credits: number } };
  'account.ledger': { params: { cursor?: string } | void; result: { items: LedgerItem[]; nextCursor?: string | null } };
  'account.estimate': { params: { modelId: string; imageSize: string; count?: number }; result: { credits: number | null } };
}

export type RpcMethod = keyof RpcMethods;
export type RpcParams<M extends RpcMethod> = RpcMethods[M]['params'];
export type RpcResult<M extends RpcMethod> = RpcMethods[M]['result'];

/** Every event the host pushes, and its payload. */
export interface BridgeEvents {
  'job.updated': JobInfo;
  'backends.changed': BackendSelection;
  'settings.changed': UiSettings;
  'account.changed': AccountInfo;
  'session.changed': SessionInfo;
}

export type BridgeEventName = keyof BridgeEvents;

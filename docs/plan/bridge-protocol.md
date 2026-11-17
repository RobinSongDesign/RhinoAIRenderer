# UI 与宿主的桥接协议

`Chameleon.UI`（WebView 里的前端）与 `Chameleon.App`（.NET 进程内）之间的全部通信都走这份协议。
协议与传输方式无关：消息格式固定，传输可替换。DTO 的 TypeScript 定义放在 `src/Chameleon.UI/src/bridge/types.ts`，C# 定义放在 `src/Chameleon.App/Bridge/Dto/`，两边字段名一致，JSON 用 camelCase。

协议版本号 `protocolVersion = 1`，由 `app.getState` 返回。不兼容的修改要递增版本号。

## 1. 传输

### 1.1 首选：本地回环 HTTP

`LocalServer` 用 `HttpListener` 监听 `http://127.0.0.1:{port}/`，端口在启动时随机选取可用端口。每次启动生成 32 字节随机令牌（十六进制），所有路径都位于 `/{token}/` 之下，令牌不对即返回 404。

| 路径 | 方法 | 作用 |
|---|---|---|
| `/{token}/` 及静态文件 | GET | 前端构建产物（从 `Chameleon.Rhino` 的嵌入资源读取；开发宿主从磁盘目录读取） |
| `/{token}/rpc` | POST | RPC 调用，请求与响应都是 JSON |
| `/{token}/events` | GET | 服务器推送事件（SSE，`text/event-stream`） |
| `/{token}/assets/{assetId}` | GET | 图片字节；`Content-Type` 为实际格式；`Cache-Control: immutable` |
| `/{token}/assets` | POST | 上传图片，请求体为原始字节，`Content-Type` 为图片类型；返回 `AssetRef` |

安全要求：

- 只绑定 `127.0.0.1`，不绑定 `localhost` 或 `0.0.0.0`。
- 校验 `Host` 请求头必须是 `127.0.0.1:{port}`，防止 DNS 重绑定。
- 带 `Origin` 请求头时必须等于 `http://127.0.0.1:{port}`，否则 403。
- 不返回任何 CORS 允许头。
- 上传大小上限 64 MB。
- 令牌不写入日志。

### 1.2 备选：Eto 消息通道

仅当 P0 证明 macOS 的 WebView 无法使用回环 HTTP 时才实现。

- UI → 宿主：`window.eto.postMessage(JSON.stringify(envelope))`，宿主在 `WebView.MessageReceived` 中接收。
- 宿主 → UI：`webView.ExecuteScriptAsync("window.__chameleon.receive(" + json + ")")`，用于 RPC 响应和事件。
- 页面：`WebView.LoadHtml(html, baseUri)`，前端产物解压到缓存目录后以该目录为 `baseUri`。
- 图片：下行用缓存目录里的 `file://` 路径；上行把字节分块（每块不超过 512 KB 的 base64）通过 `assets.uploadChunk` 发送。

前端通过 `Transport` 接口屏蔽差异，业务代码只调用 `bridge.call(method, params)`、`bridge.on(event, handler)`、`bridge.assetUrl(assetId)`、`bridge.upload(blob)`：

```ts
export interface Transport {
  call<T>(method: string, params?: unknown, signal?: AbortSignal): Promise<T>;
  on(event: string, handler: (data: unknown) => void): () => void;
  assetUrl(assetId: string): string;
  upload(blob: Blob): Promise<AssetRef>;
}
```

## 2. 消息格式

```ts
// Request (HTTP body of POST /rpc)
interface RpcRequest { id: string; method: string; params?: unknown }

// Response: HTTP status is always 200 for a handled call
interface RpcResponse { id: string; result?: unknown; error?: RpcError }

interface RpcError {
  code: string;          // see section 5
  message: string;       // English, for logs; the UI localizes by code
  detail?: string;       // provider message or stack summary, may be shown in an expandable area
  retryable?: boolean;
}

// Event (one SSE message; `event:` line carries the name, `data:` line carries JSON)
interface BridgeEvent { event: string; data: unknown }
```

事件流建立后服务器每 15 秒发一条注释行保活。前端断线后自动重连，重连成功后调用 `app.getState` 与 `jobs.list` 重新同步。

## 3. 公共类型

```ts
interface AssetRef { assetId: string; mimeType: string; width: number; height: number; bytes: number }

interface ModelInfo { id: string; displayName: string; capabilities: Capabilities; price?: PriceInfo }

interface Capabilities {
  supportsMask: boolean;
  maxInputImages: number;
  imageSizes: string[];
  aspectRatios: string[];
  maxConcurrency: number;
}

interface BackendInfo {
  id: string;                      // "direct:{profileId}" | "cloud"
  kind: 'direct' | 'cloud' | 'dev';   // 'dev' only exists in the development host
  displayName: string;
  ready: boolean;                  // direct: key is set; cloud: signed in
  models: ModelInfo[];
  defaultModel: string;
}

interface GenerateParams {
  backendId: string;
  modelId: string;
  prompt: string;
  addonPrompt?: string;            // appended to prompt after a blank line (per-view prompt in batches)
  systemPrompt?: string | null;    // null/omitted = the user's setting, else the catalog default
  sourceAssetId?: string;
  maskAssetId?: string;
  references?: { assetId: string; role: 'style' | 'material' | 'object' | 'consistency'; note?: string }[];
  aspectRatio?: string;            // '' = keep source ratio
  imageSize: string;
  groupId?: string;                // batch / multi-candidate group
  dependsOn?: string[];            // job ids whose results are appended as consistency references
  label?: string;                  // e.g. named view name
  parentNodeId?: string;           // P3: where the result is attached in the history tree
  deferNode?: boolean;             // P3: host does not create a history node; the UI adds it after compositing
}

// P3: camera of a viewport at capture time; `signature` is a hash of all other fields plus the view aspect ratio
interface CameraState {
  location: [number, number, number];
  target: [number, number, number];
  up: [number, number, number];
  lensLength: number;
  parallel: boolean;
  signature: string;
}

interface JobInfo {
  id: string;
  groupId?: string;
  label?: string;
  state: 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled';
  createdAt: string;               // ISO 8601
  result?: { assets: AssetRef[]; text?: string; creditsCharged?: number; nodeIds?: string[] };
  error?: RpcError;
}
```

## 4. 方法

「阶段」一列是该方法首次实现的阶段。未列出返回值的方法返回 `null`。

### 4.1 应用与设置

| 方法 | 参数 | 返回 | 阶段 |
|---|---|---|---|
| `app.getState` | — | `{ protocolVersion, appVersion, platform: 'win'\|'mac'\|'dev', rhinoVersion, language, backends: BackendInfo[], activeBackendId, activeModelId, catalog: Catalog, settings: UiSettings, account?: AccountInfo }` | P2 |
| `settings.update` | `Partial<UiSettings>`（`language`、`systemPrompt`、`batch`） | 更新后的 `UiSettings` | P2 |
| `backend.select` | `{ backendId, modelId? }` | `{ activeBackendId, activeModelId }` | P2 |
| `shell.openUrl` | `{ url }`（只允许 `https:`） | — | P2 |
| `log.write` | `{ level, message }` | — | P2 |

`Catalog` 包含 `styleTemplates: { id, name: { zh, en }, prompt }[]`、`defaultSystemPrompt`、`quickActions`（P3）。

### 4.2 服务商（自带 Key）

| 方法 | 参数 | 返回 | 阶段 |
|---|---|---|---|
| `providers.list` | — | `ProviderDto[]`（含 `hasKey: boolean`，不含 Key） | P2 |
| `providers.save` | `ProviderDto`（无 `id` 表示新增；`isBuiltIn` 的项保存为覆盖配置） | `ProviderDto` | P2 |
| `providers.delete` | `{ id }`（内置项不可删，返回 `invalid_request`） | — | P2 |
| `providers.resetBuiltIn` | `{ id }` | `ProviderDto` | P2 |
| `providers.setKey` | `{ id, key }`（`key` 为空字符串表示清除） | `{ hasKey }` | P2 |
| `providers.testKey` | `{ id, key?, modelId? }`（`key` 省略时用已保存的） | `{ ok: true }` 或抛 `auth_failed` / `network` 等 | P2 |

Key 只有写入方向：没有任何方法会把已保存的 Key 返回给 UI。

### 4.3 Rhino

| 方法 | 参数 | 返回 | 阶段 |
|---|---|---|---|
| `rhino.captureActiveView` | `{ width?, height? }`（省略则用视口尺寸） | `AssetRef` | P2 |
| `rhino.listNamedViews` | — | `{ name: string }[]` | P2 |
| `rhino.captureNamedView` | `{ name, width?, height? }` | `AssetRef` | P2 |
| `rhino.getDocument` | — | `{ id, name, path? }` 或 `null` | P3 |
| `rhino.getCamera` | — | `CameraState`（活动视口） | P3 |
| `rhino.restoreCamera` | `{ camera: CameraState }` | — | P3 |
| `rhino.captureSelectionMask` | `{ width, height }` | `{ asset: AssetRef \| null, camera: CameraState }`（没有选中物体时 `asset` 为 `null`） | P3 |
| `rhino.setViewportWallpaper` | `{ assetId }` | — | P3 |

### 4.4 任务

| 方法 | 参数 | 返回 | 阶段 |
|---|---|---|---|
| `jobs.submit` | `GenerateParams` | `JobInfo` | P2 |
| `jobs.submitBatch` | `{ items: GenerateParams[], mode: 'serial'\|'anchorParallel'\|'independent' }` | `{ groupId, jobs: JobInfo[] }` | P2（`anchorParallel` 在 P3） |
| `jobs.cancel` | `{ jobId }` 或 `{ groupId }` | — | P2 |
| `jobs.list` | — | `JobInfo[]`（本次进程内的全部任务） | P2 |

`jobs.submitBatch` 的三种模式：

- `serial`：与现有行为一致，逐个执行，每个任务的 `dependsOn` 为之前所有任务，失败任务的结果不进入参考。
- `anchorParallel`：第一个任务先执行，其余任务并行执行并只依赖第一个。
- `independent`：互不依赖，全部并行（受并发上限约束）。后端能力不支持多图输入时强制使用此模式。

### 4.5 文件与剪贴板

| 方法 | 参数 | 返回 | 阶段 |
|---|---|---|---|
| `files.saveImage` | `{ assetId, suggestedName? }`（弹出系统保存对话框；扩展名取自图片的实际格式） | `{ path }` 或 `null`（用户取消） | P2 |
| `files.saveMany` | `{ items: { assetId, fileName }[] }`（弹出文件夹选择对话框） | `{ folder, saved: number }` 或 `null` | P2 |
| `files.pickImages` | `{ multiple: boolean }` | `AssetRef[]`（非 PNG/JPEG/WebP 的文件被跳过） | P2 |
| `clipboard.copyImage` | `{ assetId }` | — | P3 |

格式转换（PNG ↔ JPEG）由 UI 在 Canvas 中完成后上传为新资源，再调用保存；宿主只负责把字节写到磁盘。

### 4.6 编辑会话（P3）

| 方法 | 参数 | 返回 |
|---|---|---|
| `session.get` | — | `SessionDto`（当前文档的项目：节点列表、当前节点） |
| `session.addNode` | `{ parentId?, kind, assetId, label?, recipe?, camera? }` | `NodeDto` |
| `session.updateNode` | `{ id, label?, starred? }` | `NodeDto` |
| `session.deleteNode` | `{ id, withDescendants: boolean }` | — |
| `session.setCurrent` | `{ id }` | — |
| `session.clear` | — | — |

生成任务成功时，宿主自动为每张结果图创建节点（父节点为 `GenerateParams.parentNodeId`），并在 `job.updated` 的 `result.nodeIds` 中返回。

### 4.7 账号（P4）

| 方法 | 参数 | 返回 |
|---|---|---|
| `auth.startLogin` | — | `{ userCode, verificationUrl, expiresIn }`（同时用系统浏览器打开验证页面） |
| `auth.cancelLogin` | — | — |
| `auth.logout` | — | — |
| `account.get` | `{ refresh?: boolean }` | `AccountInfo`（`{ signedIn, email?, credits?, plan? }`） |
| `account.redeem` | `{ code }` | `{ creditsAdded, credits }` |
| `account.ledger` | `{ cursor? }` | `{ items: LedgerItem[], nextCursor? }` |
| `account.estimate` | `GenerateParams` | `{ credits: number }` |

登录结果通过 `account.changed` 事件通知，UI 不轮询。

## 5. 错误码

| `code` | 含义 | 来源 |
|---|---|---|
| `invalid_request` | 参数错误 | 宿主校验或 `RenderErrorCode.InvalidRequest` |
| `auth_failed` | Key 无效或登录失效 | `AuthFailed` |
| `not_ready` | 当前后端未设置 Key 或未登录 | 宿主 |
| `insufficient_credits` | 点数不足 | `InsufficientCredits` |
| `rate_limited` | 触发限流 | `RateLimited` |
| `content_blocked` | 被服务商安全策略拦截 | `ContentBlocked` |
| `model_unavailable` | 模型不存在或不可用 | `ModelUnavailable` |
| `timeout` | 超时 | `Timeout` |
| `network` | 网络错误 | `Network` |
| `provider_error` | 服务商返回错误或无法解析 | `ProviderError` |
| `no_image` | 响应中没有图片 | `NoImageReturned` |
| `cancelled` | 已取消 | `OperationCanceledException` |
| `rhino_error` | 截图失败、没有活动文档、命名视图不存在等 | `IRhinoHost` |
| `not_found` | 资源、任务、节点不存在 | 宿主 |
| `internal` | 未预期的异常 | 宿主 |

## 6. 事件

| 事件 | 数据 | 触发时机 | 阶段 |
|---|---|---|---|
| `job.updated` | `JobInfo` | 任务状态变化 | P2 |
| `backends.changed` | `{ backends: BackendInfo[], activeBackendId, activeModelId }` | 服务商增删改、Key 变化、登录状态变化 | P2 |
| `settings.changed` | `UiSettings` | 设置变化 | P2 |
| `rhino.documentChanged` | `{ id, name }` 或 `null` | 活动文档切换、新建、关闭 | P3 |
| `rhino.selectionChanged` | `{ count: number }` | Rhino 中选中物体数量变化（去抖 200 毫秒） | P3 |
| `session.changed` | `SessionDto` | 节点增删改、当前节点变化 | P3 |
| `account.changed` | `AccountInfo` | 登录、登出、点数变化 | P4 |

## 7. 约定

- 时间一律 ISO 8601 UTC 字符串。
- `assetId` 是图片内容的 SHA-256 十六进制串，相同内容得到相同 id。
- 宿主对未知方法返回 `invalid_request`；对未知参数字段忽略。
- 新增方法、新增可选字段不算破坏兼容；删除或改变已有字段的含义需要递增 `protocolVersion`。
- 每新增一个方法，要同时：在 C# 侧注册处理器、在 `types.ts` 加类型、在开发宿主中可用、在本文件登记。

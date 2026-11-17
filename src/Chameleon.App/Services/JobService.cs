using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Chameleon.Core.Catalog;
using Chameleon.Core.Generation;
using Chameleon.Core.Imaging;
using Chameleon.Core.Jobs;
using Chameleon.Core.Settings;

namespace Chameleon.App.Services
{
    /// <summary>
    /// Submitting, cancelling and reporting generation jobs. Translates between asset ids used by
    /// the UI and the images the queue works with.
    /// </summary>
    public sealed class JobService
    {
        /// <summary>Job tag carrying the history node the result belongs under.</summary>
        public const string ParentNodeTag = "parentNodeId";

        /// <summary>Job tag set when the UI creates the history node itself.</summary>
        public const string DeferNodeTag = "deferNode";

        private readonly IGenerationQueue _queue;
        private readonly BackendRegistry _backends;
        private readonly IAssetStore _assets;
        private readonly SettingsEditor _settings;
        private readonly ChameleonCatalog _catalog;
        private readonly EventBus _events;

        // Result images are stored once per job; later snapshots reuse the references.
        private readonly ConcurrentDictionary<string, IReadOnlyList<AssetRef>> _resultAssets =
            new ConcurrentDictionary<string, IReadOnlyList<AssetRef>>(StringComparer.Ordinal);

        /// <summary>Creates the service and starts forwarding queue changes to the UI.</summary>
        public JobService(
            IGenerationQueue queue,
            BackendRegistry backends,
            IAssetStore assets,
            SettingsEditor settings,
            ChameleonCatalog catalog,
            EventBus events)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _events = events ?? throw new ArgumentNullException(nameof(events));

            _queue.JobChanged += (sender, e) => _events.Publish(BridgeEvents.JobUpdated, ToDto(e.Job));
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.Register<GenerateParamsDto, JobInfoDto>("jobs.submit", (p, ct) => Task.FromResult(Submit(p)));
            router.Register<BatchParamsDto, BatchResultDto>("jobs.submitBatch", (p, ct) => Task.FromResult(SubmitBatch(p)));
            router.Register<CancelParams, object?>("jobs.cancel", (p, ct) =>
            {
                Cancel(p);
                return Task.FromResult<object?>(null);
            });
            router.Register("jobs.list", ct => Task.FromResult(List()));
        }

        /// <summary>Queues one generation.</summary>
        public JobInfoDto Submit(GenerateParamsDto parameters)
        {
            IRenderBackend backend = RequireReadyBackend(parameters.BackendId);
            AppSettings settings = _settings.Read();
            GenerationRequest request = BuildRequest(parameters, settings);
            request.Prompt = BatchPlanner.CombinePrompt(request.Prompt, parameters.AddonPrompt);

            var options = new JobOptions
            {
                GroupId = parameters.GroupId,
                Label = parameters.Label,
                MaxConsistencyRefs = settings.Batch.MaxConsistencyRefs
            };
            foreach (string id in parameters.DependsOn ?? new List<string>())
            {
                options.DependsOn.Add(id);
            }

            CopyTags(parameters, options.Tags);
            return ToDto(_queue.Enqueue(backend.Id, request, options));
        }

        /// <summary>Queues a batch of views with the dependencies its mode calls for.</summary>
        public BatchResultDto SubmitBatch(BatchParamsDto parameters)
        {
            if (parameters.Items == null || parameters.Items.Count == 0)
            {
                throw BridgeException.InvalidRequest("A batch needs at least one item.");
            }

            string backendId = parameters.Items[0].BackendId;
            string modelId = parameters.Items[0].ModelId;
            if (parameters.Items.Any(i => i.BackendId != backendId || i.ModelId != modelId))
            {
                throw BridgeException.InvalidRequest("All items of a batch must use the same backend and model.");
            }

            IRenderBackend backend = RequireReadyBackend(backendId);
            AppSettings settings = _settings.Read();
            BatchMode mode = ParseMode(parameters.Mode);

            List<BatchItem> items = parameters.Items
                .Select(i => new BatchItem(BuildRequest(i, settings), i.AddonPrompt, i.Label))
                .ToList();
            IReadOnlyList<PlannedJob> plan = BatchPlanner.Plan(items, mode, backend.GetCapabilities(modelId));
            for (int i = 0; i < plan.Count; i++)
            {
                CopyTags(parameters.Items[i], plan[i].Tags);
            }

            string groupId = Guid.NewGuid().ToString("N");
            IReadOnlyList<GenerationJob> jobs = _queue.EnqueueBatch(backend.Id, plan, groupId, settings.Batch.MaxConsistencyRefs);
            return new BatchResultDto { GroupId = groupId, Jobs = jobs.Select(ToDto).ToList() };
        }

        /// <summary>Cancels a job or a whole group.</summary>
        public void Cancel(CancelParams parameters)
        {
            if (!string.IsNullOrEmpty(parameters.GroupId))
            {
                _queue.CancelGroup(parameters.GroupId!);
            }
            else if (!string.IsNullOrEmpty(parameters.JobId))
            {
                _queue.Cancel(parameters.JobId!);
            }
            else
            {
                throw BridgeException.InvalidRequest("jobs.cancel needs a jobId or a groupId.");
            }
        }

        /// <summary>Lists every job of this session.</summary>
        public IReadOnlyList<JobInfoDto> List()
        {
            return _queue.Snapshot().Select(ToDto).ToList();
        }

        /// <summary>Converts a job snapshot for the UI, storing result images as assets on first sight.</summary>
        public JobInfoDto ToDto(GenerationJob job)
        {
            var dto = new JobInfoDto
            {
                Id = job.Id,
                GroupId = job.GroupId,
                Label = job.Label,
                State = job.State.ToString().ToLowerInvariant(),
                CreatedAt = job.CreatedAt
            };

            if (job.State == JobState.Succeeded && job.Result != null)
            {
                GenerationResult result = job.Result;
                dto.Result = new JobResultDto
                {
                    Assets = _resultAssets.GetOrAdd(job.Id, _ => result.Images.Select(_assets.Put).ToList()),
                    Text = result.Text,
                    CreditsCharged = result.Usage?.CreditsCharged
                };
            }
            else if (job.State == JobState.Failed && job.Error != null)
            {
                dto.Error = BridgeErrorMapper.FromRenderException(job.Error);
            }
            else if (job.State == JobState.Cancelled)
            {
                dto.Error = new RpcError { Code = BridgeErrorCodes.Cancelled, Message = "The job was cancelled." };
            }

            return dto;
        }

        private IRenderBackend RequireReadyBackend(string backendId)
        {
            IRenderBackend backend = _backends.Find(backendId)
                                     ?? throw BridgeException.NotFound("Unknown backend '" + backendId + "'.");
            if (!_backends.IsReady(backend))
            {
                throw new BridgeException(
                    BridgeErrorCodes.NotReady,
                    "Backend '" + backend.DisplayName + "' is not ready: set an API key or sign in first.");
            }

            return backend;
        }

        private GenerationRequest BuildRequest(GenerateParamsDto parameters, AppSettings settings)
        {
            var request = new GenerationRequest
            {
                ModelId = parameters.ModelId ?? string.Empty,
                Prompt = parameters.Prompt ?? string.Empty,
                // An explicit value wins, then the user's own system prompt, then the catalog default.
                SystemPrompt = parameters.SystemPrompt ?? settings.SystemPrompt ?? _catalog.DefaultSystemPrompt,
                AspectRatio = parameters.AspectRatio,
                ImageSize = string.IsNullOrEmpty(parameters.ImageSize) ? _catalog.DefaultImageSize : parameters.ImageSize!
            };

            if (!string.IsNullOrEmpty(parameters.SourceAssetId))
            {
                request.Source = RequireAsset(parameters.SourceAssetId!);
            }

            if (!string.IsNullOrEmpty(parameters.MaskAssetId))
            {
                request.Mask = RequireAsset(parameters.MaskAssetId!);
            }

            foreach (ReferenceParamsDto reference in parameters.References ?? new List<ReferenceParamsDto>())
            {
                if (!Enum.TryParse(reference.Role, true, out ReferenceRole role))
                {
                    throw BridgeException.InvalidRequest("Unknown reference role '" + reference.Role + "'.");
                }

                request.References.Add(new ReferenceImage(RequireAsset(reference.AssetId), role, reference.Note));
            }

            return request;
        }

        private ImageData RequireAsset(string assetId)
        {
            return _assets.TryGet(assetId) ?? throw BridgeException.NotFound("Unknown asset '" + assetId + "'.");
        }

        private static void CopyTags(GenerateParamsDto parameters, IDictionary<string, string> tags)
        {
            if (!string.IsNullOrEmpty(parameters.ParentNodeId))
            {
                tags[ParentNodeTag] = parameters.ParentNodeId!;
            }

            if (parameters.DeferNode)
            {
                tags[DeferNodeTag] = "true";
            }
        }

        private static BatchMode ParseMode(string? mode)
        {
            switch (mode)
            {
                case null:
                case "":
                case BatchSettings.ModeSerial:
                    return BatchMode.Serial;
                case BatchSettings.ModeAnchorParallel:
                    return BatchMode.AnchorParallel;
                case BatchSettings.ModeIndependent:
                    return BatchMode.Independent;
                default:
                    throw BridgeException.InvalidRequest("Unknown batch mode '" + mode + "'.");
            }
        }
    }
}

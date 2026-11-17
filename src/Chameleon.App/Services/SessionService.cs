using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Chameleon.App.Projects;
using Chameleon.Core.Imaging;
using Chameleon.Core.Sessions;

namespace Chameleon.App.Services
{
    /// <summary>The "session.*" methods and the "session.changed" event: the edit history over the bridge.</summary>
    public sealed class SessionService
    {
        private readonly ProjectService _projects;
        private readonly IAssetStore _assets;
        private readonly EventBus _events;

        // Describing an image means reading it; node images never change, so once is enough.
        private readonly ConcurrentDictionary<string, AssetRef> _described = new ConcurrentDictionary<string, AssetRef>(StringComparer.Ordinal);

        /// <summary>Creates the service and starts announcing changes to the current project.</summary>
        public SessionService(ProjectService projects, IAssetStore assets, EventBus events)
        {
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _events = events ?? throw new ArgumentNullException(nameof(events));

            _projects.Changed += (sender, e) =>
            {
                // Results of jobs started in another document land in that document's project.
                if (e.ProjectId == _projects.CurrentProjectId)
                {
                    _events.Publish(BridgeEvents.SessionChanged, Describe());
                }
            };
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.Register("session.get", ct => Task.FromResult(Describe()));
            router.Register<AddNodeParams, NodeDto>("session.addNode", (p, ct) => Task.FromResult(AddNode(p)));
            router.Register<UpdateNodeParams, NodeDto>("session.updateNode", (p, ct) =>
                Task.FromResult(ToDto(_projects.UpdateNode(p.Id, p.Label, p.Starred))));
            router.Register<DeleteNodeParams, object?>("session.deleteNode", (p, ct) =>
            {
                _projects.DeleteNode(p.Id, p.WithDescendants);
                return Task.FromResult<object?>(null);
            });
            router.Register<SetCurrentNodeParams, object?>("session.setCurrent", (p, ct) =>
            {
                _projects.SetCurrent(p.Id);
                return Task.FromResult<object?>(null);
            });
            router.Register<object?>("session.clear", ct =>
            {
                _projects.Clear();
                return Task.FromResult<object?>(null);
            });
        }

        /// <summary>The current project for the UI.</summary>
        public SessionDto Describe()
        {
            SessionSnapshot snapshot = _projects.GetCurrent();
            return new SessionDto
            {
                ProjectId = snapshot.ProjectId,
                CurrentNodeId = snapshot.CurrentNodeId,
                Nodes = snapshot.Nodes.Select(ToDto).ToList()
            };
        }

        private NodeDto AddNode(AddNodeParams parameters)
        {
            if (!AssetIds.IsValid(parameters.AssetId))
            {
                throw BridgeException.InvalidRequest("session.addNode needs the asset id of an image.");
            }

            return ToDto(_projects.AddNode(new NodeSpec
            {
                ParentId = parameters.ParentId,
                Kind = parameters.Kind,
                AssetId = parameters.AssetId,
                Label = parameters.Label,
                Recipe = parameters.Recipe,
                Camera = parameters.Camera
            }));
        }

        private NodeDto ToDto(ImageNode node)
        {
            return new NodeDto
            {
                Id = node.Id,
                ParentId = node.ParentId,
                Kind = node.Kind,
                Asset = DescribeAsset(node.AssetId),
                CreatedAt = node.CreatedAt,
                Label = node.Label,
                Starred = node.Starred,
                Recipe = node.Recipe,
                Camera = node.Camera
            };
        }

        private AssetRef DescribeAsset(string assetId)
        {
            if (_described.TryGetValue(assetId, out AssetRef? known))
            {
                return known;
            }

            ImageData? image = _assets.TryGet(assetId);
            if (image == null)
            {
                // The file went missing behind our back; the UI shows a placeholder for it.
                return new AssetRef { AssetId = assetId };
            }

            return _described.GetOrAdd(assetId, AssetIds.Describe(image));
        }
    }
}

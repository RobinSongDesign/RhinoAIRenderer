using System;
using System.Collections.Generic;
using System.Linq;

namespace Chameleon.Core.Sessions
{
    /// <summary>
    /// A project's history: a forest of image nodes plus the selected node. Every node's parent
    /// exists and there are no cycles. Not thread-safe; the owner serializes access.
    /// </summary>
    public sealed class EditSession
    {
        /// <summary>Version written into new project files.</summary>
        public const int CurrentSchemaVersion = 1;

        // Insertion order is kept so the history reads in the order things happened.
        private readonly List<ImageNode> _order = new List<ImageNode>();
        private readonly Dictionary<string, ImageNode> _nodes = new Dictionary<string, ImageNode>(StringComparer.Ordinal);

        /// <summary>Creates an empty session.</summary>
        public EditSession(string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                throw new ArgumentException("A project id is required.", nameof(projectId));
            }

            ProjectId = projectId;
        }

        /// <summary>Project this history belongs to.</summary>
        public string ProjectId { get; }

        /// <summary>Selected node, or null.</summary>
        public string? CurrentNodeId { get; private set; }

        /// <summary>All nodes in the order they were added.</summary>
        public IReadOnlyList<ImageNode> Nodes => _order;

        /// <summary>Number of nodes.</summary>
        public int Count => _order.Count;

        /// <summary>Finds a node; null when unknown.</summary>
        public ImageNode? Find(string id)
        {
            return id != null && _nodes.TryGetValue(id, out ImageNode? node) ? node : null;
        }

        /// <summary>Adds a node. Its id must be new and its parent, when given, must exist.</summary>
        /// <exception cref="ArgumentException">The id is taken or the parent is unknown.</exception>
        public ImageNode AddNode(ImageNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (string.IsNullOrEmpty(node.Id) || _nodes.ContainsKey(node.Id))
            {
                throw new ArgumentException("Node id '" + node.Id + "' is empty or already used.", nameof(node));
            }

            if (string.IsNullOrEmpty(node.AssetId))
            {
                throw new ArgumentException("A node needs an image.", nameof(node));
            }

            if (node.ParentId != null && !_nodes.ContainsKey(node.ParentId))
            {
                throw new ArgumentException("Unknown parent node '" + node.ParentId + "'.", nameof(node));
            }

            // A new node cannot close a cycle: nothing can point at an id that did not exist.
            _nodes.Add(node.Id, node);
            _order.Add(node);
            return node;
        }

        /// <summary>Changes a node's label and/or favourite flag.</summary>
        /// <exception cref="KeyNotFoundException">The node is unknown.</exception>
        public ImageNode UpdateNode(string id, string? label, bool? starred)
        {
            ImageNode node = Require(id);
            if (label != null)
            {
                node.Label = label.Length == 0 ? null : label;
            }

            if (starred.HasValue)
            {
                node.Starred = starred.Value;
            }

            return node;
        }

        /// <summary>
        /// Removes a node. With <paramref name="withDescendants"/> its whole subtree goes too;
        /// otherwise its children move up to its parent. The selection moves to the parent when it
        /// pointed at a removed node.
        /// </summary>
        /// <returns>The removed nodes.</returns>
        /// <exception cref="KeyNotFoundException">The node is unknown.</exception>
        public IReadOnlyList<ImageNode> DeleteNode(string id, bool withDescendants)
        {
            ImageNode node = Require(id);
            var removed = new List<ImageNode>();
            if (withDescendants)
            {
                removed.AddRange(GetSubtree(id));
            }
            else
            {
                foreach (ImageNode child in GetChildren(id))
                {
                    child.ParentId = node.ParentId;
                }

                removed.Add(node);
            }

            var removedIds = new HashSet<string>(removed.Select(n => n.Id), StringComparer.Ordinal);
            _order.RemoveAll(n => removedIds.Contains(n.Id));
            foreach (string removedId in removedIds)
            {
                _nodes.Remove(removedId);
            }

            if (CurrentNodeId != null && removedIds.Contains(CurrentNodeId))
            {
                CurrentNodeId = node.ParentId;
            }

            return removed;
        }

        /// <summary>Removes every node.</summary>
        /// <returns>The removed nodes.</returns>
        public IReadOnlyList<ImageNode> Clear()
        {
            List<ImageNode> removed = _order.ToList();
            _order.Clear();
            _nodes.Clear();
            CurrentNodeId = null;
            return removed;
        }

        /// <summary>Selects a node, or clears the selection with null.</summary>
        /// <exception cref="KeyNotFoundException">The node is unknown.</exception>
        public void SetCurrent(string? id)
        {
            CurrentNodeId = id == null ? null : Require(id).Id;
        }

        /// <summary>Nodes from the root down to the given node, inclusive.</summary>
        /// <exception cref="KeyNotFoundException">The node is unknown.</exception>
        public IReadOnlyList<ImageNode> GetPath(string id)
        {
            var path = new List<ImageNode>();
            for (ImageNode? node = Require(id); node != null; node = node.ParentId == null ? null : _nodes[node.ParentId])
            {
                path.Add(node);
            }

            path.Reverse();
            return path;
        }

        /// <summary>Direct children of a node (roots for null), in the order they were added.</summary>
        public IReadOnlyList<ImageNode> GetChildren(string? id)
        {
            return _order.Where(n => string.Equals(n.ParentId, id, StringComparison.Ordinal)).ToList();
        }

        /// <summary>Every asset some node still needs.</summary>
        public ISet<string> GetReferencedAssetIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ImageNode node in _order)
            {
                ids.UnionWith(node.GetAssetIds());
            }

            return ids;
        }

        /// <summary>Copies the session into its persisted form.</summary>
        public SessionSnapshot ToSnapshot()
        {
            return new SessionSnapshot
            {
                ProjectId = ProjectId,
                CurrentNodeId = CurrentNodeId,
                Nodes = _order.Select(n => n.Clone()).ToList()
            };
        }

        /// <summary>
        /// Rebuilds a session from its persisted form. A damaged file is repaired rather than
        /// rejected: duplicate or image-less nodes are dropped, and nodes whose parent is missing
        /// (or that form a cycle) become roots, so no image is lost.
        /// </summary>
        public static EditSession FromSnapshot(SessionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var session = new EditSession(snapshot.ProjectId);
            var candidates = new Dictionary<string, ImageNode>(StringComparer.Ordinal);
            var order = new List<ImageNode>();
            foreach (ImageNode? node in snapshot.Nodes ?? new List<ImageNode>())
            {
                if (node != null && !string.IsNullOrEmpty(node.Id) && !string.IsNullOrEmpty(node.AssetId) && !candidates.ContainsKey(node.Id))
                {
                    ImageNode copy = node.Clone();
                    candidates.Add(copy.Id, copy);
                    order.Add(copy);
                }
            }

            foreach (ImageNode node in order)
            {
                if (node.ParentId != null && (!candidates.ContainsKey(node.ParentId) || LeadsToCycle(node, candidates)))
                {
                    node.ParentId = null;
                }
            }

            // Parents first, so AddNode's checks hold whatever order the file had.
            var added = new HashSet<string>(StringComparer.Ordinal);
            while (added.Count < order.Count)
            {
                foreach (ImageNode node in order)
                {
                    if (!added.Contains(node.Id) && (node.ParentId == null || added.Contains(node.ParentId)))
                    {
                        session._nodes.Add(node.Id, node);
                        added.Add(node.Id);
                    }
                }
            }

            session._order.AddRange(order);
            if (snapshot.CurrentNodeId != null && session._nodes.ContainsKey(snapshot.CurrentNodeId))
            {
                session.CurrentNodeId = snapshot.CurrentNodeId;
            }

            return session;
        }

        private static bool LeadsToCycle(ImageNode start, IReadOnlyDictionary<string, ImageNode> nodes)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { start.Id };
            for (string? parent = start.ParentId; parent != null; parent = nodes.TryGetValue(parent, out ImageNode? next) ? next.ParentId : null)
            {
                if (!seen.Add(parent))
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerable<ImageNode> GetSubtree(string id)
        {
            var result = new List<ImageNode>();
            var pending = new Queue<string>();
            pending.Enqueue(id);
            while (pending.Count > 0)
            {
                string next = pending.Dequeue();
                result.Add(_nodes[next]);
                foreach (ImageNode child in _order.Where(n => string.Equals(n.ParentId, next, StringComparison.Ordinal)))
                {
                    pending.Enqueue(child.Id);
                }
            }

            return result;
        }

        private ImageNode Require(string id)
        {
            return Find(id) ?? throw new KeyNotFoundException("Unknown node '" + id + "'.");
        }
    }
}

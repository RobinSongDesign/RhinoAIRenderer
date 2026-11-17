using System;
using System.Collections.Generic;
using System.Linq;
using Chameleon.Core.Generation;
using Chameleon.Core.Serialization;
using Chameleon.Core.Sessions;
using Newtonsoft.Json;
using Xunit;

namespace Chameleon.Core.Tests.Sessions
{
    public class EditSessionTests
    {
        private static ImageNode Node(string id, string? parent = null, string? asset = null, Recipe? recipe = null)
        {
            return new ImageNode
            {
                Id = id,
                ParentId = parent,
                Kind = parent == null ? NodeKind.Capture : NodeKind.Generation,
                AssetId = asset ?? "asset-" + id,
                CreatedAt = DateTimeOffset.UnixEpoch,
                Recipe = recipe
            };
        }

        // a ─ b ─ c
        //   └ d
        private static EditSession Tree()
        {
            var session = new EditSession("p1");
            session.AddNode(Node("a"));
            session.AddNode(Node("b", "a"));
            session.AddNode(Node("c", "b"));
            session.AddNode(Node("d", "a"));
            return session;
        }

        private static string[] Ids(IEnumerable<ImageNode> nodes) => nodes.Select(n => n.Id).ToArray();

        [Fact]
        public void AddNode_RejectsDuplicateIdsUnknownParentsAndMissingImages()
        {
            EditSession session = Tree();

            Assert.Throws<ArgumentException>(() => session.AddNode(Node("a")));
            Assert.Throws<ArgumentException>(() => session.AddNode(Node("x", "nope")));
            Assert.Throws<ArgumentException>(() => session.AddNode(new ImageNode { Id = "y" }));
            Assert.Equal(4, session.Count);
        }

        [Fact]
        public void PathAndChildren_FollowTheTree()
        {
            EditSession session = Tree();

            Assert.Equal(new[] { "a", "b", "c" }, Ids(session.GetPath("c")));
            Assert.Equal(new[] { "b", "d" }, Ids(session.GetChildren("a")));
            Assert.Equal(new[] { "a" }, Ids(session.GetChildren(null)));
            Assert.Throws<KeyNotFoundException>(() => session.GetPath("nope"));
        }

        [Fact]
        public void DeleteNode_WithoutDescendants_MovesChildrenToTheParent()
        {
            EditSession session = Tree();
            session.SetCurrent("b");

            IReadOnlyList<ImageNode> removed = session.DeleteNode("b", withDescendants: false);

            Assert.Equal(new[] { "b" }, Ids(removed));
            Assert.Equal("a", session.Find("c")!.ParentId);
            Assert.Equal(new[] { "a", "c" }, Ids(session.GetPath("c")));
            Assert.Equal("a", session.CurrentNodeId);
        }

        [Fact]
        public void DeleteNode_WithDescendants_RemovesTheSubtree()
        {
            EditSession session = Tree();
            session.SetCurrent("c");

            IReadOnlyList<ImageNode> removed = session.DeleteNode("b", withDescendants: true);

            Assert.Equal(new[] { "b", "c" }, Ids(removed));
            Assert.Equal(new[] { "a", "d" }, Ids(session.Nodes));
            Assert.Equal("a", session.CurrentNodeId);
        }

        [Fact]
        public void DeletingARoot_WithoutDescendants_MakesItsChildrenRoots()
        {
            EditSession session = Tree();

            session.DeleteNode("a", withDescendants: false);

            Assert.Equal(new[] { "b", "d" }, Ids(session.GetChildren(null)));
        }

        [Fact]
        public void UpdateNode_ChangesOnlyWhatIsGiven_AndAnEmptyLabelClearsIt()
        {
            EditSession session = Tree();

            session.UpdateNode("b", "Dusk", null);
            session.UpdateNode("b", null, true);
            Assert.Equal("Dusk", session.Find("b")!.Label);
            Assert.True(session.Find("b")!.Starred);

            session.UpdateNode("b", string.Empty, null);
            Assert.Null(session.Find("b")!.Label);
            Assert.True(session.Find("b")!.Starred);
        }

        [Fact]
        public void ReferencedAssets_IncludeMasksAndReferences()
        {
            var session = new EditSession("p1");
            session.AddNode(Node("a", asset: "s"));
            var recipe = new Recipe { MaskAssetId = "m" };
            recipe.References.Add(new RecipeReference { AssetId = "r", Role = ReferenceRole.Style });
            session.AddNode(Node("b", "a", "out", recipe));

            Assert.Equal(new[] { "m", "out", "r", "s" }, session.GetReferencedAssetIds().OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Snapshot_RoundTripsThroughJson()
        {
            EditSession session = Tree();
            session.SetCurrent("c");
            session.Find("a")!.Camera = new CameraState { Location = new[] { 1.0, 2, 3 }, Signature = "sig" };
            session.Find("b")!.Recipe = new Recipe { Prompt = "dusk", ImageSize = "2K", References = { new RecipeReference { AssetId = "r", Role = ReferenceRole.Material, Note = "oak" } } };

            string json = JsonConvert.SerializeObject(session.ToSnapshot(), CoreJson.Settings);
            EditSession loaded = EditSession.FromSnapshot(JsonConvert.DeserializeObject<SessionSnapshot>(json, CoreJson.Settings)!);

            Assert.Contains("\"schemaVersion\":1", json);
            Assert.Contains("\"kind\":\"generation\"", json);
            Assert.Equal(Ids(session.Nodes), Ids(loaded.Nodes));
            Assert.Equal("c", loaded.CurrentNodeId);
            Assert.Equal("sig", loaded.Find("a")!.Camera!.Signature);
            Assert.Equal(ReferenceRole.Material, loaded.Find("b")!.Recipe!.References[0].Role);
        }

        [Fact]
        public void Snapshot_IsACopy()
        {
            EditSession session = Tree();

            session.ToSnapshot().Nodes[0].Label = "changed";

            Assert.Null(session.Find("a")!.Label);
        }

        [Fact]
        public void FromSnapshot_RepairsDamagedFiles()
        {
            var snapshot = new SessionSnapshot
            {
                ProjectId = "p1",
                CurrentNodeId = "gone",
                Nodes =
                {
                    Node("child", "parent"),   // listed before its parent
                    Node("parent"),
                    Node("orphan", "missing"),
                    Node("x", "y"),            // x and y point at each other
                    Node("y", "x"),
                    Node("parent"),            // duplicate id
                    new ImageNode { Id = "no-image" }
                }
            };

            EditSession session = EditSession.FromSnapshot(snapshot);

            Assert.Equal(new[] { "child", "parent", "orphan", "x", "y" }, Ids(session.Nodes));
            Assert.Equal("parent", session.Find("child")!.ParentId);
            Assert.Null(session.Find("orphan")!.ParentId);
            Assert.True(session.Find("x")!.ParentId == null || session.Find("y")!.ParentId == null);
            Assert.Null(session.CurrentNodeId);
            foreach (ImageNode node in session.Nodes)
            {
                Assert.NotEmpty(session.GetPath(node.Id));
            }
        }
    }
}

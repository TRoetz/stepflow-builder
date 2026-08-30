using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using Newtonsoft.Json;
using StepFlow.DataModel.Entities.DataSource;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.Workspace;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// WorkspaceStore (node CRUD, ACLs + inheritance, flows) and the tree-aware DataExchangeProfileStore:
    /// round-trips, dedupe/merge rules, id derivation, on-disk layout, migration.
    /// </summary>
    public class WorkspaceStoreTests : IDisposable
    {
        private const string Definition = "{\"startAt\":\"Start\",\"states\":{\"Start\":{}}}";

        private readonly string _dir;
        private readonly WorkspaceStore _store;

        public WorkspaceStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "stepflow-workspace-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _store = new WorkspaceStore(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private static AccessEntry Grant(string principal, string role) => new() { Principal = principal, Role = role };

        private static DataExchangeProfile Profile(string name, string? id = null) => new()
        {
            DataExchangeProfileName = name,
            ProfileId = id
        };

        // ── node CRUD ────────────────────────────────────────────────

        [Fact]
        public void CreateNode_BuildsTreeAndListsAtEachLevel()
        {
            Assert.Equal("Acme", _store.CreateNode("Acme"));
            Assert.Equal("Acme/Website", _store.CreateNode("Website", "Acme"));
            Assert.Equal("Acme/Website/Web", _store.CreateNode("Web", "Acme/Website"));

            Assert.Equal(new[] { "Acme" }, _store.ListOrgs());
            Assert.Equal(new[] { "Website" }, _store.ListProjects("Acme"));
            Assert.Equal(new[] { "Web" }, _store.ListSubProjects("Acme", "Website"));
            Assert.True(_store.NodeExists("Acme/Website/Web"));

            // Sub-project nodes seed their content directories from birth.
            var subFull = Path.Combine(_dir, "Acme", "Website", "Web");
            Assert.True(Directory.Exists(Path.Combine(subFull, "flows")));
            Assert.True(Directory.Exists(Path.Combine(subFull, "data-exchange")));
        }

        [Fact]
        public void CreateNode_RejectsDepthBeyondSubProject() =>
            Assert.Throws<InvalidOperationException>(() => _store.CreateNode("TooDeep", "Acme/Website/Web"));

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(".")]
        [InlineData("..")]
        [InlineData("a/b")]
        [InlineData("bad*name")]
        public void CreateNode_RejectsInvalidNames(string name) =>
            Assert.Throws<InvalidOperationException>(() => _store.CreateNode(name));

        [Fact]
        public void CreateNode_RequiresExistingParent() =>
            Assert.Throws<DirectoryNotFoundException>(() => _store.CreateNode("Orphan", "Nope"));

        [Fact]
        public void NodePaths_CannotEscapeRoot()
        {
            Assert.Throws<InvalidOperationException>(() => _store.CreateNode("x", "../outside"));
            Assert.False(_store.NodeExists("../outside"));
        }

        [Fact]
        public void RenameNode_MovesChildrenAndAcls()
        {
            _store.EnsureSubProject("Acme/Website/Web");
            _store.SaveAcl("Acme/Website", new[] { Grant("alice", AccessRole.Owner) });
            _store.SaveFlow("Acme/Website/Web", null, "Order Flow", null, Definition);

            var newPath = _store.RenameNode("Acme/Website", "Storefront");

            Assert.Equal("Acme/Storefront", newPath);
            Assert.False(_store.NodeExists("Acme/Website"));
            Assert.True(_store.NodeExists("Acme/Storefront/Web"));
            // ACL moved with the node.
            Assert.Contains(_store.LoadAcl("Acme/Storefront").Entries, e => e.Principal == "alice");
            // Flows moved too.
            Assert.Single(_store.ListFlows("Acme/Storefront/Web"));
        }

        [Fact]
        public void RenameNode_RejectsExistingTarget()
        {
            _store.CreateNode("One");
            _store.CreateNode("Two");
            Assert.Throws<InvalidOperationException>(() => _store.RenameNode("One", "Two"));
        }

        [Fact]
        public void DeleteNode_RemovesRecursively()
        {
            _store.EnsureSubProject("Acme/Website/Web");
            _store.SaveAcl("Acme", new[] { Grant("bob", AccessRole.Editor) });

            Assert.True(_store.DeleteNode("Acme"));
            Assert.False(_store.NodeExists("Acme/Website/Web"));
            Assert.Empty(_store.ListOrgs());
            Assert.False(Directory.Exists(Path.Combine(_dir, "Acme")));
        }

        [Fact]
        public void DeleteNode_MissingReturnsFalse() => Assert.False(_store.DeleteNode("Ghost"));

        // ── ACLs ─────────────────────────────────────────────────────

        [Fact]
        public void Acl_SaveLoadRoundTrip_NormalizesAndDedupes()
        {
            _store.CreateNode("Acme");
            _store.SaveAcl("Acme", new[]
            {
                Grant(" alice ", AccessRole.Editor),
                Grant("ALICE", "OWNER"),   // duplicate principal (case-insensitive) — higher rank wins
                Grant("bob", "superuser"), // unknown role → viewer
                Grant("", AccessRole.Owner) // blank principal dropped
            });

            var acl = _store.LoadAcl("Acme");
            Assert.Equal(2, acl.Entries.Count);

            var alice = acl.Entries.Single(e => e.Principal == "alice");
            Assert.Equal(AccessRole.Owner, alice.Role);

            var bob = acl.Entries.Single(e => e.Principal == "bob");
            Assert.Equal(AccessRole.Viewer, bob.Role);
        }

        [Fact]
        public void Acl_FileIsCamelCaseJson()
        {
            _store.CreateNode("Acme");
            _store.SaveAcl("Acme", new[] { Grant("alice", AccessRole.Owner) });

            var json = File.ReadAllText(Path.Combine(_dir, "Acme", "access.json"));
            var doc = Newtonsoft.Json.Linq.JObject.Parse(json);
            Assert.Equal("alice", (string)doc["entries"][0]["principal"]);
            Assert.Equal("owner", (string)doc["entries"][0]["role"]);
        }

        [Fact]
        public void Acl_SaveToMissingNodeThrows() =>
            Assert.Throws<DirectoryNotFoundException>(() => _store.SaveAcl("Ghost", new[] { Grant("a", AccessRole.Viewer) }));

        // ── effective grants / inheritance ───────────────────────────

        [Fact]
        public void EffectiveGrants_MergeAncestors_HigherRankWins()
        {
            _store.EnsureSubProject("Acme/Website/Web");
            _store.SaveAcl("Acme", new[] { Grant("alice", AccessRole.Owner), Grant("bob", AccessRole.Editor) });
            _store.SaveAcl("Acme/Website", new[] { Grant("bob", AccessRole.Viewer), Grant("carol", AccessRole.Admin) });

            var grants = _store.GetEffectiveGrants("Acme/Website/Web");

            // alice: owner inherited from the org.
            var alice = grants.Single(g => g.Principal == "alice");
            Assert.Equal(AccessRole.Owner, alice.Role);
            Assert.Equal("Acme", alice.SourcePath);

            // bob: editor (org) beats viewer (project) — higher rank wins regardless of depth.
            var bob = grants.Single(g => g.Principal == "bob");
            Assert.Equal(AccessRole.Editor, bob.Role);
            Assert.Equal("Acme", bob.SourcePath);

            // carol: admin from the project.
            var carol = grants.Single(g => g.Principal == "carol");
            Assert.Equal(AccessRole.Admin, carol.Role);
            Assert.Equal("Acme/Website", carol.SourcePath);
        }

        [Fact]
        public void EffectiveGrants_DeepNodeCanGrantHigherRole()
        {
            _store.EnsureSubProject("Acme/Website/Web");
            _store.SaveAcl("Acme", new[] { Grant("dave", AccessRole.Editor) });
            _store.SaveAcl("Acme/Website/Web", new[] { Grant("dave", AccessRole.Owner) });

            var dave = _store.GetEffectiveGrants("Acme/Website/Web").Single(g => g.Principal == "dave");
            Assert.Equal(AccessRole.Owner, dave.Role);
            Assert.Equal("Acme/Website/Web", dave.SourcePath);
        }

        // ── CanAct matrix ────────────────────────────────────────────

        [Fact]
        public void CanAct_UnknownOrMissingPrincipalDenied()
        {
            var service = new WorkspaceAccessService(_store);
            _store.CreateNode("Acme");
            _store.SaveAcl("Acme", new[] { Grant("alice", AccessRole.Owner) });

            Assert.False(service.CanAct(null, "Acme", WorkspaceAction.View));
            Assert.False(service.CanAct("", "Acme", WorkspaceAction.View));
            Assert.False(service.CanAct("mallory", "Acme", WorkspaceAction.ManageAccess));
        }

        [Fact]
        public void CanAct_MatrixPerRole()
        {
            var service = new WorkspaceAccessService(_store);
            _store.CreateNode("Acme");

            void Check(string role, bool view, bool edit, bool del, bool manage)
            {
                _store.SaveAcl("Acme", new[] { Grant("u", role) });
                Assert.Equal(view, service.CanAct("u", "Acme", WorkspaceAction.View));
                Assert.Equal(edit, service.CanAct("u", "Acme", WorkspaceAction.Edit));
                Assert.Equal(del, service.CanAct("u", "Acme", WorkspaceAction.Delete));
                Assert.Equal(manage, service.CanAct("u", "Acme", WorkspaceAction.ManageAccess));
            }

            Check(AccessRole.Viewer, true, false, false, false);
            Check(AccessRole.Editor, true, true, false, false);
            Check(AccessRole.Admin, true, true, true, false);
            Check(AccessRole.Owner, true, true, true, true);
        }

        [Fact]
        public void CanAct_InheritsFromAncestors()
        {
            var service = new WorkspaceAccessService(_store);
            _store.EnsureSubProject("Acme/Website/Web");
            _store.SaveAcl("Acme", new[] { Grant("alice", AccessRole.Editor) });

            Assert.True(service.CanAct("alice", "Acme/Website/Web", WorkspaceAction.Edit));
            Assert.False(service.CanAct("alice", "Acme/Website/Web", WorkspaceAction.Delete));
        }

        // ── flows under a sub-project ────────────────────────────────

        [Fact]
        public void Flows_SaveListGetDeleteRoundTrip()
        {
            _store.EnsureSubProject("Acme/Website/Web");

            var (id, created) = _store.SaveFlow("Acme/Website/Web", null, "Order Flow", "desc", Definition);
            Assert.True(created);
            Assert.Equal("order-flow", id); // slug of the name

            var flows = _store.ListFlows("Acme/Website/Web");
            Assert.Single(flows);
            Assert.Equal("Order Flow", flows[0].Meta.Name);
            Assert.Equal("desc", flows[0].Meta.Description);

            Assert.Equal(Definition, _store.LoadFlowDefinition("Acme/Website/Web", id));

            // On-disk layout: {sub}/flows/{id}/{flow.json,meta.json}
            var dir = Path.Combine(_dir, "Acme", "Website", "Web", "flows", id);
            Assert.True(File.Exists(Path.Combine(dir, "flow.json")));
            Assert.True(File.Exists(Path.Combine(dir, "meta.json")));

            Assert.True(_store.DeleteFlow("Acme/Website/Web", id));
            Assert.Empty(_store.ListFlows("Acme/Website/Web"));
            Assert.False(Directory.Exists(dir));
        }

        [Fact]
        public void Flows_IdCollisionGetsSuffix()
        {
            _store.EnsureSubProject("Acme/Website/Web");

            var a = _store.SaveFlow("Acme/Website/Web", "shared", "Alpha", null, Definition);
            // No requested id; slug of the name collides with the existing folder → -2 suffix.
            var b = _store.SaveFlow("Acme/Website/Web", null, "Shared", null, Definition);

            Assert.Equal("shared", a.Id);
            Assert.Equal("shared-2", b.Id);
        }

        [Fact]
        public void Flows_SameNameUpdateReusesIdAndPreservesCreatedAt()
        {
            _store.EnsureSubProject("Acme/Website/Web");
            var (id, created) = _store.SaveFlow("Acme/Website/Web", null, "Order Flow", null, Definition);
            Assert.True(created);

            var before = _store.ListFlows("Acme/Website/Web").Single().Meta.CreatedAt;
            Thread.Sleep(40); // make the timestamps distinguishable (UtcNow ticks ~15ms)

            var (id2, created2) = _store.SaveFlow("Acme/Website/Web", null, "Order Flow", "updated desc", Definition);
            Assert.Equal(id, id2);
            Assert.False(created2);

            var after = _store.ListFlows("Acme/Website/Web").Single();
            Assert.Equal(before, after.Meta.CreatedAt);
            Assert.Equal("updated desc", after.Meta.Description);
        }

        [Fact]
        public void Flows_ExplicitIdUpdatesInPlace()
        {
            _store.EnsureSubProject("Acme/Website/Web");
            var (id, _) = _store.SaveFlow("Acme/Website/Web", "my-flow", "First Name", null, Definition);
            Assert.Equal("my-flow", id);

            // Same explicit id with a different name → in-place update of the same folder.
            var (id2, created2) = _store.SaveFlow("Acme/Website/Web", "my-flow", "Renamed Flow", null, Definition);
            Assert.Equal(id, id2);
            Assert.False(created2);

            var flows = _store.ListFlows("Acme/Website/Web");
            Assert.Single(flows);
            Assert.Equal("Renamed Flow", flows[0].Meta.Name);
        }

        [Fact]
        public void Flows_SaveToMissingSubProjectThrows() =>
            Assert.Throws<DirectoryNotFoundException>(() => _store.SaveFlow("Nope/Nope/Nope", null, "X", null, Definition));

        // ── profile store (tree-aware) ───────────────────────────────

        [Fact]
        public void ProfileStore_UnassignedSaveLoad()
        {
            var store = new DataExchangeProfileStore(_dir); // single-arg ctor: workspace root only

            var id = store.Save(Profile("Customer Import", "cust-import"));
            Assert.Equal("cust-import", id);

            // Unassigned bucket: {root}/{id}/profile.json
            Assert.True(File.Exists(Path.Combine(_dir, "cust-import", "profile.json")));

            var loaded = store.LoadAll();
            Assert.Single(loaded);
            Assert.Equal("Customer Import", loaded[0].DataExchangeProfileName);
            Assert.NotNull(store.Get("cust-import"));
        }

        [Fact]
        public void ProfileStore_OrganizedSaveDiscoveredByLoadAll()
        {
            _store.EnsureSubProject("Acme/Website/Web");
            var store = new DataExchangeProfileStore(_dir);

            var id = store.Save(Profile("Orders Feed", "orders-feed"), "acme/website/web");

            Assert.True(File.Exists(Path.Combine(_dir, "Acme", "Website", "Web", "data-exchange", id, "profile.json")));

            var entry = store.ScanAll().Single(e => string.Equals(DataExchangeProfileStore.ResolveId(e.Profile), id));
            Assert.Equal("Acme/Website/Web", entry.SubProjectPath);
        }

        [Fact]
        public void ProfileStore_LegacyFlatDirStillLoads()
        {
            var legacy = Path.Combine(_dir, "legacy-profiles");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "old-one.json"), JsonConvert.SerializeObject(Profile("Old One", "old-one")));

            var store = new DataExchangeProfileStore(_dir, legacy);
            Assert.Contains(store.LoadAll(), p => p.ProfileId == "old-one");
        }

        [Fact]
        public void ProfileStore_WorkspaceWinsOverLegacyOnDuplicate()
        {
            var legacy = Path.Combine(_dir, "legacy-profiles");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "dup.json"), JsonConvert.SerializeObject(Profile("Legacy Dup", "dup")));

            var store = new DataExchangeProfileStore(_dir, legacy);
            store.Save(Profile("Workspace Dup", "dup")); // unassigned bucket — same resolved id as the legacy file

            var all = store.LoadAll();
            Assert.Single(all.Where(p => p.ProfileId == "dup"));
            Assert.Equal("Workspace Dup", all.Single(p => p.ProfileId == "dup").DataExchangeProfileName);
        }

        [Fact]
        public void ProfileStore_UpdateInPlaceKeepsLocation()
        {
            _store.EnsureSubProject("Acme/Website/Web");
            var store = new DataExchangeProfileStore(_dir);
            store.Save(Profile("Feed", "feed"), "acme/website/web");

            // Re-saving the same id (even with a different location) updates the file in place — no move.
            store.Save(Profile("Feed v2", "feed"));

            Assert.True(File.Exists(Path.Combine(_dir, "Acme", "Website", "Web", "data-exchange", "feed", "profile.json")));
            var loaded = store.LoadAll().Single();
            Assert.Equal("Feed v2", loaded.DataExchangeProfileName);
        }

        [Fact]
        public void ProfileStore_DeleteRemovesFileAndEmptyFolder()
        {
            _store.EnsureSubProject("Acme/Website/Web");
            var store = new DataExchangeProfileStore(_dir);
            store.Save(Profile("Gone", "gone"), "acme/website/web");

            Assert.True(store.Delete("gone"));
            Assert.False(File.Exists(Path.Combine(_dir, "Acme", "Website", "Web", "data-exchange", "gone", "profile.json")));
            // The empty {id} folder is cleaned up; the data-exchange dir remains.
            Assert.False(Directory.Exists(Path.Combine(_dir, "Acme", "Website", "Web", "data-exchange", "gone")));
            Assert.True(Directory.Exists(Path.Combine(_dir, "Acme", "Website", "Web", "data-exchange")));

            Assert.False(store.Delete("gone")); // already gone
        }

        // ── migration ────────────────────────────────────────────────

        [Fact]
        public void Migration_MovesLegacyProfilesIntoDefaultSubProject_AndIsIdempotent()
        {
            var legacy = Path.Combine(_dir, "dataexchange", "profiles");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "alpha.json"), JsonConvert.SerializeObject(Profile("Alpha", "alpha")));
            File.WriteAllText(Path.Combine(legacy, "beta.json"), JsonConvert.SerializeObject(Profile("Beta"))); // no ProfileId → slug

            WorkspaceMigration.Migrate(_store, legacy);

            var target = Path.Combine(_dir, "Default", "Default", "Default", "data-exchange");
            Assert.True(File.Exists(Path.Combine(target, "alpha", "profile.json")));
            Assert.True(File.Exists(Path.Combine(target, "beta", "profile.json")));
            Assert.Empty(Directory.GetFiles(legacy));

            // Idempotent: re-running is a no-op.
            WorkspaceMigration.Migrate(_store, legacy);
            Assert.True(File.Exists(Path.Combine(target, "alpha", "profile.json")));

            // The store sees the migrated profiles under the default sub-project.
            var entry = new DataExchangeProfileStore(_dir)
                .ScanAll()
                .Single(e => string.Equals(DataExchangeProfileStore.ResolveId(e.Profile), "alpha"));
            Assert.Equal("Default/Default/Default", entry.SubProjectPath);
        }
    }
}

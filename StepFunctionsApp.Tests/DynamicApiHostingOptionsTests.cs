using System;
using StepFunctionsApp.DynamicApi;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// DynamicApiHostingOptions — "DynamicApi" appsettings section: port → node bindings,
    /// BindingForPort lookup and fail-fast validation (range, duplicates, node-path shape).
    /// </summary>
    public class DynamicApiHostingOptionsTests
    {
        private static DynamicApiEndpointBinding Endpoint(int port, string nodePath) => new() { Port = port, NodePath = nodePath };

        // ── BindingForPort ───────────────────────────────────────────

        [Fact]
        public void BindingForPort_ReturnsMatch_AndNullForUnknown()
        {
            var options = new DynamicApiHostingOptions();
            var ep = Endpoint(5101, "Acme UI/Website");
            options.Endpoints.Add(ep);

            Assert.Same(ep, options.BindingForPort(5101));
            Assert.Null(options.BindingForPort(5001)); // management port → unscoped
            Assert.Null(options.BindingForPort(9999));
            Assert.Null(options.BindingForPort(0));     // TestServer / no local port
        }

        // ── Validate: duplicates ─────────────────────────────────────

        [Fact]
        public void Validate_ThrowsOnDuplicatePorts()
        {
            var options = new DynamicApiHostingOptions { ManagementPort = 5101 };
            options.Endpoints.Add(Endpoint(5101, "Org"));
            var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
            Assert.Contains("5101", ex.Message);

            var two = new DynamicApiHostingOptions();
            two.Endpoints.Add(Endpoint(6001, "OrgA"));
            two.Endpoints.Add(Endpoint(6001, "OrgB"));
            Assert.Throws<InvalidOperationException>(() => two.Validate());
        }

        // ── Validate: node path shape + normalization ────────────────

        [Fact]
        public void Validate_ThrowsOnBadNodePath()
        {
            foreach (var bad in new[] { "", "a/b/c/d", "a/../b" })
            {
                var options = new DynamicApiHostingOptions();
                options.Endpoints.Add(Endpoint(5201, bad));
                Assert.Throws<InvalidOperationException>(() => options.Validate());
            }

            foreach (var good in new[] { "org", "org/project", "org/project/sub" })
            {
                var options = new DynamicApiHostingOptions();
                options.Endpoints.Add(Endpoint(5201, good));
                options.Validate(); // must not throw
            }

            // Leading/trailing '/' are trimmed so the value matches SqliteDynamicApiStore.GetAll's prefix query.
            var normalized = new DynamicApiHostingOptions();
            normalized.Endpoints.Add(Endpoint(5201, "/org/project/"));
            normalized.Validate();
            Assert.Equal("org/project", normalized.Endpoints[0].NodePath);
        }

        // ── Validate: port range ─────────────────────────────────────

        [Fact]
        public void Validate_ThrowsOnOutOfRangePort()
        {
            var mgmt = new DynamicApiHostingOptions { ManagementPort = 0 };
            Assert.Throws<InvalidOperationException>(() => mgmt.Validate());

            var mgmtHigh = new DynamicApiHostingOptions { ManagementPort = 70000 };
            Assert.Throws<InvalidOperationException>(() => mgmtHigh.Validate());

            foreach (var port in new[] { 0, 70000 })
            {
                var options = new DynamicApiHostingOptions();
                options.Endpoints.Add(Endpoint(port, "Org"));
                Assert.Throws<InvalidOperationException>(() => options.Validate());
            }
        }
    }
}

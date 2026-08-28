using System;
using System.Collections.Generic;
using System.Linq;
using StepFunctionsApp.DynamicApi;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// DynamicApiMatcher - pure path matching for /api/dynamic requests: normalization,
    /// template params, literal-preference ranking and the 405 Allow set.
    /// </summary>
    public class DynamicApiMatcherTests
    {
        private static DynamicApiOperation Op(string method, string path) => new() { Method = method, Path = path };

        private static DynamicApiDefinition Api(string id, string basePath, params DynamicApiOperation[] ops) =>
            new() { Id = id, Name = id, NodePath = "Org", BasePath = basePath, IsActive = true, Operations = new List<DynamicApiOperation>(ops) };

        // ── normalization ────────────────────────────────────────────

        [Fact]
        public void Normalize_CollapsesDuplicateAndTrailingSlashes()
        {
            Assert.Equal("/a/b", DynamicApiMatcher.Normalize("/a//b/"));
            Assert.Equal("/orders/items", DynamicApiMatcher.Normalize("orders\\items"));
            Assert.Equal("/", DynamicApiMatcher.Normalize(""));
            Assert.Equal("/", DynamicApiMatcher.Normalize("/"));
            Assert.Equal("/x/y", DynamicApiMatcher.Normalize("//x///y//"));
        }

        [Fact]
        public void JoinPaths_BasesWithOpPath_AndNormalizes()
        {
            Assert.Equal("/orders/{id}", DynamicApiMatcher.JoinPaths("/orders", "/{id}"));
            Assert.Equal("/", DynamicApiMatcher.JoinPaths("/", ""));
            Assert.Equal("/orders/items", DynamicApiMatcher.JoinPaths("/orders", "/items"));
        }

        [Fact]
        public void PathSegments_SplitsNormalizedPath()
        {
            Assert.Equal(new[] { "a", "b" }, DynamicApiMatcher.PathSegments("/a//b/"));
            Assert.Empty(DynamicApiMatcher.PathSegments("/"));
        }

        // ── matching ─────────────────────────────────────────────────

        [Fact]
        public void Match_ReturnsNull_WhenNothingMatches()
        {
            var apis = new List<DynamicApiDefinition> { Api("a", "/orders", Op("GET", "")) };

            Assert.Null(DynamicApiMatcher.Match("GET", "/nope", apis));          // no path match
            Assert.Null(DynamicApiMatcher.Match("POST", "/orders", apis));       // method mismatch
            Assert.Null(DynamicApiMatcher.Match("GET", "/orders/extra", apis));  // segment count mismatch

            var inactive = Api("b", "/ghost", Op("GET", ""));
            inactive.IsActive = false;
            Assert.Null(DynamicApiMatcher.Match("GET", "/ghost", new List<DynamicApiDefinition> { inactive }));
        }

        [Fact]
        public void Match_MatchesLiteralPath_AndExtractsTemplateParams()
        {
            var apis = new List<DynamicApiDefinition> { Api("a", "/orders", Op("GET", "/{id}")) };

            var m = DynamicApiMatcher.Match("get", "/orders/42", apis); // method is case-insensitive
            Assert.NotNull(m);
            Assert.Equal("a", m!.Api.Id);
            Assert.Equal("42", (string)m.PathParams["id"]!);
        }

        [Fact]
        public void Match_ExtractsMultipleTemplateParams()
        {
            var apis = new List<DynamicApiDefinition> { Api("a", "/a", Op("GET", "/{x}/{y}")) };

            var m = DynamicApiMatcher.Match("GET", "/a/1/two", apis);
            Assert.NotNull(m);
            Assert.Equal("1", (string)m!.PathParams["x"]!);
            Assert.Equal("two", (string)m.PathParams["y"]!);
        }

        [Fact]
        public void Match_PrefersMostLiteralSegments_OverTemplate()
        {
            var apis = new List<DynamicApiDefinition>
            {
                Api("a", "/orders", Op("GET", "/{id}"), Op("GET", "/special")),
            };

            Assert.Equal("/special", DynamicApiMatcher.Match("GET", "/orders/special", apis)!.Operation.Path);
            Assert.Equal("/{id}", DynamicApiMatcher.Match("GET", "/orders/other", apis)!.Operation.Path);
        }

        [Fact]
        public void Match_TieBreaksByApiIdOrdinal()
        {
            var apis = new List<DynamicApiDefinition>
            {
                Api("zeta", "/twin", Op("GET", "")),
                Api("alpha", "/twin", Op("GET", "")),
            };

            Assert.Equal("alpha", DynamicApiMatcher.Match("GET", "/twin", apis)!.Api.Id);
        }

        // ── allowed methods (405 Allow header) ───────────────────────

        [Fact]
        public void AllowedMethods_ReturnsStructuralMatches_Only()
        {
            var apis = new List<DynamicApiDefinition>
            {
                Api("a", "/orders", Op("GET", "/{id}"), Op("POST", "")),
            };

            Assert.Equal(new[] { "GET" }, DynamicApiMatcher.AllowedMethods("/orders/1", apis).ToArray());
            Assert.Equal(new[] { "POST" }, DynamicApiMatcher.AllowedMethods("/orders", apis).ToArray());
            Assert.Empty(DynamicApiMatcher.AllowedMethods("/nope", apis));

            var inactive = Api("b", "/ghost", Op("DELETE", ""));
            inactive.IsActive = false;
            Assert.Empty(DynamicApiMatcher.AllowedMethods("/ghost", new List<DynamicApiDefinition> { inactive }));
        }
    }
}

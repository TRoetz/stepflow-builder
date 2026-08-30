using Newtonsoft.Json.Linq;
using StepFlow.DynamicApi;

namespace StepFunctionsApp.Tests
{
    /// <summary>Pure-string mapping of eav GET requests onto engine routes (no host involved).</summary>
    public sealed class EavGetMapperTests
    {
        private static JObject Params(params (string Name, string Value)[] pairs) =>
            new(pairs.Select(p => new JProperty(p.Name, p.Value)));

        [Fact]
        public void Map_NoParams_Collection_VerbatimQuery()
        {
            var mapping = EavGetMapper.Map("", new JObject(), "?entityId=e1&limit=5");

            Assert.Equal(EavGetMode.Collection, mapping.Mode);
            Assert.Null(mapping.Id);
            Assert.Equal("entityId=e1&limit=5", mapping.Query);
        }

        [Fact]
        public void Map_SingleParam_OneSegmentOpPath_Lookup_DecodesValue()
        {
            var mapping = EavGetMapper.Map("/{id}", Params(("id", "my%20id")), "?fields=name");

            Assert.Equal(EavGetMode.Lookup, mapping.Mode);
            Assert.Equal("my id", mapping.Id); // percent-encoding from the matcher is decoded here
            Assert.Equal("fields=name", mapping.Query);
        }

        [Fact]
        public void Map_SingleParam_EmptyOpPath_Lookup()
        {
            // Identity parameter supplied by the API base path, op path empty.
            var mapping = EavGetMapper.Map("", Params(("org", "acme")), null);

            Assert.Equal(EavGetMode.Lookup, mapping.Mode);
            Assert.Equal("acme", mapping.Id);
            Assert.Equal("", mapping.Query);
        }

        [Fact]
        public void Map_SingleParam_MultiSegmentOpPath_EntityFilter_MergesEntityId()
        {
            var mapping = EavGetMapper.Map("/{id}/comments", Params(("id", "o1")), "?sort=-capturedAtUtc");

            Assert.Equal(EavGetMode.EntityFilter, mapping.Mode);
            Assert.Equal("o1", mapping.Id);
            Assert.Equal("sort=-capturedAtUtc&entityId=o1", mapping.Query);
        }

        [Fact]
        public void Map_EntityFilter_SameEntityIdInQuery_Dedupes()
        {
            var mapping = EavGetMapper.Map("/{id}/comments", Params(("id", "o1")), "?entityId=o1&limit=2");

            Assert.Equal(EavGetMode.EntityFilter, mapping.Mode);
            Assert.Equal("limit=2&entityId=o1", mapping.Query);
        }

        [Fact]
        public void Map_EntityFilter_ConflictingEntityId_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => EavGetMapper.Map("/{id}/comments", Params(("id", "o1")), "?entityId=other"));

            Assert.Contains("Conflicting entityId", ex.Message);
        }

        [Fact]
        public void Map_TwoParams_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => EavGetMapper.Map("/{a}/{b}", Params(("a", "1"), ("b", "2")), null));

            Assert.Contains("at most one path parameter", ex.Message);
        }
    }
}

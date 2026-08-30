using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// EavMapper (payload → strict EAV dictionary) and CompositeEavEntityProvider
    /// (registry ∪ attribute domains, domain wins on name collision).
    /// </summary>
    public class EavMapperTests : IDisposable
    {
        private readonly string _dir;

        public EavMapperTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "stepflow-eavmapper-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private static EavEntityDefinition Entity(params (string Name, string Type, bool Required, object? Default, string Path)[] attrs) => new()
        {
            EntityName = "Test",
            Attributes = attrs.Select(a => new EavAttributeDefinition
            {
                AttributeName = a.Name,
                DataType = a.Type,
                IsRequired = a.Required,
                DefaultValue = a.Default,
                JsonPathMapping = a.Path
            }).ToList()
        };

        // ── mapper ────────────────────────────────────────────────────────────────

        [Fact]
        public void Map_ExtractsByJsonPath_AndCastsToEavTypes()
        {
            var entity = Entity(
                ("name", "string", false, null, "$.customer.name"),
                ("qty", "number", false, null, "quantity"), // no $ prefix — mapper adds it
                ("ok", "boolean", false, null, "$.flags.ok"),
                ("when", "date", false, null, "$.at"));

            var payload = JObject.Parse("""{"customer":{"name":"Acme"},"quantity":3,"flags":{"ok":true},"at":"2026-08-25T10:00:00Z"}""");
            var result = EavMapper.Map(entity, payload);

            Assert.Equal("Acme", result["name"]);
            Assert.Equal(3.0, result["qty"]);
            Assert.Equal(DateTime.Parse("2026-08-25T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal), (DateTime)result["when"]);
        }
        [Fact]
        public void Map_MissingOptionalAttribute_IsOmitted()
        {
            var entity = Entity(("missing", "string", false, null, "$.nope"));
            var result = EavMapper.Map(entity, new JObject());
            Assert.Empty(result);
        }

        [Fact]
        public void Map_MissingRequiredAttribute_Throws()
        {
            var entity = Entity(("req", "string", true, null, "$.req"));
            var ex = Assert.Throws<StepEngineException>(() => EavMapper.Map(entity, new JObject()));
            Assert.Contains("Required attribute 'req' not found", ex.Message);
        }

        [Fact]
        public void Map_DefaultValueUsedWhenTokenAbsent()
        {
            var entity = Entity(("d", "string", false, "fallback", "$.absent"));
            var result = EavMapper.Map(entity, new JObject());
            Assert.Equal("fallback", result["d"]);
        }

        [Fact]
        public void CastToEavType_BadCast_Throws()
        {
            var ex = Assert.Throws<StepEngineException>(() => EavMapper.CastToEavType(new JValue("abc"), "number"));
            Assert.Contains("Failed to cast attribute", ex.Message);
        }

        // ── composite provider ────────────────────────────────────────────────────

        [Fact]
        public void CompositeProvider_MergesRegistryAndDomains_OrderedByName()
        {
            var registry = new EavRegistryService();
            registry.RegisterEntity(new EavEntityDefinition
            {
                EntityName = "Zeta",
                Attributes = new List<EavAttributeDefinition> { new() { AttributeName = "a" } }
            });

            var domains = new JsonFileAttributeDomainStore(Path.Combine(_dir, "attribute_domains.json"), NullLogger<JsonFileAttributeDomainStore>.Instance);
            domains.Save(new AttributeDomain
            {
                AttributeDomainName = "Alpha",
                Attributes = new List<EntityAttribute> { new() { AttributeName = "b", DataType = AttributeDataType.Number } }
            });

            var provider = new CompositeEavEntityProvider(registry, domains);
            var names = provider.GetAllEntities().Select(e => e.EntityName).ToList();
            Assert.Equal(new[] { "Alpha", "Zeta" }, names);
        }

        [Fact]
        public void CompositeProvider_DomainWinsOverRegistryOnCollision()
        {
            var registry = new EavRegistryService();
            registry.RegisterEntity(new EavEntityDefinition
            {
                EntityName = "Order",
                Attributes = new List<EavAttributeDefinition> { new() { AttributeName = "LegacyAttr" } }
            });

            var domains = new JsonFileAttributeDomainStore(Path.Combine(_dir, "attribute_domains.json"), NullLogger<JsonFileAttributeDomainStore>.Instance);
            domains.Save(new AttributeDomain
            {
                AttributeDomainName = "Order",
                Attributes = new List<EntityAttribute>
                {
                    new() { AttributeName = "CustomerName", DataType = AttributeDataType.String, ValidationSchemaJson = "{\"required\":true}" },
                    new() { AttributeName = "Amount", DataType = AttributeDataType.Number }
                }
            });

            var provider = new CompositeEavEntityProvider(registry, domains);
            var entity = provider.GetEntity("order"); // case-insensitive lookup

            Assert.NotNull(entity);
            Assert.Equal(2, entity!.Attributes.Count);
            Assert.Contains(entity.Attributes, a => a.AttributeName == "CustomerName" && a.IsRequired);
            Assert.Contains(entity.Attributes, a => a.AttributeName == "Amount" && a.DataType == "number");
        }

        [Fact]
        public void CompositeProvider_FallsBackToRegistryWhenDomainUnknown()
        {
            var registry = new EavRegistryService();
            registry.RegisterEntity(new EavEntityDefinition
            {
                EntityName = "Legacy",
                Attributes = new List<EavAttributeDefinition> { new() { AttributeName = "x" } }
            });

            var domains = new JsonFileAttributeDomainStore(Path.Combine(_dir, "attribute_domains.json"), NullLogger<JsonFileAttributeDomainStore>.Instance);
            var provider = new CompositeEavEntityProvider(registry, domains);

            Assert.NotNull(provider.GetEntity("Legacy"));
            Assert.Null(provider.GetEntity("Unknown"));
        }
    }
}

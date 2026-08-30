using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Builds a fully-wired CompositeResourceInvoker for unit tests: blank inventories, NullLoggers,
    /// isolated temp dirs — same construction pattern as StepFlowTests, shared by the scheme-handler test classes.
    /// </summary>
    internal static class TestResourceInvokers
    {
        public static (CompositeResourceInvoker Invoker, EavRowStore EavRows, string EavDir) Create()
        {
            var mockFactory = new Mock<System.Net.Http.IHttpClientFactory>();

            var ruleEngine = new RuleEngineService(NullLogger<RuleEngineService>.Instance);
            var msRulesEngine = new MicrosoftRulesEngineService(NullLogger<MicrosoftRulesEngineService>.Instance);
            var duckDb = new DuckDbTransformService(NullLogger<DuckDbTransformService>.Instance);
            var scriptExecution = new ScriptExecutionService(NullLogger<ScriptExecutionService>.Instance);

            // Circular reference Lazy resolver mock (StepFunctionService is never invoked by the scheme handlers under test)
            var mockStepService = new Mock<StepFunctionService>(null!, null!, null!, null!, null!, null!);
            var lazyStepService = new Lazy<StepFunctionService>(() => mockStepService.Object);

            var dataExchange = new DataExchangeExecutor(
                new DataExchangeProfileStore(Path.Combine(Path.GetTempPath(), "dataexchange-tests", Guid.NewGuid().ToString("N"))),
                duckDb,
                mockFactory.Object,
                NullLogger<DataExchangeExecutor>.Instance);

            var aiDecision = new AiDecisionService(mockFactory.Object, NullLogger<AiDecisionService>.Instance);

            var eavDir = Path.Combine(Path.GetTempPath(), "eav-rows-tests", Guid.NewGuid().ToString("N"));
            var eavRows = new EavRowStore(NullLogger<EavRowStore>.Instance);
            eavRows.Initialize(eavDir);

            var invoker = new CompositeResourceInvoker(
                mockFactory.Object,
                ruleEngine,
                msRulesEngine,
                duckDb,
                new CompositeEavEntityProvider(new EavRegistryService(), new JsonFileAttributeDomainStore(Path.Combine(Path.GetTempPath(), "eav-provider-tests", Guid.NewGuid().ToString("N")))), // blank registry + empty domain store
                scriptExecution,
                lazyStepService,
                dataExchange,
                new SshCommandService(new SshHostStore(), aiDecision), // blank inventory; AI mocked via factory
                new FetchRemoteFilesService(new SshHostStore()), // blank inventory — fetch:// validation only
                eavRows,
                NullLogger<CompositeResourceInvoker>.Instance);

            return (invoker, eavRows, eavDir);
        }
    }
}

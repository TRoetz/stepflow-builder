namespace StepFunctionsApp.Rules
{
    /// <summary>Persistence for named rule artifacts (the project's rule catalog).</summary>
    public interface INamedRuleStore
    {
        void Initialize(string path);

        IReadOnlyList<NamedRule> List();

        NamedRule? Get(string name);

        /// <summary>Upserts by name and persists. Returns the stored copy.</summary>
        NamedRule Save(NamedRule rule);

        bool Delete(string name);
    }
}

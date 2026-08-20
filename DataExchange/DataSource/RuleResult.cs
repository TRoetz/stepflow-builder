
namespace StepFlow.DataModel.Entities.DataSource
{
    /// <summary>
    /// Concrete implementation of rule result
    /// </summary>
    public class RuleResult : IRuleResult
    {
        public bool Status { get; set; }            // True = Pass, False = Fail
        public bool HasErrored { get; set; }        // True if code crashed (e.g. missing param)
        public string ErrorMessage { get; set; }    // Error details if HasErrored is true
        public List<KeyValuePair<string, object>> Output { get; set; } = new(); // Complex outputs
    }
}
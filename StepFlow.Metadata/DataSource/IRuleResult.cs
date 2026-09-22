
/// <summary>
/// Result from rule execution
/// </summary>
public interface IRuleResult
{
    bool Status { get; }
    bool HasErrored { get; }
    string ErrorMessage { get; }
    List<KeyValuePair<string, object>> Output { get; }
}

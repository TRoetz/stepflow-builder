using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using StepFlow.DataModel.Entities.MetaData;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class Rule
    {
        public int RuleId { get; set; }
        public string RuleName { get; set; }

        public RuleType Type { get; set; }

        /// <summary>
        /// The main body of the rule, typically a SQL CASE statement or a boolean expression.
        /// This is the "SELECT" or "CASE" part of the script.
        /// Example: "CASE WHEN {CreditScore} > 720 THEN 'Approved' ELSE 'Review' END"
        /// Example: "{ApplicantAge} >= 18 AND {LoanAmount} > 0"
        /// </summary>
        public string Expression { get; set; }

        /// <summary>
        /// A comma-separated list of the Lookup names required by this rule.
        /// This is the "FROM" part of the script.
        /// Example: "ValidStateCodesLookup,CreditBureauLookup"
        /// </summary>
        public string? RequiredLookupsCsv { get; set; }

        [NotMapped]
        public List<string> RequiredLookups
        {
            get => !string.IsNullOrEmpty(RequiredLookupsCsv)
                ? RequiredLookupsCsv.Split(',').Select(s => s.Trim()).ToList()
                : new List<string>();
            set => RequiredLookupsCsv = string.Join(",", value);
        }
        public string DefaultFailureMessage { get; set; }
        public ICollection<RuleParameter> Parameters { get; set; } = new List<RuleParameter>();

        public ICollection<Entities.MetaData.EntityAttribute> Attributes { get; set; } = new List<Entities.MetaData.EntityAttribute>();

    }
}
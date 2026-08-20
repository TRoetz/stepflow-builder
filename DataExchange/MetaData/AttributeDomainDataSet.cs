namespace StepFlow.DataModel.Entities.MetaData
{
    public class AttributeDomainDataSet
    {
        public int AttributeDomainDataSetId { get; set; }
        public string AttributeDomainDataSetName { get; set; }
        public int AttributeDomainId { get; set; } // Link to the domain structure it follows
        public string SourceIdentifier { get; set; } // e.g., "Herbicide Spray 1st Quarter.csv"
        public DateTime LoadTimestamp { get; set; }
    }
}
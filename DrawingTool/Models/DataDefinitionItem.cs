namespace DrawingTool.Models
{
    public class DataDefinitionItem
    {
        public string Name { get; set; } = "";
        public string ReferenceDefinitionName { get; set; } = "";

        public string DisplayText
        {
            get
            {
                return string.IsNullOrWhiteSpace(ReferenceDefinitionName)
                    ? Name
                    : $"{Name} -> {ReferenceDefinitionName}.ID";
            }
        }
    }
}

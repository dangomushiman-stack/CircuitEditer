namespace DrawingTool.Models
{
    public class DataDefinitionItem
    {
        public string Name { get; set; } = "";
        public string ReferenceDefinitionName { get; set; } = "";
        public bool UsePythonValueGenerator { get; set; }
        public string PythonValueGeneratorCode { get; set; } = "";

        public string DisplayText
        {
            get
            {
                var text = string.IsNullOrWhiteSpace(ReferenceDefinitionName)
                    ? Name
                    : $"{Name} -> {ReferenceDefinitionName}.ID";
                return UsePythonValueGenerator ? $"{text} (Python)" : text;
            }
        }
    }
}

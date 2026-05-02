using System.Collections.Generic;
using System.Linq;

namespace DrawingTool.Models
{
    public class DataRecord
    {
        public string Name { get; set; } = "";
        public string DefinitionName { get; set; } = "";
        public List<SymbolAttribute> Attributes { get; set; } = new List<SymbolAttribute>();

        public string DisplayText => $"[{DefinitionName}] ID={Name} ({Attributes.Count} values)";

        public DataRecord Clone()
        {
            return new DataRecord
            {
                Name = Name,
                DefinitionName = DefinitionName,
                Attributes = Attributes
                    .Select(attribute => new SymbolAttribute { Key = attribute.Key, Value = attribute.Value })
                    .ToList()
            };
        }
    }
}

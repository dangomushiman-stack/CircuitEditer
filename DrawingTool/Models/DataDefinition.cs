using System.Collections.ObjectModel;
using System.Linq;

namespace DrawingTool.Models
{
    public class DataDefinition
    {
        public string Name { get; set; } = "";
        public string ParentDefinitionName { get; set; } = "";
        public string IdItemName { get; set; } = "";
        public bool UsePythonIdGenerator { get; set; }
        public string PythonIdGeneratorCode { get; set; } = "";
        public ObservableCollection<string> Items { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<DataDefinitionItem> ItemDefinitions { get; set; } = new ObservableCollection<DataDefinitionItem>();

        public string DisplayText
        {
            get
            {
                var parentText = string.IsNullOrWhiteSpace(ParentDefinitionName)
                    ? "親なし"
                    : $"親: {ParentDefinitionName}";
                var idText = string.IsNullOrWhiteSpace(IdItemName)
                    ? "ID未設定"
                    : $"ID: {IdItemName}";
                var pythonText = UsePythonIdGenerator ? ", PythonID" : "";
                return $"{Name} ({Items.Count} items, {parentText}, {idText}{pythonText})";
            }
        }

        public string GetReferenceDefinitionName(string itemName)
        {
            return ItemDefinitions
                .FirstOrDefault(item => item.Name == itemName)
                ?.ReferenceDefinitionName ?? "";
        }
    }
}

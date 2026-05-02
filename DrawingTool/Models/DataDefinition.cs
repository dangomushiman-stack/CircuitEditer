using System.Collections.ObjectModel;

namespace DrawingTool.Models
{
    public class DataDefinition
    {
        public string Name { get; set; } = "";
        public string ParentDefinitionName { get; set; } = "";
        public string IdItemName { get; set; } = "";
        public ObservableCollection<string> Items { get; set; } = new ObservableCollection<string>();

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
                return $"{Name} ({Items.Count} items, {parentText}, {idText})";
            }
        }
    }
}

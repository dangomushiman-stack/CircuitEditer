using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using DrawingTool.Models;

namespace DrawingTool.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<ShapeDefinition> RegisteredShapes { get; } = new ObservableCollection<ShapeDefinition>();
        public ObservableCollection<ShapeDefinition> RegisteredSymbols { get; } = new ObservableCollection<ShapeDefinition>();
        public ObservableCollection<PlacedDrawingInfo> PlacedSymbols { get; } = new ObservableCollection<PlacedDrawingInfo>();
        public ObservableCollection<PlacedDrawingInfo> LineGroups { get; } = new ObservableCollection<PlacedDrawingInfo>();
        public ObservableCollection<DataDefinition> DataDefinitions { get; } = new ObservableCollection<DataDefinition>();
        public ObservableCollection<DataRecord> DataRecords { get; } = new ObservableCollection<DataRecord>();
        public List<Point> TempConnectionPoints { get; } = new List<Point>();
        public List<SymbolVectorElement> TempVectorElements { get; } = new List<SymbolVectorElement>();
        public List<SymbolAttribute> TempAttributes { get; } = new List<SymbolAttribute>();
        public ObservableCollection<DataDefinitionItem> TempDataItems { get; } = new ObservableCollection<DataDefinitionItem>();

        private ShapeDefinition? selectedShape;
        private ShapeDefinition? selectedRegisteredSymbol;
        private DataDefinition? selectedDataDefinition;
        private DataRecord? selectedDataRecord;

        public ShapeDefinition? SelectedShape
        {
            get => selectedShape;
            set => SetProperty(ref selectedShape, value);
        }

        public ShapeDefinition? SelectedRegisteredSymbol
        {
            get => selectedRegisteredSymbol;
            set => SetProperty(ref selectedRegisteredSymbol, value);
        }

        public DataDefinition? SelectedDataDefinition
        {
            get => selectedDataDefinition;
            set => SetProperty(ref selectedDataDefinition, value);
        }

        public DataRecord? SelectedDataRecord
        {
            get => selectedDataRecord;
            set => SetProperty(ref selectedDataRecord, value);
        }

        public MainWindowViewModel()
        {
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-NORMAL", Type = "Line", LineRole = LineRoleType.Normal });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-WIRE-A", Type = "Line", LineRole = LineRoleType.WireA });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-WIRE-B", Type = "Line", LineRole = LineRoleType.WireB });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-WIRE-C", Type = "Line", LineRole = LineRoleType.WireC });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-BUS", Type = "Line", LineRole = LineRoleType.Bus });
            RegisteredShapes.Add(new ShapeDefinition { Id = "RECT-01", Type = "Rectangle" });
        }

        public ShapeDefinition RegisterShape(
            string id,
            string type,
            double fixedSize,
            double fixedHeight,
            int gridWidthCount,
            int gridHeightCount,
            LineRoleType lineRole,
            bool isLineGroupTarget,
            string lineGroupDataDefinitionName,
            bool autoCreateLineGroupData)
        {
            var definition = new ShapeDefinition
            {
                Id = id,
                Type = type,
                FixedSize = fixedSize,
                FixedHeight = fixedHeight,
                GridWidthCount = gridWidthCount,
                GridHeightCount = gridHeightCount,
                ConnectionPoints = new List<Point>(TempConnectionPoints),
                VectorElements = TempVectorElements
                    .Select(element => new SymbolVectorElement
                    {
                        Type = element.Type,
                        X1 = element.X1,
                        Y1 = element.Y1,
                        X2 = element.X2,
                        Y2 = element.Y2
                    })
                    .ToList(),
                Attributes = TempAttributes
                    .Select(attribute => new SymbolAttribute
                    {
                        Key = attribute.Key,
                        Value = attribute.Value
                    })
                    .ToList(),
                LineRole = lineRole,
                IsLineGroupTarget = isLineGroupTarget,
                LineGroupDataDefinitionName = lineGroupDataDefinitionName,
                AutoCreateLineGroupData = autoCreateLineGroupData
            };

            RegisteredShapes.Add(definition);
            if (definition.Type == "Symbol")
            {
                RegisteredSymbols.Add(definition);
                SelectedRegisteredSymbol = definition;
            }

            SelectedShape = definition;
            return definition;
        }

        public void ClearTempConnectionPoints()
        {
            TempConnectionPoints.Clear();
        }

        public void ClearTempVectorElements()
        {
            TempVectorElements.Clear();
        }

        public void ClearTempAttributes()
        {
            TempAttributes.Clear();
        }

        public void RefreshPlacedDrawings(IEnumerable<PlacedDrawingInfo> drawings)
        {
            PlacedSymbols.Clear();
            foreach (var drawing in drawings)
            {
                PlacedSymbols.Add(drawing);
            }
        }

        public void RefreshLineGroups(IEnumerable<PlacedDrawingInfo> groups)
        {
            LineGroups.Clear();
            foreach (var group in groups)
            {
                LineGroups.Add(group);
            }
        }

        public void ReplaceShapeDefinitions(IEnumerable<ShapeDefinition> definitions)
        {
            RegisteredShapes.Clear();
            RegisteredSymbols.Clear();

            foreach (var definition in definitions)
            {
                RegisteredShapes.Add(definition);
                if (definition.Type == "Symbol")
                {
                    RegisteredSymbols.Add(definition);
                }
            }

            SelectedShape = RegisteredShapes.FirstOrDefault();
            SelectedRegisteredSymbol = RegisteredSymbols.FirstOrDefault();
        }

        public DataDefinition RegisterDataDefinition(
            string name,
            string parentDefinitionName,
            string idItemName,
            bool usePythonIdGenerator,
            string pythonIdGeneratorCode)
        {
            var definition = new DataDefinition
            {
                Name = name,
                ParentDefinitionName = parentDefinitionName,
                IdItemName = idItemName,
                UsePythonIdGenerator = usePythonIdGenerator,
                PythonIdGeneratorCode = pythonIdGeneratorCode
            };
            foreach (var item in TempDataItems)
            {
                definition.Items.Add(item.Name);
                definition.ItemDefinitions.Add(new DataDefinitionItem
                {
                    Name = item.Name,
                    ReferenceDefinitionName = item.ReferenceDefinitionName,
                    UsePythonValueGenerator = item.UsePythonValueGenerator,
                    PythonValueGeneratorCode = item.PythonValueGeneratorCode
                });
            }

            DataDefinitions.Add(definition);
            SelectedDataDefinition = definition;
            return definition;
        }

        public void ReplaceDataDefinitions(IEnumerable<DataDefinition> definitions)
        {
            DataDefinitions.Clear();
            foreach (var definition in definitions)
            {
                DataDefinitions.Add(definition);
            }

            SelectedDataDefinition = DataDefinitions.FirstOrDefault();
        }

        public DataRecord RegisterDataRecord(string name, DataDefinition definition, IEnumerable<SymbolAttribute> attributes)
        {
            var record = new DataRecord
            {
                Name = name,
                DefinitionName = definition.Name,
                Attributes = attributes
                    .Select(attribute => new SymbolAttribute { Key = attribute.Key, Value = attribute.Value })
                    .ToList()
            };

            DataRecords.Add(record);
            SelectedDataRecord = record;
            return record;
        }

        public void ReplaceDataRecords(IEnumerable<DataRecord> records)
        {
            DataRecords.Clear();
            foreach (var record in records)
            {
                DataRecords.Add(record);
            }

            SelectedDataRecord = DataRecords.FirstOrDefault();
        }
    }
}

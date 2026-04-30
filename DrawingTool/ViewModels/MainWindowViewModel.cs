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
        public List<Point> TempConnectionPoints { get; } = new List<Point>();

        private ShapeDefinition? selectedShape;
        private ShapeDefinition? selectedRegisteredSymbol;

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

        public MainWindowViewModel()
        {
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-NORMAL", Type = "Line", LineRole = LineRoleType.Normal });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-WIRE-A", Type = "Line", LineRole = LineRoleType.WireA });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-WIRE-B", Type = "Line", LineRole = LineRoleType.WireB });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-WIRE-C", Type = "Line", LineRole = LineRoleType.WireC });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-BUS", Type = "Line", LineRole = LineRoleType.Bus });
            RegisteredShapes.Add(new ShapeDefinition { Id = "RECT-01", Type = "Rectangle" });
        }

        public ShapeDefinition RegisterShape(string id, string type, double fixedSize, double fixedHeight, int gridWidthCount, int gridHeightCount, LineRoleType lineRole)
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
                LineRole = lineRole
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

        public void RefreshPlacedDrawings(IEnumerable<PlacedDrawingInfo> drawings)
        {
            PlacedSymbols.Clear();
            foreach (var drawing in drawings)
            {
                PlacedSymbols.Add(drawing);
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
    }
}

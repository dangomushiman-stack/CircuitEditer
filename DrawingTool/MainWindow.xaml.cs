using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DrawingTool.Models;
using DrawingTool.ViewModels;
using Microsoft.Win32;

namespace DrawingTool
{
    public partial class MainWindow : Window
    {
        private class ConnectionNode
        {
            public Point Position { get; set; }
            public List<(Line Line, bool IsStart)> Endpoints { get; } = new List<(Line Line, bool IsStart)>();
        }

        private readonly MainWindowViewModel viewModel = new MainWindowViewModel();
        private Dictionary<Line, LineConnectionInfo> lineConnections = new Dictionary<Line, LineConnectionInfo>();
        private Dictionary<Line, ConnectionNode> lineStartNodes = new Dictionary<Line, ConnectionNode>();
        private Dictionary<Line, ConnectionNode> lineEndNodes = new Dictionary<Line, ConnectionNode>();
        private LineConnectionInfo wireCSplitTargets = null;

        private bool isDrawingOrMoving = false;
        private bool isResizing = false;
        private Point startPoint;
        private UIElement currentElement = null;
        private Rectangle selectionBox;
        private Line selectionLineHighlight;
        private Rectangle rectResizeHandle, lineStartHandle, lineEndHandle, activeHandle;
        private bool isDrawingEditorVector = false;
        private bool isLoadingSymbolDefinition = false;
        private Point editorVectorStart;
        private SymbolVectorElement editorVectorPreview = null;
        private const int GridSize = 20;
        private const int VectorGridSize = 5;

        private int GetSymbolGridWidthCount()
        {
            if (!int.TryParse(txtSymbolGridCount.Text, out int count)) return 3;
            return Math.Max(1, count);
        }

        private int GetSymbolGridHeightCount()
        {
            if (!int.TryParse(txtSymbolGridHeightCount.Text, out int count)) return 2;
            return Math.Max(1, count);
        }

        private double GetSymbolFixedWidth()
        {
            return GetSymbolGridWidthCount() * GridSize;
        }

        private double GetSymbolFixedHeight()
        {
            return GetSymbolGridHeightCount() * GridSize;
        }

        private bool IsAutoConnectionLine(ShapeDefinition def)
        {
            return def.LineRole == LineRoleType.WireA ||
                   def.LineRole == LineRoleType.WireB ||
                   def.LineRole == LineRoleType.WireC;
        }

        private Line CreateLineElement(ShapeDefinition def, Point start, Point end)
        {
            Brush brush = Brushes.Black;
            double thickness = 2;

            if (def.LineRole == LineRoleType.Bus)
            {
                brush = Brushes.Purple;
                thickness = 4;
            }
            else if (def.LineRole == LineRoleType.WireA)
            {
                brush = Brushes.Blue;
            }
            else if (def.LineRole == LineRoleType.WireB)
            {
                brush = Brushes.DeepPink;
            }
            else if (def.LineRole == LineRoleType.WireC)
            {
                brush = Brushes.DarkCyan;
            }

            return new Line
            {
                Stroke = brush,
                StrokeThickness = thickness,
                MinWidth = 12,
                MinHeight = 12,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Tag = def
            };
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = viewModel;
            InitializeSelectionHighlight();
            InitializeHandles();
            RefreshEditor();
            RefreshPlacedSymbols();
        }

        private void RefreshEditor() {
            if (EditorCanvas == null) return;
            EditorCanvas.Children.Clear();
            double width = GetSymbolFixedWidth();
            double height = GetSymbolFixedHeight();
            var guide = new Rectangle { Width = width, Height = height, Stroke = Brushes.LightBlue, StrokeDashArray = new DoubleCollection { 2, 2 } };
            Canvas.SetLeft(guide, 10); Canvas.SetTop(guide, 10); EditorCanvas.Children.Add(guide);
            for (double x = 0; x <= width; x += VectorGridSize) { for (double y = 0; y <= height; y += VectorGridSize) { var dot = new Ellipse { Width = 2, Height = 2, Fill = Brushes.Gainsboro }; Canvas.SetLeft(dot, x + 10 - 1); Canvas.SetTop(dot, y + 10 - 1); EditorCanvas.Children.Add(dot); } }
            for (double x = 0; x <= width; x += GridSize) { for (double y = 0; y <= height; y += GridSize) { var dot = new Ellipse { Width = 4, Height = 4, Fill = Brushes.LightGray }; Canvas.SetLeft(dot, x + 10 - 2); Canvas.SetTop(dot, y + 10 - 2); EditorCanvas.Children.Add(dot); } }
            foreach (var vectorElement in viewModel.TempVectorElements) { AddVectorElementToCanvas(EditorCanvas, vectorElement, 10, Brushes.Black, 2, 1); }
            if (editorVectorPreview != null) { AddVectorElementToCanvas(EditorCanvas, editorVectorPreview, 10, Brushes.DarkGreen, 2, 0.65); }
            foreach (var p in viewModel.TempConnectionPoints) { var connDot = new Ellipse { Width = 8, Height = 8, Fill = Brushes.Blue, Stroke = Brushes.White, StrokeThickness = 1 }; Canvas.SetLeft(connDot, p.X + 10 - 4); Canvas.SetTop(connDot, p.Y + 10 - 4); EditorCanvas.Children.Add(connDot); }
        }

        private void RefreshPlacedSymbols()
        {
            var placedDrawings = new List<PlacedDrawingInfo>();

            int no = 1;

            foreach (UIElement element in DrawCanvas.Children)
            {
                if (element is Canvas symbolCanvas &&
                    symbolCanvas.Tag is ShapeDefinition def &&
                    def.Type == "Symbol")
                {
                    placedDrawings.Add(new PlacedDrawingInfo
                    {
                        No = no++,
                        Id = def.Id,
                        Type = def.Type,
                        X = Canvas.GetLeft(symbolCanvas),
                        Y = Canvas.GetTop(symbolCanvas),
                        Size = def.FixedSize,
                        Height = def.FixedHeight,
                        GridWidthCount = def.GridWidthCount,
                        GridHeightCount = def.GridHeightCount,
                        ConnectionPointCount = def.ConnectionPoints.Count
                    });
                }
                else if (element is Line line &&
                         line.Tag is ShapeDefinition lineDef &&
                         lineDef.Type == "Line")
                {
                    placedDrawings.Add(new PlacedDrawingInfo
                    {
                        No = no++,
                        Id = lineDef.Id,
                        Type = lineDef.Type,
                        X = line.X1,
                        Y = line.Y1,
                        X2 = line.X2,
                        Y2 = line.Y2
                    });
                }
                else if (element is Rectangle rectangle &&
                         rectangle.Tag is ShapeDefinition rectDef &&
                         rectDef.Type == "Rectangle")
                {
                    placedDrawings.Add(new PlacedDrawingInfo
                    {
                        No = no++,
                        Id = rectDef.Id,
                        Type = rectDef.Type,
                        X = Canvas.GetLeft(rectangle),
                        Y = Canvas.GetTop(rectangle),
                        Width = rectangle.Width,
                        Height = rectangle.Height
                    });
                }
            }

            viewModel.RefreshPlacedDrawings(placedDrawings);
        }

        private void EditorCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            Point newPoint = GetEditorGridPoint(e.GetPosition(EditorCanvas));
            if (!IsPointInsideSymbolEditor(newPoint)) return;

            if (rbEditorVector.IsChecked == true)
            {
                editorVectorStart = newPoint;
                editorVectorPreview = CreateEditorVectorElement(editorVectorStart, newPoint);
                isDrawingEditorVector = true;
                EditorCanvas.CaptureMouse();
                RefreshEditor();
                return;
            }

            if (viewModel.TempConnectionPoints.Any(pt => pt == newPoint)) viewModel.TempConnectionPoints.Remove(newPoint); else viewModel.TempConnectionPoints.Add(newPoint);
            RefreshEditor();
        }

        private void EditorCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDrawingEditorVector)
            {
                return;
            }

            Point endPoint = GetEditorGridPoint(e.GetPosition(EditorCanvas));
            endPoint = ClampPointToSymbolEditor(endPoint);
            editorVectorPreview = CreateEditorVectorElement(editorVectorStart, endPoint);
            RefreshEditor();
        }

        private void EditorCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDrawingEditorVector)
            {
                return;
            }

            Point endPoint = ClampPointToSymbolEditor(GetEditorGridPoint(e.GetPosition(EditorCanvas)));
            var vectorElement = CreateEditorVectorElement(editorVectorStart, endPoint);
            if (HasVectorElementSize(vectorElement))
            {
                viewModel.TempVectorElements.Add(vectorElement);
            }

            isDrawingEditorVector = false;
            editorVectorPreview = null;
            EditorCanvas.ReleaseMouseCapture();
            RefreshEditor();
        }

        private void BtnUndoVector_Click(object sender, RoutedEventArgs e)
        {
            if (viewModel.TempVectorElements.Count == 0)
            {
                return;
            }

            viewModel.TempVectorElements.RemoveAt(viewModel.TempVectorElements.Count - 1);
            RefreshEditor();
        }

        private void BtnClearVector_Click(object sender, RoutedEventArgs e)
        {
            viewModel.ClearTempVectorElements();
            RefreshEditor();
        }

        private Point GetEditorGridPoint(Point editorPoint)
        {
            int gridSize = rbEditorVector?.IsChecked == true ? VectorGridSize : GridSize;
            return GetEditorGridPoint(editorPoint, gridSize);
        }

        private Point GetEditorGridPoint(Point editorPoint, int gridSize)
        {
            return new Point(
                Math.Round((editorPoint.X - 10) / gridSize) * gridSize,
                Math.Round((editorPoint.Y - 10) / gridSize) * gridSize);
        }

        private bool IsPointInsideSymbolEditor(Point point)
        {
            return point.X >= 0 &&
                   point.X <= GetSymbolFixedWidth() &&
                   point.Y >= 0 &&
                   point.Y <= GetSymbolFixedHeight();
        }

        private Point ClampPointToSymbolEditor(Point point)
        {
            return new Point(
                Math.Max(0, Math.Min(GetSymbolFixedWidth(), point.X)),
                Math.Max(0, Math.Min(GetSymbolFixedHeight(), point.Y)));
        }

        private SymbolVectorElement CreateEditorVectorElement(Point start, Point end)
        {
            string type = "Line";
            if (cmbVectorShape?.SelectedItem is ComboBoxItem item && item.Tag is string selectedType)
            {
                type = selectedType;
            }

            return new SymbolVectorElement
            {
                Type = type,
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y
            };
        }

        private bool HasVectorElementSize(SymbolVectorElement element)
        {
            return Math.Abs(element.X1 - element.X2) >= VectorGridSize ||
                   Math.Abs(element.Y1 - element.Y2) >= VectorGridSize;
        }

        private void AddVectorElementToCanvas(Canvas canvas, SymbolVectorElement element, double offset, Brush stroke, double strokeThickness, double opacity)
        {
            Shape shape;
            double left = Math.Min(element.X1, element.X2) + offset;
            double top = Math.Min(element.Y1, element.Y2) + offset;
            double width = Math.Abs(element.X1 - element.X2);
            double height = Math.Abs(element.Y1 - element.Y2);

            if (element.Type == "Rectangle")
            {
                shape = new Rectangle
                {
                    Width = Math.Max(1, width),
                    Height = Math.Max(1, height),
                    Stroke = stroke,
                    StrokeThickness = strokeThickness,
                    Fill = Brushes.Transparent,
                    Opacity = opacity
                };
                Canvas.SetLeft(shape, left);
                Canvas.SetTop(shape, top);
            }
            else if (element.Type == "Ellipse")
            {
                shape = new Ellipse
                {
                    Width = Math.Max(1, width),
                    Height = Math.Max(1, height),
                    Stroke = stroke,
                    StrokeThickness = strokeThickness,
                    Fill = Brushes.Transparent,
                    Opacity = opacity
                };
                Canvas.SetLeft(shape, left);
                Canvas.SetTop(shape, top);
            }
            else
            {
                shape = new Line
                {
                    X1 = element.X1 + offset,
                    Y1 = element.Y1 + offset,
                    X2 = element.X2 + offset,
                    Y2 = element.Y2 + offset,
                    Stroke = stroke,
                    StrokeThickness = strokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Opacity = opacity
                };
            }

            shape.IsHitTestVisible = false;
            canvas.Children.Add(shape);
        }

        private void TxtSymbolGridCount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isLoadingSymbolDefinition)
            {
                RefreshEditor();
                return;
            }

            viewModel.ClearTempConnectionPoints();
            viewModel.ClearTempVectorElements();
            RefreshEditor();
        }
        
        private void CmbShapeType_SelectionChanged(object sender, SelectionChangedEventArgs e) { 
            if (SymbolSettings == null || LineSettings == null) return;
            int idx = cmbShapeType.SelectedIndex;
            LineSettings.Visibility = (idx == 0) ? Visibility.Visible : Visibility.Collapsed;
            SymbolSettings.Visibility = (idx == 2) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnRegisterShape_Click(object sender, RoutedEventArgs e) {
            LineRoleType role = LineRoleType.Normal;
            if (rbLineWireA.IsChecked == true) role = LineRoleType.WireA;
            else if (rbLineWireB.IsChecked == true) role = LineRoleType.WireB;
            else if (rbLineWireC.IsChecked == true) role = LineRoleType.WireC;
            else if (rbLineBus.IsChecked == true) role = LineRoleType.Bus;

            var def = viewModel.RegisterShape(
                txtShapeId.Text,
                ((ComboBoxItem)cmbShapeType.SelectedItem).Tag.ToString(),
                GetSymbolFixedWidth(),
                GetSymbolFixedHeight(),
                GetSymbolGridWidthCount(),
                GetSymbolGridHeightCount(),
                role);

            if (def.Type == "Symbol")
            {
                lstRegisteredSymbols.SelectedItem = def;
                RightTabControl.SelectedIndex = 1; // 登録直後に「シンボル一覧」タブで確認できるようにする
            }
            else
            {
                RightTabControl.SelectedIndex = 0;
            }

            lstShapes.SelectedItem = def;
        }

        private void LstRegisteredSymbols_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstRegisteredSymbols.SelectedItem is ShapeDefinition def)
            {
                lstShapes.SelectedItem = def;
            }
        }

        private void BtnLoadSelectedSymbol_Click(object sender, RoutedEventArgs e)
        {
            if (lstRegisteredSymbols.SelectedItem is not ShapeDefinition definition)
            {
                MessageBox.Show(this, "編集するシンボルをシンボル一覧で選択してください。", "シンボル編集", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LoadSymbolDefinitionToEditor(definition);
        }

        private void BtnUpdateSelectedSymbol_Click(object sender, RoutedEventArgs e)
        {
            if (lstRegisteredSymbols.SelectedItem is not ShapeDefinition definition)
            {
                MessageBox.Show(this, "更新するシンボルをシンボル一覧で選択してください。", "シンボル編集", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UpdateSymbolDefinitionFromEditor(definition);
            RebuildPlacedSymbolsForDefinition(definition);
            lstRegisteredSymbols.Items.Refresh();
            lstShapes.Items.Refresh();
            RefreshPlacedSymbols();
        }

        private void LoadSymbolDefinitionToEditor(ShapeDefinition definition)
        {
            isLoadingSymbolDefinition = true;
            try
            {
                txtShapeId.Text = definition.Id;
                cmbShapeType.SelectedIndex = 2;
                txtSymbolGridCount.Text = Math.Max(1, definition.GridWidthCount).ToString();
                txtSymbolGridHeightCount.Text = Math.Max(1, definition.GridHeightCount).ToString();

                viewModel.TempConnectionPoints.Clear();
                viewModel.TempConnectionPoints.AddRange(definition.ConnectionPoints);
                viewModel.TempVectorElements.Clear();
                viewModel.TempVectorElements.AddRange(definition.VectorElements.Select(CloneVectorElement));
                rbEditorPorts.IsChecked = true;
            }
            finally
            {
                isLoadingSymbolDefinition = false;
            }

            RightTabControl.SelectedIndex = 2;
            RefreshEditor();
        }

        private void UpdateSymbolDefinitionFromEditor(ShapeDefinition definition)
        {
            definition.Id = txtShapeId.Text;
            definition.Type = "Symbol";
            definition.FixedSize = GetSymbolFixedWidth();
            definition.FixedHeight = GetSymbolFixedHeight();
            definition.GridWidthCount = GetSymbolGridWidthCount();
            definition.GridHeightCount = GetSymbolGridHeightCount();
            definition.ConnectionPoints = new List<Point>(viewModel.TempConnectionPoints);
            definition.VectorElements = viewModel.TempVectorElements.Select(CloneVectorElement).ToList();
            definition.LineRole = LineRoleType.Normal;
        }

        private SymbolVectorElement CloneVectorElement(SymbolVectorElement element)
        {
            return new SymbolVectorElement
            {
                Type = element.Type,
                X1 = element.X1,
                Y1 = element.Y1,
                X2 = element.X2,
                Y2 = element.Y2
            };
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedElement();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete || e.OriginalSource is TextBox)
            {
                return;
            }

            DeleteSelectedElement();
            e.Handled = true;
        }

        private void DeleteSelectedElement()
        {
            if (currentElement == null || !IsSavedDrawingElement(currentElement))
            {
                return;
            }

            var deletedElement = currentElement;
            RemoveConnectionsForDeletedElement(deletedElement);
            DrawCanvas.Children.Remove(deletedElement);

            currentElement = null;
            isDrawingOrMoving = false;
            isResizing = false;
            activeHandle = null;
            wireCSplitTargets = null;
            HideAllHandles();
            HideSelectionHighlight();
            RefreshPlacedSymbols();
        }

        private void RemoveConnectionsForDeletedElement(UIElement deletedElement)
        {
            if (deletedElement is Line deletedLine)
            {
                RemoveEndpointFromNode(deletedLine, true);
                RemoveEndpointFromNode(deletedLine, false);
                lineConnections.Remove(deletedLine);
            }

            foreach (var connection in lineConnections.Values)
            {
                if (connection.StartElement == deletedElement)
                {
                    connection.StartElement = null;
                    connection.StartRelPoint = new Point();
                    connection.StartLineRatio = 0;
                }

                if (connection.EndElement == deletedElement)
                {
                    connection.EndElement = null;
                    connection.EndRelPoint = new Point();
                    connection.EndLineRatio = 0;
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "図面データを保存",
                Filter = "Drawing Tool JSON (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json",
                FileName = $"drawing-{DateTime.Now:yyyyMMdd-HHmmss}.json"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var saveData = CreateSaveData();
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(saveData, options));
            MessageBox.Show(this, "保存しました。", "保存", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "図面データを開く",
                Filter = "Drawing Tool JSON (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(dialog.FileName);
                var saveData = JsonSerializer.Deserialize<DrawingSaveData>(json);
                if (saveData == null)
                {
                    MessageBox.Show(this, "ファイルを読み込めませんでした。", "開く", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                LoadSaveData(saveData);
                MessageBox.Show(this, "読み込みました。", "開く", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"ファイルを開けませんでした。\n{ex.Message}", "開く", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSaveData(DrawingSaveData saveData)
        {
            HideAllHandles();
            currentElement = null;
            isDrawingOrMoving = false;
            isResizing = false;
            activeHandle = null;
            wireCSplitTargets = null;
            HideSelectionHighlight();
            lineConnections.Clear();
            lineStartNodes.Clear();
            lineEndNodes.Clear();

            foreach (var element in DrawCanvas.Children.OfType<UIElement>().Where(IsSavedDrawingElement).ToList())
            {
                DrawCanvas.Children.Remove(element);
            }

            var definitions = saveData.ShapeDefinitions
                .Select(CreateShapeDefinition)
                .ToList();
            viewModel.ReplaceShapeDefinitions(definitions);

            var definitionById = definitions
                .Where(definition => !string.IsNullOrWhiteSpace(definition.Id))
                .GroupBy(definition => definition.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var elementsByItemNo = new Dictionary<int, UIElement>();

            foreach (var item in saveData.Items.OrderBy(item => item.ItemNo))
            {
                var definition = GetDefinitionForLoadedItem(item, definitionById);
                var element = CreateElementFromSavedItem(item, definition);
                DrawCanvas.Children.Add(element);
                elementsByItemNo[item.ItemNo] = element;
            }

            RestoreLineConnections(saveData, elementsByItemNo);
            RestoreConnectionNodes(saveData, elementsByItemNo);

            foreach (var line in elementsByItemNo.Values.OfType<Line>())
            {
                ApplyOwnLineConnections(line);
                UpdateConnectedLines(line);
            }

            rbSelect.IsChecked = true;
            RefreshPlacedSymbols();
        }

        private ShapeDefinition CreateShapeDefinition(SavedShapeDefinition savedDefinition)
        {
            Enum.TryParse(savedDefinition.LineRole, out LineRoleType lineRole);
            int gridWidthCount = savedDefinition.GridWidthCount > 0 ? savedDefinition.GridWidthCount : 3;
            int gridHeightCount = savedDefinition.GridHeightCount > 0 ? savedDefinition.GridHeightCount : gridWidthCount;
            double fixedSize = savedDefinition.FixedSize > 0 ? savedDefinition.FixedSize : gridWidthCount * GridSize;
            double fixedHeight = savedDefinition.FixedHeight > 0 ? savedDefinition.FixedHeight : gridHeightCount * GridSize;

            return new ShapeDefinition
            {
                Id = savedDefinition.Id,
                Type = savedDefinition.Type,
                FixedSize = fixedSize,
                FixedHeight = fixedHeight,
                GridWidthCount = gridWidthCount,
                GridHeightCount = gridHeightCount,
                LineRole = lineRole,
                ConnectionPoints = savedDefinition.ConnectionPoints
                    .Select(point => new Point(point.X, point.Y))
                    .ToList(),
                VectorElements = savedDefinition.VectorElements
                    .Select(CreateVectorElement)
                    .ToList()
            };
        }

        private SymbolVectorElement CreateVectorElement(SavedSymbolVectorElement element)
        {
            return new SymbolVectorElement
            {
                Type = element.Type,
                X1 = element.X1,
                Y1 = element.Y1,
                X2 = element.X2,
                Y2 = element.Y2
            };
        }

        private SavedSymbolVectorElement CreateSavedVectorElement(SymbolVectorElement element)
        {
            return new SavedSymbolVectorElement
            {
                Type = element.Type,
                X1 = element.X1,
                Y1 = element.Y1,
                X2 = element.X2,
                Y2 = element.Y2
            };
        }

        private ShapeDefinition GetDefinitionForLoadedItem(SavedDrawingItem item, Dictionary<string, ShapeDefinition> definitionById)
        {
            if (definitionById.TryGetValue(item.DefinitionId, out var definition))
            {
                return definition;
            }

            return new ShapeDefinition
            {
                Id = item.DefinitionId,
                Type = item.Type,
                FixedSize = item.Width,
                FixedHeight = item.Height
            };
        }

        private UIElement CreateElementFromSavedItem(SavedDrawingItem item, ShapeDefinition definition)
        {
            if (item.Type == "Line")
            {
                return CreateLineElement(definition, new Point(item.X, item.Y), new Point(item.X2, item.Y2));
            }

            if (item.Type == "Symbol")
            {
                if (definition.VectorElements.Count == 0 && item.VectorElements.Count > 0)
                {
                    definition.VectorElements = item.VectorElements.Select(CreateVectorElement).ToList();
                }

                return CreateSymbolElement(definition, item.X, item.Y, item.Width, item.Height);
            }

            return CreateRectangleElement(definition, item.X, item.Y, item.Width, item.Height);
        }

        private Rectangle CreateRectangleElement(ShapeDefinition definition, double x, double y, double width, double height)
        {
            var rectangle = new Rectangle
            {
                Width = Math.Max(GridSize, width),
                Height = Math.Max(GridSize, height),
                Stroke = Brushes.Blue,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(100, 173, 216, 230)),
                Tag = definition
            };
            Canvas.SetLeft(rectangle, x);
            Canvas.SetTop(rectangle, y);
            return rectangle;
        }

        private Canvas CreateSymbolElement(ShapeDefinition definition, double x, double y, double width, double height)
        {
            double symbolWidth = width > 0 ? width : definition.FixedSize;
            double symbolHeight = height > 0 ? height : (definition.FixedHeight > 0 ? definition.FixedHeight : definition.FixedSize);
            var symbol = new Canvas { Width = symbolWidth, Height = symbolHeight, Tag = definition };
            PopulateSymbolCanvas(symbol, definition, symbolWidth, symbolHeight);

            Canvas.SetLeft(symbol, x);
            Canvas.SetTop(symbol, y);
            return symbol;
        }

        private void PopulateSymbolCanvas(Canvas symbol, ShapeDefinition definition, double symbolWidth, double symbolHeight)
        {
            symbol.Children.Clear();
            symbol.Children.Add(new Rectangle
            {
                Width = symbolWidth,
                Height = symbolHeight,
                Stroke = Brushes.DarkOrange,
                StrokeThickness = 2,
                Fill = Brushes.Orange
            });

            foreach (var vectorElement in definition.VectorElements)
            {
                AddVectorElementToCanvas(symbol, vectorElement, 0, Brushes.Black, 2, 1);
            }

            foreach (var connectionPoint in definition.ConnectionPoints)
            {
                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Brushes.Blue,
                    Stroke = Brushes.White,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(dot, connectionPoint.X - 3);
                Canvas.SetTop(dot, connectionPoint.Y - 3);
                symbol.Children.Add(dot);
            }
        }

        private void RebuildPlacedSymbolsForDefinition(ShapeDefinition definition)
        {
            foreach (UIElement element in DrawCanvas.Children)
            {
                if (element is not Canvas symbol || !ReferenceEquals(symbol.Tag, definition))
                {
                    continue;
                }

                symbol.Width = definition.FixedSize;
                symbol.Height = definition.FixedHeight > 0 ? definition.FixedHeight : definition.FixedSize;
                PopulateSymbolCanvas(symbol, definition, symbol.Width, symbol.Height);
                UpdateConnectedLines(symbol);
            }
        }

        private void RestoreLineConnections(DrawingSaveData saveData, Dictionary<int, UIElement> elementsByItemNo)
        {
            foreach (var item in saveData.Items)
            {
                if (!elementsByItemNo.TryGetValue(item.ItemNo, out var element) || element is not Line line)
                {
                    continue;
                }

                var connection = new LineConnectionInfo();
                RestoreEndpointConnection(item.StartConnection, connection, true, elementsByItemNo);
                RestoreEndpointConnection(item.EndConnection, connection, false, elementsByItemNo);

                if (connection.StartElement != null || connection.EndElement != null)
                {
                    lineConnections[line] = connection;
                }
            }
        }

        private void RestoreEndpointConnection(
            SavedLineEndpointConnection? savedConnection,
            LineConnectionInfo connection,
            bool isStart,
            Dictionary<int, UIElement> elementsByItemNo)
        {
            if (savedConnection == null ||
                !elementsByItemNo.TryGetValue(savedConnection.TargetItemNo, out var targetElement))
            {
                return;
            }

            if (isStart)
            {
                connection.StartElement = targetElement;
                connection.StartRelPoint = new Point(savedConnection.RelativeX, savedConnection.RelativeY);
                connection.StartLineRatio = savedConnection.LineRatio;
            }
            else
            {
                connection.EndElement = targetElement;
                connection.EndRelPoint = new Point(savedConnection.RelativeX, savedConnection.RelativeY);
                connection.EndLineRatio = savedConnection.LineRatio;
            }
        }

        private void RestoreConnectionNodes(DrawingSaveData saveData, Dictionary<int, UIElement> elementsByItemNo)
        {
            foreach (var savedNode in saveData.ConnectionNodes.OrderBy(node => node.NodeId))
            {
                var node = new ConnectionNode { Position = new Point(savedNode.X, savedNode.Y) };
                foreach (var endpoint in savedNode.Endpoints)
                {
                    if (elementsByItemNo.TryGetValue(endpoint.ItemNo, out var element) && element is Line line)
                    {
                        AttachEndpointToNode(line, endpoint.IsStart, node);
                    }
                }
            }
        }

        private DrawingSaveData CreateSaveData()
        {
            var saveData = new DrawingSaveData();
            var itemNumbers = new Dictionary<UIElement, int>();
            var nodeNumbers = new Dictionary<ConnectionNode, int>();

            int itemNo = 1;
            foreach (UIElement element in DrawCanvas.Children)
            {
                if (IsSavedDrawingElement(element))
                {
                    itemNumbers[element] = itemNo++;
                }
            }

            int nodeNo = 1;
            foreach (var node in lineStartNodes.Values.Concat(lineEndNodes.Values).Distinct())
            {
                nodeNumbers[node] = nodeNo++;
            }

            foreach (var definition in viewModel.RegisteredShapes)
            {
                saveData.ShapeDefinitions.Add(new SavedShapeDefinition
                {
                    Id = definition.Id,
                    Type = definition.Type,
                    FixedSize = definition.FixedSize,
                    FixedHeight = definition.FixedHeight,
                    GridWidthCount = definition.GridWidthCount,
                    GridHeightCount = definition.GridHeightCount,
                    LineRole = definition.LineRole.ToString(),
                    ConnectionPoints = definition.ConnectionPoints
                        .Select(point => new SavedPoint { X = point.X, Y = point.Y })
                        .ToList(),
                    VectorElements = definition.VectorElements
                        .Select(CreateSavedVectorElement)
                        .ToList()
                });
            }

            foreach (var nodePair in nodeNumbers.OrderBy(pair => pair.Value))
            {
                var node = nodePair.Key;
                saveData.ConnectionNodes.Add(new SavedConnectionNode
                {
                    NodeId = nodePair.Value,
                    X = node.Position.X,
                    Y = node.Position.Y,
                    Endpoints = node.Endpoints
                        .Where(endpoint => itemNumbers.ContainsKey(endpoint.Line))
                        .Select(endpoint => new SavedNodeEndpoint
                        {
                            ItemNo = itemNumbers[endpoint.Line],
                            IsStart = endpoint.IsStart
                        })
                        .ToList()
                });
            }

            foreach (var itemPair in itemNumbers.OrderBy(pair => pair.Value))
            {
                saveData.Items.Add(CreateSavedDrawingItem(itemPair.Key, itemPair.Value, itemNumbers, nodeNumbers));
            }

            return saveData;
        }

        private bool IsSavedDrawingElement(UIElement element)
        {
            return element switch
            {
                Canvas canvas => canvas.Tag is ShapeDefinition,
                Line line => line.Tag is ShapeDefinition,
                Rectangle rectangle => rectangle.Tag is ShapeDefinition,
                _ => false
            };
        }

        private SavedDrawingItem CreateSavedDrawingItem(
            UIElement element,
            int itemNo,
            Dictionary<UIElement, int> itemNumbers,
            Dictionary<ConnectionNode, int> nodeNumbers)
        {
            var definition = (ShapeDefinition)((FrameworkElement)element).Tag;
            var item = new SavedDrawingItem
            {
                ItemNo = itemNo,
                DefinitionId = definition.Id,
                Type = definition.Type
            };

            if (element is Canvas canvas)
            {
                item.X = Canvas.GetLeft(canvas);
                item.Y = Canvas.GetTop(canvas);
                item.Width = canvas.Width;
                item.Height = canvas.Height;
                item.VectorElements = definition.VectorElements
                    .Select(CreateSavedVectorElement)
                    .ToList();
            }
            else if (element is Rectangle rectangle)
            {
                item.X = Canvas.GetLeft(rectangle);
                item.Y = Canvas.GetTop(rectangle);
                item.Width = rectangle.Width;
                item.Height = rectangle.Height;
            }
            else if (element is Line line)
            {
                item.X = line.X1;
                item.Y = line.Y1;
                item.X2 = line.X2;
                item.Y2 = line.Y2;

                if (GetEndpointNode(line, true) is ConnectionNode startNode && nodeNumbers.TryGetValue(startNode, out int startNodeId))
                {
                    item.StartNodeId = startNodeId;
                }

                if (GetEndpointNode(line, false) is ConnectionNode endNode && nodeNumbers.TryGetValue(endNode, out int endNodeId))
                {
                    item.EndNodeId = endNodeId;
                }

                if (lineConnections.TryGetValue(line, out var connection))
                {
                    item.StartConnection = CreateSavedEndpointConnection(connection.StartElement, connection.StartRelPoint, connection.StartLineRatio, itemNumbers);
                    item.EndConnection = CreateSavedEndpointConnection(connection.EndElement, connection.EndRelPoint, connection.EndLineRatio, itemNumbers);
                }
            }

            return item;
        }

        private SavedLineEndpointConnection? CreateSavedEndpointConnection(
            UIElement? targetElement,
            Point relativePoint,
            double lineRatio,
            Dictionary<UIElement, int> itemNumbers)
        {
            if (targetElement == null || !itemNumbers.TryGetValue(targetElement, out int targetItemNo))
            {
                return null;
            }

            return new SavedLineEndpointConnection
            {
                TargetItemNo = targetItemNo,
                TargetKind = targetElement is Line ? "Line" : "Symbol",
                RelativeX = relativePoint.X,
                RelativeY = relativePoint.Y,
                LineRatio = lineRatio
            };
        }

        // ★スナップ判定：WireA / WireB / WireC でターゲットの許可条件を変える
        private (Point SnapPt, UIElement Element, Point RelPt, double Ratio) GetSnappedPointForLine(Point rawPoint, ShapeDefinition draggingLineDef)
        {
            Point p = SnapToGrid(rawPoint);
            // WireA / WireB / WireC でなければ吸着しない
            if (draggingLineDef == null ||
                (draggingLineDef.LineRole != LineRoleType.WireA &&
                 draggingLineDef.LineRole != LineRoleType.WireB &&
                 draggingLineDef.LineRole != LineRoleType.WireC))
                return (p, null, new Point(), 0);

            double minDistance = 15;
            Point bestPoint = p;
            UIElement bestElement = null;
            Point bestRelPoint = new Point();
            double bestRatio = 0;

            foreach (UIElement element in DrawCanvas.Children) {
                // 1. シンボルのポート（A/B/C が吸着可）
                if (element is Canvas symCanvas && symCanvas.Tag is ShapeDefinition def && def.Type == "Symbol") {
                    double left = Canvas.GetLeft(symCanvas); double top = Canvas.GetTop(symCanvas);
                    foreach (var cp in def.ConnectionPoints) {
                        double cx = left + cp.X; double cy = top + cp.Y;
                        double dist = Math.Sqrt(Math.Pow(rawPoint.X - cx, 2) + Math.Pow(rawPoint.Y - cy, 2));
                        if (dist < minDistance) { minDistance = dist; bestPoint = new Point(cx, cy); bestElement = symCanvas; bestRelPoint = cp; bestRatio = 0; }
                    }
                }
                // 2. 他の線分への吸着
                else if (element is Line targetLine && targetLine != currentElement && targetLine.Tag is ShapeDefinition targetDef) {
                    
                    bool canSnapToTarget = false;
                    if (targetDef.LineRole == LineRoleType.Bus) {
                        canSnapToTarget = true; // A/B/C はバスに吸着できる
                    }
                    else if (targetDef.LineRole == LineRoleType.WireB && draggingLineDef.LineRole == LineRoleType.WireB) {
                        canSnapToTarget = true; // ★WireB は 他の WireB に吸着できる
                    }
                    else if (draggingLineDef.LineRole == LineRoleType.WireC) {
                        canSnapToTarget = true; // ★WireC は線へ結合し、完了時に相手線を分割する
                    }

                    if (canSnapToTarget) {
                        Point A = new Point(targetLine.X1, targetLine.Y1);
                        Point B = new Point(targetLine.X2, targetLine.Y2);
                        
                        double l2 = Math.Pow(A.X - B.X, 2) + Math.Pow(A.Y - B.Y, 2);
                        if (l2 == 0) continue;

                        double t = Math.Max(0, Math.Min(1, ((rawPoint.X - A.X) * (B.X - A.X) + (rawPoint.Y - A.Y) * (B.Y - A.Y)) / l2));
                        Point projection = new Point(A.X + t * (B.X - A.X), A.Y + t * (B.Y - A.Y));
                        
                        double dist = Math.Sqrt(Math.Pow(rawPoint.X - projection.X, 2) + Math.Pow(rawPoint.Y - projection.Y, 2));
                        if (dist < minDistance) { minDistance = dist; bestPoint = projection; bestElement = targetLine; bestRatio = t; bestRelPoint = new Point(); }
                    }
                }
            }
            return (bestPoint, bestElement, bestRelPoint, bestRatio);
        }

        private bool IsInteriorLinePoint(double ratio)
        {
            const double epsilon = 0.0001;
            return ratio > epsilon && ratio < 1 - epsilon;
        }

        private bool IsLineEndpointRatio(double ratio)
        {
            const double epsilon = 0.0001;
            return Math.Abs(ratio) < epsilon || Math.Abs(ratio - 1) < epsilon;
        }

        private Point RoundPointToGrid(Point p)
        {
            return new Point(
                Math.Round(p.X / GridSize) * GridSize,
                Math.Round(p.Y / GridSize) * GridSize);
        }

        private Line CloneLineSegment(Line source, Point start, Point end)
        {
            return new Line
            {
                Stroke = source.Stroke,
                StrokeThickness = source.StrokeThickness,
                MinWidth = source.MinWidth,
                MinHeight = source.MinHeight,
                StrokeStartLineCap = source.StrokeStartLineCap,
                StrokeEndLineCap = source.StrokeEndLineCap,
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Tag = source.Tag
            };
        }

        private void MoveConnectionsFromSplitLine(Line oldLine, Line firstSegment, Line secondSegment, double splitRatio)
        {
            lineConnections.TryGetValue(oldLine, out var oldLineConnection);

            if (lineConnections.ContainsKey(oldLine))
            {
                lineConnections.Remove(oldLine);
            }

            foreach (var info in lineConnections.Values)
            {
                if (info.StartElement == oldLine)
                {
                    if (info.StartLineRatio <= splitRatio)
                    {
                        info.StartElement = firstSegment;
                        info.StartLineRatio = splitRatio == 0 ? 0 : info.StartLineRatio / splitRatio;
                    }
                    else
                    {
                        info.StartElement = secondSegment;
                        info.StartLineRatio = (info.StartLineRatio - splitRatio) / (1 - splitRatio);
                    }
                }

                if (info.EndElement == oldLine)
                {
                    if (info.EndLineRatio <= splitRatio)
                    {
                        info.EndElement = firstSegment;
                        info.EndLineRatio = splitRatio == 0 ? 0 : info.EndLineRatio / splitRatio;
                    }
                    else
                    {
                        info.EndElement = secondSegment;
                        info.EndLineRatio = (info.EndLineRatio - splitRatio) / (1 - splitRatio);
                    }
                }
            }

            if (oldLineConnection != null)
            {
                if (oldLineConnection.StartElement != null)
                {
                    lineConnections[firstSegment] = new LineConnectionInfo
                    {
                        StartElement = oldLineConnection.StartElement,
                        StartRelPoint = oldLineConnection.StartRelPoint,
                        StartLineRatio = oldLineConnection.StartLineRatio
                    };
                }

                if (oldLineConnection.EndElement != null)
                {
                    lineConnections[secondSegment] = new LineConnectionInfo
                    {
                        EndElement = oldLineConnection.EndElement,
                        EndRelPoint = oldLineConnection.EndRelPoint,
                        EndLineRatio = oldLineConnection.EndLineRatio
                    };
                }
            }
        }

        private void EnsureLineConnection(Line line)
        {
            if (!lineConnections.ContainsKey(line))
            {
                lineConnections[line] = new LineConnectionInfo();
            }
        }

        private void ClearLineConnectionEndpoint(Line line, bool isStart)
        {
            if (!lineConnections.TryGetValue(line, out var info))
            {
                return;
            }

            if (isStart)
            {
                info.StartElement = null;
                info.StartRelPoint = new Point();
                info.StartLineRatio = 0;
            }
            else
            {
                info.EndElement = null;
                info.EndRelPoint = new Point();
                info.EndLineRatio = 0;
            }
        }

        private ConnectionNode GetEndpointNode(Line line, bool isStart)
        {
            if (isStart)
            {
                return lineStartNodes.TryGetValue(line, out var startNode) ? startNode : null;
            }

            return lineEndNodes.TryGetValue(line, out var endNode) ? endNode : null;
        }

        private void RemoveEndpointFromNode(Line line, bool isStart)
        {
            var node = GetEndpointNode(line, isStart);
            if (node == null)
            {
                return;
            }

            node.Endpoints.RemoveAll(endpoint => endpoint.Line == line && endpoint.IsStart == isStart);
            if (isStart)
            {
                lineStartNodes.Remove(line);
            }
            else
            {
                lineEndNodes.Remove(line);
            }
        }

        private void AttachEndpointToNode(Line line, bool isStart, ConnectionNode node)
        {
            RemoveEndpointFromNode(line, isStart);
            ClearLineConnectionEndpoint(line, isStart);

            if (!node.Endpoints.Any(endpoint => endpoint.Line == line && endpoint.IsStart == isStart))
            {
                node.Endpoints.Add((line, isStart));
            }

            if (isStart)
            {
                lineStartNodes[line] = node;
                line.X1 = node.Position.X;
                line.Y1 = node.Position.Y;
            }
            else
            {
                lineEndNodes[line] = node;
                line.X2 = node.Position.X;
                line.Y2 = node.Position.Y;
            }
        }

        private void MoveConnectionNode(ConnectionNode node, Point newPosition)
        {
            node.Position = newPosition;

            foreach (var endpoint in node.Endpoints.ToList())
            {
                if (endpoint.IsStart)
                {
                    endpoint.Line.X1 = newPosition.X;
                    endpoint.Line.Y1 = newPosition.Y;
                }
                else
                {
                    endpoint.Line.X2 = newPosition.X;
                    endpoint.Line.Y2 = newPosition.Y;
                }
            }

            foreach (var endpoint in node.Endpoints.ToList())
            {
                UpdateConnectedLines(endpoint.Line);
            }
        }

        private Point GetConnectedPoint(UIElement targetElement, Point relPoint, double lineRatio)
        {
            if (targetElement is Canvas symbol)
            {
                return new Point(Canvas.GetLeft(symbol) + relPoint.X, Canvas.GetTop(symbol) + relPoint.Y);
            }

            if (targetElement is Line targetLine)
            {
                return new Point(
                    targetLine.X1 + lineRatio * (targetLine.X2 - targetLine.X1),
                    targetLine.Y1 + lineRatio * (targetLine.Y2 - targetLine.Y1));
            }

            return new Point();
        }

        private void ApplyOwnLineConnections(Line line)
        {
            if (!lineConnections.TryGetValue(line, out var info))
            {
                var startNodeOnly = GetEndpointNode(line, true);
                if (startNodeOnly != null)
                {
                    line.X1 = startNodeOnly.Position.X;
                    line.Y1 = startNodeOnly.Position.Y;
                }

                var endNodeOnly = GetEndpointNode(line, false);
                if (endNodeOnly != null)
                {
                    line.X2 = endNodeOnly.Position.X;
                    line.Y2 = endNodeOnly.Position.Y;
                }

                return;
            }

            var startNode = GetEndpointNode(line, true);
            if (startNode != null)
            {
                line.X1 = startNode.Position.X;
                line.Y1 = startNode.Position.Y;
            }
            else if (info.StartElement != null)
            {
                Point p = GetConnectedPoint(info.StartElement, info.StartRelPoint, info.StartLineRatio);
                line.X1 = p.X;
                line.Y1 = p.Y;
            }

            var endNode = GetEndpointNode(line, false);
            if (endNode != null)
            {
                line.X2 = endNode.Position.X;
                line.Y2 = endNode.Position.Y;
            }
            else if (info.EndElement != null)
            {
                Point p = GetConnectedPoint(info.EndElement, info.EndRelPoint, info.EndLineRatio);
                line.X2 = p.X;
                line.Y2 = p.Y;
            }
        }

        private bool TryMoveConnectedLineEndpoint(Line line, bool isStartHandle, Point newPoint)
        {
            var node = GetEndpointNode(line, isStartHandle);
            if (node != null)
            {
                MoveConnectionNode(node, newPoint);
                return true;
            }

            if (!lineConnections.TryGetValue(line, out var info))
            {
                return false;
            }

            UIElement connectedElement = isStartHandle ? info.StartElement : info.EndElement;
            double ratio = isStartHandle ? info.StartLineRatio : info.EndLineRatio;

            if (connectedElement is not Line connectedLine || !IsLineEndpointRatio(ratio))
            {
                return false;
            }

            if (Math.Abs(ratio) < 0.0001)
            {
                connectedLine.X1 = newPoint.X;
                connectedLine.Y1 = newPoint.Y;
            }
            else
            {
                connectedLine.X2 = newPoint.X;
                connectedLine.Y2 = newPoint.Y;
            }

            ApplyOwnLineConnections(connectedLine);
            UpdateConnectedLines(connectedLine);
            ApplyOwnLineConnections(line);
            UpdateConnectedLines(line);
            return true;
        }

        private (Line FirstSegment, Line SecondSegment) SplitLineAtRatio(Line targetLine, double ratio)
        {
            if (!IsInteriorLinePoint(ratio))
            {
                return (targetLine, null);
            }

            Point start = new Point(targetLine.X1, targetLine.Y1);
            Point end = new Point(targetLine.X2, targetLine.Y2);
            Point splitPoint = new Point(
                targetLine.X1 + ratio * (targetLine.X2 - targetLine.X1),
                targetLine.Y1 + ratio * (targetLine.Y2 - targetLine.Y1));
            splitPoint = RoundPointToGrid(splitPoint);
            var originalStartNode = GetEndpointNode(targetLine, true);
            var originalEndNode = GetEndpointNode(targetLine, false);

            var firstSegment = CloneLineSegment(targetLine, start, splitPoint);
            var secondSegment = CloneLineSegment(targetLine, splitPoint, end);
            int index = DrawCanvas.Children.IndexOf(targetLine);

            RemoveEndpointFromNode(targetLine, true);
            RemoveEndpointFromNode(targetLine, false);
            DrawCanvas.Children.Remove(targetLine);
            if (index < 0 || index > DrawCanvas.Children.Count)
            {
                DrawCanvas.Children.Add(firstSegment);
                DrawCanvas.Children.Add(secondSegment);
            }
            else
            {
                DrawCanvas.Children.Insert(index, secondSegment);
                DrawCanvas.Children.Insert(index, firstSegment);
            }

            MoveConnectionsFromSplitLine(targetLine, firstSegment, secondSegment, ratio);
            if (originalStartNode != null)
            {
                AttachEndpointToNode(firstSegment, true, originalStartNode);
            }

            if (originalEndNode != null)
            {
                AttachEndpointToNode(secondSegment, false, originalEndNode);
            }

            return (firstSegment, secondSegment);
        }

        private bool CanSplitLineByWireC(Line targetLine)
        {
            return targetLine.Tag is ShapeDefinition definition &&
                   definition.LineRole != LineRoleType.Bus;
        }

        private Line FindLineNearPoint(Point point, double maxDistance)
        {
            Line bestLine = null;
            double bestDistance = maxDistance;

            foreach (UIElement element in DrawCanvas.Children)
            {
                if (element is not Line line || line.Tag is not ShapeDefinition)
                {
                    continue;
                }

                Point a = new Point(line.X1, line.Y1);
                Point b = new Point(line.X2, line.Y2);
                double lengthSquared = Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2);
                if (lengthSquared == 0)
                {
                    continue;
                }

                double t = Math.Max(0, Math.Min(1, ((point.X - a.X) * (b.X - a.X) + (point.Y - a.Y) * (b.Y - a.Y)) / lengthSquared));
                Point projection = new Point(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
                double distance = Math.Sqrt(Math.Pow(point.X - projection.X, 2) + Math.Pow(point.Y - projection.Y, 2));

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestLine = line;
                }
            }

            return bestLine;
        }

        private void SplitWireCTargetLines(Line wireCLine)
        {
            if (wireCSplitTargets == null)
            {
                return;
            }

            double lengthSquared = Math.Pow(wireCLine.X2 - wireCLine.X1, 2) + Math.Pow(wireCLine.Y2 - wireCLine.Y1, 2);
            if (lengthSquared == 0)
            {
                wireCSplitTargets = null;
                return;
            }

            // WireC はT字接続の交点を1か所だけ分割する。
            // 両端が線に吸着した場合は、描画終了点側を優先して余分な4本化を防ぐ。
            if (wireCSplitTargets.EndElement is Line endTarget &&
                CanSplitLineByWireC(endTarget) &&
                IsInteriorLinePoint(wireCSplitTargets.EndLineRatio))
            {
                var split = SplitLineAtRatio(endTarget, wireCSplitTargets.EndLineRatio);
                if (split.SecondSegment != null)
                {
                    var node = new ConnectionNode { Position = new Point(split.FirstSegment.X2, split.FirstSegment.Y2) };
                    AttachEndpointToNode(split.FirstSegment, false, node);
                    AttachEndpointToNode(split.SecondSegment, true, node);
                    AttachEndpointToNode(wireCLine, false, node);
                }
            }
            else if (wireCSplitTargets.StartElement is Line startTarget &&
                     CanSplitLineByWireC(startTarget) &&
                     IsInteriorLinePoint(wireCSplitTargets.StartLineRatio))
            {
                var split = SplitLineAtRatio(startTarget, wireCSplitTargets.StartLineRatio);
                if (split.SecondSegment != null)
                {
                    var node = new ConnectionNode { Position = new Point(split.FirstSegment.X2, split.FirstSegment.Y2) };
                    AttachEndpointToNode(split.FirstSegment, false, node);
                    AttachEndpointToNode(split.SecondSegment, true, node);
                    AttachEndpointToNode(wireCLine, true, node);
                }
            }

            wireCSplitTargets = null;
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point rawPoint = e.GetPosition(DrawCanvas); startPoint = SnapToGrid(rawPoint);

            if (rbSelect.IsChecked == true) {
                if (e.OriginalSource is Shape shape && !IsHandle(shape)) {
                    var parent = VisualTreeHelper.GetParent(shape) as Canvas;
                    currentElement = (parent != null && parent.Tag is ShapeDefinition) ? (UIElement)parent : (UIElement)shape;
                    isDrawingOrMoving = true;
                    ShowSelectionHighlight(currentElement);
                    if (currentElement is Shape s && s.Tag is ShapeDefinition d) ShowResizeHandles(s, d);
                    else if (currentElement is Canvas c && c.Tag is ShapeDefinition cd) ShowResizeHandles(c, cd);
                } else if (e.OriginalSource == DrawCanvas) {
                    var nearbyLine = FindLineNearPoint(rawPoint, 12);
                    if (nearbyLine != null && nearbyLine.Tag is ShapeDefinition nearbyDef)
                    {
                        currentElement = nearbyLine;
                        isDrawingOrMoving = true;
                        ShowSelectionHighlight(currentElement);
                        ShowResizeHandles(nearbyLine, nearbyDef);
                    }
                    else
                    {
                        currentElement = null;
                        HideAllHandles();
                        HideSelectionHighlight();
                    }
                }
            }
            else if (rbDraw.IsChecked == true) {
                HideSelectionHighlight();
                HideAllHandles();
                var def = lstShapes.SelectedItem as ShapeDefinition;
                if (def == null) return;
                
                if (def.Type == "Line") {
                    currentElement = null;
                    var snap = GetSnappedPointForLine(rawPoint, def);
                    startPoint = snap.SnapPt;

                    var l = CreateLineElement(def, startPoint, startPoint);

                    if (IsAutoConnectionLine(def)) {
                        lineConnections[l] = new LineConnectionInfo { StartElement = snap.Element, StartRelPoint = snap.RelPt, StartLineRatio = snap.Ratio };
                    }
                    if (def.LineRole == LineRoleType.WireC) {
                        wireCSplitTargets = new LineConnectionInfo { StartElement = snap.Element, StartRelPoint = snap.RelPt, StartLineRatio = snap.Ratio };
                    }
                    currentElement = l; DrawCanvas.Children.Add(l); isDrawingOrMoving = true;
                }
                else if (def.Type == "Rectangle") {
                    var r = new Rectangle { Stroke = Brushes.Blue, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(100, 173, 216, 230)), Tag = def };
                    Canvas.SetLeft(r, startPoint.X); Canvas.SetTop(r, startPoint.Y); currentElement = r; DrawCanvas.Children.Add(r); isDrawingOrMoving = true;
                }
                else if (def.Type == "Symbol") {
                    var sym = CreateSymbolElement(def, startPoint.X, startPoint.Y, def.FixedSize, def.FixedHeight);
                    DrawCanvas.Children.Add(sym);
                    RefreshPlacedSymbols();
                }
            }
            if (isDrawingOrMoving) DrawCanvas.CaptureMouse();
        }

        // ★連鎖追従機能：HashSet を使って再帰的に更新する（無限ループ防止）
        private void UpdateConnectedLines(UIElement targetElement, HashSet<UIElement> visited = null)
        {
            if (visited == null) visited = new HashSet<UIElement>();
            if (visited.Contains(targetElement)) return; // 既に処理済みの要素はスキップ
            visited.Add(targetElement);

            foreach (var kvp in lineConnections) {
                var movingLine = kvp.Key;
                if (movingLine == targetElement) continue;

                bool isUpdated = false;
                var info = kvp.Value;
                
                if (info.StartElement == targetElement) {
                    if (targetElement is Canvas symbol) {
                        movingLine.X1 = Canvas.GetLeft(symbol) + info.StartRelPoint.X;
                        movingLine.Y1 = Canvas.GetTop(symbol) + info.StartRelPoint.Y;
                    } else if (targetElement is Line targetLine) {
                        movingLine.X1 = targetLine.X1 + info.StartLineRatio * (targetLine.X2 - targetLine.X1);
                        movingLine.Y1 = targetLine.Y1 + info.StartLineRatio * (targetLine.Y2 - targetLine.Y1);
                    }
                    isUpdated = true;
                }
                if (info.EndElement == targetElement) {
                    if (targetElement is Canvas symbol) {
                        movingLine.X2 = Canvas.GetLeft(symbol) + info.EndRelPoint.X;
                        movingLine.Y2 = Canvas.GetTop(symbol) + info.EndRelPoint.Y;
                    } else if (targetElement is Line targetLine) {
                        movingLine.X2 = targetLine.X1 + info.EndLineRatio * (targetLine.X2 - targetLine.X1);
                        movingLine.Y2 = targetLine.Y1 + info.EndLineRatio * (targetLine.Y2 - targetLine.Y1);
                    }
                    isUpdated = true;
                }

                // ★この線が動いたことで、さらにこの線に繋がっているWireBも連鎖して動かす
                if (isUpdated) {
                    UpdateConnectedLines(movingLine, visited);
                }
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e) {
            Point raw = e.GetPosition(DrawCanvas); Point gridP = SnapToGrid(raw);
            if (isResizing && currentElement != null) {
                if (currentElement is Rectangle r) { r.Width = Math.Max(GridSize, gridP.X - Canvas.GetLeft(r)); r.Height = Math.Max(GridSize, gridP.Y - Canvas.GetTop(r)); }
                else if (currentElement is Line l && l.Tag is ShapeDefinition def) { 
                    bool isStartHandle = activeHandle == lineStartHandle;
                    if (TryMoveConnectedLineEndpoint(l, isStartHandle, gridP))
                    {
                        UpdateSelectionHighlight(currentElement);
                        UpdateHandlePositions(currentElement);
                        return;
                    }

                    var snap = GetSnappedPointForLine(raw, def); 
                    if (isStartHandle) { 
                        l.X1 = snap.SnapPt.X; l.Y1 = snap.SnapPt.Y; 
                        if (IsAutoConnectionLine(def)) { 
                            if (!lineConnections.ContainsKey(l)) lineConnections[l] = new LineConnectionInfo();
                            lineConnections[l].StartElement = snap.Element; lineConnections[l].StartRelPoint = snap.RelPt; lineConnections[l].StartLineRatio = snap.Ratio; 
                        }
                        if (def.LineRole == LineRoleType.WireC && wireCSplitTargets != null) {
                            wireCSplitTargets.StartElement = snap.Element; wireCSplitTargets.StartRelPoint = snap.RelPt; wireCSplitTargets.StartLineRatio = snap.Ratio;
                        }
                    } else { 
                        l.X2 = snap.SnapPt.X; l.Y2 = snap.SnapPt.Y; 
                        if (IsAutoConnectionLine(def)) { 
                            if (!lineConnections.ContainsKey(l)) lineConnections[l] = new LineConnectionInfo();
                            lineConnections[l].EndElement = snap.Element; lineConnections[l].EndRelPoint = snap.RelPt; lineConnections[l].EndLineRatio = snap.Ratio; 
                        }
                        if (def.LineRole == LineRoleType.WireC && wireCSplitTargets != null) {
                            wireCSplitTargets.EndElement = snap.Element; wireCSplitTargets.EndRelPoint = snap.RelPt; wireCSplitTargets.EndLineRatio = snap.Ratio;
                        }
                    } 
                    UpdateConnectedLines(l); 
                }
                UpdateSelectionHighlight(currentElement);
                UpdateHandlePositions(currentElement);
            } else if (isDrawingOrMoving && currentElement != null) {
                if (rbSelect.IsChecked == true) {
                    double dx = gridP.X - startPoint.X; double dy = gridP.Y - startPoint.Y;
                    if (dx != 0 || dy != 0) {
                        if (currentElement is Line l) { 
                            l.X1 += dx; l.Y1 += dy; l.X2 += dx; l.Y2 += dy; 
                            ApplyOwnLineConnections(l);
                            UpdateConnectedLines(l); 
                        }
                        else { 
                            Canvas.SetLeft(currentElement, Canvas.GetLeft(currentElement) + dx); Canvas.SetTop(currentElement, Canvas.GetTop(currentElement) + dy); 
                            UpdateConnectedLines(currentElement); 
                        }
                        UpdateSelectionHighlight(currentElement);
                        UpdateHandlePositions(currentElement); startPoint = gridP;
                    }
                } else {
                    if (currentElement is Line l && l.Tag is ShapeDefinition def) { 
                        var snap = GetSnappedPointForLine(raw, def); 
                        l.X2 = snap.SnapPt.X; l.Y2 = snap.SnapPt.Y; 
                        if (IsAutoConnectionLine(def)) { 
                            if (!lineConnections.ContainsKey(l)) lineConnections[l] = new LineConnectionInfo();
                            lineConnections[l].EndElement = snap.Element; lineConnections[l].EndRelPoint = snap.RelPt; lineConnections[l].EndLineRatio = snap.Ratio; 
                        }
                        if (def.LineRole == LineRoleType.WireC && wireCSplitTargets != null) {
                            wireCSplitTargets.EndElement = snap.Element; wireCSplitTargets.EndRelPoint = snap.RelPt; wireCSplitTargets.EndLineRatio = snap.Ratio;
                        }
                    }
                    else if (currentElement is Rectangle r) { Canvas.SetLeft(r, Math.Min(startPoint.X, gridP.X)); Canvas.SetTop(r, Math.Min(startPoint.Y, gridP.Y)); r.Width = Math.Abs(startPoint.X - gridP.X); r.Height = Math.Abs(startPoint.Y - gridP.Y); }
                }
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (rbDraw.IsChecked == true &&
                currentElement is Line line &&
                line.Tag is ShapeDefinition def &&
                def.LineRole == LineRoleType.WireC)
            {
                SplitWireCTargetLines(line);
            }

            isDrawingOrMoving = false;
            isResizing = false;
            activeHandle = null;
            DrawCanvas.ReleaseMouseCapture();
            RefreshPlacedSymbols();
        }

        private void InitializeSelectionHighlight()
        {
            selectionBox = new Rectangle
            {
                Stroke = Brushes.Gold,
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = Brushes.Transparent,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            selectionLineHighlight = new Line
            {
                Stroke = Brushes.Gold,
                StrokeThickness = 8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.45,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            DrawCanvas.Children.Add(selectionBox);
            DrawCanvas.Children.Add(selectionLineHighlight);
        }

        private void ShowSelectionHighlight(UIElement element)
        {
            UpdateSelectionHighlight(element);
        }

        private void HideSelectionHighlight()
        {
            selectionBox.Visibility = Visibility.Collapsed;
            selectionLineHighlight.Visibility = Visibility.Collapsed;
        }

        private void UpdateSelectionHighlight(UIElement element)
        {
            if (element is Line line)
            {
                selectionBox.Visibility = Visibility.Collapsed;
                selectionLineHighlight.X1 = line.X1;
                selectionLineHighlight.Y1 = line.Y1;
                selectionLineHighlight.X2 = line.X2;
                selectionLineHighlight.Y2 = line.Y2;
                selectionLineHighlight.Visibility = Visibility.Visible;
                return;
            }

            selectionLineHighlight.Visibility = Visibility.Collapsed;

            if (element is Rectangle rectangle)
            {
                Canvas.SetLeft(selectionBox, Canvas.GetLeft(rectangle) - 4);
                Canvas.SetTop(selectionBox, Canvas.GetTop(rectangle) - 4);
                selectionBox.Width = rectangle.Width + 8;
                selectionBox.Height = rectangle.Height + 8;
                selectionBox.Visibility = Visibility.Visible;
                return;
            }

            if (element is Canvas canvas)
            {
                Canvas.SetLeft(selectionBox, Canvas.GetLeft(canvas) - 4);
                Canvas.SetTop(selectionBox, Canvas.GetTop(canvas) - 4);
                selectionBox.Width = canvas.Width + 8;
                selectionBox.Height = canvas.Height + 8;
                selectionBox.Visibility = Visibility.Visible;
                return;
            }

            HideSelectionHighlight();
        }

        private void InitializeHandles() { rectResizeHandle = CreateHandle(Brushes.Red, Cursors.SizeNWSE); lineStartHandle = CreateHandle(Brushes.Green, Cursors.Cross); lineEndHandle = CreateHandle(Brushes.Green, Cursors.Cross); }
        private Rectangle CreateHandle(Brush c, Cursor cur) { var h = new Rectangle { Width = 16, Height = 16, Fill = c, Stroke = Brushes.White, StrokeThickness = 2, Visibility = Visibility.Collapsed, Cursor = cur }; h.MouseLeftButtonDown += (s, e) => { isResizing = true; activeHandle = h; startPoint = SnapToGrid(e.GetPosition(DrawCanvas)); DrawCanvas.CaptureMouse(); e.Handled = true; }; DrawCanvas.Children.Add(h); return h; }
        private Point SnapToGrid(Point p) => new Point(Math.Round(p.X / GridSize) * GridSize, Math.Round(p.Y / GridSize) * GridSize);
        private bool IsHandle(Shape s) => s == rectResizeHandle || s == lineStartHandle || s == lineEndHandle;
        private void HideAllHandles() { rectResizeHandle.Visibility = lineStartHandle.Visibility = lineEndHandle.Visibility = Visibility.Collapsed; }
        private void ShowResizeHandles(UIElement e, ShapeDefinition d) { HideAllHandles(); if (!d.IsResizable) return; if (e is Rectangle r) { rectResizeHandle.Visibility = Visibility.Visible; UpdateHandlePositions(r); } else if (e is Line l) { lineStartHandle.Visibility = lineEndHandle.Visibility = Visibility.Visible; UpdateHandlePositions(l); } }
        private bool IsConnectedLineEndpoint(Line line, bool isStartHandle)
        {
            if (GetEndpointNode(line, isStartHandle) != null)
            {
                return true;
            }

            if (!lineConnections.TryGetValue(line, out var info))
            {
                return false;
            }

            UIElement connectedElement = isStartHandle ? info.StartElement : info.EndElement;
            double ratio = isStartHandle ? info.StartLineRatio : info.EndLineRatio;
            return connectedElement is Line && IsLineEndpointRatio(ratio);
        }

        private void UpdateHandlePositions(UIElement e) { if (e is Rectangle r) { Canvas.SetLeft(rectResizeHandle, Canvas.GetLeft(r) + r.Width - 8); Canvas.SetTop(rectResizeHandle, Canvas.GetTop(r) + r.Height - 8); } else if (e is Line l) { lineStartHandle.Fill = IsConnectedLineEndpoint(l, true) ? Brushes.Gold : Brushes.Green; lineEndHandle.Fill = IsConnectedLineEndpoint(l, false) ? Brushes.Gold : Brushes.Green; Canvas.SetLeft(lineStartHandle, l.X1 - 8); Canvas.SetTop(lineStartHandle, l.Y1 - 8); Canvas.SetLeft(lineEndHandle, l.X2 - 8); Canvas.SetTop(lineEndHandle, l.Y2 - 8); } }
    }
}

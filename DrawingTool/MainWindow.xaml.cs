using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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

        private class DrawingDataReference
        {
            public string DataDefinitionName { get; set; } = "";
            public string DataId { get; set; } = "";
        }

        private readonly MainWindowViewModel viewModel = new MainWindowViewModel();
        private Dictionary<Line, LineConnectionInfo> lineConnections = new Dictionary<Line, LineConnectionInfo>();
        private Dictionary<Line, ConnectionNode> lineStartNodes = new Dictionary<Line, ConnectionNode>();
        private Dictionary<Line, ConnectionNode> lineEndNodes = new Dictionary<Line, ConnectionNode>();
        private Dictionary<UIElement, List<SymbolAttribute>> drawingElementAttributes = new Dictionary<UIElement, List<SymbolAttribute>>();
        private Dictionary<UIElement, DrawingDataReference> drawingElementDataReferences = new Dictionary<UIElement, DrawingDataReference>();
        private Dictionary<string, DrawingDataReference> lineGroupDataReferences = new Dictionary<string, DrawingDataReference>();
        private Dictionary<string, List<string>> csvTableColumns = new Dictionary<string, List<string>>();
        private Dictionary<string, List<List<string>>> csvTableRows = new Dictionary<string, List<List<string>>>();
        private string selectedLineGroupKey = "";
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
            var lineGroups = new List<PlacedDrawingInfo>();

            int no = 1;
            int groupNo = 1;

            foreach (UIElement element in DrawCanvas.Children)
            {
                if (element is Canvas symbolCanvas &&
                    symbolCanvas.Tag is ShapeDefinition def &&
                    def.Type == "Symbol")
                {
                    int attributeCount = GetDrawingElementAttributes(symbolCanvas).Count;
                    var dataRecord = GetDrawingElementDataRecord(symbolCanvas);
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
                        ConnectionPointCount = def.ConnectionPoints.Count,
                        AttributeCount = attributeCount,
                        DataDefinitionName = dataRecord?.DefinitionName ?? "",
                        DataId = dataRecord?.Name ?? "",
                        Element = symbolCanvas
                    });
                }
                else if (element is Line line &&
                         line.Tag is ShapeDefinition lineDef &&
                         lineDef.Type == "Line")
                {
                    int attributeCount = GetDrawingElementAttributes(line).Count;
                    var dataRecord = GetDrawingElementDataRecord(line);
                    placedDrawings.Add(new PlacedDrawingInfo
                    {
                        No = no++,
                        Id = lineDef.Id,
                        Type = lineDef.Type,
                        X = line.X1,
                        Y = line.Y1,
                        X2 = line.X2,
                        Y2 = line.Y2,
                        AttributeCount = attributeCount,
                        DataDefinitionName = dataRecord?.DefinitionName ?? "",
                        DataId = dataRecord?.Name ?? "",
                        Element = line
                    });
                }
                else if (element is Rectangle rectangle &&
                         rectangle.Tag is ShapeDefinition rectDef &&
                         rectDef.Type == "Rectangle")
                {
                    int attributeCount = GetDrawingElementAttributes(rectangle).Count;
                    var dataRecord = GetDrawingElementDataRecord(rectangle);
                    placedDrawings.Add(new PlacedDrawingInfo
                    {
                        No = no++,
                        Id = rectDef.Id,
                        Type = rectDef.Type,
                        X = Canvas.GetLeft(rectangle),
                        Y = Canvas.GetTop(rectangle),
                        Width = rectangle.Width,
                        Height = rectangle.Height,
                        AttributeCount = attributeCount,
                        DataDefinitionName = dataRecord?.DefinitionName ?? "",
                        DataId = dataRecord?.Name ?? "",
                        Element = rectangle
                    });
                }
            }

            foreach (var group in GetLineGroupComponents())
            {
                string groupKey = GetLineGroupKey(group);
                lineGroupDataReferences.TryGetValue(groupKey, out var reference);
                lineGroups.Add(new PlacedDrawingInfo
                {
                    No = groupNo++,
                    Id = ((ShapeDefinition)group[0].Tag).Id,
                    Type = "LineGroup",
                    IsLineGroup = true,
                    LineGroupKey = groupKey,
                    LineCount = group.Count,
                    DataDefinitionName = reference?.DataDefinitionName ?? "",
                    DataId = reference?.DataId ?? ""
                });
            }

            viewModel.RefreshPlacedDrawings(placedDrawings);
            viewModel.RefreshLineGroups(lineGroups);
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

        private void BtnAddDataItem_Click(object sender, RoutedEventArgs e)
        {
            string itemName = txtDataItemName.Text.Trim();
            if (itemName.Length == 0 || viewModel.TempDataItems.Contains(itemName))
            {
                return;
            }

            viewModel.TempDataItems.Add(itemName);
            txtDataItemName.Clear();
        }

        private void BtnRemoveDataItem_Click(object sender, RoutedEventArgs e)
        {
            if (lstTempDataItems.SelectedItem is string itemName)
            {
                viewModel.TempDataItems.Remove(itemName);
                if (cmbDataDefinitionIdItem.SelectedItem as string == itemName)
                {
                    cmbDataDefinitionIdItem.SelectedItem = null;
                }
            }
        }

        private void BtnClearDataItems_Click(object sender, RoutedEventArgs e)
        {
            viewModel.TempDataItems.Clear();
            cmbDataDefinitionIdItem.SelectedItem = null;
        }

        private void BtnClearParentDataDefinition_Click(object sender, RoutedEventArgs e)
        {
            cmbParentDataDefinition.SelectedItem = null;
        }

        private void BtnRegisterDataDefinition_Click(object sender, RoutedEventArgs e)
        {
            string name = txtDataDefinitionName.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "データ定義名を入力してください。", "データ定義", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string parentDefinitionName = GetSelectedParentDataDefinitionName();
            string idItemName = GetSelectedDataDefinitionIdItemName();
            if (!ValidateDataDefinitionRelationship(name, parentDefinitionName, null))
            {
                return;
            }
            if (!ValidateDataDefinitionIdItem(idItemName))
            {
                return;
            }

            viewModel.RegisterDataDefinition(name, parentDefinitionName, idItemName);
            RefreshDataDefinitionViews();
        }

        private void BtnLoadDataDefinition_Click(object sender, RoutedEventArgs e)
        {
            if (lstDataDefinitions.SelectedItem is not DataDefinition definition)
            {
                MessageBox.Show(this, "読み込むデータ定義を選択してください。", "データ定義", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LoadDataDefinitionToEditor(definition);
        }

        private void BtnUpdateDataDefinition_Click(object sender, RoutedEventArgs e)
        {
            if (lstDataDefinitions.SelectedItem is not DataDefinition definition)
            {
                MessageBox.Show(this, "更新するデータ定義を選択してください。", "データ定義", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string name = txtDataDefinitionName.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "データ定義名を入力してください。", "データ定義", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string parentDefinitionName = GetSelectedParentDataDefinitionName();
            string idItemName = GetSelectedDataDefinitionIdItemName();
            if (!ValidateDataDefinitionRelationship(name, parentDefinitionName, definition))
            {
                return;
            }
            if (!ValidateDataDefinitionIdItem(idItemName))
            {
                return;
            }

            string oldName = definition.Name;
            definition.Name = name;
            definition.ParentDefinitionName = parentDefinitionName;
            definition.IdItemName = idItemName;
            definition.Items.Clear();
            foreach (var item in viewModel.TempDataItems)
            {
                definition.Items.Add(item);
            }

            UpdateDataDefinitionReferences(oldName, name);
            SyncDataRecordIdsForDefinition(definition);
            RefreshDataDefinitionViews();
        }

        private void LoadDataDefinitionToEditor(DataDefinition definition)
        {
            txtDataDefinitionName.Text = definition.Name;
            cmbParentDataDefinition.SelectedItem = viewModel.DataDefinitions
                .FirstOrDefault(item => item.Name == definition.ParentDefinitionName);
            viewModel.TempDataItems.Clear();
            foreach (var item in definition.Items)
            {
                viewModel.TempDataItems.Add(item);
            }
            cmbDataDefinitionIdItem.SelectedItem = viewModel.TempDataItems
                .FirstOrDefault(item => item == definition.IdItemName);
        }

        private string GetSelectedParentDataDefinitionName()
        {
            return cmbParentDataDefinition.SelectedItem is DataDefinition parentDefinition
                ? parentDefinition.Name
                : "";
        }

        private string GetSelectedDataDefinitionIdItemName()
        {
            return cmbDataDefinitionIdItem.SelectedItem is string itemName ? itemName : "";
        }

        private bool ValidateDataDefinitionIdItem(string idItemName)
        {
            if (!string.IsNullOrWhiteSpace(idItemName) && viewModel.TempDataItems.Contains(idItemName))
            {
                return true;
            }

            MessageBox.Show(this, "ID項目は必須です。このデータ定義の項目から1つ選択してください。", "データ定義", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private bool ValidateDataDefinitionRelationship(
            string name,
            string parentDefinitionName,
            DataDefinition? editingDefinition)
        {
            if (viewModel.DataDefinitions.Any(definition => definition != editingDefinition && definition.Name == name))
            {
                MessageBox.Show(this, "同じ名前のデータ定義が既にあります。親子関係を扱うため、データ定義名は一意にしてください。", "データ定義", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (string.IsNullOrWhiteSpace(parentDefinitionName))
            {
                return true;
            }

            if (parentDefinitionName == name)
            {
                MessageBox.Show(this, "自分自身を親データ定義にはできません。", "データ定義", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var visitedNames = new HashSet<string>();
            string currentParentName = parentDefinitionName;
            while (!string.IsNullOrWhiteSpace(currentParentName))
            {
                if (!visitedNames.Add(currentParentName) ||
                    currentParentName == name ||
                    (editingDefinition != null && currentParentName == editingDefinition.Name))
                {
                    MessageBox.Show(this, "循環する親子関係は定義できません。", "データ定義", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                var parent = viewModel.DataDefinitions.FirstOrDefault(definition => definition.Name == currentParentName);
                if (parent == null)
                {
                    break;
                }

                currentParentName = parent.ParentDefinitionName;
            }

            return true;
        }

        private void UpdateDataDefinitionReferences(string oldName, string newName)
        {
            if (oldName == newName)
            {
                return;
            }

            foreach (var definition in viewModel.DataDefinitions)
            {
                if (definition.ParentDefinitionName == oldName)
                {
                    definition.ParentDefinitionName = newName;
                }
            }

            foreach (var record in viewModel.DataRecords)
            {
                if (record.DefinitionName == oldName)
                {
                    record.DefinitionName = newName;
                }
            }
        }

        private void RefreshDataDefinitionViews()
        {
            lstDataDefinitions.Items.Refresh();
            cmbParentDataDefinition.Items.Refresh();
            cmbPlacedSymbolDataDefinition.Items.Refresh();
            cmbLineGroupDataDefinition.Items.Refresh();
            cmbRecordDataDefinition.Items.Refresh();
            RefreshPlacedDataRecordChoices();
            RefreshLineGroupDataRecordChoices();
            lstDataRecords.Items.Refresh();
        }

        private void SyncDataRecordIdsForDefinition(DataDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(definition.IdItemName))
            {
                return;
            }

            foreach (var record in viewModel.DataRecords.Where(record => record.DefinitionName == definition.Name))
            {
                var idValue = record.Attributes
                    .FirstOrDefault(attribute => attribute.Key == definition.IdItemName)
                    ?.Value
                    .Trim();
                if (!string.IsNullOrWhiteSpace(idValue))
                {
                    record.Name = idValue;
                }
            }
        }

        private void BtnCreateRecordFields_Click(object sender, RoutedEventArgs e)
        {
            if (cmbRecordDataDefinition.SelectedItem is not DataDefinition definition)
            {
                MessageBox.Show(this, "データ定義を選択してください。", "追加データ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CreateDataRecordFieldInputs(definition, ReadDataRecordFieldInputs());
        }

        private void CmbRecordDataDefinition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbRecordDataDefinition.SelectedItem is DataDefinition definition)
            {
                CreateDataRecordFieldInputs(definition, ReadDataRecordFieldInputs());
            }
            else
            {
                pnlDataRecordFields.Children.Clear();
            }
        }

        private void BtnRegisterDataRecord_Click(object sender, RoutedEventArgs e)
        {
            if (cmbRecordDataDefinition.SelectedItem is not DataDefinition definition)
            {
                MessageBox.Show(this, "データ定義を選択してください。", "追加データ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            EnsureDataRecordFieldInputs(definition);
            var attributes = ReadDataRecordFieldInputs();
            if (!TryGetDataRecordId(definition, attributes, out string idValue))
            {
                return;
            }

            if (HasDuplicateDataRecordId(definition.Name, idValue, null))
            {
                MessageBox.Show(this, "同じデータ定義内に同じIDの追加データが既にあります。", "追加データ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            viewModel.RegisterDataRecord(idValue, definition, attributes);
            lstDataRecords.Items.Refresh();
            RefreshPlacedDataRecordChoices();
            RefreshLineGroupDataRecordChoices();
        }

        private void BtnLoadDataRecord_Click(object sender, RoutedEventArgs e)
        {
            if (lstDataRecords.SelectedItem is not DataRecord record)
            {
                MessageBox.Show(this, "読み込む追加データを選択してください。", "追加データ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LoadDataRecordToEditor(record);
        }

        private void BtnUpdateDataRecord_Click(object sender, RoutedEventArgs e)
        {
            if (lstDataRecords.SelectedItem is not DataRecord record)
            {
                MessageBox.Show(this, "更新する追加データを選択してください。", "追加データ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (cmbRecordDataDefinition.SelectedItem is not DataDefinition definition)
            {
                MessageBox.Show(this, "データ定義を選択してください。", "追加データ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            EnsureDataRecordFieldInputs(definition);
            var attributes = ReadDataRecordFieldInputs();
            if (!TryGetDataRecordId(definition, attributes, out string idValue))
            {
                return;
            }

            if (HasDuplicateDataRecordId(definition.Name, idValue, record))
            {
                MessageBox.Show(this, "同じデータ定義内に同じIDの追加データが既にあります。", "追加データ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            record.Name = idValue;
            record.DefinitionName = definition.Name;
            record.Attributes = attributes;
            lstDataRecords.Items.Refresh();
            RefreshPlacedDataRecordChoices();
            RefreshLineGroupDataRecordChoices();
        }

        private void LoadDataRecordToEditor(DataRecord record)
        {
            var definition = viewModel.DataDefinitions.FirstOrDefault(item => item.Name == record.DefinitionName);
            if (definition != null)
            {
                cmbRecordDataDefinition.SelectedItem = definition;
                CreateDataRecordFieldInputs(definition, record.Attributes);
            }
            else
            {
                pnlDataRecordFields.Children.Clear();
            }
        }

        private bool TryGetDataRecordId(DataDefinition definition, IEnumerable<SymbolAttribute> attributes, out string idValue)
        {
            idValue = "";
            if (string.IsNullOrWhiteSpace(definition.IdItemName))
            {
                MessageBox.Show(this, "このデータ定義にはID項目が設定されていません。データ定義タブでID項目を設定してください。", "追加データ", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            idValue = attributes
                .FirstOrDefault(attribute => attribute.Key == definition.IdItemName)
                ?.Value
                .Trim() ?? "";
            if (idValue.Length == 0)
            {
                MessageBox.Show(this, $"ID項目「{definition.IdItemName}」の値を入力してください。", "追加データ", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        private bool HasDuplicateDataRecordId(string definitionName, string idValue, DataRecord? editingRecord)
        {
            return viewModel.DataRecords.Any(record =>
                record != editingRecord &&
                record.DefinitionName == definitionName &&
                record.Name == idValue);
        }

        private void EnsureDataRecordFieldInputs(DataDefinition definition)
        {
            if (pnlDataRecordFields.Children.OfType<TextBox>().Any())
            {
                return;
            }

            CreateDataRecordFieldInputs(definition, Enumerable.Empty<SymbolAttribute>());
        }

        private void CreateDataRecordFieldInputs(DataDefinition definition, IEnumerable<SymbolAttribute> currentValues)
        {
            var valueByKey = currentValues
                .GroupBy(attribute => attribute.Key)
                .ToDictionary(group => group.Key, group => group.First().Value);

            pnlDataRecordFields.Children.Clear();
            foreach (var itemName in GetDataDefinitionItemsIncludingParents(definition))
            {
                var isIdItem = itemName == definition.IdItemName;
                var label = new TextBlock
                {
                    Text = isIdItem ? $"{itemName} (ID)" : itemName,
                    Margin = new Thickness(0, 0, 0, 2),
                    FontWeight = isIdItem ? FontWeights.Bold : FontWeights.Normal
                };

                var input = new TextBox
                {
                    Tag = itemName,
                    Text = valueByKey.TryGetValue(itemName, out var value) ? value : "",
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(2),
                    BorderBrush = isIdItem ? Brushes.DarkBlue : SystemColors.ControlDarkBrush,
                    BorderThickness = isIdItem ? new Thickness(2) : new Thickness(1)
                };

                pnlDataRecordFields.Children.Add(label);
                pnlDataRecordFields.Children.Add(input);
            }
        }

        private List<SymbolAttribute> ReadDataRecordFieldInputs()
        {
            return pnlDataRecordFields.Children
                .OfType<TextBox>()
                .Select(input => new SymbolAttribute
                {
                    Key = input.Tag?.ToString() ?? "",
                    Value = input.Text
                })
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key))
                .ToList();
        }

        private List<string> GetDataDefinitionItemsIncludingParents(DataDefinition definition)
        {
            var result = new List<string>();
            var visitedDefinitions = new HashSet<string>();
            AppendDataDefinitionItems(definition, result, visitedDefinitions);
            return result;
        }

        private void AppendDataDefinitionItems(DataDefinition definition, List<string> result, HashSet<string> visitedDefinitions)
        {
            if (!visitedDefinitions.Add(definition.Name))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(definition.ParentDefinitionName))
            {
                var parent = viewModel.DataDefinitions.FirstOrDefault(item => item.Name == definition.ParentDefinitionName);
                if (parent != null)
                {
                    AppendDataDefinitionItems(parent, result, visitedDefinitions);
                }
            }

            foreach (var itemName in definition.Items)
            {
                if (!result.Contains(itemName))
                {
                    result.Add(itemName);
                }
            }
        }

        private List<SymbolAttribute> ParseAttributesText(string text)
        {
            return text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Select(line =>
                {
                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex < 0)
                    {
                        return new SymbolAttribute { Key = line, Value = "" };
                    }

                    return new SymbolAttribute
                    {
                        Key = line.Substring(0, separatorIndex).Trim(),
                        Value = line.Substring(separatorIndex + 1).Trim()
                    };
                })
                .Where(attribute => attribute.Key.Length > 0)
                .ToList();
        }

        private string FormatAttributesText(IEnumerable<SymbolAttribute> attributes)
        {
            return string.Join(Environment.NewLine, attributes.Select(attribute => $"{attribute.Key}={attribute.Value}"));
        }

        private SymbolAttribute CloneAttribute(SymbolAttribute attribute)
        {
            return new SymbolAttribute
            {
                Key = attribute.Key,
                Value = attribute.Value
            };
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
            bool isLineGroupTarget = ((ComboBoxItem)cmbShapeType.SelectedItem).Tag.ToString() == "Line" &&
                                     chkLineGroupTarget.IsChecked == true;

            viewModel.TempAttributes.Clear();

            var def = viewModel.RegisterShape(
                txtShapeId.Text,
                ((ComboBoxItem)cmbShapeType.SelectedItem).Tag.ToString(),
                GetSymbolFixedWidth(),
                GetSymbolFixedHeight(),
                GetSymbolGridWidthCount(),
                GetSymbolGridHeightCount(),
                role,
                isLineGroupTarget);

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

        private void LstPlacedSymbols_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstPlacedSymbols.SelectedItem is not PlacedDrawingInfo placed)
            {
                return;
            }

            selectedLineGroupKey = "";
            if (placed.IsLineGroup)
            {
                currentElement = null;
                selectedLineGroupKey = placed.LineGroupKey;
                rbSelect.IsChecked = true;
                HideAllHandles();
                HideSelectionHighlight();
                LoadLineGroupDataAssignment(selectedLineGroupKey);
                return;
            }

            if (placed.Element == null)
            {
                return;
            }

            currentElement = placed.Element;
            rbSelect.IsChecked = true;
            HideAllHandles();
            ShowSelectionHighlight(currentElement);
            if (currentElement is Shape shape && shape.Tag is ShapeDefinition shapeDefinition)
            {
                ShowResizeHandles(shape, shapeDefinition);
            }

            if (currentElement != null && IsSavedDrawingElement(currentElement))
            {
                LoadPlacedElementDataAssignment(currentElement);
            }
        }

        private UIElement? GetSelectedPlacedElement()
        {
            if (lstPlacedSymbols.SelectedItem is PlacedDrawingInfo placedGroup && placedGroup.IsLineGroup)
            {
                return null;
            }

            if (lstPlacedSymbols.SelectedItem is PlacedDrawingInfo placed &&
                placed.Element != null &&
                IsSavedDrawingElement(placed.Element))
            {
                return placed.Element;
            }

            if (currentElement != null && IsSavedDrawingElement(currentElement))
            {
                return currentElement;
            }

            return null;
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

        private void CmbPlacedSymbolDataDefinition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshPlacedDataRecordChoices();
        }

        private void RefreshPlacedDataRecordChoices()
        {
            var selectedRecord = cmbPlacedDataRecord.SelectedItem as DataRecord;
            if (cmbPlacedSymbolDataDefinition.SelectedItem is not DataDefinition definition)
            {
                cmbPlacedDataRecord.ItemsSource = null;
                return;
            }

            var records = viewModel.DataRecords
                .Where(record => record.DefinitionName == definition.Name)
                .ToList();
            cmbPlacedDataRecord.ItemsSource = records;

            if (selectedRecord != null && records.Contains(selectedRecord))
            {
                cmbPlacedDataRecord.SelectedItem = selectedRecord;
            }
            else
            {
                cmbPlacedDataRecord.SelectedItem = null;
            }
        }

        private void LstLineGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLineGroups.SelectedItem is not PlacedDrawingInfo group)
            {
                return;
            }

            selectedLineGroupKey = group.LineGroupKey;
            currentElement = null;
            rbSelect.IsChecked = true;
            HideAllHandles();
            HideSelectionHighlight();
            lstPlacedSymbols.SelectedItem = null;
            LoadLineGroupDataAssignmentToLineGroupTab(selectedLineGroupKey);
        }

        private void CmbLineGroupDataDefinition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshLineGroupDataRecordChoices();
        }

        private void RefreshLineGroupDataRecordChoices()
        {
            var selectedRecord = cmbLineGroupDataRecord.SelectedItem as DataRecord;
            if (cmbLineGroupDataDefinition.SelectedItem is not DataDefinition definition)
            {
                cmbLineGroupDataRecord.ItemsSource = null;
                return;
            }

            var records = viewModel.DataRecords
                .Where(record => record.DefinitionName == definition.Name)
                .ToList();
            cmbLineGroupDataRecord.ItemsSource = records;

            if (selectedRecord != null && records.Contains(selectedRecord))
            {
                cmbLineGroupDataRecord.SelectedItem = selectedRecord;
            }
            else
            {
                cmbLineGroupDataRecord.SelectedItem = null;
            }
        }

        private void BtnAssignDataRecordToLineGroup_Click(object sender, RoutedEventArgs e)
        {
            if (lstLineGroups.SelectedItem is not PlacedDrawingInfo group || string.IsNullOrWhiteSpace(group.LineGroupKey))
            {
                MessageBox.Show(this, "線グループ一覧から線グループを選択してください。", "線グループ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (cmbLineGroupDataDefinition.SelectedItem is not DataDefinition definition)
            {
                MessageBox.Show(this, "データ定義を選択してください。", "線グループ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (cmbLineGroupDataRecord.SelectedItem is not DataRecord record)
            {
                MessageBox.Show(this, "追加データを選択してください。", "線グループ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (record.DefinitionName != definition.Name)
            {
                MessageBox.Show(this, "選択したデータ定義と追加データの種別が一致していません。", "線グループ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            lineGroupDataReferences[group.LineGroupKey] = new DrawingDataReference
            {
                DataDefinitionName = record.DefinitionName,
                DataId = record.Name
            };
            ApplyLineGroupDataReferenceToLines(group.LineGroupKey, record);
            selectedLineGroupKey = group.LineGroupKey;
            txtLineGroupAttributes.Text = FormatAttributesText(record.Attributes);
            RefreshPlacedSymbols();
        }

        private void ApplyLineGroupDataReferenceToLines(string groupKey, DataRecord record)
        {
            var reference = new DrawingDataReference
            {
                DataDefinitionName = record.DefinitionName,
                DataId = record.Name
            };

            foreach (var line in GetLinesForLineGroupKey(groupKey))
            {
                drawingElementDataReferences[line] = reference;
                drawingElementAttributes.Remove(line);
            }
        }

        private List<Line> GetLinesForLineGroupKey(string groupKey)
        {
            return GetLineGroupComponents()
                .FirstOrDefault(group => GetLineGroupKey(group) == groupKey) ?? new List<Line>();
        }

        private void BtnAssignDataRecordToPlaced_Click(object sender, RoutedEventArgs e)
        {
            string lineGroupKey = GetSelectedLineGroupKey();
            UIElement? element = GetSelectedPlacedElement();
            if (element == null && string.IsNullOrWhiteSpace(lineGroupKey))
            {
                MessageBox.Show(this, "配置済み図形一覧から図形または線グループを選択してください。", "対応情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (cmbPlacedSymbolDataDefinition.SelectedItem is not DataDefinition definition)
            {
                MessageBox.Show(this, "データ定義を選択してください。", "対応情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (cmbPlacedDataRecord.SelectedItem is not DataRecord record)
            {
                MessageBox.Show(this, "追加データを選択してください。", "対応情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (record.DefinitionName != definition.Name)
            {
                MessageBox.Show(this, "選択したデータ定義と追加データの種別が一致していません。", "対応情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var reference = new DrawingDataReference
            {
                DataDefinitionName = record.DefinitionName,
                DataId = record.Name
            };

            if (!string.IsNullOrWhiteSpace(lineGroupKey))
            {
                lineGroupDataReferences[lineGroupKey] = reference;
            }
            else if (element != null)
            {
                drawingElementDataReferences[element] = reference;
                drawingElementAttributes.Remove(element);
            }

            txtPlacedSymbolAttributes.Text = FormatAttributesText(record.Attributes);
            RefreshPlacedSymbols();
        }

        private void LoadLineGroupDataAssignment(string groupKey)
        {
            var record = GetLineGroupDataRecord(groupKey);
            if (record == null)
            {
                cmbPlacedSymbolDataDefinition.SelectedItem = null;
                cmbPlacedDataRecord.ItemsSource = null;
                cmbPlacedDataRecord.SelectedItem = null;
                txtPlacedSymbolAttributes.Clear();
                return;
            }

            var definition = viewModel.DataDefinitions.FirstOrDefault(item => item.Name == record.DefinitionName);
            cmbPlacedSymbolDataDefinition.SelectedItem = definition;
            RefreshPlacedDataRecordChoices();
            cmbPlacedDataRecord.SelectedItem = record;
            txtPlacedSymbolAttributes.Text = FormatAttributesText(record.Attributes);
        }

        private void LoadLineGroupDataAssignmentToLineGroupTab(string groupKey)
        {
            var record = GetLineGroupDataRecord(groupKey);
            if (record == null)
            {
                cmbLineGroupDataDefinition.SelectedItem = null;
                cmbLineGroupDataRecord.ItemsSource = null;
                cmbLineGroupDataRecord.SelectedItem = null;
                txtLineGroupAttributes.Clear();
                return;
            }

            var definition = viewModel.DataDefinitions.FirstOrDefault(item => item.Name == record.DefinitionName);
            cmbLineGroupDataDefinition.SelectedItem = definition;
            RefreshLineGroupDataRecordChoices();
            cmbLineGroupDataRecord.SelectedItem = record;
            txtLineGroupAttributes.Text = FormatAttributesText(record.Attributes);
        }

        private DataRecord? GetLineGroupDataRecord(string groupKey)
        {
            if (!lineGroupDataReferences.TryGetValue(groupKey, out var reference))
            {
                return null;
            }

            return viewModel.DataRecords.FirstOrDefault(record =>
                record.DefinitionName == reference.DataDefinitionName &&
                record.Name == reference.DataId);
        }

        private void LoadPlacedElementDataAssignment(UIElement element)
        {
            var record = GetDrawingElementDataRecord(element);
            if (record == null)
            {
                cmbPlacedSymbolDataDefinition.SelectedItem = null;
                cmbPlacedDataRecord.ItemsSource = null;
                cmbPlacedDataRecord.SelectedItem = null;
                txtPlacedSymbolAttributes.Text = FormatAttributesText(GetDrawingElementAttributes(element));
                return;
            }

            var definition = viewModel.DataDefinitions.FirstOrDefault(item => item.Name == record.DefinitionName);
            cmbPlacedSymbolDataDefinition.SelectedItem = definition;
            RefreshPlacedDataRecordChoices();
            cmbPlacedDataRecord.SelectedItem = record;
            txtPlacedSymbolAttributes.Text = FormatAttributesText(record.Attributes);
        }

        private DataRecord? GetDrawingElementDataRecord(UIElement element)
        {
            if (drawingElementDataReferences.TryGetValue(element, out var reference))
            {
                return viewModel.DataRecords.FirstOrDefault(record =>
                    record.DefinitionName == reference.DataDefinitionName &&
                    record.Name == reference.DataId);
            }

            return FindDataRecordByAttributes(GetLegacyDrawingElementAttributes(element));
        }

        private DataRecord? FindDataRecordByAttributes(IEnumerable<SymbolAttribute> attributes)
        {
            var attributeList = attributes.ToList();
            foreach (var definition in viewModel.DataDefinitions)
            {
                if (string.IsNullOrWhiteSpace(definition.IdItemName))
                {
                    continue;
                }

                var idValue = attributeList
                    .FirstOrDefault(attribute => attribute.Key == definition.IdItemName)
                    ?.Value
                    .Trim();
                if (string.IsNullOrWhiteSpace(idValue))
                {
                    continue;
                }

                var record = viewModel.DataRecords.FirstOrDefault(item =>
                    item.DefinitionName == definition.Name &&
                    item.Name == idValue);
                if (record != null)
                {
                    return record;
                }
            }

            return null;
        }

        private string GetSelectedLineGroupKey()
        {
            if (lstPlacedSymbols.SelectedItem is PlacedDrawingInfo placed && placed.IsLineGroup)
            {
                return placed.LineGroupKey;
            }

            return selectedLineGroupKey;
        }

        private void LoadSymbolDefinitionToEditor(ShapeDefinition definition)
        {
            isLoadingSymbolDefinition = true;
            try
            {
                txtShapeId.Text = definition.Id;
                cmbShapeType.SelectedIndex = definition.Type == "Line" ? 0 : definition.Type == "Rectangle" ? 1 : 2;
                chkLineGroupTarget.IsChecked = definition.IsLineGroupTarget;
                rbLineNormal.IsChecked = definition.LineRole == LineRoleType.Normal;
                rbLineWireA.IsChecked = definition.LineRole == LineRoleType.WireA;
                rbLineWireB.IsChecked = definition.LineRole == LineRoleType.WireB;
                rbLineWireC.IsChecked = definition.LineRole == LineRoleType.WireC;
                rbLineBus.IsChecked = definition.LineRole == LineRoleType.Bus;
                txtSymbolGridCount.Text = Math.Max(1, definition.GridWidthCount).ToString();
                txtSymbolGridHeightCount.Text = Math.Max(1, definition.GridHeightCount).ToString();

                viewModel.TempConnectionPoints.Clear();
                viewModel.TempConnectionPoints.AddRange(definition.ConnectionPoints);
                viewModel.TempVectorElements.Clear();
                viewModel.TempVectorElements.AddRange(definition.VectorElements.Select(CloneVectorElement));
                viewModel.TempAttributes.Clear();
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

        private List<SymbolAttribute> GetDrawingElementAttributes(UIElement element)
        {
            if (GetDrawingElementDataRecord(element) is DataRecord record)
            {
                return record.Attributes.Select(CloneAttribute).ToList();
            }

            return GetLegacyDrawingElementAttributes(element);
        }

        private List<SymbolAttribute> GetLegacyDrawingElementAttributes(UIElement element)
        {
            if (drawingElementAttributes.TryGetValue(element, out var attributes))
            {
                return attributes.Select(CloneAttribute).ToList();
            }

            if (element is Canvas && element is FrameworkElement frameworkElement && frameworkElement.Tag is ShapeDefinition definition)
            {
                return definition.Attributes.Select(CloneAttribute).ToList();
            }

            return new List<SymbolAttribute>();
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
            drawingElementAttributes.Remove(deletedElement);
            drawingElementDataReferences.Remove(deletedElement);
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

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "CSV出力先を指定",
                Filter = "CSV base file (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"drawing-export-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var saveData = CreateSaveData();
                ExportCsvFiles(saveData, dialog.FileName);
                MessageBox.Show(this, "CSVを出力しました。", "CSV出力", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"CSV出力に失敗しました。\n{ex.Message}", "CSV出力", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportCsvFiles(DrawingSaveData saveData, string baseFilePath)
        {
            string directory = System.IO.Path.GetDirectoryName(baseFilePath) ?? "";
            string baseName = System.IO.Path.GetFileNameWithoutExtension(baseFilePath);

            WriteCsvFile(GetCsvPath(directory, baseName, "data_definitions"), CreateDataDefinitionCsvRows(saveData));
            WriteCsvFile(GetCsvPath(directory, baseName, "shape_definitions"), CreateShapeDefinitionCsvRows(saveData));
            WriteCsvFile(GetCsvPath(directory, baseName, "shape_connection_points"), CreateShapeConnectionPointCsvRows(saveData));
            WriteCsvFile(GetCsvPath(directory, baseName, "shape_vector_elements"), CreateShapeVectorElementCsvRows(saveData));
            WriteCsvFile(GetCsvPath(directory, baseName, "placed_items"), CreatePlacedItemCsvRows(saveData));
            WriteCsvFile(GetCsvPath(directory, baseName, "line_groups"), CreateLineGroupCsvRows(saveData));
            WriteCsvFile(GetCsvPath(directory, baseName, "connection_nodes"), CreateConnectionNodeCsvRows(saveData));

            foreach (var definition in saveData.DataDefinitions)
            {
                string definitionFileName = CreateSafeFileNamePart(definition.Name);
                WriteCsvFile(
                    GetCsvPath(directory, baseName, $"data_records_{definitionFileName}"),
                    CreateDataRecordCsvRowsByDefinition(saveData, definition));
                WriteCsvFile(
                    GetCsvPath(directory, baseName, $"placed_item_data_{definitionFileName}"),
                    CreatePlacedItemDataCsvRowsByDefinition(saveData, definition));
                WriteCsvFile(
                    GetCsvPath(directory, baseName, $"line_group_data_{definitionFileName}"),
                    CreateLineGroupDataCsvRowsByDefinition(saveData, definition));
            }
        }

        private void BtnRefreshCsvTables_Click(object sender, RoutedEventArgs e)
        {
            RefreshCsvTableList();
        }

        private void LstCsvTables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            lstCsvColumns.Items.Clear();
            gridCsvPreview.ItemsSource = null;
            if (lstCsvTables.SelectedItem is not string tableName ||
                !csvTableColumns.TryGetValue(tableName, out var columns))
            {
                return;
            }

            foreach (var column in columns)
            {
                lstCsvColumns.Items.Add(column);
            }

            if (csvTableRows.TryGetValue(tableName, out var rows))
            {
                gridCsvPreview.ItemsSource = CreateCsvPreviewDataTable(columns, rows).DefaultView;
            }
        }

        private void RefreshCsvTableList()
        {
            var saveData = CreateSaveData();
            csvTableColumns = CreateCsvTableColumns(saveData);
            csvTableRows = CreateCsvTableRows(saveData);
            lstCsvTables.Items.Clear();
            lstCsvColumns.Items.Clear();
            gridCsvPreview.ItemsSource = null;
            foreach (var tableName in csvTableColumns.Keys.OrderBy(name => name))
            {
                lstCsvTables.Items.Add(tableName);
            }
        }

        private DataTable CreateCsvPreviewDataTable(List<string> columns, List<List<string>> rows)
        {
            var table = new DataTable();
            foreach (var column in columns)
            {
                table.Columns.Add(column);
            }

            foreach (var row in rows)
            {
                var values = columns
                    .Select((_, index) => index < row.Count ? row[index] : "")
                    .Cast<object>()
                    .ToArray();
                table.Rows.Add(values);
            }

            return table;
        }

        private Dictionary<string, List<string>> CreateCsvTableColumns(DrawingSaveData saveData)
        {
            var tables = new Dictionary<string, List<string>>
            {
                ["data_definitions"] = GetCsvHeader(CreateDataDefinitionCsvRows(saveData)),
                ["shape_definitions"] = GetCsvHeader(CreateShapeDefinitionCsvRows(saveData)),
                ["shape_connection_points"] = GetCsvHeader(CreateShapeConnectionPointCsvRows(saveData)),
                ["shape_vector_elements"] = GetCsvHeader(CreateShapeVectorElementCsvRows(saveData)),
                ["placed_items"] = GetCsvHeader(CreatePlacedItemCsvRows(saveData)),
                ["line_groups"] = GetCsvHeader(CreateLineGroupCsvRows(saveData)),
                ["connection_nodes"] = GetCsvHeader(CreateConnectionNodeCsvRows(saveData))
            };

            foreach (var definition in saveData.DataDefinitions)
            {
                string definitionFileName = CreateSafeFileNamePart(definition.Name);
                tables[$"data_records_{definitionFileName}"] = GetCsvHeader(CreateDataRecordCsvRowsByDefinition(saveData, definition));
                tables[$"placed_item_data_{definitionFileName}"] = GetCsvHeader(CreatePlacedItemDataCsvRowsByDefinition(saveData, definition));
                tables[$"line_group_data_{definitionFileName}"] = GetCsvHeader(CreateLineGroupDataCsvRowsByDefinition(saveData, definition));
            }

            return tables;
        }

        private List<string> GetCsvHeader(IEnumerable<IEnumerable<string>> rows)
        {
            return rows.First().ToList();
        }

        private Dictionary<string, List<List<string>>> CreateCsvTableRows(DrawingSaveData saveData)
        {
            var tables = new Dictionary<string, List<List<string>>>
            {
                ["data_definitions"] = GetCsvBody(CreateDataDefinitionCsvRows(saveData)),
                ["shape_definitions"] = GetCsvBody(CreateShapeDefinitionCsvRows(saveData)),
                ["shape_connection_points"] = GetCsvBody(CreateShapeConnectionPointCsvRows(saveData)),
                ["shape_vector_elements"] = GetCsvBody(CreateShapeVectorElementCsvRows(saveData)),
                ["placed_items"] = GetCsvBody(CreatePlacedItemCsvRows(saveData)),
                ["line_groups"] = GetCsvBody(CreateLineGroupCsvRows(saveData)),
                ["connection_nodes"] = GetCsvBody(CreateConnectionNodeCsvRows(saveData))
            };

            foreach (var definition in saveData.DataDefinitions)
            {
                string definitionFileName = CreateSafeFileNamePart(definition.Name);
                tables[$"data_records_{definitionFileName}"] = GetCsvBody(CreateDataRecordCsvRowsByDefinition(saveData, definition));
                tables[$"placed_item_data_{definitionFileName}"] = GetCsvBody(CreatePlacedItemDataCsvRowsByDefinition(saveData, definition));
                tables[$"line_group_data_{definitionFileName}"] = GetCsvBody(CreateLineGroupDataCsvRowsByDefinition(saveData, definition));
            }

            return tables;
        }

        private List<List<string>> GetCsvBody(IEnumerable<IEnumerable<string>> rows)
        {
            return rows.Skip(1)
                .Select(row => row.ToList())
                .ToList();
        }

        private string GetCsvPath(string directory, string baseName, string suffix)
        {
            return System.IO.Path.Combine(directory, $"{baseName}_{suffix}.csv");
        }

        private string CreateSafeFileNamePart(string value)
        {
            string safe = string.Join("_", value.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "unnamed" : safe;
        }

        private void WriteCsvFile(string path, IEnumerable<IEnumerable<string>> rows)
        {
            File.WriteAllLines(path, rows.Select(CreateCsvLine), new UTF8Encoding(true));
        }

        private string CreateCsvLine(IEnumerable<string> values)
        {
            return string.Join(",", values.Select(EscapeCsvValue));
        }

        private string EscapeCsvValue(string value)
        {
            value ??= "";
            if (value.Contains('"') || value.Contains(',') || value.Contains('\r') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private string CsvNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private IEnumerable<IEnumerable<string>> CreateDataDefinitionCsvRows(DrawingSaveData saveData)
        {
            yield return new[] { "DefinitionName", "ParentDefinitionName", "IdItemName", "ItemOrder", "ItemName" };
            foreach (var definition in saveData.DataDefinitions)
            {
                if (definition.Items.Count == 0)
                {
                    yield return new[] { definition.Name, definition.ParentDefinitionName, definition.IdItemName, "", "" };
                    continue;
                }

                for (int i = 0; i < definition.Items.Count; i++)
                {
                    yield return new[] { definition.Name, definition.ParentDefinitionName, definition.IdItemName, (i + 1).ToString(CultureInfo.InvariantCulture), definition.Items[i] };
                }
            }
        }

        private IEnumerable<IEnumerable<string>> CreateDataRecordCsvRows(DrawingSaveData saveData)
        {
            yield return new[] { "DefinitionName", "DataId", "ItemName", "Value" };
            foreach (var record in saveData.DataRecords)
            {
                if (record.Attributes.Count == 0)
                {
                    yield return new[] { record.DefinitionName, record.Name, "", "" };
                    continue;
                }

                foreach (var attribute in record.Attributes)
                {
                    yield return new[] { record.DefinitionName, record.Name, attribute.Key, attribute.Value };
                }
            }
        }

        private IEnumerable<IEnumerable<string>> CreateDataRecordCsvRowsByDefinition(DrawingSaveData saveData, SavedDataDefinition definition)
        {
            var itemNames = GetSavedDataDefinitionItemsIncludingParents(saveData, definition);
            yield return new[] { "DataId" }.Concat(itemNames);

            foreach (var record in saveData.DataRecords.Where(record => record.DefinitionName == definition.Name))
            {
                var valueByKey = record.Attributes
                    .GroupBy(attribute => attribute.Key)
                    .ToDictionary(group => group.Key, group => group.First().Value);
                yield return new[] { record.Name }
                    .Concat(itemNames.Select(itemName => valueByKey.TryGetValue(itemName, out var value) ? value : ""));
            }
        }

        private IEnumerable<IEnumerable<string>> CreateShapeDefinitionCsvRows(DrawingSaveData saveData)
        {
            yield return new[] { "DefinitionId", "Type", "LineRole", "IsLineGroupTarget", "GridWidthCount", "GridHeightCount", "FixedSize", "FixedHeight" };
            foreach (var definition in saveData.ShapeDefinitions)
            {
                yield return new[]
                {
                    definition.Id,
                    definition.Type,
                    definition.LineRole,
                    definition.IsLineGroupTarget ? "true" : "false",
                    definition.GridWidthCount.ToString(CultureInfo.InvariantCulture),
                    definition.GridHeightCount.ToString(CultureInfo.InvariantCulture),
                    CsvNumber(definition.FixedSize),
                    CsvNumber(definition.FixedHeight)
                };
            }
        }

        private IEnumerable<IEnumerable<string>> CreateShapeConnectionPointCsvRows(DrawingSaveData saveData)
        {
            yield return new[] { "DefinitionId", "PointOrder", "X", "Y" };
            foreach (var definition in saveData.ShapeDefinitions)
            {
                for (int i = 0; i < definition.ConnectionPoints.Count; i++)
                {
                    var point = definition.ConnectionPoints[i];
                    yield return new[] { definition.Id, (i + 1).ToString(CultureInfo.InvariantCulture), CsvNumber(point.X), CsvNumber(point.Y) };
                }
            }
        }

        private IEnumerable<IEnumerable<string>> CreateShapeVectorElementCsvRows(DrawingSaveData saveData)
        {
            yield return new[] { "DefinitionId", "ElementOrder", "Type", "X1", "Y1", "X2", "Y2" };
            foreach (var definition in saveData.ShapeDefinitions)
            {
                for (int i = 0; i < definition.VectorElements.Count; i++)
                {
                    var element = definition.VectorElements[i];
                    yield return new[]
                    {
                        definition.Id,
                        (i + 1).ToString(CultureInfo.InvariantCulture),
                        element.Type,
                        CsvNumber(element.X1),
                        CsvNumber(element.Y1),
                        CsvNumber(element.X2),
                        CsvNumber(element.Y2)
                    };
                }
            }
        }

        private IEnumerable<IEnumerable<string>> CreatePlacedItemCsvRows(DrawingSaveData saveData)
        {
            yield return new[] { "ItemNo", "DefinitionId", "Type", "X", "Y", "X2", "Y2", "Width", "Height", "DataDefinitionName", "DataId", "StartNodeId", "EndNodeId" };
            foreach (var item in saveData.Items)
            {
                yield return new[]
                {
                    item.ItemNo.ToString(CultureInfo.InvariantCulture),
                    item.DefinitionId,
                    item.Type,
                    CsvNumber(item.X),
                    CsvNumber(item.Y),
                    CsvNumber(item.X2),
                    CsvNumber(item.Y2),
                    CsvNumber(item.Width),
                    CsvNumber(item.Height),
                    item.DataDefinitionName,
                    item.DataId,
                    item.StartNodeId?.ToString(CultureInfo.InvariantCulture) ?? "",
                    item.EndNodeId?.ToString(CultureInfo.InvariantCulture) ?? ""
                };
            }
        }

        private IEnumerable<IEnumerable<string>> CreatePlacedItemDataCsvRows(DrawingSaveData saveData)
        {
            yield return new[] { "ItemNo", "DataDefinitionName", "DataId", "ItemName", "Value" };
            foreach (var item in saveData.Items)
            {
                var matchedRecord = FindSavedDataRecordForItem(saveData, item);
                string definitionName = matchedRecord?.DefinitionName ?? "";
                string dataId = matchedRecord?.Name ?? "";

                if (item.Attributes.Count == 0)
                {
                    yield return new[] { item.ItemNo.ToString(CultureInfo.InvariantCulture), definitionName, dataId, "", "" };
                    continue;
                }

                foreach (var attribute in item.Attributes)
                {
                    yield return new[] { item.ItemNo.ToString(CultureInfo.InvariantCulture), definitionName, dataId, attribute.Key, attribute.Value };
                }
            }
        }

        private IEnumerable<IEnumerable<string>> CreateLineGroupCsvRows(DrawingSaveData saveData)
        {
            yield return new[] { "GroupNo", "DefinitionId", "LineItemNos", "DataDefinitionName", "DataId" };
            foreach (var group in saveData.LineGroups)
            {
                yield return new[]
                {
                    group.GroupNo.ToString(CultureInfo.InvariantCulture),
                    group.DefinitionId,
                    string.Join(";", group.ItemNos),
                    group.DataDefinitionName,
                    group.DataId
                };
            }
        }

        private IEnumerable<IEnumerable<string>> CreateLineGroupDataCsvRowsByDefinition(DrawingSaveData saveData, SavedDataDefinition definition)
        {
            var itemNames = GetSavedDataDefinitionItemsIncludingParents(saveData, definition);
            yield return new[] { "GroupNo", "DefinitionId", "LineItemNos", "DataId" }.Concat(itemNames);

            foreach (var group in saveData.LineGroups.Where(group => group.DataDefinitionName == definition.Name))
            {
                var record = saveData.DataRecords.FirstOrDefault(record =>
                    record.DefinitionName == group.DataDefinitionName &&
                    record.Name == group.DataId);
                var valueByKey = (record?.Attributes ?? new List<SavedSymbolAttribute>())
                    .GroupBy(attribute => attribute.Key)
                    .ToDictionary(attributeGroup => attributeGroup.Key, attributeGroup => attributeGroup.First().Value);

                yield return new[]
                    {
                        group.GroupNo.ToString(CultureInfo.InvariantCulture),
                        group.DefinitionId,
                        string.Join(";", group.ItemNos),
                        group.DataId
                    }
                    .Concat(itemNames.Select(itemName => valueByKey.TryGetValue(itemName, out var value) ? value : ""));
            }
        }

        private IEnumerable<IEnumerable<string>> CreatePlacedItemDataCsvRowsByDefinition(DrawingSaveData saveData, SavedDataDefinition definition)
        {
            var itemNames = GetSavedDataDefinitionItemsIncludingParents(saveData, definition);
            yield return new[] { "ItemNo", "DefinitionId", "Type", "DataId" }.Concat(itemNames);

            foreach (var item in saveData.Items)
            {
                var matchedRecord = FindSavedDataRecordForItem(saveData, item);
                if (matchedRecord?.DefinitionName != definition.Name)
                {
                    continue;
                }

                var valueByKey = matchedRecord.Attributes
                    .GroupBy(attribute => attribute.Key)
                    .ToDictionary(group => group.Key, group => group.First().Value);
                yield return new[]
                    {
                        item.ItemNo.ToString(CultureInfo.InvariantCulture),
                        item.DefinitionId,
                        item.Type,
                        matchedRecord.Name
                    }
                    .Concat(itemNames.Select(itemName => valueByKey.TryGetValue(itemName, out var value) ? value : ""));
            }
        }

        private List<string> GetSavedDataDefinitionItemsIncludingParents(DrawingSaveData saveData, SavedDataDefinition definition)
        {
            var result = new List<string>();
            var visitedDefinitions = new HashSet<string>();
            AppendSavedDataDefinitionItems(saveData, definition, result, visitedDefinitions);
            return result;
        }

        private void AppendSavedDataDefinitionItems(
            DrawingSaveData saveData,
            SavedDataDefinition definition,
            List<string> result,
            HashSet<string> visitedDefinitions)
        {
            if (!visitedDefinitions.Add(definition.Name))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(definition.ParentDefinitionName))
            {
                var parent = saveData.DataDefinitions.FirstOrDefault(item => item.Name == definition.ParentDefinitionName);
                if (parent != null)
                {
                    AppendSavedDataDefinitionItems(saveData, parent, result, visitedDefinitions);
                }
            }

            foreach (var itemName in definition.Items)
            {
                if (!result.Contains(itemName))
                {
                    result.Add(itemName);
                }
            }
        }

        private SavedDataRecord? FindSavedDataRecordByAttributes(DrawingSaveData saveData, IEnumerable<SavedSymbolAttribute> attributes)
        {
            var attributeList = attributes.ToList();
            foreach (var definition in saveData.DataDefinitions)
            {
                if (string.IsNullOrWhiteSpace(definition.IdItemName))
                {
                    continue;
                }

                string idValue = attributeList
                    .FirstOrDefault(attribute => attribute.Key == definition.IdItemName)
                    ?.Value
                    .Trim() ?? "";
                if (idValue.Length == 0)
                {
                    continue;
                }

                var record = saveData.DataRecords.FirstOrDefault(item =>
                    item.DefinitionName == definition.Name &&
                    item.Name == idValue);
                if (record != null)
                {
                    return record;
                }
            }

            return null;
        }

        private SavedDataRecord? FindSavedDataRecordForItem(DrawingSaveData saveData, SavedDrawingItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.DataDefinitionName) && !string.IsNullOrWhiteSpace(item.DataId))
            {
                var record = saveData.DataRecords.FirstOrDefault(record =>
                    record.DefinitionName == item.DataDefinitionName &&
                    record.Name == item.DataId);
                if (record != null)
                {
                    return record;
                }
            }

            return FindSavedDataRecordByAttributes(saveData, item.Attributes);
        }

        private IEnumerable<IEnumerable<string>> CreateConnectionNodeCsvRows(DrawingSaveData saveData)
        {
            yield return new[] { "NodeId", "X", "Y", "EndpointItemNo", "EndpointIsStart" };
            foreach (var node in saveData.ConnectionNodes)
            {
                if (node.Endpoints.Count == 0)
                {
                    yield return new[] { node.NodeId.ToString(CultureInfo.InvariantCulture), CsvNumber(node.X), CsvNumber(node.Y), "", "" };
                    continue;
                }

                foreach (var endpoint in node.Endpoints)
                {
                    yield return new[]
                    {
                        node.NodeId.ToString(CultureInfo.InvariantCulture),
                        CsvNumber(node.X),
                        CsvNumber(node.Y),
                        endpoint.ItemNo.ToString(CultureInfo.InvariantCulture),
                        endpoint.IsStart ? "true" : "false"
                    };
                }
            }
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
            drawingElementAttributes.Clear();
            drawingElementDataReferences.Clear();
            lineGroupDataReferences.Clear();
            selectedLineGroupKey = "";

            foreach (var element in DrawCanvas.Children.OfType<UIElement>().Where(IsSavedDrawingElement).ToList())
            {
                DrawCanvas.Children.Remove(element);
            }

            var definitions = saveData.ShapeDefinitions
                .Select(CreateShapeDefinition)
                .ToList();
            viewModel.ReplaceShapeDefinitions(definitions);
            viewModel.ReplaceDataDefinitions(saveData.DataDefinitions.Select(CreateDataDefinition));
            viewModel.ReplaceDataRecords(saveData.DataRecords.Select(CreateDataRecord));

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
            RestoreLineGroups(saveData, elementsByItemNo);

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
                IsLineGroupTarget = savedDefinition.IsLineGroupTarget,
                ConnectionPoints = savedDefinition.ConnectionPoints
                    .Select(point => new Point(point.X, point.Y))
                    .ToList(),
                VectorElements = savedDefinition.VectorElements
                    .Select(CreateVectorElement)
                    .ToList(),
                Attributes = savedDefinition.Attributes
                    .Select(CreateAttribute)
                    .ToList()
            };
        }

        private DataDefinition CreateDataDefinition(SavedDataDefinition savedDefinition)
        {
            var definition = new DataDefinition
            {
                Name = savedDefinition.Name,
                ParentDefinitionName = savedDefinition.ParentDefinitionName,
                IdItemName = savedDefinition.IdItemName
            };
            foreach (var item in savedDefinition.Items)
            {
                definition.Items.Add(item);
            }

            return definition;
        }

        private DataRecord CreateDataRecord(SavedDataRecord savedRecord)
        {
            return new DataRecord
            {
                Name = savedRecord.Name,
                DefinitionName = savedRecord.DefinitionName,
                Attributes = savedRecord.Attributes.Select(CreateAttribute).ToList()
            };
        }

        private SymbolAttribute CreateAttribute(SavedSymbolAttribute attribute)
        {
            return new SymbolAttribute
            {
                Key = attribute.Key,
                Value = attribute.Value
            };
        }

        private SavedSymbolAttribute CreateSavedAttribute(SymbolAttribute attribute)
        {
            return new SavedSymbolAttribute
            {
                Key = attribute.Key,
                Value = attribute.Value
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
                var line = CreateLineElement(definition, new Point(item.X, item.Y), new Point(item.X2, item.Y2));
                RestoreDrawingDataAssignment(line, item);
                return line;
            }

            if (item.Type == "Symbol")
            {
                if (definition.VectorElements.Count == 0 && item.VectorElements.Count > 0)
                {
                    definition.VectorElements = item.VectorElements.Select(CreateVectorElement).ToList();
                }

                var symbol = CreateSymbolElement(definition, item.X, item.Y, item.Width, item.Height);
                RestoreDrawingDataAssignment(symbol, item);
                if (!drawingElementDataReferences.ContainsKey(symbol) && item.Attributes.Count == 0)
                {
                    drawingElementAttributes[symbol] = definition.Attributes.Select(CloneAttribute).ToList();
                }
                return symbol;
            }

            var rectangle = CreateRectangleElement(definition, item.X, item.Y, item.Width, item.Height);
            RestoreDrawingDataAssignment(rectangle, item);
            return rectangle;
        }

        private void RestoreDrawingDataAssignment(UIElement element, SavedDrawingItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.DataDefinitionName) && !string.IsNullOrWhiteSpace(item.DataId))
            {
                drawingElementDataReferences[element] = new DrawingDataReference
                {
                    DataDefinitionName = item.DataDefinitionName,
                    DataId = item.DataId
                };
                return;
            }

            var attributes = item.Attributes.Select(CreateAttribute).ToList();
            var record = FindDataRecordByAttributes(attributes);
            if (record != null)
            {
                drawingElementDataReferences[element] = new DrawingDataReference
                {
                    DataDefinitionName = record.DefinitionName,
                    DataId = record.Name
                };
                return;
            }

            drawingElementAttributes[element] = attributes;
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
            drawingElementAttributes[symbol] = definition.Attributes.Select(CloneAttribute).ToList();

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

        private void RestoreLineGroups(DrawingSaveData saveData, Dictionary<int, UIElement> elementsByItemNo)
        {
            foreach (var savedGroup in saveData.LineGroups)
            {
                var lines = savedGroup.ItemNos
                    .Select(itemNo => elementsByItemNo.TryGetValue(itemNo, out var element) ? element as Line : null)
                    .Where(line => line != null)
                    .Cast<Line>()
                    .ToList();
                if (lines.Count < 2 ||
                    string.IsNullOrWhiteSpace(savedGroup.DataDefinitionName) ||
                    string.IsNullOrWhiteSpace(savedGroup.DataId))
                {
                    continue;
                }

                lineGroupDataReferences[GetLineGroupKey(lines)] = new DrawingDataReference
                {
                    DataDefinitionName = savedGroup.DataDefinitionName,
                    DataId = savedGroup.DataId
                };
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
                    IsLineGroupTarget = definition.IsLineGroupTarget,
                    ConnectionPoints = definition.ConnectionPoints
                        .Select(point => new SavedPoint { X = point.X, Y = point.Y })
                        .ToList(),
                    VectorElements = definition.VectorElements
                        .Select(CreateSavedVectorElement)
                        .ToList(),
                    Attributes = definition.Attributes
                        .Select(CreateSavedAttribute)
                        .ToList()
                });
            }

            foreach (var definition in viewModel.DataDefinitions)
            {
                saveData.DataDefinitions.Add(new SavedDataDefinition
                {
                    Name = definition.Name,
                    ParentDefinitionName = definition.ParentDefinitionName,
                    IdItemName = definition.IdItemName,
                    Items = definition.Items.ToList()
                });
            }

            foreach (var record in viewModel.DataRecords)
            {
                saveData.DataRecords.Add(new SavedDataRecord
                {
                    Name = record.Name,
                    DefinitionName = record.DefinitionName,
                    Attributes = record.Attributes.Select(CreateSavedAttribute).ToList()
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

            int groupNo = 1;
            foreach (var group in GetLineGroupComponents())
            {
                string groupKey = GetLineGroupKey(group);
                lineGroupDataReferences.TryGetValue(groupKey, out var reference);
                saveData.LineGroups.Add(new SavedLineGroup
                {
                    GroupNo = groupNo++,
                    DefinitionId = ((ShapeDefinition)group[0].Tag).Id,
                    ItemNos = group
                        .Where(line => itemNumbers.ContainsKey(line))
                        .Select(line => itemNumbers[line])
                        .OrderBy(itemNoValue => itemNoValue)
                        .ToList(),
                    DataDefinitionName = reference?.DataDefinitionName ?? "",
                    DataId = reference?.DataId ?? ""
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

        private List<List<Line>> GetLineGroupComponents()
        {
            var lines = DrawCanvas.Children
                .OfType<Line>()
                .Where(IsLineGroupCandidate)
                .ToList();
            var adjacency = lines.ToDictionary(line => line, _ => new HashSet<Line>());

            foreach (var node in lineStartNodes.Values.Concat(lineEndNodes.Values).Distinct())
            {
                var nodeLines = node.Endpoints
                    .Select(endpoint => endpoint.Line)
                    .Where(line => adjacency.ContainsKey(line))
                    .ToList();
                ConnectSameDefinitionLines(nodeLines, adjacency);
            }

            foreach (var pair in lineConnections)
            {
                if (!adjacency.ContainsKey(pair.Key))
                {
                    continue;
                }

                AddLineGroupEdge(pair.Key, pair.Value.StartElement as Line, adjacency);
                AddLineGroupEdge(pair.Key, pair.Value.EndElement as Line, adjacency);
            }

            var result = new List<List<Line>>();
            var visited = new HashSet<Line>();
            foreach (var line in lines)
            {
                if (!visited.Add(line))
                {
                    continue;
                }

                var component = new List<Line>();
                var stack = new Stack<Line>();
                stack.Push(line);
                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    component.Add(current);
                    foreach (var next in adjacency[current])
                    {
                        if (visited.Add(next))
                        {
                            stack.Push(next);
                        }
                    }
                }

                if (component.Count >= 1)
                {
                    result.Add(component);
                }
            }

            return result;
        }

        private bool IsLineGroupCandidate(Line line)
        {
            return line.Tag is ShapeDefinition definition &&
                   definition.Type == "Line" &&
                   definition.IsLineGroupTarget;
        }

        private void ConnectSameDefinitionLines(List<Line> lines, Dictionary<Line, HashSet<Line>> adjacency)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    AddLineGroupEdge(lines[i], lines[j], adjacency);
                }
            }
        }

        private void AddLineGroupEdge(Line line, Line? otherLine, Dictionary<Line, HashSet<Line>> adjacency)
        {
            if (otherLine == null ||
                !adjacency.ContainsKey(line) ||
                !adjacency.ContainsKey(otherLine) ||
                !HaveSameDefinitionId(line, otherLine))
            {
                return;
            }

            adjacency[line].Add(otherLine);
            adjacency[otherLine].Add(line);
        }

        private bool HaveSameDefinitionId(Line first, Line second)
        {
            return first.Tag is ShapeDefinition firstDefinition &&
                   second.Tag is ShapeDefinition secondDefinition &&
                   firstDefinition.Id == secondDefinition.Id;
        }

        private string GetLineGroupKey(IEnumerable<Line> lines)
        {
            return string.Join("|", lines
                .Select(line => RuntimeHelpers.GetHashCode(line).ToString(CultureInfo.InvariantCulture))
                .OrderBy(value => value));
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
            ApplySavedDrawingDataReference(element, item);

            if (element is Canvas canvas)
            {
                item.X = Canvas.GetLeft(canvas);
                item.Y = Canvas.GetTop(canvas);
                item.Width = canvas.Width;
                item.Height = canvas.Height;
                item.VectorElements = definition.VectorElements
                    .Select(CreateSavedVectorElement)
                    .ToList();
                item.Attributes = CreateSavedLegacyAttributes(canvas);
            }
            else if (element is Rectangle rectangle)
            {
                item.X = Canvas.GetLeft(rectangle);
                item.Y = Canvas.GetTop(rectangle);
                item.Width = rectangle.Width;
                item.Height = rectangle.Height;
                item.Attributes = CreateSavedLegacyAttributes(rectangle);
            }
            else if (element is Line line)
            {
                item.X = line.X1;
                item.Y = line.Y1;
                item.X2 = line.X2;
                item.Y2 = line.Y2;
                item.Attributes = CreateSavedLegacyAttributes(line);

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

        private void ApplySavedDrawingDataReference(UIElement element, SavedDrawingItem item)
        {
            var record = GetDrawingElementDataRecord(element);
            if (record == null)
            {
                return;
            }

            item.DataDefinitionName = record.DefinitionName;
            item.DataId = record.Name;
        }

        private List<SavedSymbolAttribute> CreateSavedLegacyAttributes(UIElement element)
        {
            if (GetDrawingElementDataRecord(element) != null)
            {
                return new List<SavedSymbolAttribute>();
            }

            return GetLegacyDrawingElementAttributes(element)
                .Select(CreateSavedAttribute)
                .ToList();
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


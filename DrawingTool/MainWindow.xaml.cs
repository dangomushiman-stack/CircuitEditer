using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DrawingTool
{
    // ★役割を5種類に拡張
    public enum LineRoleType { Normal, WireA, WireB, WireC, Bus }

    public class ShapeDefinition
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsResizable => Type != "Symbol";
        public double FixedSize { get; set; }
        public int GridWidthCount { get; set; } = 3;
        public List<Point> ConnectionPoints { get; set; } = new List<Point>();
        
        public LineRoleType LineRole { get; set; } = LineRoleType.Normal;

        public string DisplayText => $"[{Id}] {Type}" + 
            (Type == "Symbol" ? $" ({GridWidthCount}×{GridWidthCount}グリッド / {ConnectionPoints.Count}点)" : "") +
            (Type == "Line" && LineRole == LineRoleType.WireA ? " (接続線A)" : "") +
            (Type == "Line" && LineRole == LineRoleType.WireB ? " (接続線B)" : "") +
            (Type == "Line" && LineRole == LineRoleType.WireC ? " (接続線C)" : "") +
            (Type == "Line" && LineRole == LineRoleType.Bus ? " (バス)" : "");
    }

    public class PlacedDrawingInfo
    {
        public int No { get; set; }
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Size { get; set; }
        public int GridWidthCount { get; set; }
        public int ConnectionPointCount { get; set; }

        public string DisplayText
        {
            get
            {
                if (Type == "Line")
                    return $"{No}: [{Id}] 線分 X1={X}, Y1={Y}, X2={X2}, Y2={Y2}";

                if (Type == "Rectangle")
                    return $"{No}: [{Id}] 四角形 X={X}, Y={Y}, W={Width}, H={Height}";

                return $"{No}: [{Id}] シンボル X={X}, Y={Y}, Grid={GridWidthCount}×{GridWidthCount}, Size={Size}, 接続点={ConnectionPointCount}";
            }
        }
    }

    public class LineConnectionInfo
    {
        public UIElement StartElement { get; set; }
        public Point StartRelPoint { get; set; }
        public double StartLineRatio { get; set; }

        public UIElement EndElement { get; set; }
        public Point EndRelPoint { get; set; }
        public double EndLineRatio { get; set; }
    }

    public partial class MainWindow : Window
    {
        public ObservableCollection<ShapeDefinition> RegisteredShapes { get; set; } = new ObservableCollection<ShapeDefinition>();
        public ObservableCollection<ShapeDefinition> RegisteredSymbols { get; set; } = new ObservableCollection<ShapeDefinition>();
        public ObservableCollection<PlacedDrawingInfo> PlacedSymbols { get; set; } = new ObservableCollection<PlacedDrawingInfo>();

        private List<Point> tempConnectionPoints = new List<Point>();
        private Dictionary<Line, LineConnectionInfo> lineConnections = new Dictionary<Line, LineConnectionInfo>();
        private LineConnectionInfo wireCSplitTargets = null;

        private bool isDrawingOrMoving = false;
        private bool isResizing = false;
        private Point startPoint;
        private UIElement currentElement = null;
        private Rectangle rectResizeHandle, lineStartHandle, lineEndHandle, activeHandle;
        private const int GridSize = 20;

        private int GetSymbolGridWidthCount()
        {
            if (!int.TryParse(txtSymbolGridCount.Text, out int count)) return 3;
            return Math.Max(1, count);
        }

        private double GetSymbolFixedSize()
        {
            return GetSymbolGridWidthCount() * GridSize;
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
            InitializeHandles();
            lstShapes.ItemsSource = RegisteredShapes;
            lstRegisteredSymbols.ItemsSource = RegisteredSymbols;
            lstPlacedSymbols.ItemsSource = PlacedSymbols;
            
            // 初期データ登録
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-NORMAL", Type = "Line", LineRole = LineRoleType.Normal });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-WIRE-A", Type = "Line", LineRole = LineRoleType.WireA });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-WIRE-B", Type = "Line", LineRole = LineRoleType.WireB });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-WIRE-C", Type = "Line", LineRole = LineRoleType.WireC });
            RegisteredShapes.Add(new ShapeDefinition { Id = "LINE-BUS", Type = "Line", LineRole = LineRoleType.Bus });
            RegisteredShapes.Add(new ShapeDefinition { Id = "RECT-01", Type = "Rectangle" });
            RefreshEditor();
            RefreshPlacedSymbols();
        }

        private void RefreshEditor() {
            if (EditorCanvas == null) return;
            EditorCanvas.Children.Clear();
            double size = GetSymbolFixedSize();
            var guide = new Rectangle { Width = size, Height = size, Stroke = Brushes.LightBlue, StrokeDashArray = new DoubleCollection { 2, 2 } };
            Canvas.SetLeft(guide, 10); Canvas.SetTop(guide, 10); EditorCanvas.Children.Add(guide);
            for (double x = 0; x <= size; x += GridSize) { for (double y = 0; y <= size; y += GridSize) { var dot = new Ellipse { Width = 4, Height = 4, Fill = Brushes.LightGray }; Canvas.SetLeft(dot, x + 10 - 2); Canvas.SetTop(dot, y + 10 - 2); EditorCanvas.Children.Add(dot); } }
            foreach (var p in tempConnectionPoints) { var connDot = new Ellipse { Width = 8, Height = 8, Fill = Brushes.Blue, Stroke = Brushes.White, StrokeThickness = 1 }; Canvas.SetLeft(connDot, p.X + 10 - 4); Canvas.SetTop(connDot, p.Y + 10 - 4); EditorCanvas.Children.Add(connDot); }
        }

        private void RefreshPlacedSymbols()
        {
            PlacedSymbols.Clear();

            int no = 1;

            foreach (UIElement element in DrawCanvas.Children)
            {
                if (element is Canvas symbolCanvas &&
                    symbolCanvas.Tag is ShapeDefinition def &&
                    def.Type == "Symbol")
                {
                    PlacedSymbols.Add(new PlacedDrawingInfo
                    {
                        No = no++,
                        Id = def.Id,
                        Type = def.Type,
                        X = Canvas.GetLeft(symbolCanvas),
                        Y = Canvas.GetTop(symbolCanvas),
                        Size = def.FixedSize,
                        GridWidthCount = def.GridWidthCount,
                        ConnectionPointCount = def.ConnectionPoints.Count
                    });
                }
                else if (element is Line line &&
                         line.Tag is ShapeDefinition lineDef &&
                         lineDef.Type == "Line")
                {
                    PlacedSymbols.Add(new PlacedDrawingInfo
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
                    PlacedSymbols.Add(new PlacedDrawingInfo
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
        }

        private void EditorCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            Point p = e.GetPosition(EditorCanvas); double size = GetSymbolFixedSize();
            double relX = Math.Round((p.X - 10) / GridSize) * GridSize; double relY = Math.Round((p.Y - 10) / GridSize) * GridSize;
            if (relX >= 0 && relX <= size && relY >= 0 && relY <= size) { Point newPoint = new Point(relX, relY); if (tempConnectionPoints.Any(pt => pt == newPoint)) tempConnectionPoints.Remove(newPoint); else tempConnectionPoints.Add(newPoint); RefreshEditor(); }
        }

        private void TxtSymbolGridCount_TextChanged(object sender, TextChangedEventArgs e) { tempConnectionPoints.Clear(); RefreshEditor(); }
        
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

            var def = new ShapeDefinition { 
                Id = txtShapeId.Text, Type = ((ComboBoxItem)cmbShapeType.SelectedItem).Tag.ToString(),
                FixedSize = GetSymbolFixedSize(),
                GridWidthCount = GetSymbolGridWidthCount(),
                ConnectionPoints = new List<Point>(tempConnectionPoints),
                LineRole = role
            };

            RegisteredShapes.Add(def);

            if (def.Type == "Symbol")
            {
                RegisteredSymbols.Add(def);
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
                return;
            }

            if (info.StartElement != null)
            {
                Point p = GetConnectedPoint(info.StartElement, info.StartRelPoint, info.StartLineRatio);
                line.X1 = p.X;
                line.Y1 = p.Y;
            }

            if (info.EndElement != null)
            {
                Point p = GetConnectedPoint(info.EndElement, info.EndRelPoint, info.EndLineRatio);
                line.X2 = p.X;
                line.Y2 = p.Y;
            }
        }

        private bool TryMoveConnectedLineEndpoint(Line line, bool isStartHandle, Point newPoint)
        {
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

            var firstSegment = CloneLineSegment(targetLine, start, splitPoint);
            var secondSegment = CloneLineSegment(targetLine, splitPoint, end);
            int index = DrawCanvas.Children.IndexOf(targetLine);

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
            return (firstSegment, secondSegment);
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
            if (wireCSplitTargets.EndElement is Line endTarget && IsInteriorLinePoint(wireCSplitTargets.EndLineRatio))
            {
                var split = SplitLineAtRatio(endTarget, wireCSplitTargets.EndLineRatio);
                if (split.SecondSegment != null)
                {
                    EnsureLineConnection(wireCLine);
                    lineConnections[wireCLine].EndElement = split.FirstSegment;
                    lineConnections[wireCLine].EndLineRatio = 1;

                    EnsureLineConnection(split.SecondSegment);
                    lineConnections[split.SecondSegment].StartElement = split.FirstSegment;
                    lineConnections[split.SecondSegment].StartLineRatio = 1;
                }
            }
            else if (wireCSplitTargets.StartElement is Line startTarget && IsInteriorLinePoint(wireCSplitTargets.StartLineRatio))
            {
                var split = SplitLineAtRatio(startTarget, wireCSplitTargets.StartLineRatio);
                if (split.SecondSegment != null)
                {
                    EnsureLineConnection(wireCLine);
                    lineConnections[wireCLine].StartElement = split.FirstSegment;
                    lineConnections[wireCLine].StartLineRatio = 1;

                    EnsureLineConnection(split.SecondSegment);
                    lineConnections[split.SecondSegment].StartElement = split.FirstSegment;
                    lineConnections[split.SecondSegment].StartLineRatio = 1;
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
                    if (currentElement is Shape s && s.Tag is ShapeDefinition d) ShowResizeHandles(s, d);
                    else if (currentElement is Canvas c && c.Tag is ShapeDefinition cd) ShowResizeHandles(c, cd);
                } else if (e.OriginalSource == DrawCanvas) { currentElement = null; HideAllHandles(); }
            }
            else if (rbDraw.IsChecked == true) {
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
                    var sym = new Canvas { Width = def.FixedSize, Height = def.FixedSize, Tag = def };
                    sym.Children.Add(new Rectangle { Width = def.FixedSize, Height = def.FixedSize, Stroke = Brushes.DarkOrange, StrokeThickness = 2, Fill = Brushes.Orange });
                    foreach (var cp in def.ConnectionPoints) {
                        var dot = new Ellipse { Width = 6, Height = 6, Fill = Brushes.Blue, Stroke = Brushes.White, StrokeThickness = 1 };
                        Canvas.SetLeft(dot, cp.X - 3); Canvas.SetTop(dot, cp.Y - 3); sym.Children.Add(dot);
                    }
                    Canvas.SetLeft(sym, startPoint.X - def.FixedSize / 2); Canvas.SetTop(sym, startPoint.Y - def.FixedSize / 2);
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

        private void InitializeHandles() { rectResizeHandle = CreateHandle(Brushes.Red, Cursors.SizeNWSE); lineStartHandle = CreateHandle(Brushes.Green, Cursors.Cross); lineEndHandle = CreateHandle(Brushes.Green, Cursors.Cross); }
        private Rectangle CreateHandle(Brush c, Cursor cur) { var h = new Rectangle { Width = 10, Height = 10, Fill = c, Visibility = Visibility.Collapsed, Cursor = cur }; h.MouseLeftButtonDown += (s, e) => { isResizing = true; activeHandle = h; startPoint = SnapToGrid(e.GetPosition(DrawCanvas)); DrawCanvas.CaptureMouse(); e.Handled = true; }; DrawCanvas.Children.Add(h); return h; }
        private Point SnapToGrid(Point p) => new Point(Math.Round(p.X / GridSize) * GridSize, Math.Round(p.Y / GridSize) * GridSize);
        private bool IsHandle(Shape s) => s == rectResizeHandle || s == lineStartHandle || s == lineEndHandle;
        private void HideAllHandles() { rectResizeHandle.Visibility = lineStartHandle.Visibility = lineEndHandle.Visibility = Visibility.Collapsed; }
        private void ShowResizeHandles(UIElement e, ShapeDefinition d) { HideAllHandles(); if (!d.IsResizable) return; if (e is Rectangle r) { rectResizeHandle.Visibility = Visibility.Visible; UpdateHandlePositions(r); } else if (e is Line l) { lineStartHandle.Visibility = lineEndHandle.Visibility = Visibility.Visible; UpdateHandlePositions(l); } }
        private void UpdateHandlePositions(UIElement e) { if (e is Rectangle r) { Canvas.SetLeft(rectResizeHandle, Canvas.GetLeft(r) + r.Width - 5); Canvas.SetTop(rectResizeHandle, Canvas.GetTop(r) + r.Height - 5); } else if (e is Line l) { Canvas.SetLeft(lineStartHandle, l.X1 - 5); Canvas.SetTop(lineStartHandle, l.Y1 - 5); Canvas.SetLeft(lineEndHandle, l.X2 - 5); Canvas.SetTop(lineEndHandle, l.Y2 - 5); } }
    }
}

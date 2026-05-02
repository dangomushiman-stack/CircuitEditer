using System.Collections.Generic;
using System.Windows;

namespace DrawingTool.Models
{
    public class ShapeDefinition
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsResizable => Type != "Symbol";
        public double FixedSize { get; set; }
        public double FixedHeight { get; set; }
        public int GridWidthCount { get; set; } = 3;
        public int GridHeightCount { get; set; } = 3;
        public List<Point> ConnectionPoints { get; set; } = new List<Point>();
        public List<SymbolVectorElement> VectorElements { get; set; } = new List<SymbolVectorElement>();
        public List<SymbolAttribute> Attributes { get; set; } = new List<SymbolAttribute>();

        public LineRoleType LineRole { get; set; } = LineRoleType.Normal;

        public string DisplayText => $"[{Id}] {Type}" +
            (Type == "Symbol" ? $" ({GridWidthCount}x{GridHeightCount} grid / {ConnectionPoints.Count} ports / {VectorElements.Count} vectors / {Attributes.Count} attrs)" : "") +
            (Type == "Line" && LineRole == LineRoleType.WireA ? " (WireA)" : "") +
            (Type == "Line" && LineRole == LineRoleType.WireB ? " (WireB)" : "") +
            (Type == "Line" && LineRole == LineRoleType.WireC ? " (WireC)" : "") +
            (Type == "Line" && LineRole == LineRoleType.Bus ? " (Bus)" : "");
    }
}

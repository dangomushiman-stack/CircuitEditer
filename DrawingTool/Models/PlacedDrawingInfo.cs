using System.Windows;

namespace DrawingTool.Models
{
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
        public int GridHeightCount { get; set; }
        public int ConnectionPointCount { get; set; }
        public int AttributeCount { get; set; }
        public string DataDefinitionName { get; set; } = "";
        public string DataId { get; set; } = "";
        public bool IsLineGroup { get; set; }
        public string LineGroupKey { get; set; } = "";
        public int LineCount { get; set; }
        public UIElement? Element { get; set; }

        public string DisplayText
        {
            get
            {
                string dataText = string.IsNullOrWhiteSpace(DataDefinitionName) || string.IsNullOrWhiteSpace(DataId)
                    ? ""
                    : $" Data=[{DataDefinitionName}:{DataId}]";

                if (IsLineGroup)
                {
                    return $"{No}: [{Id}] LineGroup Lines={LineCount}{dataText}";
                }

                if (Type == "Line")
                {
                    return $"{No}: [{Id}] Line X1={X:0}, Y1={Y:0}, X2={X2:0}, Y2={Y2:0}{dataText}";
                }

                if (Type == "Rectangle")
                {
                    return $"{No}: [{Id}] Rectangle X={X:0}, Y={Y:0}, W={Width:0}, H={Height:0}{dataText}";
                }

                return $"{No}: [{Id}] Symbol X={X:0}, Y={Y:0}, Grid={GridWidthCount}x{GridHeightCount}, Size={Size:0}x{Height:0}, Ports={ConnectionPointCount}{dataText}";
            }
        }
    }
}

using System.Windows;

namespace DrawingTool.Models
{
    public class LineConnectionInfo
    {
        public UIElement? StartElement { get; set; }
        public Point StartRelPoint { get; set; }
        public double StartLineRatio { get; set; }

        public UIElement? EndElement { get; set; }
        public Point EndRelPoint { get; set; }
        public double EndLineRatio { get; set; }
    }
}

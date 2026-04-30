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

        public string DisplayText
        {
            get
            {
                if (Type == "Line")
                    return $"{No}: [{Id}] 線分 X1={X:0}, Y1={Y:0}, X2={X2:0}, Y2={Y2:0}";

                if (Type == "Rectangle")
                    return $"{No}: [{Id}] 四角形 X={X:0}, Y={Y:0}, W={Width:0}, H={Height:0}";

                return $"{No}: [{Id}] シンボル X={X:0}, Y={Y:0}, Grid={GridWidthCount}x{GridHeightCount}, Size={Size:0}x{Height:0}, 接続点={ConnectionPointCount}";
            }
        }
    }
}

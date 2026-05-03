using System.Collections.Generic;

namespace DrawingTool.Models
{
    public class DrawingSaveData
    {
        public int Version { get; set; } = 1;
        public List<SavedShapeDefinition> ShapeDefinitions { get; set; } = new List<SavedShapeDefinition>();
        public List<SavedDataDefinition> DataDefinitions { get; set; } = new List<SavedDataDefinition>();
        public List<SavedDataRecord> DataRecords { get; set; } = new List<SavedDataRecord>();
        public List<SavedDrawingItem> Items { get; set; } = new List<SavedDrawingItem>();
        public List<SavedLineGroup> LineGroups { get; set; } = new List<SavedLineGroup>();
        public List<SavedConnectionNode> ConnectionNodes { get; set; } = new List<SavedConnectionNode>();
    }

    public class SavedDataDefinition
    {
        public string Name { get; set; } = "";
        public string ParentDefinitionName { get; set; } = "";
        public string IdItemName { get; set; } = "";
        public bool UsePythonIdGenerator { get; set; }
        public string PythonIdGeneratorCode { get; set; } = "";
        public List<string> Items { get; set; } = new List<string>();
        public List<SavedDataDefinitionItem> ItemDefinitions { get; set; } = new List<SavedDataDefinitionItem>();
    }

    public class SavedDataDefinitionItem
    {
        public string Name { get; set; } = "";
        public string ReferenceDefinitionName { get; set; } = "";
        public bool UsePythonValueGenerator { get; set; }
        public string PythonValueGeneratorCode { get; set; } = "";
    }

    public class SavedDataRecord
    {
        public string Name { get; set; } = "";
        public string DefinitionName { get; set; } = "";
        public List<SavedSymbolAttribute> Attributes { get; set; } = new List<SavedSymbolAttribute>();
    }

    public class SavedShapeDefinition
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public double FixedSize { get; set; }
        public double FixedHeight { get; set; }
        public int GridWidthCount { get; set; }
        public int GridHeightCount { get; set; }
        public string LineRole { get; set; } = "";
        public bool IsLineGroupTarget { get; set; }
        public string LineGroupDataDefinitionName { get; set; } = "";
        public bool AutoCreateLineGroupData { get; set; }
        public List<SavedPoint> ConnectionPoints { get; set; } = new List<SavedPoint>();
        public List<SavedSymbolVectorElement> VectorElements { get; set; } = new List<SavedSymbolVectorElement>();
        public List<SavedSymbolAttribute> Attributes { get; set; } = new List<SavedSymbolAttribute>();
    }

    public class SavedLineGroup
    {
        public int GroupNo { get; set; }
        public string DefinitionId { get; set; } = "";
        public List<int> ItemNos { get; set; } = new List<int>();
        public string DataDefinitionName { get; set; } = "";
        public string DataId { get; set; } = "";
    }

    public class SavedSymbolAttribute
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class SavedSymbolVectorElement
    {
        public string Type { get; set; } = "";
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
    }

    public class SavedDrawingItem
    {
        public int ItemNo { get; set; }
        public string DefinitionId { get; set; } = "";
        public string Type { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public List<SavedSymbolVectorElement> VectorElements { get; set; } = new List<SavedSymbolVectorElement>();
        public List<SavedSymbolAttribute> Attributes { get; set; } = new List<SavedSymbolAttribute>();
        public string DataDefinitionName { get; set; } = "";
        public string DataId { get; set; } = "";
        public int? StartNodeId { get; set; }
        public int? EndNodeId { get; set; }
        public SavedLineEndpointConnection? StartConnection { get; set; }
        public SavedLineEndpointConnection? EndConnection { get; set; }
    }

    public class SavedLineEndpointConnection
    {
        public int TargetItemNo { get; set; }
        public string TargetKind { get; set; } = "";
        public double RelativeX { get; set; }
        public double RelativeY { get; set; }
        public double LineRatio { get; set; }
    }

    public class SavedConnectionNode
    {
        public int NodeId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public List<SavedNodeEndpoint> Endpoints { get; set; } = new List<SavedNodeEndpoint>();
    }

    public class SavedNodeEndpoint
    {
        public int ItemNo { get; set; }
        public bool IsStart { get; set; }
    }

    public class SavedPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}

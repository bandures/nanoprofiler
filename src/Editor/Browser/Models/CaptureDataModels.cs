using System;
using System.Collections.Generic;

namespace NanoProfiler.Editor
{
    public class HierarchyNode
    {
        public NodeType Type;
        public int UniqueId;
        public string Name;
        public string RemotePath;
        public List<HierarchyNode> Children = new();
        
        public string Version;
        public string DeviceModel;
        // Trace-only fields
        public string SessionId;
        public DateTime Date;
        public ulong SizeBytes;
        public bool IsDownloaded;
        public string LocalPath;

        public enum NodeType { Version, DeviceModel, Trace }
    }
}

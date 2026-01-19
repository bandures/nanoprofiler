using System;
using System.Collections.Generic;
using Semver;

namespace NanoProfiler.Editor
{
    internal class HierarchyNode
    {
        public NodeType Type;
        public int UniqueId;
        public string Name;
        public string RemotePath;
        public List<HierarchyNode> Children = new();
        
        public SemVersion Version;
        public string DeviceModel;
        // Trace-only fields
        public string SessionId;
        public DateTime CreateTimestamp;
        public DateTime? UploadTimestamp;
        public ulong SizeBytes;
        public bool IsDownloaded;
        public string LocalPath;

        public enum NodeType { Version, DeviceModel, Trace }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace NanoProfiler.Editor
{
    public class BrowserWindowController
    {
        private const string SEM_VER_PATTERN = @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$";        
        private readonly GoogleCloudService _service;
        private readonly List<HierarchyNode> _hierarchy;
        private readonly Regex _semVerRegex;
        private readonly PerfettoHost _perfettoHost;
        private int _lastId = 0;
        
        public BrowserWindowController()
        {
            _service = new GoogleCloudService();
            _hierarchy = new List<HierarchyNode>();
            _semVerRegex = new Regex(SEM_VER_PATTERN, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);
            
            var absolute = Path.GetFullPath("Packages/com.meowtron.nanoprofiler/PerfettoUI~");
            _perfettoHost = new PerfettoHost(absolute);
        }

        public async Task<List<HierarchyNode>> RefreshVersionsAsync()
        {
            _hierarchy.Clear();
            
            var remoteVersions = await _service.ListVersionsAsync();
            foreach (var remotePath in remoteVersions)
            {
                var name = Path.GetFileName(remotePath.TrimEnd('/'));
                
                if (!_semVerRegex.IsMatch(name))
                    continue;
                
                var versionNode = new HierarchyNode 
                { 
                    Type = HierarchyNode.NodeType.Version,
                    UniqueId = _lastId++,
                    Name = name, 
                    RemotePath = remotePath,
                    Version = name
                };
                _hierarchy.Add(versionNode);

                // Extra increment for a fake child to show expand arrow
                _lastId++;
            }
            
            return _hierarchy;
        }

        public async ValueTask LoadModelsAsync(HierarchyNode versionNode)
        {
            if (versionNode.Type != HierarchyNode.NodeType.Version || versionNode.Children.Count > 0)
                return;

            var remoteModels = await _service.ListModelsAsync(versionNode.RemotePath);
            foreach (var remotePath in remoteModels)
            {
                var name = Path.GetFileName(remotePath.TrimEnd('/'));
                versionNode.Children.Add(new HierarchyNode
                {
                    Type = HierarchyNode.NodeType.DeviceModel,
                    UniqueId = _lastId++,
                    Name = name,
                    RemotePath = remotePath,
                    Version = versionNode.Version,
                    DeviceModel = name
                });
            }
        }

        public async ValueTask LoadTracesAsync(HierarchyNode deviceNode)
        {
            if (deviceNode.Type != HierarchyNode.NodeType.DeviceModel || deviceNode.Children.Count > 0)
                return;

            var remoteTraces = await _service.ListTracesAsync(deviceNode.RemotePath);
            foreach (var traceInfo in remoteTraces)
            {
                var name = Path.GetFileName(traceInfo.RemotePath);
                var sessionId = "unknown";
                var date = DateTime.MinValue;

                // Example: 2025-07-31-085caec9-7c94-43c9-9a99-624bdd882907.trace
                // Split by first 3 dashes for date
                var nameParts = name.Split('-');
                if (nameParts.Length >= 4)
                {
                    if(DateTime.TryParse($"{nameParts[0]}-{nameParts[1]}-{nameParts[2]}", out var parsedDate))
                        date = parsedDate;
                        
                    sessionId = name[11..].Replace(".trace", ""); // Skip YYYY-MM-DD-
                }
                
                deviceNode.Children.Add(new HierarchyNode
                {
                    Type = HierarchyNode.NodeType.Trace,
                    UniqueId = _lastId++,
                    Name = name,
                    RemotePath = traceInfo.RemotePath,
                    Version = deviceNode.Version,
                    DeviceModel = deviceNode.DeviceModel,
                    SessionId = sessionId,
                    Date = date,
                    SizeBytes = traceInfo.SizeBytes,
                    IsDownloaded = _service.IsTraceDownloaded(traceInfo.RemotePath),
                    LocalPath = _service.GetTraceLocalPath(traceInfo.RemotePath)
                });
            }
        }

        public async Task DownloadFileAsync(HierarchyNode trace)
        {
            if (trace.IsDownloaded)
                return;
            
            var localPath = await _service.DownloadTraceAsync(trace.RemotePath);
            if (localPath == null)
                return;
            
            trace.IsDownloaded = true;
            trace.LocalPath = localPath;
        }

        public async Task DownloadAllAsync(List<HierarchyNode> traces)
        {
            foreach (var trace in traces)
                await DownloadFileAsync(trace);
        }

        public void OpenTrace(HierarchyNode trace)
        {
            if (!trace.IsDownloaded)
            {
                Debug.LogWarning($"[NanoProfiler] Trace {trace.Name} is not downloaded yet.");
                return;
            }
            
            _perfettoHost.OpenTrace(trace.LocalPath);
        }
    }
}
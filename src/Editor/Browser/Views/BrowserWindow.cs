using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NanoProfiler.Editor
{
    public class BrowserWindow : EditorWindow
    {
        private BrowserWindowController _controller;
        
        private TreeView _treeView;
        private MultiColumnListView _listView;
        private ToolbarSearchField _searchField;
        private ToolbarButton _btnRefresh;
        private ToolbarButton _btnDownloadAll;
        private Label _statusLabel;

        private List<HierarchyNode> _roots = new();
        private Dictionary<int, HierarchyNode> _idToItem = new();
        private HierarchyNode _currentSelectedNode;
        private List<HierarchyNode> _currentVisibleTraces = new();

        [MenuItem("Window/Analysis/NanoProfiler Browser")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<BrowserWindow>();
            wnd.titleContent = new GUIContent("Cloud Profiler");
            wnd.minSize = new Vector2(800, 600);
        }

        public void CreateGUI()
        {
            _controller = new BrowserWindowController();

            // Load UXML
            var assetPath = AssetDatabase.GUIDToAssetPath("3fbee47ceaf78dc468d1d8bbffb1bc6e");
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath); 
            if (visualTree == null)
            {
                var label = new Label("Could not load UXML. Check path.");
                rootVisualElement.Add(label);
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            // Bind UI Elements
            _treeView = rootVisualElement.Q<TreeView>("versionTree");
            _listView = rootVisualElement.Q<MultiColumnListView>("traceList");
            _searchField = rootVisualElement.Q<ToolbarSearchField>("searchField");
            _btnRefresh = rootVisualElement.Q<ToolbarButton>("btnRefresh");
            _btnDownloadAll = rootVisualElement.Q<ToolbarButton>("btnDownloadAll");
            _statusLabel = rootVisualElement.Q<Label>("statusLabel");

            SetupTreeView();
            SetupListView();
            SetupToolbar();

            // Initial Load
            RefreshData();
        }

        public void OnDestroy()
        {
            
        }

        private void SetupToolbar()
        {
            _btnRefresh.clicked += RefreshData;

            _searchField.RegisterValueChangedCallback(_ => RefreshListContent());

            _btnDownloadAll.clicked += DownloadVisibleTracesAsync;
        }

        private async void RefreshData()
        {
            try
            {
                _statusLabel.text = "Fetching versions...";
                _roots = await _controller.RefreshVersionsAsync();
                BuildTreeView();
                _statusLabel.text = "Versions loaded.";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private async void DownloadVisibleTracesAsync()
        {
            try
            {
                if (_currentVisibleTraces.Count <= 0)
                    return;

                _statusLabel.text = $"Downloading {_currentVisibleTraces.Count} files...";
                await _controller.DownloadAllAsync(_currentVisibleTraces);
                _listView.RefreshItems();
                _statusLabel.text = "Download complete.";

            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void SetupTreeView()
        {
            _treeView.makeItem = () => new Label();
            _treeView.bindItem = (element, id) => 
            {
                if (id == -1)
                    return;
                var node = GetNodeByIndex(id);
                if (node == null)
                    return;
                if (element is Label label)
                    label.text = node.Name;
            };

            _treeView.viewDataKey = "ProfilerVersionTree";
            
#if UNITY_6000_0_OR_NEWER
            _treeView.itemExpandedChanged += args =>
            {
                if (!args.isExpanded)
                    return;
                if (!_idToItem.TryGetValue(args.id, out var node))
                    return;
                if (node is not { Type: HierarchyNode.NodeType.Version } || node.Children.Count != 0) 
                    return;
                
                LoadModelsAsync(node, args.id).SafeFireAndForget();
            };
#else
            _treeView.itemsChosen += objects =>
            {
                var obj = objects.FirstOrDefault();
                if (obj is not HierarchyNode node)
                    return;
                if (node is not { Type: HierarchyNode.NodeType.Version } || node.Children.Count != 0) 
                    return;
                
                LoadModelsAsync(node, node.UniqueId).SafeFireAndForget();
            };
#endif

            _treeView.selectionChanged += (selectedIds) => 
            {
                var obj = selectedIds.FirstOrDefault();
                if (obj is not HierarchyNode node)
                    return;
                
                _currentSelectedNode = node;
                RefreshListContent();
            };
        }

        private async ValueTask LoadModelsAsync(HierarchyNode node, int id)
        {
            _statusLabel.text = $"Loading models for {node.Name}...";
            await _controller.LoadModelsAsync(node);
            
            BuildTreeView();
            _treeView.ExpandItem(id);
            _statusLabel.text = "Models loaded.";
        }

        private HierarchyNode GetNodeByIndex(int index)
        {
            var id = _treeView.GetIdForIndex(index);
            if (_idToItem.TryGetValue(id, out var item))
                return item;
            
            Debug.LogWarning($"Node {id} not found.");
            return null;
        }

        private void SetupListView()
        {
            _listView.itemsSource = _currentVisibleTraces;

            _listView.columns["colName"].makeCell = () => new Label();
            _listView.columns["colName"].bindCell = (element, index) => 
                (element as Label).text = _currentVisibleTraces[index].Name;

            _listView.columns["colSession"].makeCell = () => new Label();
            _listView.columns["colSession"].bindCell = (element, index) => 
                (element as Label).text = _currentVisibleTraces[index].SessionId;

            _listView.columns["colSize"].makeCell = () => new Label();
            _listView.columns["colSize"].bindCell = (element, index) => 
                (element as Label).text = $"{_currentVisibleTraces[index].SizeBytes / 1024.0 / 1024.0:F2} MB";

            _listView.columns["colStatus"].makeCell = () => new Label();
            _listView.columns["colStatus"].bindCell = (element, index) => 
            {
                var trace = _currentVisibleTraces[index];
                var label = (element as Label);
                label.text = trace.IsDownloaded ? "Local" : "Cloud";
                label.style.color = trace.IsDownloaded ? new StyleColor(Color.green) : new StyleColor(new Color(0.2f, 0.6f, 1f));
            };

            _listView.itemsChosen += (obj) => 
            {
                foreach(var item in obj)
                {
                    if (item is not HierarchyNode { Type: HierarchyNode.NodeType.Trace } trace)
                        continue;
                    
                    if (trace.IsDownloaded)
                    {
                        _controller.OpenTrace(trace);
                    }
                    else
                    {
                        DownloadAndOpen(trace);
                    }
                }
            };
        }

        private async void DownloadAndOpen(HierarchyNode trace)
        {
            _statusLabel.text = $"Downloading {trace.Name}...";
            await _controller.DownloadFileAsync(trace);
            _listView.RefreshItems();
            _statusLabel.text = $"Opening {trace.Name}...";
            _controller.OpenTrace(trace);
        }

        private void BuildTreeView()
        {
            _idToItem.Clear();
            var treeViewItems = _roots.Select(ConvertToTreeData).ToList();
            
            _treeView.SetRootItems(treeViewItems);
            _treeView.Rebuild();
            
            return;

            TreeViewItemData<HierarchyNode> ConvertToTreeData(HierarchyNode node)
            {
                _idToItem[node.UniqueId] = node;
                
                var children = new List<TreeViewItemData<HierarchyNode>>();
                if (node.Children is { Count: > 0 })
                {
                    foreach (var child in node.Children)
                        children.Add(ConvertToTreeData(child));
                }
                else if (node.Type == HierarchyNode.NodeType.Version)
                {
                    // Fake node to show expand arrow
                    _idToItem[node.UniqueId + 1] = null;
                    children.Add(new TreeViewItemData<HierarchyNode>(node.UniqueId + 1, null));
                    //_treeView?.CollapseItem(node.UniqueId);
                }

                return new TreeViewItemData<HierarchyNode>(node.UniqueId, node, children);
            }
        }

        private async void RefreshListContent()
        {
            try
            {
                if (_currentSelectedNode is not { Type: HierarchyNode.NodeType.DeviceModel })
                {
                    _currentVisibleTraces.Clear();
                    _listView.RefreshItems();
                    return;
                }

                _statusLabel.text = $"Loading traces for {_currentSelectedNode.Name}...";
                await _controller.LoadTracesAsync(_currentSelectedNode);
                _currentVisibleTraces.Clear();
                _currentVisibleTraces.AddRange(_currentSelectedNode.Children);
                _listView.RefreshItems();
                _statusLabel.text = "Traces loaded.";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
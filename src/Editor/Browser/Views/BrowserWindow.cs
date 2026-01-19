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

        private readonly List<HierarchyNode> _roots = new();
        private readonly Dictionary<int, HierarchyNode> _idToItem = new();
        private readonly List<HierarchyNode> _currentVisibleTraces = new();
        
        private string _currentFilter;
        private HierarchyNode _currentSelectedNode;
        

        [MenuItem("Window/Analysis/NanoProfiler Browser")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<BrowserWindow>();
            wnd.titleContent = new GUIContent("NanoProfiler Browser");
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

        private void SetupToolbar()
        {
            _btnRefresh.clicked += () => RefreshData(_currentFilter);

            _searchField.RegisterValueChangedCallback(val => FilterContent(val));

            _btnDownloadAll.clicked += DownloadVisibleTracesAsync;
        }

        private void FilterContent(ChangeEvent<string> evt)
        {
            _currentFilter = evt.newValue;
            RefreshData(_currentFilter);
        }

        private async void RefreshData(string filter = null)
        {
            try
            {
                _statusLabel.text = "Fetching versions...";
                _roots.Clear();
                _roots.AddRange(await _controller.RefreshVersionsAsync());
                BuildTreeView(filter);
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
            _treeView.viewDataKey = "ProfilerVersionTree";
            
            _treeView.makeItem = MakeCell;
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
            
            BuildTreeView(_currentFilter);
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

            _listView.columns["colName"].makeCell = MakeCell;
            _listView.columns["colName"].bindCell = (element, index) => 
                ((Label)element).text = _currentVisibleTraces[index].Name;

            _listView.columns["colSession"].makeCell = MakeCell;
            _listView.columns["colSession"].bindCell = (element, index) => 
                ((Label)element).text = _currentVisibleTraces[index].SessionId;

            _listView.columns["colSize"].makeCell = MakeCell;
            _listView.columns["colSize"].bindCell = (element, index) => 
                ((Label)element).text = $"{EditorUtility.FormatBytes((long)_currentVisibleTraces[index].SizeBytes)}";

            _listView.columns["colDate"].makeCell = MakeCell;
            _listView.columns["colDate"].bindCell = (element, index) => 
                ((Label)element).text = _currentVisibleTraces[index].UploadTimestamp?.ToString() ?? "N/A";

            _listView.columns["colStatus"].makeCell = MakeCell;
            _listView.columns["colStatus"].bindCell = (element, index) => 
            {
                var trace = _currentVisibleTraces[index];
                if (element is not Label label)
                    return;
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

        private VisualElement MakeCell()
        {
            var label = new Label();
            label.AddToClassList("list-label");
            return label;
        }

        private async void DownloadAndOpen(HierarchyNode trace)
        {
            try
            {
                _statusLabel.text = $"Downloading {trace.Name}...";
                await _controller.DownloadFileAsync(trace);
                _listView.RefreshItems();
                _statusLabel.text = $"Opening {trace.Name}...";
                _controller.OpenTrace(trace);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void BuildTreeView(string filter)
        {
            _idToItem.Clear();
            
            var treeViewItems = new List<TreeViewItemData<HierarchyNode>>();
            foreach (var root in _roots)
            {
                if (ConvertToTreeData(root, out var item))
                    treeViewItems.Add(item);
            }
            
            _treeView.SetRootItems(treeViewItems);
            _treeView.Rebuild();
            
            return;

            bool ConvertToTreeData(HierarchyNode node, out TreeViewItemData<HierarchyNode> item)
            {
                item = default;
                
                if (filter != null && !node.RemotePath.Contains(filter))
                    return false;
                
                _idToItem[node.UniqueId] = node;
                
                List<TreeViewItemData<HierarchyNode>> children = null;
                if (node.Type == HierarchyNode.NodeType.Version)
                {
                    children = new();
                    if (node.Children is { Count: > 0 })
                    {
                        foreach (var child in node.Children)
                        {
                            if (ConvertToTreeData(child, out var childItem))
                                children.Add(childItem);
                        }
                    }
                    else
                    {
                        // Fake node to show expand arrow
                        _idToItem[node.UniqueId + 1] = null;
                        children.Add(new TreeViewItemData<HierarchyNode>(node.UniqueId + 1, null));
                    }
                }

                item = new TreeViewItemData<HierarchyNode>(node.UniqueId, node, children);
                return true;
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
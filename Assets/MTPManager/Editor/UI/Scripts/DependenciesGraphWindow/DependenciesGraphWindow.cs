using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    using LM  = LogManager;
    public class DependenciesGraphWindow : EditorWindow
    {
        // private List<PackageInstallInfo> _packageInfoList;
        private Dictionary<string, int> _packageLevels;
        private const float VERTICAL_SPACING = 200f;
        private const float HORIZONTAL_SPACING = 200f;
        private const float HOVER_UPDATE_INTERVAL = 0.5f;

        private Vector2 _panOffset = Vector2.zero;
        private bool _isPanning;
        private string _draggingNode;
        private Vector2 _lastMousePosition;
        private float _zoom = 1f;
        private const float MIN_ZOOM = 0.5f;
        private const float MAX_ZOOM = 2f;
        private const float ZOOM_SPEED = 0.1f;
        private float _lastClickTime;
        private const float DOUBLE_CLICK_TIME = 0.3f;
        private bool _showArrows = true;

        // layout-space positions (before zoom/pan); screen = layout * zoom + pan
        private Dictionary<string, Vector2> _layoutPositions;
        private Dictionary<string, Vector2> _cachedPositions;
        private bool _needsPositionUpdate = true;
        private Vector2 _lastWindowSize;
        private float _lastZoom;
        private Vector2 _lastPanOffset;
        private Dictionary<string, int> _cachedConnectionCounts;

        private List<DependencyGraphLine> _cachedLines;
        private bool _needsLineUpdate = true;
        private float _lastHoverUpdateTime;
        
        private static IProjectInfoService ProjectInfoService => ServiceLocator.Get<IProjectInfoService>();
        private static IPackageJsonService PackageJsonService => ServiceLocator.Get<IPackageJsonService>();
        private static IRootPackageService RootPackageService => ServiceLocator.Get<IRootPackageService>();
        private static IMTPackageInfoService MTPInfoService => ServiceLocator.Get<IMTPackageInfoService>();

        public static void Load()
        {
            var window = GetWindow<DependenciesGraphWindow>();
            window.titleContent = new GUIContent("Dependencies Graph");
            window.minSize = new Vector2(800, 600);
            window.CalculatePackageLevels();
            foreach (var kvp in window._packageLevels)
            {
                Debug.Log(kvp.Key + " : " + kvp.Value);
            }

            window._layoutPositions = null;
            window._needsPositionUpdate = true;
            window._cachedPositions = window.CalculatePackagePositions();
            window.InitializeDependencyLines();
        }

        private void CalculatePackageLevels()
        {
            if (_packageLevels == null)
            {
                _packageLevels = new Dictionary<string, int>();
            }
            
            Dictionary<string, int> entries = new Dictionary<string, int>(); // 存所有已安装包节点的入度
            foreach (var p in  MTPInfoService.GetAllPackages())
            {
                entries[p.PackageName] = MTPInfoService.GetAllUsedBy(p.PackageName).Count;
            }
            
            // 拓扑排序
            int level = 0;
            List<string> zeroEntries;
            do
            {
                zeroEntries = new List<string>();
                foreach (var key in entries.Keys)
                {
                    if (entries[key] == 0)
                    {
                        zeroEntries.Add(key);
                        _packageLevels[key] = level;
                    }
                }

                foreach (var key in zeroEntries)
                {
                    foreach (var p in MTPInfoService.GetPackage(key).Dependencies.Keys)
                    {
                        if (entries.TryGetValue(p, out var count))
                        {
                            if (count <= 0)
                            {
                                LM.LogWarning($"[DependenciesGraphWindow] 尝试进行拓扑排序时，入度计算出错，计算到 package {key} 的依赖 {p}");
                                break;
                            }

                            entries[p]--;
                        }
                    }
                    
                    entries.Remove(key);
                }

                level++;
            } while (entries.Count > 0 && zeroEntries.Count > 0);
        }

        private void InitializeDependencyLines()
        {
            if (_cachedPositions == null)
            {
                _cachedPositions = CalculatePackagePositions();
            }

            _cachedLines = new List<DependencyGraphLine>();
            var installedPackages = MTPInfoService.GetAllPackages();

            foreach (var package in installedPackages)
            {
                if (package.Dependencies == null) continue;

                string startPackageName = package.PackageName;
                Vector2 startPos = _cachedPositions[startPackageName];

                foreach (var depName in package.Dependencies.Keys)
                {
                    if (string.IsNullOrEmpty(depName)) continue;

                    if (_cachedPositions.TryGetValue(depName, out Vector2 endPos))
                    {
                        _cachedLines.Add(new DependencyGraphLine
                        {
                            StartPackageName = startPackageName,
                            EndPackageName = depName,
                            Start = startPos,
                            End = endPos,
                            IsSelected = false,
                            SamplePoints = DependencyGraphLineDrawer.PreCalculateSamplePoints(startPos, endPos)
                        });
                    }
                }
            }
        }

        private void UpdateDependencyLines()
        {
            if (_cachedLines == null)
            {
                InitializeDependencyLines();
                return;
            }

            Vector2 mousePosition = Event.current.mousePosition;
            bool skipHoverSelect = !string.IsNullOrEmpty(_draggingNode);

            foreach (var line in _cachedLines)
            {
                line.Start = _cachedPositions[line.StartPackageName];
                line.End = _cachedPositions[line.EndPackageName];
                line.SamplePoints = DependencyGraphLineDrawer.PreCalculateSamplePoints(line.Start, line.End);
                if (!skipHoverSelect)
                {
                    line.IsSelected = DependencyGraphLineDrawer.IsMouseNearLine(mousePosition, line.SamplePoints);
                }
            }
        }

        private void OnGUI()
        {
            if (MTPInfoService?.GetAllPackages() == null || _packageLevels == null)
            {
                return;
            }

            DrawToolbar();
            HandleGraphInput();

            bool draggingNode = !string.IsNullOrEmpty(_draggingNode);
            if (_needsPositionUpdate ||
                _lastWindowSize != position.size ||
                _lastZoom != _zoom ||
                _lastPanOffset != _panOffset)
            {
                _cachedPositions = CalculatePackagePositions();
                _needsPositionUpdate = false;
                _lastWindowSize = position.size;
                _lastZoom = _zoom;
                _lastPanOffset = _panOffset;
                _needsLineUpdate = true;
            }

            float currentTime = Time.realtimeSinceStartup;
            bool shouldUpdateHover = !draggingNode &&
                                     currentTime - _lastHoverUpdateTime >= HOVER_UPDATE_INTERVAL;
            if (_needsLineUpdate || draggingNode || shouldUpdateHover)
            {
                UpdateDependencyLines();
                _needsLineUpdate = false;
                _lastHoverUpdateTime = currentTime;
            }

            BeginWindows();
            DependencyGraphLineDrawer.DrawAll(_cachedLines, _zoom, _showArrows);
            EndWindows();

            BeginWindows();
            DependencyGraphNodeDrawer.Draw(_cachedPositions, _zoom, _draggingNode);
            EndWindows();

            Repaint();
        }
        

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(30)))
            {
                float newZoom = Mathf.Clamp(_zoom + ZOOM_SPEED, MIN_ZOOM, MAX_ZOOM);
                Vector2 zoomCenter = position.size / 2;
                _panOffset = zoomCenter + (_panOffset - zoomCenter) * (newZoom / _zoom);
                _zoom = newZoom;
            }

            if (GUILayout.Button("-", EditorStyles.toolbarButton, GUILayout.Width(30)))
            {
                float newZoom = Mathf.Clamp(_zoom - ZOOM_SPEED, MIN_ZOOM, MAX_ZOOM);
                Vector2 zoomCenter = position.size / 2;
                _panOffset = zoomCenter + (_panOffset - zoomCenter) * (newZoom / _zoom);
                _zoom = newZoom;
            }

            if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                _panOffset = Vector2.zero;
                _zoom = 1f;
                _needsPositionUpdate = true;
            }

            if (GUILayout.Button("Reset Layout", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                _layoutPositions = null;
                _panOffset = Vector2.zero;
                _zoom = 1f;
                _needsPositionUpdate = true;
            }

            GUILayout.Label($"Zoom: {_zoom:F1}x", EditorStyles.toolbarButton);

            _showArrows = GUILayout.Toggle(_showArrows, "Arrows", EditorStyles.toolbarButton, GUILayout.Width(60));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void HandleGraphInput()
        {
            Event e = Event.current;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0)
                    {
                        float timeSinceLastClick = Time.realtimeSinceStartup - _lastClickTime;
                        if (timeSinceLastClick < DOUBLE_CLICK_TIME)
                        {
                            _panOffset = Vector2.zero;
                            _zoom = 1f;
                            _needsPositionUpdate = true;
                            e.Use();
                            _lastClickTime = Time.realtimeSinceStartup;
                            break;
                        }
                        _lastClickTime = Time.realtimeSinceStartup;

                        string hitNode = DependencyGraphNodeDrawer.HitTest(_cachedPositions, e.mousePosition, _zoom);
                        if (hitNode != null)
                        {
                            _draggingNode = hitNode;
                            _isPanning = false;
                            _lastMousePosition = e.mousePosition;
                            e.Use();
                            break;
                        }

                        DependencyGraphLine hitLine = DependencyGraphLineDrawer.HitTest(_cachedLines, e.mousePosition);
                        foreach (var line in _cachedLines)
                        {
                            line.IsSelected = line == hitLine;
                        }
                        _needsLineUpdate = true;

                        if (hitLine != null)
                        {
                            _isPanning = false;
                            _draggingNode = null;
                            e.Use();
                            break;
                        }

                        _isPanning = true;
                        _draggingNode = null;
                        _lastMousePosition = e.mousePosition;
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (e.button == 0)
                    {
                        _isPanning = false;
                        _draggingNode = null;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (e.button != 0) break;

                    Vector2 delta = e.mousePosition - _lastMousePosition;
                    _lastMousePosition = e.mousePosition;

                    if (!string.IsNullOrEmpty(_draggingNode) &&
                        _layoutPositions != null &&
                        _layoutPositions.ContainsKey(_draggingNode))
                    {
                        _layoutPositions[_draggingNode] += delta / _zoom;
                        _cachedPositions[_draggingNode] =
                            _layoutPositions[_draggingNode] * _zoom + _panOffset;
                        _needsLineUpdate = true;
                        e.Use();
                    }
                    else if (_isPanning)
                    {
                        _panOffset += delta;
                        _needsPositionUpdate = true;
                        e.Use();
                    }
                    break;

                case EventType.ScrollWheel:
                {
                    float zoomDelta = -e.delta.y * ZOOM_SPEED;
                    float newZoom = Mathf.Clamp(_zoom + zoomDelta, MIN_ZOOM, MAX_ZOOM);
                    Vector2 zoomCenter = e.mousePosition;
                    _panOffset = zoomCenter + (_panOffset - zoomCenter) * (newZoom / _zoom);
                    _zoom = newZoom;
                    _needsPositionUpdate = true;
                    e.Use();
                    break;
                }
            }
        }

        private Dictionary<string, Vector2> CalculatePackagePositions()
        {
            var screenPositions = new Dictionary<string, Vector2>();
            var installedPackages = MTPInfoService.GetAllPackages();

            if (_cachedConnectionCounts == null)
            {
                _cachedConnectionCounts = new Dictionary<string, int>();
                foreach (var package in installedPackages)
                {
                    string packageName = package.PackageName;
                    int dependencyCount = package.Dependencies?.Count ?? 0;
                    int dependentCount = MTPInfoService.GetAllUsedBy(package.PackageName).Count;
                    _cachedConnectionCounts[packageName] = dependencyCount + dependentCount;
                }
            }

            bool needLayout = _layoutPositions == null ||
                              _layoutPositions.Count != installedPackages.Count ||
                              installedPackages.Any(p => !_layoutPositions.ContainsKey(p.PackageName));

            if (needLayout)
            {
                _layoutPositions = new Dictionary<string, Vector2>();
                var packagesByLevel = installedPackages
                    .GroupBy(p => _packageLevels[p.PackageName])
                    .OrderBy(g => g.Key)
                    .ToList();

                int maxLevel = packagesByLevel.Count > 0 ? 
                    packagesByLevel.Max(g => g.Key) : 0;
                float totalHeight = maxLevel * VERTICAL_SPACING;
                // Depth 0 at top, deeper dependencies further down
                float startY = (position.height - totalHeight) / 2f;

                foreach (var levelGroup in packagesByLevel)
                {
                    int level = levelGroup.Key;
                    var packages = levelGroup
                        .OrderByDescending(p => _cachedConnectionCounts[p.PackageName])
                        .ToList();

                    float levelY = startY + level * VERTICAL_SPACING;
                    float startX = position.width / 2 - (packages.Count - 1) * HORIZONTAL_SPACING / 2;
                    for (int i = 0; i < packages.Count; i++)
                    {
                        float x = startX + i * HORIZONTAL_SPACING;
                        _layoutPositions[packages[i].PackageName] = new Vector2(x, levelY);
                    }
                }
            }

            foreach (var kv in _layoutPositions)
            {
                screenPositions[kv.Key] = kv.Value * _zoom + _panOffset;
            }

            return screenPositions;
        }
    }
}

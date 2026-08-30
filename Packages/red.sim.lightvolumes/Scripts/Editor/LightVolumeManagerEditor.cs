using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VRCLightVolumes {
    [CustomEditor(typeof(LightVolumeManager))]
    public sealed class LightVolumeManagerEditor : UnityEditor.Editor {
        private const string DebugFoldoutSessionKey = "VRCLightVolumes.LightVolumeManagerEditor.DebugFoldout";
        private const string SortLightVolumesMenu = "CONTEXT/LightVolumeManager/Sort Light Volumes";
        private const int VisibleRegistryRows = 12;
        private const float RegistryHeaderHeight = 20f;
        private const float RegistryWeightWidth = 48f;
        private const float RegistryDynamicIndicatorSize = 20f;
        private const float RegistryShadowIndicatorSize = 18f;
        private const float RegistryColorIndicatorSize = 12f;
        private const float RegistryIndicatorSpacing = 5f;
        private const float RegistryIndicatorOuterSpacing = 3f;
        private const float RegistryLightVolumeIndicatorsWidth = RegistryIndicatorOuterSpacing * 2f + RegistryDynamicIndicatorSize;
        private const float PendingShadowIndicatorAlpha = 0.35f;
        private const float RegistryScrollPadding = 2f;
        private const float RegistryScrollSmoothTime = 0.08f;
        private const float RegistryWheelStep = 18f;
        private const float RegistryDragScrollEdge = 24f;
        private const float RegistryDragScrollSpeed = 220f;
        private const float RegistryScrollbarRightInset = 2f;
        private const float InspectorSectionSpacing = 10f;
        private const double StatsRefreshInterval = 0.25d;
        private const double BundleCompressionEstimate = 0.315d;
        private static readonly int[] TextureResolutions = { 16, 32, 64, 128, 256, 512, 1024, 2048 };
        private static readonly string[] TextureResolutionLabels = { "16 x 16", "32 x 32", "64 x 64", "128 x 128", "256 x 256", "512 x 512", "1024 x 1024", "2048 x 2048" };
        private static readonly int[] CoarseValues = { 2, 4, 8 };
        private static readonly string[] CoarseLabels = { "2x", "4x", "8x" };
        private static readonly int[] BakingValues = { 0, 1, 2 };
        private static readonly string[] BakingLabels = { "Progressive", "Bakery", "Custom Lightmapper" };
        private static readonly int[] DownscaleValues = { 0, 1, 2, 3 };
        private static readonly string[] DownscaleLabels = { "None", "2x", "4x", "8x" };
        private static readonly string[] BakeryMaskLabels = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30" };
        private static GUIContent _bakedShadowIndicatorContent;
        private static GUIContent _pendingShadowIndicatorContent;
        private static GUIContent _runtimeShadowIndicatorContent;
        private static GUIContent _dynamicIndicatorContent;
        private static GUIContent _regularLightVolumeIconContent;
        private static GUIContent _additiveLightVolumeIconContent;
        private static GUIContent _pointLightIconContent;
        private static GUIContent _spotLightIconContent;
        private static GUIContent _areaLightIconContent;
        private static readonly GUIContent _lightColorIndicatorContent = new GUIContent(string.Empty, "Light color");

        private sealed class RegistryScrollState {
            public Vector2 Position;
            public float TargetY;
            public float Velocity;
            public double LastRepaintTime;
        }

        private LightVolumeManager _manager;
        private SerializedProperty _lightVolumes;
        private SerializedProperty _pointLights;
        private ReorderableList _lightVolumeList;
        private ReorderableList _pointLightList;
        private readonly RegistryScrollState _lightVolumeScroll = new RegistryScrollState();
        private readonly RegistryScrollState _pointLightScroll = new RegistryScrollState();
        private bool _registryChanged;
        private bool _pointRegistryChanged;
        private GUIStyle _richLabelStyle;
        private double _nextStatsRefresh;
        private int _cachedPointCount = -1;
        private ulong _cachedVramBytes;
        private ulong _cachedBundleBytes;
        private bool _canBatchBakeShadows;
        private bool _multipleManagers;
        private LightVolumeManager _primaryManager;
        private bool _debugExpanded;
        private readonly List<UnityEngine.Object> _textureDependencyRoots = new List<UnityEngine.Object>();
        private readonly HashSet<UnityEngine.Object> _textureDependencyRootSet = new HashSet<UnityEngine.Object>();
        private readonly HashSet<Texture> _directTextureRoots = new HashSet<Texture>();
        private readonly HashSet<Texture> _countedVramTextures = new HashSet<Texture>();
        private readonly HashSet<Texture> _countedBundleTextures = new HashSet<Texture>();
        private readonly HashSet<Texture> _cubemapTextureSources = new HashSet<Texture>();
        private readonly HashSet<Material> _cubemapMaterialSources = new HashSet<Material>();
        private readonly HashSet<Texture> _singleTextureSources = new HashSet<Texture>();
        private readonly HashSet<Material> _singleMaterialSources = new HashSet<Material>();
        private readonly HashSet<PointLightVolumeInstance> _runtimeShadowSourceTargets = new HashSet<PointLightVolumeInstance>();
        private readonly HashSet<PointLightVolumeInstance> _runtimeShadowDirectTargets = new HashSet<PointLightVolumeInstance>();
        private readonly HashSet<PointLightVolumeInstance> _retainedShadowScratchTargets = new HashSet<PointLightVolumeInstance>();
        private readonly HashSet<PointLightVolumeInstance> _oneShotShadowScratchTargets = new HashSet<PointLightVolumeInstance>();

        [MenuItem(SortLightVolumesMenu)]
        // Sorts the selected Manager's Light Volumes by effective voxel density.
        private static void SortLightVolumes(MenuCommand command) {
            if (command.context is LightVolumeManager manager) LightVolumeManagerEditorBackend.SortLightVolumesByVoxelsPerUnit(manager);
        }

        [MenuItem(SortLightVolumesMenu, true)]
        // Enables the sort command only for editable Managers with multiple volumes.
        private static bool CanSortLightVolumes(MenuCommand command) {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !(command.context is LightVolumeManager manager)) return false;
            return manager == LightVolumeManagerEditorBackend.GetPrimaryManager() && manager.LightVolumeInstances != null && manager.LightVolumeInstances.Length > 1;
        }

        // Caches registries, repairs stale entries and installs editor callbacks.
        private void OnEnable() {
            _manager = (LightVolumeManager)target;
            _debugExpanded = SessionState.GetBool(DebugFoldoutSessionKey, false);
            RefreshManagerCount();
            // UdonSharp creates a custom editor before its first Play Mode Udon-to-proxy copy.
            // Merely selecting the Manager late must therefore never repair and serialize the
            // still-stale managed proxy over the running UdonBehaviour.
            if (!EditorApplication.isPlayingOrWillChangePlaymode && _manager != null && _manager == _primaryManager && _manager.SanitizeRegistries()) {
                LightVolumeManagerEditorBackend.CopyProxyToUdon(_manager);
                LightVolumeManagerEditorBackend.QueueRuntimeManagerRefresh(_manager);
                LVUtils.MarkDirty(_manager);
            }
            serializedObject.Update();
            _lightVolumes = serializedObject.FindProperty("LightVolumeInstances");
            _pointLights = serializedObject.FindProperty("PointLightVolumeInstances");
            _lightVolumeList = CreateRegistryList(_lightVolumes, false);
            _pointLightList = CreateRegistryList(_pointLights, true);
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        // Removes the Undo callback owned by this inspector.
        private void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        // Recounts eligible Managers across loaded scenes after hierarchy changes.
        private void OnHierarchyChange() {
            RefreshManagerCount();
        }

        // Reapplies restored settings and fully refreshes runtime texture caches after Undo or Redo.
        private void OnUndoRedoPerformed() {
            if (_manager == null) return;
            RefreshManagerCount();
            if (_manager != _primaryManager) return;
            serializedObject.UpdateIfRequiredOrScript();
            // Undo can restore source/layout fields together with their hidden derived values, so change detection alone cannot reliably invalidate either runtime texture cache.
            LightVolumeManagerEditorBackend.ApplySettings(_manager, false, updateVolumes: false, copyProxyToUdon: false);
            if (!LightVolumeManagerEditorBackend.RefreshRuntimeManagerFromProxyImmediately(_manager, true, true)) LightVolumeManagerEditorBackend.ReinitializeTextures(_manager, true, true);
            _cachedPointCount = -1;
            _nextStatsRefresh = 0d;
            Repaint();
            SceneView.RepaintAll();
        }

        // Draws Manager registries and settings, then propagates explicit changes to runtime state.
        public override void OnInspectorGUI() {
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(_manager);
            serializedObject.Update();
            EditorGUILayout.Space(EditorGUIUtility.singleLineHeight * 0.5f);

            if (LVUtils.IsInPrefabAsset(_manager))
                EditorGUILayout.HelpBox("This component is part of a prefab asset.\nEdit the instance placed in a scene.", MessageType.Warning);
            if (_multipleManagers) {
                string primaryName = _primaryManager != null ? _primaryManager.name : "none";
                string selection = _manager == _primaryManager ? "This is the primary Manager. All other Managers are ignored." : $"This Manager is ignored.";
                EditorGUILayout.HelpBox($"Multiple Light Volume Managers were found in loaded scenes. {selection}\nRemove the extra Managers before building.", MessageType.Error);
                GUILayout.Space(8f);
                if (_manager != _primaryManager) return;
            }

            RefreshStats();
            GUILayout.Label(
                new GUIContent(
                    $"Data size in VRAM: <b>{FormatMegabytes(_cachedVramBytes)} MB</b>",
                    "Estimated peak texture memory used by VRC Light Volumes. Includes loaded atlas, cookie and shadow sources, runtime arrays, active froxel masks, packed Hi-Z, persistent Bake In Game outputs and runtime-shadow bake scratch."),
                RichLabelStyle);
            GUILayout.Label(
                new GUIContent(
                    $"Data size in bundle: <b>{FormatMegabytes(_cachedBundleBytes)} MB (Approximately)</b>",
                    "Estimated compressed texture payload kept by the build: the final Light Volume atlas, projection-source textures and baked shadows. Runtime arrays, froxel masks, Hi-Z and Bake In Game preview shadows are excluded."),
                RichLabelStyle);
            GUILayout.Space(8f);

            DrawScrollableList(_lightVolumeList, _lightVolumeScroll, _lightVolumes, false);
            GUILayout.Space(EditorGUIUtility.singleLineHeight);
            DrawScrollableList(_pointLightList, _pointLightScroll, _pointLights, true);
            GUILayout.Space(EditorGUIUtility.singleLineHeight);

            int previousCookieResolution = _manager.CustomTexturesWidth;
            int previousShadowResolution = _manager.ShadowTexturesWidth;
            int previousBakingMode = _manager.BakingMode;
            bool previousAutoUpdateTextures = _manager.AutoUpdateTextures;
            Texture previousAtlas = _manager.LightVolumeAtlas;
            float previousBrightnessCutoff = _manager.LightsBrightnessCutoff;
            bool hasLightVolumes = HasRegistryEntries(_lightVolumes);
            bool hasPointLights = HasRegistryEntries(_pointLights);
            bool hasPreviousSection = false;

            if (hasPointLights) {
                DrawSectionHeader("Point Lights", hasPreviousSection);
                DrawPointLightSettings();
                hasPreviousSection = true;
            }
            if (hasLightVolumes) {
                DrawSectionHeader("Baking", hasPreviousSection);
                DrawBakingSettings();
                hasPreviousSection = true;
            }
            if (hasLightVolumes || hasPointLights) {
                DrawSectionHeader("Visuals", hasPreviousSection);
                DrawVisualSettings(hasLightVolumes, hasPointLights);
            }
            if (hasPointLights) {
                DrawSectionHeader("Froxel Clustering", true);
                DrawClusteringSettings();
            }
            if (hasLightVolumes || hasPointLights) {
                DrawActions(hasLightVolumes, hasPointLights);
            }
            DrawDebugSection();

            bool managerChanged = serializedObject.ApplyModifiedProperties();
            if (!managerChanged && !_registryChanged && !_pointRegistryChanged) return;

            bool cookieLayoutChanged = previousCookieResolution != _manager.CustomTexturesWidth || _pointRegistryChanged;
            bool shadowLayoutChanged = previousShadowResolution != _manager.ShadowTexturesWidth || _pointRegistryChanged;
            bool pointLightRangesChanged = previousBrightnessCutoff != _manager.LightsBrightnessCutoff;
            bool rebuildRuntimeData = _registryChanged || _pointRegistryChanged || previousAutoUpdateTextures != _manager.AutoUpdateTextures || previousAtlas != _manager.LightVolumeAtlas;
            bool fullRuntimeRefresh = rebuildRuntimeData || cookieLayoutChanged || shadowLayoutChanged;
            LightVolumeManagerEditorBackend.ApplySettings( _manager, false, cookieLayoutChanged, shadowLayoutChanged, fullRuntimeRefresh, !EditorApplication.isPlaying);
            if (!fullRuntimeRefresh) {
                if (pointLightRangesChanged) {
                    LightVolumeManagerEditorBackend.RefreshManagerOnce(_manager, false);
                } else {
                    LightVolumeManagerEditorBackend.ApplyRuntimeManagerSettings(_manager);
                }
            }
            LightVolumeManagerEditorBackend.HandleBakingModeChanged(_manager, previousBakingMode);
            _registryChanged = false;
            _pointRegistryChanged = false;
            _nextStatsRefresh = 0d;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        // Creates a compact reorderable registry with selection and dirty-state callbacks.
        private ReorderableList CreateRegistryList(SerializedProperty source, bool pointLights) {
            ReorderableList list = new ReorderableList(serializedObject, source, true, false, false, false) {
                headerHeight = 0f,
                footerHeight = 0f,
                showDefaultBackground = false
            };
            list.drawElementCallback = (rect, index, active, focused) => DrawRegistryElement(rect, source, index, pointLights);
            list.onReorderCallbackWithDetails = (reorderable, oldIndex, newIndex) => {
                _registryChanged = true;
                if (pointLights) _pointRegistryChanged = true;
            };
            list.onSelectCallback = reorderable => {
                int sourceIndex = reorderable.index;
                if (sourceIndex < 0 || sourceIndex >= source.arraySize) return;
                UnityEngine.Object value = source.GetArrayElementAtIndex(sourceIndex).objectReferenceValue;
                if (value != null) EditorGUIUtility.PingObject(value);
            };
            return list;
        }

        // Draws a registry header and accepts compatible components dropped onto it.
        private void DrawRegistryHeader(Rect rect, SerializedProperty source, bool pointLights, float rightInset) {
            string label = pointLights ? "Point Light Volumes" : "Light Volumes";
            GUIContent title = new GUIContent($"{label} ({source.arraySize})", pointLights ? "At most 128 active lights are rendered." : "At most 32 active volumes are rendered.");
            Rect weightRect = default;
            float titleX = rect.x + 15f;
            float titleRight = rect.xMax - rightInset;
            if (!pointLights) {
                weightRect = new Rect(rect.xMax - rightInset - RegistryWeightWidth + 3f, rect.y, RegistryWeightWidth - 3f, rect.height);
                titleRight = weightRect.x;
            }
            EditorGUI.LabelField(new Rect(titleX, rect.y, Mathf.Max(0f, titleRight - titleX), rect.height), title);
            if (!pointLights) EditorGUI.LabelField(weightRect, "Weight");

            // Registry references are Udon graph links rather than ordinary runtime values. Keep
            // selection available in Play Mode, but do not present drag/drop that cannot safely
            // replace the live graph while UdonSharp is running.
            if (Application.isPlaying) return;
            Event current = Event.current;
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform || !rect.Contains(current.mousePosition)) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (current.type != EventType.DragPerform) return;
            DragAndDrop.AcceptDrag();
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++) {
                GameObject gameObject = DragAndDrop.objectReferences[i] as GameObject;
                if (gameObject == null && DragAndDrop.objectReferences[i] is Component component) gameObject = component.gameObject;
                if (gameObject == null) continue;
                UnityEngine.Object instance = pointLights ? (UnityEngine.Object)gameObject.GetComponent<PointLightVolumeInstance>() : gameObject.GetComponent<LightVolumeInstance>();
                if (instance == null || ContainsReference(source, instance)) continue;
                int index = source.arraySize;
                source.arraySize++;
                source.GetArrayElementAtIndex(index).objectReferenceValue = instance;
                _registryChanged = true;
                if (pointLights) _pointRegistryChanged = true;
            }
            current.Use();
        }

        // Checks whether a serialized registry already contains an object reference.
        private static bool ContainsReference(SerializedProperty source, UnityEngine.Object value) {
            for (int i = 0; i < source.arraySize; i++) if (source.GetArrayElementAtIndex(i).objectReferenceValue == value) return true;
            return false;
        }

        // Checks whether a serialized registry contains at least one live entry.
        private static bool HasRegistryEntries(SerializedProperty source) {
            for (int i = 0; i < source.arraySize; i++) if (source.GetArrayElementAtIndex(i).objectReferenceValue != null) return true;
            return false;
        }

        // Draws one registry row with its type, status indicators and optional volume weight.
        private void DrawRegistryElement(Rect rect, SerializedProperty source, int sourceIndex, bool pointLights) {
            if (sourceIndex < 0 || sourceIndex >= source.arraySize) return;
            UnityEngine.Object value = source.GetArrayElementAtIndex(sourceIndex).objectReferenceValue;
            rect.y += 2f;
            float indicatorWidth = GetRegistryIndicatorWidth(value, pointLights);
            float trailingWidth = indicatorWidth + (pointLights ? 0f : RegistryWeightWidth);
            Rect iconRect = new Rect(rect.x, rect.y, 20f, EditorGUIUtility.singleLineHeight);
            Rect nameRect = new Rect(rect.x + 24f, rect.y, Mathf.Max(0f, rect.width - 24f - trailingWidth), EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(iconRect, GetRegistryIcon(value, pointLights));
            EditorGUI.LabelField(nameRect, value != null ? value.name : "None");
            if (pointLights) {
                if (value is PointLightVolumeInstance pointLight) DrawPointLightIndicators(new Rect(rect.xMax - indicatorWidth, rect.y, indicatorWidth, EditorGUIUtility.singleLineHeight), pointLight);
                return;
            }
            if (!(value is LightVolumeInstance volume)) return;

            if (volume.IsDynamic) {
                Rect dynamicGroupRect = new Rect(rect.xMax - RegistryWeightWidth - indicatorWidth, rect.y, indicatorWidth, EditorGUIUtility.singleLineHeight);
                Rect dynamicRect = new Rect(dynamicGroupRect.x + RegistryIndicatorOuterSpacing, dynamicGroupRect.y + (dynamicGroupRect.height - RegistryDynamicIndicatorSize) * 0.5f, RegistryDynamicIndicatorSize, RegistryDynamicIndicatorSize);
                DrawDynamicIndicator(dynamicRect, true);
            }
            Rect weightRect = new Rect(rect.xMax - RegistryWeightWidth + 3f, rect.y, RegistryWeightWidth - 3f, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginChangeCheck();
            float weight = EditorGUI.FloatField(weightRect, volume.RegistryWeight);
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(volume, "Change Light Volume Weight");
            volume.RegistryWeight = weight;
            LVUtils.MarkDirty(volume);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
            _registryChanged = true;
        }

        // Calculates the trailing width required by the indicators present on one registry row.
        private static float GetRegistryIndicatorWidth(UnityEngine.Object value, bool pointLights) {
            if (!pointLights) return value is LightVolumeInstance volume && volume.IsDynamic ? RegistryLightVolumeIndicatorsWidth : 0f;
            if (!(value is PointLightVolumeInstance pointLight)) return 0f;

            float width = RegistryIndicatorOuterSpacing * 2f + RegistryColorIndicatorSize;
            if (pointLight.IsDynamic) width += RegistryIndicatorSpacing + RegistryDynamicIndicatorSize;
            if (pointLight.Shadows) width += RegistryIndicatorSpacing + RegistryShadowIndicatorSize;
            return width;
        }

        // Draws dynamic, shadow and color indicators for a Point Light Volume row.
        private static void DrawPointLightIndicators(Rect rect, PointLightVolumeInstance pointLight) {
            float dynamicY = rect.y + (rect.height - RegistryDynamicIndicatorSize) * 0.5f;
            float shadowY = rect.y + (rect.height - RegistryShadowIndicatorSize) * 0.5f;
            float colorY = rect.y + (rect.height - RegistryColorIndicatorSize) * 0.5f;
            Rect colorRect = new Rect(rect.xMax - RegistryIndicatorOuterSpacing - RegistryColorIndicatorSize, colorY, RegistryColorIndicatorSize, RegistryColorIndicatorSize);
            float nextIndicatorRight = colorRect.x - RegistryIndicatorSpacing;

            if (pointLight.IsDynamic) {
                Rect dynamicRect = new Rect(nextIndicatorRight - RegistryDynamicIndicatorSize, dynamicY, RegistryDynamicIndicatorSize, RegistryDynamicIndicatorSize);
                DrawDynamicIndicator(dynamicRect, true);
                nextIndicatorRight = dynamicRect.x - RegistryIndicatorSpacing;
            }
            if (pointLight.Shadows) {
                Rect shadowRect = new Rect(nextIndicatorRight - RegistryShadowIndicatorSize, shadowY, RegistryShadowIndicatorSize, RegistryShadowIndicatorSize);
                bool baked = pointLight.ShadowMap != null;
                Color previousColor = GUI.color;
                if (!baked) GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * PendingShadowIndicatorAlpha);
                GUI.Label(shadowRect, GetShadowIndicatorContent(baked, pointLight.BakeInGame));
                GUI.color = previousColor;
            }

            Color lightColor = pointLight.Color;
            lightColor.a = 1f;
            DrawRoundedColor(colorRect, lightColor);
            GUI.Label(colorRect, _lightColorIndicatorContent);
        }

        // Draws Unity's animation icon for dynamic lights and volumes.
        private static void DrawDynamicIndicator(Rect rect, bool isDynamic) {
            if (!isDynamic) return;
            if (_dynamicIndicatorContent == null) _dynamicIndicatorContent = new GUIContent(GetThemedUnityIcon("AnimationClip On Icon").image, "Dynamic");
            GUI.Label(rect, _dynamicIndicatorContent);
        }

        // Draws a borderless circular light-color swatch.
        private static void DrawRoundedColor(Rect rect, Color color) {
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, rect.width * 0.5f);
        }

        // Returns the Unity shadow icon and tooltip matching the light's bake state.
        private static GUIContent GetShadowIndicatorContent(bool baked, bool bakeInGame) {
            if (_bakedShadowIndicatorContent == null) {
                Texture icon = GetThemedUnityIcon("Shadow Icon").image;
                _bakedShadowIndicatorContent = new GUIContent(icon, "Shadows are enabled and baked");
                _pendingShadowIndicatorContent = new GUIContent(icon, "Shadows are enabled but not baked");
                _runtimeShadowIndicatorContent = new GUIContent(icon, "Shadows are enabled and will be baked at runtime");
            }
            if (baked) return _bakedShadowIndicatorContent;
            return bakeInGame ? _runtimeShadowIndicatorContent : _pendingShadowIndicatorContent;
        }

        // Returns the cached Unity icon for a registry entry's volume or light type.
        private static GUIContent GetRegistryIcon(UnityEngine.Object value, bool pointLights) {
            if (!pointLights && value is LightVolumeInstance volume) {
                if (volume.IsAdditive) return GetRegistryIconContent(ref _additiveLightVolumeIconContent, "LightProbes Icon", "Additive Light Volume");
                return GetRegistryIconContent(ref _regularLightVolumeIconContent, "PreMatLight1@2x", "Regular Light Volume");
            }
            if (value is PointLightVolumeInstance pointLight) {
                if (pointLight.LightType == 1) return GetRegistryIconContent(ref _spotLightIconContent, "Spotlight Icon", "Spot Light");
                if (pointLight.LightType == 2) return GetRegistryIconContent(ref _areaLightIconContent, "AreaLight Icon", "Area Light");
                return GetRegistryIconContent(ref _pointLightIconContent, "Light Icon", "Point Light");
            }
            return GetThemedUnityIcon("Light Icon");
        }

        // Lazily creates reusable icon content with a descriptive tooltip.
        private static GUIContent GetRegistryIconContent(ref GUIContent content, string iconName, string tooltip) {
            if (content == null) content = new GUIContent(GetThemedUnityIcon(iconName).image, tooltip);
            return content;
        }

        // Returns the Unity icon variant matching the active Editor theme.
        private static GUIContent GetThemedUnityIcon(string iconName) {
            return EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? $"d_{iconName}" : iconName);
        }

        // Draws shared projection, shadow and culling settings for Point Light Volumes.
        private void DrawPointLightSettings() {
            DrawIntPopup("Cookie Resolution", "CustomTexturesWidth", TextureResolutionLabels, TextureResolutions);
            DrawIntPopup("Shadow Resolution", "ShadowTexturesWidth", TextureResolutionLabels, TextureResolutions);
            DrawSlider("ShadowBleedReduction", "Shadow Bleed Reduction", 0f, 1f);
            string varianceName = LightVolumeManagerEditorBackend.IsMobileBuildTarget() ? "ShadowMinVarianceMobile" : "ShadowMinVarianceDesktop";
            DrawSlider(varianceName, "Shadow Min Variance", 0f, 1f);
            DrawSlider("LightsBrightnessCutoff", "Brightness Cutoff", 0.05f, 1f);
        }

        // Draws froxel clustering controls and live Scene View grid estimates.
        private void DrawClusteringSettings() {
            SerializedProperty clustering = serializedObject.FindProperty("Clustering");
            EditorGUILayout.PropertyField(clustering, new GUIContent("Clustering Enabled", clustering.tooltip));
            if (!clustering.boolValue) return;
            DrawProperty("ClusteringMinLights", "Min Lights Count");
            DrawProperty("FroxelDensity", "Angular Density");
            DrawProperty("FroxelSlices", "Slices Count");
            DrawIntPopup("Coarse Reduction", "FroxelCoarse", CoarseLabels, CoarseValues);
            SerializedProperty shadowCulling = serializedObject.FindProperty("ShadowCulling");
            EditorGUILayout.PropertyField(shadowCulling, new GUIContent("Shadow Culling", shadowCulling.tooltip));

            GUILayout.Space(8f);
            if (!_manager.FroxelLayoutValidPreview) {
                GUILayout.Label(new GUIContent("Froxel Layout: <b>waiting for a camera render</b>", "The grid and its packed mask textures are shown after clustering has been calculated for a camera."), RichLabelStyle);
                return;
            }

            Vector4 fineGrid = _manager.FineFroxelGridParamsPreview;
            Vector4 coarseGrid = _manager.CoarseFroxelGridParamsPreview;
            int columns = Mathf.RoundToInt(fineGrid.x);
            int slices = Mathf.RoundToInt(fineGrid.y);
            int rows = Mathf.RoundToInt(fineGrid.z);
            int coarseColumns = Mathf.RoundToInt(coarseGrid.x);
            int coarseSlices = Mathf.RoundToInt(coarseGrid.y);
            int coarseRows = Mathf.RoundToInt(coarseGrid.z);
            GUILayout.Label(
                new GUIContent(
                    $"Fine Froxels: <b>{columns} x {rows} x {slices} ({(long)columns * rows * slices:N0} froxels)</b>",
                    "The detailed camera grid currently used by shaders. The texture resolution is the actual packed Fine mask atlas written by the clustering blit."),
                RichLabelStyle);
            GUILayout.Label(
                new GUIContent(
                    $"Coarse Froxels: <b>{coarseColumns} x {coarseRows} x {coarseSlices} ({(long)coarseColumns * coarseRows * coarseSlices:N0} froxels)</b>",
                    "The helper grid currently used to reject unrelated lights. The texture resolution is the actual packed Coarse mask atlas written by the clustering blit."),
                RichLabelStyle);
        }

        // Draws lightmapper-specific bake and atlas settings.
        private void DrawBakingSettings() {
            DrawIntPopup("Baking Mode", "BakingMode", BakingLabels, BakingValues);
            int mode = serializedObject.FindProperty("BakingMode").intValue;
            bool isProgressive = mode == 0;
            bool isBakery = mode == 1;

            if (isProgressive) {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Denoise"));
                SerializedProperty dilate = serializedObject.FindProperty("DilateInvalidProbes");
                EditorGUILayout.PropertyField(dilate);
                if (dilate.boolValue) {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("DilationIterations"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("DilationBackfaceBias"));
                }
            }

            if (isBakery) {
                if (BakeryEditorBridge.IsAvailable) {
                    if (!BakeryEditorBridge.SupportsFullRenderLifecycle) {
                        EditorGUILayout.HelpBox("This Bakery version does not expose the full render lifecycle required for automatic Light Volume import and atlas finalization. Update Bakery to enable it.", MessageType.Warning);
                    }
                    using (new EditorGUI.DisabledScope(!BakeryEditorBridge.SupportsRuntimeBitmasks)) {
                        DrawMask("Volume Bitmask", "VolumeBitmask");
                        DrawMask("Probe Bitmask", "ProbeBitmask");
                    }
                    if (!BakeryEditorBridge.SupportsRuntimeBitmasks) {
                        EditorGUILayout.HelpBox("This Bakery version does not expose compatible implicit-group bitmasks. Bitmask overrides are disabled.", MessageType.Warning);
                    }
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("FixLightProbesL1"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Denoise"));
                } else {
                    string message = BakeryEditorBridge.IsInstalled
                        ? "The installed Bakery API is incomplete or incompatible with VRC Light Volumes. Update Bakery to use Bakery mode."
                        : "Bakery mode requires the Bakery asset.";
                    EditorGUILayout.HelpBox(message, MessageType.Error);
                }
            }
            DrawIntPopup("Downscale Volumes", "DownscaleVolumes", DownscaleLabels, DownscaleValues);
            DrawProperty("LightVolumeAtlas", "Light Volume Atlas");
        }

        // Draws runtime update, blending and overdraw settings relevant to current registries.
        private void DrawVisualSettings(bool hasLightVolumes, bool hasPointLights) {
            if (hasLightVolumes) {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("LightProbesBlending"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("SharpBounds"));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AutoUpdateVolumes"));
            if (hasPointLights) EditorGUILayout.PropertyField(serializedObject.FindProperty("AutoUpdateTextures"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AdditiveMaxOverdraw"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ForceSceneLighting"));
        }

        // Draws atlas packing and batch shadow baking actions when applicable.
        private void DrawActions(bool hasLightVolumes, bool hasPointLights) {
            GUILayout.Space(InspectorSectionSpacing);
            using (new EditorGUILayout.HorizontalScope()) {
                if (hasLightVolumes) {
                    if (GUILayout.Button(new GUIContent("Pack Light Volumes", "Rebuilds the Light Volume 3D atlas."))) LightVolumeManagerEditorBackend.GenerateAtlas(_manager);
                }
                if (hasPointLights) {
                    using (new EditorGUI.DisabledScope(!_canBatchBakeShadows)) {
                        if (GUILayout.Button(new GUIContent("Bake Shadows", "Bakes every shadow-enabled light with Rebake Shadows enabled."))) LightVolumeManagerEditorBackend.BakeShadowMaps(_manager);
                    }
                }
            }
        }

        // Draws read-only texture, clustering, count and runtime material diagnostics.
        private void DrawDebugSection() {
            GUILayout.Space(InspectorSectionSpacing);
            EditorGUI.BeginChangeCheck();
            _debugExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(_debugExpanded, new GUIContent("Debug", "Shows read-only live Manager data for troubleshooting."));
            if (EditorGUI.EndChangeCheck()) SessionState.SetBool(DebugFoldoutSessionKey, _debugExpanded);

            if (_debugExpanded) {
                if (!EditorApplication.isPlaying) EditorGUILayout.HelpBox("Live values are populated in Play Mode. Runtime texture arrays are rebuilt on initialization and are not stored in the build.", MessageType.Info);

                LightVolumeDebugGUI.DrawGroupHeader("Runtime Texture Arrays", false, "Live texture arrays rebuilt by the Manager and sampled by shaders.");
                LightVolumeDebugGUI.DrawObject(serializedObject, nameof(LightVolumeManager.CustomTextures), _manager.CustomTextures, typeof(RenderTexture), "Cookie Array");
                LightVolumeDebugGUI.DrawInt("Cookie Slices", GetTextureDepth(_manager.CustomTextures), "Number of allocated array slices. Each cubemap uses six slices.");
                LightVolumeDebugGUI.DrawInt(serializedObject, nameof(LightVolumeManager.CubemapsCount), _manager.CubemapsCount, "Cookie Cubemaps");
                LightVolumeDebugGUI.DrawBool("Dynamic Cookie Sources", _manager.HasAutoCustomTextureUpdates, "Whether any cookie source must be copied again at runtime.");

                GUILayout.Space(EditorGUIUtility.standardVerticalSpacing);
                LightVolumeDebugGUI.DrawObject(serializedObject, nameof(LightVolumeManager.ShadowTextures), _manager.ShadowTextures, typeof(RenderTexture), "Shadow Array");
                LightVolumeDebugGUI.DrawInt("Shadow Slices", GetTextureDepth(_manager.ShadowTextures), "Number of allocated array slices. Each cubemap shadow uses six slices.");
                LightVolumeDebugGUI.DrawInt(serializedObject, nameof(LightVolumeManager.ShadowMapsCount), _manager.ShadowMapsCount, "Shadow Maps");
                LightVolumeDebugGUI.DrawInt(serializedObject, nameof(LightVolumeManager.ShadowCubemapsCount), _manager.ShadowCubemapsCount, "Shadow Cubemaps");
                LightVolumeDebugGUI.DrawBool("Dynamic Shadow Sources", _manager.HasAutoShadowTextureUpdates, "Whether any shadow source must be copied again at runtime.");

                LightVolumeDebugGUI.DrawGroupHeader("Froxel Clustering", true, "Live clustering textures and the current clustering state.");
                LightVolumeDebugGUI.DrawObject("Fine Cluster Mask", _manager.FineClusterMaskPreview, typeof(RenderTexture), "The detailed clustered-light mask currently sampled by shaders.");
                LightVolumeDebugGUI.DrawText("Fine Resolution", GetTextureResolution(_manager.FineClusterMaskPreview), "Actual resolution of the packed Fine mask atlas written by the clustering blit.");
                LightVolumeDebugGUI.DrawObject("Coarse Cluster Mask", _manager.CoarseClusterMaskPreview, typeof(RenderTexture), "The lower-resolution mask used to reject unrelated lights before building the Fine mask.");
                LightVolumeDebugGUI.DrawText("Coarse Resolution", GetTextureResolution(_manager.CoarseClusterMaskPreview), "Actual resolution of the packed Coarse mask atlas written by the clustering blit.");
                LightVolumeDebugGUI.DrawText("Clustering Status", GetClusteringStatus(), "Current runtime state of froxel clustering.");
                DrawShadowCullPyramidDebug();

                LightVolumeDebugGUI.DrawGroupHeader("Runtime State", true, "Live initialization state and the counts currently uploaded by the Manager.");
                LightVolumeDebugGUI.DrawBool("Runtime Initialized", _manager.RuntimeInitializedPreview, "Whether the Manager has completed runtime initialization.");
                LightVolumeDebugGUI.DrawInt("Active Light Volumes", _manager.EnabledCount, "Light Volumes currently uploaded to shaders.");
                LightVolumeDebugGUI.DrawInt("Active Point Lights", _manager.ActivePointLightCountPreview, "Point Light Volumes currently uploaded to shaders.");
                LightVolumeDebugGUI.DrawInt("Active Shadows", _manager.ActiveShadowCountPreview, "Uploaded Point Light Volumes that currently use a valid shadow map.");
                LightVolumeDebugGUI.DrawInt("Shadow-Cull Eligible Lights", _manager.ActiveShadowCullCountPreview, "Full-strength shadowed Point Light Volumes eligible for conservative Hi-Z removal.");

                LightVolumeDebugGUI.DrawGroupHeader("Runtime Materials", true, "Materials used internally by runtime texture and clustering passes.");
                LightVolumeDebugGUI.DrawObject("Cookie Copy Material", _manager.CubemapFaceMaterial, typeof(Material), "Copies cubemap faces into the runtime cookie array.");
                LightVolumeDebugGUI.DrawObject("Shadow Depth Material", _manager.RuntimeShadowDepthEncodeMaterial, typeof(Material), "Encodes shadow-camera depth into runtime shadow textures.");
                LightVolumeDebugGUI.DrawObject("Shadow Blur Material", _manager.RuntimeShadowBlurMaterial, typeof(Material), "Filters runtime shadow textures.");
                LightVolumeDebugGUI.DrawObject("Clustering Material", _manager.ClusteringMaterialPreview, typeof(Material), "Builds the Fine and Coarse froxel masks.");
                LightVolumeDebugGUI.DrawObject("Shadow Culling Material", _manager.ShadowCullingMaterialPreview, typeof(Material), "Builds and packs the persistent critical-depth hierarchy.");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // Exposes the single persistent hierarchy resource. Per-level build textures are temporary
        // and have already been released by the time the Inspector can repaint.
        private void DrawShadowCullPyramidDebug() {
            LightVolumeDebugGUI.DrawGroupHeader("Shadow Cull Hi-Z", true, "The cached critical-depth hierarchy used to reject fully shadowed froxels.");
            LightVolumeDebugGUI.DrawText("Hi-Z Status", GetShadowCullPyramidStatus(), "Only a Ready hierarchy is sampled by the clustering shader.");
            RenderTexture hierarchy = _manager.ShadowCullPyramidPreview;
            LightVolumeDebugGUI.DrawInt("Active Levels", _manager.ShadowCullPyramidValidPreview ? _manager.ShadowCullPyramidLevelCountPreview : 0,
                "Number of exact max-reduction levels packed into the hierarchy.");
            LightVolumeDebugGUI.DrawInt("Finest Level Resolution", _manager.ShadowCullPyramidValidPreview ? _manager.ShadowCullPyramidFinestResolutionPreview : 0,
                "Resolution of the most detailed retained Hi-Z level for each shadow-map face.");
            LightVolumeDebugGUI.DrawInt("Shadow Slices", _manager.ShadowCullPyramidValidPreview ? _manager.ShadowCullPyramidSliceCountPreview : 0,
                "Number of spot-map and cubemap-face slices represented by the packed hierarchy.");

            if (hierarchy == null) {
                LightVolumeDebugGUI.DrawText("Packed Hierarchy", "Not Allocated", "The hierarchy is created lazily after shadow-assisted clustering first runs.");
                return;
            }
            if (!_manager.ShadowCullPyramidValidPreview) {
                EditorGUILayout.HelpBox("This cached texture is stale and is not currently sampled. The Hi-Z Status above explains why.", MessageType.Info);
            }
            string allocationState = hierarchy.IsCreated() ? string.Empty : " (Released)";
            LightVolumeDebugGUI.DrawObject("Packed Hierarchy" + allocationState, hierarchy, typeof(RenderTexture),
                "The complete point-filtered RFloat hierarchy used by froxel clustering.");
            LightVolumeDebugGUI.DrawText("Storage Resolution", GetTextureResolution(hierarchy),
                "Physical dimensions of the single persistent packed texture.");
        }

        // Converts the Manager's clustering flags into one inspector status label.
        private string GetClusteringStatus() {
            if (!_manager.Clustering) return "Disabled";
            if (_manager.ClusteringUnsupportedPreview) return "Unsupported";
            if (_manager.ClusteringAllocationFailedPreview) return "Allocation Failed";
            if (!_manager.ClusteringActivePreview) return "Inactive";
            return _manager.ClusterMaskValidPreview ? "Active" : "Building";
        }

        // Distinguishes a usable hierarchy from cached allocations that are deliberately ignored.
        private string GetShadowCullPyramidStatus() {
            if (!_manager.Clustering || !_manager.ShadowCulling) return "Disabled";
            if (_manager.ShadowCullPyramidSuspendedPreview) return "Suspended (Auto-updating Shadows)";
            if (_manager.ShadowCullPyramidUnsupportedPreview) return "Unsupported";
            if (_manager.ShadowCullPyramidAllocationFailedPreview) return "Allocation Failed";
            if (_manager.ShadowCullPyramidValidPreview) return "Ready";
            return _manager.ShadowCullPyramidDirtyPreview ? "Waiting For Build" : "Unavailable";
        }

        // Returns the allocated slice count of a runtime texture array.
        private static int GetTextureDepth(RenderTexture texture) {
            return texture != null ? Mathf.Max(texture.volumeDepth, 1) : 0;
        }

        // Formats the live two-dimensional allocation size of a runtime texture.
        private static string GetTextureResolution(RenderTexture texture) {
            return texture != null ? $"{texture.width} x {texture.height}" : "Unavailable";
        }

        // Draws a bold inspector section title with optional leading spacing.
        private static void DrawSectionHeader(string title, bool addTopSpacing) {
            if (addTopSpacing) GUILayout.Space(InspectorSectionSpacing);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        // Draws a registry in a bounded viewport with a custom header and smooth scrolling.
        private void DrawScrollableList(ReorderableList list, RegistryScrollState scroll, SerializedProperty source, bool pointLights) {
            list.draggable = !Application.isPlaying;
            float contentHeight = Mathf.Max(list.GetHeight(), list.elementHeight + RegistryScrollPadding * 2f);
            float maxViewportHeight = (list.elementHeight + 2f) * VisibleRegistryRows + RegistryScrollPadding * 2f;
            float viewportHeight = Mathf.Min(contentHeight, maxViewportHeight);
            Rect area = GUILayoutUtility.GetRect(0f, RegistryHeaderHeight + viewportHeight + 1f, GUILayout.ExpandWidth(true));
            Rect headerRect = new Rect(area.x, area.y, area.width, RegistryHeaderHeight);
            Rect bodyRect = new Rect(area.x, headerRect.yMax - 1f, area.width, area.yMax - headerRect.yMax + 1f);
            Rect viewportRect = new Rect(area.x + 1f, headerRect.yMax, area.width - 2f, viewportHeight);
            float maxScrollY = Mathf.Max(0f, contentHeight - viewportHeight);
            bool showScrollbar = maxScrollY > 0.5f;
            float scrollbarWidth = showScrollbar ? Mathf.Max(16f, GUI.skin.verticalScrollbar.fixedWidth) : 0f;
            float headerRightInset = 1f + ReorderableList.Defaults.padding + (showScrollbar ? RegistryScrollbarRightInset + scrollbarWidth : 0f);

            if (Event.current.type == EventType.Repaint) {
                ReorderableList.defaultBehaviours.boxBackground.Draw(bodyRect, false, false, false, false);
                ReorderableList.defaultBehaviours.headerBackground.Draw(headerRect, false, false, false, false);
            }
            DrawRegistryHeader(headerRect, source, pointLights, headerRightInset);

            HandleSmoothRegistryScroll(viewportRect, list, scroll, maxScrollY);

            if (showScrollbar) viewportRect.width = Mathf.Max(1f, viewportRect.width - RegistryScrollbarRightInset);
            Rect contentRect = new Rect(0f, 0f, Mathf.Max(1f, viewportRect.width - scrollbarWidth), contentHeight);
            Vector2 previousPosition = scroll.Position;
            scroll.Position = GUI.BeginScrollView(viewportRect, scroll.Position, contentRect, false, showScrollbar);
            list.DoList(new Rect(0f, 0f, contentRect.width, contentHeight));
            GUI.EndScrollView();

            scroll.Position.x = 0f;
            scroll.Position.y = Mathf.Clamp(scroll.Position.y, 0f, maxScrollY);
            if (!Mathf.Approximately(previousPosition.y, scroll.Position.y) && Event.current.type != EventType.Repaint) {
                scroll.TargetY = scroll.Position.y;
                scroll.Velocity = 0f;
            }
        }

        // Updates wheel and drag-edge scrolling with clamped smooth motion.
        private void HandleSmoothRegistryScroll(Rect viewport, ReorderableList list, RegistryScrollState scroll, float maxScrollY) {
            scroll.TargetY = Mathf.Clamp(scroll.TargetY, 0f, maxScrollY);
            scroll.Position.y = Mathf.Clamp(scroll.Position.y, 0f, maxScrollY);

            Event current = Event.current;
            if (maxScrollY > 0f && current.type == EventType.ScrollWheel && viewport.Contains(current.mousePosition)) {
                float target = Mathf.Clamp(scroll.TargetY + current.delta.y * RegistryWheelStep, 0f, maxScrollY);
                if (!Mathf.Approximately(target, scroll.TargetY)) {
                    scroll.TargetY = target;
                    current.Use();
                    Repaint();
                }
            }

            if (current.type != EventType.Repaint) return;
            double now = EditorApplication.timeSinceStartup;
            float deltaTime = scroll.LastRepaintTime > 0d ? Mathf.Min((float)(now - scroll.LastRepaintTime), 0.05f) : 1f / 60f;
            scroll.LastRepaintTime = now;
            if (GUIUtility.hotControl != 0 && list.index >= 0 && current.mousePosition.x >= viewport.x && current.mousePosition.x <= viewport.x + 36f) {
                float direction = 0f;
                if (current.mousePosition.y >= viewport.y && current.mousePosition.y < viewport.y + RegistryDragScrollEdge) direction = -1f;
                else if (current.mousePosition.y <= viewport.yMax && current.mousePosition.y > viewport.yMax - RegistryDragScrollEdge) direction = 1f;
                if (direction != 0f) scroll.TargetY = Mathf.Clamp(scroll.TargetY + direction * RegistryDragScrollSpeed * deltaTime, 0f, maxScrollY);

            }
            if (Mathf.Abs(scroll.Position.y - scroll.TargetY) < 0.05f && Mathf.Abs(scroll.Velocity) < 0.05f) {
                scroll.Position.y = scroll.TargetY;
                scroll.Velocity = 0f;
                return;
            }
            scroll.Position.y = Mathf.SmoothDamp(scroll.Position.y, scroll.TargetY, ref scroll.Velocity, RegistryScrollSmoothTime, Mathf.Infinity, deltaTime);
            Repaint();
        }

        // Recalculates cached VRAM, bundle and shadow-bake statistics at a limited rate.
        private void RefreshStats() {
            double now = EditorApplication.timeSinceStartup;
            if (_cachedPointCount == _pointLights.arraySize && now < _nextStatsRefresh) return;
            _cachedPointCount = _pointLights.arraySize;
            _nextStatsRefresh = now + StatsRefreshInterval;

            ulong vram = 0;
            ulong bundle = 0;
            _textureDependencyRoots.Clear();
            _textureDependencyRootSet.Clear();
            _directTextureRoots.Clear();
            _countedVramTextures.Clear();
            _countedBundleTextures.Clear();
            _canBatchBakeShadows = false;
            PointLightVolumeInstance[] lights = _manager.PointLightVolumeInstances;
            BuildRuntimeShadowEstimateSets(lights);

            // The build preprocessor keeps only the final atlas. If it is a CustomRenderTexture,
            // dependency collection also finds the baked Texture3D feeding its material.
            Texture finalAtlas = _manager.LightVolumeAtlas != null ? _manager.LightVolumeAtlas : _manager.LightVolumeAtlasBase;
            AddTextureDependencyRoot(finalAtlas);
            for (int i = 0; i < lights.Length; i++) {
                PointLightVolumeInstance light = lights[i];
                if (light == null) continue;
                if (light.Shadows && light.RebakeShadows) _canBatchBakeShadows = true;

                UnityEngine.Object projectionSource = light.GetProjectionSource();
                if (projectionSource is Texture || projectionSource is Material) AddTextureDependencyRoot(projectionSource);

                // Bake In Game preview shadows are deliberately stripped from the build. Their
                // generated runtime arrays are estimated below instead of counting this editor asset.
                if (!light.Shadows || light.BakeInGame) continue;
                Texture shadowTexture = light.GetShadowMapTexture();
                Material shadowMaterial = light.GetShadowMapMaterial();
                if (shadowTexture != null) AddTextureDependencyRoot(shadowTexture);
                else if (shadowMaterial != null) AddTextureDependencyRoot(shadowMaterial);
            }

            AddDependencyTextureData(ref vram, ref bundle);

            ulong customArrayBytes = GetTextureGpuBytes(_manager.CustomTextures);
            ulong estimatedCustomArrayBytes = EstimateCustomTextureArrayBytes(lights);
            if (estimatedCustomArrayBytes > customArrayBytes) customArrayBytes = estimatedCustomArrayBytes;
            vram += customArrayBytes;

            int estimatedShadowSliceCount;
            ulong shadowArrayBytes = GetTextureGpuBytes(_manager.ShadowTextures);
            ulong estimatedShadowArrayBytes = EstimateShadowTextureArrayBytes(lights, out estimatedShadowSliceCount);
            if (estimatedShadowArrayBytes > shadowArrayBytes) shadowArrayBytes = estimatedShadowArrayBytes;
            vram += shadowArrayBytes;

            ulong fineMaskBytes = GetTextureGpuBytes(_manager.FineClusterMaskPreview);
            ulong coarseMaskBytes = GetTextureGpuBytes(_manager.CoarseClusterMaskPreview);
            EstimateFroxelMaskBytes(out ulong estimatedFineMaskBytes, out ulong estimatedCoarseMaskBytes);
            if (estimatedFineMaskBytes > fineMaskBytes) fineMaskBytes = estimatedFineMaskBytes;
            if (estimatedCoarseMaskBytes > coarseMaskBytes) coarseMaskBytes = estimatedCoarseMaskBytes;
            vram += fineMaskBytes + coarseMaskBytes;

            ulong hiZBytes = GetTextureGpuBytes(_manager.ShadowCullPyramidPreview);
            ulong estimatedHiZBytes = EstimateShadowCullPyramidBytes(lights, estimatedShadowSliceCount);
            if (estimatedHiZBytes > hiZBytes) hiZBytes = estimatedHiZBytes;
            vram += hiZBytes;

            foreach (PointLightVolumeInstance target in _runtimeShadowSourceTargets) {
                if (target != null) vram += GetRuntimeShadowOutputBytes(target);
            }

            foreach (PointLightVolumeInstance target in _retainedShadowScratchTargets) {
                if (target != null) vram += GetRuntimeShadowScratchBytes(target);
            }

            ulong oneShotScratchPeak = 0;
            foreach (PointLightVolumeInstance target in _oneShotShadowScratchTargets) {
                if (target == null || _retainedShadowScratchTargets.Contains(target)) continue;
                ulong scratchBytes = GetRuntimeShadowScratchBytes(target);
                if (scratchBytes > oneShotScratchPeak) oneShotScratchPeak = scratchBytes;
            }
            vram += oneShotScratchPeak;

            _cachedVramBytes = vram;
            _cachedBundleBytes = bundle;
        }

        // Collects all texture assets reachable from the exact sources retained by the temporary
        // build scene. One dependency walk catches textures hidden behind projection materials.
        private void AddDependencyTextureData(ref ulong vram, ref ulong bundle) {
            if (_textureDependencyRoots.Count == 0) return;
            UnityEngine.Object[] dependencies = EditorUtility.CollectDependencies(_textureDependencyRoots.ToArray());
            for (int i = 0; i < dependencies.Length; i++) {
                Texture texture = dependencies[i] as Texture;
                if (texture == null) continue;
                bool projectAsset = AssetDatabase.Contains(texture);
                if (!projectAsset && !_directTextureRoots.Contains(texture)) continue;

                if (_countedVramTextures.Add(texture)) vram += GetTextureGpuBytes(texture);
                // RenderTexture assets serialize a descriptor, not their runtime pixel allocation.
                // Any persistent texture feeding a CustomRenderTexture is a separate dependency.
                if (projectAsset && !(texture is RenderTexture) && _countedBundleTextures.Add(texture))
                    bundle += EstimateCompressedBundleBytes(texture);
            }
        }

        // Adds one build-retained texture or material without allocating duplicate dependency roots.
        private void AddTextureDependencyRoot(UnityEngine.Object root) {
            if (root == null || !_textureDependencyRootSet.Add(root)) return;
            _textureDependencyRoots.Add(root);
            if (root is Texture texture) _directTextureRoots.Add(texture);
        }

        // Resolves which point lights own persistent generated outputs, direct atlas ranges and
        // retained realtime bake scratch. Bake In Game and one-shot bakers share one peak buffer.
        private void BuildRuntimeShadowEstimateSets(PointLightVolumeInstance[] lights) {
            _runtimeShadowSourceTargets.Clear();
            _runtimeShadowDirectTargets.Clear();
            _retainedShadowScratchTargets.Clear();
            _oneShotShadowScratchTargets.Clear();

            for (int i = 0; i < lights.Length; i++) {
                PointLightVolumeInstance light = lights[i];
                if (light == null || !light.Shadows || !light.BakeInGame) continue;
                _runtimeShadowSourceTargets.Add(light);
                _oneShotShadowScratchTargets.Add(light);
            }

            PointLightShadowRuntimeBaker[] bakers = UnityEngine.Object.FindObjectsOfType<PointLightShadowRuntimeBaker>(true);
            for (int i = 0; i < bakers.Length; i++) {
                PointLightShadowRuntimeBaker baker = bakers[i];
                if (baker == null || baker.gameObject.scene != _manager.gameObject.scene) continue;
                PointLightVolumeInstance target = baker.TargetPointLightVolume;
                if (target == null || target.LightVolumeManager != _manager) continue;

                if (baker.Realtime) {
                    _retainedShadowScratchTargets.Add(target);
                    if (RuntimeShadowResolutionMatchesManager(target)) _runtimeShadowDirectTargets.Add(target);
                    else _runtimeShadowSourceTargets.Add(target);
                } else if (baker.BakeOnEnable) {
                    _runtimeShadowSourceTargets.Add(target);
                    _oneShotShadowScratchTargets.Add(target);
                }
            }

            // A normal generated source is authoritative if two configured systems target the same
            // light; counting an additional direct range would overestimate the final atlas layout.
            foreach (PointLightVolumeInstance target in _runtimeShadowSourceTargets)
                _runtimeShadowDirectTargets.Remove(target);
        }

        // Predicts the final RGBAHalf cookie/LUT array, including the full mip chain needed for
        // Area Light average-color readback and the ABI's reserved ID-zero slice for Point LUTs.
        private ulong EstimateCustomTextureArrayBytes(PointLightVolumeInstance[] lights) {
            _cubemapTextureSources.Clear();
            _cubemapMaterialSources.Clear();
            _singleTextureSources.Clear();
            _singleMaterialSources.Clear();
            Texture firstSingleTexture = null;
            Material firstSingleMaterial = null;
            bool firstTextureUsedByPointLut = false;
            bool firstMaterialUsedByPointLut = false;
            bool useMipMap = false;

            for (int i = 0; i < lights.Length; i++) {
                PointLightVolumeInstance light = lights[i];
                if (light == null || !light.IsActive) continue;
                UnityEngine.Object source = light.GetProjectionSource();
                if (!(source is Texture) && !(source is Material)) continue;

                int projectionMode = light.LightType == 2 ? 2 : light.Projection;
                if (projectionMode != 1 && projectionMode != 2) continue;
                bool cubemap = light.LightType == 0 && projectionMode == 2;
                bool pointLut = light.LightType == 0 && projectionMode == 1;
                if (light.LightType == 2) useMipMap = true;

                if (source is Texture texture) {
                    if (cubemap) _cubemapTextureSources.Add(texture);
                    else {
                        if (_singleTextureSources.Add(texture) && firstSingleTexture == null) firstSingleTexture = texture;
                        if (pointLut && texture == firstSingleTexture) firstTextureUsedByPointLut = true;
                    }
                } else {
                    Material material = (Material)source;
                    if (cubemap) _cubemapMaterialSources.Add(material);
                    else {
                        if (_singleMaterialSources.Add(material) && firstSingleMaterial == null) firstSingleMaterial = material;
                        if (pointLut && material == firstSingleMaterial) firstMaterialUsedByPointLut = true;
                    }
                }
            }

            int cubemapCount = _cubemapTextureSources.Count + _cubemapMaterialSources.Count;
            int reservedSlice = cubemapCount == 0 && (firstTextureUsedByPointLut
                || _singleTextureSources.Count == 0 && firstMaterialUsedByPointLut) ? 1 : 0;
            int sliceCount = cubemapCount * 6 + reservedSlice + _singleTextureSources.Count + _singleMaterialSources.Count;
            if (sliceCount <= 0) return 0;
            int mipCount = useMipMap ? GetFullMipCount(_manager.CustomTexturesWidth, _manager.CustomTexturesHeight, 1, false) : 1;
            return GetKnownFormatTextureBytes(_manager.CustomTexturesWidth, _manager.CustomTexturesHeight,
                sliceCount, 8UL, mipCount, false);
        }

        // Predicts the deduplicated final EVSM texture-array layout after all configured runtime
        // bakes have published. The returned slice count also drives the lazy Hi-Z estimate.
        private ulong EstimateShadowTextureArrayBytes(PointLightVolumeInstance[] lights, out int sliceCount) {
            _cubemapTextureSources.Clear();
            _cubemapMaterialSources.Clear();
            _singleTextureSources.Clear();
            _singleMaterialSources.Clear();
            int generatedCubemaps = 0;
            int generatedSingles = 0;
            int directCubemaps = 0;
            int directSingles = 0;

            for (int i = 0; i < lights.Length; i++) {
                PointLightVolumeInstance light = lights[i];
                if (light == null || !light.IsActive || !light.Shadows) continue;
                bool cubemap = light.ShouldBakeCubemapShadows();
                if (_runtimeShadowSourceTargets.Contains(light)) {
                    if (cubemap) generatedCubemaps++;
                    else generatedSingles++;
                    continue;
                }
                if (_runtimeShadowDirectTargets.Contains(light)) {
                    if (cubemap) directCubemaps++;
                    else directSingles++;
                    continue;
                }

                Texture texture = light.GetShadowMapTexture();
                Material material = light.GetShadowMapMaterial();
                if (texture != null) {
                    if (cubemap) _cubemapTextureSources.Add(texture);
                    else _singleTextureSources.Add(texture);
                } else if (material != null) {
                    if (cubemap) _cubemapMaterialSources.Add(material);
                    else _singleMaterialSources.Add(material);
                }
            }

            int cubemapCount = _cubemapTextureSources.Count + _cubemapMaterialSources.Count
                + generatedCubemaps + directCubemaps;
            sliceCount = cubemapCount * 6 + _singleTextureSources.Count + _singleMaterialSources.Count
                + generatedSingles + directSingles;
            if (sliceCount <= 0) return 0;
            ulong bytesPerPixel = _manager.ShadowTextureFormat == 0 ? 8UL : 16UL;
            return GetKnownFormatTextureBytes(_manager.ShadowTexturesWidth, _manager.ShadowTexturesHeight,
                sliceCount, bytesPerPixel, 1, false);
        }

        // Uses the live packed grid descriptor to account for lazily allocated RGBA32I masks even
        // if an Inspector repaint lands between layout calculation and RenderTexture creation.
        private void EstimateFroxelMaskBytes(out ulong fineBytes, out ulong coarseBytes) {
            fineBytes = 0;
            coarseBytes = 0;
            if (!_manager.Clustering || !_manager.FroxelLayoutValidPreview) return;
            fineBytes = GetPackedFroxelMaskBytes(_manager.FineFroxelGridParamsPreview);
            coarseBytes = GetPackedFroxelMaskBytes(_manager.CoarseFroxelGridParamsPreview);
        }

        private static ulong GetPackedFroxelMaskBytes(Vector4 grid) {
            int columns = Mathf.Max(Mathf.RoundToInt(grid.x), 1);
            int depthSlices = Mathf.Max(Mathf.RoundToInt(grid.y), 1);
            int rows = Mathf.Max(Mathf.RoundToInt(grid.z), 1);
            int tileColumns = 1 << Mathf.Clamp(Mathf.RoundToInt(grid.w), 0, 12);
            int tileRows = (rows + tileColumns - 1) / tileColumns;
            return (ulong)columns * (ulong)tileColumns * (ulong)depthSlices * (ulong)tileRows * 16UL;
        }

        // Asks the production packer for the physical descriptor so this estimate automatically
        // follows the 128-per-face precision cap and 4K packed-atlas fallback.
        private ulong EstimateShadowCullPyramidBytes(PointLightVolumeInstance[] lights, int shadowSliceCount) {
            if (!_manager.Clustering || !_manager.ShadowCulling || shadowSliceCount <= 0) return 0;
            if (_manager.AutoUpdateTextures && _manager.HasAutoShadowTextureUpdates) return 0;
            bool hasEligibleLight = false;
            for (int i = 0; i < lights.Length; i++) {
                PointLightVolumeInstance light = lights[i];
                if (light == null || !light.IsActive || !light.Shadows || light.ShadingStrength < 1f
                        || light.SquaredRange <= 0f) continue;
                bool hasPlannedRuntimeShadow = _runtimeShadowSourceTargets.Contains(light)
                    || _runtimeShadowDirectTargets.Contains(light);
                if (!hasPlannedRuntimeShadow && light.GetShadowMapTexture() == null
                        && light.GetShadowMapMaterial() == null) continue;
                hasEligibleLight = true;
                break;
            }
            if (!hasEligibleLight) return 0;
            if (!LightVolumeManager.TryGetShadowCullPyramidSizePreview(_manager.ShadowTexturesWidth,
                    shadowSliceCount, out int width, out int height)) return 0;
            return (ulong)width * (ulong)height * 4UL;
        }

        private bool RuntimeShadowResolutionMatchesManager(PointLightVolumeInstance light) {
            int resolution = PointLightShadowBaker.ResolveShadowBakeResolution(light, _manager);
            return resolution == _manager.ShadowTexturesWidth && resolution == _manager.ShadowTexturesHeight;
        }

        private ulong GetRuntimeShadowOutputBytes(PointLightVolumeInstance light) {
            int resolution = PointLightShadowBaker.ResolveShadowBakeResolution(light, _manager);
            int slices = light.ShouldBakeCubemapShadows() ? 6 : 1;
            ulong bytesPerPixel = _manager.ShadowTextureFormat == 0 ? 8UL : 16UL;
            return GetKnownFormatTextureBytes(resolution, resolution, slices, bytesPerPixel, 1, false);
        }

        private ulong GetRuntimeShadowScratchBytes(PointLightVolumeInstance light) {
            int resolution = PointLightShadowBaker.ResolveShadowBakeResolution(light, _manager);
            ulong depthBytes = (ulong)resolution * (ulong)resolution * 4UL;
            ulong blurBytes = light.Blur > 0.0001f ? GetRuntimeShadowOutputBytes(light) : 0UL;
            return depthBytes + blurBytes + 4UL; // Udon's one-pixel material-blit source.
        }

        // Calculates GPU texel storage from the texture's real GraphicsFormat, mip count and
        // dimension. This handles block-compressed projection assets without assuming RGBAHalf.
        private static ulong GetTextureGpuBytes(Texture texture) {
            if (texture == null) return 0;
            int width = Mathf.Max(texture.width, 1);
            int height = Mathf.Max(texture.height, 1);
            int mipCount = Mathf.Max(texture.mipmapCount, 1);
            int depthOrSlices = 1;
            bool volume = false;

            if (texture is Texture3D texture3D) {
                depthOrSlices = Mathf.Max(texture3D.depth, 1);
                volume = true;
            } else if (texture is Texture2DArray textureArray) {
                depthOrSlices = Mathf.Max(textureArray.depth, 1);
            } else if (texture is CubemapArray cubemapArray) {
                depthOrSlices = Mathf.Max(cubemapArray.cubemapCount, 1) * 6;
            } else if (texture is Cubemap) {
                depthOrSlices = 6;
            } else if (texture is RenderTexture renderTexture) {
                if (renderTexture.dimension == TextureDimension.Tex3D) {
                    depthOrSlices = Mathf.Max(renderTexture.volumeDepth, 1);
                    volume = true;
                } else if (renderTexture.dimension == TextureDimension.Tex2DArray) {
                    depthOrSlices = Mathf.Max(renderTexture.volumeDepth, 1);
                } else if (renderTexture.dimension == TextureDimension.Cube) {
                    depthOrSlices = 6;
                } else if (renderTexture.dimension == TextureDimension.CubeArray) {
                    depthOrSlices = Mathf.Max(renderTexture.volumeDepth, 1) * 6;
                }
            }

            ulong bytes = GetGraphicsFormatTextureBytes(width, height, depthOrSlices,
                texture.graphicsFormat, mipCount, volume);
            if (texture is RenderTexture target && target.depthStencilFormat != GraphicsFormat.None)
                bytes += GetGraphicsFormatTextureBytes(width, height, depthOrSlices,
                    target.depthStencilFormat, 1, volume);
            return bytes;
        }

        private static ulong GetGraphicsFormatTextureBytes(int width, int height, int depthOrSlices,
                GraphicsFormat format, int mipCount, bool volume) {
            if (format == GraphicsFormat.None) return 0;
            uint blockWidth = GraphicsFormatUtility.GetBlockWidth(format);
            uint blockHeight = GraphicsFormatUtility.GetBlockHeight(format);
            uint blockSize = GraphicsFormatUtility.GetBlockSize(format);
            if (blockWidth == 0 || blockHeight == 0 || blockSize == 0) return 0;

            ulong bytes = 0;
            int mipWidth = Mathf.Max(width, 1);
            int mipHeight = Mathf.Max(height, 1);
            int mipDepth = Mathf.Max(depthOrSlices, 1);
            for (int mip = 0; mip < Mathf.Max(mipCount, 1); mip++) {
                ulong blocksX = ((ulong)mipWidth + blockWidth - 1UL) / blockWidth;
                ulong blocksY = ((ulong)mipHeight + blockHeight - 1UL) / blockHeight;
                bytes += blocksX * blocksY * (ulong)mipDepth * blockSize;
                mipWidth = Mathf.Max(mipWidth >> 1, 1);
                mipHeight = Mathf.Max(mipHeight >> 1, 1);
                if (volume) mipDepth = Mathf.Max(mipDepth >> 1, 1);
            }
            return bytes;
        }

        private static ulong GetKnownFormatTextureBytes(int width, int height, int depthOrSlices,
                ulong bytesPerPixel, int mipCount, bool volume) {
            ulong bytes = 0;
            int mipWidth = Mathf.Max(width, 1);
            int mipHeight = Mathf.Max(height, 1);
            int mipDepth = Mathf.Max(depthOrSlices, 1);
            for (int mip = 0; mip < Mathf.Max(mipCount, 1); mip++) {
                bytes += (ulong)mipWidth * (ulong)mipHeight * (ulong)mipDepth * bytesPerPixel;
                mipWidth = Mathf.Max(mipWidth >> 1, 1);
                mipHeight = Mathf.Max(mipHeight >> 1, 1);
                if (volume) mipDepth = Mathf.Max(mipDepth >> 1, 1);
            }
            return bytes;
        }

        private static int GetFullMipCount(int width, int height, int depth, bool volume) {
            int mipCount = 1;
            width = Mathf.Max(width, 1);
            height = Mathf.Max(height, 1);
            depth = Mathf.Max(depth, 1);
            while (width > 1 || height > 1 || volume && depth > 1) {
                width = Mathf.Max(width >> 1, 1);
                height = Mathf.Max(height >> 1, 1);
                if (volume) depth = Mathf.Max(depth >> 1, 1);
                mipCount++;
            }
            return mipCount;
        }

        // Uncompressed payloads keep the established empirical bundle ratio. Already
        // block-compressed imports are not multiplied by that ratio a second time.
        private static ulong EstimateCompressedBundleBytes(Texture texture) {
            ulong bytes = GetTextureGpuBytes(texture);
            if (bytes == 0) return 0;
            if (texture.graphicsFormat != GraphicsFormat.None
                    && GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat)) return bytes;
            return (ulong)(bytes * BundleCompressionEstimate);
        }

        // Counts texels across supported 2D, array and cubemap shadow sources.
        private static ulong GetTextureTexels(Texture texture) {
            if (texture == null) return 0;
            if (texture is Texture2DArray array) return (ulong)array.width * (ulong)array.height * (ulong)array.depth;
            if (texture is Cubemap cubemap) return (ulong)cubemap.width * (ulong)cubemap.height * 6UL;
            if (texture is Texture2D texture2D) return (ulong)texture2D.width * (ulong)texture2D.height;
            return 0;
        }

        // Formats a byte count as megabytes with two decimal places.
        private static string FormatMegabytes(ulong bytes) {
            return (bytes / (double)(1024 * 1024)).ToString("0.00");
        }

        // Draws a named serialized property with its field-level tooltip.
        private void DrawProperty(string propertyName, string label) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip));
        }

        // Draws a clamped serialized slider with its field-level tooltip.
        private void DrawSlider(string propertyName, string label, float min, float max) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            EditorGUILayout.Slider(property, min, max, new GUIContent(label, property.tooltip));
        }

        // Draws an integer-backed popup while preserving serialized property help.
        private void DrawIntPopup(string label, string propertyName, string[] labels, int[] values) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Rect rect = EditorGUILayout.GetControlRect();
            Rect popupRect = EditorGUI.PrefixLabel(rect, new GUIContent(label, property.tooltip));
            property.intValue = EditorGUI.IntPopup(popupRect, property.intValue, labels, values);
        }

        // Draws a Bakery bitmask with named bit positions and field-level help.
        private void DrawMask(string label, string propertyName) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.intValue = EditorGUILayout.MaskField(new GUIContent(label, property.tooltip), property.intValue, BakeryMaskLabels);
        }

        // Detects duplicate Managers across all loaded scenes and resolves the primary one.
        private void RefreshManagerCount() {
            _primaryManager = LightVolumeManagerEditorBackend.GetPrimaryManager(out int count);
            _multipleManagers = count > 1;
        }

        private GUIStyle RichLabelStyle => _richLabelStyle ?? (_richLabelStyle = new GUIStyle(EditorStyles.label) { richText = true });

        // Keeps animated registries, statistics and live debug values visually current.
        public override bool RequiresConstantRepaint() {
            return _debugExpanded && EditorApplication.isPlaying;
        }
    }

}

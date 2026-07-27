using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
#if UDONSHARP
using UdonSharpEditor;
#endif

namespace VRCLightVolumes {
    [CustomEditor(typeof(LightVolumeManager))]
    public sealed class LightVolumeManagerEditor : Editor {
        private const string DebugFoldoutSessionKey = "VRCLightVolumes.LightVolumeManagerEditor.DebugFoldout";
        private const int VisibleRegistryRows = 12;
        private const float RegistryHeaderHeight = 20f;
        private const float RegistryScrollPadding = 2f;
        private const float RegistryScrollSmoothTime = 0.08f;
        private const float RegistryWheelStep = 18f;
        private const float RegistryDragScrollEdge = 24f;
        private const float RegistryDragScrollSpeed = 220f;
        private const float RegistryScrollbarRightInset = 2f;
        private const float InspectorSectionSpacing = 10f;
        private const double StatsRefreshInterval = 0.25d;
        private const double RuntimeDebugRefreshInterval = 0.2d;
        private const double BundleCompressionEstimate = 0.315d;
        private static readonly int[] TextureResolutions = { 16, 32, 64, 128, 256, 512, 1024, 2048 };
        private static readonly string[] TextureResolutionLabels = { "16 x 16", "32 x 32", "64 x 64", "128 x 128", "256 x 256", "512 x 512", "1024 x 1024", "2048 x 2048" };
        private static readonly int[] CoarseValues = { 2, 4, 8 };
        private static readonly string[] CoarseLabels = { "2x", "4x", "8x" };
        private static readonly int[] BakingValues = { 0, 1 };
        private static readonly string[] BakingLabels = { "Progressive", "Bakery" };
        private static readonly int[] DownscaleValues = { 0, 1, 2, 3 };
        private static readonly string[] DownscaleLabels = { "None", "2x", "4x", "8x" };
        private static readonly string[] BakeryMaskLabels = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30" };

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
        private bool _debugExpanded;
        private double _nextRuntimeDebugRefresh;
        private readonly HashSet<Texture> _countedShadowTextures = new HashSet<Texture>();

        private void OnEnable() {
            _manager = (LightVolumeManager)target;
            _debugExpanded = SessionState.GetBool(DebugFoldoutSessionKey, false);
            if (_manager != null && _manager.SanitizeRegistries()) {
                LightVolumeManagerTools.CopyProxyToUdon(_manager);
                LVUtils.MarkDirty(_manager);
            }
            serializedObject.Update();
            _lightVolumes = serializedObject.FindProperty("LightVolumeInstances");
            _pointLights = serializedObject.FindProperty("PointLightVolumeInstances");
            _lightVolumeList = CreateRegistryList(_lightVolumes, false);
            _pointLightList = CreateRegistryList(_pointLights, true);
            RefreshManagerCount();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnHierarchyChange() {
            RefreshManagerCount();
        }

        private void OnUndoRedoPerformed() {
            if (_manager == null) return;
            serializedObject.UpdateIfRequiredOrScript();
            LightVolumeManagerTools.ApplySettings(_manager, false);
#if BAKERY_INCLUDED
            LightVolumeBaker.QueueBakeryWatcherRefresh();
#endif
            _cachedPointCount = -1;
            _nextStatsRefresh = 0d;
            Repaint();
            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI() {
            RefreshRuntimeDebugProxy();
            serializedObject.Update();
            EditorGUILayout.Space(EditorGUIUtility.singleLineHeight * 0.5f);

            if (LVUtils.IsInPrefabAsset(_manager))
                EditorGUILayout.HelpBox("This component is part of a prefab asset. Edit the instance placed in a scene.", MessageType.Warning);
            if (_multipleManagers)
                EditorGUILayout.HelpBox("Multiple Light Volume Managers were found in this scene. Only one is supported; remove the extra Manager before building.", MessageType.Error);

            RefreshStats();
            GUILayout.Label($"Data size in VRAM: <b>{FormatMegabytes(_cachedVramBytes)} MB</b>", RichLabelStyle);
            GUILayout.Label($"Data size in bundle: <b>{FormatMegabytes((ulong)(_cachedBundleBytes * BundleCompressionEstimate))} MB (Approximately)</b>", RichLabelStyle);
            GUILayout.Space(8f);

            DrawScrollableList(_lightVolumeList, _lightVolumeScroll, _lightVolumes, false);
            GUILayout.Space(EditorGUIUtility.singleLineHeight);
            DrawScrollableList(_pointLightList, _pointLightScroll, _pointLights, true);
            GUILayout.Space(EditorGUIUtility.singleLineHeight);

            int previousCookieResolution = _manager.CustomTexturesWidth;
            int previousShadowResolution = _manager.ShadowTexturesWidth;
            int previousBakingMode = _manager.BakingMode;
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
            LightVolumeManagerTools.ApplySettings(_manager, markDirty: false, reinitializeCustomTextures: cookieLayoutChanged, reinitializeShadowTextures: shadowLayoutChanged);
            LightVolumeManagerTools.HandleBakingModeChanged(_manager, previousBakingMode);
            _registryChanged = false;
            _pointRegistryChanged = false;
            _nextStatsRefresh = 0d;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private ReorderableList CreateRegistryList(SerializedProperty source, bool pointLights) {
            ReorderableList list = new ReorderableList(serializedObject, source, true, false, false, false) {
                headerHeight = 0f,
                footerHeight = 0f,
                showDefaultBackground = false
            };
            list.drawElementCallback = (rect, index, active, focused) => DrawRegistryElement(rect, source, index, pointLights);
            list.onReorderCallbackWithDetails = (reorderable, oldIndex, newIndex) => {
                if (!pointLights) PreserveLightVolumePriorities(source, oldIndex, newIndex);
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

        // Light volumes are canonically sorted by descending RegistryWeight. Reordering the
        // serialized array alone would therefore be undone by ApplySettings immediately.
        // Rotate the existing priority values with the dragged range so its new visual order
        // remains canonical without inventing new weights or disturbing priorities outside it.
        private static void PreserveLightVolumePriorities(SerializedProperty source, int oldIndex, int newIndex) {
            if (oldIndex == newIndex || oldIndex < 0 || newIndex < 0 || oldIndex >= source.arraySize || newIndex >= source.arraySize) return;

            int first = Mathf.Min(oldIndex, newIndex);
            int last = Mathf.Max(oldIndex, newIndex);
            int count = last - first + 1;
            LightVolumeInstance[] volumes = new LightVolumeInstance[count];
            float[] weights = new float[count];
            for (int i = 0; i < count; i++) {
                volumes[i] = source.GetArrayElementAtIndex(first + i).objectReferenceValue as LightVolumeInstance;
                if (volumes[i] == null) return;
                weights[i] = volumes[i].RegistryWeight;
            }

            if (oldIndex < newIndex) {
                float movedWeight = weights[count - 1];
                for (int i = count - 1; i > 0; i--) weights[i] = weights[i - 1];
                weights[0] = movedWeight;
            } else {
                float movedWeight = weights[0];
                for (int i = 0; i < count - 1; i++) weights[i] = weights[i + 1];
                weights[count - 1] = movedWeight;
            }

            Undo.RecordObjects(volumes, "Reorder Light Volumes");
            for (int i = 0; i < count; i++) {
                LightVolumeInstance volume = volumes[i];
                if (volume.RegistryWeight == weights[i]) continue;
                volume.RegistryWeight = weights[i];
                LVUtils.MarkDirty(volume);
                LightVolumeManagerTools.CopyProxyToUdon(volume);
            }
        }

        private void DrawRegistryHeader(Rect rect, SerializedProperty source, bool pointLights) {
            string label = pointLights ? "Point Light Volumes" : "Light Volumes";
            GUIContent title = new GUIContent($"{label} ({source.arraySize})", pointLights ? "At most 128 active lights are rendered." : "At most 32 active volumes are rendered.");
            EditorGUI.LabelField(new Rect(rect.x + 15f, rect.y, rect.width - 15f, rect.height), title);

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

        private static bool ContainsReference(SerializedProperty source, UnityEngine.Object value) {
            for (int i = 0; i < source.arraySize; i++)
                if (source.GetArrayElementAtIndex(i).objectReferenceValue == value) return true;
            return false;
        }

        private static bool HasRegistryEntries(SerializedProperty source) {
            for (int i = 0; i < source.arraySize; i++)
                if (source.GetArrayElementAtIndex(i).objectReferenceValue != null) return true;
            return false;
        }

        private void DrawRegistryElement(Rect rect, SerializedProperty source, int sourceIndex, bool pointLights) {
            if (sourceIndex < 0 || sourceIndex >= source.arraySize) return;
            UnityEngine.Object value = source.GetArrayElementAtIndex(sourceIndex).objectReferenceValue;
            rect.y += 2f;
            float weightWidth = pointLights ? 0f : 48f;
            Rect iconRect = new Rect(rect.x, rect.y, 20f, EditorGUIUtility.singleLineHeight);
            Rect nameRect = new Rect(rect.x + 24f, rect.y, rect.width - 24f - weightWidth, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(iconRect, GetRegistryIcon(value, pointLights));
            EditorGUI.LabelField(nameRect, value != null ? value.name : "None");
            if (pointLights || !(value is LightVolumeInstance volume)) return;

            Rect weightRect = new Rect(rect.xMax - weightWidth + 3f, rect.y, weightWidth - 3f, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginChangeCheck();
            float weight = EditorGUI.FloatField(weightRect, volume.RegistryWeight);
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(volume, "Change Light Volume Weight");
            volume.RegistryWeight = weight;
            LVUtils.MarkDirty(volume);
            LightVolumeManagerTools.CopyProxyToUdon(volume);
            _registryChanged = true;
        }

        private static GUIContent GetRegistryIcon(UnityEngine.Object value, bool pointLights) {
            if (!pointLights && value is LightVolumeInstance volume)
                return EditorGUIUtility.IconContent(volume.IsAdditive ? "d_LightProbes Icon" : "d_PreMatLight1@2x");
            if (value is PointLightVolumeInstance pointLight) {
                if (pointLight.LightType == 1) return EditorGUIUtility.IconContent("d_Spotlight Icon");
                if (pointLight.LightType == 2) return EditorGUIUtility.IconContent("d_AreaLight Icon");
            }
            return EditorGUIUtility.IconContent("d_Light Icon");
        }

        private void DrawPointLightSettings() {
            DrawIntPopup("Cookie Resolution", "CustomTexturesWidth", TextureResolutionLabels, TextureResolutions);
            DrawIntPopup("Shadow Resolution", "ShadowTexturesWidth", TextureResolutionLabels, TextureResolutions);
            DrawSlider("ShadowBleedReduction", "Shadow Bleed Reduction", 0f, 1f);
            string varianceName = LightVolumeManagerTools.IsMobileBuildTarget() ? "ShadowMinVarianceMobile" : "ShadowMinVarianceDesktop";
            DrawSlider(varianceName, "Shadow Min Variance", 0f, 1f);
            DrawSlider("LightsBrightnessCutoff", "Brightness Cutoff", 0.05f, 1f);
        }

        private void DrawClusteringSettings() {
            SerializedProperty clustering = serializedObject.FindProperty("Clustering");
            EditorGUILayout.PropertyField(clustering, new GUIContent("Clustering Enabled", clustering.tooltip));
            if (!clustering.boolValue) return;
            DrawProperty("ClusteringMinLights", "Min Lights Count");
            DrawProperty("FroxelDensity", "Angular Density");
            DrawProperty("FroxelSlices", "Slices Count");
            DrawIntPopup("Coarse Reduction", "FroxelCoarse", CoarseLabels, CoarseValues);

            Camera camera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : Camera.main;
            float verticalFov = camera != null && !camera.orthographic ? Mathf.Clamp(camera.fieldOfView, 1f, 179f) : 90f;
            float aspect = camera != null && camera.aspect > 0.001f ? camera.aspect : 1.7777778f;
            float horizontalFov = Mathf.Atan(Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad) * aspect) * (2f * Mathf.Rad2Deg);
            float density = Mathf.Clamp(serializedObject.FindProperty("FroxelDensity").floatValue, 0.05f, 3f);
            int columns = Mathf.Clamp(Mathf.CeilToInt(horizontalFov * density), 1, 256);
            int rows = Mathf.Clamp(Mathf.CeilToInt(verticalFov * density), 1, 256);
            int slices = Mathf.Clamp(serializedObject.FindProperty("FroxelSlices").intValue, 8, 256);
            int coarse = LightVolumeManagerTools.ResolveCoarseFactor(serializedObject.FindProperty("FroxelCoarse").intValue);
            int shift = coarse == 2 ? 1 : coarse == 4 ? 2 : 3;
            int coarseColumns = (columns + coarse - 1) >> shift;
            int coarseRows = (rows + coarse - 1) >> shift;
            int coarseSlices = (slices + coarse - 1) >> shift;
            GUILayout.Space(3f);
            GUILayout.Label(
                new GUIContent(
                    $"Fine Froxels: <b>{columns} x {rows} x {slices} ({(long)columns * rows * slices:N0} froxels)</b>",
                    "The detailed camera grid used by shaders to find which Point Light Volumes affect each pixel. The shown size is an editor estimate; in-game it changes with the player's FOV."),
                RichLabelStyle);
            GUILayout.Label(
                new GUIContent(
                    $"Coarse Froxels: <b>{coarseColumns} x {coarseRows} x {coarseSlices} ({(long)coarseColumns * coarseRows * coarseSlices:N0} froxels)</b>",
                    "A simpler helper grid that quickly removes unrelated lights before the detailed Fine grid is built. The shown size is an editor estimate; in-game it changes with the player's FOV."),
                RichLabelStyle);
        }

        private void DrawBakingSettings() {
            DrawIntPopup("Baking Mode", "BakingMode", BakingLabels, BakingValues);
            int mode = serializedObject.FindProperty("BakingMode").intValue;
#if BAKERY_INCLUDED
            if (mode == 1) {
                DrawMask("Volume Bitmask", "VolumeBitmask");
                DrawMask("Probe Bitmask", "ProbeBitmask");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("FixLightProbesL1"));
            }
#endif
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Denoise"));
            if (mode == 0) {
                SerializedProperty dilate = serializedObject.FindProperty("DilateInvalidProbes");
                EditorGUILayout.PropertyField(dilate);
                if (dilate.boolValue) {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("DilationIterations"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("DilationBackfaceBias"));
                }
            }
            DrawIntPopup("Downscale Volumes", "DownscaleVolumes", DownscaleLabels, DownscaleValues);
            DrawProperty("LightVolumeAtlas", "Light Volume Atlas");
        }

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

        private void DrawActions(bool hasLightVolumes, bool hasPointLights) {
            GUILayout.Space(InspectorSectionSpacing);
            using (new EditorGUILayout.HorizontalScope()) {
                if (hasLightVolumes) {
                    if (GUILayout.Button(new GUIContent("Pack Light Volumes", "Rebuilds the Light Volume 3D atlas.")))
                        LightVolumeManagerTools.GenerateAtlas(_manager);
                }
                if (hasPointLights) {
                    using (new EditorGUI.DisabledScope(!_canBatchBakeShadows)) {
                        if (GUILayout.Button(new GUIContent("Bake Shadows", "Bakes every shadow-enabled light with Rebake Shadows enabled.")))
                            LightVolumeManagerTools.BakeShadowMaps(_manager);
                    }
                }
            }
        }

        private void DrawDebugSection() {
            GUILayout.Space(InspectorSectionSpacing);
            EditorGUI.BeginChangeCheck();
            _debugExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(
                _debugExpanded,
                new GUIContent("Debug", "Shows read-only live Manager data for troubleshooting."));
            if (EditorGUI.EndChangeCheck()) {
                SessionState.SetBool(DebugFoldoutSessionKey, _debugExpanded);
                _nextRuntimeDebugRefresh = 0d;
            }

            if (_debugExpanded) {
                if (!EditorApplication.isPlaying)
                    EditorGUILayout.HelpBox("Live values are populated in Play Mode. Runtime texture arrays are rebuilt on initialization and are not stored in the build.", MessageType.Info);

                LightVolumeDebugGUI.DrawGroupHeader(
                    "Runtime Texture Arrays",
                    false,
                    "Live texture arrays rebuilt by the Manager and sampled by shaders.");
                LightVolumeDebugGUI.DrawObject(
                    "Cookie Array",
                    _manager.CustomTextures,
                    typeof(RenderTexture),
                    "The live cookie, LUT and cubemap array. Its serialized reference is cleared for builds and the Manager rebuilds it at runtime.");
                LightVolumeDebugGUI.DrawInt("Cookie Slices", GetTextureDepth(_manager.CustomTextures), "Number of allocated array slices. Each cubemap uses six slices.");
                LightVolumeDebugGUI.DrawInt("Cookie Cubemaps", _manager.CubemapsCount, "Number of cubemap cookie sources packed into the live array.");
                LightVolumeDebugGUI.DrawBool("Dynamic Cookie Sources", _manager.HasAutoCustomTextureUpdates, "Whether any cookie source must be copied again at runtime.");

                GUILayout.Space(EditorGUIUtility.standardVerticalSpacing);
                LightVolumeDebugGUI.DrawObject(
                    "Shadow Array",
                    _manager.ShadowTextures,
                    typeof(RenderTexture),
                    "The live shadow array. Its serialized reference is cleared for builds and the Manager rebuilds it at runtime.");
                LightVolumeDebugGUI.DrawInt("Shadow Slices", GetTextureDepth(_manager.ShadowTextures), "Number of allocated array slices. Each cubemap shadow uses six slices.");
                LightVolumeDebugGUI.DrawInt("Shadow Maps", _manager.ShadowMapsCount, "Number of 2D shadow maps packed into the live array.");
                LightVolumeDebugGUI.DrawInt("Shadow Cubemaps", _manager.ShadowCubemapsCount, "Number of cubemap shadow sources packed into the live array.");
                LightVolumeDebugGUI.DrawBool("Dynamic Shadow Sources", _manager.HasAutoShadowTextureUpdates, "Whether any shadow source must be copied again at runtime.");

                LightVolumeDebugGUI.DrawGroupHeader(
                    "Froxel Clustering",
                    true,
                    "Live clustering textures and the current clustering state.");
                LightVolumeDebugGUI.DrawObject("Fine Cluster Mask", _manager.FineClusterMaskPreview, typeof(RenderTexture), "The detailed clustered-light mask currently sampled by shaders.");
                LightVolumeDebugGUI.DrawObject("Coarse Cluster Mask", _manager.CoarseClusterMaskPreview, typeof(RenderTexture), "The lower-resolution mask used to reject unrelated lights before building the Fine mask.");
                LightVolumeDebugGUI.DrawText("Clustering Status", GetClusteringStatus(), "Current runtime state of froxel clustering.");

                LightVolumeDebugGUI.DrawGroupHeader(
                    "Runtime State",
                    true,
                    "Live initialization state and the counts currently uploaded by the Manager.");
                LightVolumeDebugGUI.DrawBool("Runtime Initialized", _manager.RuntimeInitializedPreview, "Whether the Manager has completed runtime initialization.");
                LightVolumeDebugGUI.DrawInt("Active Light Volumes", _manager.EnabledCount, "Light Volumes currently uploaded to shaders.");
                LightVolumeDebugGUI.DrawInt("Active Point Lights", _manager.ActivePointLightCountPreview, "Point Light Volumes currently uploaded to shaders.");
                LightVolumeDebugGUI.DrawInt("Active Shadows", _manager.ActiveShadowCountPreview, "Uploaded Point Light Volumes that currently use a valid shadow map.");

                LightVolumeDebugGUI.DrawGroupHeader(
                    "Runtime Materials",
                    true,
                    "Materials used internally by runtime texture and clustering passes.");
                LightVolumeDebugGUI.DrawObject("Cookie Copy Material", _manager.CubemapFaceMaterial, typeof(Material), "Copies cubemap faces into the runtime cookie array.");
                LightVolumeDebugGUI.DrawObject("Shadow Depth Material", _manager.RuntimeShadowDepthEncodeMaterial, typeof(Material), "Encodes shadow-camera depth into runtime shadow textures.");
                LightVolumeDebugGUI.DrawObject("Shadow Blur Material", _manager.RuntimeShadowBlurMaterial, typeof(Material), "Filters runtime shadow textures.");
                LightVolumeDebugGUI.DrawObject("Clustering Material", _manager.ClusteringMaterialPreview, typeof(Material), "Builds the Fine and Coarse froxel masks.");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void RefreshRuntimeDebugProxy() {
#if UDONSHARP
            if (!_debugExpanded || !EditorApplication.isPlaying || _manager == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextRuntimeDebugRefresh) return;
            _nextRuntimeDebugRefresh = now + RuntimeDebugRefreshInterval;
            if (UdonSharpEditorUtility.GetBackingUdonBehaviour(_manager) != null)
                UdonSharpEditorUtility.CopyUdonToProxy(_manager);
#endif
        }

        private string GetClusteringStatus() {
            if (!_manager.Clustering) return "Disabled";
            if (_manager.ClusteringUnsupportedPreview) return "Unsupported";
            if (_manager.ClusteringAllocationFailedPreview) return "Allocation Failed";
            if (!_manager.ClusteringActivePreview) return "Inactive";
            return _manager.ClusterMaskValidPreview ? "Active" : "Building";
        }

        private static int GetTextureDepth(RenderTexture texture) {
            return texture != null ? Mathf.Max(texture.volumeDepth, 1) : 0;
        }

        private static void DrawSectionHeader(string title, bool addTopSpacing) {
            if (addTopSpacing) GUILayout.Space(InspectorSectionSpacing);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private void DrawScrollableList(ReorderableList list, RegistryScrollState scroll, SerializedProperty source, bool pointLights) {
            float contentHeight = Mathf.Max(list.GetHeight(), list.elementHeight + RegistryScrollPadding * 2f);
            float maxViewportHeight = (list.elementHeight + 2f) * VisibleRegistryRows + RegistryScrollPadding * 2f;
            float viewportHeight = Mathf.Min(contentHeight, maxViewportHeight);
            Rect area = GUILayoutUtility.GetRect(0f, RegistryHeaderHeight + viewportHeight + 1f, GUILayout.ExpandWidth(true));
            Rect headerRect = new Rect(area.x, area.y, area.width, RegistryHeaderHeight);
            Rect bodyRect = new Rect(area.x, headerRect.yMax - 1f, area.width, area.yMax - headerRect.yMax + 1f);
            Rect viewportRect = new Rect(area.x + 1f, headerRect.yMax, area.width - 2f, viewportHeight);

            if (Event.current.type == EventType.Repaint) {
                ReorderableList.defaultBehaviours.boxBackground.Draw(bodyRect, false, false, false, false);
                ReorderableList.defaultBehaviours.headerBackground.Draw(headerRect, false, false, false, false);
            }
            DrawRegistryHeader(headerRect, source, pointLights);

            float maxScrollY = Mathf.Max(0f, contentHeight - viewportHeight);
            HandleSmoothRegistryScroll(viewportRect, list, scroll, maxScrollY);

            bool showScrollbar = maxScrollY > 0.5f;
            if (showScrollbar) viewportRect.width = Mathf.Max(1f, viewportRect.width - RegistryScrollbarRightInset);
            float scrollbarWidth = showScrollbar ? Mathf.Max(16f, GUI.skin.verticalScrollbar.fixedWidth) : 0f;
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
            if (GUIUtility.hotControl != 0 && list.index >= 0 && current.mousePosition.x >= viewport.x
                && current.mousePosition.x <= viewport.x + 36f) {
                float direction = 0f;
                if (current.mousePosition.y >= viewport.y && current.mousePosition.y < viewport.y + RegistryDragScrollEdge) direction = -1f;
                else if (current.mousePosition.y <= viewport.yMax && current.mousePosition.y > viewport.yMax - RegistryDragScrollEdge) direction = 1f;
                if (direction != 0f) {
                    scroll.TargetY = Mathf.Clamp(scroll.TargetY + direction * RegistryDragScrollSpeed * deltaTime, 0f, maxScrollY);
                }
            }
            if (Mathf.Abs(scroll.Position.y - scroll.TargetY) < 0.05f && Mathf.Abs(scroll.Velocity) < 0.05f) {
                scroll.Position.y = scroll.TargetY;
                scroll.Velocity = 0f;
                return;
            }

            scroll.Position.y = Mathf.SmoothDamp(
                scroll.Position.y,
                scroll.TargetY,
                ref scroll.Velocity,
                RegistryScrollSmoothTime,
                Mathf.Infinity,
                deltaTime);
            Repaint();
        }

        private void RefreshStats() {
            double now = EditorApplication.timeSinceStartup;
            if (_cachedPointCount == _pointLights.arraySize && now < _nextStatsRefresh) return;
            _cachedPointCount = _pointLights.arraySize;
            _nextStatsRefresh = now + StatsRefreshInterval;
            ulong vram = 0;
            ulong bundle = 0;
            Texture atlas = _manager.LightVolumeAtlasBase != null ? _manager.LightVolumeAtlasBase : _manager.LightVolumeAtlas;
            if (atlas is Texture3D atlas3D) {
                ulong bytes = (ulong)atlas3D.width * (ulong)atlas3D.height * (ulong)atlas3D.depth * 8UL;
                vram += bytes;
                bundle += bytes;
            } else if (atlas is RenderTexture atlasRT) vram += GetRenderTextureBytes(atlasRT, 8UL);
            if (_manager.CustomTextures != null) vram += GetRenderTextureBytes(_manager.CustomTextures, 8UL);
            if (_manager.ShadowTextures != null) vram += GetRenderTextureBytes(_manager.ShadowTextures, _manager.ShadowTextureFormat == 0 ? 8UL : 16UL);

            _canBatchBakeShadows = false;
            _countedShadowTextures.Clear();
            PointLightVolumeInstance[] lights = _manager.PointLightVolumeInstances;
            for (int i = 0; i < lights.Length; i++) {
                PointLightVolumeInstance light = lights[i];
                if (light == null) continue;
                if (light.Shadows && light.RebakeShadows) _canBatchBakeShadows = true;
                if (!light.Shadows || light.BakeInGame || !(light.ShadowMap is Texture texture) || texture is RenderTexture || !_countedShadowTextures.Add(texture)) continue;
                ulong bytes = GetTextureTexels(texture) * (_manager.ShadowTextureFormat == 0 ? 8UL : 16UL);
                vram += bytes;
                bundle += bytes;
            }
            _cachedVramBytes = vram;
            _cachedBundleBytes = bundle;
        }

        private static ulong GetRenderTextureBytes(RenderTexture texture, ulong bytesPerPixel) {
            ulong bytes = (ulong)texture.width * (ulong)texture.height * (ulong)Mathf.Max(texture.volumeDepth, 1) * bytesPerPixel;
            return texture.useMipMap ? bytes * 4UL / 3UL : bytes;
        }

        private static ulong GetTextureTexels(Texture texture) {
            if (texture is Texture2DArray array) return (ulong)array.width * (ulong)array.height * (ulong)array.depth;
            if (texture is Cubemap cubemap) return (ulong)cubemap.width * (ulong)cubemap.height * 6UL;
            if (texture is Texture2D texture2D) return (ulong)texture2D.width * (ulong)texture2D.height;
            return 0;
        }

        private static string FormatMegabytes(ulong bytes) {
            return (bytes / (double)(1024 * 1024)).ToString("0.00");
        }

        private void DrawProperty(string propertyName, string label) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip));
        }

        private void DrawSlider(string propertyName, string label, float min, float max) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            EditorGUILayout.Slider(property, min, max, new GUIContent(label, property.tooltip));
        }

        private void DrawIntPopup(string label, string propertyName, string[] labels, int[] values) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Rect rect = EditorGUILayout.GetControlRect();
            Rect popupRect = EditorGUI.PrefixLabel(rect, new GUIContent(label, property.tooltip));
            property.intValue = EditorGUI.IntPopup(popupRect, property.intValue, labels, values);
        }

        private void DrawMask(string label, string propertyName) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.intValue = EditorGUILayout.MaskField(new GUIContent(label, property.tooltip), property.intValue, BakeryMaskLabels);
        }

        private void RefreshManagerCount() {
            _multipleManagers = false;
            if (_manager == null || !_manager.gameObject.scene.IsValid()) return;
            LightVolumeManager[] managers = FindObjectsByType<LightVolumeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (manager == null || manager.gameObject.scene != _manager.gameObject.scene) continue;
                if (++count > 1) {
                    _multipleManagers = true;
                    return;
                }
            }
        }

        private GUIStyle RichLabelStyle => _richLabelStyle ?? (_richLabelStyle = new GUIStyle(EditorStyles.label) { richText = true });

        public override bool RequiresConstantRepaint() {
            return _debugExpanded && EditorApplication.isPlaying;
        }
    }

    internal static class LightVolumeDebugGUI {
        private static GUIStyle _valueStyle;

        private static GUIStyle ValueStyle =>
            _valueStyle ?? (_valueStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold });

        public static void DrawGroupHeader(string title, bool addTopSpacing, string tooltip) {
            GUILayout.Space(addTopSpacing ? 7f : 3f);
            EditorGUILayout.LabelField(new GUIContent(title, tooltip), EditorStyles.boldLabel);
        }

        public static void DrawObject(string label, Object value, System.Type type, string tooltip) {
            EditorGUI.ObjectField(
                EditorGUILayout.GetControlRect(),
                new GUIContent(label, tooltip),
                value,
                type,
                true);
        }

        public static void DrawText(string label, string value, string tooltip) {
            Rect valueRect = EditorGUI.PrefixLabel(
                EditorGUILayout.GetControlRect(),
                new GUIContent(label, tooltip));
            EditorGUI.LabelField(valueRect, new GUIContent(value), ValueStyle);
        }

        public static void DrawInt(string label, int value, string tooltip) {
            DrawText(label, value.ToString(), tooltip);
        }

        public static void DrawFloat(string label, float value, string tooltip) {
            DrawText(label, value.ToString("0.###"), tooltip);
        }

        public static void DrawVector3(string label, Vector3 value, string tooltip) {
            DrawText(label, value.ToString("0.###"), tooltip);
        }

        public static void DrawVector3Int(string label, Vector3Int value, string tooltip) {
            DrawText(label, value.ToString(), tooltip);
        }

        public static void DrawVector4(string label, Vector4 value, string tooltip) {
            DrawText(label, value.ToString("0.###"), tooltip);
        }

        public static void DrawQuaternion(string label, Quaternion value, string tooltip) {
            DrawText(label, value.eulerAngles.ToString("0.###") + " deg", tooltip);
        }

        public static void DrawBool(string label, bool value, string tooltip) {
            EditorGUI.Toggle(
                EditorGUILayout.GetControlRect(),
                new GUIContent(label, tooltip),
                value);
        }
    }
}

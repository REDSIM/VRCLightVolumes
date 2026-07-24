using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace VRCLightVolumes {
    [CustomEditor(typeof(LightVolumeManager))]
    public sealed class LightVolumeManagerEditor : Editor {
        private const int ItemsPerPage = 16;
        private const double StatsRefreshInterval = 0.25d;
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
        private static readonly GUIContent[] PageDigits = {
            new GUIContent("0"), new GUIContent("1"), new GUIContent("2"), new GUIContent("3"), new GUIContent("4"),
            new GUIContent("5"), new GUIContent("6"), new GUIContent("7"), new GUIContent("8"), new GUIContent("9")
        };
        private static readonly GUIContent PageSeparator = new GUIContent("/");

        private sealed class PageState {
            public readonly List<int> Items = new List<int>(ItemsPerPage);
            public int Index;
            public int Count = 1;
            public int Start;
            public string CurrentText = "1";
            public string CountText = "1";

            public void Refresh(int itemCount) {
                Count = Mathf.Max(1, (itemCount + ItemsPerPage - 1) / ItemsPerPage);
                Index = Mathf.Clamp(Index, 0, Count - 1);
                Start = Index * ItemsPerPage;
                CurrentText = (Index + 1).ToString();
                CountText = Count.ToString();
                Items.Clear();
                int end = Mathf.Min(Start + ItemsPerPage, itemCount);
                for (int i = Start; i < end; i++) Items.Add(i);
            }

            public int SourceIndex(int visibleIndex) {
                return visibleIndex >= 0 && visibleIndex < Items.Count ? Start + visibleIndex : -1;
            }
        }

        private LightVolumeManager _manager;
        private SerializedProperty _lightVolumes;
        private SerializedProperty _pointLights;
        private ReorderableList _lightVolumeList;
        private ReorderableList _pointLightList;
        private readonly PageState _lightVolumePage = new PageState();
        private readonly PageState _pointLightPage = new PageState();
        private bool _registryChanged;
        private bool _pointRegistryChanged;
        private GUIContent _previousPage;
        private GUIContent _nextPage;
        private GUIStyle _pageGlyphStyle;
        private GUIStyle _richLabelStyle;
        private double _nextStatsRefresh;
        private int _cachedPointCount = -1;
        private ulong _cachedVramBytes;
        private ulong _cachedBundleBytes;
        private bool _canBatchBakeShadows;
        private bool _multipleManagers;
        private readonly HashSet<Texture> _countedShadowTextures = new HashSet<Texture>();

        private void OnEnable() {
            _manager = (LightVolumeManager)target;
            _lightVolumes = serializedObject.FindProperty("LightVolumeInstances");
            _pointLights = serializedObject.FindProperty("PointLightVolumeInstances");
            _lightVolumeList = CreateRegistryList(_lightVolumePage, _lightVolumes, false);
            _pointLightList = CreateRegistryList(_pointLightPage, _pointLights, true);
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
            serializedObject.Update();
            EditorGUILayout.Space(EditorGUIUtility.singleLineHeight * 0.5f);

            if (LVUtils.IsInPrefabAsset(_manager))
                EditorGUILayout.HelpBox("This component is part of a prefab asset. Edit the instance placed in a scene.", MessageType.Warning);
            if (_multipleManagers)
                EditorGUILayout.HelpBox("Multiple active Light Volume Managers were found. Assign every volume explicitly and keep only one manager active per rendered scene.", MessageType.Error);

            RefreshStats();
            GUILayout.Label($"Data size in VRAM: <b>{FormatMegabytes(_cachedVramBytes)} MB</b>", RichLabelStyle);
            GUILayout.Label($"Data size in bundle: <b>{FormatMegabytes((ulong)(_cachedBundleBytes * BundleCompressionEstimate))} MB (Approximately)</b>", RichLabelStyle);
            GUILayout.Space(8f);

            DrawPagedList(_lightVolumeList, _lightVolumePage, _lightVolumes.arraySize);
            GUILayout.Space(4f);
            DrawPagedList(_pointLightList, _pointLightPage, _pointLights.arraySize);
            GUILayout.Space(6f);

            int previousCookieResolution = _manager.CustomTexturesWidth;
            int previousShadowResolution = _manager.ShadowTexturesWidth;
            int previousBakingMode = _manager.BakingMode;
            DrawPointLightSettings();
            DrawClusteringSettings();
            DrawBakingSettings();
            DrawVisualSettings();
            DrawActions();

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

        private ReorderableList CreateRegistryList(PageState page, SerializedProperty source, bool pointLights) {
            ReorderableList list = new ReorderableList(page.Items, typeof(int), true, true, false, false) { footerHeight = 0f };
            list.drawHeaderCallback = rect => DrawRegistryHeader(rect, source, pointLights);
            list.drawElementCallback = (rect, index, active, focused) => DrawRegistryElement(rect, page, source, index, pointLights);
            list.onReorderCallbackWithDetails = (reorderable, oldIndex, newIndex) => {
                int oldSource = page.SourceIndex(oldIndex);
                int newSource = page.SourceIndex(newIndex);
                if (oldSource < 0 || newSource < 0 || oldSource >= source.arraySize || newSource >= source.arraySize) return;
                source.MoveArrayElement(oldSource, newSource);
                _registryChanged = true;
                if (pointLights) _pointRegistryChanged = true;
            };
            list.onSelectCallback = reorderable => {
                int sourceIndex = page.SourceIndex(reorderable.index);
                if (sourceIndex < 0 || sourceIndex >= source.arraySize) return;
                UnityEngine.Object value = source.GetArrayElementAtIndex(sourceIndex).objectReferenceValue;
                if (value != null) EditorGUIUtility.PingObject(value);
            };
            return list;
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

        private void DrawRegistryElement(Rect rect, PageState page, SerializedProperty source, int visibleIndex, bool pointLights) {
            int sourceIndex = page.SourceIndex(visibleIndex);
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
            if (_pointLights.arraySize == 0) return;
            DrawIntPopup("Cookie Resolution", "CustomTexturesWidth", TextureResolutionLabels, TextureResolutions);
            DrawIntPopup("Shadow Resolution", "ShadowTexturesWidth", TextureResolutionLabels, TextureResolutions);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ShadowBleedReduction"));
            string varianceName = LightVolumeManagerTools.IsMobileBuildTarget() ? "ShadowMinVarianceMobile" : "ShadowMinVarianceDesktop";
            EditorGUILayout.PropertyField(serializedObject.FindProperty(varianceName), new GUIContent("Shadow Min Variance"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("LightsBrightnessCutoff"), new GUIContent("Brightness Cutoff"));
        }

        private void DrawClusteringSettings() {
            GUILayout.Space(8f);
            SerializedProperty clustering = serializedObject.FindProperty("Clustering");
            EditorGUILayout.PropertyField(clustering, new GUIContent("Froxel Clustering", clustering.tooltip));
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
            GUILayout.Label($"Fine Froxels: <b>{columns} x {rows} x {slices} ({(long)columns * rows * slices:N0} froxels)</b>", RichLabelStyle);
            GUILayout.Label($"Coarse Froxels: <b>{coarseColumns} x {coarseRows} x {coarseSlices} ({(long)coarseColumns * coarseRows * coarseSlices:N0} froxels)</b>", RichLabelStyle);
        }

        private void DrawBakingSettings() {
            GUILayout.Space(8f);
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
        }

        private void DrawVisualSettings() {
            GUILayout.Space(8f);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("LightProbesBlending"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SharpBounds"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AutoUpdateVolumes"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AutoUpdateTextures"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AdditiveMaxOverdraw"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ForceSceneLighting"));
        }

        private void DrawActions() {
            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(_lightVolumes.arraySize == 0)) {
                    if (GUILayout.Button(new GUIContent("Pack Light Volumes", "Rebuilds the Light Volume 3D atlas.")))
                        LightVolumeManagerTools.GenerateAtlas(_manager);
                }
                using (new EditorGUI.DisabledScope(!_canBatchBakeShadows)) {
                    if (GUILayout.Button(new GUIContent("Bake Shadows", "Bakes every shadow-enabled light with Rebake Shadows enabled.")))
                        LightVolumeManagerTools.BakeShadowMaps(_manager);
                }
            }
        }

        private void DrawPagedList(ReorderableList list, PageState page, int itemCount) {
            page.Refresh(itemCount);
            if (list.index >= page.Items.Count) list.index = -1;
            list.DoLayoutList();
            if (page.Count <= 1) return;
            int missingRows = ItemsPerPage - page.Items.Count;
            if (missingRows > 0) DrawEmptyRows(list, missingRows);
            EnsurePageStyles();
            GUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(page.Index == 0))
                    if (GUILayout.Button(_previousPage, EditorStyles.miniButtonLeft, GUILayout.Width(28f), GUILayout.Height(20f))) ChangePage(list, page, -1);
                float width = Mathf.Max(54f, page.CountText.Length * 16f + 14f);
                Rect labelRect = GUILayoutUtility.GetRect(width, 20f, _pageGlyphStyle, GUILayout.Width(width), GUILayout.Height(20f));
                DrawPageLabel(labelRect, page);
                using (new EditorGUI.DisabledScope(page.Index == page.Count - 1))
                    if (GUILayout.Button(_nextPage, EditorStyles.miniButtonRight, GUILayout.Width(28f), GUILayout.Height(20f))) ChangePage(list, page, 1);
                GUILayout.FlexibleSpace();
            }
        }

        private static void DrawEmptyRows(ReorderableList list, int count) {
            float rowHeight = list.elementHeight + 2f;
            Rect reserved = GUILayoutUtility.GetRect(10f, count * rowHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            Rect background = new Rect(reserved.x, reserved.y - 4f, reserved.width, reserved.height + 4f);
            ReorderableList.defaultBehaviours.boxBackground.Draw(background, false, false, false, false);
            Rect row = new Rect(reserved.x + 1f, reserved.y - 4f, reserved.width - 2f, rowHeight);
            for (int i = 0; i < count; i++) {
                ReorderableList.defaultBehaviours.DrawElementBackground(row, -1, false, false, false);
                row.y += rowHeight;
            }
        }

        private void ChangePage(ReorderableList list, PageState page, int offset) {
            page.Index = Mathf.Clamp(page.Index + offset, 0, page.Count - 1);
            list.index = -1;
            GUI.FocusControl(null);
            Repaint();
        }

        private void DrawPageLabel(Rect rect, PageState page) {
            const float cell = 8f;
            int slots = page.CountText.Length;
            float separatorX = Mathf.Round(rect.center.x - cell * 0.5f);
            int currentOffset = slots - page.CurrentText.Length;
            for (int i = 0; i < page.CurrentText.Length; i++)
                DrawPageGlyph(separatorX - (slots - currentOffset - i) * cell, rect, page.CurrentText[i]);
            DrawPageGlyph(separatorX, rect, '/');
            for (int i = 0; i < page.CountText.Length; i++)
                DrawPageGlyph(separatorX + (i + 1) * cell, rect, page.CountText[i]);
        }

        private void DrawPageGlyph(float x, Rect row, char glyph) {
            GUIContent content = glyph == '/' ? PageSeparator : PageDigits[glyph - '0'];
            GUI.Label(new Rect(x, row.y, 8f, row.height), content, _pageGlyphStyle);
        }

        private void EnsurePageStyles() {
            if (_previousPage == null) {
                Texture previous = EditorGUIUtility.IconContent("tab_prev").image;
                Texture next = EditorGUIUtility.IconContent("tab_next").image;
                _previousPage = previous != null ? new GUIContent(previous, "Previous page") : new GUIContent("<", "Previous page");
                _nextPage = next != null ? new GUIContent(next, "Next page") : new GUIContent(">", "Next page");
            }
            if (_pageGlyphStyle != null) return;
            _pageGlyphStyle = new GUIStyle(EditorStyles.miniLabel) {
                alignment = TextAnchor.MiddleCenter,
                contentOffset = new Vector2(0f, -1f),
                padding = new RectOffset()
            };
            Color color = EditorStyles.label.normal.textColor;
            _pageGlyphStyle.normal.textColor = color;
            _pageGlyphStyle.hover.textColor = color;
            _pageGlyphStyle.active.textColor = color;
            _pageGlyphStyle.focused.textColor = color;
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
            _multipleManagers = FindObjectsByType<LightVolumeManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length > 1;
        }

        private GUIStyle RichLabelStyle => _richLabelStyle ?? (_richLabelStyle = new GUIStyle(EditorStyles.label) { richText = true });
    }
}

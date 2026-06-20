using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;

namespace VRCLightVolumes {
    [CustomEditor(typeof(LightVolumeSetup))]
    public class LightVolumeSetupEditor : Editor {

        private SerializedProperty _volumesProp;
        private SerializedProperty _weightsProp;
        private ReorderableList _lightVolumesList;

        private SerializedProperty _pointLightVolumesProp;
        private SerializedProperty _bakingModeProp;
        private SerializedProperty _volumeBitmaskProp;
        private SerializedProperty _probeBitmaskProp;
        private ReorderableList _pointLightVolumesList;

        private LightVolumeSetup _lightVolumeSetup;

        private bool _isMultipleInstancesError = false;
        private static readonly string[] _bakeryBitmaskLabels = new string[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30" };
        private const double BundleCompressionEstimate = 0.315d;

        private void OnEnable() {

            int managersCount = FindObjectsByType<LightVolumeManager>(FindObjectsSortMode.None).Length;
            _isMultipleInstancesError = managersCount > 1;

            _lightVolumeSetup = (LightVolumeSetup)target;

            _volumesProp = serializedObject.FindProperty("LightVolumes");
            _weightsProp = serializedObject.FindProperty("LightVolumesWeights");
            _bakingModeProp = serializedObject.FindProperty("BakingMode");
            _volumeBitmaskProp = serializedObject.FindProperty("VolumeBitmask");
            _probeBitmaskProp = serializedObject.FindProperty("ProbeBitmask");

            // ============ LIGHT VOLUMES LIST ===============

            _lightVolumesList = new ReorderableList(
                serializedObject,
                _volumesProp,
                true, // draggable
                true, // displayHeader
                false, // displayAddButton
                false  // displayRemoveButton
            );

            // Drawing header
            _lightVolumesList.drawHeaderCallback = (Rect rect) => {

                float totalWidth = rect.width;
                float availableWidth = totalWidth - 15f - 4f;
                float weightWidth = 42f;
                float space = 5f;
                float volumeWidth = availableWidth - weightWidth - space;
                float xOffset = rect.x + 15f;

                var headerCountStyle = new GUIStyle(EditorStyles.numberField) {
                    alignment = TextAnchor.MiddleCenter,
                    contentOffset = Vector2.zero,
                    fixedHeight = EditorGUIUtility.singleLineHeight - 3,
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0),
                    fontSize = 11
                };
                headerCountStyle.normal.textColor = EditorStyles.label.normal.textColor;

                Rect volumeHeaderRect = new Rect(xOffset, rect.y, volumeWidth, EditorGUIUtility.singleLineHeight);
                Rect weightHeaderRect = new Rect(xOffset + volumeWidth + space, rect.y, weightWidth, EditorGUIUtility.singleLineHeight);
                Rect fieldRect = new Rect(xOffset + 96, rect.y + 2, 32, rect.height);

                GUIContent title = new GUIContent($"Light Volumes ({_lightVolumeSetup.LightVolumes.Count})");
                title.tooltip = "Max 32 can be visible on scene at the same time.";
                EditorGUI.LabelField(volumeHeaderRect, title);
                EditorGUI.LabelField(weightHeaderRect, "Weight");

                EventType eventType = Event.current.type;
                if (eventType == EventType.DragUpdated || eventType == EventType.DragPerform) {
                    if (!rect.Contains(Event.current.mousePosition)) return;
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (eventType == EventType.DragPerform) {
                        DragAndDrop.AcceptDrag();
                        for (int i = 0; i < DragAndDrop.objectReferences.Length; i++) {
                            GameObject go = (GameObject)DragAndDrop.objectReferences[i];
                            if (go.TryGetComponent(out LightVolume volume)) {
                                int newIndex = _volumesProp.arraySize;
                                if (newIndex == 256) break;
                                _volumesProp.arraySize++;
                                _weightsProp.arraySize = _volumesProp.arraySize;
                                _volumesProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = volume;
                                _weightsProp.GetArrayElementAtIndex(newIndex).floatValue = 0;
                            }
                        }
                        Event.current.Use();
                    }
                }

            };

            // Drawing each element
            _lightVolumesList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) => {

                if (index < 0 || index >= _volumesProp.arraySize || index >= _weightsProp.arraySize) return;

                SerializedProperty volumeElement = _volumesProp.GetArrayElementAtIndex(index);
                SerializedProperty weightElement = _weightsProp.GetArrayElementAtIndex(index);

                rect.y += 2; // Top padding
                float totalWidth = rect.width;
                float weightWidth = 45f; // Weight width
                float space = 5f;        // Spacing

                Rect iconRect = new Rect(rect.x, rect.y, totalWidth - weightWidth - space, EditorGUIUtility.singleLineHeight);
                Rect volumeRect = new Rect(rect.x + 24, rect.y, totalWidth - weightWidth - space, EditorGUIUtility.singleLineHeight);
                Rect weightRect = new Rect(rect.x + totalWidth - weightWidth, rect.y, weightWidth, EditorGUIUtility.singleLineHeight);

                if (volumeElement.objectReferenceValue != null && volumeElement.objectReferenceValue.GetType() == typeof(LightVolume)) {
                    var volume = (LightVolume)volumeElement.objectReferenceValue;
                    GUIContent icon = volume.Additive ? EditorGUIUtility.IconContent("d_LightProbes Icon") : EditorGUIUtility.IconContent("d_PreMatLight1@2x");
                    icon.tooltip = volume.Additive ? "Additive Volume" : "Regular Volume";
                    EditorGUI.LabelField(iconRect, icon);
                }

                EditorGUI.LabelField(volumeRect, volumeElement.objectReferenceValue != null ? volumeElement.objectReferenceValue.name : "None");
                EditorGUI.PropertyField(weightRect, weightElement, GUIContent.none);

            };

            // On Moving element around
            _lightVolumesList.onReorderCallbackWithDetails = (ReorderableList list, int oldIndex, int newIndex) => {
                if (oldIndex >= 0 && oldIndex < _weightsProp.arraySize && newIndex >= 0 && newIndex < _weightsProp.arraySize) {
                    _weightsProp.MoveArrayElement(oldIndex, newIndex);
                } else if (_weightsProp.arraySize != _volumesProp.arraySize) {
                    // In case of a sync error
                    _weightsProp.arraySize = _volumesProp.arraySize;
                    EditorUtility.SetDirty(target);
                }
                _lightVolumeSetup.SyncUdonScript();
            };

            // On Click on element
            _lightVolumesList.onSelectCallback = (ReorderableList list) => {
                SerializedProperty volumeElement = _volumesProp.GetArrayElementAtIndex(list.index);
                LightVolume volume = volumeElement.objectReferenceValue as LightVolume;
                if (volume != null) EditorGUIUtility.PingObject(volume.gameObject);
            };

            // ================ POINT LIGHT VOLUMES LIST =============

            _pointLightVolumesProp = serializedObject.FindProperty("PointLightVolumes");

            _pointLightVolumesList = new ReorderableList(
                serializedObject,
                _pointLightVolumesProp,
                true, // draggable
                true, // displayHeader
                false, // displayAddButton
                false  // displayRemoveButton
            );

            // Drawing header
            _pointLightVolumesList.drawHeaderCallback = (Rect rect) => {

                float totalWidth = rect.width;
                float availableWidth = totalWidth - 15f - 4f;
                float weightWidth = 42f;
                float space = 5f;
                float volumeWidth = availableWidth - weightWidth - space;
                float xOffset = rect.x + 15f;

                Rect volumeHeaderRect = new Rect(xOffset, rect.y, volumeWidth, EditorGUIUtility.singleLineHeight);

                GUIContent title = new GUIContent($"Point Light Volumes ({_lightVolumeSetup.PointLightVolumes.Count})");
                title.tooltip = "Max 128 can be visible on scene at the same time.";
                EditorGUI.LabelField(volumeHeaderRect, title);

            };

            // Drawing each element
            _pointLightVolumesList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) => {

                if (index < 0 || index >= _pointLightVolumesProp.arraySize) return;

                SerializedProperty volumeElement = _pointLightVolumesProp.GetArrayElementAtIndex(index);

                rect.y += 2; // Top padding
                float totalWidth = rect.width;
                float weightWidth = 45f; // Weight width
                float space = 5f;        // Spacing

                Rect iconRect = new Rect(rect.x, rect.y, totalWidth - weightWidth - space, EditorGUIUtility.singleLineHeight);
                Rect volumeRect = new Rect(rect.x + 24, rect.y, totalWidth - weightWidth - space, EditorGUIUtility.singleLineHeight);
                Rect weightRect = new Rect(rect.x + totalWidth - weightWidth, rect.y, weightWidth, EditorGUIUtility.singleLineHeight);

                if (volumeElement.objectReferenceValue != null && volumeElement.objectReferenceValue.GetType() == typeof(PointLightVolume)) {
                    var volume = (PointLightVolume)volumeElement.objectReferenceValue;
                    GUIContent icon; 
                    if(volume.Type == PointLightVolume.LightType.SpotLight) {
                        icon = EditorGUIUtility.IconContent("d_Spotlight Icon");
                        icon.tooltip = "Spot Light Volume";
                    } else if (volume.Type == PointLightVolume.LightType.AreaLight) {
                        icon = EditorGUIUtility.IconContent("d_AreaLight Icon");
                        icon.tooltip = "Area Light Volume";
                    } else {
                        icon = EditorGUIUtility.IconContent("d_Light Icon");
                        icon.tooltip = "Point Light Volume";
                    }
                    EditorGUI.LabelField(iconRect, icon);
                }

                EditorGUI.LabelField(volumeRect, volumeElement.objectReferenceValue != null ? volumeElement.objectReferenceValue.name : "None");

            };

            // On Moving element around
            _pointLightVolumesList.onReorderCallbackWithDetails = (ReorderableList list, int oldIndex, int newIndex) => {
                _lightVolumeSetup.SyncUdonScript();
            };

            // On Click on element
            _pointLightVolumesList.onSelectCallback = (ReorderableList list) => {
                SerializedProperty volumeElement = _pointLightVolumesProp.GetArrayElementAtIndex(list.index);
                PointLightVolume volume = volumeElement.objectReferenceValue as PointLightVolume;
                if (volume != null) EditorGUIUtility.PingObject(volume.gameObject);
            };

        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            if (_volumesProp.arraySize != _weightsProp.arraySize) {
                _weightsProp.arraySize = _volumesProp.arraySize;
                for (int i = 0; i < _volumesProp.arraySize; i++) {
                    if (i >= _weightsProp.arraySize - (_volumesProp.arraySize - _weightsProp.arraySize)) {
                        SerializedProperty weightElement = _weightsProp.GetArrayElementAtIndex(i);
                        weightElement.floatValue = i;
                    }
                }
                EditorUtility.SetDirty(target);
            }

            GUILayout.Space(10);

            if (LVUtils.IsInPrefabAsset(_lightVolumeSetup)) {
                EditorGUILayout.HelpBox("This component is part of a prefab asset (not in the scene). Please, use one that is placed on your scene!", MessageType.Warning);
                GUILayout.Space(10);
            } else if (_isMultipleInstancesError) {
                EditorGUILayout.HelpBox("Multiple Light Volume Managers detected in the scene. Please ensure only one is active to avoid unexpected behavior!", MessageType.Error);
                GUILayout.Space(10);
            }

            ulong atlasVoxelCount = 0;
            ulong vramBytes = 0;
            ulong bundleRawBytes = 0;
            if (_lightVolumeSetup.LightVolumeManager != null) {
                LightVolumeManager manager = _lightVolumeSetup.LightVolumeManager;
                Texture atlasBase = GetSerializedTexture(manager, "LightVolumeAtlasBase");
                Texture atlas = atlasBase != null ? atlasBase : GetSerializedTexture(manager, "LightVolumeAtlas");
                Texture3D atlas3D = atlas as Texture3D;
                if (atlas3D != null) {
                    atlasVoxelCount = (ulong)atlas3D.width * (ulong)atlas3D.height * (ulong)atlas3D.depth;
                    ulong atlasBytes = atlasVoxelCount * 8;
                    vramBytes += atlasBytes;
                    bundleRawBytes += atlasBytes;
                }

                RenderTexture atlasRT = atlas as RenderTexture;
                if (atlasRT != null) vramBytes += (ulong)atlasRT.width * (ulong)atlasRT.height * (ulong)Mathf.Max(atlasRT.volumeDepth, 1) * 8;

                RenderTexture customTexturesRT = GetSerializedTexture(manager, "CustomTextures") as RenderTexture;
                if (customTexturesRT != null) vramBytes += GetRenderTextureArrayBytes(customTexturesRT, 8UL);

                RenderTexture shadowTexturesRT = GetSerializedTexture(manager, "ShadowTextures") as RenderTexture;
                if (shadowTexturesRT != null) {
                    ulong shadowBytesPerPixel = _lightVolumeSetup.ShadowTextureFormat == LightVolumeSetup.ShadowTexturePrecision.Half ? 4UL : 8UL;
                    vramBytes += GetRenderTextureArrayBytes(shadowTexturesRT, shadowBytesPerPixel);
                }
            }
            ulong bakedShadowTextureBytes = GetBakedShadowTextureBytes();
            vramBytes += bakedShadowTextureBytes;
            bundleRawBytes += bakedShadowTextureBytes;

            GUILayout.Label(new GUIContent($"Data size in VRAM: {SizeInVRAM(vramBytes)} MB", "Includes only the Light Volume 3D atlas, cookie texture arrays, baked VSM shadow map assets and shadow map arrays."));
            GUILayout.Label(new GUIContent($"Data size in bundle: {SizeInBundle(bundleRawBytes)} MB (Approximately)", "Includes only the Light Volume 3D atlas and baked VSM shadow map assets."));

            GUILayout.Space(10);

            List<string> hiddenFields = new List<string>() { "m_Script", "LightVolumes", "PointLightVolumes", "LightVolumesWeights", "LightVolumeAtlas", "LightVolumeDataList", "LightVolumeManager", "_bakingModePrev", "BakingMode", "VolumeBitmask", "ProbeBitmask" };
            hiddenFields.Add("CookieResolution");
            hiddenFields.Add("BrightnessCutoff");
            hiddenFields.Add("ShadowResolution");
            hiddenFields.Add("ShadowTextureFormat");
            hiddenFields.Add("ShadowBleedReduction");
            hiddenFields.Add("AtlasPostProcessors");
            int plvCount = _lightVolumeSetup.PointLightVolumes.Count;
            bool isShadowBatchBake = false;
            for (int i = 0; i < plvCount; i++) {
                PointLightVolume pointLightVolume = _lightVolumeSetup.PointLightVolumes[i];
                if (pointLightVolume == null) continue;
                if (pointLightVolume.Shadows && pointLightVolume.RebakeShadows) {
                    isShadowBatchBake = true;
                    break;
                }
            }

            if (_lightVolumeSetup.LightVolumes.Count > 0)
                _lightVolumesList.DoLayoutList();

            if (plvCount > 0) {
                _pointLightVolumesList.DoLayoutList();
            }

            GUILayout.Space(-15);

            if (plvCount > 0) {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("CookieResolution"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ShadowResolution"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ShadowTextureFormat"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ShadowBleedReduction"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("BrightnessCutoff"));
            }

            EditorGUILayout.PropertyField(_bakingModeProp);
            bool isBakeryMode = (LightVolumeSetup.Baking)_bakingModeProp.enumValueIndex == LightVolumeSetup.Baking.Bakery;
#if BAKERY_INCLUDED
            if (isBakeryMode) DrawBakeryBitmasks();
#endif

            if (!isBakeryMode) {
                hiddenFields.Add("FixLightProbesL1");
                if (!_lightVolumeSetup.DilateInvalidProbes) {
                    hiddenFields.Add("DilationIterations");
                    hiddenFields.Add("DilationBackfaceBias");
                }
            } else {
                hiddenFields.Add("DilateInvalidProbes");
                hiddenFields.Add("DilationIterations");
                hiddenFields.Add("DilationBackfaceBias");
            }

            DrawPropertiesExcluding(serializedObject, hiddenFields.ToArray());

            GUILayout.Space(5);

            GUILayout.BeginHorizontal();

            if (_lightVolumeSetup.LightVolumes.Count > 0) {
                if (GUILayout.Button(new GUIContent("Pack Light Volumes", "Repacks Light Volumes 3D Atlas. Should be done manually if you added new volumes to your scene, or made some changes with their 3D textures."))) {
                    _lightVolumeSetup.GenerateAtlas();
                }
            }

            if (plvCount > 0) {
                GUILayout.Space(5);

                GUI.enabled = isShadowBatchBake;
                if (GUILayout.Button(new GUIContent("Bake Shadows", "Bakes shadow maps for all point, spot and area Light Volumes with Shadows and Rebake Shadows enabled."))) {
                    _lightVolumeSetup.BakeShadowMaps();
                }
                GUI.enabled = true;
            }

            GUILayout.EndHorizontal();

            if (serializedObject.ApplyModifiedProperties()) {
                _lightVolumeSetup.SyncUdonScript();
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }

        }

        // Real size in VRAM.
        private string SizeInVRAM(ulong byteCount) {
            double mb = byteCount / (double)(1024 * 1024);
            return mb.ToString("0.00");
        }

        // Approximate size in Asset bundle.
        private string SizeInBundle(ulong rawByteCount) {
            double mb = rawByteCount * BundleCompressionEstimate / (double)(1024 * 1024);
            return mb.ToString("0.00");
        }

        // Returns the estimated runtime memory for a render texture array.
        private static ulong GetRenderTextureArrayBytes(RenderTexture texture, ulong bytesPerPixel) {
            ulong bytes = (ulong)texture.width * (ulong)texture.height * (ulong)Mathf.Max(texture.volumeDepth, 1) * bytesPerPixel;
            return texture.useMipMap ? bytes * 4UL / 3UL : bytes;
        }

        // Returns the estimated raw byte size of unique baked VSM shadow texture assets included in the bundle.
        private ulong GetBakedShadowTextureBytes() {
            ulong bytes = 0;
            ulong bytesPerPixel = _lightVolumeSetup.ShadowTextureFormat == LightVolumeSetup.ShadowTexturePrecision.Half ? 4UL : 8UL;
            HashSet<Texture> countedTextures = new HashSet<Texture>();
            for (int i = 0; i < _lightVolumeSetup.PointLightVolumes.Count; i++) {
                PointLightVolume pointLightVolume = _lightVolumeSetup.PointLightVolumes[i];
                if (pointLightVolume == null || !pointLightVolume.Shadows) continue;
                Texture texture = pointLightVolume.GetShadowMapTexture();
                if (texture == null || texture is RenderTexture || !countedTextures.Add(texture)) continue;
                bytes += GetShadowTextureTexelCount(texture) * bytesPerPixel;
            }
            return bytes;
        }

        // Returns the texel count for baked shadow texture asset shapes.
        private static ulong GetShadowTextureTexelCount(Texture texture) {
            if (texture is Texture2DArray textureArray) return (ulong)textureArray.width * (ulong)textureArray.height * (ulong)textureArray.depth;
            if (texture is Cubemap) return (ulong)texture.width * (ulong)texture.height * 6UL;
            if (texture is Texture2D) return (ulong)texture.width * (ulong)texture.height;
            return 0UL;
        }

        // Reads texture references without touching UdonSharp proxy fields that may be stale after 2.x scene load.
        private static Texture GetSerializedTexture(Object target, string propertyName) {
            SerializedObject serializedTarget = new SerializedObject(target);
            SerializedProperty property = serializedTarget.FindProperty(propertyName);
            if (property == null) return null;

            try {
                return property.objectReferenceValue as Texture;
            } catch (MissingReferenceException) {
                return null;
            }
        }

        // Draws Bakery bitmasks using Bakery's zero-based mask popup labels.
        private void DrawBakeryBitmasks() {
            int prevValue = _volumeBitmaskProp.intValue;
            int newValue = EditorGUILayout.MaskField(EditorGUIUtility.TrTextContent(_volumeBitmaskProp.displayName, _volumeBitmaskProp.tooltip), prevValue, _bakeryBitmaskLabels);
            if (prevValue != newValue) _volumeBitmaskProp.intValue = newValue;

            prevValue = _probeBitmaskProp.intValue;
            newValue = EditorGUILayout.MaskField(EditorGUIUtility.TrTextContent(_probeBitmaskProp.displayName, _probeBitmaskProp.tooltip), prevValue, _bakeryBitmaskLabels);
            if (prevValue != newValue) _probeBitmaskProp.intValue = newValue;
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace UnityEditor
{
    internal class LightVolumeParticleLitShaderGUI : ShaderGUI
    {
        private static class Styles
        {
            // Custom properties
            public static GUIContent mainTexText = EditorGUIUtility.TrTextContent("Main Texture", "Main diffuse texture (RGB) and Alpha (A).");
            public static GUIContent tintText = EditorGUIUtility.TrTextContent("Tint", "Color multiplier for the Main Texture.");
            public static GUIContent normalMapText = EditorGUIUtility.TrTextContent("Normal Map", "Normal map (RGB).");
            public static GUIContent normalPowerText = EditorGUIUtility.TrTextContent("Normal Power", "Controls the intensity of the normal map.");
            public static GUIContent flipbookBlendingText = EditorGUIUtility.TrTextContent("Flipbook Blending", "Enables flipbook texture sheet animation blending (_FLIPBOOKBLENDING_ON).");
            public static GUIContent lightVolumesText = EditorGUIUtility.TrTextContent("Enable Light Volumes", "Enables effect interaction with light volumes (_LIGHTVOLUMES_ON).");
            public static GUIContent additiveOnlyText = EditorGUIUtility.TrTextContent("Additive Only", "Makes the particle purely additive (_ADDITIVEONLY_ON).");
            public static GUIContent enableSoftParticleText = EditorGUIUtility.TrTextContent("Enable Soft Particles", "Enables soft blending with scene geometry (_ENABLESOFTPARTICLE_ON).");
            public static GUIContent softParticleDistanceText = EditorGUIUtility.TrTextContent("Soft Particle Distance", "Distance for soft particle fading.");
            public static GUIContent cullingText = EditorGUIUtility.TrTextContent("Culling Mode", "Polygon culling mode.");

            // Titles
            public static GUIContent featureTogglesText = EditorGUIUtility.TrTextContent("Feature Toggles");
            public static GUIContent renderingOptionsText = EditorGUIUtility.TrTextContent("Rendering Options");

            // Vertex Stream related
            public static GUIContent requiredVertexStreamsText = EditorGUIUtility.TrTextContent("Required Vertex Streams");
            public static GUIContent streamPositionText = EditorGUIUtility.TrTextContent("Position (POSITION.xyz)");
            public static GUIContent streamNormalText = EditorGUIUtility.TrTextContent("Normal (NORMAL.xyz)");
            public static GUIContent streamColorText = EditorGUIUtility.TrTextContent("Color (COLOR.xyzw)");
           
            public static GUIContent streamUVText = EditorGUIUtility.TrTextContent("UV (TEXCOORD0.xy)");
            public static GUIContent streamUV2Text = EditorGUIUtility.TrTextContent("UV2 (TEXCOORD0.zw)");
            public static GUIContent streamAnimBlendText = EditorGUIUtility.TrTextContent("AnimBlend (TEXCOORD1.x)"); 
         
            public static GUIContent streamApplyToAllSystemsText = EditorGUIUtility.TrTextContent("Apply to Systems", "Apply the vertex stream layout to all Particle Systems using this material");
            public static string undoApplyCustomVertexStreams = "Apply custom vertex streams from material";
        }


        string REDSIMUrl = "https://github.com/REDSIM/VRCLightVolumes";
        string KyrowoURL = "https://x.com/KyrowoVRC";

        // Material Properties
        MaterialProperty mainTexProp;
        MaterialProperty colorProp;
        MaterialProperty bumpMapProp;
        MaterialProperty bumpScaleProp;
        MaterialProperty flipbookBlendingProp;
        MaterialProperty lightVolumesProp;
        MaterialProperty additiveOnlyProp;
        MaterialProperty enableSoftparticleProp;
        MaterialProperty softparticleDistanceProp;
        MaterialProperty cullingProp; 

        MaterialEditor m_MaterialEditor;
        List<ParticleSystemRenderer> m_RenderersUsingThisMaterial = new List<ParticleSystemRenderer>();
        bool m_FirstTimeApply = true;

        public void FindProperties(MaterialProperty[] props)
        {
            mainTexProp = FindProperty("_MainTex", props);
            colorProp = FindProperty("_Color", props);
            bumpMapProp = FindProperty("_BumpMap", props);
            bumpScaleProp = FindProperty("_BumpScale", props);
            flipbookBlendingProp = FindProperty("_FlipbookBlending", props);
            lightVolumesProp = FindProperty("_LightVolumes", props);
            additiveOnlyProp = FindProperty("_AdditiveOnly", props);
            enableSoftparticleProp = FindProperty("_EnableSoftparticle", props);
            softparticleDistanceProp = FindProperty("_SoftparticleDistance", props);
            cullingProp = FindProperty("_Culling", props);
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
        {
            FindProperties(props);
            m_MaterialEditor = materialEditor;
            Material material = materialEditor.target as Material;

            if (m_FirstTimeApply)
            {
                CacheRenderersUsingThisMaterial(material);
                m_FirstTimeApply = false;
            }

            ShaderPropertiesGUI(material);
        }

        public void Repaint()
        {
            if (m_MaterialEditor != null && m_MaterialEditor.target is Material currentMaterial)
            {
                bool needsRecache = true;
                if (m_RenderersUsingThisMaterial.Count > 0 && m_RenderersUsingThisMaterial[0] != null)
                {
                    if (m_RenderersUsingThisMaterial[0].sharedMaterial == currentMaterial ||
                        m_RenderersUsingThisMaterial[0].sharedMaterials.Contains(currentMaterial))
                    {
                        needsRecache = false;
                    }
                }

                if (needsRecache)
                {
                    CacheRenderersUsingThisMaterial(currentMaterial);
                }
            }
        }


        public void ShaderPropertiesGUI(Material material)
        {
            EditorGUIUtility.labelWidth = 0f;

            EditorGUI.BeginChangeCheck();
            {
                EditorGUILayout.BeginVertical(GUI.skin.box); // GUI.skin.box provides a default boxed style

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Light Volume Shader made by RED_SIM: ", GUILayout.ExpandWidth(false));
                if(EditorGUILayout.LinkButton(REDSIMUrl))
                {
                    Application.OpenURL(REDSIMUrl);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Particle Shader made by KyrowoVRC: ", GUILayout.ExpandWidth(false));
                if (EditorGUILayout.LinkButton(KyrowoURL))
                {
                    Application.OpenURL(KyrowoURL);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

                m_MaterialEditor.TextureProperty(mainTexProp,"Main Texture",false);
                m_MaterialEditor.ColorProperty(colorProp, "Tint");


                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

                m_MaterialEditor.TextureProperty(bumpMapProp, "Normal Map",false);
                m_MaterialEditor.ShaderProperty(bumpScaleProp, Styles.normalPowerText);

                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                GUILayout.Label(Styles.featureTogglesText, EditorStyles.boldLabel);

                m_MaterialEditor.ShaderProperty(flipbookBlendingProp, Styles.flipbookBlendingText);
                m_MaterialEditor.ShaderProperty(lightVolumesProp, Styles.lightVolumesText);
                m_MaterialEditor.ShaderProperty(additiveOnlyProp, Styles.additiveOnlyText);

                m_MaterialEditor.ShaderProperty(enableSoftparticleProp, Styles.enableSoftParticleText);
                if (material.IsKeywordEnabled("_ENABLESOFTPARTICLE_ON"))
                {
                    EditorGUI.indentLevel++;
                    m_MaterialEditor.ShaderProperty(softparticleDistanceProp, Styles.softParticleDistanceText);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space();
                GUILayout.Label(Styles.renderingOptionsText, EditorStyles.boldLabel);
                m_MaterialEditor.ShaderProperty(cullingProp, Styles.cullingText);
            }
            if (EditorGUI.EndChangeCheck())
            {
                m_MaterialEditor.Repaint();
            }

            EditorGUILayout.Space();
            GUILayout.Label(Styles.requiredVertexStreamsText, EditorStyles.boldLabel);
            DoVertexStreamsArea(material);
        }

        void DoVertexStreamsArea(Material material)
        {
            bool isFlipbookBlendingOn = material.IsKeywordEnabled("_FLIPBOOKBLENDING_ON");

            GUILayout.Label(Styles.streamPositionText, EditorStyles.label);
            GUILayout.Label(Styles.streamNormalText, EditorStyles.label);
            GUILayout.Label(Styles.streamColorText, EditorStyles.label);
            GUILayout.Label(Styles.streamUVText, EditorStyles.label);

            if (isFlipbookBlendingOn)
            {
                GUILayout.Label(Styles.streamUV2Text, EditorStyles.label);
                GUILayout.Label(Styles.streamAnimBlendText, EditorStyles.label);
            }

            List<ParticleSystemVertexStream> streamsToApply = new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV
            };

            if (isFlipbookBlendingOn)
            {
                streamsToApply.Add(ParticleSystemVertexStream.UV2);
                streamsToApply.Add(ParticleSystemVertexStream.AnimBlend);
            }

            if (GUILayout.Button(Styles.streamApplyToAllSystemsText, EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                CacheRenderersUsingThisMaterial(material);

                var validRenderers = m_RenderersUsingThisMaterial.Where(r => r != null).ToArray();
                if (validRenderers.Length > 0)
                {
                    Undo.RecordObjects(validRenderers, Styles.undoApplyCustomVertexStreams);
                    foreach (ParticleSystemRenderer renderer in validRenderers)
                    {
                        renderer.SetActiveVertexStreams(streamsToApply);
                        EditorUtility.SetDirty(renderer);
                    }
                    Debug.Log($"Applied vertex streams to {validRenderers.Length} Particle System(s) using material '{material.name}'. Flipbook Blending: {isFlipbookBlendingOn}");
                }
                else
                {
                    Debug.Log($"No Particle Systems found using material '{material.name}' to apply streams to.");
                }
            }

            string warnings = "";
            List<ParticleSystemVertexStream> currentRendererStreams = new List<ParticleSystemVertexStream>();

            foreach (ParticleSystemRenderer renderer in m_RenderersUsingThisMaterial)
            {
                if (renderer == null) continue;

                renderer.GetActiveVertexStreams(currentRendererStreams);
                bool streamsMatchTargetForWarning;

                if (isFlipbookBlendingOn)
                {
                    streamsMatchTargetForWarning = currentRendererStreams.SequenceEqual(streamsToApply);
                }
                else
                {
                    if (currentRendererStreams.Count == 0) // No custom streams is OK
                    {
                        streamsMatchTargetForWarning = true;
                    }
                    else // Custom streams active, must match the non-flipbook set
                    {
                        streamsMatchTargetForWarning = currentRendererStreams.SequenceEqual(streamsToApply);
                    }
                }

                if (!streamsMatchTargetForWarning)
                {
                    warnings += "  " + renderer.gameObject.name + "\n";
                }
            }

            if (warnings != "")
            {
                EditorGUILayout.HelpBox("The following Particle System Renderers are using this material with incorrect Vertex Streams:\n" + warnings + "Use the Apply to Systems button to fix this.", MessageType.Warning, true);
            }
            EditorGUILayout.Space();
        }

        void CacheRenderersUsingThisMaterial(Material material)
        {
            m_RenderersUsingThisMaterial.Clear();
            if (material == null) return;

            ParticleSystemRenderer[] renderersInScene = UnityEngine.Object.FindObjectsOfType<ParticleSystemRenderer>(true);

            foreach (ParticleSystemRenderer renderer in renderersInScene)
            {
                if (renderer.sharedMaterial == material)
                {
                    m_RenderersUsingThisMaterial.Add(renderer);
                }
                else if (renderer.sharedMaterials != null && renderer.sharedMaterials.Contains(material))
                {
                    if (!m_RenderersUsingThisMaterial.Contains(renderer))
                    {
                        m_RenderersUsingThisMaterial.Add(renderer);
                    }
                }
            }
        }

        public void OnSelectionChange(UnityEngine.Object[] targets)
        {
            if (targets.Length > 0 && targets[0] is Material currentMaterial)
            {
                CacheRenderersUsingThisMaterial(currentMaterial);
                m_FirstTimeApply = false; 
            }
            else
            {
                m_RenderersUsingThisMaterial.Clear();
            }
        }
    }
}
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    // Synchronizes Point Light Volume authoring state for inspectors and other editor workflows.
    internal static class PointLightVolumeEditorUtility {
        internal const int CustomTexturesChanged = 1;
        internal const int ShadowTexturesChanged = 2;

        // Unity's own texture inspector uses this source-data flag, but Unity 2022.3 keeps the accessor internal even though SourceTextureInformation.hdr is part of the editor API.
        private static readonly MethodInfo _isSourceTextureHdrMethod = typeof(TextureImporter).GetMethod("IsSourceTextureHDR", BindingFlags.Instance | BindingFlags.NonPublic);

        // Applies derived data once and copies the proxy without rebuilding Manager-owned caches.
        internal static int Sync(PointLightVolumeInstance pointLightVolume, bool recordUndo = false, bool notifyManager = true, bool forceCustomTexturesChanged = false) {
            if (pointLightVolume == null) return 0;

            bool customTexturesChanged = forceCustomTexturesChanged || pointLightVolume.HasEditorCustomTextureChanges();
            bool shadowTexturesChanged = pointLightVolume.HasEditorShadowTextureChanges();
            if (recordUndo) Undo.RecordObject(pointLightVolume, "Sync Point Light Volume");

            if (customTexturesChanged) EnsureProjectionTextureImportSettings(pointLightVolume);

            pointLightVolume.EditorApplyAuthoringData(customTexturesChanged, shadowTexturesChanged, notifyManager);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(pointLightVolume);

            int changes = (customTexturesChanged ? CustomTexturesChanged : 0) | (shadowTexturesChanged ? ShadowTexturesChanged : 0);
            return changes;
        }

        // Ensures only the Android import of an HDR source selected by this light resolves to a linear floating-point format. Default settings used by every other platform remain untouched.
        internal static bool EnsureProjectionTextureImportSettings(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return false;

            Texture texture = pointLightVolume.GetCustomTexture();
            if (texture == null || texture is RenderTexture) return false;

            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path)) return false;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || !IsHdrSource(importer)) return false;

            TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
            TextureImporterFormat resolvedAndroidFormat = androidSettings.overridden ? androidSettings.format : importer.GetAutomaticFormat("Android");
            if (IsHdrTextureFormat(resolvedAndroidFormat)) return false;

            androidSettings.overridden = true;
            androidSettings.format = TextureImporterFormat.RGBAHalf;
            androidSettings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(androidSettings);
            importer.SaveAndReimport();
            return true;
        }

        private static bool IsHdrSource(TextureImporter importer) {
            if (_isSourceTextureHdrMethod == null) return false;
            try {
                return (bool)_isSourceTextureHdrMethod.Invoke(importer, null);
            } catch (TargetInvocationException) {
                return false;
            }
        }

        private static bool IsHdrTextureFormat(TextureImporterFormat format) {
            switch (format) {
                case TextureImporterFormat.RHalf:
                case TextureImporterFormat.RGHalf:
                case TextureImporterFormat.RGBAHalf:
                case TextureImporterFormat.RFloat:
                case TextureImporterFormat.RGFloat:
                case TextureImporterFormat.RGBAFloat:
                case TextureImporterFormat.RGB9E5:
                case TextureImporterFormat.ASTC_HDR_4x4:
                case TextureImporterFormat.ASTC_HDR_5x5:
                case TextureImporterFormat.ASTC_HDR_6x6:
                case TextureImporterFormat.ASTC_HDR_8x8:
                case TextureImporterFormat.ASTC_HDR_10x10:
                case TextureImporterFormat.ASTC_HDR_12x12:
                    return true;
                default:
                    return false;
            }
        }
    }
}

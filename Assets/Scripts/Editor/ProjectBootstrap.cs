using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Pitch.Game;

namespace Pitch.EditorTools
{
    /// <summary>
    /// Reproducible project setup: URP Forward+ asset, Android player settings,
    /// the single boot scene and the generated texture import rules. Everything
    /// is created from code so a clean clone has no binary scene dependency.
    /// </summary>
    public static class ProjectBootstrap
    {
        public const string PipelinePath = "Assets/Settings/PitchURP.asset";
        public const string RendererPath = "Assets/Settings/PitchURPRenderer.asset";
        public const string BootScenePath = "Assets/Scenes/Boot.unity";

        [MenuItem("Pitch/1. Set up project")]
        public static void EnsureProject()
        {
            EnsureFolders();
            EnsurePipeline();
            EnsureBootScene();
            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Pitch] project setup complete (URP, ARM64, IL2CPP, boot scene)");
        }

        private static void EnsureFolders()
        {
            foreach (var folder in new[] { "Assets/Settings", "Assets/Scenes", "Assets/Resources", "Assets/Resources/Generated", "Assets/Resources/Generated/Textures", "Assets/Generated" })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/') ?? "Assets";
                string name = Path.GetFileName(folder);
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static void EnsurePipeline()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (existing == null)
            {
                var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                rendererData.name = "Pitch URP Renderer";
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                pipeline.name = "Pitch URP";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                existing = pipeline;

                var serialized = new SerializedObject(existing);
                SetInt(serialized, "m_MSAA", 4);
                SetInt(serialized, "m_RenderScale", 1);
                SetInt(serialized, "m_ShadowDistance", 70);
                SetInt(serialized, "m_ShadowCascadeCount", 3);
                SetInt(serialized, "m_MainLightShadowmapResolution", 2048);
                SetInt(serialized, "m_AdditionalLightsRenderingMode", 1);
                SetInt(serialized, "m_AdditionalLightsPerObjectLimit", 4);
                SetBool(serialized, "m_SupportsHDR", true);
                SetBool(serialized, "m_SupportsCameraDepthTexture", true);
                SetBool(serialized, "m_SupportsCameraOpaqueTexture", false);
                SetBool(serialized, "m_SupportsDynamicBatching", true);
                SetBool(serialized, "m_UseSRPBatcher", true);
                SetBool(serialized, "m_RequireDepthTexture", false);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[Pitch] created URP Forward+ asset");
            }

            GraphicsSettings.defaultRenderPipeline = existing;
            QualitySettings.renderPipeline = existing;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = existing;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
        }

        private static void EnsureBootScene()
        {
            if (File.Exists(BootScenePath))
            {
                SetBuildScenes();
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Matchday Eleven");
            root.AddComponent<GameBootstrap>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, BootScenePath);
            SetBuildScenes();
            Debug.Log("[Pitch] created boot scene");
        }

        private static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, true),
            };
        }

        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Northport Interactive";
            PlayerSettings.productName = "Matchday Eleven";
            PlayerSettings.bundleVersion = "2.0.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.northport.matchdayeleven");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.graphicsJobs = false;
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.High);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                GraphicsDeviceType.Vulkan,
                GraphicsDeviceType.OpenGLES3,
            });
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)26;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35;
            PlayerSettings.Android.bundleVersionCode = 20000;
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.Android.forceSDCardPermission = false;
            PlayerSettings.Android.androidIsGame = true;
            PlayerSettings.Android.blitType = AndroidBlitType.Never;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            // Legacy input is required by the touch fallback; keep the serialized
            // handler at "Both" (2) if the Input System package is installed.
            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (assets.Length > 0)
                {
                    var so = new SerializedObject(assets[0]);
                    var handler = so.FindProperty("activeInputHandler");
                    if (handler != null)
                    {
                        handler.intValue = 2;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Pitch] could not set input handler: {exception.Message}");
            }
        }

        private static void SetInt(SerializedObject so, string name, int value)
        {
            var property = so.FindProperty(name);
            if (property != null) property.intValue = value;
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            var property = so.FindProperty(name);
            if (property != null) property.boolValue = value;
        }
    }

    /// <summary>
    /// Import rules for generated PBR textures: 4K max, ASTC 6x6 on Android,
    /// mipmaps and streaming enabled. ASTC keeps the APK in the 200-300 MB band
    /// without runtime decode cost.
    /// </summary>
    public sealed class GeneratedTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("Resources/Generated/Textures")) return;
            var importer = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            bool normal = file.Contains("normal");
            bool data = normal || file.Contains("orm") || file.Contains("mask");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !data;
            importer.maxTextureSize = 4096;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;

            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 4096;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 100;
            importer.SetPlatformTextureSettings(android);
        }
    }
}

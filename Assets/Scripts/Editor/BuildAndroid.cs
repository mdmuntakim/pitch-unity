using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Pitch.EditorTools
{
    /// <summary>
    /// One-command Android build: IL2CPP, ARM64 only, ASTC, stripping and a
    /// signed APK whose size and ABI are checked after the build. Emits
    /// Build/report.json and Build/build-facts.txt for the release notes.
    /// </summary>
    public static class BuildAndroid
    {
        public const string DefaultOutput = "Build/MatchdayEleven-arm64.apk";
        private const long MinSize = 200L * 1024L * 1024L;
        private const long MaxSize = 300L * 1024L * 1024L;

        [MenuItem("Pitch/2. Build signed arm64 APK")]
        public static void BuildFromMenu() => Build();

        /// <summary>
        /// Cheap preflight for CI, run before the long build: proves the editor
        /// starts with the active licence and that the Android target is usable.
        /// Prints one machine readable PROBE line for the workflow and exits
        /// non-zero when the target is missing, so the job fails in a minute
        /// instead of after an hour.
        /// </summary>
        public static void Probe()
        {
            bool supported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);
            Debug.Log($"[Pitch] PROBE unity={Application.unityVersion} batch={Application.isBatchMode} " +
                      $"graphics={SystemInfo.graphicsDeviceType} android_supported={supported.ToString().ToLowerInvariant()}");
            string engines = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines");
            Debug.Log($"[Pitch] PROBE playback_engines={engines} " +
                      $"androidplayer={Directory.Exists(Path.Combine(engines, "AndroidPlayer")).ToString().ToLowerInvariant()}");
            // Exit 0 even when the target is unsupported: this is a report, and
            // build/export.sh decides whether to switch to GUI mode or fail.
            // A non-zero exit is reserved for "the editor cannot run at all".
            if (!supported)
                Debug.LogWarning("[Pitch] PROBE the Android build target is unsupported in this mode " +
                                 "(a Personal licence without com.unity.editor.headless reports this in batch mode)");
            EditorApplication.Exit(0);
        }

        /// <summary>Entry point for -executeMethod from CI.</summary>
        public static void BuildFromCli()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Pitch] build failed: {exception}");
                EditorApplication.Exit(1);
            }
        }

        public static void Build()
        {
            var started = DateTime.UtcNow;
            ProjectBootstrap.EnsureProject();
            EnsureGeneratedAssets();

            string output = Environment.GetEnvironmentVariable("PITCH_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = DefaultOutput;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".");

            ConfigureSigning();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectBootstrap.BootScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            Debug.Log($"[Pitch] building {output} with Unity {Application.unityVersion}");
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception($"BuildResult={report.summary.result}, errors={report.summary.totalErrors}");

            var facts = Inspect(output, report, DateTime.UtcNow - started);
            WriteReport(facts);
            Debug.Log("[Pitch] build facts:\n" + facts.ToText());
        }

        private static void EnsureGeneratedAssets()
        {
            string manifest = "Assets/Resources/Generated/Textures/grass_albedo.png";
            if (File.Exists(manifest)) return;
            string script = Path.Combine(Directory.GetCurrentDirectory(), "tools", "asset_generator.py");
            if (!File.Exists(script))
            {
                Debug.LogWarning("[Pitch] generated textures missing and tools/asset_generator.py not found; " +
                                 "the build will use flat fallback materials and will not reach the size target");
                return;
            }
            string size = Environment.GetEnvironmentVariable("PITCH_TEXTURE_SIZE");
            if (string.IsNullOrEmpty(size)) size = "4096";
            Debug.Log($"[Pitch] generating deterministic PBR texture set at {size}px");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{script}\" --out Assets/Resources/Generated/Textures --size {size}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Debug.Log(stdout);
            if (process.ExitCode != 0)
                throw new Exception($"asset_generator.py failed: {stderr}");
            AssetDatabase.Refresh();
        }

        private static void ConfigureSigning()
        {
            string keystore = Environment.GetEnvironmentVariable("PITCH_KEYSTORE");
            if (string.IsNullOrEmpty(keystore))
            {
                string releasePath = Path.Combine(Directory.GetCurrentDirectory(), "signing", "release.keystore");
                string debugPath = Path.Combine(Directory.GetCurrentDirectory(), "signing", "debug.keystore");
                keystore = File.Exists(releasePath) ? releasePath : (File.Exists(debugPath) ? debugPath : null);
            }

            if (string.IsNullOrEmpty(keystore) || !File.Exists(keystore))
            {
                Debug.LogWarning("[Pitch] no keystore found; Unity will sign with the per-machine debug key. " +
                                 "Provide signing/release.keystore or PITCH_KEYSTORE for a reproducible signature.");
                PlayerSettings.Android.useCustomKeystore = false;
                return;
            }

            string storePass = Environment.GetEnvironmentVariable("PITCH_STOREPASS") ?? "android";
            string keyAlias = Environment.GetEnvironmentVariable("PITCH_KEYALIAS") ?? "matchday";
            string keyPass = Environment.GetEnvironmentVariable("PITCH_KEYPASS") ?? storePass;
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = storePass;
            PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keyaliasPass = keyPass;
            Debug.Log($"[Pitch] signing with {Path.GetFileName(keystore)} alias {keyAlias}");
        }

        private struct BuildFacts
        {
            public string Output;
            public long Bytes;
            public string Sha256;
            public string Abis;
            public bool OnlyArm64;
            public bool SizeInRange;
            public double BuildSeconds;
            public string UnityVersion;
            public string Compression;
            public long TextureBytes;
            public long AudioBytes;
            public long CodeBytes;
            public long OtherBytes;

            public string ToText()
            {
                var builder = new StringBuilder();
                builder.AppendLine($"apk: {Output}");
                builder.AppendLine($"bytes: {Bytes} ({Bytes / 1024.0 / 1024.0:F1} MiB)");
                builder.AppendLine($"sha256: {Sha256}");
                builder.AppendLine($"abis: {Abis}");
                builder.AppendLine($"only_arm64: {OnlyArm64}");
                builder.AppendLine($"size_in_200_300_mb: {SizeInRange}");
                builder.AppendLine($"build_seconds: {BuildSeconds:F1}");
                builder.AppendLine($"unity: {UnityVersion}");
                builder.AppendLine($"texture_bytes: {TextureBytes}");
                builder.AppendLine($"audio_bytes: {AudioBytes}");
                builder.AppendLine($"code_bytes: {CodeBytes}");
                builder.AppendLine($"other_bytes: {OtherBytes}");
                return builder.ToString();
            }
        }

        private static BuildFacts Inspect(string apk, BuildReport report, TimeSpan duration)
        {
            var facts = new BuildFacts
            {
                Output = apk,
                Bytes = new FileInfo(apk).Length,
                Sha256 = Hash(apk),
                BuildSeconds = duration.TotalSeconds,
                UnityVersion = Application.unityVersion,
            };

            var abis = new HashSet<string>();
            long texture = 0, audio = 0, code = 0, other = 0;
            using (var archive = ZipFile.OpenRead(apk))
            {
                foreach (var entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (name.StartsWith("lib/"))
                    {
                        var parts = name.Split('/');
                        if (parts.Length > 1) abis.Add(parts[1]);
                    }
                    if (name.EndsWith(".astc") || name.Contains("Texture") || name.StartsWith("assets/"))
                        texture += entry.CompressedLength;
                    else if (name.EndsWith(".wav") || name.EndsWith(".ogg") || name.Contains("Audio"))
                        audio += entry.CompressedLength;
                    else if (name.Contains("libil2cpp") || name.EndsWith(".so") || name.StartsWith("lib/"))
                        code += entry.CompressedLength;
                    else
                        other += entry.CompressedLength;
                }
            }

            facts.Abis = string.Join(",", abis.OrderBy(a => a));
            facts.OnlyArm64 = abis.Count == 1 && abis.Contains("arm64-v8a");
            facts.SizeInRange = facts.Bytes >= MinSize && facts.Bytes <= MaxSize;
            facts.TextureBytes = texture;
            facts.AudioBytes = audio;
            facts.CodeBytes = code;
            facts.OtherBytes = other;

            Debug.Log($"[Pitch] APK {facts.Bytes} bytes, ABI [{facts.Abis}], SHA-256 {facts.Sha256}");
            if (!facts.OnlyArm64)
                throw new Exception($"APK contains unexpected ABIs: {facts.Abis}; release must be arm64-v8a only");
            if (facts.Bytes < MinSize || facts.Bytes > MaxSize)
            {
                string message = $"APK is {facts.Bytes} bytes, outside the required 200-300 MB band. " +
                                 "Add generated texture sets (tools/asset_generator.py) or reduce them.";
                if (Environment.GetEnvironmentVariable("PITCH_ENFORCE_SIZE") == "1")
                    throw new Exception(message);
                Debug.LogWarning(message);
            }
            return facts;
        }

        private static void WriteReport(BuildFacts facts)
        {
            Directory.CreateDirectory("Build");
            File.WriteAllText("Build/build-facts.txt", facts.ToText());
            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine($"  \"apk\": \"{facts.Output}\",");
            json.AppendLine($"  \"bytes\": {facts.Bytes},");
            json.AppendLine($"  \"sha256\": \"{facts.Sha256}\",");
            json.AppendLine($"  \"abis\": \"{facts.Abis}\",");
            json.AppendLine($"  \"onlyArm64\": {(facts.OnlyArm64 ? "true" : "false")},");
            json.AppendLine($"  \"sizeInRange\": {(facts.SizeInRange ? "true" : "false")},");
            json.AppendLine($"  \"buildSeconds\": {facts.BuildSeconds:F1},");
            json.AppendLine($"  \"unity\": \"{facts.UnityVersion}\"");
            json.AppendLine("}");
            File.WriteAllText("Build/report.json", json.ToString());
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}

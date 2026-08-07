using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using Unity.Android.Types;
using UnityEngine;
using BuildReportResult = UnityEditor.Build.Reporting.BuildResult;

namespace UnicoStudio.BuildSystem.Editor
{
    public sealed class PlayerBuildStage : IBuildStage
    {
        public string Name => "Player build";

        public void Execute(BuildContext ctx)
        {
            var req = ctx.Request;
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var version = PlayerSettings.bundleVersion;
            var buildDate = DateTime.Now;
            var code = req.Platform == BuildPlatform.Android
                ? PlayerSettings.Android.bundleVersionCode
                : int.TryParse(PlayerSettings.iOS.buildNumber, out var n) ? n : 0;

            var folder = string.IsNullOrWhiteSpace(req.OutputFolder)
                ? Path.Combine("Builds", req.Platform.ToString(), req.Kind.ToString())
                : req.OutputFolder;
            Directory.CreateDirectory(folder);

            if (req.Platform == BuildPlatform.iOS)
            {
                // iOS output is an Xcode project folder — no file extension.
                var outPath = Path.Combine(folder,
                    BuildArtifactNaming.FileName(BuildArtifactNaming.ProductPrefix, req.Label, version, code, buildDate, req.Kind, ""));
                RunBuild(ctx, scenes, BuildTarget.iOS, outPath, aab: false);
                return;
            }

            // Android: APK then AAB per requested outputs.
            if ((req.Outputs & OutputKind.Apk) != 0)
            {
                var apk = Path.Combine(folder,
                    BuildArtifactNaming.FileName(BuildArtifactNaming.ProductPrefix, req.Label, version, code, buildDate, req.Kind, "apk"));
                RunBuild(ctx, scenes, BuildTarget.Android, apk, aab: false);
            }
            if ((req.Outputs & OutputKind.Aab) != 0)
            {
                var aab = Path.Combine(folder,
                    BuildArtifactNaming.FileName(BuildArtifactNaming.ProductPrefix, req.Label, version, code, buildDate, req.Kind, "aab"));
                RunBuild(ctx, scenes, BuildTarget.Android, aab, aab: true);
            }
        }

        // Scripted builds take compression ONLY from BuildOptions flags — the Build Settings
        // window's Compression Method does not apply to BuildPipeline.BuildPlayer calls. Without
        // these flags every panel build would silently ship with Default instead of LZ4HC.
        internal static BuildOptions OptionsFor(CompressionKind compression) =>
            compression switch
            {
                CompressionKind.LZ4HC => BuildOptions.CompressWithLz4HC,
                CompressionKind.LZ4 => BuildOptions.CompressWithLz4,
                _ => BuildOptions.None
            };

        private void RunBuild(BuildContext ctx, string[] scenes, BuildTarget target, string outPath, bool aab)
        {
            if (target == BuildTarget.Android)
            {
                EditorUserBuildSettings.buildAppBundle = aab;
                // Debug symbols (.symbols.zip) only for the AAB (Play Store artifact); APK stays lean.
                UserBuildSettings.DebugSymbols.level = aab ? DebugSymbolLevel.SymbolTable : DebugSymbolLevel.None;
            }

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outPath,
                target = target,
                options = OptionsFor(ctx.Request.Compression),
                extraScriptingDefines = ctx.ExtraScriptingDefines.ToArray(),
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var ok = report.summary.result == BuildReportResult.Succeeded;
            ctx.AddStep($"{(aab ? "AAB (+symbols)" : target == BuildTarget.iOS ? "Xcode" : "APK")} " +
                        (ok ? $"-> {outPath}" : $"FAILED ({report.summary.result})"));
            if (!ok) throw new BuildFailedException($"Player build failed: {report.summary.result}");

            ctx.AddArtifact(outPath, aab ? ArtifactKind.Aab
                : target == BuildTarget.iOS ? ArtifactKind.XcodeProject : ArtifactKind.Apk);
            if (aab)
            {
                // The symbols archive must be hand-uploaded to Play Console (and Crashlytics), so
                // it belongs on the artifact list. ASK THE OWNER: the build's own report lists it
                // (measured on a real 6000.0.62f1 AAB: exactly one entry, role "Symbols", at its
                // output-folder path) — this replaces the *.symbols.zip glob over the output
                // folder, which reconstructed Unity's naming and needed an mtime filter to avoid
                // adopting an earlier build's archive.
                foreach (var symbols in SelectSymbolsArchives(
                             report.GetFiles().Select(f => (f.role, f.path)),
                             Directory.GetCurrentDirectory()))
                    ctx.AddArtifact(symbols, ArtifactKind.SymbolsZip);
            }
        }

        // Pure core (unit-tested): which report entries are the uploadable symbols archive, as
        // project-relative paths ('/' normalized) matching every other artifact this stage adds.
        // The role string is Unity's own classification (measured); the suffix test is a GUARD on
        // API-returned values — not a reconstruction — so a future Unity classifying something
        // else under "Symbols" is skipped rather than mislabeled. A path outside the project
        // root is kept absolute: wrong-looking but truthful beats prettified and wrong.
        internal static List<string> SelectSymbolsArchives(
            IEnumerable<(string role, string path)> reportFiles, string projectRoot)
        {
            var root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            var picked = new List<string>();
            foreach (var (role, path) in reportFiles)
            {
                if (role != "Symbols") continue;
                if (!path.EndsWith(".symbols.zip", StringComparison.OrdinalIgnoreCase)) continue;
                var p = path.Replace('\\', '/');
                picked.Add(p.StartsWith(root, StringComparison.Ordinal) ? p.Substring(root.Length) : p);
            }
            return picked;
        }
    }
}

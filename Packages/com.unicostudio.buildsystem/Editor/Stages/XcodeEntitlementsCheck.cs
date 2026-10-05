using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace UnicoStudio.BuildSystem.Editor
{
    // Post-build check on an iOS export: every CODE_SIGN_ENTITLEMENTS value in the Xcode project
    // must resolve to an existing file the way Xcode resolves it — relative to the folder that
    // holds the .xcodeproj (SRCROOT).
    //
    // Measured twice on real exports (BT5 2026-09-15, BTAS 1.6.8 2026-10-05): Unity's build graph
    // stores the value relative to the UNITY project; a capability post-processor that reads it
    // and hands it to ProjectCapabilityManager gets it resolved against the Xcode folder (path
    // doubled, DirectoryNotFoundException logged, capability silently not written) and the export
    // keeps the Unity-relative value. The Unity build reported success both times; Xcode Archive
    // then failed with "could not be opened". The fix lives in the host post-processor (pass the
    // bare file name) — this check only makes the next occurrence fail HERE, in Unity, instead of
    // at Archive. It deliberately does NOT rewrite the value: a broken value means a writer failed,
    // and silently repairing the path would ship an export missing that writer's capability.
    //
    // Parses the pbxproj text directly (no UnityEditor.iOS.Xcode dependency), so Android-only
    // hosts without iOS Build Support still compile the package.
    internal static class XcodeEntitlementsCheck
    {
        // CODE_SIGN_ENTITLEMENTS = Name.entitlements;   or   = "Builds/iOS/.../Name.entitlements";
        private static readonly Regex s_setting = new Regex(
            @"\bCODE_SIGN_ENTITLEMENTS\s*=\s*(?:""((?:[^""\\]|\\.)*)""|([^;\s]+))\s*;",
            RegexOptions.Compiled);

        private static readonly Regex s_srcRoot = new Regex(@"^\$[\(\{]SRCROOT[\)\}]/?", RegexOptions.Compiled);

        // Pure core (unit-tested). Returns null when every value resolves (or there is none), else
        // a message naming each offending value and where Xcode will look for it. Values built from
        // other build settings ($(PROJECT_DIR), $(TARGET_NAME), ...) cannot be resolved without
        // Xcode and are skipped rather than failed.
        internal static string FindProblem(string pbxprojText, string xcodeRoot, Func<string, bool> fileExists)
        {
            var root = xcodeRoot.Replace('\\', '/').TrimEnd('/');
            var problems = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match m in s_setting.Matches(pbxprojText ?? ""))
            {
                var value = m.Groups[1].Success ? m.Groups[1].Value.Replace("\\\"", "\"") : m.Groups[2].Value;
                if (value.Length == 0 || !seen.Add(value)) continue;

                var path = s_srcRoot.Replace(value, "");
                if (path.Contains("$")) continue;

                var resolved = path.StartsWith("/", StringComparison.Ordinal) ? path : root + "/" + path;
                if (!fileExists(resolved))
                    problems.Add($"'{value}' (Xcode resolves it to {resolved})");
            }

            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        // Disk wrapper used by PlayerBuildStage. Returns the problem message (null = fine) and the
        // entitlements values found, for the step log. Finding no .xcodeproj is not this check's
        // failure to report — BuildPlayer already said it succeeded — so it yields null.
        internal static string Run(string xcodeOutputFolder, out bool hasEntitlements)
        {
            hasEntitlements = false;
            var root = Path.GetFullPath(xcodeOutputFolder);
            if (!Directory.Exists(root)) return null;

            string problem = null;
            foreach (var xcodeproj in Directory.GetDirectories(root, "*.xcodeproj", SearchOption.TopDirectoryOnly))
            {
                var pbx = Path.Combine(xcodeproj, "project.pbxproj");
                if (!File.Exists(pbx)) continue;
                var text = File.ReadAllText(pbx);
                hasEntitlements |= s_setting.IsMatch(text);
                var p = FindProblem(text, root, File.Exists);
                if (p != null) problem = problem == null ? p : problem + "; " + p;
            }
            return problem;
        }
    }
}

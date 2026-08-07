using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    // Headless entry point:
    //   Unity -batchmode -projectPath <proj> -buildTarget android \
    //     -executeMethod UnicoStudio.BuildSystem.Editor.UnicoBuildCli.Build \
    //     -platform Android -kind Release -versionName 1.6.7 -bumpBuildCode -outputs Aab \
    //     -resultFile Builds/result.json -strictWarnings -timeoutMinutes 60
    // NEVER pass -quit: the reload chain needs the editor alive; the watcher exits the process.
    // CI defaults deliberately differ from the panel: nothing bumps unless explicitly flagged.
    // Flag names deliberately avoid Unity's own argument namespace (-version is consumed by Unity itself and short-circuits the launch).
    public static class UnicoBuildCli
    {
        [Serializable]
        internal sealed class CliOptions
        {
            public BuildRequest Request = new();
            public string ResultFile = "Builds/result.json";
            public bool StrictWarnings;
            public int TimeoutMinutes = 90;
            public string Error = "";           // non-empty = parse failed (exit 2)
        }

        public static void Build()
        {
            // Interactive editors must never be terminated (or have orphan recovery disarmed /
            // keystore settings mutated) by a stray invocation — reflection tooling can reach any
            // public static. The CI contract requires -batchmode anyway.
            if (!Application.isBatchMode)
            {
                Debug.LogError("[Build] UnicoBuildCli.Build requires -batchmode; ignored.");
                return;
            }

            var options = Parse(Environment.GetCommandLineArgs());
            if (!string.IsNullOrEmpty(options.Error))
            {
                Debug.LogError("[Build] " + options.Error);
                CiCompletionWatcher.WriteResultFile(options.ResultFile,
                    new BuildResult { Success = false, Error = options.Error });
                EditorApplication.Exit(2);
                return;
            }

            // CI pre-clean: no interactive recovery paths, secrets from the environment.
            OrphanedDevStateRecovery.Disarm();
            // Disarm together with the mirror above. The two records are independent — each is
            // checked on its own by the recovery flow — so this pre-clean has to name both
            // explicitly; clearing only the mirror would leave a stale rollback record that the
            // job started below could never own, and the next reload would warn about it forever.
            ContentStateGuard.Disarm();
            InjectKeystoreFromEnvironment();

            CiCompletionWatcher.Arm(options.ResultFile, options.TimeoutMinutes);
            UnicoBuildService.Start(options.Request, BuildConfigCatalog.LoadAll(),
                options.StrictWarnings ? WarnPolicy.Strict : WarnPolicy.Interactive);
            // Deliberately no wait: the resumer drives the job across domain reloads; the watcher
            // writes the result and exits the process. Returning lets queued reloads land.
        }

        // Enum.TryParse accepts ANY integer-formatted string — "-platform 5" would parse to an
        // undefined BuildPlatform and sail past the "unknown platform" guard into a build that
        // targets nothing. Reject values the enum does not actually define — a numeric that lands
        // on a real member still parses.
        private static bool TryParseName<T>(string value, out T parsed) where T : struct, Enum
        {
            parsed = default;
            return Enum.TryParse(value, true, out parsed) && Enum.IsDefined(typeof(T), parsed);
        }

        // Pure and order-tolerant: unrecognized tokens (Unity's own -batchmode/-projectPath/...)
        // are skipped; our flags are matched case-sensitively. Errors accumulate so CI logs show
        // every problem at once.
        internal static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();
            o.Request.BumpBuildCode = false;   // CI default: explicit or nothing
            var errors = new List<string>();
            var platformSet = false;
            var kindSet = false;

            string Next(ref int i, string flag)
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("-"))
                {
                    errors.Add($"{flag} requires a value");
                    return null;
                }
                return args[++i];
            }

            bool? NextBool(ref int i, string flag)
            {
                var v = Next(ref i, flag);
                if (v == null) return null;
                if (bool.TryParse(v, out var b)) return b;
                errors.Add($"{flag} expects true|false, got '{v}'");
                return null;
            }

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-platform":
                    {
                        var v = Next(ref i, "-platform");
                        if (v != null && TryParseName<BuildPlatform>(v, out var p)) { o.Request.Platform = p; platformSet = true; }
                        else if (v != null) errors.Add($"unknown platform '{v}'");
                        break;
                    }
                    case "-kind":
                    {
                        var v = Next(ref i, "-kind");
                        if (v != null && TryParseName<BuildKind>(v, out var k)) { o.Request.Kind = k; kindSet = true; }
                        else if (v != null) errors.Add($"unknown kind '{v}'");
                        break;
                    }
                    case "-versionName":
                        o.Request.VersionName = Next(ref i, "-versionName") ?? o.Request.VersionName;
                        break;
                    case "-bumpBuildCode":
                        if (o.Request.BuildCode == 0) o.Request.BumpBuildCode = true;
                        break;
                    case "-buildCode":
                    {
                        var v = Next(ref i, "-buildCode");
                        if (v != null && int.TryParse(v, out var code) && code > 0)
                        {
                            o.Request.BuildCode = code;
                            o.Request.BumpBuildCode = false;   // explicit pin wins
                        }
                        else if (v != null) errors.Add($"-buildCode expects a positive integer, got '{v}'");
                        break;
                    }
                    case "-bumpAddressables":
                        o.Request.BumpAddressablesVersion = true;
                        break;
                    case "-buildAddressables":
                    {
                        var b = NextBool(ref i, "-buildAddressables");
                        if (b.HasValue) o.Request.BuildAddressables = b.Value;
                        break;
                    }
                    case "-addressablesMode":
                    {
                        var v = Next(ref i, "-addressablesMode");
                        if (v != null && TryParseName<AddressablesMode>(v, out var m)) o.Request.AddressablesMode = m;
                        else if (v != null) errors.Add($"unknown addressablesMode '{v}'");
                        break;
                    }
                    case "-buildPlayer":
                    {
                        var b = NextBool(ref i, "-buildPlayer");
                        if (b.HasValue) o.Request.BuildPlayer = b.Value;
                        break;
                    }
                    case "-outputs":
                    {
                        var v = Next(ref i, "-outputs");
                        if (v != null)
                        {
                            var outputs = OutputKind.None;
                            var ok = true;
                            foreach (var part in v.Split(','))
                            {
                                if (TryParseName<OutputKind>(part.Trim(), out var one)) outputs |= one;
                                else { errors.Add($"unknown output '{part.Trim()}'"); ok = false; }
                            }
                            if (ok) o.Request.Outputs = outputs;
                        }
                        break;
                    }
                    case "-label":
                        o.Request.Label = Next(ref i, "-label") ?? o.Request.Label;
                        break;
                    case "-outputFolder":
                        o.Request.OutputFolder = Next(ref i, "-outputFolder") ?? o.Request.OutputFolder;
                        break;
                    case "-resultFile":
                        o.ResultFile = Next(ref i, "-resultFile") ?? o.ResultFile;
                        break;
                    case "-strictWarnings":
                        o.StrictWarnings = true;
                        break;
                    case "-timeoutMinutes":
                    {
                        var v = Next(ref i, "-timeoutMinutes");
                        if (v != null && int.TryParse(v, out var minutes) && minutes > 0) o.TimeoutMinutes = minutes;
                        else if (v != null) errors.Add($"-timeoutMinutes expects a positive integer, got '{v}'");
                        break;
                    }
                }
            }

            if (!platformSet) errors.Add("-platform is required");
            if (!kindSet) errors.Add("-kind is required");
            if (errors.Count > 0) o.Error = "CLI parse failed: " + string.Join("; ", errors);
            return o;
        }

        // Secrets come from the CI environment, never from committed sources. With the variables
        // unset this is a no-op and KeystoreCheck still blocks an unsigned Android Release cleanly.
        internal static void InjectKeystoreFromEnvironment()
        {
            var pass = Environment.GetEnvironmentVariable("UNICO_KEYSTORE_PASS");
            var alias = Environment.GetEnvironmentVariable("UNICO_KEYALIAS_PASS");
            if (string.IsNullOrEmpty(pass) || string.IsNullOrEmpty(alias)) return;
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystorePass = pass;
            PlayerSettings.Android.keyaliasPass = alias;
            Debug.Log("[Build] Keystore credentials injected from environment.");
        }
    }
}

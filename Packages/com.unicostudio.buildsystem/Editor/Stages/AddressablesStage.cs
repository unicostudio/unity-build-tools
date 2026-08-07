#if UNICO_HAS_ADDRESSABLES
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;
using UnityEngine;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.ResourceManagement.Util;
#endif
using UnityEditor.Build;

namespace UnicoStudio.BuildSystem.Editor
{
    public sealed class AddressablesStage : IBuildStage
    {
        public string Name => "Addressables";

#if UNICO_HAS_ADDRESSABLES
        public void Execute(BuildContext ctx)
        {
            // A soft skip here would finish the job "OK" without the requested content — the same
            // ship-without-remote-content hazard as a swallowed builder error. Fail loud instead.
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (!settings)
                throw new BuildFailedException("Addressables build requested but AddressableAssetSettings was not found.");

            // Every remote path binds through the version store; the profile evaluator SWALLOWS
            // resolution failures and returns the literal token, so a missing store must be
            // caught here, before anything is built.
            UnicoBuildPaths.RequireVersion();

            // 1) Switch active profile (enum -> profile name only at the Addressables API boundary).
            var profileName = ctx.Profile.ToString();
            var profileId = settings.profileSettings.GetProfileId(profileName);
            if (string.IsNullOrEmpty(profileId))
                throw new InvalidOperationException($"Addressables profile '{profileName}' not found.");
            settings.activeProfileId = profileId;

            // 1a) Drop Addressables' memoised property values before ANYTHING below evaluates.
            // AddressablesRuntimeProperties caches every successful resolution in a process-wide
            // static for the life of the domain, and the only thing that clears it is Addressables'
            // own BuildScriptBase.BuildData — which runs in step 4, AFTER this stage has validated.
            // Two ways that goes wrong without this call:
            //   * ApplyVersionStage bumps AddressablesVersionStore.Version earlier in this same run,
            //     so anything resolved before the bump is now stale by a version.
            //   * Preflight's RemotePathCheck evaluates Remote.LoadPath on every panel repaint, so a
            //     success gets pinned systematically — and stays pinned even after someone clears
            //     BuildSystemSettings.RemoteLoadRoot. Validating against that cache would pass, then
            //     BuildData would clear it, re-evaluate, fail, and (the evaluator swallows failures)
            //     bake the raw token text into the shipped catalog as the remote URL.
            // Clearing here is what makes this stage's validation and the catalog write see the same
            // state, so a Block here is the truth about what is about to be written.
            AddressablesRuntimeProperties.ClearCachedPropertyValues();

            // 1b) Arm the content-state rollback. Placement is load-bearing in BOTH directions:
            // GetContentStateDataPath evaluates through the ACTIVE profile, so arming before the
            // switch above would resolve against the developer's profile instead of this build's;
            // and everything below (clean, then build) is what overwrites the file, so arming any
            // later would have nothing to roll back to. DevStateSnapshot cannot do this at all —
            // it is captured in Start, before both the platform and the profile switch.
            ArmContentStateGuard(ctx);

            // 2) Validate that the profile's remote paths actually evaluate BEFORE building — an
            // unresolved binding would bake a garbage URL into the catalog.
            var buildFolder = EvaluateProfileVariable(settings, profileId, profileName, "Remote.BuildPath");
            var loadPath = EvaluateProfileVariable(settings, profileId, profileName, "Remote.LoadPath");
            if (!loadPath.StartsWith("http"))
                throw new BuildFailedException(
                    $"Remote.LoadPath evaluated to '{loadPath}' — not a URL. Check the Addressables " +
                    "profile's Remote.LoadPath and BuildSystemSettings.RemoteLoadRoot.");

            // 3) Clear build cache (All).
            AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);

            // 4) Update Previous Build or New Build.
            if (ctx.Request.AddressablesMode == AddressablesMode.UpdatePrevious)
            {
                var result = RunContentUpdate(settings);
                ThrowIfFailed(result, "Update Previous Build");
                ctx.AddStep($"Addressables: {profileName} - Update Previous Build");
            }
            else
            {
                AddressableAssetSettings.BuildPlayerContent(out var result);
                ThrowIfFailed(result, "New Build");
                ctx.AddStep($"Addressables: {profileName} - New Build");
            }

            // Register the content output folder (e.g. ServerData/v36/Production/Android) as an
            // artifact: it is exactly what must be uploaded to the CDN before a release ships.
            ctx.AddArtifact(buildFolder, ArtifactKind.AddressablesContent);
        }

        // The path, and whether it is a local file at all, are Addressables' to decide — never
        // re-derived here. A remote content state cannot be copied, so the run proceeds without a
        // rollback record and says so in the step log rather than pretending it is protected.
        private static void ArmContentStateGuard(BuildContext ctx)
        {
            var statePath = ContentUpdateScript.GetContentStateDataPath(false);
            if (ResourceManagerConfig.ShouldPathUseWebRequest(statePath))
            {
                ctx.AddStep($"Content state is remote ({statePath}) — no rollback backup taken.");
                return;
            }

            var existed = File.Exists(statePath);
            var backupPath = existed ? TryBackupContentState(statePath, ctx.Request.Platform) : "";
            // Stamp the record with the running job's identity so a later, unrelated job's Finish
            // can never apply a record left behind by this one (see ContentStateGuard.Restore).
            ContentStateGuard.Arm(statePath, existed, backupPath, BuildJobState.Load().StartedTicksUtc);
        }

        // Backup lives in Library (per-checkout, survives editor restarts for crash recovery).
        // A failed copy degrades to "no rollback" and is logged; it never fails the build.
        private static string TryBackupContentState(string statePath, BuildPlatform platform)
        {
            try
            {
                var backupPath = Path.Combine("Library", "UnicoBuild", $"content_state_backup_{platform}.bin");
                var backupDir = Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrEmpty(backupDir))
                    Directory.CreateDirectory(backupDir);
                File.Copy(statePath, backupPath, overwrite: true);
                return backupPath;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Build] Content state backup failed ({e.Message}); a failed build will not roll the content state back.");
                return "";
            }
        }

        // Asks Addressables itself for the evaluated profile value — the same evaluator the build
        // uses, so the registered artifact path is exactly what the build wrote. The evaluator
        // never throws: on failure it returns the token text verbatim, which we treat as fatal.
        private static string EvaluateProfileVariable(
            AddressableAssetSettings settings, string profileId, string profileName, string variableName)
        {
            var raw = settings.profileSettings.GetValueByName(profileId, variableName);
            if (string.IsNullOrEmpty(raw))
                throw new BuildFailedException($"Profile variable '{variableName}' is not defined in the Addressables profile settings.");

            var evaluated = settings.profileSettings.EvaluateString(profileId, raw);
            if (string.IsNullOrEmpty(evaluated))
                throw new BuildFailedException($"{variableName} evaluated to nothing (raw '{raw}').");

            // Two distinct failures, each with its own diagnosis. Which one applies is
            // ProfileBindingRule's call, not this stage's: preflight's RemotePathCheck asks the
            // same method, so the two can never disagree about what counts as broken. Only the
            // wording is local.
            var binding = ProfileBindingRule.Inspect(raw, evaluated);
            if (binding.Problem == ProfileBindingProblem.UnresolvedToken)
                throw new BuildFailedException(
                    $"Profile '{profileName}': {variableName} did not resolve — '{binding.Token}' survived " +
                    $"evaluation of '{raw}' -> '{evaluated}'. The binding names a type or member that " +
                    "does not exist, or its getter threw — the Addressables evaluator swallows both and " +
                    "substitutes the token's own text. Check the AddressablesVersionStore asset and, if " +
                    "the binding points at the package, BuildSystemSettings.RemoteLoadRoot.");

            // The other half of the same rule: a surviving bracket means the raw value's own
            // delimiters are malformed, which no token extraction can see. Different fix,
            // different message.
            if (binding.Problem == ProfileBindingProblem.MalformedDelimiters)
                throw new BuildFailedException(
                    $"Profile '{profileName}': {variableName} has malformed binding delimiters — raw " +
                    $"'{raw}' evaluated to '{evaluated}', which still contains '[' or ']'. Every binding " +
                    "must be a balanced '[Type.Member]' token; fix the brackets in the Addressables " +
                    "profile value.");

            return evaluated;
        }

        // Mirrors the Groups window's "Update a Previous Build" (Addressables 2.x): a normal
        // pipeline run with the previous content state attached. The legacy
        // ContentUpdateScript.BuildContentUpdate API is deliberately NOT used — it hard-requires
        // the current catalog load path to equal the recorded one, which this project's
        // catalog-pinned layout (the catalog stays at the ORIGINAL player's path while new bundles
        // land in v{N} folders) intentionally violates. The internal overload the menu calls
        // (AddressableAssetSettings.BuildPlayerContent(out, input)) is replicated here step by
        // step through public API only.
        private static AddressablesPlayerBuildResult RunContentUpdate(AddressableAssetSettings settings)
        {
            // This project's content-state path is local ("<default settings path>"). If the team
            // ever points Content State Build Path at a URL, this needs the menu's
            // DownloadBinFileToTempLocation handling before the File.Exists guard.
            var statePath = ContentUpdateScript.GetContentStateDataPath(false);
            if (!File.Exists(statePath))
                throw new BuildFailedException(
                    $"Update Previous Build requires the previous content state at '{statePath}'. " +
                    "Run a New Build once to establish it.");

            var state = ContentUpdateScript.LoadContentState(statePath);
            if (state == null)
                throw new BuildFailedException($"Could not load the previous content state at '{statePath}'.");

            // Menu parity: honor the "Check for Update Restrictions" option. The interactive
            // preview window is not available in an automated run, so any non-Disabled option
            // fails the build listing the offending assets (the menu's own batch-mode behavior).
            if (settings.CheckForContentUpdateRestrictionsOption != CheckForContentUpdateRestrictionsOptions.Disabled)
            {
                var modified = ContentUpdateScript.GatherModifiedEntriesWithDependencies(settings, statePath);
                if (modified.Count > 0)
                {
                    var sb = new StringBuilder("Modified entries in 'Cannot Change Post Release' groups were detected:");
                    foreach (var pair in modified)
                        sb.Append("\n  ").Append(pair.Key.AssetPath);
                    throw new BuildFailedException(sb.ToString());
                }
            }

            // Parity with the internal BuildPlayerContent: reset cached bundle file ids first.
            foreach (var group in settings.groups)
            {
                if (!group) continue;
                foreach (var entry in group.entries)
                    entry.BundleFileId = null;
            }

            // PlayerVersion is carried from the state so the catalog keeps its original file name
            // (e.g. catalog_1.2.0) — shipped players poll exactly that name.
            var input = new AddressablesDataBuilderInput(settings)
            {
                PlayerVersion = state.playerVersion,
                PreviousContentState = state,
            };
            var result = settings.ActivePlayerDataBuilder.BuildData<AddressablesPlayerBuildResult>(input);
            // Log parity with BuildPlayerContentImpl — ops can grep the same duration line for
            // menu and panel builds alike. Failures throw upstream with the error message.
            if (result != null && string.IsNullOrEmpty(result.Error))
                Debug.Log($"Addressable content successfully built (duration : {TimeSpan.FromSeconds(result.Duration):g})");
            BuildScript.buildCompleted?.Invoke(result);
            AssetDatabase.Refresh();
            return result;
        }

        // The Addressables builders never throw on failure — they report via result.Error (or a
        // null result). Treating "no exception" as success would ship a player whose remote
        // content was never produced, so any failure must fail the whole job here.
        internal static string FailureOf(AddressablesPlayerBuildResult result) =>
            result == null ? "builder returned no result" :
            string.IsNullOrEmpty(result.Error) ? null : result.Error;

        private static void ThrowIfFailed(AddressablesPlayerBuildResult result, string mode)
        {
            var failure = FailureOf(result);
            if (failure != null)
                throw new BuildFailedException($"Addressables {mode} failed: {failure}");
        }
#else
        // com.unity.addressables is not installed in this host. The type (and Name) stay
        // compiled so the stage factory and its tests are unconditional; requesting the stage
        // fails loud instead of finishing a job "OK" without its remote content.
        public void Execute(BuildContext ctx) => throw new BuildFailedException("Addressables package not installed but Build Addressables was requested.");
#endif
    }
}

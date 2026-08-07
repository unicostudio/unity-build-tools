using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    public static class UnicoBuildService
    {
        private const string RESULT_KEY = "UnicoBuild.BuildResult";

        // Single availability probe for hosts without com.unity.addressables. Constant per
        // compilation: the asmdef's UNICO_HAS_ADDRESSABLES versionDefine exists only when the
        // package is installed. A property (not const) so `if` sites don't warn as unreachable.
#if UNICO_HAS_ADDRESSABLES
        public static bool AddressablesAvailable => true;
#else
        public static bool AddressablesAvailable => false;
#endif

        // Read-only view of the running job's request; null when idle. Lets host Unity callbacks
        // (IPreprocessBuildWithReport etc.) detect panel/pipeline builds and their parameters.
        public static BuildRequest ActiveRequest
        {
            get
            {
                var state = BuildJobState.Load();
                return state.Active ? JsonUtility.FromJson<BuildRequest>(state.RequestJson) : null;
            }
        }

        // PostSuccessRunner reports post-step outcomes into the already-recorded result so the
        // panel's "Last build" block shows them. Success is never modified here.
        //
        // The outcome belongs to the job whose queue is draining — NOT to whatever result happens
        // to be current. A second build started before the queue drained (programmatic back-to-back
        // runs) would otherwise stamp the previous job's post steps onto the new run's result.
        internal static void AppendPostStep(string resultStamp, string step)
        {
            var result = LatestResult;
            if (result == null || !ShouldRecordPostStep(result.StartedUtc, resultStamp))
            {
                var why = result == null ? "no build result is recorded"
                    : string.IsNullOrEmpty(resultStamp) ? "its run could not be identified"
                    : "its build result is no longer the current one";
                Debug.Log($"[Build] {step} (logged only: {why})");
                return;
            }
            result.Steps.Add(step);
            LatestResult = result;   // re-persist through the SessionState-backed setter
        }

        // Pure core (unit-tested). StartedUtc identifies a run (Finish stamps it, ISO "o"); an
        // empty stamp on either side identifies nothing, so it never matches.
        internal static bool ShouldRecordPostStep(string currentStamp, string queueStamp) =>
            !string.IsNullOrEmpty(queueStamp) && currentStamp == queueStamp;

        // Lets a host IPostSuccessStep register a file it produced or verified on the already
        // recorded result, under the same run-attribution guard as AppendPostStep.
        //
        // PUBLIC, unlike AppendPostStep, because contributing an artifact is exactly what a host
        // post-success step exists to do. The stamp is no burden on the host: PostSuccessRunner
        // hands the step the BuildResult and derives its own stamp from result.StartedUtc, so the
        // step already holds it. Exposing the stamp is safe because the guard below rejects a
        // wrong one — a host cannot staple its artifact onto someone else's run.
        //
        // A host reporting a PROBLEM does not need an API: it throws, and PostSuccessRunner
        // records "Post step FAILED: {label} — {message}" on the same result.
        public static void AppendPostArtifact(string resultStamp, string path, ArtifactKind kind)
        {
            var result = LatestResult;
            if (result == null || !ShouldRecordPostStep(result.StartedUtc, resultStamp))
            {
                var why = result == null ? "no build result is recorded"
                    : string.IsNullOrEmpty(resultStamp) ? "its run could not be identified"
                    : "its build result is no longer the current one";
                Debug.Log($"[Build] artifact {path} (logged only: {why})");
                return;
            }
            AppendArtifactTo(result, path, kind);
            LatestResult = result;   // re-persist through the SessionState-backed setter
        }

        // Pure core (unit-tested). Finish keeps Artifacts and TypedArtifacts index-aligned; a
        // legacy payload can hydrate with fewer typed entries than paths, so pad first — otherwise
        // every reader after the gap pairs one artifact's kind with another artifact's path.
        internal static void AppendArtifactTo(BuildResult result, string path, ArtifactKind kind)
        {
            while (result.TypedArtifacts.Count < result.Artifacts.Count)
            {
                result.TypedArtifacts.Add(new BuildArtifact
                {
                    Path = result.Artifacts[result.TypedArtifacts.Count],
                    Kind = ArtifactKind.Unknown,
                });
            }

            result.Artifacts.Add(path);
            result.TypedArtifacts.Add(new BuildArtifact { Path = path, Kind = kind });
        }

        // Backed by SessionState: the define-restoring reload at the end of most builds wipes
        // statics, and the result must survive it for the panel to show the outcome at all.
        private static BuildResult s_latestResult;
        public static BuildResult LatestResult
        {
            get
            {
                if (s_latestResult == null)
                {
                    var json = SessionState.GetString(RESULT_KEY, "");
                    if (!string.IsNullOrEmpty(json))
                        s_latestResult = JsonUtility.FromJson<BuildResult>(json).Normalize();
                }
                return s_latestResult;
            }
            private set
            {
                s_latestResult = value;
                SessionState.SetString(RESULT_KEY, value == null ? "" : JsonUtility.ToJson(value));
            }
        }

        public static bool IsRunning => BuildJobState.Load().Active;

        // Progress surface for the panel while a job runs; null when idle. Stage instances are
        // stateless, so materializing them just for names is cheap.
        public static BuildProgress GetProgress()
        {
            var state = BuildJobState.Load();
            if (!state.Active) return null;
            // Prefer the frozen display names (they include host steps and cost no
            // materialization); legacy jobs fall back to the request-derived factory.
            IReadOnlyList<string> names = state.StageDisplayNames;
            if (names.Count == 0)
            {
                var request = JsonUtility.FromJson<BuildRequest>(state.RequestJson);
                names = Stages(request).Select(s => s.Name).ToList();
            }
            return new BuildProgress
            {
                StageNumber = Math.Min(state.StepIndex + 1, names.Count),
                StageCount = names.Count,
                CurrentStageName = state.StepIndex < names.Count ? names[state.StepIndex] : "Finishing",
                Steps = state.Steps,
            };
        }

        // Recovery affordance for a wedged job (e.g. compile errors holding the reload sentinel,
        // or an exception that escaped Finish). Restores the captured dev state through the normal
        // Finish path and clears the job so the panel is usable again.
        public static void ResetStuckJob()
        {
            var state = BuildJobState.Load();
            if (!state.Active) return;
            // The wedge may BE a queued reload that never landed (e.g. compile errors); clear the
            // sentinel so the next job isn't blocked. Finish re-arms it if its restore queues one.
            s_reloadPending = false;
            var ctx = new BuildContext(JsonUtility.FromJson<BuildRequest>(state.RequestJson));
            ctx.Steps.AddRange(state.Steps);
            ctx.Artifacts.AddRange(state.Artifacts);
            ctx.ArtifactKinds.AddRange(state.ArtifactKinds);
            Finish(state, ctx, success: false, error: "Build job manually reset from the Build Panel.");
        }

        // True while a domain reload is queued (platform switch, global define change) but has not
        // started yet. EditorApplication.isCompiling stays false until the compile actually begins,
        // so without this flag a resumer update tick could re-enter Advance and run the remaining
        // stages against stale assemblies. Statics reset on domain reload — only the reload itself
        // clears the flag, which is exactly the condition we are waiting for.
        private static bool s_reloadPending;

        // Resolve + materialize in one hop: the full pipeline including discovered host steps.
        // Also the fallback for legacy (pre-v0.4.0) job payloads with no frozen name list.
        // Internal for the EditMode tests.
        internal static IBuildStage[] Stages(BuildRequest req) =>
            BuildStepRegistry.Materialize(BuildStepRegistry.ToTypeNames(BuildStepRegistry.ResolvePipeline(req)));

        public static void Start(BuildRequest request) => Start(request, BuildConfigCatalog.LoadAll());

        public static void Start(BuildRequest request, IReadOnlyList<BuildTargetConfig> configs) =>
            Start(request, configs, WarnPolicy.Interactive);

        public static void Start(BuildRequest request, IReadOnlyList<BuildTargetConfig> configs, WarnPolicy warnPolicy)
        {
            // A second Start mid-job would recapture DevStateSnapshot from already-mutated build
            // state (bumped versions, stripped defines) and overwrite the running job's true
            // pre-build record and its orphan mirror — permanently. The panel disables its button;
            // the API surface must refuse too.
            if (IsRunning)
            {
                Debug.LogError("[Build] A build job is already running; Start ignored.");
                return;
            }

            // Resolved BEFORE preflight: checks that depend on the profile must see the build's,
            // not BuildContext's initialiser.
            var ctx = new BuildContext(request)
            {
                Profile = BuildConfigCatalog.ResolveProfile(configs, request.Platform, request.Kind),
            };

            // Pre-flight. Interactive: the panel gates Warn acknowledgement before calling Start;
            // Strict: non-exempt warns block right here. Warnings are recorded in every mode.
            var defaults = PreflightRunner.Default();
            var checks = PreflightRunner.Run(ctx, defaults);
            var (blockMessage, warnings) = PreflightRunner.Gate(
                checks, defaults.Select(c => c.IsStrictExempt).ToList(), warnPolicy);
            if (blockMessage != null)
            {
                LatestResult = new BuildResult
                {
                    Success = false,
                    Error = blockMessage,
                    Warnings = new List<string>(warnings),
                };
                return;
            }

            // Freeze the full pipeline (built-ins + discovered host steps) NOW — materialized once
            // to validate every step and harvest display names; Advance re-materializes from the
            // frozen names on every entry.
            var stageTypeNames = BuildStepRegistry.ToTypeNames(BuildStepRegistry.ResolvePipeline(request));
            IBuildStage[] pipelineStages;
            try
            {
                pipelineStages = BuildStepRegistry.Materialize(stageTypeNames);
            }
            catch (Exception e)
            {
                LatestResult = new BuildResult { Success = false, Error = e.Message };
                return;
            }

            var state = new BuildJobState
            {
                Active = true,
                StepIndex = 0,
                RequestJson = JsonUtility.ToJson(request),
                SnapshotJson = JsonUtility.ToJson(DevStateSnapshot.Capture(request.Platform)),
                Profile = ctx.Profile,
                StartedTicksUtc = DateTime.UtcNow.Ticks,
            };
            state.StageTypeNames.AddRange(stageTypeNames);
            state.StageDisplayNames.AddRange(pipelineStages.Select(s => s.Name));
            state.Warnings.AddRange(warnings);
            LatestResult = null; // the previous run's result would read as this run's mid-build
            state.Save();
            OrphanedDevStateRecovery.Arm(state.SnapshotJson);

            BuildSession.Begin();
            Advance();
        }

        // Runs stages from StepIndex until a stage triggers a domain reload or all are done.
        public static void Advance()
        {
            if (s_reloadPending) return; // a reload is queued; resume only after it lands
            var state = BuildJobState.Load();
            if (!state.Active) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            var request = JsonUtility.FromJson<BuildRequest>(state.RequestJson);

            // Ensure the active platform matches; switching reimports + reloads, then the resumer re-enters.
            var wantTarget = request.Platform.ToBuildTarget();
            if (EditorUserBuildSettings.activeBuildTarget != wantTarget)
            {
                var group = request.Platform.ToBuildTargetGroup();
                // Fail cleanly if the switch is rejected (e.g. platform module not installed) instead of
                // busy-looping the resumer: a false return leaves activeBuildTarget unchanged forever.
                if (EditorUserBuildSettings.SwitchActiveBuildTarget(group, wantTarget))
                {
                    s_reloadPending = true; // reimport + reload queued; the resumer re-enters after it
                }
                else
                {
                    // Hydrate accumulated progress so the failure result keeps the step log.
                    var failCtx = new BuildContext(request);
                    failCtx.Steps.AddRange(state.Steps);
                    failCtx.Artifacts.AddRange(state.Artifacts);
                    failCtx.ArtifactKinds.AddRange(state.ArtifactKinds);
                    Finish(state, failCtx, success: false,
                        error: $"Platform switch to {wantTarget} failed — is the {group} module installed?");
                }
                return;
            }

            // Rebuild the frozen pipeline from names; a type that vanished mid-job fails the job
            // cleanly instead of resuming into a shifted stage list. Empty names = a legacy job
            // started before v0.4.0 — fall back to the request-derived factory.
            IBuildStage[] stages;
            try
            {
                stages = state.StageTypeNames.Count > 0
                    ? BuildStepRegistry.Materialize(state.StageTypeNames)
                    : Stages(request);
            }
            catch (Exception e)
            {
                var failCtx = new BuildContext(request);
                failCtx.Steps.AddRange(state.Steps);
                failCtx.Artifacts.AddRange(state.Artifacts);
                failCtx.ArtifactKinds.AddRange(state.ArtifactKinds);
                Finish(state, failCtx, success: false, error: e.Message);
                return;
            }

            var ctx = new BuildContext(request)
            {
                Profile = state.Profile,
            };
            ctx.Steps.AddRange(state.Steps);
            ctx.Artifacts.AddRange(state.Artifacts);
            ctx.ArtifactKinds.AddRange(state.ArtifactKinds);
            ctx.ExtraScriptingDefines.AddRange(state.ExtraDefines);
            foreach (var kv in state.GetData()) ctx.Data[kv.Key] = kv.Value;

            BuildSession.Begin();

            IBuildStage stage = null;
            try
            {
                while (state.StepIndex < stages.Length)
                {
                    stage = stages[state.StepIndex];
                    var definesBefore = ProjectDefinesHash(request);

                    stage.Execute(ctx);
                    state.StepIndex++;

                    // Persist progress before a potential reload.
                    state.Steps = new List<string>(ctx.Steps);
                    state.Artifacts = new List<string>(ctx.Artifacts);
                    state.ArtifactKinds = new List<int>(ctx.ArtifactKinds);
                    state.ExtraDefines = new List<string>(ctx.ExtraScriptingDefines);
                    state.SetData(ctx.Data);
                    state.Save();

                    // If this stage changed global defines, a recompile + reload is queued: stop here;
                    // the sentinel keeps update ticks out until the reload lands, then the resumer re-enters.
                    if (ProjectDefinesHash(request) != definesBefore)
                    {
                        s_reloadPending = true;
                        return;
                    }
                }

                stage = null; // all stages succeeded — a restore failure below is not a stage failure
                Finish(state, ctx, success: true, error: "");
            }
            catch (Exception e)
            {
                // All stages succeeded and the exception escaped Finish itself (which contains its
                // own failures now, so this is belt-and-braces): a second Finish would roll back a
                // successful build's version bumps. Surface the error without double-finishing.
                if (stage == null)
                {
                    Debug.LogException(e);
                    return;
                }

                // Name the failing stage — a bare exception message doesn't say where to look.
                Finish(state, ctx, success: false, error: $"[{stage.Name}] {e.Message}");
            }
        }

        private static void Finish(BuildJobState state, BuildContext ctx, bool success, string error)
        {
            // Contained restore + guaranteed cleanup: an exception that escaped here used to leave
            // the job Active, so the resumer re-entered Finish (and threw again) every editor tick —
            // and ResetStuckJob, routing through the same path, could not clear it either.
            var restored = false;
            // The job's OWN outcome, pinned before the catch below can overwrite `success` with a
            // restore failure. The cleanup in the finally has to distinguish "the build failed"
            // from "the build succeeded but rolling dev state back threw" — those want opposite
            // treatment for the content-state record (see below), and after the catch runs the
            // `success` parameter can no longer tell them apart.
            var jobSucceeded = success;
            try
            {
                var definesBefore = ProjectDefinesHash(ctx.Request);
                var snap = JsonUtility.FromJson<DevStateSnapshot>(state.SnapshotJson);
                // FromJson returns NULL for a null/empty payload — it does not throw (measured on
                // 6000.0.62f1; whitespace is the odd one out and throws ArgumentException, equally
                // contained by the catch below). No writer can produce an empty SnapshotJson today
                // (Start is the only one), so this is robustness, not a live path: without it the
                // failure mode would be an NRE into the catch wearing a misleading message.
                if (snap == null)
                    throw new InvalidOperationException(
                        "BuildJobState.SnapshotJson is empty — no captured dev state to restore.");
                snap.Restore();
                // If the restore changed defines (e.g. re-adding TEST_MODE after a Release build), a
                // recompile is queued — hold any immediately-started next job until the reload lands.
                if (ProjectDefinesHash(ctx.Request) != definesBefore)
                    s_reloadPending = true;
                // A failed run produced nothing; roll its version writes and content state back.
                if (!success && snap.RestoreVersions())
                    ctx.AddStep("Rolled back version bumps");
                if (!success && ContentStateGuard.Restore(state.StartedTicksUtc))
                    ctx.AddStep("Restored addressables content state");
                restored = true;
                ctx.AddStep("Restored dev state");
            }
            catch (Exception e)
            {
                success = false;
                error = string.IsNullOrEmpty(error)
                    ? $"Dev-state restore failed: {e.Message}"
                    : $"{error} (dev-state restore also failed: {e.Message})";
                ctx.AddStep("Dev-state restore FAILED — recovery will be offered after the next reload");
                Debug.LogException(e);
            }
            finally
            {
                // Keep the EditorPrefs mirror armed when the restore failed: the job is cleared
                // below, so the next domain reload sees an orphaned snapshot and offers recovery.
                if (restored)
                    OrphanedDevStateRecovery.Disarm();

                // The content-state record is checked independently of the mirror by the recovery
                // flow, so it no longer has to follow `restored` to stay reachable — and it must
                // not, for two reasons:
                //
                // (1) OWNERSHIP. Restore() above deliberately declines a record armed by a
                //     different run and says so, leaving it for its owner. An unconditional
                //     Disarm() here deleted exactly that record a line later, and since the file
                //     is git-ignored and its Library/ backup is overwritten by the next
                //     Addressables run, the rollback was gone for good. DisarmIfOwnedBy only ever
                //     clears THIS job's record.
                //
                // (2) A SUCCESSFUL RUN'S RECORD HAS NO PURPOSE. Its content state is never rolled
                //     back, so keeping the record can only hurt: when the build succeeded but the
                //     dev-state restore threw, `restored` is false, and the record — holding this
                //     run's PRE-build state — survived to be applied by the recovery flow after
                //     the next reload, reverting or deleting the content state of a build that
                //     actually shipped. Hence `jobSucceeded ||`, not just `restored`.
                if (jobSucceeded || restored)
                    ContentStateGuard.DisarmIfOwnedBy(state.StartedTicksUtc);

                var typed = new List<BuildArtifact>();
                for (var i = 0; i < ctx.Artifacts.Count; i++)
                {
                    typed.Add(new BuildArtifact
                    {
                        Path = ctx.Artifacts[i],
                        // Legacy hydration may have fewer kinds than paths — pad as Unknown.
                        Kind = i < ctx.ArtifactKinds.Count ? (ArtifactKind)ctx.ArtifactKinds[i] : ArtifactKind.Unknown,
                    });
                }
                var store = AddressablesVersionStore.LoadOrNull();
                LatestResult = new BuildResult
                {
                    Success = success,
                    Error = error,
                    DurationSeconds = state.StartedTicksUtc > 0
                        ? (DateTime.UtcNow - new DateTime(state.StartedTicksUtc, DateTimeKind.Utc)).TotalSeconds
                        : 0,
                    Steps = new List<string>(ctx.Steps),
                    Artifacts = new List<string>(ctx.Artifacts),
                    Warnings = new List<string>(state.Warnings),
                    TypedArtifacts = typed,
                    // Snapshot of the values the run ends with: on success these are the produced
                    // build's values; on failure the rollback has already restored the originals.
                    VersionName = PlayerSettings.bundleVersion,
                    BuildCode = ctx.Request.Platform == BuildPlatform.Android
                        ? PlayerSettings.Android.bundleVersionCode.ToString()
                        : PlayerSettings.iOS.buildNumber,
                    AddressablesVersion = store ? store.Version : -1,
                    StartedUtc = state.StartedTicksUtc > 0
                        ? new DateTime(state.StartedTicksUtc, DateTimeKind.Utc).ToString("o") : "",
                    EndedUtc = DateTime.UtcNow.ToString("o"),
                };

                BuildSession.End();
                BuildJobState.Clear();
                // Post-success steps run OUTSIDE the job: armed only when the whole job succeeded,
                // drained by PostSuccessRunner's update tick after any restore-reload lands.
                if (success) PostSuccessRunner.Arm(LatestResult);
                Debug.Log($"[Build] {(success ? "OK" : "FAIL " + error)}\n" + string.Join("\n", ctx.Steps));
            }
        }

        private static string ProjectDefinesHash(BuildRequest req) =>
            PlayerSettings.GetScriptingDefineSymbols(req.Platform.ToNamedBuildTarget());
    }
}

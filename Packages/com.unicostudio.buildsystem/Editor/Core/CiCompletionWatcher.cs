using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    // Concludes a CLI-started job: writes the machine-readable result file and exits the process
    // with 0 (success), 1 (failure) or 2 (timeout). Armed only by UnicoBuildCli, and every action
    // is additionally guarded by Application.isBatchMode — a stray SessionState record must never
    // terminate an interactive editor. [InitializeOnLoad] + update ticks: the same reload-survival
    // pattern as BuildJobResumer, because the watched job reloads the domain mid-flight.
    [InitializeOnLoad]
    public static class CiCompletionWatcher
    {
        private const string Key = "UnicoBuild.CiWatch";

        [Serializable]
        internal sealed class Record
        {
            public string ResultFile = "";
            public long DeadlineTicksUtc;
        }

        internal enum WatchAction { None, ExitSuccess, ExitFailure, TimeoutReset }

        private static bool s_knownInactive;

        static CiCompletionWatcher() => EditorApplication.update += OnUpdate;

        public static void Arm(string resultFile, int timeoutMinutes)
        {
            SessionState.SetString(Key, JsonUtility.ToJson(new Record
            {
                ResultFile = resultFile,
                DeadlineTicksUtc = DateTime.UtcNow.AddMinutes(timeoutMinutes).Ticks,
            }));
            s_knownInactive = false;
        }

        // Pure decision core (unit-tested). A CONCLUDED job outranks the deadline: its artifacts —
        // and any post-success uploads that already ran — are real, so timing it out would report a
        // build that actually succeeded as a failure. The deadline is the escape hatch for states
        // that cannot conclude on their own: a wedged job (compile error holding the reload
        // sentinel) or a queue that never drains would otherwise hang the CI agent forever.
        internal static WatchAction Decide(bool isRunning, bool hasResult, bool postSuccessPending,
            bool resultSuccess, long nowTicksUtc, long deadlineTicksUtc)
        {
            if (!isRunning && hasResult && !postSuccessPending)
                return resultSuccess ? WatchAction.ExitSuccess : WatchAction.ExitFailure;
            if (nowTicksUtc >= deadlineTicksUtc) return WatchAction.TimeoutReset;
            return WatchAction.None;
        }

        private static void OnUpdate()
        {
            if (s_knownInactive) return;
            if (!Application.isBatchMode) return;   // interactive editors are never exited
            var json = SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(json))
            {
                s_knownInactive = true;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            // Everything from here on — deserializing the record, reading the latest result and the
            // post-success queue, claiming the record, running the rollback/flush — is one blast
            // radius. A throw in ANY of it, even before the record is claimed, must not leave
            // OnUpdate free to re-enter next tick and throw again forever: with the record still in
            // place that is a silent hang (no result file, no exit code) the CI agent can only end
            // with its own outer timeout.
            Record record = null;
            try
            {
                record = JsonUtility.FromJson<Record>(json);
                var result = UnicoBuildService.LatestResult;
                var action = Decide(UnicoBuildService.IsRunning, result != null, PostSuccessRunner.HasPending,
                    result?.Success ?? false, DateTime.UtcNow.Ticks, record.DeadlineTicksUtc);
                if (action == WatchAction.None) return;

                SessionState.EraseString(Key);
                s_knownInactive = true;
                // The record is claimed: from here the process MUST conclude. An exception escaping
                // the rollback or the flush would otherwise leave the batchmode run with no result
                // file and no exit code — a hang the CI agent can only end with its own outer timeout.
                if (action == WatchAction.TimeoutReset)
                {
                    UnicoBuildService.ResetStuckJob();   // restores dev state through the normal path
                    var timeoutResult = UnicoBuildService.LatestResult ?? new BuildResult
                    {
                        Success = false,
                        Error = "no result was produced",
                    };
                    timeoutResult.Success = false;
                    timeoutResult.Error = "CI deadline exceeded — " + timeoutResult.Error;
                    SaveProjectStateBeforeExit();
                    WriteResultFile(record.ResultFile, timeoutResult);
                    EditorApplication.Exit(2);
                    return;
                }

                SaveProjectStateBeforeExit();
                WriteResultFile(record.ResultFile, result);
                EditorApplication.Exit(action == WatchAction.ExitSuccess ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                // The record may not have been claimed yet (the throw could have come from parsing
                // it or from the reads above) — erase it either way so a repeat tick cannot re-enter
                // this same throw forever.
                SessionState.EraseString(Key);
                s_knownInactive = true;
                // Keep whatever the run actually produced (steps, artifacts, versions) — the flush
                // failing does not unmake the build; only the outcome flips.
                var salvaged = UnicoBuildService.LatestResult ?? new BuildResult();
                salvaged.Success = false;
                salvaged.Error = "CI watcher failed to conclude the run: " + e.Message +
                                 (string.IsNullOrEmpty(salvaged.Error) ? "" : " | " + salvaged.Error);
                WriteResultFile(record?.ResultFile, salvaged);
                EditorApplication.Exit(2);
            }
        }

        // Exit skips Unity's graceful shutdown save, so every exit path flushes AFTER the state it
        // reports is final — on the timeout path that means after ResetStuckJob's rollback, or the
        // run's bumped versions and stripped defines would persist on disk while the rollback that
        // undid them is thrown away. (The success path's bumps describe a real build and are kept,
        // which is what a commit-back CI pipeline reads.)
        private static void SaveProjectStateBeforeExit() => AssetDatabase.SaveAssets();

        internal static void WriteResultFile(string path, BuildResult result)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonUtility.ToJson(result, prettyPrint: true));
                Debug.Log($"[Build] CI result written: {path}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);   // the exit code still carries the outcome
            }
        }
    }
}

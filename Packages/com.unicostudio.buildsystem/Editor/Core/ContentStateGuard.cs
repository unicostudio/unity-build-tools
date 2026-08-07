using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    // Rollback record for the Addressables content-state file (addressables_content_state.bin).
    //
    // WHY THIS IS NOT PART OF DevStateSnapshot: the file's location is owned by Addressables and
    // resolved through ContentUpdateScript.GetContentStateDataPath, which evaluates the ACTIVE
    // profile and falls back to a default derived from the ACTIVE build target. DevStateSnapshot
    // is captured in Start — before the platform switch and before the profile switch — so it
    // cannot ask the authoritative resolver, and reconstructing the layout instead is exactly the
    // silent-divergence this package is removing. Only AddressablesStage runs at a moment when
    // the answer is correct, so the stage arms this guard right after it switches the active
    // profile — before the clean and build steps below it overwrite the file.
    //
    // A stage cannot patch the snapshot either: Advance holds BuildJobState in memory and calls
    // Save() after every stage, which would overwrite the patch at the next stage boundary.
    //
    // The record lives in EditorPrefs for the same reason OrphanedDevStateRecovery does:
    // SessionState dies with the editor process, and a crash mid-content-build is precisely when
    // the rollback matters. This type stays UNGATED so Finish and the recovery flow can call it
    // without #if; only the gated stage arms it, and only the gated stage decides whether the
    // path is local (Addressables' own ShouldPathUseWebRequest makes that call).
    //
    // INTERNAL BY DESIGN: the job-stamp check below is the only thing standing between a stale
    // record and a file it was never captured for, and AnyJobStamp bypasses that check by
    // definition. A public surface would let any host editor script call
    // Restore(AnyJobStamp) and unconditionally apply whatever is armed, so the whole ownership
    // gate is kept inside this assembly — arming belongs to AddressablesStage, applying to
    // Finish and OrphanedDevStateRecovery, and nothing else has business here. The test fixture
    // reaches it through InternalsVisibleTo (Editor/AssemblyInfo.cs).
    internal static class ContentStateGuard
    {
        [Serializable]
        internal sealed class Record
        {
            public string Path = "";
            public string BackupPath = "";
            public bool ExistedBefore;
            // Identifies the job that armed this record (BuildJobState.StartedTicksUtc). Without
            // this, a stale record left behind by one job (e.g. its Finish crashed before
            // disarming) could be silently APPLIED by a later, unrelated job's Finish — the old
            // DevStateSnapshot-based design could only ever have a stale record disarmed, since
            // each job restored from its own in-memory snapshot instead of a shared EditorPrefs key.
            public long JobStamp;
        }

        // EditorPrefs is machine-global; key on the checkout path, matching
        // OrphanedDevStateRecovery — several clones of this repo share one applicationIdentifier.
        private static readonly string s_key = $"UnicoBuild.ContentState.{Application.dataPath}";

        // Exposed only for ContentStateGuardEditModeTests: the key is shared with production code
        // (an actual in-flight build can have it armed), so the fixture needs it to save the
        // pre-existing EditorPrefs value in [SetUp] and restore it in [TearDown] instead of
        // clobbering a real rollback record.
        internal static string Key => s_key;

        // Sentinel meaning "apply regardless of which job armed the record". Used only by
        // OrphanedDevStateRecovery.Offer: it restores a record left behind by a run that crashed
        // in a PREVIOUS editor session, so there is no current job to compare a stamp against —
        // a crashed run's record is exactly what that recovery flow exists to apply.
        internal const long AnyJobStamp = long.MinValue;

        internal static bool IsArmed => !string.IsNullOrEmpty(EditorPrefs.GetString(s_key, ""));

        // The raw record, for diagnostics only. The recovery flow logs it verbatim whenever it
        // clears a record without applying it (batchmode, "Ignore", a failed restore): the
        // content-state file is git-ignored and its Library/ backup is overwritten by the next
        // Addressables run, so the log line is the last thing a developer can act on by hand.
        internal static string RecordJson => EditorPrefs.GetString(s_key, "");

        // The content-state path the armed record protects, or "" when nothing usable is armed.
        internal static string ArmedPath => ReadRecord()?.Path ?? "";

        // NEVER THROWS. JsonUtility.FromJson raises ArgumentException on a malformed non-empty
        // payload, and every path that reaches here is one a throw cannot be recovered from:
        //  - UnicoBuildService.Finish's finally calls DisarmIfOwnedBy → IsArmedFor → here, UPSTREAM
        //    of BuildJobState.Clear(). A throw would leave the job Active forever: the resumer
        //    re-enters Finish and throws again on every editor tick, and ResetStuckJob cannot clear
        //    it because it routes through the same Finish.
        //  - OrphanedDevStateRecovery's [InitializeOnLoad] constructor reads ArmedPath → here
        //    BEFORE its Disarm() calls, so a throw becomes a TypeInitializationException on every
        //    domain reload with the records never cleared. (Its IsArmed gate does not parse the
        //    payload, so it cannot catch this either.)
        // A record that cannot be parsed protects nothing, so it is ERASED rather than left to keep
        // IsArmed true and re-trigger the recovery dialog forever — same trade PostSuccessRunner.Parse
        // makes for an unreadable queue payload. The warning carries the key and the payload verbatim
        // because it is the last copy: the content-state file is git-ignored and its Library/ backup
        // is overwritten by the next Addressables run.
        private static Record ReadRecord()
        {
            var json = EditorPrefs.GetString(s_key, "");
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<Record>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Build] Content-state rollback record under '{s_key}' was unreadable " +
                                 $"and has been dropped: {e.Message} Record was: {json}");
                Disarm();
                return null;
            }
        }

        // True only when a record is armed AND it belongs to jobStamp's job. Callers use this to
        // decide whether a record is theirs to act on; a foreign record is an orphan for the
        // recovery flow, not something to apply or discard here.
        internal static bool IsArmedFor(long jobStamp)
        {
            var record = ReadRecord();
            return record != null && record.JobStamp == jobStamp;
        }

        // Clears the record only when it belongs to jobStamp's job. A record armed by a DIFFERENT
        // run must survive: it is an orphan for OrphanedDevStateRecovery to offer, and deleting it
        // here would destroy the only rollback that exists — the content-state file is git-ignored,
        // so there is no VCS fallback, and its Library/ backup dies with the next Addressables run.
        internal static void DisarmIfOwnedBy(long jobStamp)
        {
            if (IsArmedFor(jobStamp))
                Disarm();
        }

        internal static void Arm(string path, bool existedBefore, string backupPath, long jobStamp)
        {
            // A record is only ever applied by matching this stamp against a live job's
            // StartedTicksUtc (always DateTime.UtcNow.Ticks, i.e. strictly positive), so a stamp
            // that can never match arms a rollback nothing will ever apply — the failure is silent
            // and only shows up as a content state left at build values. The realistic way to get
            // here is calling AddressablesStage.Execute outside a live job, where
            // BuildJobState.Load() hands back a fresh instance with StartedTicksUtc == 0.
            if (jobStamp == AnyJobStamp)
                throw new ArgumentOutOfRangeException(nameof(jobStamp),
                    "AnyJobStamp is the apply-side sentinel and can never identify a job; " +
                    "arm with the running job's BuildJobState.StartedTicksUtc.");
            if (jobStamp <= 0)
                throw new ArgumentOutOfRangeException(nameof(jobStamp), jobStamp,
                    "A content-state rollback record must be armed from inside a live build job. " +
                    "Stamp 0 is what BuildJobState.Load() reports when no job is active, and no " +
                    "Finish can ever match it, so the rollback would be silently skipped.");

            EditorPrefs.SetString(s_key, JsonUtility.ToJson(new Record
            {
                // The content-state path is Addressables' to resolve, not ours to reconstruct —
                // unlike the path this replaced, it can legitimately fall outside Assets/ (e.g.
                // Local.BuildPath under Library/, Remote.BuildPath under ServerData/). Path.Combine
                // leaves '\' separators on Windows even for the default layout, and AssetDatabase
                // rejects those, so normalize to '/' once here rather than at every call site.
                Path = NormalizeSeparators(path),
                BackupPath = NormalizeSeparators(backupPath),
                ExistedBefore = existedBefore,
                JobStamp = jobStamp,
            }));
        }

        internal static void Disarm() => EditorPrefs.DeleteKey(s_key);

        private static string NormalizeSeparators(string path) =>
            string.IsNullOrEmpty(path) ? "" : path.Replace('\\', '/');

        // True when the stored path is one AssetDatabase can act on. The content-state path can
        // legitimately sit outside Assets/ (Local.BuildPath resolves under Library/,
        // Remote.BuildPath under ServerData/) — a case the reconstructed path this replaced could
        // never hit, since it was always built under Assets/ by construction. AssetDatabase silently
        // no-ops (and reports failure) for anything outside Assets/, so callers must branch on this
        // rather than assume every stored path is a project asset.
        private static bool IsAssetDatabasePath(string path) =>
            !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal);

        internal enum RestoreAction { None, RestoreBackup, DeleteCurrent }

        // Pure decision core (unit-tested): what to do with the content-state file when rolling a
        // failed or crashed run back.
        internal static RestoreAction PlanRestore(bool existedAtCapture, bool backupAvailable, bool existsNow)
        {
            if (existedAtCapture)
                return backupAvailable ? RestoreAction.RestoreBackup : RestoreAction.None;
            // The file was born during this run (first New Build); a failed run produced nothing.
            return existsNow ? RestoreAction.DeleteCurrent : RestoreAction.None;
        }

        // Applies the plan for the record armed by expectedJobStamp's job. Returns true only when
        // a file was actually restored or deleted, so callers can log honestly. Never throws for a
        // missing or empty record. Pass AnyJobStamp to apply whatever record is stored regardless
        // of which job armed it (see AnyJobStamp).
        internal static bool Restore(long expectedJobStamp)
        {
            var record = ReadRecord();
            if (record == null || string.IsNullOrEmpty(record.Path)) return false;

            if (expectedJobStamp != AnyJobStamp && record.JobStamp != expectedJobStamp)
            {
                // A stale record from a different run — applying it here would restore/delete a
                // file on behalf of a job it was never captured for. Leave both the record and the
                // file alone; whichever job actually owns it (or the orphan-recovery flow, which
                // uses AnyJobStamp) will deal with it.
                Debug.Log("[Build] Content-state rollback record belongs to a different run " +
                    $"(stamp {record.JobStamp}, expected {expectedJobStamp}) — skipped.");
                return false;
            }

            var backupAvailable = !string.IsNullOrEmpty(record.BackupPath) && File.Exists(record.BackupPath);
            switch (PlanRestore(record.ExistedBefore, backupAvailable, File.Exists(record.Path)))
            {
                case RestoreAction.RestoreBackup:
                    if (File.Exists(record.Path) &&
                        File.ReadAllBytes(record.Path).SequenceEqual(File.ReadAllBytes(record.BackupPath)))
                        return false; // the run never got far enough to rewrite it

                    // CleanPlayerContent (run between arming and a failure) can wipe the content
                    // output folder entirely; File.Copy throws DirectoryNotFoundException if the
                    // destination directory is gone, so recreate it first.
                    var directory = Path.GetDirectoryName(record.Path);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    File.Copy(record.BackupPath, record.Path, overwrite: true);
                    // Importing a path outside Assets/ is meaningless — only project assets need
                    // AssetDatabase to notice the change.
                    if (IsAssetDatabasePath(record.Path))
                        AssetDatabase.ImportAsset(record.Path);
                    return true;
                case RestoreAction.DeleteCurrent:
                    if (IsAssetDatabasePath(record.Path))
                        AssetDatabase.DeleteAsset(record.Path);
                    else
                    {
                        File.Delete(record.Path);
                        var metaPath = record.Path + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                    }
                    // Never trust the API's own success signal — AssetDatabase.DeleteAsset silently
                    // deletes nothing and returns false for a path outside Assets/, which used to
                    // make this method report a rollback that never happened. File.Exists is ground
                    // truth either way.
                    return !File.Exists(record.Path);
                default:
                    return false;
            }
        }
    }
}

using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class ContentStateGuardEditModeTests
    {
        // ContentStateGuard's EditorPrefs key is shared with production code: a real build could
        // have AddressablesStage armed when this suite runs. Save whatever is already under the
        // key before each test and put it back afterwards, rather than plain-Disarm()-ing in
        // TearDown, so this fixture cannot destroy a real rollback record.
        private string _savedRecord;

        [SetUp]
        public void SaveExistingRecord() =>
            _savedRecord = EditorPrefs.GetString(ContentStateGuard.Key, "");

        [TearDown]
        public void RestoreExistingRecord()
        {
            if (string.IsNullOrEmpty(_savedRecord))
                EditorPrefs.DeleteKey(ContentStateGuard.Key);
            else
                EditorPrefs.SetString(ContentStateGuard.Key, _savedRecord);
        }

        // --- PlanRestore: moved verbatim from DevStateSnapshotEditModeTests ---

        [Test]
        public void PlanRestore_ExistedWithBackup_RestoresBackup()
        {
            Assert.AreEqual(ContentStateGuard.RestoreAction.RestoreBackup,
                ContentStateGuard.PlanRestore(existedAtCapture: true, backupAvailable: true, existsNow: true));
            // Even if the run deleted the file, the backup brings it back.
            Assert.AreEqual(ContentStateGuard.RestoreAction.RestoreBackup,
                ContentStateGuard.PlanRestore(existedAtCapture: true, backupAvailable: true, existsNow: false));
        }

        [Test]
        public void PlanRestore_ExistedWithoutBackup_DoesNothing()
        {
            // No backup (the copy failed at capture) — restoring nothing beats writing garbage.
            Assert.AreEqual(ContentStateGuard.RestoreAction.None,
                ContentStateGuard.PlanRestore(existedAtCapture: true, backupAvailable: false, existsNow: true));
        }

        [Test]
        public void PlanRestore_BornDuringRun_IsDeleted()
        {
            // The file did not exist when the stage armed (first New Build) — a failed run
            // produced nothing, so the file it created must go.
            Assert.AreEqual(ContentStateGuard.RestoreAction.DeleteCurrent,
                ContentStateGuard.PlanRestore(existedAtCapture: false, backupAvailable: false, existsNow: true));
        }

        [Test]
        public void PlanRestore_NeverExisted_DoesNothing()
        {
            Assert.AreEqual(ContentStateGuard.RestoreAction.None,
                ContentStateGuard.PlanRestore(existedAtCapture: false, backupAvailable: false, existsNow: false));
        }

        // --- The EditorPrefs record ---

        [Test]
        public void NotArmedByDefault()
        {
            ContentStateGuard.Disarm();
            Assert.IsFalse(ContentStateGuard.IsArmed);
        }

        [Test]
        public void Arm_ThenDisarm_ClearsTheRecord()
        {
            ContentStateGuard.Arm("Assets/AddressableAssetsData/iOS/addressables_content_state.bin",
                existedBefore: true, backupPath: "Library/UnicoBuild/content_state_backup_iOS.bin",
                jobStamp: 1);
            Assert.IsTrue(ContentStateGuard.IsArmed);

            ContentStateGuard.Disarm();
            Assert.IsFalse(ContentStateGuard.IsArmed);
        }

        [Test]
        public void Arm_NormalizesWindowsSeparators()
        {
            // Path.Combine leaves '\' before the filename on Windows; AssetDatabase rejects that,
            // so Arm must normalize both stored paths to '/' regardless of host OS.
            ContentStateGuard.Arm(@"Assets\AddressableAssetsData\iOS\addressables_content_state.bin",
                existedBefore: true, backupPath: @"Library\UnicoBuild\content_state_backup_iOS.bin",
                jobStamp: 1);

            var json = EditorPrefs.GetString(ContentStateGuard.Key, "");
            StringAssert.DoesNotContain("\\", json);
            StringAssert.Contains("Assets/AddressableAssetsData/iOS/addressables_content_state.bin", json);
            StringAssert.Contains("Library/UnicoBuild/content_state_backup_iOS.bin", json);
        }

        [Test]
        public void Arm_RejectsStampsNoFinishCouldEverMatch()
        {
            // A record is only applied when its stamp matches a live job's StartedTicksUtc, which
            // is DateTime.UtcNow.Ticks — always strictly positive. Arming with anything else buys a
            // rollback nothing will ever apply, and the failure is invisible until a build's content
            // state is found stuck at build values. Stamp 0 is the realistic mistake: it is what
            // BuildJobState.Load() reports when no job is active, i.e. what a host gets by calling
            // AddressablesStage.Execute directly.
            ContentStateGuard.Disarm(); // a real in-flight build may have armed the shared key
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ContentStateGuard.Arm(ProbePath, existedBefore: false, backupPath: "", jobStamp: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ContentStateGuard.Arm(ProbePath, existedBefore: false, backupPath: "", jobStamp: -5));
            // AnyJobStamp is the apply-side sentinel; a record carrying it would be indistinguishable
            // from "apply me to anything".
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ContentStateGuard.Arm(ProbePath, existedBefore: false, backupPath: "",
                    jobStamp: ContentStateGuard.AnyJobStamp));

            Assert.IsFalse(ContentStateGuard.IsArmed, "A rejected Arm must not write a record.");
        }

        [Test]
        public void IsArmedFor_MatchesOnlyTheOwningJob()
        {
            ContentStateGuard.Disarm();
            Assert.IsFalse(ContentStateGuard.IsArmedFor(111), "Nothing armed — nobody owns anything.");

            ContentStateGuard.Arm(ProbePath, existedBefore: false, backupPath: "", jobStamp: 111);

            Assert.IsTrue(ContentStateGuard.IsArmedFor(111));
            Assert.IsFalse(ContentStateGuard.IsArmedFor(222));
        }

        [Test]
        public void DisarmIfOwnedBy_ClearsOwnRecordAndKeepsForeignOne()
        {
            // Finish uses this instead of a bare Disarm(): Restore() deliberately declines a record
            // armed by another run and leaves it for its owner, so deleting it a line later
            // destroyed the only rollback that existed — the content-state file is git-ignored and
            // its Library/ backup dies with the next Addressables run.
            ContentStateGuard.Arm(ProbePath, existedBefore: false, backupPath: "", jobStamp: 111);
            ContentStateGuard.DisarmIfOwnedBy(222);
            Assert.IsTrue(ContentStateGuard.IsArmed, "A foreign record must survive — it is an orphan to recover, not this job's to discard.");
            Assert.IsTrue(ContentStateGuard.IsArmedFor(111), "...and it must survive unmodified.");

            ContentStateGuard.DisarmIfOwnedBy(111);
            Assert.IsFalse(ContentStateGuard.IsArmed);
        }

        [Test]
        public void CorruptRecord_DegradesWithoutThrowingAndErasesItself()
        {
            // JsonUtility.FromJson throws ArgumentException on a malformed non-empty payload, and
            // every read path leads somewhere that cannot survive a throw: Finish's finally reaches
            // IsArmedFor UPSTREAM of BuildJobState.Clear() (a throw there leaves the job Active
            // forever, and ResetStuckJob routes through the same Finish), and
            // OrphanedDevStateRecovery's [InitializeOnLoad] constructor reaches ArmedPath before its
            // Disarm() calls (a throw becomes a TypeInitializationException on every domain reload,
            // records never cleared). The record must also ERASE itself: one that cannot be parsed
            // protects nothing, and leaving it would keep IsArmed true and re-offer recovery forever.
            const string corrupt = "{ this is not a record";
            var unreadable = new Regex("unreadable");

            EditorPrefs.SetString(ContentStateGuard.Key, corrupt);
            LogAssert.Expect(LogType.Warning, unreadable);
            Assert.IsFalse(ContentStateGuard.IsArmedFor(111), "An unparseable record belongs to no job.");
            Assert.IsFalse(ContentStateGuard.IsArmed, "...and it must not survive the read.");

            EditorPrefs.SetString(ContentStateGuard.Key, corrupt);
            LogAssert.Expect(LogType.Warning, unreadable);
            Assert.AreEqual("", ContentStateGuard.ArmedPath);
            Assert.IsFalse(ContentStateGuard.IsArmed);

            EditorPrefs.SetString(ContentStateGuard.Key, corrupt);
            LogAssert.Expect(LogType.Warning, unreadable);
            Assert.IsFalse(ContentStateGuard.Restore(ContentStateGuard.AnyJobStamp),
                "Nothing can be applied from a record that cannot be read.");
            Assert.IsFalse(ContentStateGuard.IsArmed);
        }

        [Test]
        public void Restore_WhenNotArmed_DoesNothing()
        {
            ContentStateGuard.Disarm();
            Assert.IsFalse(ContentStateGuard.Restore(ContentStateGuard.AnyJobStamp));
        }

        [Test]
        public void Restore_WithAnEmptyPath_DoesNothing()
        {
            // Defensive: an armed record with no path identifies no file. Restoring must be a
            // no-op rather than throwing out of Finish's restore block.
            ContentStateGuard.Arm("", existedBefore: false, backupPath: "", jobStamp: 1);
            Assert.IsFalse(ContentStateGuard.Restore(1));
        }

        [Test]
        public void Restore_EmptyPathWithLiveBackup_IsStillANoOp()
        {
            // The case above cannot OBSERVE the empty-path guard: with ExistedBefore=false and no
            // backup, PlanRestore says None and Restore returns false whether the guard exists or
            // not (measured: deleting the guard left the whole suite green). This shape can:
            // ExistedBefore=true plus a live backup plans RestoreBackup, and without the guard the
            // restore falls through to File.Copy(backup, ""), throwing out of Finish's restore
            // block — the exact failure the guard exists to prevent.
            WriteBackup(new byte[] { 9 });
            ContentStateGuard.Arm("", existedBefore: true, backupPath: BackupPath, jobStamp: 1);
            Assert.IsFalse(ContentStateGuard.Restore(1));
        }

        [Test]
        public void Restore_MismatchedJobStamp_LeavesFileAloneAndReturnsFalse()
        {
            // A record from a different (unrelated) job must never be applied — only disarmed by
            // whichever flow owns that other job, or restored unconditionally via AnyJobStamp by
            // the orphan-recovery flow.
            WriteProbe(new byte[] { 1, 2, 3 });
            ContentStateGuard.Arm(ProbePath, existedBefore: false, backupPath: "", jobStamp: 111);

            var result = ContentStateGuard.Restore(222);

            Assert.IsFalse(result);
            Assert.IsTrue(File.Exists(ProbePath));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(ProbePath));
            // The record itself must also survive untouched — Restore only skips acting on it.
            Assert.IsTrue(ContentStateGuard.IsArmed);
        }

        // --- Restore(): the two outcomes that actually touch the file system, plus the
        // "never got far enough to rewrite it" short-circuit inside RestoreBackup. Restore() calls
        // AssetDatabase.ImportAsset/DeleteAsset, which only operate on paths under Assets/, so
        // these tests create a real temp folder there and delete it (folder + generated .meta) in
        // TearDown. The backup file lives under Library/ — exactly like production's
        // DevStateSnapshot/AddressablesStage backups — since Restore() only ever touches it with
        // raw File I/O, never through AssetDatabase.

        private const string TestFolder = "Assets/__ContentStateGuardTests__";
        private const string ProbePath = TestFolder + "/probe.bin";
        private static readonly string BackupFolder = Path.Combine("Library", "ContentStateGuardTests");
        private static readonly string BackupPath = Path.Combine(BackupFolder, "probe_backup.bin");

        // A path outside Assets/ (e.g. Local.BuildPath under Library/) — used to cover the
        // File.Delete/File.Copy branches that AssetDatabase never sees.
        private static readonly string OutsideAssetsFolder = Path.Combine("Library", "ContentStateGuardTests_Outside");
        private static readonly string OutsideAssetsPath = Path.Combine(OutsideAssetsFolder, "content_state.bin");

        [TearDown]
        public void CleanUpTestAssets()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.DeleteAsset(TestFolder);

            if (Directory.Exists(BackupFolder))
                Directory.Delete(BackupFolder, recursive: true);

            if (Directory.Exists(OutsideAssetsFolder))
                Directory.Delete(OutsideAssetsFolder, recursive: true);
        }

        private static void WriteProbe(byte[] bytes)
        {
            if (!AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.CreateFolder("Assets", "__ContentStateGuardTests__");
            File.WriteAllBytes(ProbePath, bytes);
            AssetDatabase.ImportAsset(ProbePath);
        }

        private static void WriteBackup(byte[] bytes)
        {
            Directory.CreateDirectory(BackupFolder);
            File.WriteAllBytes(BackupPath, bytes);
        }

        [Test]
        public void Restore_BackupDiffersFromCurrent_CopiesBackupOverCurrentFile()
        {
            WriteProbe(new byte[] { 1, 2, 3 });
            WriteBackup(new byte[] { 9, 9, 9, 9 });
            ContentStateGuard.Arm(ProbePath, existedBefore: true, backupPath: BackupPath, jobStamp: 1);

            var result = ContentStateGuard.Restore(1);

            Assert.IsTrue(result);
            // Pin the EXPECTED bytes rather than re-reading BackupPath: File.Copy equalises both
            // files whichever way it runs, so comparing them to each other after the fact would
            // pass even if the copy direction were reversed.
            CollectionAssert.AreEqual(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(ProbePath));
            CollectionAssert.AreEqual(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(BackupPath));
        }

        [Test]
        public void Restore_AnyJobStamp_AppliesARecordArmedByADifferentJob()
        {
            // The whole point of the sentinel: OrphanedDevStateRecovery runs after a crash, in a new
            // editor session with no job to compare against, and the record it must apply was armed
            // by the run that died. Restore(111) would decline this record; Restore(AnyJobStamp)
            // must actually touch the file system.
            WriteProbe(new byte[] { 1, 2, 3 });
            WriteBackup(new byte[] { 9, 9, 9, 9 });
            ContentStateGuard.Arm(ProbePath, existedBefore: true, backupPath: BackupPath, jobStamp: 111);

            var result = ContentStateGuard.Restore(ContentStateGuard.AnyJobStamp);

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(ProbePath));
        }

        [Test]
        public void Restore_CurrentAlreadyMatchesBackup_ReturnsFalseWithoutRewriting()
        {
            // The run never got far enough to rewrite the content-state file — Restore() must
            // recognize that and skip the copy rather than reporting a rollback that never happened.
            var bytes = new byte[] { 5, 6, 7, 8 };
            WriteProbe(bytes);
            WriteBackup(bytes);
            // Pin the mtime far in the past: any real File.Copy stamps "now", so an unchanged
            // mtime is unambiguous proof no copy occurred, regardless of filesystem timestamp
            // resolution.
            var pinnedTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(ProbePath, pinnedTime);

            ContentStateGuard.Arm(ProbePath, existedBefore: true, backupPath: BackupPath, jobStamp: 1);
            var result = ContentStateGuard.Restore(1);

            Assert.IsFalse(result);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(ProbePath));
            Assert.AreEqual(pinnedTime, File.GetLastWriteTimeUtc(ProbePath));
        }

        [Test]
        public void Restore_FileBornDuringRun_DeletesIt()
        {
            // existedBefore: false — the file did not exist when the guard armed (first "New
            // Build"); it exists now only because this run created it and then failed, so the
            // rollback is to remove it, not restore a backup that never existed.
            WriteProbe(new byte[] { 1, 2, 3 });
            ContentStateGuard.Arm(ProbePath, existedBefore: false, backupPath: "", jobStamp: 1);

            var result = ContentStateGuard.Restore(1);

            Assert.IsTrue(result);
            Assert.IsFalse(File.Exists(ProbePath));
        }

        [Test]
        public void Restore_FileBornDuringRun_OutsideAssets_DeletesItAndItsMeta()
        {
            // A content-state path outside Assets/ (Local.BuildPath under Library/, Remote.BuildPath
            // under ServerData/) must be removed with File.Delete — AssetDatabase.DeleteAsset
            // silently no-ops (and reports failure) for anything outside Assets/.
            Directory.CreateDirectory(OutsideAssetsFolder);
            File.WriteAllBytes(OutsideAssetsPath, new byte[] { 1, 2, 3 });
            var metaPath = OutsideAssetsPath + ".meta";
            File.WriteAllText(metaPath, "fake meta");
            ContentStateGuard.Arm(OutsideAssetsPath, existedBefore: false, backupPath: "", jobStamp: 1);

            var result = ContentStateGuard.Restore(1);

            Assert.IsTrue(result);
            Assert.IsFalse(File.Exists(OutsideAssetsPath));
            Assert.IsFalse(File.Exists(metaPath));
        }

        [Test]
        public void Restore_ContentDirectoryWasWiped_RecreatesItBeforeRestoringBackup()
        {
            // CleanPlayerContent (run between arming and a failure) can delete the whole content
            // output folder; File.Copy throws DirectoryNotFoundException unless Restore recreates
            // the directory first.
            WriteBackup(new byte[] { 4, 5, 6 });
            if (Directory.Exists(OutsideAssetsFolder))
                Directory.Delete(OutsideAssetsFolder, recursive: true);
            Assert.IsFalse(Directory.Exists(OutsideAssetsFolder), "Precondition: directory must not exist yet.");
            ContentStateGuard.Arm(OutsideAssetsPath, existedBefore: true, backupPath: BackupPath, jobStamp: 1);

            var result = ContentStateGuard.Restore(1);

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, File.ReadAllBytes(OutsideAssetsPath));
        }
    }
}

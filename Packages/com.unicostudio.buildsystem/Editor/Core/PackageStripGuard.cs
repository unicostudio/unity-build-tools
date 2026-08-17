using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    /// <summary>
    /// Build-scoped UPM package removal support (StripPackages, spec:
    /// docs/specs/2026-08-17-strippackages-design.md). Mirrors ContentStateGuard's shape:
    /// a job-stamped EditorPrefs record pointing at byte backups of manifest.json and
    /// packages-lock.json under Library/UnicoBuild/, armed by the stage that edits the
    /// manifest and disarmed on restore. Restore is a byte copy — measured 2026-08-17:
    /// with exact-pinned dependencies the full remove/re-add cycle brings manifest, lock
    /// and ProjectSettings back byte-identical.
    ///
    /// The strip-direction manifest edit is textual and line-based on purpose: this
    /// package has no JSON library dependency, byte fidelity is only needed on the
    /// restore side (which uses the backups), and a line-based removal with dangling-comma
    /// repair keeps the intermediate manifest valid for Unity's resolver.
    /// </summary>
    internal static class PackageStripGuard
    {
        // EditorPrefs is machine-global; key on the checkout path, matching
        // OrphanedDevStateRecovery and ContentStateGuard.
        private static readonly string s_key = $"UnicoBuild.PackageStrip.{Application.dataPath}";

        // Tests save/restore the live value around each case (fixture pattern shared with
        // ContentStateGuardEditModeTests) instead of inventing a parallel key.
        internal static string Key => s_key;

        [Serializable]
        private class Record
        {
            public string ManifestBackupPath;
            public string LockBackupPath;
            public long JobStampTicks;
        }

        internal static bool IsArmed => !string.IsNullOrEmpty(EditorPrefs.GetString(s_key, ""));

        internal static string RecordJson => EditorPrefs.GetString(s_key, "");

        private static Record ReadRecord()
        {
            var json = EditorPrefs.GetString(s_key, "");
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<Record>(json); }
            catch { return null; }
        }

        // Crash recovery has no current job to compare a stamp against — a crashed run's
        // record is exactly what it exists to apply. Same bypass shape as ContentStateGuard.
        internal const long AnyJobStamp = long.MinValue;

        internal static bool IsArmedFor(long jobStamp)
        {
            var record = ReadRecord();
            if (record == null) return false;
            return jobStamp == AnyJobStamp || record.JobStampTicks == jobStamp;
        }

        internal static (string manifestBackup, string lockBackup) ArmedBackups()
        {
            var record = ReadRecord();
            return (record?.ManifestBackupPath ?? "", record?.LockBackupPath ?? "");
        }

        internal static void Arm(string manifestBackupPath, string lockBackupPath, long jobStamp)
        {
            EditorPrefs.SetString(s_key, JsonUtility.ToJson(new Record
            {
                ManifestBackupPath = manifestBackupPath,
                LockBackupPath = lockBackupPath,
                JobStampTicks = jobStamp,
            }));
        }

        internal static void DisarmIfOwnedBy(long jobStamp)
        {
            if (IsArmedFor(jobStamp)) Disarm();
        }

        internal static void Disarm() => EditorPrefs.DeleteKey(s_key);

        /// <summary>
        /// Unconditional-direction restore for the owning job (success and failure alike,
        /// unlike version rollbacks): puts the backed-up manifest/lock bytes back, disarms,
        /// and asks the package manager to re-resolve. The caller must treat the queued
        /// reload like the define-restore path does (hold the next job until it lands).
        /// </summary>
        internal static bool RestoreIfArmedFor(long jobStamp)
        {
            if (!IsArmedFor(jobStamp)) return false;
            var (manifestBackup, lockBackup) = ArmedBackups();
            if (!string.IsNullOrEmpty(manifestBackup) && System.IO.File.Exists(manifestBackup))
                System.IO.File.Copy(manifestBackup, "Packages/manifest.json", overwrite: true);
            if (!string.IsNullOrEmpty(lockBackup) && System.IO.File.Exists(lockBackup))
                System.IO.File.Copy(lockBackup, "Packages/packages-lock.json", overwrite: true);
            Disarm();
            UnityEditor.PackageManager.Client.Resolve();
            return true;
        }

        /// <summary>
        /// Pure manifest editor (unit-tested): drops the listed dependency lines and
        /// repairs the dangling comma a last-entry removal leaves behind. Absent ids are
        /// a no-op and the input text is returned unchanged when nothing was removed.
        /// </summary>
        internal static (string text, List<string> removed) RemoveDependencies(
            string manifestText, IReadOnlyList<string> packageIds)
        {
            var removed = new List<string>();
            var lines = manifestText.Split('\n').ToList();
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                var id = packageIds.FirstOrDefault(p =>
                    Regex.IsMatch(lines[i], $"^\\s*\"{Regex.Escape(p)}\"\\s*:"));
                if (id == null) continue;
                lines.RemoveAt(i);
                removed.Add(id);
            }
            if (removed.Count == 0) return (manifestText, removed);
            removed.Reverse(); // manifest order, not reverse-scan order

            // Dangling-comma repair: a line ending in ',' whose next non-blank line closes
            // an object/array lost its successor — strip the comma so the JSON stays valid.
            for (var i = 0; i < lines.Count - 1; i++)
            {
                if (!lines[i].TrimEnd().EndsWith(",")) continue;
                var next = lines.Skip(i + 1).FirstOrDefault(l => l.Trim().Length > 0);
                if (next == null) continue;
                var head = next.TrimStart()[0];
                if (head == '}' || head == ']')
                    lines[i] = lines[i].TrimEnd().TrimEnd(',');
            }
            return (string.Join("\n", lines), removed);
        }

        /// <summary>
        /// Pure preflight core (unit-tested): a strippable dependency must be exact —
        /// an exact semver for registry packages or a '#'-anchored git URL — because the
        /// byte-deterministic restore measured in the spec holds only for exact pins.
        /// </summary>
        internal static bool IsExactPinned(string dependencyValue)
        {
            if (string.IsNullOrEmpty(dependencyValue)) return false;
            if (dependencyValue.Contains("#")) return true;
            return Regex.IsMatch(dependencyValue, @"^\d+\.\d+\.\d+([\-+].*)?$");
        }

        /// <summary>
        /// Pure dependents heuristic over packages-lock.json text (unit-tested): a package
        /// id appears once as its own entry key and once per dependent's "dependencies"
        /// map, so a count above one means something depends on it. Text-level on purpose —
        /// this package carries no JSON parser; the id is matched as an exact quoted key.
        /// </summary>
        internal static int CountKeyOccurrences(string lockText, string packageId)
        {
            return Regex.Matches(lockText, $"\"{Regex.Escape(packageId)}\"\\s*:").Count;
        }
    }
}

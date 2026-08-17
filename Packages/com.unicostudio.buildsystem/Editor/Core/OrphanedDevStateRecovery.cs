using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    // A build job's undo record (DevStateSnapshot) lives in SessionState, which an editor
    // crash/quit mid-build wipes — leaving stripped defines, the app-bundle flag, the debug-symbol
    // level and the addressables profile silently stuck at build values. This mirror survives
    // restarts in EditorPrefs: Start arms it, Finish disarms it, and a session where the mirror is
    // still armed but NO SessionState job is active means the previous session died mid-build.
    [InitializeOnLoad]
    public static class OrphanedDevStateRecovery
    {
        // EditorPrefs is machine-global; key on the checkout path — multiple clones of this repo
        // share the same applicationIdentifier, and dataPath is also stable across platform switches.
        private static readonly string s_key = "UnicoBuild.OrphanSnapshot." + Application.dataPath;

        static OrphanedDevStateRecovery()
        {
            // Mid-build domain reloads run this too — an ACTIVE job is in progress, not orphaned.
            if (BuildJobState.Load().Active) return;
            // The dev-state mirror and the content-state record are INDEPENDENT: Finish clears the
            // mirror when the restore succeeded and the content-state record only when this job
            // owns it, so either can outlive the other. Checking only the mirror would strand a
            // guard record here with nothing left to offer it.
            var json = EditorPrefs.GetString(s_key, "");
            if (string.IsNullOrEmpty(json) && !ContentStateGuard.IsArmed && !PackageStripGuard.IsArmed) return;
            // No human in the loop to judge whether these leftovers are still relevant, and a CI
            // run is normally seconds away from capturing its own dev state — silently mutating the
            // workspace from a stale EditorPrefs record would be invisible in the log and wrong.
            // NOTE: the old justification for this branch ("CI agents assume clean checkouts") does
            // NOT hold for the content state: addressables_content_state.bin is git-ignored, so a
            // clean checkout never carries it, and an Update-Previous build hard-fails without it —
            // any agent capable of Update-Previous therefore persists that file across runs by
            // definition. Dropping its rollback record silently would leave the agent's content
            // state at the interrupted run's values with no signal at all, hence the warning names
            // the record and its path.
            if (Application.isBatchMode)
            {
                if (!string.IsNullOrEmpty(json))
                    Debug.LogWarning("[Build] Orphaned dev-state snapshot found in batchmode — NOT auto-restoring. Snapshot: " + json);
                // Read the raw payload BEFORE ArmedPath: an unparseable record makes ArmedPath erase
                // itself (ContentStateGuard.ReadRecord never throws), which would empty RecordJson
                // out from under this very log line — the one thing an agent could still act on.
                var contentJson = ContentStateGuard.RecordJson;
                if (!string.IsNullOrEmpty(contentJson))
                    Debug.LogWarning("[Build] Orphaned addressables content-state rollback record found in batchmode — " +
                        $"NOT auto-restoring; '{ContentStateGuard.ArmedPath}' is left exactly as the interrupted run " +
                        $"left it, which will corrupt the next Update-Previous build if that run had rewritten it. " +
                        $"Record: {contentJson}");
                var stripJson = PackageStripGuard.RecordJson;
                if (!string.IsNullOrEmpty(stripJson))
                    Debug.LogWarning("[Build] Orphaned package-strip record found in batchmode — NOT auto-restoring; " +
                        "Packages/manifest.json (and the lock) may still be missing the stripped packages. Unlike " +
                        "the records above these are TRACKED files: `git checkout -- Packages/` heals the checkout, " +
                        $"and fresh CI checkouts are unaffected. Record: {stripJson}");
                Disarm();
                ContentStateGuard.Disarm();
                PackageStripGuard.Disarm();
                return;
            }
            // Dialogs are unsafe during InitializeOnLoad; defer to the first editor tick. Offer
            // re-reads both records rather than closing over them: a domain reload can land between
            // here and the deferred call, and acting on a stale copy is precisely the bug the
            // records exist to prevent.
            EditorApplication.delayCall += Offer;
        }

        public static void Arm(string snapshotJson) => EditorPrefs.SetString(s_key, snapshotJson);

        public static void Disarm() => EditorPrefs.DeleteKey(s_key);

        private static void Offer()
        {
            var json = EditorPrefs.GetString(s_key, "");
            var contentRecord = ContentStateGuard.RecordJson;
            var stripRecord = PackageStripGuard.RecordJson;
            var hasSnapshot = !string.IsNullOrEmpty(json);
            var hasContentState = !string.IsNullOrEmpty(contentRecord);
            var hasStrippedPackages = !string.IsNullOrEmpty(stripRecord);
            // Either record can have been cleared between the constructor and this deferred call
            // (UnicoBuildCli's CI pre-clean, a new job's Finish, a second reload). With nothing
            // left, there is nothing to ask about.
            if (!hasSnapshot && !hasContentState && !hasStrippedPackages) return;

            // Describe what is actually armed — the records are independent, and promising to
            // restore a dev state that is not there teaches developers to distrust the dialog.
            const string devStateText = "dev state (scripting defines, app bundle flag, debug " +
                "symbols, addressables profile, version bumps)";
            const string contentStateText = "addressables content state";
            const string strippedText = "build-stripped packages (manifest/lock)";
            var parts = new List<string>(3);
            if (hasSnapshot) parts.Add(devStateText);
            if (hasContentState) parts.Add(contentStateText);
            if (hasStrippedPackages) parts.Add(strippedText);
            var what = string.Join(" and the ", parts);

            var restore = EditorUtility.DisplayDialog("Interrupted build detected",
                $"A previous build was interrupted before restoring {what}.\n\n" +
                $"Restore the captured {what} now?",
                "Restore", "Ignore");

            // Restore does real file I/O (File.Copy/Delete, Directory.CreateDirectory,
            // AssetDatabase.DeleteAsset) — a read-only file, a vanished volume or a denied
            // directory creation throws right out of the delayCall. Without containment the
            // records stayed armed, so this dialog reappeared on EVERY subsequent domain reload
            // and every "Restore" click threw again. Clear both in the finally regardless, and log
            // enough on the way out that a developer can still act by hand.
            //
            // The two restores get SEPARATE containment because the two records are independent —
            // the same reason the constructor above checks them one by one. Under a single shared
            // try, ANY throw out of snap.Restore()/RestoreVersions() jumped straight past the
            // content-state restore to the catch, and the finally then deleted the content-state
            // record ANYWAY — a rollback destroyed without ever being attempted, with no second
            // copy of it anywhere. (An earlier version of this comment offered "AssetDatabase.
            // SaveAssets failing on a VCS-locked version store" as the example — audited as
            // UNMEASURED: SaveAssets is a bare InternalCall and unwritable assets surface as
            // console errors, not managed exceptions, and this project runs no VCS provider. The
            // containment stays because it is cheap and the throw set of editor APIs is not ours
            // to pin — but do not cite that sentence as evidence a specific call throws.)
            // With each restore attempted on its own, clearing both in the finally is honest.
            try
            {
                if (!restore)
                {
                    // The offer is one-shot; keep both records in the log so they stay recoverable.
                    Debug.LogWarning("[Build] Orphaned build state ignored. Snapshot was: " +
                        (hasSnapshot ? json : "<none>") +
                        " Content-state record was: " + (hasContentState ? contentRecord : "<none>"));
                    return;
                }

                if (hasSnapshot)
                {
                    try
                    {
                        var snap = JsonUtility.FromJson<DevStateSnapshot>(json);
                        snap.Restore();
                        // A crashed run produced nothing — exactly like a failed one, its bumps roll back.
                        snap.RestoreVersions();
                        Debug.Log("[Build] Orphaned dev state restored.");
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        Debug.LogError("[Build] Orphaned dev-state restore FAILED (the content state is " +
                            "restored separately below, and was not affected by this). The snapshot has " +
                            "been cleared so this dialog does not reappear on every domain reload — " +
                            "restore by hand from the value below, this log line is the last copy. " +
                            "Snapshot was: " + json);
                    }
                }

                // The content state is part of the same undo; it lives in its own record because
                // only AddressablesStage can resolve its path authoritatively. This flow has no
                // current job to compare a stamp against — a crashed run's record is exactly what
                // it exists to apply — so it restores unconditionally via AnyJobStamp.
                if (hasContentState)
                {
                    try
                    {
                        if (ContentStateGuard.Restore(ContentStateGuard.AnyJobStamp))
                            Debug.Log("[Build] Orphaned addressables content state restored.");
                        else
                            // Restore declined: either the run never got far enough to rewrite the
                            // file, or the Library/UnicoBuild backup it needed is gone. The record is
                            // deleted in the finally either way, so the developer who explicitly
                            // asked for this rollback gets it verbatim here — same as every other
                            // path in this file that clears a record without applying it.
                            Debug.LogWarning("[Build] Orphaned addressables content state was NOT restored — " +
                                "nothing to apply (the run never rewrote the file, or its " +
                                "Library/UnicoBuild backup is gone). The file is left exactly as the " +
                                "interrupted run left it, which will corrupt the next Update-Previous " +
                                "build if that run had rewritten it. Record was: " + contentRecord);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        Debug.LogError("[Build] Orphaned addressables content-state restore FAILED. The " +
                            "record has been cleared so this dialog does not reappear on every domain " +
                            "reload — restore by hand from the value below (the content-state file is " +
                            "git-ignored and its Library/UnicoBuild backup is overwritten by the next " +
                            "Addressables run, so this log line is the last copy). Content-state record " +
                            "was: " + contentRecord);
                    }
                }

                // The package strip is the third independent undo, contained on its own for the
                // same reason as the two above. RestoreIfArmedFor copies TRACKED files back
                // (manifest/lock) — the least dangerous of the three restores, and the one a
                // developer can always redo by hand with `git checkout -- Packages/`.
                if (hasStrippedPackages)
                {
                    try
                    {
                        if (PackageStripGuard.RestoreIfArmedFor(PackageStripGuard.AnyJobStamp))
                            Debug.Log("[Build] Orphaned package strip restored (manifest/lock).");
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        Debug.LogError("[Build] Orphaned package-strip restore FAILED. The record has " +
                            "been cleared so this dialog does not reappear — the manifest and lock are " +
                            "TRACKED files, `git checkout -- Packages/` restores them by hand. Record " +
                            "was: " + stripRecord);
                    }
                }
            }
            finally
            {
                Disarm(); // whatever happened — don't ask again every reload
                ContentStateGuard.Disarm();
                PackageStripGuard.Disarm();
            }
        }
    }
}

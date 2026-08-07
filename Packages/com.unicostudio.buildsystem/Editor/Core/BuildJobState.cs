using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    [Serializable]
    public sealed class BuildJobState
    {
        private const string Key = "UnicoBuild.BuildJob";

        public bool Active;
        public int StepIndex;
        public string RequestJson;      // serialized BuildRequest
        public string SnapshotJson;     // serialized DevStateSnapshot
        public AddressablesProfile Profile;
        public long StartedTicksUtc;    // job start (UTC ticks); feeds BuildResult.DurationSeconds
        // Accumulated build progress; survives the domain reloads between stages. JsonUtility
        // serializes List<string> directly, so there is no fragile CSV/newline packing to corrupt.
        public List<string> ExtraDefines = new();
        public List<string> Steps = new();
        public List<string> Artifacts = new();
        // Parallel to Artifacts — (int)ArtifactKind per entry.
        public List<int> ArtifactKinds = new();
        // Non-Pass preflight messages recorded at Start (every WarnPolicy) — surfaced into
        // BuildResult.Warnings by Finish so CI output carries what a human would have been shown.
        public List<string> Warnings = new();

        // The FULL pipeline resolved at Start (built-ins + host steps), frozen for the job's
        // lifetime: Advance re-materializes stages from these names, so a mid-job recompile that
        // changes the discovered type set can never shift StepIndex positions.
        public List<string> StageTypeNames = new();
        public List<string> StageDisplayNames = new();   // stage.Name per entry, for progress UI
        // BuildContext.Data as parallel lists (JsonUtility cannot serialize dictionaries).
        public List<string> DataKeys = new();
        public List<string> DataValues = new();

        // Idle-tick guard: the resumer (and the panel's repaint pump) poll Load() constantly; once
        // a load has answered "no job", skip the SessionState JSON parse until Save/Clear — or the
        // domain reload that resets statics, which is exactly when re-checking matters.
        private static bool s_knownInactive;

        public static BuildJobState Load()
        {
            if (s_knownInactive) return new BuildJobState();
            var json = SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(json))
            {
                s_knownInactive = true;
                return new BuildJobState();
            }
            var state = FromJson(json);
            if (!state.Active) s_knownInactive = true;
            return state;
        }

        // DEFENSIVE ONLY on the targeted Unity. An earlier version of this comment claimed
        // JsonUtility skips field initializers — measured FALSE on 6000.0.62f1 (same probe as
        // BuildResult.Normalize, CHANGELOG 0.9.2): FromJson runs them, so the collections below
        // are non-null out of the box and these ??= are insurance against a future Unity changing
        // that. RequestJson/SnapshotJson carry NO initializers and genuinely can be null — that is
        // initializer absence, not JsonUtility behavior. (An earlier version of this sentence
        // claimed "callers null-check them" — audited FALSE: Finish parses SnapshotJson and
        // dereferences the result, relying on its own null guard over the PARSE result instead,
        // because FromJson returns null rather than throwing on a null/empty payload. Claims about
        // callers belong at the call sites; this comment now only states what THIS type
        // guarantees: nothing, for these two fields.)
        // Internal for the EditMode tests — Load itself is SessionState-coupled.
        internal static BuildJobState FromJson(string json)
        {
            var state = JsonUtility.FromJson<BuildJobState>(json);
            state.ExtraDefines ??= new();
            state.Steps ??= new();
            state.Artifacts ??= new();
            state.ArtifactKinds ??= new();
            state.Warnings ??= new();
            state.StageTypeNames ??= new();
            state.StageDisplayNames ??= new();
            state.DataKeys ??= new();
            state.DataValues ??= new();
            return state;
        }

        public void SetData(System.Collections.Generic.Dictionary<string, string> data)
        {
            DataKeys.Clear();
            DataValues.Clear();
            foreach (var kv in data)
            {
                DataKeys.Add(kv.Key);
                // JsonUtility cannot round-trip a null string inside a list (it deserializes as
                // ""); normalize at write time so the behavior is explicit, not serializer-luck.
                DataValues.Add(kv.Value ?? "");
            }
        }

        public System.Collections.Generic.Dictionary<string, string> GetData()
        {
            var data = new System.Collections.Generic.Dictionary<string, string>();
            for (var i = 0; i < DataKeys.Count && i < DataValues.Count; i++)
                data[DataKeys[i]] = DataValues[i];
            return data;
        }

        public void Save()
        {
            s_knownInactive = false;
            SessionState.SetString(Key, JsonUtility.ToJson(this));
        }

        public static void Clear()
        {
            s_knownInactive = true;
            SessionState.EraseString(Key);
        }
    }
}

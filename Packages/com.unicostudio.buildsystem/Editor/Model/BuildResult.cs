using System;
using System.Collections.Generic;

namespace UnicoStudio.BuildSystem.Editor
{
    [Serializable]
    public sealed class BuildArtifact
    {
        public string Path = "";
        public ArtifactKind Kind = ArtifactKind.Unknown;
    }

    [Serializable]
    public sealed class BuildResult
    {
        public bool Success;
        public string Error = "";
        public double DurationSeconds;  // 0 = unknown (legacy result)
        public List<string> Steps = new List<string>();
        public List<string> Artifacts = new List<string>();   // paths only — kept for back-compat

        // v2 (all additive; JsonUtility defaults them for legacy payloads):
        public List<string> Warnings = new List<string>();
        public List<BuildArtifact> TypedArtifacts = new List<BuildArtifact>();
        public string VersionName = "";
        public string BuildCode = "";                 // Android code / iOS build number, as text
        public int AddressablesVersion = -1;          // -1 = store missing or addressables unused
        public string StartedUtc = "";                // ISO 8601 ("o"); "" = legacy result
        public string EndedUtc = "";

        // DEFENSIVE ONLY on the targeted Unity. An earlier version of this comment claimed
        // JsonUtility skips field initializers and leaves a legacy payload's collections null and
        // AddressablesVersion at 0 — measured FALSE on 6000.0.62f1 (probe: FromJson of a payload
        // without the v2 fields yields non-null collections and the initializer's -1), and the
        // false claim sent a code audit chasing a phantom defect. Normalize stays because callers
        // already route through it and it is cheap insurance against a future Unity changing that
        // behavior, but nothing currently reaches the null branches.
        public BuildResult Normalize()
        {
            Steps ??= new List<string>();
            Artifacts ??= new List<string>();
            Warnings ??= new List<string>();
            TypedArtifacts ??= new List<BuildArtifact>();
            Error ??= "";
            VersionName ??= "";
            BuildCode ??= "";
            StartedUtc ??= "";
            EndedUtc ??= "";
            return this;
        }
    }
}

using System.Collections.Generic;

namespace UnicoStudio.BuildSystem.Editor
{
    public sealed class BuildContext
    {
        public BuildRequest Request { get; }
        public List<string> Steps { get; } = new();
        public List<string> Artifacts { get; } = new();
        public List<int> ArtifactKinds { get; } = new();

        // Additive scripting defines for the player build (no reload). Populated by earlier stages
        // and by PreBuild hooks (the documented way to contribute build-only defines);
        // ConfigureDefinesStage merges its own resolved additions into this list rather than
        // replacing it, and evicts anything the build strips.
        public List<string> ExtraScriptingDefines { get; } = new();

        // Cross-stage scratch data (e.g. a PreBuild hook's git hash read by a later stage).
        // Persisted through BuildJobState as parallel lists, so it survives domain reloads.
        public Dictionary<string, string> Data { get; } = new();

        // Addressables profile to activate, resolved from BuildTargetConfig via
        // BuildConfigCatalog.ResolveProfile. EVERY caller must resolve it before running preflight
        // checks, not just the orchestrator: the panel builds its own display context too, and
        // RemotePathCheck validates whichever profile it finds here. Leaving the field at its
        // initialiser would silently check Test while the build runs Production.
        public AddressablesProfile Profile { get; set; } = AddressablesProfile.Test;

        public BuildContext(BuildRequest request) => Request = request;

        // Q5 signal path: a stage (or host hook) that queues a domain reload through
        // something OTHER than a global-define change — e.g. StripPackages' manifest edit
        // + Client.Resolve — raises this so Advance stops exactly like the defines-hash
        // path and resumes after the reload lands. Consume-once and same-tick by design:
        // Advance reads it immediately after Execute, so it needs no persistence.
        private bool _reloadRequested;

        public void RequestReload() => _reloadRequested = true;

        public bool ConsumeReloadRequest()
        {
            var requested = _reloadRequested;
            _reloadRequested = false;
            return requested;
        }

        public void AddStep(string step) => Steps.Add(step);

        public void AddArtifact(string path) => AddArtifact(path, ArtifactKind.Unknown);

        public void AddArtifact(string path, ArtifactKind kind)
        {
            Artifacts.Add(path);
            ArtifactKinds.Add((int)kind);
        }
    }
}

namespace UnicoStudio.BuildSystem.Editor
{
    // Integration flag: true for the duration of a panel-driven build so the legacy
    // UnicoBuildPreprocessor suppresses its modal dialogs. Owned here; UnicoBuildService
    // toggles it via Begin()/End() (set on each Advance, cleared in Finish — spans domain reloads).
    public static class BuildSession
    {
        public static bool IsBuildingViaPanel { get; private set; }

        public static void Begin() => IsBuildingViaPanel = true;
        public static void End() => IsBuildingViaPanel = false;
    }
}

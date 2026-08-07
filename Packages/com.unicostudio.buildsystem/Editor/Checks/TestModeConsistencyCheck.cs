using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    public sealed class TestModeConsistencyCheck : IPreflightCheck
    {
        // This check's Warn is self-healing in BOTH directions: ConfigureDefinesStage writes the
        // kind rule into the global defines either way (adds the define for a Test build that lacks
        // it, removes it for a Release build that has it) and the snapshot restores the developer's
        // state afterwards. Under WarnPolicy.Strict it must stay advisory, or a clean checkout would
        // hard-fail CI on exactly the mismatch the build is about to correct.
        public bool IsStrictExempt => true;

        public static CheckResult Evaluate(BuildKind kind, bool testModeOn, string defineName)
        {
            var wantOn = kind == BuildKind.Test;
            if (wantOn == testModeOn)
                return CheckResult.Pass($"{defineName} {(testModeOn ? "on" : "off")} matches {kind}.");
            return CheckResult.Warn(
                $"{defineName} is {(testModeOn ? "on" : "off")} but {kind} expects {(wantOn ? "on" : "off")} " +
                "(the build stage will fix this).");
        }

        public CheckResult Run(BuildContext ctx)
        {
            var define = BuildSystemSettings.ResolveTestModeDefine();
            var on = BuildDefineResolver.HasDefine(
                PlayerSettings.GetScriptingDefineSymbols(ctx.Request.Platform.ToNamedBuildTarget()), define);
            return Evaluate(ctx.Request.Kind, on, define);
        }
    }
}

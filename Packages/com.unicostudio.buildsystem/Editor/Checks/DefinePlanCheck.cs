using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    // Surfaces the build's effective define plan before it runs — and blocks on config-authoring
    // errors (invalid tokens, a define in both lists) that Resolve would otherwise drop silently.
    public sealed class DefinePlanCheck : IPreflightCheck
    {
        public static CheckResult Evaluate(IReadOnlyList<DefineConfigIssue> issues, DefineDelta delta)
        {
            var blocking = issues.Where(i => i.Blocking).Select(i => i.Message).ToArray();
            if (blocking.Length > 0)
                return CheckResult.Block(string.Join(" ", blocking));

            if (issues.Count > 0)
                return CheckResult.Warn(string.Join(" ", issues.Select(i => i.Message)));

            var parts = new List<string>();
            if (delta.AddViaExtra.Length > 0)
                parts.Add($"+{string.Join(",", delta.AddViaExtra)} (build-only)");
            if (delta.AddToGlobal.Length > 0)
                parts.Add($"+{string.Join(",", delta.AddToGlobal)} (global, restored after the build)");
            if (delta.RemoveFromGlobal.Length > 0)
                parts.Add($"-{string.Join(",", delta.RemoveFromGlobal)} (global, restored after the build)");
            return CheckResult.Pass(parts.Count > 0
                ? $"Defines: {string.Join("; ", parts)}."
                : "Defines: no changes needed.");
        }

        public CheckResult Run(BuildContext ctx)
        {
            var platform = ctx.Request.Platform;
            var cfg = BuildConfigCatalog.Find(BuildConfigCatalog.LoadAll(), platform, ctx.Request.Kind);
            var extra = cfg ? cfg.ExtraDefines : null;
            var strip = cfg ? cfg.StripDefines : null;
            var testDefine = BuildSystemSettings.ResolveTestModeDefine();

            var current = PlayerSettings.GetScriptingDefineSymbols(platform.ToNamedBuildTarget())
                .Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

            return Evaluate(BuildDefineResolver.ValidateConfig(extra, strip, testDefine),
                BuildDefineResolver.Resolve(ctx.Request.Kind, current, extra, strip, testDefine));
        }
    }
}

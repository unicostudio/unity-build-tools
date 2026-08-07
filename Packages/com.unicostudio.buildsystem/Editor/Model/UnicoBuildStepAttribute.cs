using System;

namespace UnicoStudio.BuildSystem.Editor
{
    // Where a host-project build step runs relative to the built-in pipeline. Anchors are
    // POSITIONAL: BeforeAddressables/BeforePlayer run at their position even when the optional
    // stage behind them is disabled — steps self-filter via ctx.Request when they care.
    public enum BuildStepAnchor { PreBuild, BeforeAddressables, BeforePlayer, PostSuccess }

    // Registers an IBuildStage (or, for PostSuccess, an IPostSuccessStep) as a host build step,
    // discovered via TypeCache. Ordering inside an anchor is Order, then full type name
    // (ordinal) — fully deterministic, because StepIndex-based resume depends on it.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class UnicoBuildStepAttribute : Attribute
    {
        public BuildStepAnchor Anchor { get; }
        public int Order { get; }

        public UnicoBuildStepAttribute(BuildStepAnchor anchor, int order = 0)
        {
            Anchor = anchor;
            Order = order;
        }
    }
}

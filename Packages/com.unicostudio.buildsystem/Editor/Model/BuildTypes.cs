using System;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    public enum BuildPlatform { Android, [InspectorName("iOS")] iOS }

    public enum BuildKind { Test, Release }

    public enum AddressablesMode { UpdatePrevious, NewBuild }

    // Player-data compression; maps to BuildOptions flags in PlayerBuildStage (scripted builds
    // ignore the Build Settings window's choice). LZ4HC is member 0 so the enum default — and a
    // legacy request JSON missing the field — lands on the project standard, not on Default.
    public enum CompressionKind { LZ4HC, LZ4, Default }

    public enum CheckSeverity { Pass, Warn, Block }

    // How Start treats Warn-severity preflight results. Interactive: warns are advisory (the
    // panel gates them behind its dialog). Strict: any non-exempt Warn blocks the job — CI must
    // never silently build through an advisory a human would have been asked to acknowledge.
    public enum WarnPolicy { Interactive, Strict }

    // What a recorded artifact IS — CI consumes the result JSON and must not guess from paths.
    // Appended only — never inserted. BuildResult round-trips through JsonUtility, which writes
    // enum values as integers, so inserting a member re-maps every persisted artifact kind.
    public enum ArtifactKind { Unknown, Apk, Aab, SymbolsZip, XcodeProject, AddressablesContent, Metadata }

    [Flags]
    public enum OutputKind { None = 0, Apk = 1, Aab = 2, XcodeProject = 4 }

    public readonly struct CheckResult
    {
        public readonly CheckSeverity Severity;
        public readonly string Message;

        private CheckResult(CheckSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public static CheckResult Pass(string m) => new(CheckSeverity.Pass, m);
        public static CheckResult Warn(string m) => new(CheckSeverity.Warn, m);
        public static CheckResult Block(string m) => new(CheckSeverity.Block, m);
    }

    public interface IPreflightCheck
    {
        CheckResult Run(BuildContext ctx);

        // Strict-exempt checks stay advisory even under WarnPolicy.Strict — reserved for checks
        // that ALWAYS warn on a legitimate flow (CdnReminderCheck warns on every Release content
        // build) and would otherwise make strict Release builds impossible.
        bool IsStrictExempt => false;
    }

    public interface IBuildStage
    {
        string Name { get; }
        void Execute(BuildContext ctx);
    }

    // A step that runs only after the WHOLE job succeeded — after dev-state restore and after
    // LatestResult is recorded (see PostSuccessRunner). Failures are logged and appended to the
    // result's steps; they never flip the build's Success.
    public interface IPostSuccessStep
    {
        string Name { get; }
        void Execute(BuildResult result);
    }

    // Read-only progress surface the panel renders while a job runs (UnicoBuildService.GetProgress).
    public sealed class BuildProgress
    {
        public int StageNumber;         // 1-based
        public int StageCount;
        public string CurrentStageName;
        public System.Collections.Generic.IReadOnlyList<string> Steps;
    }
}

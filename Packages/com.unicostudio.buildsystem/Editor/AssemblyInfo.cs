using System.Runtime.CompilerServices;

// The EditMode tests exercise internal seams (UnicoBuildService.Stages, BuildJobState.FromJson,
// PlayerBuildStage.OptionsFor, AddressablesStage.FailureOf) from their own assembly. It is also
// the whole reach ContentStateGuardEditModeTests has: that type is internal ON PURPOSE (its
// AnyJobStamp sentinel bypasses the ownership check by definition), so this attribute is what
// keeps it testable without exposing it to host code.
[assembly: InternalsVisibleTo("UnicoStudio.BuildSystem.Editor.Tests")]

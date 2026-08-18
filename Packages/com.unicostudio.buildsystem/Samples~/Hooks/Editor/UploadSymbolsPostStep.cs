// Sample post-success step: runs ONLY after the whole job succeeded — dev state restored,
// result recorded. A failure here never flips the build's Success; it surfaces in the panel as
// "Post step FAILED". Replace the Debug.Log with your real upload tooling (Play Console,
// Crashlytics, internal CDN...).
using System.Linq;
using UnicoStudio.BuildSystem.Editor;
using UnityEngine;

[UnicoBuildStep(BuildStepAnchor.PostSuccess)]
public sealed class UploadSymbolsPostStep : IPostSuccessStep
{
    public string Name => "Upload symbols";

    public void Execute(BuildResult result)
    {
        var symbols = result.Artifacts.FirstOrDefault(a => a.EndsWith(".symbols.zip"));
        if (symbols == null)
        {
            Debug.Log("[Build] No symbols.zip artifact; nothing to upload.");
            return;
        }
        Debug.Log($"[Build] Would upload: {symbols}");
    }
}

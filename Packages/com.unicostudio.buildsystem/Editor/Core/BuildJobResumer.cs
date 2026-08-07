using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    [InitializeOnLoad]
    public static class BuildJobResumer
    {
        static BuildJobResumer()
        {
            EditorApplication.update += OnUpdate;
        }

        private static void OnUpdate()
        {
            if (!BuildJobState.Load().Active) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            UnicoBuildService.Advance();
        }
    }
}

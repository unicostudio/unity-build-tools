using UnityEditor;

namespace UnicoStudio.UnicoLibs.VersionTracker
{
    public static class UnicoVersionTrackerProgressBar
    {
        private const string TITLE = "UnicoVersionTracker is working";
        private const string INFO = "Please wait...";
        private static bool s_isLoading;
        private static float s_progress;

        public static void StartLoading()
        {
            if (s_isLoading) return;

            s_isLoading = true;
            EditorApplication.update += UpdateLoading;
            EditorUtility.DisplayProgressBar(TITLE, INFO, 0.1f);
        }

        public static void StopLoading()
        {
            if (!s_isLoading) return;

            s_isLoading = false;
            EditorApplication.update -= UpdateLoading;
            EditorUtility.ClearProgressBar();
        }

        private static void UpdateLoading()
        {
            if (!s_isLoading) return;

            s_progress += 0.1f;
            if (s_progress > 1f) s_progress = 0f;

            EditorUtility.DisplayProgressBar(TITLE, INFO, s_progress);
        }
    }
}
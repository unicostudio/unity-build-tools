namespace UnicoStudio.BuildSystem.Editor
{
    public static class BuildVersioning
    {
        public static int NextAndroidCode(int current) => current + 1;
        public static string NextIosBuildNumber(string current) => int.TryParse(current, out var n) ? (n + 1).ToString() : "1";
    }
}

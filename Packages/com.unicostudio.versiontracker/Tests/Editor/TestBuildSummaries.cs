using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace UnicoStudio.UnicoLibs.VersionTracker.Tests
{
    /// <summary>
    /// BuildSummary has no public constructor, and default(BuildSummary) carries
    /// (BuildTarget)0, which makes PlayerSettings helpers emit "unsupported platform"
    /// assert logs during export. This factory sets the platform fields by reflection;
    /// Single() fails loudly if Unity ever changes the struct's field layout.
    /// </summary>
    internal static class TestBuildSummaries
    {
        public static BuildSummary ForAndroid()
        {
            object boxed = default(BuildSummary);
            var fields = typeof(BuildSummary)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            fields.Single(f => f.FieldType == typeof(BuildTarget))
                .SetValue(boxed, BuildTarget.Android);
            fields.Single(f => f.FieldType == typeof(BuildTargetGroup))
                .SetValue(boxed, BuildTargetGroup.Android);

            return (BuildSummary)boxed;
        }
    }
}

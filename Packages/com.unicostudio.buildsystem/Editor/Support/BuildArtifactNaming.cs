using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    public static class BuildArtifactNaming
    {
        // Locked game-name prefix, derived from the host game's product name — single source for
        // both the panel preview and the actual filename. Sanitized to filename-safe characters
        // ("My Game X" -> "MyGameX"). Note: renaming the product in Player Settings
        // renames artifacts.
        public static string ProductPrefix => SanitizeLabel(Application.productName);

        // Keeps only [A-Za-z0-9]; the game-name prefix stays locked by the caller.
        public static string SanitizeLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return string.Empty;
            var sb = new StringBuilder(label.Length);
            foreach (var c in label)
            {
                if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                    sb.Append(c);
            }

            return sb.ToString();
        }

        public static string FileName(string productName, string label, string version,
            int code, DateTime date, BuildKind kind, string extension)
        {
            var clean = SanitizeLabel(label);
            var labelPart = clean.Length > 0 ? "_" + clean : string.Empty;
            var kindPart = kind == BuildKind.Release ? "RELEASE" : "TEST";
            var datePart = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            // No dot for the extensionless iOS case (the output is an Xcode project folder).
            var extPart = string.IsNullOrEmpty(extension) ? string.Empty : "." + extension;
            return $"{productName}{labelPart}_v{version}({code})_{datePart}_{kindPart}{extPart}";
        }
    }
}

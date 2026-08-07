using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class CompressionCheckEditModeTests
    {
        private static CheckSeverity Severity(CompressionKind compression, bool buildPlayer = true)
        {
            var req = new BuildRequest { Compression = compression, BuildPlayer = buildPlayer };
            return new CompressionCheck().Run(new BuildContext(req)).Severity;
        }

        [Test]
        public void ProjectStandardLz4Hc_Passes()
        {
            Assert.AreEqual(CheckSeverity.Pass, Severity(CompressionKind.LZ4HC));
            // A fresh request must already be on the standard.
            Assert.AreEqual(CompressionKind.LZ4HC, new BuildRequest().Compression);
        }

        [Test]
        public void Deviations_Warn()
        {
            Assert.AreEqual(CheckSeverity.Warn, Severity(CompressionKind.LZ4));
            Assert.AreEqual(CheckSeverity.Warn, Severity(CompressionKind.Default));
        }

        [Test]
        public void ContentOnlyJob_PassesRegardless()
        {
            Assert.AreEqual(CheckSeverity.Pass, Severity(CompressionKind.Default, buildPlayer: false));
        }
    }
}

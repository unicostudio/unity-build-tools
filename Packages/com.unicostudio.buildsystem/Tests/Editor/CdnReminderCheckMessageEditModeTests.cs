using NUnit.Framework;
using UnicoStudio.BuildSystem;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class CdnReminderCheckMessageEditModeTests
    {
        // The reminder names the folder the build actually stages content into. That folder's
        // prefix is UnicoBuildPaths.BUILD_PATH_BASE — writing it out a second time here would let
        // the reminder start naming a folder the build no longer uses.
        [Test]
        public void FolderFor_KnownVersion_DerivesFromTheSharedConstant()
        {
            Assert.AreEqual(UnicoBuildPaths.BUILD_PATH_BASE + "36", CdnReminderCheck.FolderFor(36));
        }

        [Test]
        public void FolderFor_UnknownVersion_KeepsThePlaceholder()
        {
            Assert.AreEqual(UnicoBuildPaths.BUILD_PATH_BASE + "{N}", CdnReminderCheck.FolderFor(-1));
        }
    }
}

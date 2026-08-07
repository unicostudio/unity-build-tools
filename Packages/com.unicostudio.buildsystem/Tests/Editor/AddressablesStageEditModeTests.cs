#if UNICO_HAS_ADDRESSABLES
using NUnit.Framework;
using UnityEditor.AddressableAssets.Build;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class AddressablesStageEditModeTests
    {
        [Test]
        public void FailureOf_NullResult_IsFailure()
        {
            Assert.IsNotNull(AddressablesStage.FailureOf(null));
        }

        [Test]
        public void FailureOf_ErrorResult_ReturnsError()
        {
            var result = AddressableAssetBuildResult.CreateResult<AddressablesPlayerBuildResult>(null, 0, "boom");
            Assert.AreEqual("boom", AddressablesStage.FailureOf(result));
        }

        [Test]
        public void FailureOf_CleanResult_IsNull()
        {
            var result = AddressableAssetBuildResult.CreateResult<AddressablesPlayerBuildResult>(null, 0);
            Assert.IsNull(AddressablesStage.FailureOf(result));
        }
    }
}
#endif

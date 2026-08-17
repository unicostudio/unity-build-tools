using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    /// <summary>
    /// The Q5 signal path: a stage that queues a domain reload through something OTHER
    /// than a global-define change (measured need: StripPackages' manifest edit +
    /// Client.Resolve) must be able to tell Advance to stop exactly like the
    /// defines-hash path does. The flag is consume-once and same-tick by design — it
    /// needs no persistence because Advance reads it immediately after Execute.
    /// </summary>
    public class BuildContextReloadRequestEditModeTests
    {
        [Test]
        public void FreshContext_HasNoPendingReloadRequest()
        {
            var ctx = new BuildContext(new BuildRequest());

            Assert.IsFalse(ctx.ConsumeReloadRequest());
        }

        [Test]
        public void RequestReload_IsObservedExactlyOnce()
        {
            var ctx = new BuildContext(new BuildRequest());

            ctx.RequestReload();

            Assert.IsTrue(ctx.ConsumeReloadRequest(), "first consume must observe the request");
            Assert.IsFalse(ctx.ConsumeReloadRequest(), "the flag is consume-once");
        }

        [Test]
        public void MultipleRequests_CollapseIntoOneObservation()
        {
            var ctx = new BuildContext(new BuildRequest());

            ctx.RequestReload();
            ctx.RequestReload();

            Assert.IsTrue(ctx.ConsumeReloadRequest());
            Assert.IsFalse(ctx.ConsumeReloadRequest());
        }
    }
}

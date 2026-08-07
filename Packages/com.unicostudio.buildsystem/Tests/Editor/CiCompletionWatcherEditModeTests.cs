using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class CiCompletionWatcherEditModeTests
    {
        private const long Now = 1_000_000;
        private const long Later = 2_000_000;

        [Test] public void Running_BeforeDeadline_Waits()
            => Assert.AreEqual(CiCompletionWatcher.WatchAction.None,
                CiCompletionWatcher.Decide(isRunning: true, hasResult: false, postSuccessPending: false,
                    resultSuccess: false, nowTicksUtc: Now, deadlineTicksUtc: Later));

        [Test] public void Deadline_Passed_TimesOut_EvenWhileRunning()
            => Assert.AreEqual(CiCompletionWatcher.WatchAction.TimeoutReset,
                CiCompletionWatcher.Decide(true, false, false, false, Later, Now));

        [Test] public void Concluded_Success_WaitsForPostSuccessQueue()
            => Assert.AreEqual(CiCompletionWatcher.WatchAction.None,
                CiCompletionWatcher.Decide(false, true, postSuccessPending: true, true, Now, Later));

        [Test] public void Concluded_Success_QueueDrained_ExitsZero()
            => Assert.AreEqual(CiCompletionWatcher.WatchAction.ExitSuccess,
                CiCompletionWatcher.Decide(false, true, false, true, Now, Later));

        [Test] public void Concluded_Failure_ExitsOne()
            => Assert.AreEqual(CiCompletionWatcher.WatchAction.ExitFailure,
                CiCompletionWatcher.Decide(false, true, false, false, Now, Later));

        [Test] public void NoJobNoResult_Waits_UntilDeadline()
            => Assert.AreEqual(CiCompletionWatcher.WatchAction.None,
                CiCompletionWatcher.Decide(false, false, false, false, Now, Later));

        // A concluded job outranks the deadline: its artifacts (and any post-success uploads that
        // already ran) are real, so a tick landing past the deadline must not report a timeout.
        [Test] public void Concluded_Success_AfterDeadline_StillExitsZero()
            => Assert.AreEqual(CiCompletionWatcher.WatchAction.ExitSuccess,
                CiCompletionWatcher.Decide(false, true, false, true, Later, Now));

        [Test] public void Concluded_Failure_AfterDeadline_StillExitsOne()
            => Assert.AreEqual(CiCompletionWatcher.WatchAction.ExitFailure,
                CiCompletionWatcher.Decide(false, true, false, false, Later, Now));

        // A queue that never drains cannot conclude on its own — the deadline stays its escape.
        [Test] public void PostSuccessPending_AfterDeadline_TimesOut()
            => Assert.AreEqual(CiCompletionWatcher.WatchAction.TimeoutReset,
                CiCompletionWatcher.Decide(false, true, postSuccessPending: true, true, Later, Now));
    }
}

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class PostSuccessRunnerEditModeTests
    {
        // Deliberately NOT [UnicoBuildStep]-marked (test types must never enter real discovery);
        // ExecuteQueue takes explicit type names, so the attribute is not needed here.
        public sealed class RecordingStep : IPostSuccessStep
        {
            public static int Calls;
            public static string LastVersion;
            public string Name => "Recording";
            public void Execute(BuildResult result) { Calls++; LastVersion = result?.Error; }
        }

        public sealed class ThrowingStep : IPostSuccessStep
        {
            public string Name => "Throwing";
            public void Execute(BuildResult result) => throw new System.InvalidOperationException("boom");
        }

        // The production SessionState result key (UnicoBuildService.RESULT_KEY is private; the
        // literal is pinned here the same way ContentStateGuard's tests pin its EditorPrefs key).
        // Save-and-restore in SetUp/TearDown so this fixture can never destroy a real session's
        // recorded result — and reset UnicoBuildService's private static cache on BOTH sides,
        // because the getter serves the cache first and a seeded result would otherwise shadow
        // the real one until the next domain reload.
        private const string ResultKey = "UnicoBuild.BuildResult";
        private string _savedResult;

        private static void ResetLatestResultCache() =>
            typeof(UnicoBuildService)
                .GetField("s_latestResult",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .SetValue(null, null);

        private static void SeedLatestResult(BuildResult result)
        {
            UnityEditor.SessionState.SetString(ResultKey, JsonUtility.ToJson(result));
            ResetLatestResultCache();   // the getter lazily reloads from SessionState
        }

        [SetUp]
        public void Reset()
        {
            RecordingStep.Calls = 0;
            RecordingStep.LastVersion = null;
            _savedResult = UnityEditor.SessionState.GetString(ResultKey, "");
        }

        [TearDown]
        public void RestoreRealResult()
        {
            if (string.IsNullOrEmpty(_savedResult))
                UnityEditor.SessionState.EraseString(ResultKey);
            else
                UnityEditor.SessionState.SetString(ResultKey, _savedResult);
            ResetLatestResultCache();
        }

        // --- run attribution: the stamp must come from the queue's OWN payload ---

        [Test]
        public void ExecuteQueue_MatchingRun_RecordsOntoTheCurrentResult()
        {
            // The positive half of attribution, previously untested: the stamp read from the
            // queue's payload must actually reach AppendPostStep and record. Measured: replacing
            // the stamp with "" left the old fixture green — nothing asserted that recording
            // happens at all.
            const string run = "2026-07-31T10:00:00.0000000Z";
            SeedLatestResult(new BuildResult { Success = true, StartedUtc = run });
            var q = new PostSuccessRunner.Queue
            {
                ResultJson = JsonUtility.ToJson(new BuildResult { Success = true, StartedUtc = run }),
            };
            q.TypeNames.Add(typeof(RecordingStep).AssemblyQualifiedName);

            PostSuccessRunner.ExecuteQueue(q);

            Assert.AreEqual(1, RecordingStep.Calls);
            StringAssert.Contains("Post: Recording OK",
                string.Join("|", UnicoBuildService.LatestResult.Steps));
        }

        [Test]
        public void ExecuteQueue_SupersededRun_IsLoggedOnly_AndLeavesTheCurrentResultUntouched()
        {
            // The defect this pins lived in the WIRING: ExecuteQueue must pass the stamp from ITS
            // OWN payload; reading the current result's stamp instead would record the outcome on
            // the newer run. The old shape matched Regex("logged only") — but all THREE rejection
            // branches contain that text, and with no seeded result the null-result branch fired
            // before the stamp was ever read. The narrow message below is unique to the branch
            // that proves the queue's stamp was read and compared.
            SeedLatestResult(new BuildResult { Success = true, StartedUtc = "2026-07-31T11:00:00.0000000Z" });
            var q = new PostSuccessRunner.Queue
            {
                ResultJson = JsonUtility.ToJson(new BuildResult
                {
                    Success = true,
                    StartedUtc = "1999-01-01T00:00:00.0000000Z",   // an earlier, replaced run
                }),
            };
            q.TypeNames.Add(typeof(RecordingStep).AssemblyQualifiedName);

            LogAssert.Expect(LogType.Log,
                new System.Text.RegularExpressions.Regex("no longer the current one"));
            PostSuccessRunner.ExecuteQueue(q);

            Assert.AreEqual(1, RecordingStep.Calls);                   // the step itself still ran
            Assert.IsEmpty(UnicoBuildService.LatestResult.Steps);      // nothing landed on the newer run
        }

        [Test]
        public void Queue_JsonRoundTrip()
        {
            var q = new PostSuccessRunner.Queue { ResultJson = "{\"Success\":true}" };
            q.TypeNames.Add("A, Asm");
            var back = JsonUtility.FromJson<PostSuccessRunner.Queue>(JsonUtility.ToJson(q));
            CollectionAssert.AreEqual(new[] { "A, Asm" }, back.TypeNames);
            Assert.AreEqual("{\"Success\":true}", back.ResultJson);
        }

        [Test]
        public void ExecuteQueue_RunsStepsWithDeserializedResult()
        {
            var q = new PostSuccessRunner.Queue
            {
                ResultJson = JsonUtility.ToJson(new BuildResult { Success = true, Error = "v1.2.3" }),
            };
            q.TypeNames.Add(typeof(RecordingStep).AssemblyQualifiedName);

            PostSuccessRunner.ExecuteQueue(q);

            Assert.AreEqual(1, RecordingStep.Calls);
            Assert.AreEqual("v1.2.3", RecordingStep.LastVersion);
        }

        [Test]
        public void ExecuteQueue_FailureIsIsolated_LaterStepsStillRun()
        {
            LogAssert.ignoreFailingMessages = true;   // the thrown exception is logged by design
            try
            {
                var q = new PostSuccessRunner.Queue
                {
                    ResultJson = JsonUtility.ToJson(new BuildResult { Success = true }),
                };
                q.TypeNames.Add(typeof(ThrowingStep).AssemblyQualifiedName);
                q.TypeNames.Add(typeof(RecordingStep).AssemblyQualifiedName);

                PostSuccessRunner.ExecuteQueue(q);

                Assert.AreEqual(1, RecordingStep.Calls);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void ExecuteQueue_MissingType_IsIsolated()
        {
            LogAssert.ignoreFailingMessages = true;   // the thrown exception is logged by design
            try
            {
                var q = new PostSuccessRunner.Queue
                {
                    ResultJson = JsonUtility.ToJson(new BuildResult { Success = true }),
                };
                q.TypeNames.Add("No.Such.Step, NoAsm");
                q.TypeNames.Add(typeof(RecordingStep).AssemblyQualifiedName);

                PostSuccessRunner.ExecuteQueue(q);

                Assert.AreEqual(1, RecordingStep.Calls);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void Appended_EmptyExisting_YieldsSingleItem()
        {
            var q = new PostSuccessRunner.Queue { ResultJson = "{\"Success\":true}" };
            var list = PostSuccessRunner.Appended("", q);
            Assert.AreEqual(1, list.Items.Count);
        }

        [Test]
        public void Appended_ExistingList_AppendsInsteadOfOverwriting()
        {
            var first = PostSuccessRunner.Appended("", new PostSuccessRunner.Queue { ResultJson = "{\"Error\":\"a\"}" });
            var second = PostSuccessRunner.Appended(
                UnityEngine.JsonUtility.ToJson(first),
                new PostSuccessRunner.Queue { ResultJson = "{\"Error\":\"b\"}" });
            Assert.AreEqual(2, second.Items.Count);
            StringAssert.Contains("\"a\"", second.Items[0].ResultJson);
            StringAssert.Contains("\"b\"", second.Items[1].ResultJson);
        }

        [Test]
        public void Appended_UnreadablePayload_IsDroppedInsteadOfThrowing()
        {
            // Arm calls this from Finish's FINALLY block: a throw here used to escape Finish and
            // skip the build summary log for a run that had already SUCCEEDED. The queue is a dev
            // convenience — dropping an unreadable one is the correct trade.
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Post-success queue payload was unreadable"));

            var list = PostSuccessRunner.Appended("{ this is not json",
                new PostSuccessRunner.Queue { ResultJson = "{\"Error\":\"new\"}" });

            Assert.AreEqual(1, list.Items.Count);
            StringAssert.Contains("\"new\"", list.Items[0].ResultJson);
        }

        // --- Claim: one run at a time, so a mid-drain reload costs one run, not all of them ---

        private static PostSuccessRunner.QueueList ListOf(params string[] markers)
        {
            var list = new PostSuccessRunner.QueueList();
            foreach (var m in markers)
                list.Items.Add(new PostSuccessRunner.Queue { ResultJson = "{\"Error\":\"" + m + "\"}" });
            return list;
        }

        [Test]
        public void Claim_EmptyList_YieldsNothingToRunAndNothingToPersist()
        {
            var (job, remainder) = PostSuccessRunner.Claim(new PostSuccessRunner.QueueList());
            Assert.IsNull(job);
            Assert.IsEmpty(remainder);
        }

        [Test]
        public void Claim_NullList_IsSafe()
        {
            var (job, remainder) = PostSuccessRunner.Claim(null);
            Assert.IsNull(job);
            Assert.IsEmpty(remainder);
        }

        [Test]
        public void Claim_LastRun_LeavesNothingToPersist()
        {
            var (job, remainder) = PostSuccessRunner.Claim(ListOf("only"));
            StringAssert.Contains("\"only\"", job.ResultJson);
            // Empty remainder is the signal OnUpdate uses to erase the key and go idle.
            Assert.IsEmpty(remainder);
        }

        [Test]
        public void Claim_TakesTheFirstRunAndPersistsTheOnesBehindIt()
        {
            var (job, remainder) = PostSuccessRunner.Claim(ListOf("a", "b", "c"));

            StringAssert.Contains("\"a\"", job.ResultJson);
            var rest = JsonUtility.FromJson<PostSuccessRunner.QueueList>(remainder);
            Assert.AreEqual(2, rest.Items.Count);
            StringAssert.Contains("\"b\"", rest.Items[0].ResultJson);
            StringAssert.Contains("\"c\"", rest.Items[1].ResultJson);
        }

        [Test]
        public void Claim_ChainsThroughEveryRunInOrder_NoneLost()
        {
            // The durability property itself: each claim persists the rest BEFORE its run executes,
            // so a domain reload between two ticks resumes at the next run instead of dropping the
            // whole queue (which is what erasing the key up front used to do).
            var drained = new System.Collections.Generic.List<string>();
            var list = ListOf("a", "b", "c");

            while (true)
            {
                var (job, remainder) = PostSuccessRunner.Claim(list);
                if (job == null) break;
                drained.Add(job.ResultJson);
                if (string.IsNullOrEmpty(remainder)) break;
                list = JsonUtility.FromJson<PostSuccessRunner.QueueList>(remainder);
            }

            Assert.AreEqual(3, drained.Count);
            StringAssert.Contains("\"a\"", drained[0]);
            StringAssert.Contains("\"b\"", drained[1]);
            StringAssert.Contains("\"c\"", drained[2]);
        }
    }
}

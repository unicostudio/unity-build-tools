using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    // Runs [UnicoBuildStep(PostSuccess)] steps AFTER a successful job fully finished (dev state
    // restored, LatestResult recorded, job cleared). The queue lives in SessionState and is
    // drained by an update tick, which guarantees it never runs inside Finish's stack: when the
    // restore queued a domain reload the queue survives it and drains after it lands; when no
    // reload was queued it drains on the next tick directly. Failures never flip the build's
    // Success — they are logged and, when the run they belong to is still the current result,
    // appended to it as "Post step FAILED" (an outcome whose run has been superseded is logged only).
    [InitializeOnLoad]
    public static class PostSuccessRunner
    {
        private const string Key = "UnicoBuild.PostSuccess";

        [Serializable]
        internal sealed class Queue
        {
            public List<string> TypeNames = new();
            public string ResultJson = "";
        }

        [Serializable]
        internal sealed class QueueList
        {
            public List<Queue> Items = new();
        }

        // Idle-tick guard, same pattern as BuildJobState: statics reset on the very reload that
        // could deliver a queue, which is exactly when re-checking matters.
        private static bool s_knownEmpty;

        static PostSuccessRunner() => EditorApplication.update += OnUpdate;

        public static void Arm(BuildResult result)
        {
            var types = BuildStepRegistry.ResolvePostSuccessSteps();
            if (types.Count == 0) return;
            var queue = new Queue { ResultJson = JsonUtility.ToJson(result) };
            queue.TypeNames.AddRange(types.Select(t => t.AssemblyQualifiedName));
            // APPEND: a programmatic back-to-back build must not clobber an undrained queue
            // (deferred v0.4.0 finding) — each job's steps run with that job's own result.
            SessionState.SetString(Key, JsonUtility.ToJson(Appended(SessionState.GetString(Key, ""), queue)));
            s_knownEmpty = false;
        }

        // Pure append core (unit-tested). A legacy single-Queue payload (pre-v0.5.0, session-scoped
        // so only reachable across an in-session package upgrade) deserializes to an empty list and
        // is dropped — acceptable for a dev-convenience queue.
        internal static QueueList Appended(string existingJson, Queue toAdd)
        {
            var list = Parse(existingJson);
            list.Items ??= new List<Queue>();
            list.Items.Add(toAdd);
            return list;
        }

        // Never throws. Arm calls Appended from Finish's FINALLY block: an unreadable payload used
        // to escape from there, skipping the build summary log and the post-success arming for a
        // run that had already succeeded. This queue is a dev convenience, not build state — losing
        // an unreadable one is strictly better than taking Finish down with it.
        private static QueueList Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new QueueList();
            try
            {
                return JsonUtility.FromJson<QueueList>(json) ?? new QueueList();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Build] Post-success queue payload was unreadable and has been " +
                                 $"dropped: {e.Message}");
                return new QueueList();
            }
        }

        // Pure claim core (unit-tested): splits the persisted payload into the ONE run to execute
        // now and the JSON to persist for the runs behind it ("" when none remain). Claiming one at
        // a time is what bounds the damage of a domain reload (or crash) mid-drain to a single run:
        // the rest are still in SessionState and the next update tick continues with them.
        internal static (Queue job, string remainderJson) Claim(QueueList list)
        {
            var items = list?.Items ?? new List<Queue>();
            if (items.Count == 0) return (null, "");
            var rest = new QueueList { Items = items.GetRange(1, items.Count - 1) };
            return (items[0], rest.Items.Count == 0 ? "" : JsonUtility.ToJson(rest));
        }

        // Watcher gate: the CI process must not exit while post-success steps are still queued.
        // Reads false while the LAST claimed run executes (its payload is already erased), which is
        // safe only because ExecuteQueue is synchronous inside this class's own update tick — the
        // watcher's tick cannot interleave with it. Runs still QUEUED behind it keep this true.
        internal static bool HasPending => !string.IsNullOrEmpty(SessionState.GetString(Key, ""));

        private static void OnUpdate()
        {
            if (s_knownEmpty) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (UnicoBuildService.IsRunning) return;

            var json = SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(json))
            {
                s_knownEmpty = true;
                return;
            }

            // Claim BEFORE running: a step that hard-crashes the tick must not re-run forever on
            // every subsequent update. Exactly ONE run is claimed per tick and the remainder is
            // persisted first, so a post step that queues a domain reload — or an editor crash —
            // costs that one run, not every run behind it. The next tick drains the next one.
            var (job, remainder) = Claim(Parse(json));
            if (string.IsNullOrEmpty(remainder))
            {
                SessionState.EraseString(Key);
                s_knownEmpty = true;
            }
            else
            {
                SessionState.SetString(Key, remainder);
            }

            if (job != null) ExecuteQueue(job);
        }

        // Every step is isolated: one failure (throw, vanished type, non-step type) is logged and
        // reported, and the remaining steps still run.
        internal static void ExecuteQueue(Queue queue)
        {
            BuildResult result;
            try
            {
                result = JsonUtility.FromJson<BuildResult>(queue.ResultJson).Normalize();
            }
            catch (Exception e)
            {
                // Queue is already claimed; a corrupt payload skips the steps loudly instead of
                // throwing out of the update tick. No readable payload means no run stamp either,
                // so this can only be logged — it cannot be attributed to a result.
                Debug.LogException(e);
                UnicoBuildService.AppendPostStep("", "Post steps SKIPPED — result payload could not be read.");
                return;
            }

            // Attribute every outcome to the run this queue was armed for, not to whatever result
            // is current when it drains (a later build may already have replaced it).
            var stamp = result.StartedUtc;
            foreach (var typeName in queue.TypeNames)
            {
                var label = typeName;
                try
                {
                    var type = Type.GetType(typeName);
                    if (type == null)
                        throw new InvalidOperationException("step type no longer exists");
                    if (Activator.CreateInstance(type) is not IPostSuccessStep step)
                        throw new InvalidOperationException("step type does not implement IPostSuccessStep");
                    label = step.Name;
                    step.Execute(result);
                    UnicoBuildService.AppendPostStep(stamp, $"Post: {label} OK");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    UnicoBuildService.AppendPostStep(stamp, $"Post step FAILED: {label} — {e.Message}");
                }
            }
        }
    }
}

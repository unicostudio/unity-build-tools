using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    // Discovers host-project build steps ([UnicoBuildStep] via TypeCache), orders them
    // deterministically (anchor -> Order -> FullName ordinal), splices them around the built-in
    // pipeline, and re-materializes stages from persisted assembly-qualified names. The resolved
    // type list is frozen into BuildJobState at Start, so a mid-job recompile that changes the
    // discovered set can never shift StepIndex positions.
    public static class BuildStepRegistry
    {
        // One discovered hook. The ordering/splicing core works on explicit entries so tests can
        // feed fakes without decorating real types — attribute-marked types in the Tests assembly
        // would leak into REAL builds via TypeCache (testables compile in-editor).
        internal readonly struct HookEntry
        {
            public readonly Type Type;
            public readonly BuildStepAnchor Anchor;
            public readonly int Order;

            public HookEntry(Type type, BuildStepAnchor anchor, int order)
            {
                Type = type;
                Anchor = anchor;
                Order = order;
            }
        }

        public static List<Type> ResolvePipeline(BuildRequest req) => ResolvePipeline(req, DiscoverHooks());

        public static string[] ToTypeNames(IEnumerable<Type> types) =>
            types.Select(t => t.AssemblyQualifiedName).ToArray();

        // Fail loud: resuming into a shifted or partial stage list would corrupt StepIndex
        // semantics, so a vanished or non-stage type aborts the job instead of being skipped.
        public static IBuildStage[] Materialize(IReadOnlyList<string> typeNames)
        {
            var stages = new IBuildStage[typeNames.Count];
            for (var i = 0; i < typeNames.Count; i++)
            {
                var type = Type.GetType(typeNames[i]);
                if (type == null)
                    throw new InvalidOperationException(
                        $"Build step type '{typeNames[i]}' no longer exists — the job cannot resume safely.");
                object instance;
                try
                {
                    instance = Activator.CreateInstance(type);
                }
                catch (Exception e)
                {
                    // Normalize construction failures (no parameterless ctor, throwing ctor) to the
                    // contract's single fail-loud exception type — but report the ROOT cause:
                    // Activator wraps a throwing constructor in TargetInvocationException, whose own
                    // message is a generic placeholder that says nothing about what actually failed.
                    var root = (e as TargetInvocationException)?.InnerException ?? e;
                    throw new InvalidOperationException(
                        $"Build step type '{typeNames[i]}' could not be constructed: {root.Message}", root);
                }
                if (instance is not IBuildStage stage)
                    throw new InvalidOperationException(
                        $"Build step type '{typeNames[i]}' does not implement IBuildStage.");
                stages[i] = stage;
            }
            return stages;
        }

        public static List<Type> ResolvePostSuccessSteps() =>
            DiscoverHooks().Where(h => h.Anchor == BuildStepAnchor.PostSuccess)
                .OrderBy(h => h.Order).ThenBy(h => h.Type.FullName, StringComparer.Ordinal)
                .Select(h => h.Type).ToList();

        // Pure core (TypeCache-free): the exact splice positions are load-bearing — see the
        // BuildStepAnchor doc for the positional-anchor rule.
        internal static List<Type> ResolvePipeline(BuildRequest req, List<HookEntry> hooks)
        {
            var pipeline = new List<Type>();
            pipeline.AddRange(At(hooks, BuildStepAnchor.PreBuild));
            pipeline.Add(typeof(ApplyVersionStage));
            pipeline.Add(typeof(ConfigureDefinesStage));
            pipeline.AddRange(At(hooks, BuildStepAnchor.BeforeAddressables));
            if (req.BuildAddressables) pipeline.Add(typeof(AddressablesStage));
            pipeline.AddRange(At(hooks, BuildStepAnchor.BeforePlayer));
            if (req.BuildPlayer) pipeline.Add(typeof(PlayerBuildStage));
            return pipeline;
        }

        private static IEnumerable<Type> At(List<HookEntry> hooks, BuildStepAnchor anchor) =>
            hooks.Where(h => h.Anchor == anchor)
                .OrderBy(h => h.Order).ThenBy(h => h.Type.FullName, StringComparer.Ordinal)
                .Select(h => h.Type);

        // TypeCache discovery + validation. Invalid registrations are logged and SKIPPED (never
        // silently coerced) so a bad hook cannot corrupt the pipeline.
        internal static List<HookEntry> DiscoverHooks()
        {
            var entries = new List<HookEntry>();
            foreach (var type in TypeCache.GetTypesWithAttribute<UnicoBuildStepAttribute>())
            {
                var attr = (UnicoBuildStepAttribute)Attribute.GetCustomAttribute(
                    type, typeof(UnicoBuildStepAttribute));
                if (attr == null) continue;

                var rejection = RejectionReason(type, attr.Anchor);
                if (rejection != null)
                {
                    Debug.LogError($"[Build] Ignoring [UnicoBuildStep] type '{type.FullName}': {rejection}");
                    continue;
                }
                entries.Add(new HookEntry(type, attr.Anchor, attr.Order));
            }
            return Deduplicate(entries);
        }

        // Why a registration cannot be used; null = registerable. Pure so every rule is unit-tested
        // without attribute-marked test types (TypeCache would feed those into REAL builds).
        internal static string RejectionReason(Type type, BuildStepAnchor anchor)
        {
            if (type.IsAbstract)
                return "the attribute is not inherited — mark concrete classes, not abstract bases.";
            // An open generic passes both interface and constructor probes but Activator cannot
            // construct it, so it would sail through discovery and then fail EVERY build at Start.
            if (type.IsGenericTypeDefinition)
                return "an open generic type cannot be constructed — register a closed class.";

            var wantsPost = anchor == BuildStepAnchor.PostSuccess;
            var implementsRequired = wantsPost
                ? typeof(IPostSuccessStep).IsAssignableFrom(type)
                : typeof(IBuildStage).IsAssignableFrom(type);
            if (!implementsRequired)
                return wantsPost
                    ? "PostSuccess steps must implement IPostSuccessStep."
                    : "this anchor requires IBuildStage.";
            if (type.GetConstructor(Type.EmptyTypes) == null)
                return "it needs a public parameterless constructor.";
            return null;
        }

        // The same hook class can reach TypeCache twice — the Package Manager sample imported AND
        // copied into an Assets/Editor folder is the shape the samples themselves invite — which
        // would splice it into the pipeline twice and run it twice. Keep the first by
        // assembly-qualified name (deterministic) and name both assemblies in the error.
        internal static List<HookEntry> Deduplicate(List<HookEntry> entries)
        {
            var kept = new List<HookEntry>();
            var byName = new Dictionary<string, HookEntry>(StringComparer.Ordinal);
            foreach (var entry in entries.OrderBy(e => e.Type.AssemblyQualifiedName, StringComparer.Ordinal))
            {
                if (byName.TryGetValue(entry.Type.FullName, out var first))
                {
                    if (first.Type != entry.Type)
                        Debug.LogError($"[Build] Duplicate [UnicoBuildStep] type '{entry.Type.FullName}' in both " +
                            $"'{first.Type.Assembly.GetName().Name}' and '{entry.Type.Assembly.GetName().Name}' — " +
                            "using the first and ignoring the rest; delete the extra copy.");
                    continue;
                }
                byName[entry.Type.FullName] = entry;
                kept.Add(entry);
            }
            return kept;
        }
    }
}

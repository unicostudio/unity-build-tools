namespace UnicoStudio.BuildSystem.Editor
{
    // How an Addressables profile value failed to bind, if it did. Two problems, two different
    // fixes, so callers word them separately — see ProfileBindingRule.Inspect.
    public enum ProfileBindingProblem
    {
        None,

        // A token written in the raw value survived evaluation: the binding names a type or member
        // that does not exist, or its getter threw. Fix the binding (or what it points at).
        UnresolvedToken,

        // A '[' or ']' survived evaluation, so the raw value's own delimiters are unbalanced and
        // the evaluator never treated them as a token at all. Fix the brackets.
        MalformedDelimiters,
    }

    // ProfileBindingRule's verdict for one raw/evaluated pair.
    public readonly struct ProfileBindingVerdict
    {
        public readonly ProfileBindingProblem Problem;

        // Inner text of the token that survived — set for UnresolvedToken only. A malformed value
        // is by definition not a token the rule can extract, so there is no name to report.
        public readonly string Token;

        private ProfileBindingVerdict(ProfileBindingProblem problem, string token)
        {
            Problem = problem;
            Token = token;
        }

        public static readonly ProfileBindingVerdict Ok = new(ProfileBindingProblem.None, null);
        public static readonly ProfileBindingVerdict Malformed = new(ProfileBindingProblem.MalformedDelimiters, null);
        public static ProfileBindingVerdict Unresolved(string token) => new(ProfileBindingProblem.UnresolvedToken, token);
    }

    // Whether the bindings written directly in an Addressables profile value actually resolved.
    //
    // The evaluator does NOT throw or return empty when a binding fails: it substitutes the token's
    // own text with the delimiters stripped, so "[A.B]" becomes "A.B" and a garbage path is baked
    // into the catalog silently. The old guard tested for the literal "AddressablesHelper", a host
    // game's class name that a shared package must not know. Comparing against the RAW token's
    // inner text needs no name literal, so it works for package-owned and host-owned bindings
    // alike.
    //
    // What it does NOT cover — all three are why AddressablesStage keeps its own separate checks:
    //  * RECURSION. The evaluator expands a profile variable's value, and that value may itself
    //    contain tokens (in this project "[BuildTarget]" is a profile variable whose value is
    //    "[UnityEditor.EditorUserBuildSettings.activeBuildTarget]"). Only tokens written directly
    //    in `raw` are inspected, so a break one level down reads as resolved here.
    //  * MALFORMED DELIMITERS. A raw value with an unbalanced or nested bracket is not a token this
    //    rule can extract — "[A.B" evaluates to "[A.B" and "[A[B].C]" to "AB.C", and neither is
    //    reported. Inspect below adds the bracket test that catches the unbalanced case; the nested
    //    case is caught by nothing and is a known gap.
    //  * SUBSTRING MATCHING. The match is unanchored: a variable whose name appears anywhere inside
    //    its own resolved value would be reported unresolved. No variable in this project does, and
    //    a false positive fails the build loudly rather than shipping a bad path, so the simple
    //    match is deliberate.
    public static class ProfileBindingRule
    {
        // The inner text of the first '[...]' token in `raw` that also appears in `evaluated`, or
        // null when no token does. Null is NOT proof that evaluation succeeded: it is also returned
        // when `evaluated` is empty (or `raw` is), which every caller must therefore test itself
        // before trusting the result.
        public static string FindUnresolvedToken(string raw, string evaluated)
        {
            if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(evaluated)) return null;

            var from = 0;
            while (true)
            {
                var open = raw.IndexOf('[', from);
                if (open < 0) return null;
                var close = raw.IndexOf(']', open + 1);
                if (close < 0) return null;

                var inner = raw.Substring(open + 1, close - open - 1);
                // An empty token ("[]") has empty inner text, and Contains("") is true of every
                // string — without this length test the rule would report every value unresolved.
                if (inner.Length > 0 && evaluated.Contains(inner)) return inner;
                from = close + 1;
            }
        }

        // The whole rule, in one place. FindUnresolvedToken alone is NOT the rule: it reports only
        // tokens it can extract, so an unbalanced value like "[Foo.Bar" — which the evaluator passes
        // through verbatim, measured live — reads as clean. A well-formed token that fails to
        // resolve leaves no bracket behind (the evaluator strips the delimiters), so a surviving
        // '[' or ']' can only mean the raw value's own delimiters are malformed. Two problems, and
        // a caller that applied just the first would pass a value the other caller then rejects —
        // which is exactly what preflight and AddressablesStage must never do to each other.
        //
        // The token verdict wins when a value manages both: it names the binding that broke.
        //
        // A None verdict is NOT proof that evaluation succeeded. An empty `evaluated` contains
        // neither token text nor a bracket, so — as with FindUnresolvedToken — every caller must
        // test emptiness itself before trusting this.
        public static ProfileBindingVerdict Inspect(string raw, string evaluated)
        {
            var token = FindUnresolvedToken(raw, evaluated);
            if (token != null) return ProfileBindingVerdict.Unresolved(token);

            if (!string.IsNullOrEmpty(evaluated) &&
                (evaluated.IndexOf('[') >= 0 || evaluated.IndexOf(']') >= 0))
                return ProfileBindingVerdict.Malformed;

            return ProfileBindingVerdict.Ok;
        }
    }
}

// Sample host injector: Android keystore passwords from environment variables, applied on editor
// load — the safe replacement for committing passwords in source. Import via Package Manager >
// Samples (or copy into any Editor folder) and adapt the variable names to your local/CI secret
// setup. With the variables unset this is a no-op, and the package's KeystoreCheck still blocks
// an unsigned Android Release cleanly.
//
// [InitializeOnLoad], DELIBERATELY NOT a [UnicoBuildStep] hook. Preflight runs before every hook
// anchor, and KeystoreCheck reads the live keystore passwords during preflight — Unity never
// serializes them, so in a fresh editor session they are empty until something injects them. This
// sample's original shape was a PreBuild hook, which is structurally too late on the panel path:
// for Android + Release + Build Player the check blocked the job before the hook ever executed,
// while its message told the developer to provide the very variables the hook was already reading.
// On editor load the values are in place before any preflight can run. (The CLI path needs no
// import: UnicoBuildCli performs the same injection itself, before Start.)
//
// General rule this encodes: code that feeds state PREFLIGHT reads cannot be a build hook — it
// must run on editor load. Hooks are for work scoped to the build itself.
using UnityEditor;

[InitializeOnLoad]
public static class KeystoreEnvInjection
{
    static KeystoreEnvInjection()
    {
        var pass = System.Environment.GetEnvironmentVariable("UNICO_KEYSTORE_PASS");
        var alias = System.Environment.GetEnvironmentVariable("UNICO_KEYALIAS_PASS");
        if (string.IsNullOrEmpty(pass) || string.IsNullOrEmpty(alias)) return;

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystorePass = pass;
        PlayerSettings.Android.keyaliasPass = alias;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnicoStudio.UnicoLibs.VersionTracker
{
    public static class UnicoVersionExporter
    {
        private const string ASSETS = "Assets";
        private const string PACKAGES_LOCK_PATH = "Packages/packages-lock.json";

        #region UPM Fallback Helpers

        /// <summary>
        /// Gets version from packages-lock.json for the given UPM package name.
        /// </summary>
        private static string GetVersionFromPackagesLock(string packageName)
        {
            if (!File.Exists(PACKAGES_LOCK_PATH)) return null;

            try
            {
                var json = File.ReadAllText(PACKAGES_LOCK_PATH);
                var lockFile = JObject.Parse(json);
                var dependencies = lockFile["dependencies"] as JObject;

                if (dependencies == null || !dependencies.ContainsKey(packageName)) return null;

                var package = dependencies[packageName] as JObject;
                var version = package?["version"]?.ToString();
                var source = package?["source"]?.ToString();

                return source switch
                {
                    "registry" => version,
                    "local-tarball" => ExtractVersionFromTarballPath(version),
                    "git" => GetVersionFromGitPackage(packageName),
                    "embedded" => GetVersionFromEmbeddedPackage(packageName),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                LogError($"Failed to parse packages-lock.json: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts version from tarball path like "file:../path/com.google.firebase.app-13.6.0.tgz"
        /// </summary>
        private static string ExtractVersionFromTarballPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = Regex.Match(path, @"-(\d+\.\d+\.\d+)\.tgz$");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Gets version from embedded package's package.json
        /// </summary>
        private static string GetVersionFromEmbeddedPackage(string packageName) => ParsePackageJsonVersion(Path.Combine("Packages", packageName, "package.json"));

        /// <summary>
        /// Gets version from git package in Library/PackageCache
        /// </summary>
        private static string GetVersionFromGitPackage(string packageName)
        {
            var cacheDir = "Library/PackageCache";
            if (!Directory.Exists(cacheDir)) return null;

            var dirs = Directory.GetDirectories(cacheDir, $"{packageName}@*");
            return dirs.Length > 0 ? ParsePackageJsonVersion(Path.Combine(dirs[0], "package.json")) : null;
        }

        /// <summary>
        /// Parses version from a package.json file
        /// </summary>
        private static string ParsePackageJsonVersion(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path);
                return JObject.Parse(json)["version"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Shared helper to get version info from packages-lock.json for a given package dictionary.
        /// </summary>
        private static List<VersionInfo> GetVersionInfoFromUpm(Dictionary<string, string> packages)
        {
            var versionInfo = new List<VersionInfo>();
            foreach (var (packageName, displayName) in packages)
            {
                var version = GetVersionFromPackagesLock(packageName);
                if (string.IsNullOrEmpty(version)) continue;

                s_networkIdMapping.TryGetValue(displayName, out var networkId);
                versionInfo.Add(new VersionInfo(networkId, displayName, version, version, version));
            }

            return versionInfo.Count > 0 ? versionInfo : null;
        }

        #endregion

        private static readonly JsonSerializerSettings s_jsonSerializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Include,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Formatting = Formatting.Indented,
        };

        // Static network IDs - these should never change even if network names change
        private static readonly Dictionary<string, string> s_networkIdMapping = new()
        {
            // AppLovin MAX Networks
            { "AppLovin", "_applovin_" },
            { "AdColony", "_adcolony_" },
            { "Amazon", "_amazon_" },
            { "BidMachine", "_bidmachine_" },
            { "BIGO Ads", "_bigo_ads_" },
            { "Chartboost", "_chartboost_" },
            { "CSJ", "_csj_" },
            { "DT Exchange", "_dt_exchange_" },
            { "Facebook", "_facebook_" },
            { "Fyber", "_fyber_" },
            { "Google AdMob", "_google_admob_" },
            { "Google Ad Manager", "_google_ad_manager_" },
            { "HyprMX", "_hyprmx_" },
            { "InMobi", "_inmobi_" },
            { "ironSource", "_ironsource_" },
            { "Liftoff Monetize", "_liftoff_monetize_" },
            { "LINE", "_line_" },
            { "Line", "_line_" },
            { "LinkedIn", "_linkedin_" },
            { "Maio", "_maio_" },
            { "Mintegral", "_mintegral_" },
            { "MobileFuse", "_mobilefuse_" },
            { "Moloco", "_moloco_" },
            { "MyTarget", "_mytarget_" },
            { "Nend", "_nend_" },
            { "Ogury", "_ogury_" },
            { "Pangle", "_pangle_" },
            { "PubMatic", "_pubmatic_" },
            { "Smaato", "_smaato_" },
            { "Tapjoy", "_tapjoy_" },
            { "Tencent", "_tencent_" },
            { "Unity Ads", "_unity_ads_" },
            { "Verizon", "_verizon_" },
            { "Verve", "_verve_" },
            { "VK Ad Network", "_vk_ad_network_" },
            { "Vungle", "_vungle_" },
            { "Yandex", "_yandex_" },
            { "YSO Network", "_yso_network_" },
            
            // AdMob Mediation Adapters (can have different naming than MAX)
            { "Meta", "_meta_" },
            { "MetaAudienceNetwork", "_meta_" },
            { "UnityAds", "_unity_ads_" },
            { "IronSource", "_ironsource_" },
            { "Liftoff", "_liftoff_" },
            { "Digital Turbine", "_digital_turbine_" },
            { "DTExchange", "_dt_exchange_" },
            { "i-mobile", "_i_mobile_" },
            { "LiftoffMonetize", "_liftoff_monetize_" },
            
            // Firebase
            { "FirebaseAI", "_firebase_ai_" },
            { "FirebaseAnalytics", "_firebase_analytics_" },
            { "FirebaseAppCheck", "_firebase_app_check_" },
            { "FirebaseAuth", "_firebase_auth_" },
            { "FirebaseCore", "_firebase_core_" },
            { "FirebaseCrashlytics", "_firebase_crashlytics_" },
            { "FirebaseDatabase", "_firebase_database_" },
            { "FirebaseDynamicLinks", "_firebase_dynamic_links_" },
            { "FirebaseFirestore", "_firebase_firestore_" },
            { "FirebaseFunctions", "_firebase_functions_" },
            { "FirebaseInstallations", "_firebase_installations_" },
            { "FirebaseMessaging", "_firebase_messaging_" },
            { "FirebaseRemoteConfig", "_firebase_remote_config_" },
            { "FirebaseStorage", "_firebase_storage_" },
            
            // Google ODM
            { "AdjustGoogleOdm", "_adjust_google_odm_" },
            { "GoogleAdsOnDeviceConversion", "_google_ads_on_device_conversion_" },
        };

        /// <summary>
        /// Firebase UPM package names mapped to display names for pluginVersionInfo.
        /// </summary>
        private static readonly Dictionary<string, string> s_firebasePackages = new()
        {
            { "com.google.firebase.ai", "FirebaseAI" },
            { "com.google.firebase.analytics", "FirebaseAnalytics" },
            { "com.google.firebase.app-check", "FirebaseAppCheck" },
            { "com.google.firebase.auth", "FirebaseAuth" },
            { "com.google.firebase.crashlytics", "FirebaseCrashlytics" },
            { "com.google.firebase.database", "FirebaseDatabase" },
            { "com.google.firebase.firestore", "FirebaseFirestore" },
            { "com.google.firebase.functions", "FirebaseFunctions" },
            { "com.google.firebase.installations", "FirebaseInstallations" },
            { "com.google.firebase.messaging", "FirebaseMessaging" },
            { "com.google.firebase.remote-config", "FirebaseRemoteConfig" },
            { "com.google.firebase.storage", "FirebaseStorage" },
        };

        /// <summary>
        /// AdMob mediation UPM package names mapped to adapter display names.
        /// </summary>
        private static readonly Dictionary<string, string> s_admobMediationPackages = new()
        {
            { "com.google.ads.mobile.unity.mediation.applovin", "AppLovin" },
            { "com.google.ads.mobile.unity.mediation.chartboost", "Chartboost" },
            { "com.google.ads.mobile.unity.mediation.dtexchange", "DTExchange" },
            { "com.google.ads.mobile.unity.mediation.imobile", "i-mobile" },
            { "com.google.ads.mobile.unity.mediation.inmobi", "InMobi" },
            { "com.google.ads.mobile.unity.mediation.ironsource", "IronSource" },
            { "com.google.ads.mobile.unity.mediation.liftoffmonetize", "LiftoffMonetize" },
            { "com.google.ads.mobile.unity.mediation.line", "LINE" },
            { "com.google.ads.mobile.unity.mediation.maio", "Maio" },
            { "com.google.ads.mobile.unity.mediation.meta", "Meta" },
            { "com.google.ads.mobile.unity.mediation.mintegral", "Mintegral" },
            { "com.google.ads.mobile.unity.mediation.moloco", "Moloco" },
            { "com.google.ads.mobile.unity.mediation.mytarget", "myTarget" },
            { "com.google.ads.mobile.unity.mediation.pangle", "Pangle" },
            { "com.google.ads.mobile.unity.mediation.pubmatic", "PubMatic" },
            { "com.google.ads.mobile.unity.mediation.unity", "UnityAds" },
            { "com.google.ads.mobile.unity.mediation.vpon", "Vpon" },
            { "com.google.ads.mobile.unity.mediation.zucks", "Zucks" },
        };

        public static readonly List<SdkInfo> s_sdkInfo = new()
        {
            new SdkInfo("UnicoAPIClient",
                new SdkVersionGetter(null, GetUnicoAPIClientVersion)),
            new SdkInfo("AppLovinMAX",
                new SdkVersionGetter("MaxSdk", GetAppLovinVersion, "AppLovinMax.Scripts.IntegrationManager.Editor.AppLovinIntegrationManager", GetAppLovinVersions),
                upmPackageNames: new[] { "com.applovin.mediation.ads" }),
            new SdkInfo("GoogleAdMob",
                new SdkVersionGetter("GoogleMobileAds.Api.MobileAds", GetAdMobVersion, GetAdMobMediationVersions),
                upmPackageNames: new[] { "com.google.ads.mobile" }),
            new SdkInfo("GoogleImmersiveAds",
                new SdkVersionGetter("GoogleMobileAds.Api.MobileAds", GetGoogleImmersiveAdsVersion)),
            new SdkInfo("GoogleODM",
                new SdkVersionGetter(null, GetGoogleOdmVersionsAsList)),
            new SdkInfo("Odeeo",
                new SdkVersionGetter("Odeeo.OdeeoSdk", GetOdeeoVersion)),
            new SdkInfo("AmazonSdk",
                new SdkVersionGetter("AmazonConstants", GetAmazonSdkVersion)),
            new SdkInfo("AdjustSdk",
                new SdkVersionGetter("AdjustSdk.Adjust", GetAdjustVersion),
                upmPackageNames: new[] { "com.adjust.sdk" }),
            new SdkInfo("FacebookSdk",
                new SdkVersionGetter("Facebook.Unity.FacebookSdkVersion", GetFacebookSdkVersion)),
            new SdkInfo("Firebase",
                new SdkVersionGetter("Firebase.FirebaseApp", GetFirebaseVersion, GetFirebaseVersions),
                upmPackageNames: new[] { "com.google.firebase.app" }),
        };

        /// <summary>
        /// Exports the build information to a json file.
        /// </summary>
        /// <param name="buildSummary">The build summary.</param>
        /// <remarks>
        /// The file path will be <c>Assets/../UnicoVersionTracker/[platform]_BuildInfo.json</c>.
        /// </remarks>
        public static async void ExportBuildInfoAsync(BuildSummary buildSummary)
        {
            try
            {
                UnicoVersionTrackerProgressBar.StartLoading();

                var buildInfo = new BuildInfo(buildSummary);
                var filePath = GetFilePath(string.Empty, $"{buildSummary.platform}_BuildInfo");
                var json = JsonConvert.SerializeObject(buildInfo, s_jsonSerializerSettings);

                // Save to file
                await File.WriteAllTextAsync(filePath, json);
                Debug.Log($"Build info saved to {filePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error writing file: {ex}");
            }
            finally
            {
                UnicoVersionTrackerProgressBar.StopLoading();
            }
        }

        /// <summary>
        /// Exports the SDK information to a json file.
        /// </summary>
        /// <remarks>
        /// The file path will be <c>Assets/../UnicoVersionTracker/SdkInfo.json</c>.
        /// </remarks>
        [MenuItem("UnicoStudio/Export SdkInfo", priority = -1)]
        private static async void ExportSdkInfo()
        {
            try
            {
                UnicoVersionTrackerProgressBar.StartLoading();

                RefreshSdkInfo();
                var filePath = GetFilePath("SdkInfo", "SdkInfo");
                var json = JsonConvert.SerializeObject(s_sdkInfo, s_jsonSerializerSettings);

                // Save to file
                await File.WriteAllTextAsync(filePath, json);
                Debug.Log($"Sdk info saved to {filePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error writing file: {ex}");
            }
            finally
            {
                UnicoVersionTrackerProgressBar.StopLoading();
            }
        }

        /// <summary>
        /// Asynchronously retrieves and deserializes the saved build information for the specified platform.
        /// </summary>
        /// <param name="platform">The target platform for which to retrieve the build information.</param>
        /// <returns>A <see cref="BuildInfo"/> object containing the saved build details, or null if an error occurs.</returns>
        public static async Task<BuildInfo> GetSavedBuildInfo(BuildTarget platform)
        {
            try
            {
                var json = await GetSavedBuildInfoJson(platform);
                var buildInfo = JsonConvert.DeserializeObject<BuildInfo>(json, s_jsonSerializerSettings);
                return buildInfo;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading file: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Asynchronously reads the saved build information JSON from a file for the specified platform.
        /// </summary>
        /// <param name="platform">The target platform for which to retrieve the JSON data.</param>
        /// <returns>A JSON string containing the build information, or null if an error occurs.</returns>
        public static async Task<string> GetSavedBuildInfoJson(BuildTarget platform)
        {
            try
            {
                var filePath = GetFilePath(string.Empty, $"{platform}_BuildInfo");
                var json = await File.ReadAllTextAsync(filePath);
                return json;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading file: {ex}");
                return null;
            }
        }

        private static string GetFilePath(string folderPathPostfix, string fileNamePostfix)
        {
            // Predefined folder path
            var folderPath = Path.Combine(ASSETS, "../UnicoVersionTracker/", folderPathPostfix);
            var fileName = $"{Application.productName}_{Application.version}_{fileNamePostfix}.json";
            var filePath = Path.Combine(folderPath, MakeFileNameFriendly(fileName));

            // Ensure the folder exists
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return filePath;
        }

        private static string MakeFileNameFriendly(string fileName, bool removeSpaces = true)
        {
            var newName = Regex.Replace(fileName, $"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", "-");
            if (removeSpaces) newName = newName.Replace(" ", string.Empty);
            return newName;
        }

        /// <summary>
        /// Refreshes the SDK information by updating the version of each SDK.
        /// </summary>
        /// <remarks>
        /// Iterates through the list of SDKs and calls the <c>SetVersion</c> method 
        /// on each <c>SdkInfo</c> instance to update its version information.
        /// </remarks>
        private static void RefreshSdkInfo()
        {
            foreach (var sdkInfo in s_sdkInfo)
            {
                sdkInfo.SetVersion();
            }
        }

        /// <summary>
        /// Logs the given message as an error in the Unity console, with a red color.
        /// </summary>
        /// <param name="message">The message to log.</param>
        private static void LogError(string message)
        {
            Debug.Log("<color=red>" + message + "</color>");
        }

        private static string GetAppLovinVersion(Type appLovinType)
        {
            if (appLovinType == null) return null;

            var property = appLovinType.GetProperty("Version", BindingFlags.Public | BindingFlags.Static);
            return property != null ? property.GetValue(null)?.ToString() : null;
        }

        private static List<VersionInfo> GetAppLovinVersions(Type appLovinType)
        {
            if (appLovinType == null) return null;

            // Use the static LoadPluginDataSync method (synchronous)
            var syncMethod = appLovinType.GetMethod("LoadPluginDataSync", BindingFlags.Public | BindingFlags.Static);
            if (syncMethod == null)
            {
                LogError("LoadPluginDataSync method not found!");
                return null;
            }

            // Invoke LoadPluginDataSync (no parameters, returns PluginData directly)
            var pluginData = syncMethod.Invoke(null, null);

            // If no result, return null
            if (pluginData == null)
            {
                LogError("LoadPluginDataSync did not return any PluginData! You may have internet connection problem..");
                return null;
            }

            // Access the AppLovinMax field in PluginData
            var pluginDataType = pluginData.GetType();
            var appLovinMaxField = pluginDataType.GetField("AppLovinMax", BindingFlags.Public | BindingFlags.Instance);
            if (appLovinMaxField == null)
            {
                LogError("AppLovinMax field not found in PluginData!");
                return null;
            }

            var appLovinMax = appLovinMaxField.GetValue(pluginData);
            if (appLovinMax == null)
            {
                LogError("AppLovinMax is null!");
                return null;
            }

            var versionInfo = new List<VersionInfo>();

            // Access the MediatedNetworks field in PluginData
            var mediatedNetworksField = pluginDataType.GetField("MediatedNetworks", BindingFlags.Public | BindingFlags.Instance);
            if (mediatedNetworksField == null)
            {
                LogError("MediatedNetworks field not found in PluginData!");
                return null;
            }

            var mediatedNetworks = mediatedNetworksField.GetValue(pluginData) as object[];
            if (mediatedNetworks == null)
            {
                LogError("MediatedNetworks is null!");
                return null;
            }

            // Loop through MediatedNetworks and get the Unity version
            foreach (var network in mediatedNetworks)
            {
                if (network == null) continue;
                versionInfo.Add(GetVersionInfoForNetwork(network));
            }

            // Access the PartnerMicroSdks field in PluginData
            var partnerMicroSdksField = pluginDataType.GetField("PartnerMicroSdks", BindingFlags.Public | BindingFlags.Instance);
            if (partnerMicroSdksField == null)
            {
                LogError("MediatedNetworks field not found in PluginData!");
                return null;
            }

            var partnerMicroSdks = partnerMicroSdksField.GetValue(pluginData) as object[];
            if (partnerMicroSdks == null)
            {
                LogError("PartnerMicroSdks is null!");
                return null;
            }

            // Loop through MediatedNetworks and get the Unity version
            foreach (var network in partnerMicroSdks)
            {
                if (network == null) continue;
                versionInfo.Add(GetVersionInfoForNetwork(network));
            }

            return versionInfo;

            VersionInfo GetVersionInfoForNetwork(object networkObject)
            {
                var networkType = networkObject?.GetType();
                var name = networkType
                    ?.GetField("DisplayName", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(networkObject)
                    ?.ToString();

                var versionsField = networkType
                    ?.GetField("CurrentVersions", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(networkObject);

                var versionsType = versionsField?.GetType();
                var unityVersion = versionsType?.GetField("Unity", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(versionsField)
                    ?.ToString();

                var androidVersion = versionsType?.GetField("Android", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(versionsField)
                    ?.ToString();

                var iosVersion = versionsType?.GetField("Ios", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(versionsField)
                    ?.ToString();

                // Create combined version string
                string combinedVersion = unityVersion;
                if (!string.IsNullOrEmpty(androidVersion) && !string.IsNullOrEmpty(iosVersion))
                    combinedVersion = $"android_{androidVersion}_ios_{iosVersion}";
                else if (!string.IsNullOrEmpty(androidVersion))
                    combinedVersion = $"android_{androidVersion}";
                else if (!string.IsNullOrEmpty(iosVersion))
                    combinedVersion = $"ios_{iosVersion}";

                // Get network ID from dictionary only
                s_networkIdMapping.TryGetValue(name, out var networkId);
                return new VersionInfo(networkId, name, combinedVersion, androidVersion, iosVersion);
            }
        }

        private static string GetAdMobVersion(Type _)
        {
            // First, find the GoogleMobileAds folder anywhere in the Assets folder
            var googleMobileAdsPath = Directory.GetDirectories(ASSETS, "*GoogleMobileAds", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(googleMobileAdsPath))
                return null;  // UPM fallback will be tried

            var files = Directory.GetFiles(googleMobileAdsPath, "GoogleMobileAds_version*.txt");
            if (files.Length <= 0)
                return null;

            var fileName = Path.GetFileNameWithoutExtension(files[0]);

            // Extract version from filename (e.g., "GoogleMobileAds_version-9.1.0_manifest")
            var version = fileName.Split('-')[1].Replace("_manifest", string.Empty); // "9.1.0"
            return version;
        }

        private static List<VersionInfo> GetAdMobMediationVersions(Type _)
        {
            // First, find the GoogleMobileAds/Mediation folder anywhere in the Assets folder
            var mediationFolderPath = Directory.GetDirectories(ASSETS, "*GoogleMobileAds", SearchOption.AllDirectories)
                .Where(dir => Directory.Exists(Path.Combine(dir, "Mediation")))
                .Select(dir => Path.Combine(dir, "Mediation"))
                .FirstOrDefault();

            if (string.IsNullOrEmpty(mediationFolderPath))
                return null;

            var versionInfo = new List<VersionInfo>();

            try
            {
                // Get all mediation adapter directories
                var adapterDirectories = Directory.GetDirectories(mediationFolderPath);

                foreach (var adapterDir in adapterDirectories)
                {
                    var adapterName = Path.GetFileName(adapterDir);
                    var editorPath = Path.Combine(adapterDir, "Editor");

                    if (!Directory.Exists(editorPath)) continue;

                    // Look for the mediation dependencies XML file
                    var dependenciesFiles = Directory.GetFiles(editorPath, "*MediationDependencies.xml");

                    if (dependenciesFiles.Length == 0)
                    {
                        Debug.Log($"AdMob mediation dependencies file not found for {adapterName} in: {editorPath}");
                        continue;
                    }

                    var dependenciesFile = dependenciesFiles[0]; // Use the first match

                    try
                    {
                        var xmlDocument = XDocument.Load(dependenciesFile);

                        // Extract Android version from androidPackage spec
                        var androidPackageNode = xmlDocument.Descendants("androidPackage").FirstOrDefault();
                        var spec = androidPackageNode?.Attribute("spec")?.Value;
                        string androidVersion = null;
                        if (!string.IsNullOrEmpty(spec))
                        {
                            // Extract version from spec like "com.google.ads.mediation:applovin:12.6.1.0"
                            var parts = spec.Split(':');
                            androidVersion = parts.Length >= 1 ? parts[^1] : null;
                        }

                        // Extract iOS version from iosPod version
                        var iosPodNode = xmlDocument.Descendants("iosPod").FirstOrDefault();
                        var iosVersion = iosPodNode?.Attribute("version")?.Value;

                        // Combine Android and iOS versions in the specified format
                        string combinedVersion = null;

                        if (!string.IsNullOrEmpty(androidVersion) && !string.IsNullOrEmpty(iosVersion))
                            combinedVersion = $"android_{androidVersion}_ios_{iosVersion}";
                        else if (!string.IsNullOrEmpty(androidVersion))
                            combinedVersion = $"android_{androidVersion}";
                        else if (!string.IsNullOrEmpty(iosVersion))
                            combinedVersion = $"ios_{iosVersion}";

                        if (!string.IsNullOrEmpty(combinedVersion))
                        {
                            // Get network ID from dictionary only
                            s_networkIdMapping.TryGetValue(adapterName, out var networkId);
                            versionInfo.Add(new VersionInfo(networkId, adapterName, combinedVersion, androidVersion, iosVersion));
                        }
                        else
                            LogError($"Failed to extract version for AdMob {adapterName} mediation adapter!");
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error parsing mediation dependencies for {adapterName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error reading AdMob mediation directories: {ex.Message}");
                return null;
            }

            return versionInfo.Any() ? versionInfo : GetAdMobMediationVersionsFromUpm();
        }

        /// <summary>
        /// Gets AdMob mediation versions from packages-lock.json (UPM fallback).
        /// </summary>
        private static List<VersionInfo> GetAdMobMediationVersionsFromUpm() => GetVersionInfoFromUpm(s_admobMediationPackages);

        private static string GetGoogleImmersiveAdsVersion(Type _)
        {
            // First, find the GoogleMobileAdsNative/Editor folder anywhere in the Assets folder
            var googleMobileAdsNativeEditorPath = Directory.GetDirectories(ASSETS, "*GoogleMobileAdsNative", SearchOption.AllDirectories)
                .Where(dir => Directory.Exists(Path.Combine(dir, "Editor")))
                .Select(dir => Path.Combine(dir, "Editor"))
                .FirstOrDefault();

            if (string.IsNullOrEmpty(googleMobileAdsNativeEditorPath))
            {
                LogError("GoogleMobileAdsNative/Editor folder not found!");
                return null;
            }

            var dependenciesPath = Path.Combine(googleMobileAdsNativeEditorPath, "GoogleMobileAdsNativeDependencies.xml");
            if (!File.Exists(dependenciesPath))
            {
                LogError("Google immersive ads dependencies file not found!");
                return null;
            }

            var xmlDocument = XDocument.Load(dependenciesPath);
            var androidPackageNode = xmlDocument.Descendants("androidPackage")
                .FirstOrDefault(node => node.Attribute("spec")?.Value.Contains("gson") == true);

            if (androidPackageNode != null)
            {
                var spec = androidPackageNode.Attribute("spec")?.Value;
                if (!string.IsNullOrEmpty(spec))
                {
                    var parts = spec.Split(':');
                    if (parts.Length >= 1)
                    {
                        return parts[^1];
                    }
                }
            }

            LogError("Failed to fetch Google immersive ads version!");
            return null;
        }

        private static string GetOdeeoVersion(Type odeeoType)
        {
            if (odeeoType == null) return null;

            var field = odeeoType.GetField("SDK_VERSION", BindingFlags.Public | BindingFlags.Static);
            var sdkVersion = field?.GetValue(null)?.ToString();
            if (sdkVersion == null)
            {
                LogError("SDK_VERSION field not found in OdeeoSdk!");
                return null;
            }

            var match = Regex.Match(sdkVersion, @"v(\d+\.\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string GetAmazonSdkVersion(Type amazonType)
        {
            if (amazonType == null) return null;

            var field = amazonType.GetField("VERSION", BindingFlags.Public | BindingFlags.Static);
            return field != null ? field.GetValue(null)?.ToString() : null;
        }

        private static string GetAdjustVersion(Type _)
        {
            // First, find the Adjust folder anywhere in the Assets folder
            var adjustPath = Directory.GetDirectories(ASSETS, "*Adjust", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(adjustPath))
                return null;  // UPM fallback will be tried

            var packageJsonPath = Path.Combine(adjustPath, "package.json");
            if (!File.Exists(packageJsonPath))
                return null;

            var jsonContent = File.ReadAllText(packageJsonPath);
            var jsonObject = JObject.Parse(jsonContent);

            // Extract the "version" field
            var version = jsonObject["version"]?.ToString();
            if (string.IsNullOrEmpty(version))
                return null;

            return version;
        }

        private static string GetFacebookSdkVersion(Type facebookType)
        {
            if (facebookType == null) return null;

            var property = facebookType.GetProperty("Build", BindingFlags.Public | BindingFlags.Static);
            return property != null ? property.GetValue(null)?.ToString() : null;
        }

        private static string GetFirebaseVersion(Type _)
        {
            // First, find the Firebase/Editor folder anywhere in the Assets folder
            var firebaseEditorPath = Directory.GetDirectories(ASSETS, "*Firebase", SearchOption.AllDirectories)
                .Where(dir => Directory.Exists(Path.Combine(dir, "Editor")))
                .Select(dir => Path.Combine(dir, "Editor"))
                .FirstOrDefault();

            if (string.IsNullOrEmpty(firebaseEditorPath))
                return null;  // UPM fallback will be tried

            var dependenciesPath = Path.Combine(firebaseEditorPath, "AppDependencies.xml");
            if (!File.Exists(dependenciesPath))
                return null;

            // Load the XML document
            var xmlDocument = XDocument.Load(dependenciesPath);

            // Find the <androidPackage> node with 'unity' in its spec attribute
            var androidPackageNode = xmlDocument.Descendants("androidPackage")
                .FirstOrDefault(node => node.Attribute("spec")?.Value.Contains("unity") == true);

            if (androidPackageNode != null)
            {
                // Extract the spec attribute value
                var spec = androidPackageNode.Attribute("spec")?.Value;
                if (!string.IsNullOrEmpty(spec))
                {
                    // Split the spec to get the version
                    var parts = spec.Split(':');
                    if (parts.Length >= 1)
                    {
                        return parts[^1]; // return the version part
                    }
                }
            }

            return null;
        }

        private static List<VersionInfo> GetFirebaseVersions(Type _)
        {
            // PRIMARY: Search Assets folder for Firebase/Editor version files
            var firebaseEditorPath = Directory.GetDirectories(ASSETS, "*Firebase", SearchOption.AllDirectories)
                .Where(dir => Directory.Exists(Path.Combine(dir, "Editor")))
                .Select(dir => Path.Combine(dir, "Editor"))
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(firebaseEditorPath))
            {
                var files = Directory.GetFiles(firebaseEditorPath, "Firebase*_version*.txt");
                if (files.Length > 0)
                {
                    var versionInfo = new List<VersionInfo>();
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var split = fileName.Split('-');

                        var name = split[0].Replace("_version", string.Empty);
                        var version = split[1].Replace("_manifest", string.Empty);
                        s_networkIdMapping.TryGetValue(name, out var networkId);
                        versionInfo.Add(new VersionInfo(networkId, name, version, version, version));
                    }

                    return versionInfo;
                }
            }

            // FALLBACK: UPM packages-lock.json
            return GetFirebaseVersionsFromUpm();
        }

        /// <summary>
        /// Gets Firebase plugin versions from packages-lock.json (UPM fallback).
        /// </summary>
        private static List<VersionInfo> GetFirebaseVersionsFromUpm() => GetVersionInfoFromUpm(s_firebasePackages);

        private static string GetUnicoAPIClientVersion(Type _)
        {
            try
            {
                var packagesConfigPath = Path.Combine(ASSETS, "packages.config");
                if (!File.Exists(packagesConfigPath))
                {
                    LogError("packages.config not found!");
                    return null;
                }

                var xmlDocument = XDocument.Load(packagesConfigPath);
                var unicoApiClientPackage = xmlDocument.Descendants("package")
                    .FirstOrDefault(node => string.Equals(node.Attribute("id")?.Value, "unicoapiclient", StringComparison.OrdinalIgnoreCase));

                if (unicoApiClientPackage != null)
                {
                    return unicoApiClientPackage.Attribute("version")?.Value;
                }

                return null;
            }
            catch (Exception ex)
            {
                LogError($"Failed to get UnicoAPIClient version: {ex.Message}");
                return null;
            }
        }

        private static string GetGameId()
        {
            try
            {
                var unicoConfigType = FindTypeInAssemblies("Unico.Core.Config.UnicoConfig");
                if (unicoConfigType == null)
                {
                    LogError("UnicoConfig type not found!");
                    return null;
                }

                var instanceProperty = unicoConfigType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty == null)
                {
                    LogError("UnicoConfig.Instance property not found!");
                    return null;
                }

                var instance = instanceProperty.GetValue(null);
                if (instance == null)
                {
                    LogError("UnicoConfig.Instance is null!");
                    return null;
                }

                var gameIdProperty = unicoConfigType.GetProperty("GameId", BindingFlags.Public | BindingFlags.Instance);
                if (gameIdProperty == null)
                {
                    LogError("UnicoConfig.GameId property not found!");
                    return null;
                }

                return gameIdProperty.GetValue(instance)?.ToString();
            }
            catch (Exception ex)
            {
                LogError($"Failed to get GameId from UnicoConfig: {ex.Message}");
                return null;
            }
        }

        private static MediationTypes GetMediationTypes()
        {
            try
            {
                var adManagerSettingsType = FindTypeInAssemblies("Unico.Ads.Core.AdManagerSettings");
                if (adManagerSettingsType == null)
                {
                    LogError("AdManagerSettings type not found!");
                    return null;
                }

                // Find the AdManagerSettings asset
                var assets = AssetDatabase.FindAssets($"t:{adManagerSettingsType.Name}");
                if (assets.Length == 0)
                {
                    LogError("AdManagerSettings asset not found!");
                    return null;
                }

                var assetPath = AssetDatabase.GUIDToAssetPath(assets[0]);
                var adManagerSettingsInstance = AssetDatabase.LoadAssetAtPath(assetPath, adManagerSettingsType);
                
                if (adManagerSettingsInstance == null)
                {
                    LogError("Failed to load AdManagerSettings asset!");
                    return null;
                }

                var androidMediationProperty = adManagerSettingsType.GetProperty("AndroidMediation", BindingFlags.Public | BindingFlags.Instance);
                var iosMediationProperty = adManagerSettingsType.GetProperty("IosMediation", BindingFlags.Public | BindingFlags.Instance);

                if (androidMediationProperty == null || iosMediationProperty == null)
                {
                    LogError("Mediation properties not found in AdManagerSettings!");
                    return null;
                }

                var androidMediation = androidMediationProperty.GetValue(adManagerSettingsInstance)?.ToString();
                var iosMediation = iosMediationProperty.GetValue(adManagerSettingsInstance)?.ToString();

                return new MediationTypes(androidMediation, iosMediation);
            }
            catch (Exception ex)
            {
                LogError($"Failed to get mediation types from AdManagerSettings: {ex.Message}");
                return null;
            }
        }

        private static List<VersionInfo> GetGoogleOdmVersionsAsList(Type _)
        {
            try
            {
                // Find the AdjustGoogleODMDependencies.xml file
                var xmlFiles = Directory.GetFiles(ASSETS, "AdjustGoogleODMDependencies.xml", SearchOption.AllDirectories);
                if (xmlFiles.Length == 0)
                {
                    LogError("AdjustGoogleODMDependencies.xml file not found!");
                    return null;
                }

                var xmlPath = xmlFiles[0];
                var xmlDocument = XDocument.Load(xmlPath);

                // Find the iosPod elements
                var adjustGoogleOdmPod = xmlDocument.Descendants("iosPod")
                    .FirstOrDefault(pod => pod.Attribute("name")?.Value.Contains("Adjust/AdjustGoogleOdm") == true);

                var googleAdsOnDeviceConversionPod = xmlDocument.Descendants("iosPod")
                    .FirstOrDefault(pod => pod.Attribute("name")?.Value.Contains("GoogleAdsOnDeviceConversion") == true);

                var adjustGoogleOdmVersion = adjustGoogleOdmPod?.Attribute("version")?.Value;
                var googleAdsOnDeviceConversionVersion = googleAdsOnDeviceConversionPod?.Attribute("version")?.Value;

                if (string.IsNullOrEmpty(adjustGoogleOdmVersion) && string.IsNullOrEmpty(googleAdsOnDeviceConversionVersion))
                {
                    LogError("Failed to extract Google ODM versions from AdjustGoogleODMDependencies.xml!");
                    return null;
                }

                var versionInfo = new List<VersionInfo>();
                
                if (!string.IsNullOrEmpty(adjustGoogleOdmVersion))
                {
                    s_networkIdMapping.TryGetValue("AdjustGoogleOdm", out var adjustId);
                    versionInfo.Add(new VersionInfo(adjustId, "AdjustGoogleOdm", adjustGoogleOdmVersion, null, adjustGoogleOdmVersion));
                }
                
                if (!string.IsNullOrEmpty(googleAdsOnDeviceConversionVersion))
                {
                    s_networkIdMapping.TryGetValue("GoogleAdsOnDeviceConversion", out var googleId);
                    versionInfo.Add(new VersionInfo(googleId, "GoogleAdsOnDeviceConversion", googleAdsOnDeviceConversionVersion, null, googleAdsOnDeviceConversionVersion));
                }

                return versionInfo.Count > 0 ? versionInfo : null;
            }
            catch (Exception ex)
            {
                LogError($"Failed to get Google ODM versions: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds the type with the given <paramref name="typeFullName"/> in the loaded assemblies.
        /// </summary>
        /// <param name="typeFullName">The name of the type to search for.</param>
        /// <returns>The found type, or <c>null</c> if the type is not found.</returns>
        private static Type FindTypeInAssemblies(string typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName)) return null;

            // Search through all loaded assemblies
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeFullName))
                .FirstOrDefault(type => type != null);

            return type;
        }

        public record BuildInfo
        {
            public ProjectInfo ProjectInfo { get; }
            public List<SdkInfo> SdkInfo { get; }

            [JsonConstructor]
            public BuildInfo(ProjectInfo projectInfo, List<SdkInfo> sdkInfo)
            {
                ProjectInfo = projectInfo;
                SdkInfo = sdkInfo;
            }

            public BuildInfo(BuildSummary buildSummary)
            {
                ProjectInfo = new ProjectInfo(buildSummary);
                SdkInfo = s_sdkInfo;
                RefreshSdkInfo();
            }

            public BuildInfo()
            {
                SdkInfo = s_sdkInfo;
                RefreshSdkInfo();
            }
        }

        public record ProjectInfo
        {
            public string GameId { get; }
            public string Platform { get; }
            public string UnityVersion { get; }
            public string PackageName { get; }
            public string Version { get; }
            public string CompressionMethod { get; }
            public List<string> GraphicsAPIs { get; }
            public string ManagedStrippingLevel { get; }
            public string RenderPipeline { get; }
            public AndroidInfo Android { get; }
            public IOSInfo IOS { get; }
            public MediationTypes MediationTypes { get; }

            [JsonConstructor]
            public ProjectInfo(string gameId,
                string platform,
                string unityVersion,
                string packageName,
                string version,
                string compressionMethod,
                List<string> graphicsAPIs,
                string managedStrippingLevel,
                string renderPipeline,
                AndroidInfo android,
                IOSInfo ios,
                MediationTypes mediationTypes)
            {
                GameId = gameId;
                Platform = platform;
                UnityVersion = unityVersion;
                PackageName = packageName;
                Version = version;
                CompressionMethod = compressionMethod;
                GraphicsAPIs = graphicsAPIs;
                ManagedStrippingLevel = managedStrippingLevel;
                RenderPipeline = renderPipeline;
                Android = android;
                IOS = ios;
                MediationTypes = mediationTypes;
            }

            public ProjectInfo(BuildSummary buildSummary)
            {
                GameId = GetGameId();
                Platform = buildSummary.platform.ToString();
                UnityVersion = Application.unityVersion;
                PackageName = Application.identifier;
                Version = PlayerSettings.bundleVersion;
                CompressionMethod = GetCompressionMethod(buildSummary.options);
                GraphicsAPIs = GetGraphicsAPI(buildSummary.platform);
                ManagedStrippingLevel = GetManagedStrippingLevel(buildSummary.platformGroup);
                RenderPipeline = GetRenderPipeline();
                MediationTypes = GetMediationTypes();

                if (buildSummary.platform == BuildTarget.Android) Android = new AndroidInfo();
                if (buildSummary.platform == BuildTarget.iOS) IOS = new IOSInfo();
            }

            public record AndroidInfo
            {
                public int BundleVersionCode { get; }
                public int MinSdkVersion { get; }
                public int TargetSdkVersion { get; }

                [JsonConstructor]
                public AndroidInfo(int bundleVersionCode, int minSdkVersion, int targetSdkVersion)
                {
                    BundleVersionCode = bundleVersionCode;
                    MinSdkVersion = minSdkVersion;
                    TargetSdkVersion = targetSdkVersion;
                }

                public AndroidInfo()
                {
                    BundleVersionCode = PlayerSettings.Android.bundleVersionCode;
                    MinSdkVersion = (int)PlayerSettings.Android.minSdkVersion;
                    TargetSdkVersion = (int)PlayerSettings.Android.targetSdkVersion;
                }
            }

            public record IOSInfo
            {
                public int BuildNumber { get; }
                public string TargetOSVersion { get; }

                [JsonConstructor]
                public IOSInfo(int buildNumber, string targetOSVersion)
                {
                    BuildNumber = buildNumber;
                    TargetOSVersion = targetOSVersion;
                }

                public IOSInfo()
                {
                    BuildNumber = int.Parse(PlayerSettings.iOS.buildNumber);
                    TargetOSVersion = PlayerSettings.iOS.targetOSVersionString;
                }
            }

            private static string GetCompressionMethod(BuildOptions buildOptions)
            {
                return buildOptions.HasFlag(BuildOptions.CompressWithLz4) ? "LZ4" :
                    buildOptions.HasFlag(BuildOptions.CompressWithLz4HC) ? "LZ4HC" : "Default";
            }

            private static List<string> GetGraphicsAPI(BuildTarget target)
            {
                return PlayerSettings.GetGraphicsAPIs(target).Select(graphicsAPI => graphicsAPI.ToString()).ToList();
            }

            private static string GetManagedStrippingLevel(BuildTargetGroup buildTargetGroup)
            {
                return PlayerSettings.GetManagedStrippingLevel(buildTargetGroup).ToString();
            }

            private static string GetRenderPipeline()
            {
                try
                {
                    // Get the current render pipeline asset from Graphics Settings
                    var renderPipelineAsset = GraphicsSettings.currentRenderPipeline;
                    if (!renderPipelineAsset) return "Built-in";

                    // Get the type name of the render pipeline asset
                    var typeName = renderPipelineAsset.GetType().Name;

                    return typeName switch
                    {
                        "UniversalRenderPipelineAsset" => "URP",
                        "HDRenderPipelineAsset" => "HDRP",
                        _ => $"Custom ({typeName})"
                    };
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to determine render pipeline: {ex.Message}");
                    return "Unknown";
                }
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        public record SdkInfo
        {
            [JsonProperty] public string Name { get; private set; }
            [JsonProperty] public string Version { get; private set; }
            [JsonProperty] public List<VersionInfo> PluginVersionInfo { get; private set; }
            private SdkVersionGetter VersionGetter { get; }
            private string[] UpmPackageNames { get; }  // Not serialized - for UPM fallback

            [JsonConstructor]
            public SdkInfo(string name, string version)
            {
                Name = name;
                Version = version;
            }

            public SdkInfo(string name, SdkVersionGetter versionGetter, string[] upmPackageNames = null)
            {
                Name = name;
                VersionGetter = versionGetter;
                UpmPackageNames = upmPackageNames;
            }

            public void SetVersion()
            {
                if (VersionGetter == null) return;

                var type1 = FindTypeInAssemblies(VersionGetter.TypeFullName1);
                var type2 = FindTypeInAssemblies(VersionGetter.TypeFullName2);

                Version = VersionGetter.Getter1?.Invoke(type1);

                // FALLBACK: If version not found, try UPM packages-lock.json
                if (string.IsNullOrEmpty(Version) && UpmPackageNames != null)
                {
                    foreach (var packageName in UpmPackageNames)
                    {
                        Version = GetVersionFromPackagesLock(packageName);
                        if (!string.IsNullOrEmpty(Version)) break;
                    }
                }

                // Log error only if both primary and UPM fallback failed
                if (string.IsNullOrEmpty(Version) && VersionGetter.Getter1 != null)
                    LogError($"SDK '{Name}' not found in Assets folder or UPM.");

                PluginVersionInfo = VersionGetter.Getter2?.Invoke(type2);
            }
        }

        public record SdkVersionGetter(
            string TypeFullName1,
            Func<Type, string> Getter1,
            string TypeFullName2 = null,
            Func<Type, List<VersionInfo>> Getter2 = null)
        {
            public string TypeFullName1 { get; } = TypeFullName1;
            public Func<Type, string> Getter1 { get; } = Getter1;
            public string TypeFullName2 { get; } = TypeFullName2;
            public Func<Type, List<VersionInfo>> Getter2 { get; } = Getter2;

            public SdkVersionGetter(
                string typeFullName,
                Func<Type, string> getter1,
                Func<Type, List<VersionInfo>> getter2) : this(typeFullName, getter1, typeFullName, getter2)
            {
            }

            public SdkVersionGetter(
                string typeFullName,
                Func<Type, List<VersionInfo>> getter2) : this(typeFullName, null, typeFullName, getter2)
            {
            }
        }

        public record VersionInfo(string Id, string Name, string Version, string Android = null, string Ios = null)
        {
            public string Id { get; private set; } = Id;
            public string Name { get; private set; } = Name;
            public string Version { get; private set; } = Version;
            public string Android { get; private set; } = Android;
            public string Ios { get; private set; } = Ios;
        }

        public record MediationTypes(string Android, string Ios)
        {
            public string Android { get; private set; } = Android;
            public string Ios { get; private set; } = Ios;
        }
    }
}
# UnicoVersionTracker

**UnicoVersionTracker** is a Unity Editor tool designed to automatically track and save version information of third-party SDKs (e.g., AdMob, Firebase, Adjust, AppLovin) and build settings into a `.json` file. This tool also provides a manual export option via the Unity Editor for quick access to SDK version information.

---

## Features

- **Automatic SDK Detection:** Detects third-party SDKs in the project and retrieves their versions dynamically.
- **Build Metadata Logging:** Captures platform, compression method, and other relevant build details after each build.
- **Editor Integration:** Adds a **UnicoStudio** menu in Unity's top toolbar with a one-click **Export SdkInfo** button for manual version export.
- **Flexible Output:** Saves version and build metadata to `.json` files for better readability and structure.

---

## Installation

- Add this package to your project using Unity's Package Manager.  
   Paste the following Git URL into the "Add package from Git URL" option: https://github.com/unicostudio/UnicoVersionTracker.git

---

## How It Works

### Automatic Export (Post-Build)
After every build, the tool automatically collects:

- Third-party SDK versions
- Build settings, such as platform and compression method

The information is saved in a `.json` file located at: Assets/../UnicoVersionTracker/

### Manual Export (Editor)
You can manually export the SDK version information via the Unity Editor:

1. After adding the package, you'll see a **UnicoStudio** menu at the top of Unity.
2. Click the **Export SdkInfo** button to export current SDK versions.
3. The exported file is saved to: Assets/../UnicoVersionTracker/SdkInfo/
> **Note:** The manual export only includes SDK version information, not build-specific metadata.

---

## Configuration

No manual configuration is needed. The tool automatically detects installed SDKs and collects their version details. Supported SDKs include:

- **Google AdMob**
- **Firebase**
- **Adjust**
- **AppLovin**
- And more!

If an SDK is not present in the project, it is simply skipped.

---

## Contributing

Feel free to open issues or create pull requests to improve this package. Contributions are welcome!
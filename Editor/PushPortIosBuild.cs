#if UNITY_IOS
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PushPort.Unity.Editor
{
    internal static class PushPortIosBuild
    {
        [PostProcessBuild(900)]
        private static void InstallNativePackage(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS)
                return;
            if (!Version.TryParse(PlayerSettings.iOS.targetOSVersionString, out var minimum) || minimum.Major < 15)
                throw new BuildFailedException("The PushPort Apple SDK requires iOS 15 or later.");
            var settings = PushPortSettings.instance;
            var appId = settings.AppIdForBuild();

            // Unity's API usage detection can omit callbacks used by the native PushPort SDK.
            var preprocessorPath = Path.Combine(path, "Classes", "Preprocessor.h");
            if (File.Exists(preprocessorPath))
            {
                var original = File.ReadAllText(preprocessorPath);
                var updated = Regex.Replace(original,
                    @"^([ \t]*#[ \t]*define[ \t]+UNITY_USES_REMOTE_NOTIFICATIONS[ \t]+)0\b",
                    "${1}1",
                    RegexOptions.Multiline);
                if (updated != original)
                    File.WriteAllText(preprocessorPath, updated);
            }

            var projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            var framework = project.GetUnityFrameworkTargetGuid();
            var packageId = project.AddRemotePackageReferenceAtVersion(
                "https://github.com/IamFromUA/PushPortLibraryApple.git", "0.1.1");
            project.AddRemotePackageFrameworkToProject(framework, "PushPort", packageId, false);

            var package = PackageInfo.FindForAssembly(typeof(PushPortIosBuild).Assembly);
            const string directory = "Libraries/PushPortUnity";
            Directory.CreateDirectory(Path.Combine(path, directory));
            foreach (var file in new[] { "PushPortUnityBridge.swift", "PushPortUnityLifecycle.mm" })
            {
                var relative = directory + "/" + file;
                File.Copy(Path.Combine(package.resolvedPath, "Editor", "iOS", file + ".txt"),
                    Path.Combine(path, relative), true);
                var guid = project.FindFileGuidByProjectPath(relative);
                if (string.IsNullOrEmpty(guid))
                {
                    guid = project.AddFile(relative, relative);
                    project.AddFileToBuild(framework, guid);
                }
            }
            project.SetBuildProperty(framework, "SWIFT_VERSION", "5.0");

            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("PushPortUnityAppId", appId);
            plist.root.SetString("PushPortUnityAPNsEnvironment", settings.IosSandbox ? "sandbox" : "production");
            plist.root.SetBoolean("PushPortUnityConsentRequired", settings.ConsentRequired);
            var main = project.GetUnityMainTargetGuid();
            if (settings.IosManageExtension)
                ConfigureExtension(project, packageId, path, main, plist, settings);
            ConfigureEntitlements(project, path, main, "PushPortApp.entitlements",
                settings.IosManageExtension ? settings.IosAppGroup(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS)) : null,
                settings.IosSandbox ? "development" : "production");
            plist.WriteToFile(plistPath);
            project.WriteToFile(projectPath);
            WriteLocalPackageWorkspace(path);
        }

        // Xcode gives an explicitly opened workspace package precedence over its remote dependency.
        private static void WriteLocalPackageWorkspace(string exportPath)
        {
            var source = Environment.GetEnvironmentVariable("PUSHPORT_UNITY_LOCAL_APPLE_PACKAGE");
            if (string.IsNullOrWhiteSpace(source)) return;
            var directory = new DirectoryInfo(source);
            if (!string.Equals(directory.Name, "PushPortLibraryApple", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(directory.FullName, "Package.swift")))
                throw new BuildFailedException("PUSHPORT_UNITY_LOCAL_APPLE_PACKAGE must point to a PushPortLibraryApple directory containing Package.swift.");
            var workspace = Path.Combine(exportPath, "PushPortLocal.xcworkspace");
            Directory.CreateDirectory(workspace);
            new XDocument(new XElement("Workspace", new XAttribute("version", "1.0"),
                new XElement("FileRef", new XAttribute("location", "group:Unity-iPhone.xcodeproj")),
                new XElement("FileRef", new XAttribute("location", "absolute:" + directory.FullName))))
                .Save(Path.Combine(workspace, "contents.xcworkspacedata"));
        }

        private static void ConfigureExtension(PBXProject project, string packageId, string path,
            string main, PlistDocument app, PushPortSettings settings)
        {
            const string name = "PushPortNotificationExtension";
            var bundle = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
            var group = settings.IosAppGroup(bundle);
            if (!Regex.IsMatch(group, @"^group\.[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*$"))
                throw new BuildFailedException("Enter a valid iOS App Group in Project Settings > PushPort.");

            var folder = Path.Combine(path, name);
            var relativeInfo = name + "/Info.plist";
            var infoPath = Path.Combine(path, relativeInfo);
            var targetGuid = project.TargetGuidByName(name);
            if (string.IsNullOrEmpty(targetGuid))
            {
                foreach (var other in Directory.EnumerateFiles(path, "Info.plist", SearchOption.AllDirectories))
                {
                    if (other == infoPath || !File.ReadAllText(other).Contains("com.apple.usernotifications.service")) continue;
                    throw new BuildFailedException("An existing Notification Service Extension was found. Disable Configure iOS notification extension and integrate PushPort in that target.");
                }
                targetGuid = project.AddAppExtension(main, name, bundle + ".pushport-service", relativeInfo);
            }
            Directory.CreateDirectory(folder);
            var info = new PlistDocument();
            info.root.SetString("CFBundleIdentifier", "$(PRODUCT_BUNDLE_IDENTIFIER)");
            info.root.SetString("CFBundleExecutable", "$(EXECUTABLE_NAME)");
            info.root.SetString("CFBundleName", "$(PRODUCT_NAME)");
            info.root.SetString("CFBundlePackageType", "XPC!");
            info.root.SetString("CFBundleInfoDictionaryVersion", "6.0");
            info.root.SetString("CFBundleShortVersionString", app.root.values["CFBundleShortVersionString"].AsString());
            info.root.SetString("CFBundleVersion", app.root.values["CFBundleVersion"].AsString());
            info.root.SetString("PushPortAppGroup", group);
            app.root.SetString("PushPortAppGroup", group);
            var service = info.root.CreateDict("NSExtension");
            service.SetString("NSExtensionPointIdentifier", "com.apple.usernotifications.service");
            service.SetString("NSExtensionPrincipalClass", "$(PRODUCT_MODULE_NAME).NotificationService");
            info.WriteToFile(infoPath);

            var source = name + "/NotificationService.swift";
            File.WriteAllText(Path.Combine(path, source),
                "import PushPortNotificationService\nfinal class NotificationService: PushPortNotificationService {}\n");
            if (string.IsNullOrEmpty(project.FindFileGuidByProjectPath(source)))
                project.AddFileToBuild(targetGuid, project.AddFile(source, source));
            project.AddRemotePackageFrameworkToProject(targetGuid, "PushPortNotificationService", packageId, false);
            project.SetBuildProperty(targetGuid, "SWIFT_VERSION", "5.0");
            project.SetBuildProperty(targetGuid, "APPLICATION_EXTENSION_API_ONLY", "YES");
            project.SetBuildProperty(targetGuid, "IPHONEOS_DEPLOYMENT_TARGET", PlayerSettings.iOS.targetOSVersionString);
            project.SetBuildProperty(targetGuid, "SKIP_INSTALL", "YES");
            foreach (var setting in new[] { "DEVELOPMENT_TEAM", "TARGETED_DEVICE_FAMILY", "CURRENT_PROJECT_VERSION", "MARKETING_VERSION" })
            {
                var value = project.GetBuildPropertyForAnyConfig(main, setting);
                if (!string.IsNullOrEmpty(value)) project.SetBuildProperty(targetGuid, setting, value);
            }
            ConfigureEntitlements(project, path, targetGuid, name + "/" + name + ".entitlements", group, null);
        }

        private static void ConfigureEntitlements(PBXProject project, string path, string target,
            string defaultPath, string group, string environment)
        {
            // Tell Xcode's signing system which capabilities to provision as well as writing entitlements.
            var configs = project.BuildConfigNames().Select(name => project.BuildConfigByName(target, name))
                .Where(config => !string.IsNullOrEmpty(config))
                .ToDictionary(config => config, config => project.GetBuildPropertyForConfig(config, "CODE_SIGN_ENTITLEMENTS"));
            var capabilityPath = configs.Values.FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? defaultPath;
            // Unity tracks one entitlement file per PBXProject instance even across different targets.
            var signingProject = new PBXProject();
            signingProject.ReadFromString(project.WriteToString());
            if (group != null) signingProject.AddCapability(target, PBXCapabilityType.AppGroups, capabilityPath);
            if (environment != null) signingProject.AddCapability(target, PBXCapabilityType.PushNotifications, capabilityPath);
            project.ReadFromString(signingProject.WriteToString());
            foreach (var item in configs)
            {
                var config = item.Key;
                var relative = item.Value;
                if (string.IsNullOrEmpty(relative)) relative = defaultPath;
                relative = relative.Trim('"').Replace("$(SRCROOT)/", "").Replace("$(PROJECT_DIR)/", "");
                var full = Path.GetFullPath(Path.Combine(path, relative));
                if (relative.Contains("$") || !full.StartsWith(Path.GetFullPath(path) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new BuildFailedException("Cannot update entitlements outside the Xcode export. Check CODE_SIGN_ENTITLEMENTS.");
                var doc = new PlistDocument();
                if (File.Exists(full)) doc.ReadFromFile(full);
                if (environment != null) doc.root.SetString("aps-environment", environment);
                if (group != null)
                {
                    const string key = "com.apple.security.application-groups";
                    var groups = doc.root.values.TryGetValue(key, out var existing) ? existing.AsArray() : doc.root.CreateArray(key);
                    if (!groups.values.Any(item => item.AsString() == group)) groups.AddString(group);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                doc.WriteToFile(full);
                if (string.IsNullOrEmpty(project.FindFileGuidByProjectPath(relative))) project.AddFile(relative, relative);
                project.SetBuildPropertyForConfig(config, "CODE_SIGN_ENTITLEMENTS", relative);
            }
        }
    }
}
#endif

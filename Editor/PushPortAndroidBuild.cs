#if UNITY_ANDROID
using System;
using System.IO;
using System.Xml.Linq;
using UnityEditor.Android;
using UnityEditor.Build;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PushPort.Unity.Editor
{
    internal sealed class PushPortAndroidBuild : IPostGenerateGradleAndroidProject
    {
        private const string ApplyScript = "apply from: 'pushport-unity.gradle'";
        private const string ProviderClass = "dev.pushport.unity.PushPortUnityBridge$Initializer";
        private const string AppIdKey = "dev.pushport.unity.APP_ID";
        private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";
        public int callbackOrder => 900;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var appId = PushPortSettings.instance.AppIdForBuild();
            var package = PackageInfo.FindForAssembly(typeof(PushPortAndroidBuild).Assembly);
            var java = Path.Combine(path, "src", "main", "java", "dev", "pushport", "unity");
            Directory.CreateDirectory(java);
            File.Copy(Path.Combine(package.resolvedPath, "Editor", "Android", "PushPortUnityBridge.java.txt"),
                Path.Combine(java, "PushPortUnityBridge.java"), true);

            ConfigureLocalRepository(path);
            var gradle = Path.Combine(path, "build.gradle");
            if (!File.Exists(gradle))
                throw new BuildFailedException("PushPort could not find the Unity Android Gradle module.");
            if (!File.ReadAllText(gradle).Contains(ApplyScript))
                File.AppendAllText(gradle, "\n" + ApplyScript + "\n");
            File.WriteAllText(Path.Combine(path, "pushport-unity.gradle"),
                "dependencies { implementation 'dev.pushport:android-sdk:0.1.1' }\n" +
                "android { defaultConfig { consumerProguardFiles 'pushport-unity.pro' } }\n");
            File.WriteAllText(Path.Combine(path, "pushport-unity.pro"),
                "-keep class dev.pushport.unity.PushPortUnityBridge { public static *; }\n");

            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var manifest = XDocument.Load(manifestPath);
            var application = manifest.Root?.Element("application");
            if (application == null)
                throw new BuildFailedException("PushPort could not find the application in the Unity Android manifest.");
            SetApplicationElement(application, "meta-data", AppIdKey).SetAttributeValue(Android + "value", appId);
            SetApplicationElement(application, "meta-data", "dev.pushport.unity.CONSENT_REQUIRED")
                .SetAttributeValue(Android + "value", PushPortSettings.instance.ConsentRequired ? "true" : "false");
            var provider = SetApplicationElement(application, "provider", ProviderClass);
            provider.SetAttributeValue(Android + "authorities", "${applicationId}.pushport-unity");
            provider.SetAttributeValue(Android + "exported", "false");
            // AndroidX Startup's provider must initialize WorkManager first.
            provider.SetAttributeValue(Android + "initOrder", -100);
            manifest.Save(manifestPath);
        }

        private static void ConfigureLocalRepository(string module)
        {
            const string variable = "PUSHPORT_UNITY_LOCAL_ANDROID_REPOSITORY";
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))) return;
            var settings = Path.Combine(module, "..", "settings.gradle");
            if (!File.Exists(settings))
                throw new BuildFailedException("Cannot find settings.gradle for the requested local PushPort repository.");
            if (File.ReadAllText(settings).Contains(variable)) return;
            File.AppendAllText(settings,
                "\n// Local PushPort SDK integration tests\n" +
                "def pushPortRepository = System.getenv('" + variable + "')\n" +
                "if (pushPortRepository) {\n" +
                "    def localPushPort = dependencyResolutionManagement.repositories.maven { url = new File(pushPortRepository).toURI() }\n" +
                "    dependencyResolutionManagement.repositories.exclusiveContent {\n" +
                "        forRepositories(localPushPort)\n" +
                "        filter { includeModule('dev.pushport', 'android-sdk') }\n" +
                "    }\n" +
                "}\n");
        }

        private static XElement SetApplicationElement(XElement application, string kind, string name)
        {
            foreach (var child in application.Elements(kind))
                if ((string)child.Attribute(Android + "name") == name)
                    return child;
            var element = new XElement(kind, new XAttribute(Android + "name", name));
            application.Add(element);
            return element;
        }
    }
}
#endif

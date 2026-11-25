using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace PushPort.Unity.Editor
{
    [FilePath("ProjectSettings/PushPortSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class PushPortSettings : ScriptableSingleton<PushPortSettings>
    {
        [SerializeField] private string appId = "";
        [SerializeField] private bool iosSandbox = true;

        [SerializeField] private bool consentRequired;
        [SerializeField] private bool iosManageExtension = true;
        [SerializeField] private string iosAppGroup = "";

        internal bool ConsentRequired => consentRequired;
        internal bool IosManageExtension => iosManageExtension;
        internal string IosAppGroup(string bundle) => string.IsNullOrWhiteSpace(iosAppGroup) ? "group." + bundle + ".pushport" : iosAppGroup.Trim();

        internal bool IosSandbox => iosSandbox;

        internal string AppIdForBuild()
        {
            var value = Environment.GetEnvironmentVariable("PUSHPORT_UNITY_APP_ID");
            value = string.IsNullOrWhiteSpace(value) ? appId.Trim() : value.Trim();
            if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
                throw new BuildFailedException("Set a valid PushPort App ID in Project Settings > PushPort.");
            return id.ToString("D");
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/PushPort", SettingsScope.Project)
            {
                label = "PushPort",
                guiHandler = _ =>
                {
                    var settings = new SerializedObject(instance);
                    settings.Update();
                    EditorGUILayout.PropertyField(settings.FindProperty(nameof(appId)), new GUIContent("App ID"));
                    EditorGUILayout.PropertyField(settings.FindProperty(nameof(iosSandbox)), new GUIContent("Use APNs sandbox"));
                    EditorGUILayout.PropertyField(settings.FindProperty(nameof(consentRequired)), new GUIContent("Require consent before collection"));
                    EditorGUILayout.PropertyField(settings.FindProperty(nameof(iosManageExtension)), new GUIContent("Configure iOS notification extension"));
                    EditorGUILayout.PropertyField(settings.FindProperty(nameof(iosAppGroup)), new GUIContent("iOS App Group (optional override)"));
                    EditorGUILayout.HelpBox("Use the App ID from the PushPort dashboard. Enable APNs sandbox for development signing.", MessageType.Info);
                    if (settings.ApplyModifiedProperties())
                        instance.Save(true);
                }
            };
        }
    }
}

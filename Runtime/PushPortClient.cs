using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace PushPort.Unity
{
    [Serializable]
    public sealed class AppProfile
    {
        public string id = "default";
        public string name = "Основной";
        public string value = "";
    }

    [Serializable]
    public sealed class PushPortStatus
    {
        public AppProfile appProfile;
        public bool profileChecked;
        public bool configured;
        public string installationId;
        public bool hasToken;
        public bool notificationsEnabled;
        public bool subscribed;
        public string locale;
        public string userId;
        public string externalId;
        public long lastSyncedAt;
        public string syncError;
    }

    [Serializable]
    public sealed class PushPortUserTag
    {
        public string key;
        public string value;
    }

    [Serializable]
    public sealed class PushPortUser
    {
        public string userId;
        public string externalId;
        public long revision;
        public PushPortUserTag[] tags;
        public string email;
        public string phoneNumber;
        public bool hasLocation;
        public double latitude;
        public double longitude;
    }

    /// <summary>Call from Unity's main thread after setting the App ID in Project Settings.</summary>
    public static class PushPortClient
    {
        public static AppProfile GetAppProfile()
        {
            return GetStatus().appProfile;
        }

        public static async System.Threading.Tasks.Task<AppProfile> ResolveAppProfileAsync()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var status = GetStatus();
            while (status.configured && !status.profileChecked && clock.ElapsedMilliseconds < 1000)
            {
                await System.Threading.Tasks.Task.Delay(25);
                status = GetStatus();
            }
            return status.appProfile;
        }

        public static PushPortStatus GetStatus()
        {
            string json = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            json = CallAndroid<string>("getStatus");
#elif UNITY_IOS && !UNITY_EDITOR
            var pointer = PushPortUnityGetStatus();
            if (pointer != IntPtr.Zero)
            {
                try { json = Marshal.PtrToStringUTF8(pointer); }
                finally { PushPortUnityFree(pointer); }
            }
#endif
            var status = string.IsNullOrEmpty(json) ? new PushPortStatus() : JsonUtility.FromJson<PushPortStatus>(json);
            if (string.IsNullOrEmpty(status.appProfile?.id)) status.appProfile = new AppProfile();
            return status;
        }

        /// <summary>Last profile acknowledged by PushPort, or null before registration.</summary>
        public static PushPortUser GetUser()
        {
            string json = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            json = CallAndroid<string>("getUser");
#elif UNITY_IOS && !UNITY_EDITOR
            var pointer = PushPortUnityGetUser();
            try { if (pointer != IntPtr.Zero) json = Marshal.PtrToStringUTF8(pointer); }
            finally { if (pointer != IntPtr.Zero) PushPortUnityFree(pointer); }
#endif
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PushPortUser>(json);
        }

        /// <summary>Starts the system permission flow. Read GetStatus after the user responds.</summary>
        public static void RequestPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            CallAndroid("requestPermission");
#elif UNITY_IOS && !UNITY_EDITOR
            PushPortUnityRequestPermission();
#endif
        }

        public static void SetSubscribed(bool enabled)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            CallAndroid("setSubscribed", enabled);
#elif UNITY_IOS && !UNITY_EDITOR
            PushPortUnitySetSubscribed(enabled ? 1 : 0);
#endif
        }

        /// <summary>Sets one user tag. Pass null to remove that tag.</summary>
        public static void SetTag(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A tag key is required.", nameof(key));
#if UNITY_ANDROID && !UNITY_EDITOR
            if (value == null)
                CallAndroid("removeTag", key);
            else
                CallAndroid("setTag", key, value);
#elif UNITY_IOS && !UNITY_EDITOR
            PushPortUnitySetTag(key, value);
#endif
        }

        [Serializable]
        private sealed class Entry
        {
            public string key;
            public string value;
            public bool clear;
        }

        [Serializable]
        private sealed class Update
        {
            public string kind;
            public string text;
            public bool clear;
            public bool enabled;
            public double latitude;
            public double longitude;
            public Entry[] entries;
        }

        private static Entry[] Entries(IDictionary<string, string> values)
        {
            if (values == null) return Array.Empty<Entry>();
            var result = new List<Entry>(values.Count);
            foreach (var pair in values)
                result.Add(new Entry { key = pair.Key, value = pair.Value, clear = pair.Value == null });
            return result.ToArray();
        }

        private static void Apply(Update update)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            CallAndroid("applyUpdate", JsonUtility.ToJson(update));
#elif UNITY_IOS && !UNITY_EDITOR
            PushPortUnityApplyUpdate(JsonUtility.ToJson(update));
#endif
        }

        public static void SetLocale(string languageTag) =>
            Apply(new Update { kind = "locale", text = languageTag, clear = languageTag == null });

        public static void SetTags(IDictionary<string, string> tags) =>
            Apply(new Update { kind = "tags", entries = Entries(tags ?? throw new ArgumentNullException(nameof(tags))) });

        public static void SetEmail(string email) =>
            Apply(new Update { kind = "email", text = email, clear = email == null });

        public static void SetPhoneNumber(string phone) =>
            Apply(new Update { kind = "phone", text = phone, clear = phone == null });

        public static void SetLocation(double? latitude, double? longitude)
        {
            if (latitude.HasValue != longitude.HasValue || latitude < -90 || latitude > 90 ||
                longitude < -180 || longitude > 180 || double.IsNaN(latitude ?? 0) || double.IsNaN(longitude ?? 0))
                throw new ArgumentException("Supply two valid coordinates or clear both.");
            Apply(new Update { kind = "location", latitude = latitude ?? 0, longitude = longitude ?? 0, clear = !latitude.HasValue });
        }

        public static void TrackEvent(string name, IDictionary<string, string> properties = null) =>
            Apply(new Update { kind = "event", text = name, entries = Entries(properties) });

        public static void SetConsentRequired(bool required) => Apply(new Update { kind = "consentRequired", enabled = required });
        public static void SetConsentGiven(bool given) => Apply(new Update { kind = "consentGiven", enabled = given });

        public static void Login(string externalId, string identityToken)
        {
            if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(identityToken))
                throw new ArgumentException("The player ID and a server-issued PushPort identity token are required.");
#if UNITY_ANDROID && !UNITY_EDITOR
            CallAndroid("login", externalId, identityToken);
#elif UNITY_IOS && !UNITY_EDITOR
            PushPortUnityLogin(externalId, identityToken);
#endif
        }

        public static void Logout()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            CallAndroid("logout");
#elif UNITY_IOS && !UNITY_EDITOR
            PushPortUnityLogout();
#endif
        }

        public static void Sync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            CallAndroid("sync");
#elif UNITY_IOS && !UNITY_EDITOR
            PushPortUnitySync();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string BridgeClass = "dev.pushport.unity.PushPortUnityBridge";

        private static void CallAndroid(string method, params object[] arguments)
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var bridge = new AndroidJavaClass(BridgeClass);
            if (activity == null)
                throw new InvalidOperationException("PushPort requires an active Android player.");
            bridge.CallStatic(method, WithActivity(activity, arguments));
        }

        private static T CallAndroid<T>(string method)
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var bridge = new AndroidJavaClass(BridgeClass);
            return activity == null ? default : bridge.CallStatic<T>(method, activity);
        }

        private static object[] WithActivity(AndroidJavaObject activity, object[] arguments)
        {
            var all = new object[arguments.Length + 1];
            all[0] = activity;
            arguments.CopyTo(all, 1);
            return all;
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern IntPtr PushPortUnityGetStatus();
        [DllImport("__Internal")] private static extern IntPtr PushPortUnityGetUser();
        [DllImport("__Internal")] private static extern void PushPortUnityFree(IntPtr pointer);
        [DllImport("__Internal")] private static extern void PushPortUnityRequestPermission();
        [DllImport("__Internal")] private static extern void PushPortUnitySetSubscribed(int enabled);
        [DllImport("__Internal")] private static extern void PushPortUnitySetTag(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport("__Internal")] private static extern void PushPortUnityApplyUpdate(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string json);
        [DllImport("__Internal")] private static extern void PushPortUnityLogin(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string externalId, [MarshalAs(UnmanagedType.LPUTF8Str)] string identityToken);
        [DllImport("__Internal")] private static extern void PushPortUnityLogout();
        [DllImport("__Internal")] private static extern void PushPortUnitySync();
#endif
    }
}

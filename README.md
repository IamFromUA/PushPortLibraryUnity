# PushPort for Unity

The Unity package connects the existing PushPort Android and Apple SDKs to C#. It targets Unity 6.3 LTS and mobile builds.

1. Open **Window → Package Manager → + → Install package from git URL** and enter `https://github.com/IamFromUA/PushPortLibraryUnity.git#0.1.1`. For a local checkout, select **Install package from disk** and choose `package.json`.
2. Open **Edit → Project Settings → PushPort** and enter the App ID from the PushPort console.
3. Enable Android (Firebase) and/or iOS (APNs) for that app in PushPort. On Android use compile SDK 36 or later and include `google()` and `mavenCentral()` in Gradle repositories. On iOS set deployment target 15 or later, select the signing Team and enable automatic signing or supply profiles for both the app and its extension. Enable **Use APNs sandbox** for development provisioning. Disable it for TestFlight and App Store.
4. Call `PushPortClient.RequestPermission()` from your own notification prompt or button.

```csharp
using PushPort.Unity;

PushPortClient.RequestPermission();
PushPortClient.SetTag("level", "10");
var status = PushPortClient.GetStatus();
```

For an Android development build, set `PUSHPORT_UNITY_LOCAL_ANDROID_REPOSITORY` to the absolute directory of a local Maven repository containing `dev.pushport:android-sdk:0.1.1` before starting Unity. The generated Gradle project resolves this module exclusively from that repository when the variable is set.

For an iOS development build with an unreleased Apple SDK, set `PUSHPORT_UNITY_LOCAL_APPLE_PACKAGE` to the absolute path of the `PushPortLibraryApple` checkout before exporting. Open the generated `PushPortLocal.xcworkspace` or pass it to `xcodebuild -workspace`. The game and Notification Service Extension then use the same local package. For a build with the published SDK, open the standard Unity project.

The package starts the native SDK at app launch. `GetStatus()` reads a local snapshot. A positive `lastSyncedAt` means that the server acknowledged a synchronization. To link a player's account, call `Login(externalId, identityToken)` with a short-lived token from your backend. Call `Logout()` on sign-out. Pass `null` to `SetTag` to remove that key.

## Application profiles

Available in Unity SDK 0.1.1 with Android 0.1.1 or Apple 0.1.1.

An operator must allow profiles for the project's owner in **Users**. The owner or a manager can then enable **App profiles** at project creation or in Settings. Both switches default to off. Each project has up to three named profiles including `default`. IDs survive renames. The optional `value` is public and can identify a screen or a content variant.

```csharp
var profile = await PushPortClient.ResolveAppProfileAsync(); // from the Unity main thread
if (profile.value == "catalog") OpenCatalog(); else OpenHome();
```

`PushPortClient.GetAppProfile()` reads the snapshot immediately. Use Android and Apple SDK builds with app-profile support.

The startup check shares the normal configuration request. When the network is slow, the resolver waits up to about one second and uses the last confirmed profile, or `default` on the first offline launch. Turning either switch off selects the default on the next successful check. The app chooses its own screen and can ignore unknown values. Reading the profile needs no notification permission.

## Language, profile and consent

In the development SDK, `syncError == "identity_token_required"` means a queued login needs a fresh identity token. Obtain it from your backend for the same installation and External ID, then call login again. Pending profile changes resume in order. Notification statistics continue to sync while login waits. Logging out or logging in with a different External ID discards edits queued for the prior account.

Call `SetLocale("ru-RU")` when the player selects Russian in the game. Pass `null` to restore platform language detection. The registration also includes system languages and timezone. `GetStatus()` exposes the current locale, installation ID and last confirmed user and external IDs. `GetUser()` reads the last confirmed profile with its tags, contacts, location and revision, or null before registration.

`SetTags(dictionary)` applies one patch. Omitted keys keep their values. A null value removes that key. `TrackEvent(name, properties)` accepts string properties. `SetEmail`, `SetPhoneNumber` and `SetLocation` store explicitly supplied profile fields. Pass null to clear a contact or two null coordinates to clear location.

If collection requires consent, enable **Require consent before collection** in Project Settings before building. Call `SetConsentGiven(true)` after consent and `SetConsentGiven(false)` on withdrawal.

## Images and clicks

Android uses the native SDK for the picture, large icon and bundled status-bar icon. Typical iOS notifications show the app icon. The iOS exporter enables Push Notifications, embeds a notification service extension and configures its shared App Group with the app. The default group is `group.<bundle-id>.pushport`. Both provisioning profiles must include it. Set an existing group in **iOS App Group** if required. If a host already has a service extension, disable **Configure iOS notification extension**, integrate `PushPortNotificationService` there and configure the same App Group and `PushPortAppGroup` entry in both targets.

A tap opens the game or its public HTTPS destination. The SDK records the tap and the navigation result separately. The iOS extension queues background receipt and image events for the main app's next synchronization. A text alert can still be delivered when its image fails to load.

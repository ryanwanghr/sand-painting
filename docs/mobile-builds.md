# Android and iOS builds

Use Unity **6000.6.9f1**. Both platforms use app ID
`com.mandala.sandpainting`, IL2CPP, portrait orientation, and
`Assets/Scenes/Main.unity`. Android targets API 36, supports Android 8.0/API 26
and later, and builds ARM64. iOS retains the project's minimum version of 15.0.

## Android

Install Android Build Support, OpenJDK, and the Android SDK/NDK tools for this
Editor version. The SDK must also include **CMake 3.22.1**. If Unity reports it
missing, run that SDK's `cmdline-tools/21.0/bin/sdkmanager --install 'cmake;3.22.1'`.
Select the Android profile in **File > Build Profiles** and
switch to it. Save scene changes and stop Play Mode before building.
The committed Android profile is
`Assets/Settings/Build Profiles/Android Release.asset`.

- **Tools > Mandala > Build > Android Google Play Bundle** writes
  `Builds/Android/SandPainting.aab` for Play Console.
- **Tools > Mandala > Build > Android Device APK** writes
  `Builds/Android/SandPainting.apk` for direct device installation.

Both commands create release builds with the upload key. They load signing
credentials only for the build and clear the passwords afterward. Build outputs
and signing keys are ignored by Git. Each output has a `.build-summary.txt` file
with Unity's build result, error/warning counts, size, and output location.

Signing configuration is outside the repository:
`~/.local/share/sand-painting/android-upload/signing.json` on this Linux machine.
It contains `keystorePath`, `keystorePassword`, `keyAlias`, and `keyPassword`.
The upload keystore and public `upload-certificate.pem` live beside it.
Back up this directory securely; the configuration contains private credentials.
Copy it securely to another build machine and update `keystorePath` there.
The helper uses the current user's local application data directory on each OS.

SDK/JDK/NDK paths are local Editor preferences, not committed project settings.
The 2026-11-16 migration validation uses Unity 6000.6.9f1's embedded Android
toolchain, including CMake 3.22.1 installed into its SDK.

## Verified Android artifacts

On 2026-11-16, after the URP 17.6.0 and Input System 1.20.1 migration, Unity
6000.6.9f1 built an Android ARM64 IL2CPP **development AAB** with **zero errors
and zero warnings**. Validation output is
`/tmp/sand-painting-migration/SandPainting.aab` (37,947,263 bytes), with the
full Unity report in `build-report.json` beside it. This build uses debug signing
and does not replace the release artifacts below. Rebuild through the release
menu commands before distributing the migrated version.

On 2026-11-15, Unity built the release AAB with **zero errors and zero warnings**.
Google's bundletool validated it and generated a signed universal APK from the
same bundle. Both use version `1.0`, version code `1`, app ID
`com.mandala.sandpainting`, minimum API 26, target API 36, and ARM64 only.
The AAB is approximately 20.7 MB; the APK is approximately 20.8 MB.

JAR/APK signatures were verified, all six native libraries have 16 KB load
segment alignment, and the APK passed the Android 16 KB ZIP alignment check.
`Builds/Android/validation.json` records the checks and SHA-256 hashes;
`SandPainting.manifest.xml` records the bundle's manifest. No physical Android
device was connected, so installation, touch, audio, and runtime performance
on a phone remain unverified. No iOS player has been built yet.

For another upload, increase **Player Settings > Android > Other Settings >
Bundle Version Code**. Keep the app ID and upload key consistent. Enable Play
App Signing in Play Console and upload the AAB to Internal testing first.
Run the installed game on a real phone before promoting a release: painting,
pinch/zoom, undo, safe areas, save/relaunch, background/resume, audio, and
sustained performance all need device validation.

## iOS

The shared iOS Player Settings are configured. Install iOS Build Support for
this Editor version on the build machine, switch the Build Profile to iOS, then
use **Tools > Mandala > Build > iOS Xcode Project**. This exports `Builds/iOS`.

Use a Mac with Xcode for the final device build, archive, signing, and upload.
Select your Apple Developer team, register `com.mandala.sandpainting`, create
the App Store Connect record, and distribute through TestFlight before release.
No Apple team, provisioning profile, or signing certificate is embedded in
this project.

## Reconfigure

`Mandala.Editor.MandalaMobileBuild.Configure("com.mandala.sandpainting")`
applies the shared mobile settings through Unity. It does not install modules,
switch the active platform, create a key, or change version numbers.

The Editor commands above build from the saved Main scene. The Android and iOS
builds overwrite their corresponding output paths; retain released artifacts
separately when you need historical versions.

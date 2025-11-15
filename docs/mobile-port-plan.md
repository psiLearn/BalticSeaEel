# Mobile Port Plan

## Goals

- Reuse the existing SAFE-stack client (Elmish/Fable) on Android and iOS.
- Use a .NET MAUI host similar to [SAFE Nightwatch](https://github.com/SAFE-Stack/SAFE-Nightwatch).
- Maintain the existing browser build while adding mobile targets.
- Provide responsive/touch-friendly UI and ship through platform stores.

---

## Prerequisites

1. **Tooling**
   - Visual Studio 2022 (latest) with the .NET MAUI workload installed.
   - Android SDK/emulator; Xcode/iOS simulator (requires macOS or Mac build host).
   - Node.js + npm for the existing Fable client builds.
   - .NET 8 SDK (matching current solution).

2. **Baseline Projects**
   - Clone SAFE Nightwatch or create a new solution via its template.
   - Ensure the MAUI project alone builds and runs on Android/iOS emulators before integrating the current client.

---

## Step-by-Step Plan

### 1. Scaffold MAUI Host
1. Pull in the Nightwatch repo or template into `src/Mobile`.
2. Build & run the MAUI project (`dotnet build src/Mobile/Mobile.fsproj`, `dotnet build -t:Run -f net8.0-android`).
3. Verify the default MAUI app loads in emulators (Android, optionally iOS).

### 2. Prepare Fable Client Build Output
1. Update the client `package.json` to include a `build-mobile` script that outputs to a known folder (e.g., `src/Mobile/wwwroot`).
2. Confirm `npm run build-mobile` produces `index.html`, JS bundles, CSS, assets.

### 3. Wire Client into MAUI WebView
1. In the MAUI project:
   - Add `wwwroot` with the Fable assets.
   - Configure `MauiProgram` to load `index.html` (BlazorWebView or WebView).
   - Set `MauiAsset` entries in the `.csproj` for the Fable bundle.
2. Add a partial class to expose WebView message hooks (if Elmish needs native communication).

### 4. Touch/Device Input Integration
1. Implement swipe or on-screen buttons in F# (use `PointerEvents`).
2. If needed, add native MAUI overlays (XAML) that send commands to the WebView via JavaScript injection.
3. Handle platform gestures:
   - Android back button → pause dialog.
   - iOS safe area insets adjustments for layout.

### 5. Responsive Layout Tweaks
1. Use the existing viewport sizing and compact layout logic to make sure the board scales to phone screens.
2. Add MAUI-specific CSS overrides (if needed) when running inside the app (nightwatch uses query string or JS flag).
3. Ensure the mini overlay, scoreboard visibility, and touch controls work in portrait/landscape.

### 6. Platform Configuration
1. **Android**:
   - Update `AndroidManifest.xml` (package name, internet permission).
   - Configure signing for release APK/AAB.
2. **iOS**:
   - Set bundle identifier, add entitlements.
   - Confirm network security (ATS) if calling APIs.


### 7. CI/CD Pipeline
1. Extend GitHub Actions (or Azure DevOps) to:
   - Run `npm run build-mobile`.
   - Build MAUI app for Android (`dotnet publish -f net8.0-android`).
   - Optionally integrate iOS builds (requires macOS runner).
2. Publish artifacts or push to TestFlight / internal Google Play tracks.

### 8. QA & Release
1. Test on physical devices (touch latency, battery usage).
2. Validate offline behavior (MAUI assets are packaged; API access may require CORS adjustments).
3. Document installation & submission steps (screenshots, store listing assets).

---

## Tips & Gotchas

- **Hot Reload:** Use MAUI Hot Reload for native layout adjustments; Fable client still needs `npm run start` for web edits.
- **State Sync:** Keep Elmish state as the source of truth; store viewport info via `WindowResized` message to react to device rotation.
- **Native APIs:** If accessing sensors or storage, expose a JS-interop bridge from MAUI to Fable.
- **Testing:** Consider MAUI UI tests (AppCenter, Playwright-in-WebView) once the WebView is running the client.

---

## Next Steps

1. Create the `src/Mobile` MAUI project (from Nightwatch template).
2. Integrate the current Fable client build into the MAUI `wwwroot`.
3. Implement touch controls and test on Android emulator.
4. Iterate on responsive/touch UX before proceeding to iOS and store deployments.

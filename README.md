# PhoneTrackpad

Turn a Windows PC into a wireless trackpad controlled from a phone's browser.

## Download

Download the self-contained Windows x64 executable directly: [PhoneTrackpad.exe](https://github.com/lelotkiosk/TrackPadWeb/raw/master/downloads/PhoneTrackpad.exe) (46.4 MiB). No separate .NET installation is required.

**SHA-256:** `691E6074C7B0D588604A0470C315BAFD1918F76DBA16C03B029820BADEB2E41A`

## Run

1. Run `PhoneTrackpad.exe` on the Windows PC. Windows may ask for administrator approval to add a firewall rule.
2. Keep the console window open. It prints an HTTP URL for the phone.
3. Connect the phone to the same trusted Wi-Fi/local network.
4. Press Ctrl+C in the console to stop the server.

To start the server automatically whenever your Windows account signs in, run `.\Install-AutoStart.ps1` in PowerShell from this project directory after building or downloading the executable. The script registers a per-user Scheduled Task, starts the server immediately if it is not already running, and configures it to restart after failures. It defaults to the published executable under `bin` or `downloads`; pass `-ServerExePath` if the executable is elsewhere. The server must run in your signed-in desktop session so it can control the PC's mouse and keyboard; it is not configured as a Windows service.

## Fullscreen Android app

The native Android app is named **Phone Trackpad - Fullscreen Remote** and has a custom icon. It opens to a mode chooser, hides browser address bars and Android system bars, and switches to landscape when a control mode opens. It does not need a certificate or internet tunnel.

1. Build the APK with the command below, or use [`PhoneTrackpad-Fullscreen-Remote.apk`](AndroidApp/bin/Release/net9.0-android/publish/PhoneTrackpad-Fullscreen-Remote.apk) after building.
2. Copy the APK to your Android phone and open it. If prompted, allow your file manager to install unknown apps, then install **Phone Trackpad**.
3. Choose a connection mode:
   - **Wi-Fi trackpad:** Start `PhoneTrackpad.exe` on the PC, enter the URL it prints, and keep both devices on the same local network. The PC server must stay running.
   - **Bluetooth keyboard & mouse:** On Android 9 or later, tap **Use Bluetooth keyboard & mouse**, then **Start Bluetooth on this phone** and wait for the HID-ready message. Tap **Make phone discoverable** and approve Android's visibility prompt. Within five minutes, on the PC open **Settings → Bluetooth & devices → Add device → Bluetooth** and select the phone's Bluetooth name (it may not say “Phone Trackpad”). After Windows pairs, return to the app, tap **Refresh PC list**, select the PC, then tap **Connect**. Keep the app open until its status changes from **Connecting** to **Connected** and the trackpad/keyboard screen appears. If Windows drops the link, the app reports the disconnect and returns to setup. The Bluetooth control screen uses the same fullscreen trackpad style and a fixed-fit keyboard layout as Wi-Fi mode. The keyboard shows Caps Lock and held-modifier indicators; Windows lock LEDs update the Caps Lock indicator. The app follows the HID protocol Windows selects (Boot or Report); its status reports that the phone sent an input report, not that Windows applied it. One finger moves/clicks; hold still for the haptic cue, then move to drag; two fingers scroll (or tap for right-click). Slow pointer movements are accumulated instead of discarded. Keep the app open and connected while you use it; once connected, this mode works independently of the PC server and can send input at the Windows sign-in screen.
4. The Bluetooth HID feature depends on Android's device-profile support, which varies by phone. Use a physical phone and PC to confirm pairing and connection. The first pairing is done manually in Windows Bluetooth settings.

Build the Android APK from the project directory with .NET 9 and the Android workload installed:

```powershell
dotnet publish .\AndroidApp\PhoneTrackpad.Mobile.csproj -f net9.0-android -c Release -p:AndroidPackageFormat=apk
```

The Wi-Fi mode requires the Windows PC server. Bluetooth HID mode connects directly to Windows and does not require the server.

### iPhone

On iPhone, open the HTTP URL in Safari and use **Share → Add to Home Screen**. iOS does not install Android APKs.

The phone controls the PC with touch gestures:

- One-finger movement: move the cursor; tap: left-click. To drag, hold your finger still until the haptic cue, then move. While holding the drag, push your finger into an edge and keep it there to glide the cursor; the farther into the edge zone, the faster it moves. The edge glows blue while active. Lift to release.
- Two-finger tap: right-click; two-finger drag: scroll.
- Three-finger pinch: zoom.
- The virtual keyboard gives a light haptic tap when a key is pressed. Hold a key to repeat it, or slide to another key to release the first and press the next. The web keyboard shows a compact Caps Lock indicator above the keys and changes letter case to match Windows; holding **Shift** shows alternate symbols and an active Shift indicator. The keyboard fits within its panel without scrolling. Tap **Space** to type a space; hold it and slide left/right to move the text cursor. Modifier keys stay held until you lift. Hold **Win**, slide through **Shift** to **S**, then lift to open Windows screen snipping.

## Security and compatibility

This build is for Windows x64 and uses the PC's mouse and keyboard APIs. Use it only on a trusted local network; the app serves an unauthenticated HTTP page and should not be exposed to the public internet or port-forwarded. The phone browser also needs access to the internet to load the SignalR client library from its CDN.

## Build from source

Requires the .NET 9 SDK. From the project directory, run:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true
```

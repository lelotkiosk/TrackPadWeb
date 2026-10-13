# PhoneTrackpad

Turn a Windows PC into a wireless trackpad controlled from a phone's browser.

## Download

Download the self-contained Windows x64 executable: [PhoneTrackpad.exe](downloads/PhoneTrackpad.exe) (95.2 MiB). No separate .NET installation is required.

**SHA-256:** `771846F70DC546F79765D584148A490F5645AEAF3B9AD33FF1A11A4D4B06D849`

## Run

1. Run `PhoneTrackpad.exe` on the Windows PC. Windows may ask for administrator approval to add a firewall rule.
2. Keep the console window open. It prints a URL for the phone.
3. Connect the phone to the same Wi-Fi/local network and open that URL in its browser.
4. Press Ctrl+C in the console to stop the server.

The phone controls the PC with touch gestures:

- One-finger drag: move the cursor; tap: left-click; hold and drag: select/drag.
- Two-finger tap: right-click; two-finger drag: scroll.
- Three-finger pinch: zoom.

## Security and compatibility

This build is for Windows x64 and uses the PC's mouse and keyboard APIs. Use it only on a trusted local network; the app serves an unauthenticated HTTP page and should not be exposed to the public internet or port-forwarded. The phone browser also needs access to the internet to load the SignalR client library from its CDN.

## Build from source

Requires the .NET 9 SDK. From the project directory, run:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

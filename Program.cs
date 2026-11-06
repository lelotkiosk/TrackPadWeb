using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System;
using System.Windows.Forms;

public class TrackpadHub : Hub
{
    [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
    [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    [DllImport("user32.dll")] static extern short GetKeyState(int virtualKey);

    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_WHEEL = 0x0800;
    const uint KEYEVENTF_KEYUP = 0x0002;

    const byte VK_VOLUME_MUTE = 0xAD;
    const byte VK_VOLUME_DOWN = 0xAE;
    const byte VK_VOLUME_UP = 0xAF;
    const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    const byte VK_MEDIA_PREV_TRACK = 0xB1;
    const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

    static double remainderX = 0;
    static double remainderY = 0;

    public void Move(double dx, double dy)
    {
        double sensitivity = 1.6;
        double totalDx = (dx * sensitivity) + remainderX;
        double totalDy = (dy * sensitivity) + remainderY;
        int moveX = (int)totalDx;
        int moveY = (int)totalDy;
        remainderX = totalDx - moveX;
        remainderY = totalDy - moveY;

        if (moveX != 0 || moveY != 0) mouse_event(MOUSEEVENTF_MOVE, moveX, moveY, 0, 0);
    }

    public void MouseDown()
    {
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
    }

    public void MouseUp()
    {
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    public void Click(string button)
    {
        if (button == "left") {
            mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }
        if (button == "right") mouse_event(0x0008 | 0x0010, 0, 0, 0, 0);
    }

    public void Scroll(int deltaY)
    {
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, deltaY * 20, 0);
    }

    public void Zoom(int direction)
    {
        keybd_event(0x11, 0, 0, 0);
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, direction * 120, 0);
        keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0);
    }

    public void KeyDown(byte vk)
    {
        keybd_event(vk, 0, 0, 0);
    }

    public void KeyUp(byte vk)
    {
        keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
    }

    public void PressKey(byte vk)
    {
        keybd_event(vk, 0, 0, 0);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
    }

    public bool GetCapsLockState() => (GetKeyState(0x14) & 1) != 0;

    public void MediaControl(string action)
    {
        byte vk = action switch
        {
            "vol_up" => VK_VOLUME_UP,
            "vol_down" => VK_VOLUME_DOWN,
            "mute" => VK_VOLUME_MUTE,
            "play_pause" => VK_MEDIA_PLAY_PAUSE,
            "next" => VK_MEDIA_NEXT_TRACK,
            "prev" => VK_MEDIA_PREV_TRACK,
            _ => 0
        };

        if (vk != 0)
        {
            keybd_event(vk, 0, 0, 0);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
        }
    }
}

public class Program
{
    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            RunTrayApplicationAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"PhoneTrackpad could not start.\n\n{ex.GetBaseException().Message}",
                "PhoneTrackpad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    static async Task RunTrayApplicationAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Services.AddSignalR();

        int port = FindAvailablePort(5000);
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        string phoneUrl = $"http://{GetLocalIpAddress()}:{port}";
        using var app = builder.Build();

        EnsureFirewallRule();
        app.MapHub<TrackpadHub>("/trackpad");
        app.MapGet("/", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Content(TRACKPAD_HTML, "text/html");
        });
        app.MapGet("/manifest.json", () => Results.Content(MANIFEST_JSON, "application/json"));
        app.MapGet("/sw.js", () => Results.Content(SERVICE_WORKER_JS, "application/javascript"));
        app.MapGet("/app-icon.svg", () => Results.Content(APP_ICON_SVG, "image/svg+xml"));

        await app.StartAsync();
        using var trayContext = new PhoneTrackpadTrayContext(app, phoneUrl);
        Application.Run(trayContext);
        await app.StopAsync();
    }

    static int FindAvailablePort(int startPort)
    {
        int port = startPort;
        while (IsPortInUse(port))
        {
            port++;
        }

        return port;
    }

    static bool IsPortInUse(int port) =>
        System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Any(endpoint => endpoint.Port == port);

    static string GetLocalIpAddress()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
        try
        {
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
        }
        catch (SocketException)
        {
            return "127.0.0.1";
        }
    }

    static void EnsureFirewallRule()
    {
        try
        {
            const string ruleName = "PhoneTrackpad Local Network";
            var checkRule = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"if (Get-NetFirewallRule -Name 'PhoneTrackpad_LocalNetwork' -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var checkProcess = Process.Start(checkRule);
            checkProcess?.WaitForExit();

            if (checkProcess?.ExitCode == 0)
            {
                return;
            }

            var createRule = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"Start-Process powershell -Verb RunAs -ArgumentList 'New-NetFirewallRule -Name PhoneTrackpad_LocalNetwork -DisplayName \"{ruleName}\" -Direction Inbound -LocalPort 5000-5100 -Protocol TCP -RemoteAddress LocalSubnet -Profile Domain,Private -Action Allow'",
                UseShellExecute = true,
                CreateNoWindow = true
            };
            Process.Start(createRule);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                $"PhoneTrackpad could not configure its Windows firewall rule. Phones may not be able to connect until local-network access is allowed manually.\n\n{ex.Message}",
                "PhoneTrackpad firewall setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    const string MANIFEST_JSON = @"{
        ""name"": ""PC Trackpad"",
        ""short_name"": ""Trackpad"",
        ""start_url"": ""/"",
        ""scope"": ""/"",
        ""display"": ""standalone"",
        ""background_color"": ""#090a0f"",
        ""theme_color"": ""#090a0f"",
        ""icons"": [
            {
                ""src"": ""/app-icon.svg"",
                ""sizes"": ""192x192 512x512"",
                ""type"": ""image/svg+xml"",
                ""purpose"": ""any maskable""
            }
        ]
    }";

    const string APP_ICON_SVG = """
        <svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
            <rect width="512" height="512" rx="112" fill="#090a0f"/>
            <rect x="96" y="96" width="320" height="320" rx="48" fill="#3b82f6"/>
            <path d="M160 184h192v144H160z" fill="none" stroke="#fff" stroke-width="24" stroke-linejoin="round"/>
            <path d="M256 196v120" stroke="#fff" stroke-width="18" stroke-linecap="round"/>
        </svg>
        """;

    const string SERVICE_WORKER_JS = @"
        const CACHE_NAME = 'phone-trackpad-shell-v3';
        const APP_SHELL = ['/', '/manifest.json', '/app-icon.svg'];

        self.addEventListener('install', event => {
            event.waitUntil(
                caches.open(CACHE_NAME).then(cache => cache.addAll(APP_SHELL))
            );
            self.skipWaiting();
        });

        self.addEventListener('activate', event => {
            event.waitUntil(
                caches.keys().then(keys => Promise.all(
                    keys.filter(key => key.startsWith('phone-trackpad-shell-') && key !== CACHE_NAME)
                        .map(key => caches.delete(key))
                )).then(() => self.clients.claim())
            );
        });

        self.addEventListener('fetch', event => {
            const request = event.request;
            const url = new URL(request.url);
            if (request.method !== 'GET' || url.origin !== self.location.origin || url.pathname === '/trackpad') {
                return;
            }

            if (request.mode === 'navigate') {
                event.respondWith(
                    fetch(request).catch(() => caches.match('/'))
                );
                return;
            }

            if (url.pathname === '/manifest.json' || url.pathname === '/app-icon.svg') {
                event.respondWith(
                    caches.match(request).then(cached => cached || fetch(request))
                );
            }
        });
    ";

    const string TRACKPAD_HTML = @"
    <!DOCTYPE html>
    <html lang='en'>
    <head>
        <meta charset='UTF-8'>
        <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no, viewport-fit=cover'>
        <meta name='apple-mobile-web-app-capable' content='yes'>
        <meta name='apple-mobile-web-app-status-bar-style' content='black-translucent'>
        <meta name='theme-color' content='#090a0f'>
        <link rel='manifest' href='/manifest.json'>
        <link rel='icon' href='/app-icon.svg' type='image/svg+xml'>
        <link rel='apple-touch-icon' href='/app-icon.svg'>
        <title>PC Trackpad</title>
        <style>
            * { box-sizing: border-box; -webkit-tap-highlight-color: transparent; user-select: none; }
            html, body { margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; overflow: hidden; width: 100vw; height: 100%; height: 100dvh; display: flex; flex-direction: column; transition: background 0.3s, color 0.3s; }

            body.theme-dark { background: linear-gradient(155deg, #0d1420 0%, #090a0f 58%); color: #f8fafc; }
            body.theme-light { background: #f8fafc; color: #0f172a; }
            body.theme-midnight { background: #030712; color: #38bdf8; }

            .top-bar {
                position: fixed; top: 8px; left: 12px; right: 12px; display: flex; justify-content: space-between; align-items: center; z-index: 100; pointer-events: none;
            }
            .bar-group { display: flex; gap: 6px; pointer-events: auto; }
            .ctrl-btn {
                background: rgba(255, 255, 255, 0.06); border: 1px solid rgba(255, 255, 255, 0.12);
                padding: 6px 10px; border-radius: 10px; font-size: 11px; cursor: pointer; backdrop-filter: blur(12px);
                font-weight: 500; transition: all 0.2s ease; box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
            }
            .ctrl-btn:active { transform: scale(0.95); }
            body.theme-dark .ctrl-btn { color: #f8fafc; }
            body.theme-light .ctrl-btn { color: #0f172a; background: rgba(0, 0, 0, 0.04); border-color: rgba(0, 0, 0, 0.1); }
            body.theme-midnight .ctrl-btn { color: #38bdf8; background: rgba(56, 189, 248, 0.08); border-color: rgba(56, 189, 248, 0.25); }
            #trackpad { flex: 1; display: flex; align-items: center; justify-content: center; position: relative; touch-action: none; width: 100%; height: 100%; padding-top: 40px; }
            #trackpad::before { content: ''; position: absolute; inset: 56px 12px 12px; border: 1px solid rgba(255, 255, 255, 0.045); border-radius: 26px; background: linear-gradient(145deg, rgba(255, 255, 255, 0.018), rgba(255, 255, 255, 0.004) 58%); box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.025); pointer-events: none; transition: border-color 0.18s, background 0.18s; }
            #trackpad.dragging::before { border-color: rgba(96, 165, 250, 0.32); background: linear-gradient(145deg, rgba(59, 130, 246, 0.045), rgba(255, 255, 255, 0.004) 62%); }
            #trackpad.edge-glide[data-edge~='left']::before { box-shadow: inset 14px 0 28px rgba(59, 130, 246, 0.2); }
            #trackpad.edge-glide[data-edge~='right']::before { box-shadow: inset -14px 0 28px rgba(59, 130, 246, 0.2); }
            #trackpad.edge-glide[data-edge~='top']::before { background: linear-gradient(180deg, rgba(59, 130, 246, 0.16), transparent 28%); }
            #trackpad.edge-glide[data-edge~='bottom']::before { background: linear-gradient(0deg, rgba(59, 130, 246, 0.16), transparent 28%); }
            body.theme-light #trackpad::before { border-color: rgba(15, 23, 42, 0.07); background: linear-gradient(145deg, rgba(15, 23, 42, 0.025), rgba(15, 23, 42, 0.004) 58%); }
            body.theme-midnight #trackpad::before { border-color: rgba(56, 189, 248, 0.09); background: linear-gradient(145deg, rgba(56, 189, 248, 0.025), rgba(56, 189, 248, 0.004) 58%); }
            .instructions { position: relative; z-index: 1; opacity: 0.55; pointer-events: none; text-align: center; letter-spacing: 0.3px; font-size: 12px; line-height: 1.6; font-weight: 400; padding: 12px 18px; }
            .instructions strong { font-size: 11px; letter-spacing: 1.4px; }

            #virtual-keyboard {
                display: none; position: fixed; bottom: 0; left: 0; width: 100vw; height: 75vh; height: min(75dvh, 430px); max-height: 100dvh;
                background: rgba(13, 17, 23, 0.95); backdrop-filter: blur(20px);
                border-top: 1px solid rgba(255, 255, 255, 0.15); padding: 7px 7px max(7px, env(safe-area-inset-bottom, 7px)); z-index: 200;
                box-shadow: 0 -20px 40px rgba(0, 0, 0, 0.7); flex-direction: column; gap: 3px; overflow: hidden;
            }
            body.theme-light #virtual-keyboard { background: rgba(241, 245, 249, 0.95); border-top-color: rgba(0, 0, 0, 0.1); }
            body.theme-midnight #virtual-keyboard { background: rgba(3, 7, 18, 0.95); border-top-color: rgba(56, 189, 248, 0.25); }

            .kb-row { display: flex; justify-content: center; gap: 4px; width: 100%; flex: 1 1 0; min-height: 0; }
            .kb-key {
                background: rgba(255, 255, 255, 0.08); border: 1px solid rgba(255, 255, 255, 0.15);
                color: inherit; padding: 0 2px; border-radius: 6px; font-size: clamp(9px, 2.8dvh, 12px); line-height: 1.1; font-weight: 600;
                flex: 1; height: 100%; min-height: 0; text-align: center; cursor: pointer; min-width: 0; user-select: none;
                touch-action: none; transition: none;
            }
            body.theme-light .kb-key { background: rgba(0, 0, 0, 0.06); border-color: rgba(0, 0, 0, 0.12); }
            body.theme-midnight .kb-key { background: rgba(56, 189, 248, 0.1); border-color: rgba(56, 189, 248, 0.25); }

            .kb-key:active, .kb-key.active { background: rgba(59, 130, 246, 0.18) !important; color: inherit !important; border-color: rgba(96, 165, 250, 0.42) !important; }
            .kb-key.latched { background: rgba(14, 116, 144, 0.42) !important; border-color: #67e8f9 !important; box-shadow: inset 0 0 0 1px rgba(103, 232, 249, 0.24); }
            .kb-status { align-self: flex-end; display: flex; flex: 0 0 16px; gap: 4px; padding: 0 2px; min-height: 16px; }
            .kb-indicator { display: inline-flex; align-items: center; gap: 4px; padding: 1px 5px; border: 1px solid rgba(255,255,255,0.12); border-radius: 6px; color: inherit; opacity: 0.55; font-size: 8px; line-height: 12px; font-weight: 700; letter-spacing: 0.5px; }
            .kb-indicator::before { content: ''; width: 5px; height: 5px; border-radius: 50%; background: currentColor; opacity: 0.35; }
            .kb-indicator.on { opacity: 1; color: #67e8f9; border-color: rgba(103,232,249,0.55); background: rgba(14,116,144,0.18); }
            .kb-indicator.on::before { opacity: 1; box-shadow: 0 0 8px currentColor; }

            .kb-key.wide { flex: 1.4; }
            .kb-key.extrawide { flex: 1.8; }
            .kb-key.space { flex: 6; }
        </style>
        <script src='https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js'></script>
    </head>
    <body class='theme-dark'>

    <div class='top-bar'>
        <div class='bar-group'>
            <button class='ctrl-btn' onclick='sendMedia(""prev"")'>⏮</button>
            <button class='ctrl-btn' onclick='sendMedia(""play_pause"")'>⏯ Play</button>
            <button class='ctrl-btn' onclick='sendMedia(""next"")'>⏭</button>
            <button class='ctrl-btn' onclick='sendMedia(""vol_down"")'>🔉</button>
            <button class='ctrl-btn' onclick='sendMedia(""vol_up"")'>🔊</button>
            <button class='ctrl-btn' onclick='sendMedia(""mute"")'>🔇</button>
        </div>
        <div class='bar-group'>
            <button id='theme-toggle' class='ctrl-btn'>🌙 Theme</button>
            <button id='kb-toggle' class='ctrl-btn'>⌨️ Keyboard</button>
        </div>
    </div>

    <div id='trackpad'>
        <div class='instructions'>
            <strong>WIRELESS TRACKPAD</strong><br>
            <span id='gesture-hint'>Move cursor • Tap to click • Hold, then push toward an edge to glide • 2-finger scroll</span>
        </div>
    </div>

    <div id='virtual-keyboard'>
        <div class='kb-status' aria-live='polite'>
            <span id='caps-indicator' class='kb-indicator' aria-label='Caps Lock off'>CAPS OFF</span>
            <span id='shift-indicator' class='kb-indicator' aria-label='Shift not held'>SHIFT</span>
        </div>
        <div class='kb-row'>
            <button class='kb-key' data-vk='27'>Esc</button>
            <button class='kb-key' data-vk='112'>F1</button>
            <button class='kb-key' data-vk='113'>F2</button>
            <button class='kb-key' data-vk='114'>F3</button>
            <button class='kb-key' data-vk='115'>F4</button>
            <button class='kb-key' data-vk='116'>F5</button>
            <button class='kb-key' data-vk='117'>F6</button>
            <button class='kb-key' data-vk='118'>F7</button>
            <button class='kb-key' data-vk='119'>F8</button>
            <button class='kb-key' data-vk='120'>F9</button>
            <button class='kb-key' data-vk='121'>F10</button>
            <button class='kb-key' data-vk='122'>F11</button>
            <button class='kb-key' data-vk='123'>F12</button>
            <button class='kb-key wide' data-vk='8'>Bksp</button>
        </div>
        <div class='kb-row'>
            <button class='kb-key' data-vk='192' data-shift='~'>`</button>
            <button class='kb-key' data-vk='49' data-shift='!'>1</button>
            <button class='kb-key' data-vk='50' data-shift='@'>2</button>
            <button class='kb-key' data-vk='51' data-shift='#'>3</button>
            <button class='kb-key' data-vk='52' data-shift='$'>4</button>
            <button class='kb-key' data-vk='53' data-shift='%'>5</button>
            <button class='kb-key' data-vk='54' data-shift='^'>6</button>
            <button class='kb-key' data-vk='55' data-shift='&amp;'>7</button>
            <button class='kb-key' data-vk='56' data-shift='*'>8</button>
            <button class='kb-key' data-vk='57' data-shift='('>9</button>
            <button class='kb-key' data-vk='48' data-shift=')'>0</button>
            <button class='kb-key' data-vk='189' data-shift='_'>-</button>
            <button class='kb-key' data-vk='187' data-shift='+'>=</button>
            <button class='kb-key wide' data-vk='46'>Del</button>
        </div>
        <div class='kb-row'>
            <button class='kb-key wide' data-vk='9'>Tab</button>
            <button class='kb-key' data-vk='81'>Q</button>
            <button class='kb-key' data-vk='87'>W</button>
            <button class='kb-key' data-vk='69'>E</button>
            <button class='kb-key' data-vk='82'>R</button>
            <button class='kb-key' data-vk='84'>T</button>
            <button class='kb-key' data-vk='89'>Y</button>
            <button class='kb-key' data-vk='85'>U</button>
            <button class='kb-key' data-vk='73'>I</button>
            <button class='kb-key' data-vk='79'>O</button>
            <button class='kb-key' data-vk='80'>P</button>
            <button class='kb-key' data-vk='219' data-shift='{'>[</button>
            <button class='kb-key' data-vk='221' data-shift='}'>]</button>
            <button class='kb-key' data-vk='220' data-shift='|'>\</button>
        </div>
        <div class='kb-row'>
            <button class='kb-key wide' data-vk='20'>Caps</button>
            <button class='kb-key' data-vk='65'>A</button>
            <button class='kb-key' data-vk='83'>S</button>
            <button class='kb-key' data-vk='68'>D</button>
            <button class='kb-key' data-vk='70'>F</button>
            <button class='kb-key' data-vk='71'>G</button>
            <button class='kb-key' data-vk='72'>H</button>
            <button class='kb-key' data-vk='74'>J</button>
            <button class='kb-key' data-vk='75'>K</button>
            <button class='kb-key' data-vk='76'>L</button>
            <button class='kb-key' data-vk='186' data-shift=':'>;</button>
            <button class='kb-key' data-vk='222' data-shift='&quot;'>'</button>
            <button class='kb-key extrawide' data-vk='13'>Enter</button>
        </div>
        <div class='kb-row'>
            <button class='kb-key extrawide' data-vk='16'>Shift</button>
            <button class='kb-key' data-vk='90'>Z</button>
            <button class='kb-key' data-vk='88'>X</button>
            <button class='kb-key' data-vk='67'>C</button>
            <button class='kb-key' data-vk='86'>V</button>
            <button class='kb-key' data-vk='66'>B</button>
            <button class='kb-key' data-vk='78'>N</button>
            <button class='kb-key' data-vk='77'>M</button>
            <button class='kb-key' data-vk='188' data-shift='&lt;'>,</button>
            <button class='kb-key' data-vk='190' data-shift='&gt;'>.</button>
            <button class='kb-key' data-vk='191' data-shift='?'>/</button>
            <button class='kb-key' data-vk='38'>▲</button>
            <button class='kb-key wide' data-vk='16'>Shift</button>
        </div>
        <div class='kb-row'>
            <button class='kb-key wide' data-vk='17'>Ctrl</button>
            <button class='kb-key wide' data-vk='91'>Win</button>
            <button class='kb-key wide' data-vk='18'>Alt</button>
            <button class='kb-key space' data-vk='32'>Space</button>
            <button class='kb-key wide' data-vk='18'>Alt</button>
            <button class='kb-key' data-vk='37'>◀</button>
            <button class='kb-key' data-vk='40'>▼</button>
            <button class='kb-key' data-vk='39'>▶</button>
        </div>
    </div>

    <script>
        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.register('/sw.js')
                .catch(err => console.error('Service worker registration failed:', err));
        }

        const connection = new signalR.HubConnectionBuilder().withUrl('/trackpad').build();

        const trackpad = document.getElementById('trackpad');
        const themeToggle = document.getElementById('theme-toggle');
        const kbToggle = document.getElementById('kb-toggle');
        const virtualKeyboard = document.getElementById('virtual-keyboard');
        const gestureHint = document.getElementById('gesture-hint');
        const capsIndicator = document.getElementById('caps-indicator');
        const shiftIndicator = document.getElementById('shift-indicator');

        function haptic(duration = 12) {
            if (typeof navigator.vibrate === 'function') navigator.vibrate(duration);
        }

        function sendMedia(action) {
            if (connection.state === 'Connected') {
                connection.invoke('MediaControl', action);
                haptic(16);
            }
        }

        const themes = ['theme-dark', 'theme-light', 'theme-midnight'];
        const themeLabels = ['🌙 Theme', '☀️ Theme', '🌌 Theme'];
        let currentThemeIdx = 0;

        const savedTheme = localStorage.getItem('trackpad_theme');
        if (savedTheme) {
            currentThemeIdx = themes.indexOf(savedTheme);
            if (currentThemeIdx === -1) currentThemeIdx = 0;
        }
        document.body.className = themes[currentThemeIdx];

        themeToggle.addEventListener('click', () => {
            currentThemeIdx = (currentThemeIdx + 1) % themes.length;
            const newTheme = themes[currentThemeIdx];
            document.body.className = newTheme;
            localStorage.setItem('trackpad_theme', newTheme);
        });

        let kbOpen = false;
        let capsLockPollTimer = null;
        let capsLockOn = false;

        function updateCapsLockIndicator(isOn) {
            capsLockOn = isOn;
            capsIndicator.textContent = isOn ? 'CAPS ON' : 'CAPS OFF';
            capsIndicator.classList.toggle('on', isOn);
            capsIndicator.setAttribute('aria-label', `Caps Lock ${isOn ? 'on' : 'off'}`);
            virtualKeyboard.querySelectorAll('.kb-key').forEach(key => {
                if (Number(key.dataset.vk) !== 20) return;
                key.classList.toggle('latched', isOn);
                key.setAttribute('aria-pressed', String(isOn));
            });
            updateKeyboardLabels();
        }

        async function syncCapsLockState() {
            if (connection.state !== 'Connected') return;
            try {
                updateCapsLockIndicator(await connection.invoke('GetCapsLockState'));
            } catch (error) {
                console.error('Unable to read Caps Lock state:', error);
            }
        }

        function startCapsLockPolling() {
            if (capsLockPollTimer !== null) clearInterval(capsLockPollTimer);
            syncCapsLockState();
            capsLockPollTimer = setInterval(syncCapsLockState, 500);
        }

        connection.onreconnected(startCapsLockPolling);
        connection.onclose(() => {
            clearInterval(capsLockPollTimer);
            capsLockPollTimer = null;
        });
        connection.start()
            .then(startCapsLockPolling)
            .catch(error => console.error('SignalR connection failed:', error));

        kbToggle.addEventListener('click', () => {
            kbOpen = !kbOpen;
            if (kbOpen) {
                releaseMouseButton();
                touchDragActive = false;
                trackpad.classList.remove('dragging');
                gestureHint.textContent = 'Slide across keys to type • modifier keys stay held until you lift';
                syncCapsLockState();
            } else {
                releaseAllKeyboardPointers();
                gestureHint.textContent = 'Move cursor • Tap to click • Hold, then push toward an edge to glide • 2-finger scroll';
            }
            virtualKeyboard.style.display = kbOpen ? 'flex' : 'none';
            kbToggle.style.background = kbOpen ? '#3b82f6' : '';
            kbToggle.style.color = kbOpen ? '#fff' : '';
            kbToggle.style.borderColor = kbOpen ? '#2563eb' : '';
        });

        const modifierKeys = new Set([16, 17, 18, 91]);
        const keyboardPointers = new Map();
        const keyboardKeyPointerCounts = new Map();
        const heldModifierKeyCounts = new Map();
        const heldModifierCounts = new Map();
        const heldKeyCounts = new Map();
        const allKeyboardKeys = Array.from(virtualKeyboard.querySelectorAll('.kb-key'));
        const shiftedKeys = allKeyboardKeys.filter(key => key.hasAttribute('data-shift'));
        for (const key of allKeyboardKeys) key.dataset.baseLabel = key.textContent;

        function updateShiftedKeyLabels() {
            const shiftIsHeld = (heldModifierCounts.get(16) || 0) > 0;
            document.body.classList.toggle('shift-active', shiftIsHeld);
            shiftIndicator.classList.toggle('on', shiftIsHeld);
            shiftIndicator.setAttribute('aria-label', shiftIsHeld ? 'Shift held' : 'Shift not held');
            updateKeyboardLabels();
        }

        function updateKeyboardLabels() {
            const shiftIsHeld = (heldModifierCounts.get(16) || 0) > 0;
            for (const key of shiftedKeys) {
                key.textContent = shiftIsHeld ? key.dataset.shift : key.dataset.baseLabel;
            }
            virtualKeyboard.querySelectorAll('.kb-key[data-vk]').forEach(key => {
                const vk = Number(key.dataset.vk);
                if (vk >= 65 && vk <= 90) {
                    key.textContent = (capsLockOn !== shiftIsHeld)
                        ? key.dataset.baseLabel.toUpperCase()
                        : key.dataset.baseLabel.toLowerCase();
                }
            });
        }

        function updateKeyboardKeyActive(key) {
            if ((keyboardKeyPointerCounts.get(key) || 0) > 0 ||
                (heldModifierKeyCounts.get(key) || 0) > 0) {
                key.classList.add('active');
            } else {
                key.classList.remove('active');
            }
        }

        function setKeyboardKeyPointerActive(key, active) {
            const count = (keyboardKeyPointerCounts.get(key) || 0) + (active ? 1 : -1);
            if (count > 0) keyboardKeyPointerCounts.set(key, count);
            else keyboardKeyPointerCounts.delete(key);
            updateKeyboardKeyActive(key);
        }

        function setHeldModifierKeyActive(key, active) {
            const count = (heldModifierKeyCounts.get(key) || 0) + (active ? 1 : -1);
            if (count > 0) heldModifierKeyCounts.set(key, count);
            else heldModifierKeyCounts.delete(key);
            updateKeyboardKeyActive(key);
        }

        function pressRegularKey(vk) {
            const heldKey = heldKeyCounts.get(vk);
            if (heldKey) {
                heldKey.count++;
                return;
            }

            const keyState = { count: 1, repeatTimeout: null, repeatInterval: null };
            heldKeyCounts.set(vk, keyState);
            if (connection.state === 'Connected') connection.invoke('PressKey', vk);
            haptic(12);
            keyState.repeatTimeout = setTimeout(() => {
                if (heldKeyCounts.get(vk) !== keyState) return;
                keyState.repeatInterval = setInterval(() => {
                    if (connection.state === 'Connected') connection.invoke('PressKey', vk);
                }, 65);
            }, 450);
        }

        function releaseRegularKey(vk) {
            const keyState = heldKeyCounts.get(vk);
            if (!keyState) return;
            keyState.count--;
            if (keyState.count > 0) return;

            clearTimeout(keyState.repeatTimeout);
            if (keyState.repeatInterval !== null) clearInterval(keyState.repeatInterval);
            heldKeyCounts.delete(vk);
        }

        function releaseCurrentKeyboardKey(state) {
            if (!state.currentKey) return;
            setKeyboardKeyPointerActive(state.currentKey, false);
            if (state.currentVk === 32) {
                clearTimeout(state.spaceTimer);
                if (!state.spaceCursorMode && connection.state === 'Connected') {
                    connection.invoke('PressKey', 32);
                    haptic(12);
                }
            } else if (!modifierKeys.has(state.currentVk)) {
                releaseRegularKey(state.currentVk);
            }
        }

        function moveTextCursor(state, x) {
            if (!state.spaceCursorMode && Math.abs(x - state.spacePointerX) > 10) {
                state.spaceCursorMode = true;
                clearTimeout(state.spaceTimer);
                haptic([18, 24, 18]);
            }
            if (!state.spaceCursorMode) return;

            state.spaceRemainder += x - state.lastPointerX;
            state.lastPointerX = x;
            let steps = 0;
            while (Math.abs(state.spaceRemainder) >= 14 && steps < 20) {
                const direction = state.spaceRemainder > 0 ? 39 : 37;
                if (connection.state === 'Connected') connection.invoke('PressKey', direction);
                state.spaceRemainder += state.spaceRemainder > 0 ? -14 : 14;
                steps++;
            }
        }

        function pressKeyboardKey(state, key) {
            if (state.currentKey === key) return;
            releaseCurrentKeyboardKey(state);

            state.currentKey = key;
            state.currentVk = null;
            if (!key) return;

            setKeyboardKeyPointerActive(key, true);
            const vk = Number(key.dataset.vk);
            state.currentVk = vk;
            if (modifierKeys.has(vk)) {
                if (!state.heldModifiers.has(vk)) {
                    state.heldModifiers.add(vk);
                    state.heldModifierElements.set(vk, key);
                    setHeldModifierKeyActive(key, true);
                    const count = heldModifierCounts.get(vk) || 0;
                    heldModifierCounts.set(vk, count + 1);
                    if (vk === 16) updateShiftedKeyLabels();
                    if (count === 0 && connection.state === 'Connected') {
                        connection.invoke('KeyDown', vk);
                    }
                    haptic(20);
                }
            } else if (vk === 32) {
                state.spacePointerX = state.lastPointerX;
                state.spaceCursorMode = false;
                state.spaceRemainder = 0;
                state.spaceTimer = setTimeout(() => {
                    if (state.currentVk === 32 && !state.spaceCursorMode) {
                        state.spaceCursorMode = true;
                        haptic([18, 24, 18]);
                    }
                }, 450);
            } else if (vk === 20) {
                if (connection.state === 'Connected') connection.invoke('PressKey', vk);
                haptic(18);
                setTimeout(syncCapsLockState, 80);
            } else {
                pressRegularKey(vk);
            }
        }

        function releaseKeyboardPointer(pointerId) {
            const state = keyboardPointers.get(pointerId);
            if (!state) return;

            keyboardPointers.delete(pointerId);
            releaseCurrentKeyboardKey(state);

            for (const vk of state.heldModifiers) {
                setHeldModifierKeyActive(state.heldModifierElements.get(vk), false);
                const count = (heldModifierCounts.get(vk) || 1) - 1;
                if (count > 0) {
                    heldModifierCounts.set(vk, count);
                } else {
                    heldModifierCounts.delete(vk);
                    if (connection.state === 'Connected') connection.invoke('KeyUp', vk);
                }
                if (vk === 16) updateShiftedKeyLabels();
            }
        }

        virtualKeyboard.addEventListener('pointerdown', e => {
            const key = e.target instanceof Element ? e.target.closest('.kb-key') : null;
            if (!key) return;
            e.preventDefault();

            const state = {
                currentKey: null,
                currentVk: null,
                heldModifiers: new Set(),
                heldModifierElements: new Map(),
                lastPointerX: e.clientX,
                spacePointerX: e.clientX,
                spaceCursorMode: false,
                spaceRemainder: 0,
                spaceTimer: null
            };
            keyboardPointers.set(e.pointerId, state);
            virtualKeyboard.setPointerCapture(e.pointerId);
            pressKeyboardKey(state, key);
        });

        virtualKeyboard.addEventListener('pointermove', e => {
            const state = keyboardPointers.get(e.pointerId);
            if (!state) return;

            if (state.currentVk === 32) {
                moveTextCursor(state, e.clientX);
                e.preventDefault();
                return;
            }

            const element = document.elementFromPoint(e.clientX, e.clientY);
            const key = element instanceof Element ? element.closest('.kb-key') : null;
            pressKeyboardKey(state, key && virtualKeyboard.contains(key) ? key : null);
            state.lastPointerX = e.clientX;
            e.preventDefault();
        });

        virtualKeyboard.addEventListener('pointerup', e => {
            releaseKeyboardPointer(e.pointerId);
            e.preventDefault();
        });

        virtualKeyboard.addEventListener('pointercancel', e => {
            releaseKeyboardPointer(e.pointerId);
        });

        virtualKeyboard.addEventListener('lostpointercapture', e => {
            releaseKeyboardPointer(e.pointerId);
        });

        virtualKeyboard.addEventListener('click', e => {
            if (e.target instanceof Element && e.target.closest('.kb-key')) e.preventDefault();
        });

        function releaseAllKeyboardPointers() {
            for (const pointerId of keyboardPointers.keys()) {
                releaseKeyboardPointer(pointerId);
            }
        }

        let lastX = 0, lastY = 0, startX = 0, startY = 0, touchStart = 0, lastMoveAt = 0;
        let lastDist = 0, maxFingers = 0, isTap = true;
        let isMouseDown = false;
        let touchDragActive = false;
        let holdTimeout = null;
        let pendingMoveX = 0, pendingMoveY = 0, moveFramePending = false;
        let edgePointerX = 0, edgePointerY = 0, edgeFrame = 0, edgeLastFrameAt = 0;
        let lastEdgeDirection = '';

        function getEdgeGlideVelocity(position, start, length, edgeSize, maxSpeed) {
            const fromStart = Math.max(0, Math.min(1, (start + edgeSize - position) / edgeSize));
            const fromEnd = Math.max(0, Math.min(1, (position - (start + length - edgeSize)) / edgeSize));
            return (fromEnd * fromEnd - fromStart * fromStart) * maxSpeed;
        }

        function getEdgeDirections(x, y, rect, edgeSizeX, edgeSizeY) {
            const directions = [];
            if (x < rect.left + edgeSizeX) directions.push('left');
            else if (x > rect.right - edgeSizeX) directions.push('right');
            if (y < rect.top + edgeSizeY) directions.push('top');
            else if (y > rect.bottom - edgeSizeY) directions.push('bottom');
            return directions;
        }

        function glideFromEdges(frameTime) {
            if (!isMouseDown) {
                edgeFrame = 0;
                lastEdgeDirection = '';
                trackpad.classList.remove('edge-glide');
                trackpad.removeAttribute('data-edge');
                return;
            }

            const bounds = trackpad.getBoundingClientRect();
            const rect = {
                left: bounds.left + 12,
                top: bounds.top + 56,
                width: Math.max(1, bounds.width - 24),
                height: Math.max(1, bounds.height - 68)
            };
            rect.right = rect.left + rect.width;
            rect.bottom = rect.top + rect.height;
            const edgeSizeX = Math.min(rect.width * 0.16, 110);
            const edgeSizeY = Math.min(rect.height * 0.2, 90);
            const velocityX = getEdgeGlideVelocity(edgePointerX, rect.left, rect.width, edgeSizeX, 1000);
            const velocityY = getEdgeGlideVelocity(edgePointerY, rect.top, rect.height, edgeSizeY, 850);
            const directions = getEdgeDirections(edgePointerX, edgePointerY, rect, edgeSizeX, edgeSizeY);
            const edgeDirection = directions.join(' ');
            trackpad.classList.toggle('edge-glide', directions.length > 0);
            if (directions.length > 0) trackpad.dataset.edge = edgeDirection;
            else trackpad.removeAttribute('data-edge');
            if (edgeDirection && edgeDirection !== lastEdgeDirection) haptic(8);
            lastEdgeDirection = edgeDirection;
            const elapsed = edgeLastFrameAt ? Math.min((frameTime - edgeLastFrameAt) / 1000, 0.05) : 0;
            edgeLastFrameAt = frameTime;

            if (elapsed > 0 && (velocityX !== 0 || velocityY !== 0) && connection.state === 'Connected') {
                connection.invoke('Move', velocityX * elapsed, velocityY * elapsed);
            }

            edgeFrame = requestAnimationFrame(glideFromEdges);
        }

        function updateEdgePointer(x, y) {
            edgePointerX = x;
            edgePointerY = y;
        }

        function pressMouseButton() {
            if (isMouseDown || connection.state !== 'Connected') return;
            isMouseDown = true;
            connection.invoke('MouseDown');
            trackpad.classList.add('dragging');
            gestureHint.textContent = 'Dragging • Push toward an edge to glide; farther out moves faster';
            haptic([24, 35, 18]);
            edgeLastFrameAt = 0;
            edgeFrame = requestAnimationFrame(glideFromEdges);
        }

        function releaseMouseButton() {
            if (!isMouseDown) return;
            isMouseDown = false;
            if (connection.state === 'Connected') connection.invoke('MouseUp');
            trackpad.classList.remove('dragging');
            gestureHint.textContent = kbOpen
                ? 'Slide across keys to type • modifier keys stay held until you lift'
                : 'Move cursor • Tap to click • Hold, then push toward an edge to glide • 2-finger scroll';
            haptic(12);
        }

        trackpad.addEventListener('touchstart', e => {
            if (kbOpen) return;
            e.preventDefault();
            maxFingers = Math.max(maxFingers, e.touches.length);
            if (e.touches.length === 1) {
                lastX = e.touches[0].clientX;
                lastY = e.touches[0].clientY;
                startX = lastX; startY = lastY; isTap = true; touchStart = Date.now(); lastMoveAt = performance.now();
                updateEdgePointer(lastX, lastY);
                holdTimeout = setTimeout(() => {
                    if (isTap && maxFingers === 1 && connection.state === 'Connected') {
                        touchDragActive = true;
                        pressMouseButton();
                    }
                    holdTimeout = null;
                }, 200);
            } else if (e.touches.length === 2) {
                lastY = e.touches[0].clientY;
            } else if (e.touches.length === 3) {
                lastDist = Math.hypot(e.touches[0].clientX - e.touches[2].clientX, e.touches[0].clientY - e.touches[2].clientY);
            }
        }, { passive: false });

        trackpad.addEventListener('touchmove', e => {
            if (kbOpen || connection.state !== 'Connected') return;
            e.preventDefault();
            if (e.touches.length === 1 && maxFingers === 1) {
                const currentX = e.touches[0].clientX, currentY = e.touches[0].clientY;
                updateEdgePointer(currentX, currentY);
                if (Math.abs(currentX - startX) > 8 || Math.abs(currentY - startY) > 8) {
                    isTap = false;
                    if (holdTimeout) {
                        clearTimeout(holdTimeout);
                        holdTimeout = null;
                    }
                }
                const deltaX = currentX - lastX, deltaY = currentY - lastY;
                const now = performance.now();
                const elapsed = Math.max(8, now - lastMoveAt);
                const speed = Math.hypot(deltaX, deltaY) / elapsed;
                const acceleration = Math.min(2.4, 1 + speed * 0.8);
                pendingMoveX += deltaX * acceleration;
                pendingMoveY += deltaY * acceleration;
                lastX = currentX; lastY = currentY;
                lastMoveAt = now;
                if (!moveFramePending) {
                    moveFramePending = true;
                    requestAnimationFrame(() => {
                        const moveX = pendingMoveX, moveY = pendingMoveY;
                        pendingMoveX = 0; pendingMoveY = 0; moveFramePending = false;
                        if ((moveX !== 0 || moveY !== 0) && connection.state === 'Connected') {
                            connection.invoke('Move', moveX, moveY);
                        }
                    });
                }
            } else if (e.touches.length === 2) {
                if (holdTimeout) {
                    clearTimeout(holdTimeout);
                    holdTimeout = null;
                }
                isTap = false;
                const deltaY = e.touches[0].clientY - lastY;
                if (Math.abs(deltaY) > 2) {
                    requestAnimationFrame(() => {
                        connection.invoke('Scroll', Math.round(deltaY));
                        lastY = e.touches[0].clientY;
                    });
                }
            } else if (e.touches.length === 3) {
                if (holdTimeout) {
                    clearTimeout(holdTimeout);
                    holdTimeout = null;
                }
                isTap = false;
                const currentDist = Math.hypot(e.touches[0].clientX - e.touches[2].clientX, e.touches[0].clientY - e.touches[2].clientY);
                const deltaDist = currentDist - lastDist;
                if (Math.abs(deltaDist) > 35) {
                    requestAnimationFrame(() => {
                        connection.invoke('Zoom', deltaDist > 0 ? 1 : -1);
                        lastDist = currentDist;
                    });
                }
            }
        }, { passive: false });

        trackpad.addEventListener('touchend', e => {
            if (kbOpen) return;
            e.preventDefault();
            if (holdTimeout) {
                clearTimeout(holdTimeout);
                holdTimeout = null;
            }

            const wasMouseDown = isMouseDown;
            if (touchDragActive && e.touches.length === 0) {
                touchDragActive = false;
                releaseMouseButton();
            }

            if (e.touches.length === 0) {
                if (isTap && !wasMouseDown && Date.now() - touchStart < 350 && connection.state === 'Connected') {
                    if (maxFingers === 1) { haptic(12); connection.invoke('Click', 'left'); }
                    else if (maxFingers === 2) { haptic([14, 30, 14]); connection.invoke('Click', 'right'); }
                }
                maxFingers = 0;
            }
        }, { passive: false });

        trackpad.addEventListener('touchcancel', () => {
            if (holdTimeout) {
                clearTimeout(holdTimeout);
                holdTimeout = null;
            }
            if (isMouseDown) releaseMouseButton();
            touchDragActive = false;
            maxFingers = 0;
            trackpad.classList.remove('dragging');
        });
    </script>
    </body>
    </html>";
}
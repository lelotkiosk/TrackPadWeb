using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

builder.Services.AddSignalR();

int port = 5000;
while (IsPortInUse(port))
{
    port++;
}

var app = builder.Build();

EnsureFirewallRule(port);

app.MapHub<TrackpadHub>("/trackpad");
app.MapGet("/", () => Results.Content(TRACKPAD_HTML, "text/html"));

string localIp = GetLocalIpAddress();
string phoneUrl = $"http://{localIp}:{port}";

Console.Clear();
Console.WriteLine("========================================================");
Console.WriteLine("                 PC TRACKPAD IS READY                 ");
Console.WriteLine("========================================================");
Console.WriteLine($"\n   Open this URL on your phone's browser:");
Console.WriteLine($"     {phoneUrl}\n");
Console.WriteLine("--------------------------------------------------------");
Console.WriteLine("  Keep this window open while using your phone.");
Console.WriteLine("  Press Ctrl+C to stop the server.\n");

app.Run($"http://0.0.0.0:{port}");


bool IsPortInUse(int p)
{
    var activeTcpListeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
    foreach (var listenerEndpoint in activeTcpListeners)
    {
        if (listenerEndpoint.Port == p) return true;
    }
    return false;
}

string GetLocalIpAddress()
{
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
    try
    {
        socket.Connect("8.8.8.8", 65530);
        return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
    }
    catch
    {
        return "127.0.0.1";
    }
}

void EnsureFirewallRule(int p)
{
    try
    {
        string ruleName = $"PhoneTrackpad_Port_{p}";
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var proc = Process.Start(psi);
        proc?.WaitForExit();

        if (proc?.ExitCode != 0)
        {
            var createPsi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"Start-Process powershell -Verb RunAs -ArgumentList 'New-NetFirewallRule -DisplayName \"{ruleName}\" -Direction Inbound -LocalPort {p} -Protocol TCP -Action Allow'",
                UseShellExecute = true,
                CreateNoWindow = false
            };
            var createProc = Process.Start(createPsi);
            createProc?.WaitForExit();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Warning] Firewall check: {ex.Message}");
    }
}


public class TrackpadHub : Hub
{
    [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
    [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_WHEEL = 0x0800;
    const byte VK_CONTROL = 0x11;
    const uint KEYEVENTF_KEYUP = 0x0002;

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
        keybd_event(VK_CONTROL, 0, 0, 0); 
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, direction * 120, 0);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0); 
    }
}

public partial class Program
{
    const string TRACKPAD_HTML = @"
    <!DOCTYPE html>
    <html lang='en'>
    <head>
        <meta charset='UTF-8'>
        <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no, viewport-fit=cover'>
        <meta name='apple-mobile-web-app-capable' content='yes'>
        <title>PC Trackpad</title>
        <style>
            * { box-sizing: border-box; -webkit-tap-highlight-color: transparent; user-select: none; }
            body { margin: 0; padding: 0; background: #0a0a0a; color: #fff; font-family: sans-serif; touch-action: none; overflow: hidden; }
            #trackpad { width: 100vw; height: 100vh; display: flex; align-items: center; justify-content: center; position: relative; }
            .instructions { opacity: 0.2; pointer-events: none; text-align: center; letter-spacing: 1px; font-size: 14px; line-height: 1.8; }
        </style>
        <script src='https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js'></script>
    </head>
    <body>
    <div id='trackpad'>
        <div class='instructions'>
            <strong>DRAG</strong> to move<br>
            <strong>1-FINGER HOLD & DRAG</strong> to highlight text<br>
            <strong>1-FINGER TAP</strong> to left-click<br>
            <strong>2-FINGER TAP</strong> to right-click<br>
            <strong>2-FINGER DRAG</strong> to scroll<br>
            <strong>3-FINGER PINCH</strong> to zoom
        </div>
    </div>
    <script>
        const connection = new signalR.HubConnectionBuilder().withUrl('/trackpad').build();
        connection.start().catch(err => console.error(err));
        const trackpad = document.getElementById('trackpad');
        let lastX = 0, lastY = 0, startX = 0, startY = 0, touchStart = 0;
        let lastDist = 0, maxFingers = 0, isTap = true;
        let isMouseDown = false;
        let holdTimeout = null;

        function vibrate(ms) {
            if (navigator.vibrate) {
                navigator.vibrate(ms);
            }
        }

        document.addEventListener('touchmove', e => e.preventDefault(), { passive: false });

        function updateCoordinates(touches) {
            if (touches.length === 1) { lastX = touches[0].clientX; lastY = touches[0].clientY; }
            else if (touches.length === 2) { lastY = touches[0].clientY; }
            else if (touches.length === 3) { lastDist = Math.hypot(touches[0].clientX - touches[2].clientX, touches[0].clientY - touches[2].clientY); }
        }

        trackpad.addEventListener('touchstart', e => {
            maxFingers = Math.max(maxFingers, e.touches.length);
            updateCoordinates(e.touches);
            if (e.touches.length === 1) { 
                startX = lastX; 
                startY = lastY; 
                isTap = true; 
                touchStart = Date.now();

                holdTimeout = setTimeout(() => {
                    if (isTap && maxFingers === 1 && connection.state === 'Connected') {
                        isMouseDown = true;
                        vibrate(40); // Stronger haptic feedback when drag-selecting locks in
                        connection.invoke('MouseDown');
                    }
                }, 250);
            }
        }, { passive: false });

        trackpad.addEventListener('touchmove', e => {
            if (connection.state !== 'Connected') return;
            if (e.touches.length === 1 && maxFingers === 1) {
                const currentX = e.touches[0].clientX, currentY = e.touches[0].clientY;
                if (Math.abs(currentX - startX) > 5 || Math.abs(currentY - startY) > 5) {
                    isTap = false;
                    if (holdTimeout) clearTimeout(holdTimeout);
                }
                requestAnimationFrame(() => {
                    connection.invoke('Move', currentX - lastX, currentY - lastY);
                    lastX = currentX; lastY = currentY;
                });
            } else if (e.touches.length === 2) {
                if (holdTimeout) clearTimeout(holdTimeout);
                isTap = false; 
                const deltaY = e.touches[0].clientY - lastY;
                if (Math.abs(deltaY) > 2) {
                    requestAnimationFrame(() => {
                        connection.invoke('Scroll', Math.round(deltaY));
                        lastY = e.touches[0].clientY;
                    });
                }
            } else if (e.touches.length === 3) {
                if (holdTimeout) clearTimeout(holdTimeout);
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
            if (holdTimeout) clearTimeout(holdTimeout);
            updateCoordinates(e.touches);

            if (isMouseDown && e.touches.length === 0) {
                vibrate(20);
                connection.invoke('MouseUp');
                isMouseDown = false;
            }

            if (e.touches.length === 0) {
                if (isTap && !isMouseDown && Date.now() - touchStart < 350 && connection.state === 'Connected') {
                    if (maxFingers === 1) {
                        vibrate(15); // Short snappy vibration for left click tap
                        connection.invoke('Click', 'left');
                    } else if (maxFingers === 2) {
                        vibrate([15, 30, 15]); // Double vibration pulse for right click tap
                        connection.invoke('Click', 'right');
                    }
                }
                maxFingers = 0; 
                isMouseDown = false;
            }
        });
    </script>
    </body>
    </html>";
}
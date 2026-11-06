using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Forms;

sealed class PhoneTrackpadTrayContext : ApplicationContext
{
    readonly WebApplication app;
    readonly string phoneUrl;
    readonly NotifyIcon trayIcon;
    readonly ToolStripMenuItem statusItem;
    readonly ToolStripMenuItem startItem;
    readonly ToolStripMenuItem stopItem;
    bool isRunning = true;

    public PhoneTrackpadTrayContext(WebApplication app, string phoneUrl)
    {
        this.app = app;
        this.phoneUrl = phoneUrl;

        statusItem = new ToolStripMenuItem("Server is running") { Enabled = false };
        startItem = new ToolStripMenuItem("Start server");
        stopItem = new ToolStripMenuItem("Stop server");
        var openItem = new ToolStripMenuItem("Open trackpad in browser");
        var copyItem = new ToolStripMenuItem("Copy phone address");
        var exitItem = new ToolStripMenuItem("Exit PhoneTrackpad");
        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startItem);
        menu.Items.Add(stopItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        startItem.Click += async (_, _) => await StartServerAsync();
        stopItem.Click += async (_, _) => await StopServerAsync();
        openItem.Click += (_, _) => OpenTrackpad();
        copyItem.Click += (_, _) => CopyPhoneAddress();
        exitItem.Click += (_, _) => ExitApplication();

        trayIcon = new NotifyIcon
        {
            Icon = CreateTrackpadIcon(),
            Text = "PhoneTrackpad — Server running",
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenTrackpad();
            }
        };
        UpdateMenu();
        trayIcon.ShowBalloonTip(
            5000,
            "PhoneTrackpad is running",
            $"Your phone can connect at {phoneUrl}. Right-click this tray icon for controls.",
            ToolTipIcon.Info);
    }

    async Task StartServerAsync()
    {
        if (isRunning)
        {
            return;
        }

        startItem.Enabled = false;
        try
        {
            await app.StartAsync();
            isRunning = true;
            trayIcon.ShowBalloonTip(3000, "PhoneTrackpad", "The Wi-Fi server is running.", ToolTipIcon.Info);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or SocketException)
        {
            MessageBox.Show(
                $"PhoneTrackpad could not start its Wi-Fi server.\n\n{ex.Message}",
                "PhoneTrackpad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UpdateMenu();
        }
    }

    async Task StopServerAsync()
    {
        if (!isRunning)
        {
            return;
        }

        stopItem.Enabled = false;
        try
        {
            await app.StopAsync();
            isRunning = false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            MessageBox.Show(
                $"PhoneTrackpad could not stop its Wi-Fi server cleanly.\n\n{ex.Message}",
                "PhoneTrackpad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UpdateMenu();
        }
    }

    void OpenTrackpad()
    {
        if (!isRunning)
        {
            _ = StartServerThenOpenAsync();
            return;
        }

        Process.Start(new ProcessStartInfo(phoneUrl) { UseShellExecute = true });
    }

    async Task StartServerThenOpenAsync()
    {
        await StartServerAsync();
        if (isRunning)
        {
            OpenTrackpad();
        }
    }

    void CopyPhoneAddress()
    {
        try
        {
            Clipboard.SetText(phoneUrl);
            trayIcon.ShowBalloonTip(2000, "PhoneTrackpad", "Phone address copied to the clipboard.", ToolTipIcon.Info);
        }
        catch (ExternalException ex)
        {
            MessageBox.Show(
                $"PhoneTrackpad could not copy the phone address.\n\n{ex.Message}",
                "PhoneTrackpad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    void ExitApplication()
    {
        trayIcon.Visible = false;
        trayIcon.Dispose();
        base.ExitThread();
    }

    void UpdateMenu()
    {
        statusItem.Text = isRunning ? $"Server is running at {new Uri(phoneUrl).Port}" : "Server is stopped";
        startItem.Enabled = !isRunning;
        stopItem.Enabled = isRunning;
        trayIcon.Text = isRunning
            ? "PhoneTrackpad — Server running"
            : "PhoneTrackpad — Server stopped";
    }

    static Icon CreateTrackpadIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var background = new SolidBrush(Color.FromArgb(37, 99, 235)))
        using (var outline = new Pen(Color.White, 2.2f))
        using (var centerLine = new Pen(Color.White, 1.8f))
        using (var path = new GraphicsPath())
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            AddRoundedRectangle(path, new Rectangle(2, 2, 28, 28), 7);
            graphics.FillPath(background, path);
            using var trackpadPath = new GraphicsPath();
            AddRoundedRectangle(trackpadPath, new Rectangle(9, 8, 14, 17), 3);
            graphics.DrawPath(outline, trackpadPath);
            graphics.DrawLine(centerLine, 16, 10, 16, 22);
        }

        IntPtr iconHandle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(iconHandle).Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    static void AddRoundedRectangle(GraphicsPath path, Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr handle);
}

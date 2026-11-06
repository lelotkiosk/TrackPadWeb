using System.Security;

namespace PhoneTrackpad.Mobile;

public partial class MainPage : ContentPage
{
    const string ServerUrlPreference = "trackpad_server_url";
    const string DefaultServerUrl = "http://192.168.100.2:5000";
    const int DragHoldMilliseconds = 250;

    readonly List<(Button Button, HidKey Key)> bluetoothKeys = [];
    readonly HashSet<int> bluetoothHeldModifiers = [];
    BluetoothHidController? bluetoothController;
    CancellationTokenSource? dragHoldCancellation;
    CancellationTokenSource? edgeGlideCancellation;
    double padX;
    double padY;
    double padStartX;
    double padStartY;
    double bluetoothMoveRemainderX;
    double bluetoothMoveRemainderY;
    double bluetoothScrollRemainder;
    bool padMoved;
    bool padMultiTouch;
    bool bluetoothMouseDown;
    bool bluetoothKeyboardVisible;
    bool bluetoothShiftDown;
    bool bluetoothCapsLockOn;

    public MainPage()
    {
        InitializeComponent();

        string? savedUrl = Preferences.Get(ServerUrlPreference, null);
        ServerUrlEntry.Text = savedUrl ?? DefaultServerUrl;

        BluetoothTrackpadView.Drawable = new TrackpadSurfaceDrawable();
        BuildBluetoothKeyboard();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!TrackpadWebView.IsVisible && !IsBluetoothConnected)
        {
            SetConnectOrientation();
        }
    }

    void OnConnectClicked(object? sender, EventArgs e)
    {
        string input = ServerUrlEntry.Text?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? serverUri) ||
            serverUri.Scheme != Uri.UriSchemeHttp ||
            string.IsNullOrWhiteSpace(serverUri.Host))
        {
            ShowConnectionError("Enter the full local URL, including http:// and the port.");
            return;
        }

        string serverUrl = serverUri.ToString().TrimEnd('/');
        Preferences.Set(ServerUrlPreference, serverUrl);
        ConnectToServer(serverUrl);
    }

    void OnBluetoothModeClicked(object? sender, EventArgs e)
    {
        ConnectPanel.IsVisible = false;
        TrackpadWebView.IsVisible = false;
        BluetoothPanel.IsVisible = true;
        bool connected = IsBluetoothConnected;
        BluetoothStatusLabel.Text = connected
            ? "Connected. Use the touch area for the pointer, or switch to the keyboard below."
            : "Follow the three steps below. Pairing starts from Windows, not from the phone's Bluetooth settings.";
        UpdateBluetoothConnectionUi(connected);
    }

    void OnBackToModesClicked(object? sender, EventArgs e)
    {
        StopBluetoothPadGesture();
        BluetoothPanel.IsVisible = false;
        BluetoothSetupScreen.IsVisible = true;
        BluetoothControlScreen.IsVisible = false;
        BluetoothKeyboardToggle.IsVisible = false;
        BluetoothKeyboardGrid.IsVisible = false;
        BluetoothTrackpadPanel.IsVisible = true;
        bluetoothKeyboardVisible = false;
        BluetoothKeyboardToggle.Text = "⌨  Show keyboard";
        ConnectPanel.IsVisible = true;
        SetConnectOrientation();
    }

    async void OnStartBluetoothClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            BluetoothStatusLabel.Text = "Bluetooth keyboard mode requires Android 9 or later. Wi-Fi mode remains available.";
            return;
        }

        PermissionStatus permission = await Permissions.RequestAsync<BluetoothHidPermissions>();
        if (permission != PermissionStatus.Granted)
        {
            BluetoothStatusLabel.Text = "Bluetooth permissions are needed to advertise the keyboard and connect to a paired PC.";
            return;
        }

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
        {
            BluetoothStatusLabel.Text = "Android activity is unavailable. Reopen the app and try again.";
            return;
        }

        bluetoothController ??= new BluetoothHidController();
        bluetoothController.StatusChanged -= OnBluetoothStatusChanged;
        bluetoothController.StatusChanged += OnBluetoothStatusChanged;
        bluetoothController.CapsLockStateChanged -= OnBluetoothCapsLockStateChanged;
        bluetoothController.CapsLockStateChanged += OnBluetoothCapsLockStateChanged;

        try
        {
            bluetoothController.Start(activity);
            BluetoothStatusLabel.Text = "Starting Bluetooth keyboard mode…";
        }
        catch (PlatformNotSupportedException ex)
        {
            BluetoothStatusLabel.Text = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            BluetoothStatusLabel.Text = ex.Message;
        }
        catch (SecurityException ex)
        {
            BluetoothStatusLabel.Text = $"Bluetooth permission was denied: {ex.Message}";
        }
#else
        BluetoothStatusLabel.Text = "Bluetooth HID mode is available in the Android app.";
#endif
    }

    void OnConnectBluetoothHostClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            BluetoothStatusLabel.Text = "Bluetooth keyboard mode requires Android 9 or later.";
            return;
        }

        try
        {
            if (BluetoothHostPicker.SelectedItem is not BluetoothHostInfo host)
            {
                BluetoothStatusLabel.Text = "No paired PC selected. Pair “Phone Trackpad” from Windows first, then tap Refresh PC list.";
                return;
            }

            if (bluetoothController is null)
            {
                BluetoothStatusLabel.Text = "Tap Start and wait for Bluetooth mode to become ready before connecting.";
                return;
            }

            bluetoothController.Connect(host);
        }
        catch (SecurityException ex)
        {
            BluetoothStatusLabel.Text = $"Bluetooth permission was denied: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            BluetoothStatusLabel.Text = ex.Message;
        }
#endif
    }

    void OnMakeBluetoothDiscoverableClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28) || bluetoothController is null)
        {
            BluetoothStatusLabel.Text = "Tap Start Bluetooth first and wait for Ready.";
            return;
        }

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
        {
            BluetoothStatusLabel.Text = "Android activity is unavailable. Reopen the app and try again.";
            return;
        }

        try
        {
            string deviceName = bluetoothController.RequestDiscoverability(activity);
            BluetoothStatusLabel.Text = $"Allow the visibility prompt, then immediately search on the PC for “{deviceName}”. Phone visibility lasts up to 5 minutes.";
        }
        catch (SecurityException ex)
        {
            BluetoothStatusLabel.Text = $"Bluetooth permission was denied: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            BluetoothStatusLabel.Text = ex.Message;
        }
#endif
    }

    void OnRefreshBluetoothHostsClicked(object? sender, EventArgs e)
    {
        RefreshBluetoothHosts();
    }

    void OnBluetoothStatusChanged(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BluetoothStatusLabel.Text = message;
            BluetoothControlStatusLabel.Text = message;
            bool connected = IsBluetoothConnected;
            UpdateBluetoothConnectionUi(connected);

            if (message.Contains("ready", StringComparison.OrdinalIgnoreCase))
            {
                RefreshBluetoothHosts();
            }

            if (message.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
            {
                StopBluetoothPadGesture();
            }
        });
    }

    void OnBluetoothCapsLockStateChanged(bool isOn)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            bluetoothCapsLockOn = isOn;
            UpdateBluetoothKeyIndicators();
        });
    }

    void UpdateBluetoothConnectionUi(bool connected)
    {
        BluetoothSetupScreen.IsVisible = !connected;
        BluetoothControlScreen.IsVisible = connected;
        BluetoothKeyboardToggle.IsVisible = connected;
        BluetoothKeyboardGrid.IsVisible = connected && bluetoothKeyboardVisible;
        BluetoothTrackpadPanel.IsVisible = connected && !bluetoothKeyboardVisible;
        BluetoothControlStatusLabel.TextColor = connected
            ? Color.FromArgb("#86EFAC")
            : Color.FromArgb("#FCD34D");
        if (BluetoothPanel.IsVisible)
        {
            if (connected)
            {
                SetConnectedOrientation();
            }
            else
            {
                SetConnectOrientation();
            }
        }
    }

    bool IsBluetoothConnected
    {
        get
        {
#if ANDROID
            return OperatingSystem.IsAndroidVersionAtLeast(28) && bluetoothController?.IsConnected == true;
#else
            return false;
#endif
        }
    }

    void RefreshBluetoothHosts()
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return;
        }

        try
        {
            string? selectedAddress = (BluetoothHostPicker.SelectedItem as BluetoothHostInfo)?.Address;
            var hosts = bluetoothController?.GetPairedHosts() ?? [];
            List<BluetoothHostInfo> hostList = hosts.ToList();
            BluetoothHostPicker.ItemsSource = hostList;
            BluetoothHostPicker.SelectedItem = hostList.FirstOrDefault(host =>
                string.Equals(host.Address, selectedAddress, StringComparison.OrdinalIgnoreCase))
                ?? hostList.FirstOrDefault();
            BluetoothStatusLabel.Text = hostList.Count > 0
                ? "Paired devices loaded. Select the Windows PC, then tap Connect."
                : "No paired PC found yet. On Windows, add a Bluetooth device and choose “Phone Trackpad”.";
        }
        catch (SecurityException ex)
        {
            BluetoothStatusLabel.Text = $"Bluetooth permission was denied: {ex.Message}";
        }
#endif
    }

    void OnBluetoothKeyboardToggleClicked(object? sender, EventArgs e)
    {
        bluetoothKeyboardVisible = !bluetoothKeyboardVisible;
        if (bluetoothKeyboardVisible)
        {
            StopBluetoothPadGesture();
        }

        BluetoothTrackpadPanel.IsVisible = !bluetoothKeyboardVisible;
        BluetoothKeyboardGrid.IsVisible = bluetoothKeyboardVisible;
        BluetoothKeyboardToggle.Text = bluetoothKeyboardVisible ? "⌖  Show trackpad" : "⌨  Show keyboard";
    }

    void BuildBluetoothKeyboard()
    {
        HidKey[][] rows =
        [
            [
                new("Esc", 27), new("1", 49, "!"), new("2", 50, "@"), new("3", 51, "#"),
                new("4", 52, "$"), new("5", 53, "%"), new("6", 54, "^"), new("7", 55, "&"),
                new("8", 56, "*"), new("9", 57, "("), new("0", 48, ")"), new("Bksp", 8)
            ],
            [
                new("Tab", 9), new("Q", 81), new("W", 87), new("E", 69), new("R", 82),
                new("T", 84), new("Y", 89), new("U", 85), new("I", 73), new("O", 79),
                new("P", 80), new("[", 219, "{"), new("]", 221, "}")
            ],
            [
                new("Caps", 20), new("A", 65), new("S", 83), new("D", 68), new("F", 70),
                new("G", 71), new("H", 72), new("J", 74), new("K", 75), new("L", 76),
                new(";", 186, ":"), new("'", 222, "\""), new("Enter", 13)
            ],
            [
                new("Shift", 16), new("Z", 90), new("X", 88), new("C", 67), new("V", 86),
                new("B", 66), new("N", 78), new("M", 77), new(",", 188, "<"), new(".", 190, ">"),
                new("/", 191, "?"), new("↑", 38)
            ],
            [
                new("Ctrl", 17), new("Win", 91), new("Alt", 18), new("Space", 32),
                new("←", 37), new("↓", 40), new("→", 39), new("Del", 46)
            ]
        ];

        BluetoothKeyboardGrid.Children.Clear();
        BluetoothKeyboardGrid.RowDefinitions.Clear();
        bluetoothKeys.Clear();

        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            HidKey[] row = rows[rowIndex];
            var rowGrid = new Grid { ColumnSpacing = 3 };
            BluetoothKeyboardGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
            {
                HidKey key = row[columnIndex];
                var button = new Button
                {
                    Text = key.Label,
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#F8FAFC"),
                    BackgroundColor = Color.FromArgb("#182334"),
                    BorderColor = Color.FromArgb("#34445A"),
                    BorderWidth = 1,
                    CornerRadius = 6,
                    Padding = 2,
                    MinimumWidthRequest = 0
                };
                button.AutomationId = $"BluetoothKey{key.VirtualKey}";
                button.Pressed += (_, _) => OnBluetoothKeyPressed(key);
                button.Released += (_, _) => OnBluetoothKeyReleased(key);
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                rowGrid.Add(button, columnIndex, 0);
                bluetoothKeys.Add((button, key));
            }

            BluetoothKeyboardGrid.Add(rowGrid, 0, rowIndex);
        }
    }

    void OnBluetoothKeyPressed(HidKey key)
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            bluetoothController?.KeyDown(key.VirtualKey);
        }
#endif
        HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        if (key.VirtualKey == 16)
        {
            bluetoothShiftDown = true;
        }
        else if (key.VirtualKey == 20)
        {
            bluetoothCapsLockOn = !bluetoothCapsLockOn;
        }
        else if (key.VirtualKey is 17 or 18 or 91)
        {
            bluetoothHeldModifiers.Add(key.VirtualKey);
        }

        UpdateBluetoothKeyIndicators();
    }

    void OnBluetoothKeyReleased(HidKey key)
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            bluetoothController?.KeyUp(key.VirtualKey);
        }
#endif
        if (key.VirtualKey == 16)
        {
            bluetoothShiftDown = false;
        }
        else if (key.VirtualKey is 17 or 18 or 91)
        {
            bluetoothHeldModifiers.Remove(key.VirtualKey);
        }

        UpdateBluetoothKeyIndicators();
    }

    void UpdateBluetoothKeyIndicators()
    {
        foreach ((Button button, HidKey key) in bluetoothKeys)
        {
            button.Text = key.VirtualKey == 20 && bluetoothCapsLockOn
                ? "CAPS •"
                : bluetoothShiftDown && key.ShiftLabel is not null
                    ? key.ShiftLabel
                    : key.Label;

            bool isLatched = key.VirtualKey == 20 && bluetoothCapsLockOn;
            bool isHeldModifier = key.VirtualKey == 16
                ? bluetoothShiftDown
                : bluetoothHeldModifiers.Contains(key.VirtualKey);
            Color activeColor = isLatched || isHeldModifier
                ? Color.FromArgb("#0E7490")
                : Color.FromArgb("#182334");
            button.BackgroundColor = activeColor;
            button.BorderColor = isLatched || isHeldModifier
                ? Color.FromArgb("#67E8F9")
                : Color.FromArgb("#34445A");
        }
    }

    void OnBluetoothPadStart(object? sender, TouchEventArgs e)
    {
        var touches = e.Touches.ToArray();
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return;
        }
#endif
        if (bluetoothKeyboardVisible || bluetoothController?.IsConnected != true || touches.Length == 0)
        {
            return;
        }

        padMultiTouch = touches.Length > 1;
        padX = touches.Average(touch => touch.X);
        padY = touches.Average(touch => touch.Y);
        padStartX = padX;
        padStartY = padY;
        bluetoothMoveRemainderX = 0;
        bluetoothMoveRemainderY = 0;
        padMoved = false;
        dragHoldCancellation?.Cancel();
        dragHoldCancellation?.Dispose();
        dragHoldCancellation = new CancellationTokenSource();
        if (!padMultiTouch)
        {
            _ = StartBluetoothLongPressAsync(dragHoldCancellation.Token);
        }
    }

    async Task StartBluetoothLongPressAsync(CancellationToken cancellationToken)
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return;
        }
#endif
        try
        {
            await Task.Delay(DragHoldMilliseconds, cancellationToken);
            if (!padMoved && BluetoothPanel.IsVisible && !bluetoothKeyboardVisible)
            {
                bluetoothMouseDown = true;
                bluetoothController?.SetMouseButton(true);
                HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
                edgeGlideCancellation?.Cancel();
                edgeGlideCancellation?.Dispose();
                edgeGlideCancellation = new CancellationTokenSource();
                _ = RunBluetoothEdgeGlideAsync(edgeGlideCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void OnBluetoothPadDrag(object? sender, TouchEventArgs e)
    {
        var touches = e.Touches.ToArray();
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return;
        }
#endif
        if (bluetoothKeyboardVisible || bluetoothController?.IsConnected != true || touches.Length == 0)
        {
            return;
        }

        if (touches.Length > 1)
        {
            bool beganMultiTouch = !padMultiTouch;
            padMultiTouch = true;
            dragHoldCancellation?.Cancel();
            double centerX = touches.Average(touch => touch.X);
            double centerY = touches.Average(touch => touch.Y);
            double scrollDelta = centerY - padY;
            padX = centerX;
            padY = centerY;
            if (!beganMultiTouch)
            {
                padMoved = true;
                bluetoothScrollRemainder -= scrollDelta * 0.75;
                int wheelDelta = (int)bluetoothScrollRemainder;
                if (wheelDelta != 0)
                {
                    bluetoothController.ScrollMouse(wheelDelta);
                    bluetoothScrollRemainder -= wheelDelta;
                }
            }

            return;
        }

        if (padMultiTouch)
        {
            padX = touches[0].X;
            padY = touches[0].Y;
            return;
        }

        double x = touches[0].X;
        double y = touches[0].Y;
        double deltaX = x - padX;
        double deltaY = y - padY;
        padX = x;
        padY = y;

        if (Math.Abs(x - padStartX) > 3 || Math.Abs(y - padStartY) > 3)
        {
            padMoved = true;
        }

        if (padMoved)
        {
            dragHoldCancellation?.Cancel();
        }

        SendBluetoothMouseMovement(deltaX * 1.8, deltaY * 1.8);
    }

    void SendBluetoothMouseMovement(double dx, double dy)
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return;
        }
#endif
        bluetoothMoveRemainderX += dx;
        bluetoothMoveRemainderY += dy;
        int moveX = (int)Math.Truncate(bluetoothMoveRemainderX);
        int moveY = (int)Math.Truncate(bluetoothMoveRemainderY);
        bluetoothMoveRemainderX -= moveX;
        bluetoothMoveRemainderY -= moveY;
        if (moveX != 0 || moveY != 0)
        {
            bluetoothController?.MoveMouse(moveX, moveY);
        }
    }

    async void OnBluetoothPadEnd(object? sender, TouchEventArgs e)
    {
        dragHoldCancellation?.Cancel();
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return;
        }
#endif
        if (bluetoothMouseDown)
        {
            bluetoothController?.SetMouseButton(false);
            bluetoothMouseDown = false;
            edgeGlideCancellation?.Cancel();
        }
        else if (!padMoved && bluetoothController?.IsConnected == true)
        {
            await bluetoothController.ClickMouseAsync(padMultiTouch);
        }

        padMultiTouch = false;
        bluetoothScrollRemainder = 0;
    }

    async Task RunBluetoothEdgeGlideAsync(CancellationToken cancellationToken)
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return;
        }
#endif
        try
        {
            while (bluetoothMouseDown && !cancellationToken.IsCancellationRequested)
            {
                double width = BluetoothTrackpadView.Width;
                double height = BluetoothTrackpadView.Height;
                double edgeX = Math.Min(width * 0.16, 110);
                double edgeY = Math.Min(height * 0.2, 90);
                double velocityX = GetEdgeVelocity(padX, width, edgeX, 1000);
                double velocityY = GetEdgeVelocity(padY, height, edgeY, 850);
                SendBluetoothMouseMovement(velocityX / 60, velocityY / 60);
                await Task.Delay(16, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    static double GetEdgeVelocity(double position, double length, double edgeSize, double maximumSpeed)
    {
        double fromStart = Math.Clamp((edgeSize - position) / edgeSize, 0, 1);
        double fromEnd = Math.Clamp((position - (length - edgeSize)) / edgeSize, 0, 1);
        return (fromEnd * fromEnd - fromStart * fromStart) * maximumSpeed;
    }

    void StopBluetoothPadGesture()
    {
        dragHoldCancellation?.Cancel();
        edgeGlideCancellation?.Cancel();
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return;
        }
#endif
        if (bluetoothMouseDown)
        {
            bluetoothController?.SetMouseButton(false);
            bluetoothMouseDown = false;
        }
    }

    void ConnectToServer(string serverUrl)
    {
        StatusLabel.IsVisible = false;
        ConnectPanel.IsVisible = false;
        BluetoothPanel.IsVisible = false;
        TrackpadWebView.IsVisible = true;
        TrackpadWebView.Source = new UrlWebViewSource { Url = serverUrl };
    }

    void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result == WebNavigationResult.Success)
        {
            SetConnectedOrientation();
            return;
        }

        SetConnectOrientation();
        TrackpadWebView.IsVisible = false;
        ConnectPanel.IsVisible = true;
        ShowConnectionError("Couldn't reach the PC. Check that the server is running and both devices are on the same Wi-Fi.");
    }

    void ShowConnectionError(string message)
    {
        StatusLabel.Text = message;
        StatusLabel.IsVisible = true;
    }

    void SetConnectedOrientation()
    {
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is Android.App.Activity activity)
        {
            if (activity is MainActivity)
            {
                MainActivity.LandscapeModeRequested = true;
            }
            activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.Landscape;
        }
#endif
    }

    void SetConnectOrientation()
    {
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is Android.App.Activity activity)
        {
            if (activity is MainActivity)
            {
                MainActivity.LandscapeModeRequested = false;
            }
            activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.Portrait;
        }
#endif
    }

    protected override bool OnBackButtonPressed()
    {
        if (BluetoothPanel.IsVisible)
        {
            OnBackToModesClicked(this, EventArgs.Empty);
            return true;
        }

        if (TrackpadWebView.IsVisible)
        {
            TrackpadWebView.IsVisible = false;
            ConnectPanel.IsVisible = true;
            SetConnectOrientation();
            return true;
        }

        return base.OnBackButtonPressed();
    }

    readonly record struct HidKey(string Label, int VirtualKey, string? ShiftLabel = null);

    sealed class TrackpadSurfaceDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = Color.FromArgb("#101722");
            canvas.FillRoundedRectangle(dirtyRect, 24);
            canvas.StrokeColor = Color.FromArgb("#263449");
            canvas.StrokeSize = 1;
            canvas.DrawRoundedRectangle(dirtyRect, 24);
        }
    }
}

#if ANDROID
sealed class BluetoothHidPermissions : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ?
            [
                (Android.Manifest.Permission.BluetoothConnect, true),
                (Android.Manifest.Permission.BluetoothAdvertise, true)
            ]
            : [];
}
#endif

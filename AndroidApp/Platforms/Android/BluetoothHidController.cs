using Android.Bluetooth;
using Android.Content;
using Android.OS;
using Java.Util.Concurrent;
using System.Runtime.Versioning;

namespace PhoneTrackpad.Mobile;

public sealed record BluetoothHostInfo(string Name, string Address)
{
    public override string ToString() => Name;
}

[SupportedOSPlatform("android28.0")]
public sealed class BluetoothHidController
{
    const int KeyboardReportId = 1;
    const int MouseReportId = 2;

    static readonly byte[] ReportDescriptor = Convert.FromHexString(
        "05010906A1018501050719E029E71500250175019508810295017508810195067508150025650507190029658100050819012905950575019102950175039101C0" +
        "05010902A10185020901A1000509190129051500250195057501810295037501810105010930093109381581257F750895038106C0C0");

    readonly IExecutor callbackExecutor = Executors.NewSingleThreadExecutor()
        ?? throw new InvalidOperationException("Android could not create the Bluetooth callback executor.");
    readonly HashSet<byte> pressedKeys = [];
    BluetoothAdapter? adapter;
    BluetoothHidDevice? hidDevice;
    BluetoothDevice? connectedHost;
    byte modifiers;
    byte mouseButtons;
    byte hostLedState;
    bool registered;
    bool registrationPending;
    bool profileRequestPending;
    bool connectionAttemptPending;
    bool inputReportFailureReported;
    bool inputReportSent;
    bool bootProtocolMode;

    public event Action<string>? StatusChanged;
    public event Action<bool>? CapsLockStateChanged;

    public bool IsConnected { get; private set; }

    public string RequestDiscoverability(Context context)
    {
        if (adapter is null || !registered)
        {
            throw new InvalidOperationException("Tap Start Bluetooth and wait for Ready before making the phone discoverable.");
        }

        string deviceName = string.IsNullOrWhiteSpace(adapter.Name) ? "Android phone" : adapter.Name;
        var intent = new Intent(BluetoothAdapter.ActionRequestDiscoverable);
        intent.PutExtra(BluetoothAdapter.ExtraDiscoverableDuration, 300);
        context.StartActivity(intent);
        return deviceName;
    }

    public void Start(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            throw new PlatformNotSupportedException("Bluetooth keyboard and mouse mode requires Android 9 or later.");
        }

        var bluetoothManager = (BluetoothManager?)context.GetSystemService(Context.BluetoothService);
        adapter = bluetoothManager?.Adapter
            ?? throw new PlatformNotSupportedException("This phone does not have a Bluetooth adapter.");

        if (!adapter.IsEnabled)
        {
            throw new InvalidOperationException("Turn on Bluetooth, then tap Start again.");
        }

        if (hidDevice is not null)
        {
            RegisterHidApp();
            return;
        }

        if (profileRequestPending)
        {
            return;
        }

        StatusChanged?.Invoke("Starting Bluetooth keyboard mode…");
        profileRequestPending = true;
        if (!adapter.GetProfileProxy(context, new HidProfileListener(this), ProfileType.HidDevice))
        {
            profileRequestPending = false;
            throw new InvalidOperationException("Android could not open the Bluetooth HID profile.");
        }
    }

    public IReadOnlyList<BluetoothHostInfo> GetPairedHosts()
    {
        if (adapter is null)
        {
            return [];
        }

        return (adapter.BondedDevices ?? [])
            .Select(device => new BluetoothHostInfo(
                string.IsNullOrWhiteSpace(device.Name) ? device.Address ?? "Paired device" : device.Name,
                device.Address ?? string.Empty))
            .Where(host => !string.IsNullOrWhiteSpace(host.Address))
            .OrderBy(host => host.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public bool Connect(BluetoothHostInfo host)
    {
        if (!registered || hidDevice is null || adapter is null)
        {
            StatusChanged?.Invoke("Tap Start and wait for Bluetooth mode to become ready.");
            return false;
        }

        if (IsConnected || connectionAttemptPending)
        {
            StatusChanged?.Invoke(IsConnected
                ? "Bluetooth is already connected to a PC."
                : "Bluetooth is already connecting. Keep this screen open while Windows finishes.");
            return false;
        }

        if (!(adapter.BondedDevices ?? []).Any(device => string.Equals(device.Address, host.Address, StringComparison.OrdinalIgnoreCase)))
        {
            StatusChanged?.Invoke("Pair this phone with the PC in Windows Bluetooth settings first.");
            return false;
        }

        connectedHost = adapter.GetRemoteDevice(host.Address);
        connectionAttemptPending = true;
        var accepted = hidDevice.Connect(connectedHost);
        if (!accepted)
        {
            connectionAttemptPending = false;
            connectedHost = null;
            StatusChanged?.Invoke($"Android could not start a HID connection to {host.Name}. Make sure HID mode says Ready, then remove the old pairing on both devices and pair again from Windows Bluetooth settings.");
        }
        else
        {
            StatusChanged?.Invoke($"Connecting to {host.Name}… Keep Phone Trackpad open while Windows finishes.");
        }

        return accepted;
    }

    public void Disconnect()
    {
        if (hidDevice is not null && connectedHost is not null)
        {
            hidDevice.Disconnect(connectedHost);
        }
    }

    public void KeyDown(int virtualKey)
    {
        if (TryGetModifier(virtualKey, out byte modifier))
        {
            modifiers |= modifier;
            SendKeyboardReport();
            return;
        }

        if (TryMapKey(virtualKey, out byte usage) && pressedKeys.Add(usage))
        {
            SendKeyboardReport();
        }
    }

    public void KeyUp(int virtualKey)
    {
        if (TryGetModifier(virtualKey, out byte modifier))
        {
            modifiers &= (byte)~modifier;
            SendKeyboardReport();
            return;
        }

        if (TryMapKey(virtualKey, out byte usage) && pressedKeys.Remove(usage))
        {
            SendKeyboardReport();
        }
    }

    public void PressKey(int virtualKey)
    {
        KeyDown(virtualKey);
        KeyUp(virtualKey);
    }

    public void MoveMouse(double dx, double dy)
    {
        SendMouseReport(ClampDelta(dx), ClampDelta(dy));
    }

    public void ScrollMouse(int wheelDelta)
    {
        SendMouseReport(0, 0, wheelDelta);
    }

    public void SetMouseButton(bool pressed)
    {
        mouseButtons = pressed ? (byte)(mouseButtons | 1) : (byte)(mouseButtons & ~1);
        SendMouseReport(0, 0);
    }

    public async Task ClickMouseAsync(bool rightButton = false)
    {
        byte button = rightButton ? (byte)2 : (byte)1;
        mouseButtons |= button;
        SendMouseReport(0, 0);
        await Task.Delay(30);
        mouseButtons &= (byte)~button;
        SendMouseReport(0, 0);
    }

    void OnProfileConnected(IBluetoothProfile? proxy)
    {
        profileRequestPending = false;
        hidDevice = proxy as BluetoothHidDevice;
        if (hidDevice is null)
        {
            StatusChanged?.Invoke("Android returned an unexpected Bluetooth profile.");
            return;
        }

        RegisterHidApp();
    }

    void RegisterHidApp()
    {
        if (hidDevice is null || registered || registrationPending)
        {
            return;
        }

        registrationPending = true;
        var settings = new BluetoothHidDeviceAppSdpSettings(
            "Phone Trackpad",
            "Bluetooth keyboard and mouse",
            "Phone Trackpad",
            BluetoothHidDevice.Subclass1Combo,
            ReportDescriptor);

        if (!hidDevice.RegisterApp(settings, null, null, callbackExecutor, new HidDeviceCallback(this)))
        {
            registrationPending = false;
            StatusChanged?.Invoke("Android could not register Bluetooth keyboard mode. Check Bluetooth permissions and try again.");
        }
    }

    void OnAppStatusChanged(bool isRegistered)
    {
        registered = isRegistered;
        registrationPending = false;
        StatusChanged?.Invoke(isRegistered
            ? "Bluetooth HID is ready. Tap “Make phone discoverable”, allow the Android prompt, then search for the phone from Windows."
            : "Bluetooth keyboard mode is not registered. Tap Start to try again.");
    }

    void OnSetReport(BluetoothDevice? device, sbyte type, sbyte reportId, byte[]? data)
    {
        if (device is null || type != BluetoothHidDevice.ReportTypeOutput ||
            data is not { Length: > 0 } ||
            (!bootProtocolMode && unchecked((byte)reportId) != KeyboardReportId) ||
            (bootProtocolMode && reportId != 0))
        {
            return;
        }

        bool wasCapsLockOn = (hostLedState & 0x02) != 0;
        hostLedState = data[0];
        bool isCapsLockOn = (hostLedState & 0x02) != 0;
        if (wasCapsLockOn != isCapsLockOn)
        {
            CapsLockStateChanged?.Invoke(isCapsLockOn);
        }
    }

    void OnGetReport(BluetoothDevice? device, sbyte type, sbyte reportId, int bufferSize)
    {
        if (device is null || hidDevice is null || bufferSize < 0)
        {
            return;
        }

        byte id = unchecked((byte)reportId);
        byte[]? report = type == BluetoothHidDevice.ReportTypeInput
            ? bootProtocolMode
                ? id == 0 ? GetKeyboardReport() : null
                : id switch
                {
                    KeyboardReportId => GetKeyboardReport(),
                    MouseReportId => GetMouseReport(),
                    _ => null
                }
            : type == BluetoothHidDevice.ReportTypeOutput &&
                ((bootProtocolMode && id == 0) || (!bootProtocolMode && id == KeyboardReportId))
                    ? [hostLedState]
                    : null;

        if (report is null)
        {
            return;
        }

        if (report.Length < bufferSize)
        {
            Array.Resize(ref report, bufferSize);
        }

        hidDevice.ReplyReport(device, type, reportId, report);
    }

    void OnSetProtocol(sbyte protocol)
    {
        bootProtocolMode = protocol == BluetoothHidDevice.ProtocolBootMode;
        StatusChanged?.Invoke(bootProtocolMode
            ? "Windows requested Boot protocol. Sending standard keyboard and mouse reports."
            : "Windows selected Report protocol. Sending keyboard and mouse reports.");
    }

    void OnConnectionStateChanged(BluetoothDevice? device, ProfileState state)
    {
        if (device is not null && connectedHost is not null &&
            !string.Equals(device.Address, connectedHost.Address, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (state == ProfileState.Connected)
        {
            IsConnected = true;
            connectionAttemptPending = false;
            inputReportSent = false;
            inputReportFailureReported = false;
            if (device is not null)
            {
                connectedHost = device;
            }
            StatusChanged?.Invoke($"Connected to {device?.Name ?? "Windows PC"} by Bluetooth.");
        }
        else if (state == ProfileState.Connecting)
        {
            IsConnected = false;
            StatusChanged?.Invoke($"Connecting to {device?.Name ?? "Windows PC"}… Keep Phone Trackpad open while Windows finishes.");
        }
        else if (state == ProfileState.Disconnecting)
        {
            StatusChanged?.Invoke("Windows is closing the Bluetooth HID connection…");
        }
        else
        {
            bool connectionWasPending = connectionAttemptPending;
            IsConnected = false;
            ReleaseAllInputs();
            connectionAttemptPending = false;
            if (connectionWasPending)
            {
                StatusChanged?.Invoke("Windows ended the Bluetooth connection before it finished. Keep the app open, then tap Connect again. If it repeats, remove the phone from Windows Bluetooth settings and pair it again.");
            }
            else
            {
                StatusChanged?.Invoke("The Bluetooth connection to Windows was lost. Keep the app open and tap Connect to try again.");
            }
        }
    }

    void SendKeyboardReport()
    {
        if (!IsConnected || hidDevice is null || connectedHost is null)
        {
            return;
        }

        SendReport(bootProtocolMode ? 0 : KeyboardReportId, GetKeyboardReport());
    }

    byte[] GetKeyboardReport()
    {
        var report = new byte[8];
        report[0] = modifiers;
        foreach (byte usage in pressedKeys.Take(6))
        {
            int slot = Array.IndexOf(report, (byte)0, 2, 6);
            if (slot < 0)
            {
                break;
            }

            report[slot] = usage;
        }

        return report;
    }

    void SendMouseReport(int dx, int dy, int wheelDelta = 0)
    {
        if (!IsConnected || hidDevice is null || connectedHost is null)
        {
            return;
        }

        if (bootProtocolMode)
        {
            SendReport(0, GetBootMouseReport(dx, dy));
        }
        else
        {
            SendReport(MouseReportId, GetMouseReport(dx, dy, wheelDelta));
        }
    }

    byte[] GetBootMouseReport(int dx, int dy) =>
    [
        mouseButtons,
        unchecked((byte)Math.Clamp(dx, sbyte.MinValue, sbyte.MaxValue)),
        unchecked((byte)Math.Clamp(dy, sbyte.MinValue, sbyte.MaxValue))
    ];

    byte[] GetMouseReport(int dx = 0, int dy = 0, int wheelDelta = 0)
    {
        var report = new byte[4];
        report[0] = mouseButtons;
        report[1] = unchecked((byte)Math.Clamp(dx, sbyte.MinValue, sbyte.MaxValue));
        report[2] = unchecked((byte)Math.Clamp(dy, sbyte.MinValue, sbyte.MaxValue));
        report[3] = unchecked((byte)Math.Clamp(wheelDelta, sbyte.MinValue, sbyte.MaxValue));
        return report;
    }

    void SendReport(int reportId, byte[] report)
    {
        if (hidDevice is null || connectedHost is null)
        {
            return;
        }

        if (hidDevice.SendReport(connectedHost, reportId, report))
        {
            inputReportFailureReported = false;
            if (!inputReportSent)
            {
                inputReportSent = true;
                StatusChanged?.Invoke("Connected. The phone sent an input report; Windows does not confirm whether it applied it.");
            }
        }
        else if (!inputReportFailureReported)
        {
            inputReportFailureReported = true;
            StatusChanged?.Invoke("Bluetooth says Connected, but the phone could not send keyboard/mouse input. Reconnect once; if it persists, this phone's HID profile may be incompatible.");
        }
    }

    void ReleaseAllInputs()
    {
        modifiers = 0;
        pressedKeys.Clear();
        mouseButtons = 0;
    }

    static int ClampDelta(double value) => (int)Math.Clamp(Math.Round(value), sbyte.MinValue, sbyte.MaxValue);

    static bool TryGetModifier(int virtualKey, out byte mask)
    {
        mask = virtualKey switch
        {
            16 => 0x02,
            17 => 0x01,
            18 => 0x04,
            91 => 0x08,
            _ => 0
        };
        return mask != 0;
    }

    static bool TryMapKey(int virtualKey, out byte usage)
    {
        if (virtualKey is >= 65 and <= 90)
        {
            usage = (byte)(virtualKey - 65 + 0x04);
            return true;
        }

        if (virtualKey is >= 49 and <= 57)
        {
            usage = (byte)(virtualKey - 49 + 0x1E);
            return true;
        }

        if (virtualKey == 48)
        {
            usage = 0x27;
            return true;
        }

        usage = virtualKey switch
        {
            8 => 0x2A,
            9 => 0x2B,
            13 => 0x28,
            20 => 0x39,
            27 => 0x29,
            32 => 0x2C,
            37 => 0x50,
            38 => 0x52,
            39 => 0x4F,
            40 => 0x51,
            46 => 0x4C,
            112 => 0x3A,
            113 => 0x3B,
            114 => 0x3C,
            115 => 0x3D,
            116 => 0x3E,
            117 => 0x3F,
            118 => 0x40,
            119 => 0x41,
            120 => 0x42,
            121 => 0x43,
            122 => 0x44,
            123 => 0x45,
            186 => 0x33,
            187 => 0x2E,
            188 => 0x36,
            189 => 0x2D,
            190 => 0x37,
            191 => 0x38,
            192 => 0x35,
            219 => 0x2F,
            220 => 0x31,
            221 => 0x30,
            222 => 0x34,
            _ => 0
        };
        return usage != 0;
    }

    sealed class HidProfileListener(BluetoothHidController owner) : Java.Lang.Object, IBluetoothProfileServiceListener
    {
        public void OnServiceConnected(ProfileType profile, IBluetoothProfile? proxy) => owner.OnProfileConnected(proxy);

        public void OnServiceDisconnected(ProfileType profile)
        {
            owner.hidDevice = null;
            owner.registered = false;
            owner.registrationPending = false;
            owner.profileRequestPending = false;
            owner.IsConnected = false;
            owner.connectionAttemptPending = false;
            owner.inputReportSent = false;
            owner.inputReportFailureReported = false;
            owner.ReleaseAllInputs();
            owner.StatusChanged?.Invoke("Bluetooth mode stopped. Tap Start to reconnect.");
        }
    }

    sealed class HidDeviceCallback(BluetoothHidController owner) : BluetoothHidDevice.Callback
    {
        public override void OnAppStatusChanged(BluetoothDevice? pluggedDevice, bool registered) =>
            owner.OnAppStatusChanged(registered);

        public override void OnGetReport(BluetoothDevice? device, sbyte type, sbyte id, int bufferSize) =>
            owner.OnGetReport(device, type, id, bufferSize);

        public override void OnSetReport(BluetoothDevice? device, sbyte type, sbyte id, byte[]? data) =>
            owner.OnSetReport(device, type, id, data);

        public override void OnSetProtocol(BluetoothDevice? device, sbyte protocol) =>
            owner.OnSetProtocol(protocol);

        public override void OnConnectionStateChanged(BluetoothDevice? device, ProfileState state) =>
            owner.OnConnectionStateChanged(device, state);
    }
}

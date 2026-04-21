using GRRadio.Models;
using GRRadio.Protocols;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace GRRadio.Services;

public class BluetoothKissService : IDisposable
{
    private readonly SettingsService _settings;

    private BluetoothClient?          _client;
    private Stream?                   _stream;
    private CancellationTokenSource?  _cts;
    private int                       _messageNumber = 0;
    private readonly List<byte>       _receiveBuffer = new();

    public TncState State { get; private set; } = TncState.Disconnected;

    public event EventHandler<TncState>?    StateChanged;
    public event EventHandler<AprsMessage>? MessageReceived;
    public event EventHandler<string>?      StationHeard;
    public event EventHandler<string>?      ErrorOccurred;

    public BluetoothKissService(SettingsService settings) { _settings = settings; }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<bool> RequestPermissionsAsync()
    {
        var status = await Permissions.RequestAsync<Permissions.Bluetooth>();
        if (status == PermissionStatus.Granted) return true;

        var msg = status == PermissionStatus.Denied
            ? "Bluetooth permission denied. Enable it in App Settings."
            : $"Bluetooth permission status: {status}";
        ErrorOccurred?.Invoke(this, msg);
        return false;
    }

    public async Task<IEnumerable<BluetoothDeviceInfo>> GetPairedDevicesAsync()
    {
        if (!await RequestPermissionsAsync()) return Enumerable.Empty<BluetoothDeviceInfo>();
        try
        {
            using var client = new BluetoothClient();
            return client.PairedDevices;
        }
        catch { return Enumerable.Empty<BluetoothDeviceInfo>(); }
    }

    public async Task<bool> ConnectAsync(string bluetoothAddress)
    {
        if (State == TncState.Connected || State == TncState.Connecting) return false;
        if (!await RequestPermissionsAsync()) return false;

        SetState(TncState.Connecting);
        try
        {
            var address  = BluetoothAddress.Parse(bluetoothAddress);
            var endpoint = new BluetoothEndPoint(address, KissProtocol.SppUuid);

            _client = new BluetoothClient();
            await Task.Run(() => _client.Connect(endpoint));

            _stream = _client.GetStream();
            _cts    = new CancellationTokenSource();
            _receiveBuffer.Clear();

            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));

            SetState(TncState.Connected);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Connection failed: {ex.Message}");
            SetState(TncState.Error);
            CleanupConnection();
            return false;
        }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        CleanupConnection();
        SetState(TncState.Disconnected);
    }

    public async Task<bool> SendMessageAsync(string toCall, string message)
    {
        if (State != TncState.Connected || _stream == null) return false;
        try
        {
            var s     = _settings.Load();
            var frame = KissProtocol.CreateMessageFrame(s.FullCallsign, toCall, message, ++_messageNumber);
            await _stream.WriteAsync(frame.ToBytes());
            return true;
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(this, $"Send error: {ex.Message}"); return false; }
    }

    public async Task<bool> SendBeaconAsync()
    {
        if (State != TncState.Connected || _stream == null) return false;
        try
        {
            var s     = _settings.Load();
            var frame = KissProtocol.CreateBeaconFrame(s.FullCallsign, s.Latitude, s.Longitude, s.AprsSymbol, s.AprsComment);
            await _stream.WriteAsync(frame.ToBytes());
            return true;
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(this, $"Beacon error: {ex.Message}"); return false; }
    }

    // ── Receive loop ──────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buf = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                var bytesRead = await _stream.ReadAsync(buf, ct);
                if (bytesRead == 0) break;
                _receiveBuffer.AddRange(buf[..bytesRead]);
                ProcessKissFrames();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            ErrorOccurred?.Invoke(this, $"Receive error: {ex.Message}");
        }
        finally
        {
            if (State == TncState.Connected) { SetState(TncState.Disconnected); CleanupConnection(); }
        }
    }

    private void ProcessKissFrames()
    {
        while (_receiveBuffer.Count > 0)
        {
            var start = _receiveBuffer.IndexOf(KissFrame.FEND);
            if (start == -1) { _receiveBuffer.Clear(); break; }
            if (start > 0)   _receiveBuffer.RemoveRange(0, start);

            var end = -1;
            for (int i = 1; i < _receiveBuffer.Count; i++)
                if (_receiveBuffer[i] == KissFrame.FEND) { end = i; break; }

            if (end == -1) break;

            var frameData = _receiveBuffer.GetRange(0, end + 1).ToArray();
            _receiveBuffer.RemoveRange(0, end + 1);

            var frame = KissFrame.FromBytes(frameData);
            if (frame is null) continue;

            var msg = KissProtocol.ParseAprsMessage(frame);
            if (msg is null) continue;

            MessageReceived?.Invoke(this, msg);
            StationHeard?.Invoke(this, msg.From);
        }
    }

    private void SetState(TncState newState)
    {
        if (State == newState) return;
        State = newState;
        StateChanged?.Invoke(this, newState);
    }

    private void CleanupConnection()
    {
        _stream?.Dispose(); _client?.Dispose(); _cts?.Dispose();
        _stream = null; _client = null; _cts = null;
    }

    public void Dispose() => Disconnect();
}

public enum TncState { Disconnected, Connecting, Connected, Error }

using System.Runtime.InteropServices;

namespace AsusHardwareService;

/// <summary>
/// Provides low-level access to the ASUS ACPI device interface exposed through <c>\\.\ATKACPI</c>.
/// </summary>
/// <remarks>
/// This class opens the ASUS ACPI device and sends vendor-specific control requests through
/// <see cref="DeviceIoControl(IntPtr, uint, byte[], uint, byte[], uint, ref uint, IntPtr)"/>.
/// It is used by this service to apply hardware settings such as the battery charge limit.
/// </remarks>
public sealed class AsusAcpi : IDisposable
{
    private const string FileName = @"\\.\ATKACPI";
    private const uint ControlCode = 0x0022240C;
    private const uint Devs = 0x53564544;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;

    private static readonly IntPtr InvalidHandleValue = new(-1);

    /// <summary>
    /// ASUS ACPI device identifier for the battery charge limit setting.
    /// </summary>
    public const uint BatteryLimit = 0x00120057;

    private readonly ILogger<AsusAcpi> _logger;
    private IntPtr _handle;
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether the ASUS ACPI device was opened successfully.
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// Initialises a new instance of the <see cref="AsusAcpi"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics and error reporting.</param>
    public AsusAcpi(ILogger<AsusAcpi> logger)
    {
        _logger = logger;

        _handle = CreateFile(
            FileName,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        IsConnected = _handle != IntPtr.Zero && _handle != InvalidHandleValue;

        if (!IsConnected)
        {
            int error = Marshal.GetLastWin32Error();
            _logger.LogError("Cannot open {FileName}. Win32 error {Error}.", FileName, error);
        }
    }

    /// <summary>
    /// Sends a device-setting request to the ASUS ACPI interface.
    /// </summary>
    /// <param name="deviceId">The ASUS device setting identifier.</param>
    /// <param name="status">The value to apply to the device setting.</param>
    /// <param name="logName">An optional friendly name used for logging.</param>
    /// <returns>
    /// The integer result returned by the ASUS ACPI call. A value of <c>1</c> typically indicates success.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has already been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the ACPI call fails.</exception>
    public int DeviceSet(uint deviceId, int status, string? logName)
    {
        ThrowIfDisposed();

        byte[] args = new byte[8];
        BitConverter.GetBytes(deviceId).CopyTo(args, 0);
        BitConverter.GetBytes((uint)status).CopyTo(args, 4);

        byte[] reply = CallMethod(Devs, args);
        int result = BitConverter.ToInt32(reply, 0);

        if (!string.IsNullOrWhiteSpace(logName))
        {
            _logger.LogInformation(
                "{LogName} = {Status} : {Result}",
                logName,
                status,
                result == 1 ? "OK" : result.ToString());
        }

        return result;
    }

    /// <summary>
    /// Releases the unmanaged device handle.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalises the instance if it was not disposed explicitly.
    /// </summary>
    ~AsusAcpi()
    {
        Dispose(disposing: false);
    }

    private byte[] CallMethod(uint methodId, byte[] args)
    {
        ThrowIfDisposed();

        byte[] acpiBuffer = new byte[8 + args.Length];
        byte[] outBuffer = new byte[16];

        BitConverter.GetBytes(methodId).CopyTo(acpiBuffer, 0);
        BitConverter.GetBytes((uint)args.Length).CopyTo(acpiBuffer, 4);
        Array.Copy(args, 0, acpiBuffer, 8, args.Length);

        uint bytesReturned = 0;

        bool ok = DeviceIoControl(
            _handle,
            ControlCode,
            acpiBuffer,
            (uint)acpiBuffer.Length,
            outBuffer,
            (uint)outBuffer.Length,
            ref bytesReturned,
            IntPtr.Zero);

        if (!ok)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"DeviceIoControl failed with Win32 error {error}.");
        }

        return outBuffer;
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != IntPtr.Zero && _handle != InvalidHandleValue)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        ref uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
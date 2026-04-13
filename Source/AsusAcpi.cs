using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService;

/// <summary>
/// Provides low level access to the ASUS ACPI device exposed as <c>\\.\ATKACPI</c>.
/// </summary>
/// <remarks>
/// The service uses this wrapper to read and write ASUS specific hardware values such as the battery
/// charge limit, performance mode, and GPU related flags.
/// </remarks>
public sealed class AsusAcpi : IDisposable
{
    private const string DevicePath = @"\\.\ATKACPI";
    private const uint AsusAcpiIoControlCode = 0x0022240C;
    private const uint ReadMethodId = 0x53545344;
    private const uint WriteMethodId = 0x53564544;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const int OutputBufferSize = 16;
    private const int RequestHeaderSize = 8;
    private const int ReadResultOffset = 65536;

    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>
    /// ASUS ACPI device identifier for the battery charge limit setting.
    /// </summary>
    public const uint BatteryLimit = 0x00120057;

    /// <summary>
    /// ASUS ACPI device identifier for the performance mode setting.
    /// </summary>
    public const uint PerformanceMode = 0x00120075;

    /// <summary>
    /// ASUS ACPI device identifier for the ROG GPU Eco mode setting.
    /// </summary>
    public const uint GpuEcoRog = 0x00090020;

    /// <summary>
    /// ASUS ACPI device identifier for the ROG GPU MUX mode setting.
    /// </summary>
    public const uint GpuMuxRog = 0x00090016;

    private readonly ILogger<AsusAcpi> _logger;
    private IntPtr _deviceHandle;
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether the ASUS ACPI device was opened successfully.
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// Initialises a new instance of the <see cref="AsusAcpi"/> class.
    /// </summary>
    public AsusAcpi(ILogger<AsusAcpi> logger)
    {
        _logger = logger;

        _deviceHandle = CreateFile(
            DevicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        IsConnected = _deviceHandle != IntPtr.Zero && _deviceHandle != InvalidHandle;
        if (!IsConnected)
        {
            _logger.LogError("Cannot open {DevicePath}. Win32 error {Error}.", DevicePath, Marshal.GetLastWin32Error());
        }
    }

    /// <summary>
    /// Writes an ASUS ACPI value.
    /// </summary>
    /// <param name="deviceId">The ASUS ACPI device identifier.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="logName">An optional friendly name used in log messages.</param>
    /// <returns>The raw result returned by the driver. A value of <c>1</c> typically indicates success.</returns>
    public int SetDeviceValue(uint deviceId, int value, string? logName = null)
    {
        ThrowIfDisposed();

        var arguments = new byte[8];
        BitConverter.GetBytes(deviceId).CopyTo(arguments, 0);
        BitConverter.GetBytes((uint)value).CopyTo(arguments, 4);

        var reply = InvokeMethod(WriteMethodId, arguments);
        var result = BitConverter.ToInt32(reply, 0);

        if (!string.IsNullOrWhiteSpace(logName))
        {
            _logger.LogInformation("{Name} set to {Value}. Result={Result}", logName, value, result == 1 ? "OK" : result);
        }

        return result;
    }

    /// <summary>
    /// Reads an ASUS ACPI value.
    /// </summary>
    /// <param name="deviceId">The ASUS ACPI device identifier.</param>
    /// <param name="logName">An optional friendly name used in log messages.</param>
    /// <returns>The value returned by the driver after the ASUS specific offset adjustment.</returns>
    public int GetDeviceValue(uint deviceId, string? logName = null)
    {
        ThrowIfDisposed();

        var arguments = new byte[8];
        BitConverter.GetBytes(deviceId).CopyTo(arguments, 0);

        var reply = InvokeMethod(ReadMethodId, arguments);
        var result = BitConverter.ToInt32(reply, 0) - ReadResultOffset;

        if (!string.IsNullOrWhiteSpace(logName))
        {
            _logger.LogInformation("{Name} read returned {Result}.", logName, result);
        }

        return result;
    }

    /// <summary>
    /// Releases the unmanaged ASUS device handle.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    ~AsusAcpi()
    {
        Dispose(disposing: false);
    }

    /// <summary>
    /// Calls an ASUS ACPI method through <c>DeviceIoControl</c>.
    /// </summary>
    private byte[] InvokeMethod(uint methodId, byte[] arguments)
    {
        ThrowIfDisposed();

        var requestBuffer = new byte[RequestHeaderSize + arguments.Length];
        var responseBuffer = new byte[OutputBufferSize];

        BitConverter.GetBytes(methodId).CopyTo(requestBuffer, 0);
        BitConverter.GetBytes((uint)arguments.Length).CopyTo(requestBuffer, 4);
        Array.Copy(arguments, 0, requestBuffer, RequestHeaderSize, arguments.Length);

        uint bytesReturned = 0;
        var succeeded = DeviceIoControl(
            _deviceHandle,
            AsusAcpiIoControlCode,
            requestBuffer,
            (uint)requestBuffer.Length,
            responseBuffer,
            (uint)responseBuffer.Length,
            ref bytesReturned,
            IntPtr.Zero);

        if (!succeeded)
        {
            throw new InvalidOperationException($"DeviceIoControl failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        return responseBuffer;
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (_deviceHandle != IntPtr.Zero && _deviceHandle != InvalidHandle)
        {
            CloseHandle(_deviceHandle);
            _deviceHandle = IntPtr.Zero;
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

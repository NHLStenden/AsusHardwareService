using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace AsusHardwareService.Asus.Acpi;

/// <summary>
/// Low-level client for the ASUS ACPI device exposed as <c>\\.\ATKACPI</c>.
/// </summary>
internal sealed class AsusAcpiClient : IDisposable
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

    private readonly ILogger<AsusAcpiClient> _logger;
    private readonly SafeFileHandle _deviceHandle;
    private bool _disposed;

    /// <summary>Initializes a client and opens the ASUS ACPI device handle.</summary>
    public AsusAcpiClient(ILogger<AsusAcpiClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deviceHandle = CreateFile(
            DevicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        IsConnected = !_deviceHandle.IsInvalid;
        if (!IsConnected)
        {
            _logger.LogError("Cannot open {DevicePath}. Win32 error {Error}.", DevicePath, Marshal.GetLastWin32Error());
        }
    }

    /// <summary>Gets whether the ASUS ACPI device was opened successfully.</summary>
    public bool IsConnected { get; }

    /// <summary>Writes an integer value to an ASUS ACPI device identifier.</summary>
    public int WriteDeviceValue(uint deviceId, int value, string? diagnosticName = null)
    {
        ThrowIfDisposed();
        Span<byte> arguments = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(arguments, deviceId);
        BinaryPrimitives.WriteUInt32LittleEndian(arguments[4..], unchecked((uint)value));

        var reply = InvokeMethod(WriteMethodId, arguments);
        var result = BinaryPrimitives.ReadInt32LittleEndian(reply);
        if (!string.IsNullOrWhiteSpace(diagnosticName))
        {
            _logger.LogInformation(
                "{DeviceName} set to {Value}. Result={Result}",
                diagnosticName,
                value,
                result == 1 ? "OK" : result);
        }

        return result;
    }

    /// <summary>Reads the raw ASUS ACPI value without the standard firmware offset adjustment.</summary>
    public int ReadRawDeviceValue(uint deviceId, string? diagnosticName = null)
    {
        var result = ReadDriverValue(deviceId);
        if (!string.IsNullOrWhiteSpace(diagnosticName))
        {
            _logger.LogInformation("{DeviceName} raw read returned {Result}.", diagnosticName, result);
        }

        return result;
    }

    /// <summary>Reads an ASUS ACPI value and removes the standard ASUS firmware result offset.</summary>
    public int ReadDeviceValue(uint deviceId, string? diagnosticName = null)
    {
        var result = ReadDriverValue(deviceId) - ReadResultOffset;
        if (!string.IsNullOrWhiteSpace(diagnosticName))
        {
            _logger.LogInformation("{DeviceName} read returned {Result}.", diagnosticName, result);
        }

        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _deviceHandle.Dispose();
        _disposed = true;
    }

    private int ReadDriverValue(uint deviceId)
    {
        ThrowIfDisposed();
        Span<byte> arguments = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(arguments, deviceId);
        var reply = InvokeMethod(ReadMethodId, arguments);
        return BinaryPrimitives.ReadInt32LittleEndian(reply);
    }

    private byte[] InvokeMethod(uint methodId, ReadOnlySpan<byte> arguments)
    {
        ThrowIfDisposed();
        var requestBuffer = new byte[RequestHeaderSize + arguments.Length];
        var responseBuffer = new byte[OutputBufferSize];

        BinaryPrimitives.WriteUInt32LittleEndian(requestBuffer, methodId);
        BinaryPrimitives.WriteUInt32LittleEndian(requestBuffer.AsSpan(4), (uint)arguments.Length);
        arguments.CopyTo(requestBuffer.AsSpan(RequestHeaderSize));

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

        if (bytesReturned < sizeof(int))
        {
            throw new InvalidOperationException(
                $"DeviceIoControl returned an incomplete response. BytesReturned={bytesReturned}.");
        }

        return responseBuffer;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        ref uint lpBytesReturned,
        IntPtr lpOverlapped);
}

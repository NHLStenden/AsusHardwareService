using System.IO.Pipes;
using System.Text.Json;
using AsusHardwareService.Settings;

namespace AsusHardwareService.Presentation.Settings;

/// <summary>Interactive-session client for the hardware settings API.</summary>
internal sealed class HardwareSettingsClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Reads the current hardware settings from the service.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The service response containing the current settings.</returns>
    public Task<HardwareSettingsResponse> ReadAsync(CancellationToken cancellationToken = default) =>
        SendAsync(
            new HardwareSettingsRequest(
                HardwareSettingsProtocol.Version,
                HardwareSettingsOperation.Read),
            cancellationToken);

    /// <summary>Sends a partial hardware-settings update to the service.</summary>
    /// <param name="patch">The settings properties to update.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The service response containing authoritative post-update state.</returns>
    public Task<HardwareSettingsResponse> UpdateAsync(
        HardwareSettingsPatch patch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return SendAsync(
            new HardwareSettingsRequest(
                HardwareSettingsProtocol.Version,
                HardwareSettingsOperation.Update,
                patch),
            cancellationToken);
    }

    private static async Task<HardwareSettingsResponse> SendAsync(
        HardwareSettingsRequest request,
        CancellationToken cancellationToken)
    {
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(ConnectTimeout);

        using var pipe = new NamedPipeClientStream(
            ".",
            HardwareSettingsProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false);

        // Use separate connection and operation timeouts for hardware changes.
        using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationTimeout.CancelAfter(OperationTimeout);

        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var requestJson = JsonSerializer.Serialize(request, HardwareSettingsProtocol.JsonOptions);
        await writer.WriteLineAsync(requestJson.AsMemory(), operationTimeout.Token).ConfigureAwait(false);

        var line = await reader.ReadLineAsync(operationTimeout.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line) || line.Length > HardwareSettingsProtocol.MaximumMessageCharacters)
        {
            throw new IOException("The hardware settings service returned an invalid response.");
        }

        var response = JsonSerializer.Deserialize<HardwareSettingsResponse>(
            line,
            HardwareSettingsProtocol.JsonOptions);
        if (response is null || response.ProtocolVersion != HardwareSettingsProtocol.Version)
        {
            throw new IOException("The hardware settings service returned an unsupported response.");
        }

        return response;
    }
}

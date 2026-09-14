using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AsusHardwareService.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AsusHardwareService.Service;

/// <summary>Exposes the service-owned hardware settings API to interactive local-session presentation.</summary>
internal sealed class HardwareSettingsPipeServer : BackgroundService
{
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<HardwareSettingsPipeServer> _logger;
    private readonly HardwareSettingsCoordinator _settings;

    /// <summary>Initializes the local hardware-settings endpoint.</summary>
    public HardwareSettingsPipeServer(
        ILogger<HardwareSettingsPipeServer> logger,
        HardwareSettingsCoordinator settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await HandleConnectionAsync(pipe, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Hardware settings pipe failed; accepting a new client.");
                try
                {
                    await Task.Delay(250, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(RequestReadTimeout);

        string? line;
        try
        {
            line = await ReadBoundedLineAsync(reader, requestTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

        HardwareSettingsResponse response;
        if (string.IsNullOrWhiteSpace(line) || line.Length > HardwareSettingsProtocol.MaximumMessageCharacters)
        {
            response = CreateProtocolFailure("Invalid hardware settings request.");
        }
        else
        {
            response = await DispatchAsync(line, cancellationToken).ConfigureAwait(false);
        }

        var responseJson = JsonSerializer.Serialize(response, HardwareSettingsProtocol.JsonOptions);
        await writer.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadBoundedLineAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        var line = new StringBuilder();
        while (line.Length <= HardwareSettingsProtocol.MaximumMessageCharacters)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return line.Length == 0 ? null : line.ToString();
            }

            var span = buffer.AsSpan(0, read);
            var newline = span.IndexOf('\n');
            var contentLength = newline >= 0 ? newline : span.Length;
            if (contentLength > 0 && span[contentLength - 1] == '\r')
            {
                contentLength--;
            }

            line.Append(span[..contentLength]);
            if (line.Length > HardwareSettingsProtocol.MaximumMessageCharacters)
            {
                return line.ToString();
            }

            if (newline >= 0)
            {
                return line.ToString();
            }
        }

        return line.ToString();
    }

    private async Task<HardwareSettingsResponse> DispatchAsync(
        string json,
        CancellationToken cancellationToken)
    {
        HardwareSettingsRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<HardwareSettingsRequest>(
                json,
                HardwareSettingsProtocol.JsonOptions);
        }
        catch (JsonException)
        {
            return CreateProtocolFailure("Invalid hardware settings request.");
        }

        if (request is null || request.ProtocolVersion != HardwareSettingsProtocol.Version)
        {
            return CreateProtocolFailure("Unsupported hardware settings protocol version.");
        }

        return request.Operation switch
        {
            HardwareSettingsOperation.Read => new HardwareSettingsResponse(
                HardwareSettingsProtocol.Version,
                true,
                _settings.GetSnapshot()),

            HardwareSettingsOperation.Update when request.Changes is not null =>
                ToResponse(await _settings.UpdateAsync(request.Changes, cancellationToken).ConfigureAwait(false)),

            _ => CreateProtocolFailure("Unsupported hardware settings operation."),
        };
    }

    private static HardwareSettingsResponse ToResponse(HardwareSettingsUpdateResult result) =>
        new(HardwareSettingsProtocol.Version, result.Success, result.Settings, result.Error);

    private HardwareSettingsResponse CreateProtocolFailure(string error) =>
        new(HardwareSettingsProtocol.Version, false, _settings.GetSnapshot(), error);

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            HardwareSettingsProtocol.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            security);
    }
}

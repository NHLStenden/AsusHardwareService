namespace AsusHardwareService;

/// <summary>
/// Publishes hardware state updates without coupling service orchestration to a presentation transport.
/// </summary>
public interface IHardwareStatusPublisher
{
    /// <summary>
    /// Publishes a hardware state update.
    /// </summary>
    /// <param name="status">The hardware state to publish.</param>
    void Publish(HardwareStatus status);
}

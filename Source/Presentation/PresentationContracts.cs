namespace AsusHardwareService.Presentation;

/// <summary>Publishes a hardware state change to the interactive-user presentation.</summary>
internal interface IHardwareStatusPublisher
{
    /// <summary>Publishes one hardware state update.</summary>
    void Publish(HardwareStatus status);
}

/// <summary>Controls resident on-screen-display instances owned by the service.</summary>
internal interface IOnScreenDisplayLifecycle
{
    /// <summary>Requests that the OSD running in the specified Windows session exits.</summary>
    void StopInSession(int sessionId);
}

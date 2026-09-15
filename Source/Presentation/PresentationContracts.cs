namespace AsusHardwareService.Presentation;

/// <summary>Publishes a hardware state change to the user interface.</summary>
internal interface IHardwareStatusPublisher
{
    /// <summary>Publishes a hardware state update.</summary>
    void Publish(HardwareStatus status);
}

/// <summary>Controls on-screen display instances.</summary>
internal interface IOnScreenDisplayLifecycle
{
    /// <summary>Requests that the OSD running in the specified Windows session exits.</summary>
    void StopInSession(int sessionId);
}

/// <summary>Shows hardware settings in the active interactive session.</summary>
internal interface IHardwareSettingsPresenter
{
    /// <summary>Shows or reactivates the hardware-settings fly-out.</summary>
    void Show();
}

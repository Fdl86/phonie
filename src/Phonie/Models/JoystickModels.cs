namespace Phonie.Models;

public sealed record JoystickDeviceInfo(
    uint Id,
    string Key,
    string Name,
    uint ButtonCount)
{
    public override string ToString() => $"{this.Name} ({this.ButtonCount} boutons)";
}

public sealed record JoystickButtonEvent(
    JoystickDeviceInfo Device,
    int ButtonNumber,
    bool IsPressed);

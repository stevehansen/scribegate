namespace Scribegate.Core.Entities;

public class SystemSetting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class SystemSettingKeys
{
    public const string RegistrationEnabled = "registration.enabled";
    public const string EmailValidationRequired = "registration.email_validation";
    public const string InstanceName = "instance.name";
}

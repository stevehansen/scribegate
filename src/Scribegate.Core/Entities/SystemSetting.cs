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
    public const string RequireTos = "registration.require_tos";
    public const string AccountAgeGateHours = "account.age_gate_hours";
    public const string TosUrl = "registration.tos_url"; // URL to Terms of Service page
    public const string PrivacyUrl = "registration.privacy_url"; // URL to Privacy Policy page
    public const string ContentScanningNotice = "instance.content_scanning_notice"; // "true" if content may be scanned for abuse/safety

    // Tier system settings
    public const string TierMode = "instance.tier_mode"; // "none" (default) or "enforced"
    public const string DefaultTier = "instance.default_tier"; // "free" (default) or "paid"

    // Free tier limits (0 = unlimited)
    public const string FreeTierMaxRepositories = "tier.free.max_repositories";
    public const string FreeTierMaxDocumentsPerRepo = "tier.free.max_documents_per_repo";
    public const string FreeTierMaxStorageMb = "tier.free.max_storage_mb";
    public const string FreeTierMaxApiTokens = "tier.free.max_api_tokens";
    public const string FreeTierMaxMembersPerRepo = "tier.free.max_members_per_repo";

    // SSO/OIDC settings
    public const string OidcEnabled = "oidc.enabled"; // "false" (default) or "true"
    public const string OidcAuthority = "oidc.authority"; // e.g., "https://accounts.google.com"
    public const string OidcClientId = "oidc.client_id";
    public const string OidcClientSecret = "oidc.client_secret"; // stored encrypted or via env var
    public const string OidcDisplayName = "oidc.display_name"; // e.g., "Google", "Azure AD"
    public const string OidcAutoProvision = "oidc.auto_provision"; // "true" (default) or "false"

    // Email/SMTP settings
    public const string SmtpEnabled = "smtp.enabled"; // "false" (default) or "true"
    public const string SmtpHost = "smtp.host";
    public const string SmtpPort = "smtp.port"; // default "587"
    public const string SmtpUsername = "smtp.username";
    public const string SmtpPassword = "smtp.password";
    public const string SmtpFromAddress = "smtp.from_address";
    public const string SmtpFromName = "smtp.from_name";
    public const string SmtpUseSsl = "smtp.use_ssl"; // "true" (default) or "false"

    // Paid tier limits (0 = unlimited)
    public const string PaidTierMaxRepositories = "tier.paid.max_repositories";
    public const string PaidTierMaxDocumentsPerRepo = "tier.paid.max_documents_per_repo";
    public const string PaidTierMaxStorageMb = "tier.paid.max_storage_mb";
    public const string PaidTierMaxApiTokens = "tier.paid.max_api_tokens";
    public const string PaidTierMaxMembersPerRepo = "tier.paid.max_members_per_repo";
}

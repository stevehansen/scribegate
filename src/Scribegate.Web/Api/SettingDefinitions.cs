using Scribegate.Core.Entities;

namespace Scribegate.Web.Api;

public sealed record SettingDefinition(
    string Key,
    string Group,
    string Label,
    string Type,
    string Description,
    string Default = "",
    string[]? Choices = null);

public static class SettingDefinitions
{
    public const string GroupInstance = "Instance";
    public const string GroupRegistration = "Registration";
    public const string GroupAccount = "Accounts";
    public const string GroupOidc = "SSO / OIDC";
    public const string GroupSmtp = "Email (SMTP)";
    public const string GroupFreeTier = "Free tier limits";
    public const string GroupPaidTier = "Paid tier limits";

    public static readonly IReadOnlyList<SettingDefinition> All = new SettingDefinition[]
    {
        // Instance
        new(SystemSettingKeys.InstanceName, GroupInstance, "Instance name", "string",
            "Shown in the header and email subject lines.", "Scribegate"),
        new(SystemSettingKeys.TierMode, GroupInstance, "Tier mode", "enum",
            "How tier limits are applied. 'none' disables all quotas (good for self-hosted).",
            "none", new[] { "none", "enforced" }),
        new(SystemSettingKeys.DefaultTier, GroupInstance, "Default tier", "enum",
            "Tier assigned to newly registered users.", "free", new[] { "free", "paid" }),
        new(SystemSettingKeys.ContentScanningNotice, GroupInstance, "Content scanning notice", "bool",
            "Advertise that content may be scanned for abuse/safety.", "false"),

        // Registration
        new(SystemSettingKeys.RegistrationEnabled, GroupRegistration, "Registration enabled", "bool",
            "Allow new users to sign up.", "true"),
        new(SystemSettingKeys.EmailValidationRequired, GroupRegistration, "Require email validation", "bool",
            "Users must verify their email before they can sign in.", "false"),
        new(SystemSettingKeys.RequireTos, GroupRegistration, "Require Terms acceptance", "bool",
            "Users must accept the ToS during registration.", "true"),
        new(SystemSettingKeys.TosUrl, GroupRegistration, "Terms of Service URL", "string",
            "Link shown on the registration form."),
        new(SystemSettingKeys.PrivacyUrl, GroupRegistration, "Privacy Policy URL", "string",
            "Link shown on the registration form."),

        // Accounts
        new(SystemSettingKeys.AccountAgeGateHours, GroupAccount, "Account age gate (hours)", "number",
            "Hours a new account must exist before posting proposals/comments. 0 disables.", "24"),

        // OIDC
        new(SystemSettingKeys.OidcEnabled, GroupOidc, "OIDC enabled", "bool",
            "Turns on Sign-In-With-OIDC. Requires authority + client_id + client_secret.", "false"),
        new(SystemSettingKeys.OidcDisplayName, GroupOidc, "Display name", "string",
            "Label on the sign-in button (e.g. 'Google', 'Azure AD')."),
        new(SystemSettingKeys.OidcAuthority, GroupOidc, "Authority URL", "string",
            "OIDC provider base URL (e.g. https://accounts.google.com)."),
        new(SystemSettingKeys.OidcClientId, GroupOidc, "Client ID", "string",
            "OAuth client id."),
        new(SystemSettingKeys.OidcClientSecret, GroupOidc, "Client secret", "secret",
            "OAuth client secret. Stored as-is; use env vars if you prefer."),
        new(SystemSettingKeys.OidcAutoProvision, GroupOidc, "Auto-provision accounts", "bool",
            "Create local accounts on first OIDC login automatically.", "true"),

        // SMTP
        new(SystemSettingKeys.SmtpEnabled, GroupSmtp, "SMTP enabled", "bool",
            "Master switch for outbound email. All other SMTP settings are ignored when off.", "false"),
        new(SystemSettingKeys.SmtpHost, GroupSmtp, "Host", "string",
            "SMTP server hostname (e.g. smtp.postmarkapp.com, smtp.gmail.com)."),
        new(SystemSettingKeys.SmtpPort, GroupSmtp, "Port", "number",
            "Typically 587 (STARTTLS) or 465 (implicit TLS).", "587"),
        new(SystemSettingKeys.SmtpUsername, GroupSmtp, "Username", "string",
            "SMTP auth username. Leave blank for anonymous relay."),
        new(SystemSettingKeys.SmtpPassword, GroupSmtp, "Password", "secret",
            "SMTP auth password or app-specific token."),
        new(SystemSettingKeys.SmtpUseSsl, GroupSmtp, "Use SSL/TLS", "bool",
            "Encrypt the SMTP connection. Recommended true.", "true"),
        new(SystemSettingKeys.SmtpFromAddress, GroupSmtp, "From address", "string",
            "Required. The address emails are sent from (e.g. noreply@scribegate.dev)."),
        new(SystemSettingKeys.SmtpFromName, GroupSmtp, "From name", "string",
            "Display name on outbound email.", "Scribegate"),

        // Free tier
        new(SystemSettingKeys.FreeTierMaxRepositories, GroupFreeTier, "Max repositories", "number",
            "0 = unlimited.", "3"),
        new(SystemSettingKeys.FreeTierMaxDocumentsPerRepo, GroupFreeTier, "Max documents per repo", "number",
            "0 = unlimited.", "20"),
        new(SystemSettingKeys.FreeTierMaxStorageMb, GroupFreeTier, "Max storage (MB)", "number",
            "0 = unlimited.", "50"),
        new(SystemSettingKeys.FreeTierMaxApiTokens, GroupFreeTier, "Max API tokens", "number",
            "0 = unlimited.", "2"),
        new(SystemSettingKeys.FreeTierMaxMembersPerRepo, GroupFreeTier, "Max members per repo", "number",
            "0 = unlimited.", "3"),

        // Paid tier
        new(SystemSettingKeys.PaidTierMaxRepositories, GroupPaidTier, "Max repositories", "number",
            "0 = unlimited.", "0"),
        new(SystemSettingKeys.PaidTierMaxDocumentsPerRepo, GroupPaidTier, "Max documents per repo", "number",
            "0 = unlimited.", "0"),
        new(SystemSettingKeys.PaidTierMaxStorageMb, GroupPaidTier, "Max storage (MB)", "number",
            "0 = unlimited.", "0"),
        new(SystemSettingKeys.PaidTierMaxApiTokens, GroupPaidTier, "Max API tokens", "number",
            "0 = unlimited.", "0"),
        new(SystemSettingKeys.PaidTierMaxMembersPerRepo, GroupPaidTier, "Max members per repo", "number",
            "0 = unlimited.", "0"),
    };

    public static readonly IReadOnlyDictionary<string, SettingDefinition> ByKey =
        All.ToDictionary(d => d.Key);
}

namespace N24DataRelay.Core.Models;

/// <summary>Well-known event type string constants for audit log entries.</summary>
public static class AuditEventTypes
{
    public const string UserLogin              = "UserLogin";
    public const string UserLoginFailed        = "UserLoginFailed";
    public const string UserLogout             = "UserLogout";
    public const string UserRegistered         = "UserRegistered";
    public const string UserApproved           = "UserApproved";
    public const string UserRejected           = "UserRejected";
    public const string UserRevoked            = "UserRevoked";
    public const string UserDeleted            = "UserDeleted";
    public const string RoleGranted            = "RoleGranted";
    public const string RoleRevoked            = "RoleRevoked";
    public const string PasswordChanged        = "PasswordChanged";
    public const string PasswordResetRequested = "PasswordResetRequested";
    public const string PasswordReset          = "PasswordReset";
    public const string ConfigSaved            = "ConfigSaved";
    public const string TransferFailed         = "TransferFailed";
    public const string ServiceStarted         = "ServiceStarted";
    public const string ServiceStopped         = "ServiceStopped";
}

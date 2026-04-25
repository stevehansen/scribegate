namespace Scribegate.Core;

/// <summary>
/// Suppresses the <c>SCB0001</c> analyzer for a specific symbol that legitimately
/// needs <c>ScribegateDbContext</c> outside the data layer (transaction-owning
/// command services, host-level migration / health-check bootstrap, etc.).
/// </summary>
/// <remarks>
/// The <c>Reason</c> is free-text and must be reviewed at code-review time.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method
    | AttributeTargets.Constructor
    | AttributeTargets.Class
    | AttributeTargets.Struct
    | AttributeTargets.Parameter
    | AttributeTargets.Property
    | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AllowsDbContextAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

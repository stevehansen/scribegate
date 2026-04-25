using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scribegate.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DbContextOutsideDataAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SCB0001";

    private const string DbContextFullName = "Scribegate.Data.ScribegateDbContext";
    private const string AllowAttributeFullName = "Scribegate.Core.AllowsDbContextAttribute";
    private const string DataAssemblyName = "Scribegate.Data";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Do not use ScribegateDbContext outside Scribegate.Data",
        messageFormat: "'{0}' takes a dependency on ScribegateDbContext outside Scribegate.Data — depend on a Core store interface instead, or annotate with [AllowsDbContext(reason)]",
        category: "Scribegate.Architecture",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Direct DbContext dependencies leak EF Core through the layer boundary and block the RavenDB-adapter goal in CLAUDE.md. " +
                     "Move the query into a store under Scribegate.Core.Stores. The escape hatch [AllowsDbContext(\"reason\")] exists " +
                     "for transaction-owning command services and host-level bootstrap code — review carefully.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var dbContextType = start.Compilation.GetTypeByMetadataName(DbContextFullName);
            if (dbContextType is null)
                return; // Project doesn't reference Scribegate.Data — nothing to flag.

            // Don't flag inside Scribegate.Data itself.
            if (start.Compilation.AssemblyName == DataAssemblyName)
                return;

            var allowAttribute = start.Compilation.GetTypeByMetadataName(AllowAttributeFullName);

            start.RegisterSymbolAction(ctx => AnalyzeParameter(ctx, dbContextType, allowAttribute), SymbolKind.Parameter);
            start.RegisterSymbolAction(ctx => AnalyzeField(ctx, dbContextType, allowAttribute), SymbolKind.Field);
            start.RegisterSymbolAction(ctx => AnalyzeProperty(ctx, dbContextType, allowAttribute), SymbolKind.Property);
            start.RegisterOperationAction(ctx => AnalyzeInvocation(ctx, dbContextType, allowAttribute), OperationKind.Invocation);
        });
    }

    private static void AnalyzeParameter(SymbolAnalysisContext ctx, INamedTypeSymbol dbContext, INamedTypeSymbol? allow)
    {
        var p = (IParameterSymbol)ctx.Symbol;
        if (!IsDbContext(p.Type, dbContext)) return;
        if (IsAllowed(p, allow)) return;
        if (IsAllowed(p.ContainingSymbol, allow)) return;

        var owner = p.ContainingSymbol;
        ReportOn(ctx, owner, p.Locations);
    }

    private static void AnalyzeField(SymbolAnalysisContext ctx, INamedTypeSymbol dbContext, INamedTypeSymbol? allow)
    {
        var f = (IFieldSymbol)ctx.Symbol;
        if (!IsDbContext(f.Type, dbContext)) return;
        if (IsAllowed(f, allow)) return;
        if (IsAllowed(f.ContainingType, allow)) return;
        ReportOn(ctx, f, f.Locations);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext ctx, INamedTypeSymbol dbContext, INamedTypeSymbol? allow)
    {
        var p = (IPropertySymbol)ctx.Symbol;
        if (!IsDbContext(p.Type, dbContext)) return;
        if (IsAllowed(p, allow)) return;
        if (IsAllowed(p.ContainingType, allow)) return;
        ReportOn(ctx, p, p.Locations);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext ctx, INamedTypeSymbol dbContext, INamedTypeSymbol? allow)
    {
        var op = (Microsoft.CodeAnalysis.Operations.IInvocationOperation)ctx.Operation;
        var method = op.TargetMethod;

        // Catch GetService<ScribegateDbContext>() / GetRequiredService<ScribegateDbContext>().
        if (method.TypeArguments.Length != 1) return;
        var arg = method.TypeArguments[0] as INamedTypeSymbol;
        if (!IsDbContext(arg, dbContext)) return;

        var name = method.Name;
        if (name != "GetService" && name != "GetRequiredService") return;

        var enclosing = ctx.ContainingSymbol;
        if (IsAllowed(enclosing, allow)) return;
        if (enclosing.ContainingType is { } container && IsAllowed(container, allow)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Rule, op.Syntax.GetLocation(), enclosing.Name));
    }

    private static bool IsDbContext(ITypeSymbol? type, INamedTypeSymbol dbContext)
    {
        if (type is null) return false;
        return SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, dbContext);
    }

    private static bool IsAllowed(ISymbol? symbol, INamedTypeSymbol? allowAttribute)
    {
        if (allowAttribute is null || symbol is null) return false;
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            foreach (var attr in current.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, allowAttribute))
                    return true;
            }
        }
        return false;
    }

    private static void ReportOn(SymbolAnalysisContext ctx, ISymbol owner, ImmutableArray<Location> locations)
    {
        var loc = locations.IsEmpty ? Location.None : locations[0];
        ctx.ReportDiagnostic(Diagnostic.Create(Rule, loc, owner.Name));
    }
}

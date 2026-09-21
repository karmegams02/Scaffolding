// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.DotNet.Scaffolding.Core.Scaffolders;
using Microsoft.DotNet.Scaffolding.Core.Steps;
using Microsoft.DotNet.Scaffolding.Internal.Services;
using Microsoft.DotNet.Tools.Scaffold.AspNet.ScaffoldSteps.Settings;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Tools.Scaffold.AspNet.ScaffoldSteps;

internal sealed class IdentityEndpointsScaffolderStep(
    IFileSystem fileSystem,
    ILogger<IdentityEndpointsScaffolderStep> logger) : ScaffoldStep
{
    public override Task<bool> ExecuteAsync(ScaffolderContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Properties.TryGetValue(nameof(IdentityEndpointsSettings), out var settingsValue) ||
            settingsValue is not IdentityEndpointsSettings settings)
        {
            logger.LogError("Identity endpoints settings were not initialized.");
            return Task.FromResult(false);
        }

        try
        {
            string? updatedMapping = null;
            if (!string.IsNullOrEmpty(settings.MappingFilePath))
            {
                var mappingSource = fileSystem.ReadAllText(settings.MappingFilePath);
                updatedMapping = RewriteMapping(mappingSource, settings.GeneratedMethodName, settings.GeneratedNamespace);
                if (updatedMapping is null)
                {
                    logger.LogError("The validated MapIdentityApi invocation could not be rewritten.");
                    return Task.FromResult(false);
                }
            }

            var outputDirectory = Path.GetDirectoryName(settings.OutputPath)!;
            fileSystem.CreateDirectoryIfNotExists(outputDirectory);
            fileSystem.WriteAllText(settings.OutputPath, IdentityEndpointsSource.Render(settings));

            if (updatedMapping is not null)
            {
                fileSystem.WriteAllText(settings.MappingFilePath!, updatedMapping);
            }
            else
            {
                logger.LogInformation("No MapIdentityApi call was found. Add app.MapCustomIdentityApi<{UserClass}>() where the endpoints should be mapped.", settings.UserClassName);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not write the Identity endpoints output. Check file permissions and available disk space.");
            return Task.FromResult(false);
        }

        logger.LogInformation("Generated customizable Identity API endpoints at '{OutputPath}'.", settings.OutputPath);
        return Task.FromResult(true);
    }

    internal static string? RewriteMapping(string source, string generatedMethodName, string generatedNamespace)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => GetInvokedGenericName(invocation)?.Identifier.ValueText == "MapIdentityApi")
            .ToArray();
        if (invocations.Length != 1)
        {
            return null;
        }

        var invocation = invocations[0];
        var genericName = GetInvokedGenericName(invocation)!;
        var replacementName = genericName.WithIdentifier(SyntaxFactory.Identifier(genericName.Identifier.LeadingTrivia, generatedMethodName, genericName.Identifier.TrailingTrivia));
        var replacementExpression = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.WithName(replacementName),
            GenericNameSyntax => replacementName,
            _ => invocation.Expression
        };
        var updatedRoot = root.ReplaceNode(invocation, invocation.WithExpression(replacementExpression));

        if (!updatedRoot.Usings.Any(usingDirective => string.Equals(usingDirective.Name?.ToString(), generatedNamespace, StringComparison.Ordinal)))
        {
            var endOfLine = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(generatedNamespace))
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(endOfLine));
            updatedRoot = updatedRoot.AddUsings(usingDirective);
        }

        return updatedRoot.ToFullString();
    }

    private static GenericNameSyntax? GetInvokedGenericName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } => genericName,
        GenericNameSyntax genericName => genericName,
        _ => null
    };
}

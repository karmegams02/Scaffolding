// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.DotNet.Scaffolding.Core.Scaffolders;
using Microsoft.DotNet.Scaffolding.Core.Steps;
using Microsoft.DotNet.Scaffolding.Internal.Services;
using Microsoft.DotNet.Tools.Scaffold.AspNet.Common;
using Microsoft.DotNet.Tools.Scaffold.AspNet.ScaffoldSteps.Settings;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Tools.Scaffold.AspNet.ScaffoldSteps;

internal sealed class ValidateIdentityEndpointsStep(
    IFileSystem fileSystem,
    ILogger<ValidateIdentityEndpointsStep> logger) : ScaffoldStep
{
    private const string DefaultClassName = "CustomIdentityApiEndpointRouteBuilderExtensions";
    private const string DefaultMethodName = "MapCustomIdentityApi";
    private const string DefaultOutputDirectory = "Identity";

    public string? Project { get; set; }
    public string? UserClass { get; set; }
    public string? EndpointsClass { get; set; }
    public string? OutputDirectory { get; set; }
    public bool Overwrite { get; set; }

    public override async Task<bool> ExecuteAsync(ScaffolderContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Project) || !fileSystem.FileExists(Project))
        {
            logger.LogError("Missing or invalid {Option} option.", Constants.CliOptions.ProjectCliOption);
            return false;
        }

        var projectContents = fileSystem.ReadAllText(Project);
        if (!projectContents.Contains("Microsoft.NET.Sdk.Web", StringComparison.Ordinal))
        {
            logger.LogError("The selected project must use Microsoft.NET.Sdk.Web.");
            return false;
        }

        var projectInfo = ClassAnalyzers.GetProjectInfo(Project, logger);
        if (projectInfo.CodeService is null || projectInfo.LowestSupportedTargetFramework is null)
        {
            logger.LogError("The project does not target a supported ASP.NET Core framework.");
            return false;
        }

        context.SetSpecifiedTargetFramework(projectInfo.LowestSupportedTargetFramework);
        var documents = await projectInfo.CodeService.GetAllDocumentsAsync();
        var mappings = await FindInvocationsAsync(documents, "MapIdentityApi", cancellationToken);
        var customMappings = await FindInvocationsAsync(documents, DefaultMethodName, cancellationToken);
        var registrations = await FindInvocationsAsync(documents, "AddIdentityApiEndpoints", cancellationToken);

        if (mappings.Count > 1)
        {
            logger.LogError("Multiple MapIdentityApi calls were found. Leave one mapping or scaffold the endpoints manually.");
            return false;
        }

        if (customMappings.Count > 0)
        {
            logger.LogError("A MapCustomIdentityApi call already exists in the project.");
            return false;
        }

        var mappedUserClass = mappings.SingleOrDefault()?.TypeArgument;
        var requestedUserClass = string.IsNullOrWhiteSpace(UserClass) ? mappedUserClass : UserClass.Trim();
        if (string.IsNullOrWhiteSpace(requestedUserClass))
        {
            logger.LogError("The Identity user class could not be inferred. Specify {Option}.", Constants.CliOptions.UserClassOption);
            return false;
        }

        if (registrations.Count == 0)
        {
            logger.LogError("AddIdentityApiEndpoints<{UserClass}> was not found. Configure Identity API services before scaffolding endpoints.", requestedUserClass);
            return false;
        }

        var classes = await projectInfo.CodeService.GetAllClassSymbolsAsync();
        var matchingUserSymbols = classes.OfType<INamedTypeSymbol>().Where(symbol =>
            string.Equals(symbol.Name, GetSimpleTypeName(requestedUserClass), StringComparison.Ordinal) ||
            string.Equals(symbol.ToDisplayString(), requestedUserClass, StringComparison.Ordinal)).ToArray();
        var userSymbol = matchingUserSymbols.Length == 1 ? matchingUserSymbols[0] : null;
        if (userSymbol is null)
        {
            logger.LogError("Could not uniquely resolve Identity user class '{UserClass}' in the project. Specify its fully qualified name.", requestedUserClass);
            return false;
        }

        var userTypeName = userSymbol.ToDisplayString();
        if (!registrations.Any(registration => TypeNamesMatch(registration.TypeArgument, userTypeName, userSymbol.Name)))
        {
            logger.LogError("The configured Identity API user type does not match '{UserClass}'.", userTypeName);
            return false;
        }

        if (!string.IsNullOrEmpty(mappedUserClass) && !TypeNamesMatch(mappedUserClass, userTypeName, userSymbol.Name))
        {
            logger.LogError("MapIdentityApi uses '{MappedUserClass}', which does not match '{UserClass}'.", mappedUserClass, userTypeName);
            return false;
        }

        var generatedClassName = string.IsNullOrWhiteSpace(EndpointsClass) ? DefaultClassName : EndpointsClass.Trim();
        if (!SyntaxFacts.IsValidIdentifier(generatedClassName))
        {
            logger.LogError("'{ClassName}' is not a valid C# class name.", generatedClassName);
            return false;
        }

        var outputDirectory = string.IsNullOrWhiteSpace(OutputDirectory) ? DefaultOutputDirectory : OutputDirectory.Trim();
        if (Path.IsPathRooted(outputDirectory) || outputDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
        {
            logger.LogError("The output directory must be a project-relative path that does not contain '..'.");
            return false;
        }

        var projectDirectory = Path.GetDirectoryName(Project)!;
        var outputPath = Path.Combine(projectDirectory, outputDirectory, $"{generatedClassName}.cs");
        if (fileSystem.FileExists(outputPath) && !Overwrite)
        {
            logger.LogError("'{OutputPath}' already exists. Use --overwrite to replace it.", outputPath);
            return false;
        }

        var namespaceSegments = new[] { Path.GetFileNameWithoutExtension(Project) }
            .Concat(outputDirectory.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            .Select(SanitizeNamespaceSegment);
        var generatedNamespace = string.Join('.', namespaceSegments);

        context.Properties[nameof(IdentityEndpointsSettings)] = new IdentityEndpointsSettings
        {
            Project = Project,
            TargetFramework = projectInfo.LowestSupportedTargetFramework.Value,
            UserClassName = userSymbol.Name,
            UserClassNamespace = userSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : userSymbol.ContainingNamespace.ToDisplayString(),
            GeneratedClassName = generatedClassName,
            GeneratedMethodName = DefaultMethodName,
            GeneratedNamespace = generatedNamespace,
            OutputPath = outputPath,
            MappingFilePath = mappings.SingleOrDefault()?.Document.FilePath,
            Overwrite = Overwrite
        };

        return true;
    }

    private static async Task<List<IdentityInvocation>> FindInvocationsAsync(
        IEnumerable<Document> documents,
        string methodName,
        CancellationToken cancellationToken)
    {
        var matches = new List<IdentityInvocation>();
        foreach (var document in documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (root is null)
            {
                continue;
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } => genericName,
                    GenericNameSyntax genericName => genericName,
                    _ => null
                };

                if (name?.Identifier.ValueText == methodName && name.TypeArgumentList.Arguments.Count == 1)
                {
                    var methodSymbol = semanticModel?.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                    var typeArgument = methodSymbol?.TypeArguments.SingleOrDefault()?.ToDisplayString()
                        ?? name.TypeArgumentList.Arguments[0].ToString();
                    matches.Add(new IdentityInvocation(document, invocation, typeArgument));
                }
            }
        }

        return matches;
    }

    private static string GetSimpleTypeName(string typeName) => typeName.Split('.').Last();

    private static bool TypeNamesMatch(string candidate, string fullName, string simpleName) =>
        string.Equals(candidate, fullName, StringComparison.Ordinal) ||
        string.Equals(candidate, simpleName, StringComparison.Ordinal);

    private static string SanitizeNamespaceSegment(string value)
    {
        var characters = value.Select((character, index) =>
            index == 0
                ? SyntaxFacts.IsIdentifierStartCharacter(character) ? character : '_'
                : SyntaxFacts.IsIdentifierPartCharacter(character) ? character : '_')
            .ToArray();
        var identifier = new string(characters);
        return SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : $"@{identifier}";
    }

    private sealed record IdentityInvocation(Document Document, InvocationExpressionSyntax Invocation, string TypeArgument);
}

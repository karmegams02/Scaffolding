// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.DotNet.Scaffolding.Core.Builder;
using Microsoft.DotNet.Scaffolding.Core.Model;
using Microsoft.DotNet.Tools.Scaffold.AspNet;
using Microsoft.DotNet.Tools.Scaffold.AspNet.ScaffoldSteps;
using Microsoft.DotNet.Tools.Scaffold.AspNet.ScaffoldSteps.Settings;
using Moq;
using System.Linq;
using Xunit;

namespace Microsoft.DotNet.Tools.Scaffold.Tests.AspNet.ScaffoldSteps;

public class IdentityEndpointsScaffolderStepTests
{
    [Fact]
    public void GetScaffoldSteps_ContainsIdentityEndpointsSteps()
    {
        var service = new AspNetCommandService(new Mock<IScaffoldRunnerBuilder>().Object);

        var stepTypes = service.GetScaffoldSteps();

        Assert.Contains(typeof(ValidateIdentityEndpointsStep), stepTypes);
        Assert.Contains(typeof(IdentityEndpointsScaffolderStep), stepTypes);
    }

    [Fact]
    public void RewriteMapping_PreservesReceiverTypeAndConventions()
    {
        const string source = """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapGroup("/identity")
                .MapIdentityApi<ApplicationUser>()
                .RequireAuthorization("accounts");
            app.Run();
            """;

        var result = IdentityEndpointsScaffolderStep.RewriteMapping(
            source,
            "MapCustomIdentityApi",
            "MyApp.Identity");

        Assert.NotNull(result);
        Assert.Contains("app.MapGroup(\"/identity\")", result);
        Assert.Contains(".MapCustomIdentityApi<ApplicationUser>()", result);
        Assert.Contains(".RequireAuthorization(\"accounts\")", result);
        Assert.Contains("using MyApp.Identity;", result);
        Assert.DoesNotContain("MapIdentityApi<ApplicationUser>", result);
    }

    [Fact]
    public void RewriteMapping_ReturnsNullForMultipleMappings()
    {
        const string source = """
            app.MapIdentityApi<ApplicationUser>();
            app.MapGroup("/other").MapIdentityApi<ApplicationUser>();
            """;

        Assert.Null(IdentityEndpointsScaffolderStep.RewriteMapping(source, "MapCustomIdentityApi", "MyApp.Identity"));
    }

    [Fact]
    public void Render_ContainsCompleteEndpointSetAndValidSyntax()
    {
        var settings = new IdentityEndpointsSettings
        {
            Project = "MyApp.csproj",
            TargetFramework = TargetFramework.Net10,
            UserClassName = "ApplicationUser",
            UserClassNamespace = "MyApp.Data",
            GeneratedClassName = "CustomIdentityApiEndpointRouteBuilderExtensions",
            GeneratedMethodName = "MapCustomIdentityApi",
            GeneratedNamespace = "MyApp.Identity",
            OutputPath = "Identity/CustomIdentityApiEndpointRouteBuilderExtensions.cs",
            Overwrite = false
        };

        var source = IdentityEndpointsSource.Render(settings);
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        Assert.DoesNotContain(syntaxTree.GetDiagnostics(), diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("MapCustomIdentityApi<TUser>", source);
        Assert.Contains("MapPost(\"/register\"", source);
        Assert.Contains("MapPost(\"/login\"", source);
        Assert.Contains("MapPost(\"/refresh\"", source);
        Assert.Contains("MapGet(\"/confirmEmail\"", source);
        Assert.Contains("MapPost(\"/resendConfirmationEmail\"", source);
        Assert.Contains("MapPost(\"/forgotPassword\"", source);
        Assert.Contains("MapPost(\"/resetPassword\"", source);
        Assert.Contains("MapGroup(\"/manage\").RequireAuthorization()", source);
        Assert.Contains("MapPost(\"/2fa\"", source);
        Assert.Contains("MapGet(\"/info\"", source);
        Assert.Contains("MapPost(\"/info\"", source);
        Assert.DoesNotContain("Status501NotImplemented", source);
    }
}

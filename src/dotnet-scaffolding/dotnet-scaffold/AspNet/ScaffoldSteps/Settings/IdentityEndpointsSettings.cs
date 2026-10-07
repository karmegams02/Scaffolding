// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Scaffolding.Core.Model;

namespace Microsoft.DotNet.Tools.Scaffold.AspNet.ScaffoldSteps.Settings;

internal sealed class IdentityEndpointsSettings : BaseSettings
{
    public required TargetFramework TargetFramework { get; init; }
    public required string UserClassName { get; init; }
    public required string UserClassNamespace { get; init; }
    public required string GeneratedClassName { get; init; }
    public required string GeneratedMethodName { get; init; }
    public required string GeneratedNamespace { get; init; }
    public required string OutputPath { get; init; }
    public string? MappingFilePath { get; init; }
    public bool Overwrite { get; init; }
}

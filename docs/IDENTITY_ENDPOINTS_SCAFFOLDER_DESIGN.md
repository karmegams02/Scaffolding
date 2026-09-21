# ASP.NET Core Identity Endpoints Scaffolder Design

## Summary

Add a `dotnet scaffold aspnet identity-endpoints` scaffolder that copies the implementation of ASP.NET Core's `MapIdentityApi<TUser>` endpoints into an application. The generated code is owned by the application and can be changed to collect additional registration data, return application-specific account information, alter routes, or replace individual handlers.

The current implementation generates the complete endpoint set for supported target frameworks. It preserves the framework endpoint contract and security behavior represented by the source snapshot, renames the generated extension to avoid conflicts with the framework extension, and updates an existing `MapIdentityApi<TUser>` call when one can be identified safely.

This is a source customization feature. It is not a second Identity setup experience and does not replace the existing `identity` or `blazor-identity` scaffolders.

## Motivation

`MapIdentityApi<TUser>` offers a convenient set of endpoints, but its handlers are implemented inside the ASP.NET Core shared framework. Applications cannot directly customize common requirements such as:

- collecting additional fields during registration;
- returning application-specific profile, role, or claim data;
- changing the behavior of one endpoint;
- applying endpoint-specific metadata or authorization policies;
- removing endpoints that the application does not expose.

Copying the framework source manually is possible, but it is difficult to select the correct source for the target framework, rename internal implementation details safely, preserve security-sensitive behavior, and update application startup consistently.

The existing Identity UI scaffolders establish the expected model: framework-provided defaults remain the recommended starting point, while scaffolding transfers source ownership to the application when customization is required.

## Goals

- Generate an application-owned implementation equivalent to the supported `MapIdentityApi<TUser>` source snapshot.
- Preserve the routes, HTTP methods, request and response types, endpoint metadata, authorization requirements, and security behavior of the framework implementation at generation time.
- Support ASP.NET Core projects that already configure `AddIdentityApiEndpoints<TUser>` and map `MapIdentityApi<TUser>`.
- Preserve an existing route-group receiver, including prefixes and conventions.
- Produce readable code intended for customization.
- Prevent accidental overwrites and ambiguous endpoint registration.
- Use the existing `dotnet scaffold` command, validation, Roslyn, and scaffold-step patterns.

## Non-goals

- Provisioning an Identity database, user type, stores, authentication schemes, or email sender. The existing `identity` and `blazor-identity` scaffolders cover setup scenarios.
- Adding external-login endpoints. The framework Identity API endpoints do not provide that contract. Blazor Identity's `MapAdditionalIdentityEndpoints` continues to own its component support endpoints.
- Adding logout, account deletion, claims, or roles to the default generated contract. These are useful customizations, but adding them by default would no longer reproduce `MapIdentityApi<TUser>`.
- Acting as an OAuth 2.0 or OpenID Connect server. Identity bearer tokens remain proprietary tokens intended for simple first-party scenarios.
- Automatically merging later framework changes into previously generated code.
- Generating only a selected subset of endpoints in the first version.

## Proposed command

```text
dotnet scaffold aspnet identity-endpoints \
    --project <PROJECT> \
    [--user-class <USER_CLASS>] \
    [--name <EXTENSION_CLASS>] \
    [--output-dir <OUTPUT_DIRECTORY>] \
    [--overwrite]
```

### Options

| Option         | Required    | Default                                           | Description                                                                                                            |
| -------------- | ----------- | ------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `--project`    | Yes         | None                                              | ASP.NET Core project to modify.                                                                                        |
| `--user-class` | Conditional | Inferred from `MapIdentityApi<TUser>`             | Identity user type. Required when it cannot be inferred unambiguously. The interactive experience uses a class picker. |
| `--name`       | No          | `CustomIdentityApiEndpointRouteBuilderExtensions` | Generated extension class name. The generated method name remains `MapCustomIdentityApi`.                              |
| `--output-dir` | No          | `Identity`                                        | Project-relative output directory.                                                                                     |
| `--overwrite`  | No          | `false`                                           | Replace an existing generated source file. Local edits to that file are lost.                                          |

Examples:

```bash
dotnet scaffold aspnet identity-endpoints --project MyApp.csproj
```

```bash
dotnet scaffold aspnet identity-endpoints \
  --project MyApp.csproj \
  --user-class MyApp.Data.ApplicationUser \
  --output-dir Endpoints/Identity
```

The command name uses `identity-endpoints` to distinguish this source-only operation from the broader `identity` and `blazor-identity` setup commands.

## Preconditions and validation

The validation step must:

1. Confirm that the selected project uses `Microsoft.NET.Sdk.Web` and targets a supported ASP.NET Core target framework.
2. Resolve exactly one Identity user type. An explicit `--user-class` value takes precedence; otherwise infer it from an existing `MapIdentityApi<TUser>` invocation.
3. Confirm that the user type is a class and is available to the project.
4. Detect Identity API service registration, normally `AddIdentityApiEndpoints<TUser>`. If it is absent, stop with guidance rather than adding a potentially conflicting Identity configuration.
5. Find zero or one `MapIdentityApi<TUser>` invocation in application code.
6. Reject multiple mappings because replacing one automatically could leave duplicate routes or change an intentionally partitioned setup.
7. Reject any existing `MapCustomIdentityApi` mapping. `--overwrite` permits replacing the generated source file only; it does not create or replace a second mapping.
8. Reject an existing output file unless `--overwrite` is specified.

The implementation resolves invocation type arguments semantically when Roslyn can bind them and falls back to their source spelling. The selected user type must resolve uniquely to a class declared in the project, and the type used by `AddIdentityApiEndpoints<TUser>` and `MapIdentityApi<TUser>` must match it. A custom Identity user is not required to derive from `IdentityUser`; ASP.NET Core Identity supports arbitrary class-based user types when compatible stores are configured.

When no `MapIdentityApi<TUser>` invocation exists, the scaffolder generates the source file but does not guess where or under which route prefix to map it. It reports the exact mapping statement as a required next step:

```csharp
app.MapCustomIdentityApi<ApplicationUser>();
```

This is preferable to silently exposing account endpoints at the application root.

## Generated code

The scaffolder generates one primary file:

```text
Identity/CustomIdentityApiEndpointRouteBuilderExtensions.cs
```

The generated file:

- uses a namespace built from the project file name and selected output-directory segments, with invalid identifier characters sanitized;
- defines `MapCustomIdentityApi<TUser>(this IEndpointRouteBuilder endpoints)`;
- retains `where TUser : class, new()` and all framework-required dependencies;
- maps the complete framework endpoint set listed below;
- retains typed results and endpoint metadata;
- retains helper methods for validation problems, email links, and account information;
- contains a provenance header naming the target framework and stating that the file is application-owned, without `GeneratedCodeAttribute` so analyzers continue to inspect customized code.

The generated class and method are renamed instead of shadowing `Microsoft.AspNetCore.Routing.IdentityApiEndpointRouteBuilderExtensions.MapIdentityApi`. This avoids ambiguous extension-method resolution and makes the transfer from framework-owned to application-owned behavior visible in `Program.cs`.

### Endpoint contract

The initial generated contract matches the `MapIdentityApi<TUser>` implementation shipped for the selected target framework:

| Method | Route                      | Authorization |
| ------ | -------------------------- | ------------- |
| `POST` | `/register`                | Anonymous     |
| `POST` | `/login`                   | Anonymous     |
| `POST` | `/refresh`                 | Anonymous     |
| `GET`  | `/confirmEmail`            | Anonymous     |
| `POST` | `/resendConfirmationEmail` | Anonymous     |
| `POST` | `/forgotPassword`          | Anonymous     |
| `POST` | `/resetPassword`           | Anonymous     |
| `POST` | `/manage/2fa`              | Required      |
| `GET`  | `/manage/info`             | Required      |
| `POST` | `/manage/info`             | Required      |

The current source snapshot is shared by all supported target frameworks. The endpoint list and implementation may differ in future frameworks; the table describes the current baseline and is not a cross-version promise from the scaffolder.

### Program.cs modification

Given:

```csharp
app.MapGroup("/identity").MapIdentityApi<ApplicationUser>();
```

the scaffolder changes only the terminal invocation:

```csharp
app.MapGroup("/identity").MapCustomIdentityApi<ApplicationUser>();
```

The receiver expression and its route prefix or conventions are preserved. A `using` directive for the generated namespace is added only when required.

The code modification is syntax-aware. It supports direct extension invocations and member-access invocations chained from a route group, including calls formatted across lines. Statically qualified calls through the framework extension class are not part of the currently tested rewrite contract.

## Relationship to Blazor Identity

The existing `blazor-identity` scaffolder generates `MapAdditionalIdentityEndpoints`, which supports the Razor components under `Components/Account`. Those endpoints include external login, form-based logout, passkey operations, linking external logins, and personal-data download.

The scaffolder neither replaces nor modifies `MapAdditionalIdentityEndpoints`. Both mappings may exist in one project because their current routes do not collide. The current implementation does not perform general static route-collision analysis; applications that add or rename routes in either generated surface remain responsible for avoiding collisions.

Blazor Web Apps using browser-based authentication should continue to prefer cookies. The generated login endpoint preserves the framework's `useCookies` and `useSessionCookies` behavior; the scaffolder does not change the application's authentication choice.

## Framework source and versioning

`IdentityEndpointsSource.Render` owns the source snapshot in the current implementation. It explicitly accepts .NET 8, .NET 9, .NET 10, and .NET 11 target-framework values and rejects other values. The current snapshot is shared by those supported frameworks because the required endpoint implementation compiles against each target; the target framework is included in the generated provenance header.

The generated implementation is security-sensitive. Before production release, each supported target framework must be compared with the corresponding released ASP.NET Core source, and any behavioral differences must be represented explicitly. If framework implementations diverge, split the renderer into per-framework snapshots or move to per-framework T4 templates under `Templates/netX.0/IdentityEndpoints`. Each snapshot must record its ASP.NET Core source version or commit and have a reviewed cross-version diff.

Scaffolded code is a snapshot. Running the scaffolder after upgrading the target framework does not merge framework changes into application-owned code. Without `--overwrite`, the command refuses to replace an existing output file. With `--overwrite`, the generated source file is replaced and local edits to that file are lost; an existing `MapCustomIdentityApi` mapping is still rejected to prevent ambiguous endpoint registration.

## Scaffolder implementation

The implementation follows the current ASP.NET scaffolder pipeline.

### Command registration

Register `identity-endpoints` in `AspNetCommandService` with Identity and API categories and the options described above.

### Validation and settings

Add an `IdentityEndpointsSettings` object containing:

- project path;
- target framework;
- user type name and namespace;
- generated class, method, namespace, and output path;
- the discovered endpoint-mapping file path, when present;
- overwrite state.

`ValidateIdentityEndpointsStep` uses the project code service and Roslyn semantic models to validate the project, registrations, mappings, and selected user type, then stores `IdentityEndpointsSettings` in the scaffold context. It intentionally does not reuse `ValidateIdentityStep`, because that step provisions Identity storage and requires database options outside this feature's scope.

### Source generation

`IdentityEndpointsSource.Render` produces the application-owned C# source. This avoids adding a template model and text-templating step for a single generated file. The renderer validates that the target framework is supported before returning the source snapshot.

No NuGet package step is expected. The endpoint APIs are supplied by the ASP.NET Core shared framework, and the precondition requires an already configured Identity application. If a target framework later requires a package not already implied by Identity setup, that package should be added only in that framework's pipeline.

### Generation and code modification

`IdentityEndpointsScaffolderStep` writes the generated source and performs a syntax-aware rewrite that:

- locates the validated generic `MapIdentityApi<TUser>` invocation;
- changes its method identifier to `MapCustomIdentityApi`;
- preserves its receiver, generic type, trivia, and chained conventions;
- adds a namespace import if necessary;
- makes no mapping change when validation found no existing invocation.

The existing JSON code-modification format is suitable for fixed insertions but cannot safely identify and rewrite every supported invocation shape. The implementation therefore uses Roslyn syntax nodes directly rather than string replacement. Validation uses semantic information to resolve generic type arguments; the final rewrite changes only the validated generic invocation name and adds the generated namespace import when needed.

## Security and compatibility requirements

The generated baseline must preserve these invariants from the framework implementation:

- Login failures do not disclose whether an account exists.
- Forgot-password and resend-confirmation responses do not disclose account existence or confirmation state.
- Confirmation and reset tokens remain Base64 URL encoded and are decoded defensively.
- `/manage` endpoints require authorization as a group.
- Password, lockout, two-factor, security-stamp, email, and user-store operations continue to use `UserManager<TUser>` and `SignInManager<TUser>`.
- Refresh tokens are validated for expiration and security stamp before a new principal is issued.
- Confirmation links are generated by endpoint name and preserve a surrounding route-group prefix.
- Identity failures retain their `ValidationProblem` error-code shape.
- Cookies remain the recommended browser transport; bearer tokens are not represented as JWTs or as a general token-server solution.

Generated source snapshots must not opportunistically refactor framework code. For example, replacing service-provider resolution with direct generic dependency injection should wait until Request Delegate Generator support for generic types from an outer scope is available in every supported target framework and the corresponding framework implementation adopts it.

Likewise, .NET Minimal API validation should replace explicit Identity validation only when the framework source for that target framework does so. Matching framework behavior is more important than reducing generated code.

## Error handling and diagnostics

Validation failures should be actionable and make no project changes. Important diagnostics include:

- unsupported project SDK or target framework;
- no resolvable Identity user class;
- mismatch between `AddIdentityApiEndpoints<TUser>` and `MapIdentityApi<TUser>` user types;
- missing Identity API service registration;
- multiple `MapIdentityApi` calls;
- existing generated output without `--overwrite`;
- an existing `MapCustomIdentityApi` call;
- a mapping shape that cannot be rewritten without changing its receiver or conventions.

On success, the command logs the generated output path. When no framework mapping exists, it logs the exact `MapCustomIdentityApi<TUser>` call that the developer must add. A richer summary containing the modified file, inferred user type, route prefix, and target framework is a possible follow-up.

## Testing strategy and current coverage

### Current automated coverage

- Command-step registration for validation and generation.
- Code modification that preserves a route-group receiver, generic user type, chained conventions, and the generated namespace import.
- Rejection by the rewrite helper when multiple framework mappings are present.
- Generated-source syntax and presence of the complete endpoint route set.
- CLI option inventory and command-family matrix coverage for `identity-endpoints`.

### Required follow-up coverage

For each supported target framework:

1. Create a Web API project configured with `AddIdentityApiEndpoints<ApplicationUser>` and EF Core stores.
2. Scaffold identity endpoints.
3. Verify the expected file and `Program.cs` diff.
4. Build the project with warnings treated as errors.
5. Start the application and verify the endpoint data source contains every expected route, verb, and authorization requirement.
6. Exercise registration, failed login, cookie login, token login and refresh, email confirmation, password reset, 2FA management, and account information.
7. Verify non-disclosure behavior for unknown users.
8. Repeat with `app.MapGroup("/identity")` and confirm generated email links include the prefix.
9. Run in a Blazor Identity project and verify `MapAdditionalIdentityEndpoints` continues to build and map without collisions.

Golden-file tests should compare generated source with an approved baseline per target framework. They are necessary because compile tests alone will not detect accidental security or response-contract drift.

Validation tests must also cover option defaults, user-type inference, missing or mismatched registrations, unsupported frameworks, output conflicts, and idempotence. The current change has been manually validated by compiling generated applications for .NET 8, .NET 9, .NET 10, and .NET 11 and by exercising registration, bearer login, refresh, and authorized account information in a Blazor sample. These checks demonstrate viability but do not replace committed integration and golden-file tests.

## Documentation

User documentation should cover:

- when to use built-in `MapIdentityApi<TUser>` versus scaffolding;
- the command and option reference;
- the generated files and startup change;
- a registration customization example using an application-specific request type;
- an account-info customization example that deliberately selects safe claims rather than returning all claims;
- how to add logout and authenticated account deletion safely;
- cookie guidance for browser and Blazor clients;
- the snapshot and overwrite policy;
- how to compare scaffolded code with a newer framework after upgrading.

## Alternatives considered

### Continue recommending manual source copy

Rejected. It gives developers no target-framework validation, startup rewrite, provenance, or consistent generated-source baseline for security-sensitive code.

### Generate handlers as independent public methods

Deferred. Separate handlers would improve selective reuse, but this is a larger API design that diverges substantially from the framework source. The first version optimizes for behavioral parity and easy source comparison.

### Generate selected endpoints only

Deferred. Selection is attractive but endpoints have non-obvious dependencies. Registration and account email changes depend on confirmation-link generation; authentication mode affects login and refresh; management endpoints share authorization conventions. Whole-set generation provides a safer initial contract. A later design can define named endpoint groups and dependency closure rather than expose a fragile list of checkboxes.

### Add logout and account deletion by default

Rejected for the baseline because these are not currently part of `MapIdentityApi<TUser>`. Documentation can show both customizations, and a future opt-in endpoint pack can add them after their request, antiforgery, reauthentication, token invalidation, and response contracts are reviewed.

### Integrate the feature into `blazor-identity`

Rejected. Identity API endpoints are useful to Blazor WebAssembly and other SPA clients, but are not required by Blazor Identity Razor components. Combining the commands would generate a larger public API surface for applications that only need server-rendered account UI.

## Decisions and open questions

The implementation currently makes these decisions:

- Support the tool's .NET 8, .NET 9, .NET 10, and .NET 11 target-framework values.
- Keep `--user-class` optional when it can be inferred unambiguously.
- Fix the generated method name as `MapCustomIdentityApi`; `--name` configures only the extension class name.
- Generate the complete framework endpoint set rather than selected endpoints.

The remaining design questions are:

1. Should source snapshots remain in the C# renderer or move to per-framework T4 templates before release?
2. Should a future endpoint-selection experience use individual endpoints or reviewed groups such as `authentication`, `email`, and `management`?
3. Should logout and account deletion ship later as an opt-in `--additional-endpoints` set or remain documentation-only customizations?
4. Should general static route-collision analysis and a richer scaffold summary be required for the first release?

## Acceptance criteria and implementation status

- [x] `dotnet scaffold aspnet identity-endpoints` is discoverable under the ASP.NET Core Identity and API categories.
- [x] A supported, correctly configured project receives an application-owned implementation of the complete Identity API endpoint set represented by the source snapshot.
- [x] An existing `MapIdentityApi<TUser>` call is changed to `MapCustomIdentityApi<TUser>` without losing its route-group receiver or chained conventions.
- [x] Validation completes before generation, so validation failures make no project changes.
- [x] Existing generated files are not overwritten unless `--overwrite` is specified.
- [x] The generated output has been compiled manually against every supported target-framework value.
- [x] A generated Blazor sample has exercised registration, bearer login, token refresh, and authorized account information successfully.
- [ ] Each generated snapshot is verified against the corresponding released ASP.NET Core framework source and records that source version or commit.
- [ ] Unit tests cover the complete validation and idempotence matrix.
- [ ] Golden-file, build, and runtime integration tests cover every supported target framework and the complete security-sensitive endpoint behavior.
- [ ] Automated Blazor Identity coexistence coverage verifies that `MapAdditionalIdentityEndpoints` and the generated API routes do not collide.

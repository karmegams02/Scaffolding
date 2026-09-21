// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Tools.Scaffold.AspNet.ScaffoldSteps.Settings;
using Microsoft.DotNet.Scaffolding.Core.Model;

namespace Microsoft.DotNet.Tools.Scaffold.AspNet.ScaffoldSteps;

internal static class IdentityEndpointsSource
{
    internal static string Render(IdentityEndpointsSettings settings)
    {
        _ = settings.TargetFramework switch
        {
            TargetFramework.Net8 or TargetFramework.Net9 or TargetFramework.Net10 or TargetFramework.Net11 => true,
            _ => throw new NotSupportedException($"No Identity endpoints source snapshot exists for {settings.TargetFramework}.")
        };

        return $$"""
            // Scaffolded from ASP.NET Core Identity API endpoints for {{settings.TargetFramework}}.
            // This file is application-owned and can be customized.

            using System.ComponentModel.DataAnnotations;
            using System.Security.Claims;
            using System.Text;
            using System.Text.Encodings.Web;
            using Microsoft.AspNetCore.Authentication.BearerToken;
            using Microsoft.AspNetCore.Identity;
            using Microsoft.AspNetCore.Identity.Data;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.AspNetCore.WebUtilities;
            using Microsoft.Extensions.Options;

            namespace {{settings.GeneratedNamespace}};

            public static class {{settings.GeneratedClassName}}
            {
                private static readonly EmailAddressAttribute _emailAddressAttribute = new();

                public static IEndpointConventionBuilder {{settings.GeneratedMethodName}}<TUser>(this IEndpointRouteBuilder endpoints)
                    where TUser : class, new()
                {
                    ArgumentNullException.ThrowIfNull(endpoints);

                    var timeProvider = endpoints.ServiceProvider.GetRequiredService<TimeProvider>();
                    var bearerTokenOptions = endpoints.ServiceProvider.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>();
                    var emailSender = endpoints.ServiceProvider.GetRequiredService<IEmailSender<TUser>>();
                    var linkGenerator = endpoints.ServiceProvider.GetRequiredService<LinkGenerator>();
                    string? confirmEmailEndpointName = null;

                    var routeGroup = endpoints.MapGroup("");

                    routeGroup.MapPost("/register", async ([FromBody] RegisterRequest registration, HttpContext context, [FromServices] IServiceProvider services) =>
                    {
                        var userManager = services.GetRequiredService<UserManager<TUser>>();
                        if (!userManager.SupportsUserEmail)
                        {
                            throw new NotSupportedException("Identity API endpoints require a user store with email support.");
                        }

                        var userStore = services.GetRequiredService<IUserStore<TUser>>();
                        var emailStore = (IUserEmailStore<TUser>)userStore;
                        var email = registration.Email;
                        if (string.IsNullOrEmpty(email) || !_emailAddressAttribute.IsValid(email))
                        {
                            return CreateValidationProblem(IdentityResult.Failed(userManager.ErrorDescriber.InvalidEmail(email)));
                        }

                        var user = new TUser();
                        await userStore.SetUserNameAsync(user, email, CancellationToken.None);
                        await emailStore.SetEmailAsync(user, email, CancellationToken.None);
                        var result = await userManager.CreateAsync(user, registration.Password);
                        if (!result.Succeeded)
                        {
                            return CreateValidationProblem(result);
                        }

                        await SendConfirmationEmailAsync(user, userManager, context, email);
                        return Results.Ok();
                    });

                    routeGroup.MapPost("/login", async ([FromBody] LoginRequest login, [FromQuery] bool? useCookies, [FromQuery] bool? useSessionCookies, [FromServices] IServiceProvider services) =>
                    {
                        var signInManager = services.GetRequiredService<SignInManager<TUser>>();
                        var useCookieScheme = useCookies == true || useSessionCookies == true;
                        var isPersistent = useCookies == true && useSessionCookies != true;
                        signInManager.AuthenticationScheme = useCookieScheme ? IdentityConstants.ApplicationScheme : IdentityConstants.BearerScheme;

                        var result = await signInManager.PasswordSignInAsync(login.Email, login.Password, isPersistent, lockoutOnFailure: true);
                        if (result.RequiresTwoFactor)
                        {
                            if (!string.IsNullOrEmpty(login.TwoFactorCode))
                            {
                                result = await signInManager.TwoFactorAuthenticatorSignInAsync(login.TwoFactorCode, isPersistent, rememberClient: isPersistent);
                            }
                            else if (!string.IsNullOrEmpty(login.TwoFactorRecoveryCode))
                            {
                                result = await signInManager.TwoFactorRecoveryCodeSignInAsync(login.TwoFactorRecoveryCode);
                            }
                        }

                        return result.Succeeded
                            ? TypedResults.Empty
                            : Results.Problem(result.ToString(), statusCode: StatusCodes.Status401Unauthorized);
                    });

                    routeGroup.MapPost("/refresh", async ([FromBody] RefreshRequest refreshRequest, [FromServices] IServiceProvider services) =>
                    {
                        var signInManager = services.GetRequiredService<SignInManager<TUser>>();
                        var refreshTokenProtector = bearerTokenOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
                        var refreshTicket = refreshTokenProtector.Unprotect(refreshRequest.RefreshToken);
                        if (refreshTicket?.Properties?.ExpiresUtc is not { } expiresUtc ||
                            timeProvider.GetUtcNow() >= expiresUtc ||
                            await signInManager.ValidateSecurityStampAsync(refreshTicket.Principal) is not TUser user)
                        {
                            return Results.Challenge();
                        }

                        var newPrincipal = await signInManager.CreateUserPrincipalAsync(user);
                        return Results.SignIn(newPrincipal, authenticationScheme: IdentityConstants.BearerScheme);
                    });

                    routeGroup.MapGet("/confirmEmail", async ([FromQuery] string userId, [FromQuery] string code, [FromQuery] string? changedEmail, [FromServices] IServiceProvider services) =>
                    {
                        var userManager = services.GetRequiredService<UserManager<TUser>>();
                        if (await userManager.FindByIdAsync(userId) is not { } user)
                        {
                            return Results.Unauthorized();
                        }

                        try
                        {
                            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
                        }
                        catch (FormatException)
                        {
                            return Results.Unauthorized();
                        }

                        IdentityResult result;
                        if (string.IsNullOrEmpty(changedEmail))
                        {
                            result = await userManager.ConfirmEmailAsync(user, code);
                        }
                        else
                        {
                            result = await userManager.ChangeEmailAsync(user, changedEmail, code);
                            if (result.Succeeded)
                            {
                                result = await userManager.SetUserNameAsync(user, changedEmail);
                            }
                        }

                        return result.Succeeded
                            ? Results.Text("Thank you for confirming your email.")
                            : Results.Unauthorized();
                    })
                    .Add(endpointBuilder =>
                    {
                        var finalPattern = ((RouteEndpointBuilder)endpointBuilder).RoutePattern.RawText;
                        confirmEmailEndpointName = $"{{settings.GeneratedMethodName}}-{finalPattern}";
                        endpointBuilder.Metadata.Add(new EndpointNameMetadata(confirmEmailEndpointName));
                    });

                    routeGroup.MapPost("/resendConfirmationEmail", async ([FromBody] ResendConfirmationEmailRequest request, HttpContext context, [FromServices] IServiceProvider services) =>
                    {
                        var userManager = services.GetRequiredService<UserManager<TUser>>();
                        if (await userManager.FindByEmailAsync(request.Email) is { } user)
                        {
                            await SendConfirmationEmailAsync(user, userManager, context, request.Email);
                        }

                        return Results.Ok();
                    });

                    routeGroup.MapPost("/forgotPassword", async ([FromBody] ForgotPasswordRequest request, [FromServices] IServiceProvider services) =>
                    {
                        var userManager = services.GetRequiredService<UserManager<TUser>>();
                        var user = await userManager.FindByEmailAsync(request.Email);
                        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
                        {
                            var code = await userManager.GeneratePasswordResetTokenAsync(user);
                            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                            await emailSender.SendPasswordResetCodeAsync(user, request.Email, HtmlEncoder.Default.Encode(code));
                        }

                        return Results.Ok();
                    });

                    routeGroup.MapPost("/resetPassword", async ([FromBody] ResetPasswordRequest request, [FromServices] IServiceProvider services) =>
                    {
                        var userManager = services.GetRequiredService<UserManager<TUser>>();
                        var user = await userManager.FindByEmailAsync(request.Email);
                        if (user is null || !await userManager.IsEmailConfirmedAsync(user))
                        {
                            return CreateValidationProblem(IdentityResult.Failed(userManager.ErrorDescriber.InvalidToken()));
                        }

                        IdentityResult result;
                        try
                        {
                            var code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.ResetCode));
                            result = await userManager.ResetPasswordAsync(user, code, request.NewPassword);
                        }
                        catch (FormatException)
                        {
                            result = IdentityResult.Failed(userManager.ErrorDescriber.InvalidToken());
                        }

                        return result.Succeeded ? Results.Ok() : CreateValidationProblem(result);
                    });

                    var accountGroup = routeGroup.MapGroup("/manage").RequireAuthorization();

                    accountGroup.MapPost("/2fa", async (ClaimsPrincipal principal, [FromBody] TwoFactorRequest request, [FromServices] IServiceProvider services) =>
                    {
                        var signInManager = services.GetRequiredService<SignInManager<TUser>>();
                        var userManager = signInManager.UserManager;
                        if (await userManager.GetUserAsync(principal) is not { } user)
                        {
                            return Results.NotFound();
                        }

                        if (request.Enable == true)
                        {
                            if (request.ResetSharedKey)
                            {
                                return CreateValidationProblem("CannotResetSharedKeyAndEnable", "Resetting the 2fa shared key must disable 2fa until a token based on the new key is validated.");
                            }

                            if (string.IsNullOrEmpty(request.TwoFactorCode))
                            {
                                return CreateValidationProblem("RequiresTwoFactor", "A valid 2fa token is required to enable 2fa.");
                            }

                            if (!await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode))
                            {
                                return CreateValidationProblem("InvalidTwoFactorCode", "The 2fa token was invalid.");
                            }

                            await userManager.SetTwoFactorEnabledAsync(user, true);
                        }
                        else if (request.Enable == false || request.ResetSharedKey)
                        {
                            await userManager.SetTwoFactorEnabledAsync(user, false);
                        }

                        if (request.ResetSharedKey)
                        {
                            await userManager.ResetAuthenticatorKeyAsync(user);
                        }

                        string[]? recoveryCodes = null;
                        if (request.ResetRecoveryCodes || request.Enable == true && await userManager.CountRecoveryCodesAsync(user) == 0)
                        {
                            recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray();
                        }

                        if (request.ForgetMachine)
                        {
                            await signInManager.ForgetTwoFactorClientAsync();
                        }

                        var key = await userManager.GetAuthenticatorKeyAsync(user);
                        if (string.IsNullOrEmpty(key))
                        {
                            await userManager.ResetAuthenticatorKeyAsync(user);
                            key = await userManager.GetAuthenticatorKeyAsync(user);
                            if (string.IsNullOrEmpty(key))
                            {
                                throw new NotSupportedException("The user manager must produce an authenticator key after reset.");
                            }
                        }

                        return Results.Ok(new TwoFactorResponse
                        {
                            SharedKey = key,
                            RecoveryCodes = recoveryCodes,
                            RecoveryCodesLeft = recoveryCodes?.Length ?? await userManager.CountRecoveryCodesAsync(user),
                            IsTwoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user),
                            IsMachineRemembered = await signInManager.IsTwoFactorClientRememberedAsync(user)
                        });
                    });

                    accountGroup.MapGet("/info", async (ClaimsPrincipal principal, [FromServices] IServiceProvider services) =>
                    {
                        var userManager = services.GetRequiredService<UserManager<TUser>>();
                        if (await userManager.GetUserAsync(principal) is not { } user)
                        {
                            return Results.NotFound();
                        }

                        return Results.Ok(await CreateInfoResponseAsync(user, userManager));
                    });

                    accountGroup.MapPost("/info", async (ClaimsPrincipal principal, [FromBody] InfoRequest request, HttpContext context, [FromServices] IServiceProvider services) =>
                    {
                        var userManager = services.GetRequiredService<UserManager<TUser>>();
                        if (await userManager.GetUserAsync(principal) is not { } user)
                        {
                            return Results.NotFound();
                        }

                        if (!string.IsNullOrEmpty(request.NewEmail) && !_emailAddressAttribute.IsValid(request.NewEmail))
                        {
                            return CreateValidationProblem(IdentityResult.Failed(userManager.ErrorDescriber.InvalidEmail(request.NewEmail)));
                        }

                        if (!string.IsNullOrEmpty(request.NewPassword))
                        {
                            if (string.IsNullOrEmpty(request.OldPassword))
                            {
                                return CreateValidationProblem("OldPasswordRequired", "The old password is required to set a new password. If it is forgotten, use /resetPassword.");
                            }

                            var passwordResult = await userManager.ChangePasswordAsync(user, request.OldPassword, request.NewPassword);
                            if (!passwordResult.Succeeded)
                            {
                                return CreateValidationProblem(passwordResult);
                            }
                        }

                        if (!string.IsNullOrEmpty(request.NewEmail) && await userManager.GetEmailAsync(user) != request.NewEmail)
                        {
                            await SendConfirmationEmailAsync(user, userManager, context, request.NewEmail, isChange: true);
                        }

                        return Results.Ok(await CreateInfoResponseAsync(user, userManager));
                    });

                    async Task SendConfirmationEmailAsync(TUser user, UserManager<TUser> userManager, HttpContext context, string email, bool isChange = false)
                    {
                        if (confirmEmailEndpointName is null)
                        {
                            throw new NotSupportedException("No email confirmation endpoint was registered.");
                        }

                        var code = isChange
                            ? await userManager.GenerateChangeEmailTokenAsync(user, email)
                            : await userManager.GenerateEmailConfirmationTokenAsync(user);
                        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

                        var routeValues = new RouteValueDictionary
                        {
                            ["userId"] = await userManager.GetUserIdAsync(user),
                            ["code"] = code
                        };
                        if (isChange)
                        {
                            routeValues["changedEmail"] = email;
                        }

                        var confirmEmailUrl = linkGenerator.GetUriByName(context, confirmEmailEndpointName, routeValues)
                            ?? throw new NotSupportedException($"Could not find endpoint named '{confirmEmailEndpointName}'.");
                        await emailSender.SendConfirmationLinkAsync(user, email, HtmlEncoder.Default.Encode(confirmEmailUrl));
                    }

                    return routeGroup;
                }

                private static IResult CreateValidationProblem(string errorCode, string errorDescription) =>
                    Results.ValidationProblem(new Dictionary<string, string[]> { [errorCode] = [errorDescription] });

                private static IResult CreateValidationProblem(IdentityResult result)
                {
                    var errors = result.Errors
                        .GroupBy(error => error.Code)
                        .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray());
                    return Results.ValidationProblem(errors);
                }

                private static async Task<InfoResponse> CreateInfoResponseAsync<TUser>(TUser user, UserManager<TUser> userManager)
                    where TUser : class
                {
                    return new InfoResponse
                    {
                        Email = await userManager.GetEmailAsync(user) ?? throw new NotSupportedException("Users must have an email."),
                        IsEmailConfirmed = await userManager.IsEmailConfirmedAsync(user)
                    };
                }
            }
            """;
    }
}

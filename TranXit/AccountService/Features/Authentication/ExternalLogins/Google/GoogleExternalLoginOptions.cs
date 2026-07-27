using Microsoft.Extensions.Options;

namespace AccountService.Features.Authentication.ExternalLogins.Google;

public sealed class GoogleExternalLoginOptions
{
	public const string SectionName = "ExternalLogin:Google";

	public bool Enabled { get; init; }
	public string ClientId { get; init; } = string.Empty;
	public string JwksUri { get; init; } = string.Empty;
}

public sealed class GoogleExternalLoginOptionsValidator : IValidateOptions<GoogleExternalLoginOptions>
{
	public ValidateOptionsResult Validate(string? name, GoogleExternalLoginOptions options)
	{
		if (!options.Enabled)
		{
			return ValidateOptionsResult.Success;
		}

		if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.JwksUri))
		{
			return ValidateOptionsResult.Fail(
				"Google external login requires a client ID and JWKS URI before it can be enabled.");
		}

		return ValidateOptionsResult.Fail(
			"Google external login cannot be enabled until signed ID-token and JWKS validation is implemented.");
	}
}

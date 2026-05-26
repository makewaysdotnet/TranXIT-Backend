using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace SharedManager.Extensions
{
	public static class JwtExtensions
	{
		public static void AddJwtAuthentication(this IServiceCollection services,
			IConfiguration? masterConfiguration = default)
		{
			var configuration = masterConfiguration
				?? throw new InvalidOperationException("JWT configuration is required.");
			var signingKey = configuration["SharedJwtSecrets:Key"];
			var issuer = configuration["Jwt:Issuer"];
			var audience = configuration["Jwt:Audience"];
			var requireHttpsMetadata = !bool.TryParse(configuration["Jwt:RequireHttpsMetadata"], out var configuredRequireHttps)
				|| configuredRequireHttps;

			if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
			{
				throw new InvalidOperationException("JWT signing key must be configured and at least 32 bytes.");
			}

			if (string.IsNullOrWhiteSpace(issuer))
			{
				throw new InvalidOperationException("JWT issuer must be configured.");
			}

			if (string.IsNullOrWhiteSpace(audience))
			{
				throw new InvalidOperationException("JWT audience must be configured.");
			}

			services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
				.AddJwtBearer(option =>
					{
						option.RequireHttpsMetadata = requireHttpsMetadata;
						option.SaveToken = false;
						option.TokenValidationParameters = new TokenValidationParameters
						{
							ValidateIssuerSigningKey = true,
							ValidateIssuer = true,
							ValidIssuer = issuer,
							ValidateAudience = true,
							ValidAudience = audience,
							ValidateLifetime = true,
							RequireExpirationTime = true,
							ClockSkew = TimeSpan.FromMinutes(2),
							IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
						};
					});
		}
	}
}

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Reflection;
using System.Text;

namespace SharedManager.Extensions
{
	public static class JwtExtensions
	{
		public static void AddJwtAuthentication(this IServiceCollection services)
		{
			IConfiguration configuration = new ConfigurationBuilder()
				.AddUserSecrets(Assembly.GetExecutingAssembly())
				.Build();
			services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
				.AddJwtBearer(option =>
					{
						option.RequireHttpsMetadata = false;
						option.SaveToken = true;
						option.TokenValidationParameters = new TokenValidationParameters
						{
							ValidateIssuerSigningKey = true,
							ValidateIssuer = false,
							ValidateAudience = false,
							IssuerSigningKey = new SymmetricSecurityKey(Encoding
							.UTF8
							.GetBytes(configuration["SharedJwtSecrets:Key"]!))
						};
					});
		}
	}
}

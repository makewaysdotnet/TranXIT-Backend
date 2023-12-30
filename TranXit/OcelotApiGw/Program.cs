using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.Cache.CacheManager;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
			.GetBytes(builder.Configuration["JwtSecrets:Key"]!))
		};
	});
builder.Services.AddAuthorization();

builder.Configuration.SetBasePath(builder.Environment.ContentRootPath)
	.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
	.AddEnvironmentVariables();
// Add services to the container.
builder.Services.AddOcelot(builder.Configuration)
			.AddCacheManager(o => o.WithDictionaryHandle());
builder.Services.AddCors();

var app = builder.Build();
// global cors policy
app.UseCors(options =>
{
	var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
	if (origins == null)
	{
		throw new ArgumentNullException("AllowedOrigins", "Missing 'AllowedOrigins' in appSettings.json (string array of domains).");
	}

	options
		.WithOrigins(origins)
		.AllowAnyMethod()
		.AllowAnyHeader()
		.AllowCredentials();
});

app.UseAuthentication();
app.UseAuthorization();
await app.UseOcelot();
app.Run();
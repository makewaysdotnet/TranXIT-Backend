using Ocelot.Cache.CacheManager;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using SharedManager.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

builder.Configuration
	.SetBasePath(builder.Environment.ContentRootPath)
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
// Ocelot is terminal middleware, so liveness must run before the proxy pipeline.
app.UseHealthChecks("/health");
await app.UseOcelot();
app.Run();

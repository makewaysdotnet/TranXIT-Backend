using Carter;
using CourierJobService.Database;
using CourierJobService.Repositories.JobStatusRepository;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedManager.Extensions;
using SharedServicesManager.EmailService;
using SharedServicesManager.Helpers;
using SharedServicesManager.Middlewares;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<CourierJobDbContext>(o =>
	 //o.UseLazyLoadingProxies()
	 o.UseSqlServer(builder.Configuration.GetConnectionString("Database")));
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("MailSettings"));

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
	options.AddPolicy("CustomerCourierPolicy", policy =>
	{
		policy.RequireAuthenticatedUser();
		policy.RequireRole("Courier", "Customer");
	});
	options.AddPolicy("CourierPolicy", policy =>
	{
		policy.RequireAuthenticatedUser();
		policy.RequireRole("Courier");
	});
	options.AddPolicy("CustomerPolicy", policy =>
	{
		policy.RequireAuthenticatedUser();
		policy.RequireRole("Customer");
	});
});

builder.Services.AddScoped<IJobStatusRepository, JobStatusRepository>();

var assembly = typeof(Program).Assembly;

builder.Services.AddMediatR(config =>
	config.RegisterServicesFromAssembly(assembly));
builder.Services.AddScoped<IMailService, MailService>();
builder.Services.AddScoped<IUtils, Utils>();

builder.Services.AddCarter();

builder.Services.AddValidatorsFromAssembly(assembly);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();

//builder.Services.AddMassTransit(busConfigurator =>
//{
//	busConfigurator.SetKebabCaseEndpointNameFormatter();
//	busConfigurator.UsingAmazonSqs((context, config) =>
//	{
//		config.Host(builder.Configuration["Aws:Region"], h =>
//		{
//			h.AccessKey(builder.Configuration["Aws:AccessKey"]);
//			h.SecretKey(builder.Configuration["Aws:SecretKey"]);
//		});
//		config.ConfigureEndpoints(context);
//	});
//});
builder.Services.AddMassTransit(busConfigurator =>
{
	busConfigurator.SetKebabCaseEndpointNameFormatter();
	if (builder.Environment.IsEnvironment("Testing"))
	{
		busConfigurator.UsingInMemory((context, configurator) =>
		{
			configurator.ConfigureEndpoints(context);
		});
	}
	else
	{
		busConfigurator.UsingRabbitMq((context, configurator) =>
		{
			configurator.Host(builder.Configuration["RabbitMQ:HostName"]!, "/", c =>
			{
				c.Username(builder.Configuration["RabbitMQ:UserName"]!);
				c.Password(builder.Configuration["RabbitMQ:Password"]!);
			});
			configurator.ConfigureEndpoints(context);
		});
	}
});
builder.Services.AddCors();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsEnvironment("Testing"))
{
	await CourierJobDatabaseMigrator.MigrateAsync(app.Services);
}

if (app.Environment.IsDevelopment())
{
	await CourierJobDevelopmentSeeder.SeedAsync(app.Services);
	app.UseSwagger();
	app.UseSwaggerUI();
}

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
		.AllowAnyHeader();
});

app.UseAuthentication();
app.UseAuthorization();
//app.UseAntiforgery();
app.MapCarter();
app.UseExceptionHandler();
app.MapHealthChecks("/courierjobservice");

app.Run();

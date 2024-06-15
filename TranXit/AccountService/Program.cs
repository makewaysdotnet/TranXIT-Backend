using AccountService.Database;
using AccountService.Features.Authentication.TokenManager;
using Carter;
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

builder.Services.AddDbContext<AccountDbContext>(o =>
	o.UseSqlServer(builder.Configuration.GetConnectionString("Database")));
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("MailSettings"));

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
	options.AddPolicy("CourierPolicy", policy => policy.RequireRole("Courier"));
	options.AddPolicy("CustomerPolicy", policy => policy.RequireRole("Customer"));
});
//builder.Services.AddAntiforgery();


var assembly = typeof(Program).Assembly;

builder.Services.AddMediatR(config =>
	config.RegisterServicesFromAssembly(assembly));
builder.Services.AddScoped<IJwtTokenBuilder, JwtTokenBuilder>();
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
//	busConfigurator.AddConsumers(assembly);
//	busConfigurator.AddSagaStateMachines(assembly);
//	busConfigurator.AddSagas(assembly);
//	busConfigurator.AddActivities(assembly);
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
	busConfigurator.AddConsumers(assembly);
	busConfigurator.AddSagaStateMachines(assembly);
	busConfigurator.AddSagas(assembly);
	busConfigurator.AddActivities(assembly);
	busConfigurator.UsingRabbitMq((context, configurator) =>
	{
		configurator.Host(builder.Configuration["RabbitMQ:HostName"]!, "/", c =>
		{
			c.Username(builder.Configuration["RabbitMQ:UserName"]!);
			c.Password(builder.Configuration["RabbitMQ:Password"]!);
		});
		configurator.ConfigureEndpoints(context);
	});
});
builder.Services.AddCors();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
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
app.MapHealthChecks("/");

app.Run();

using AccountService.Database;
using AccountService.Features.Authentication.TokenManager;
using Carter;
using FluentValidation;
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

builder.Services.AddJwtAuthentication();
builder.Services.AddAuthorization();
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
//	busConfigurator.UsingRabbitMq((context, config) =>
//	{
//		config.Host(new Uri(builder.Configuration["MessageBroker:Host"]!), h =>
//		{
//			h.Username(builder.Configuration["MessageBroker:Username"]!);
//			h.Password(builder.Configuration["MessageBroker:Password"]!);
//		});
//		config.ConfigureEndpoints(context);
//	});
//});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
//app.UseAntiforgery();
app.MapCarter();
app.UseExceptionHandler();

app.Run();

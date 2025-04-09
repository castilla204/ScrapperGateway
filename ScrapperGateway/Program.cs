using AutoMapper;
using DataLayer;
using DataLayer.Models;
using DataLayer.Models.DTOs;
using DataLayer.Mapping;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using ServicesLayer;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using DataLayer.Models.PostGresModels;
using ScrapperGateway.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configurar la cadena de conexión según el entorno
if (builder.Environment.IsDevelopment())
{
    builder.Configuration["ConnectionStrings:PostgresConnection"] = "Host=localhost;Port=5432;Username=postgres;Password=REEMPLAZAR;Database=grup";
}
else
{
    builder.Configuration["ConnectionStrings:PostgresConnection"] = "Host=postgres-svc;Port=5432;Username=admin;Password=REEMPLAZAR;Database=atrapo";
}

// Configure PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgresConnection")));

// Register AutoMapper
builder.Services.AddAutoMapper(typeof(AdMappingProfile), typeof(WallapopMappingProfile));

// Register Data and Service layers
builder.Services.AddScoped<IWeb1Data, Web1Data>();
builder.Services.AddScoped<IWeb1Service, Web1Service>();
builder.Services.AddScoped<IWeb2Data, Web2Data>();
builder.Services.AddScoped<IWeb2Service, Web2Service>();
builder.Services.AddScoped<IWeb3Data, Web3Data>();
builder.Services.AddScoped<IWeb3Service, Web3Service>();
builder.Services.AddScoped<IWebMixerService, WebMixerService>();

// Configure RabbitMQ
builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    var config = builder.Configuration;
    var isDevelopment = builder.Environment.IsDevelopment();
    return new ConnectionFactory
    {
        HostName = isDevelopment ? "localhost" : config["RABBITMQ_HOST"] ?? "rabbitmq-svc",
        Port = int.Parse(config["RABBITMQ_PORT"] ?? "5672"),
        UserName = config["RABBITMQ_USER"] ?? "admin",
        Password = config["RABBITMQ_PASSWORD"] ?? "REEMPLAZAR",
        RequestedHeartbeat = TimeSpan.FromSeconds(60),
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
        AutomaticRecoveryEnabled = true,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(30)
    };
});

// Add RabbitMQ Consumer Service
builder.Services.AddHostedService<RabbitMQConsumerService>();

// Register HTTP client
builder.Services.AddHttpClient();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var url = "http://localhost:7555";
app.Urls.Add(url);

app.UseCors("AllowAll");

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
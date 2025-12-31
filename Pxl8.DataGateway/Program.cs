using Pxl8.DataGateway.BackgroundServices;
using Pxl8.DataGateway.Configuration;
using Pxl8.DataGateway.Security;
using Pxl8.DataGateway.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<DataPlaneOptions>(
    builder.Configuration.GetSection(DataPlaneOptions.SectionName));

// HTTP Client for Control API with HMAC signing
builder.Services.AddTransient<HmacSigningHandler>();
builder.Services.AddHttpClient("ControlApi")
    .AddHttpMessageHandler<HmacSigningHandler>();

// Data Plane Core Services
builder.Services.AddSingleton<IBudgetManager, BudgetManager>();
builder.Services.AddSingleton<IPolicySnapshotCache, PolicySnapshotCache>();

// Background Services
builder.Services.AddHostedService<PolicySnapshotSyncer>();
builder.Services.AddHostedService<UsageReporter>();
builder.Services.AddHostedService<BudgetRefiller>();

// API Services
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;

var builder = WebApplication.CreateBuilder(args);

//Add services to the container

//https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();

//Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapControllers();

app.Run();

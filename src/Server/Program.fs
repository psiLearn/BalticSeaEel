module Eel.Server.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.OpenApi.Models
open Giraffe
open Eel.Server.Services
open Eel.Server.HttpHandlers

let builder = WebApplication.CreateBuilder()

builder.Services.AddGiraffe() |> ignore
builder.Services.AddSingleton<HighScoreStore>() |> ignore
builder.Services.AddCors(fun options ->
    options.AddPolicy(
        "AllowClient",
        fun policy ->
            policy
                .WithOrigins(
                    "http://localhost:5173",
                    "https://localhost:5173")
                .AllowAnyHeader()
                .AllowAnyMethod()
                |> ignore))
|> ignore

builder.Services.AddEndpointsApiExplorer() |> ignore
builder.Services.AddSwaggerGen(fun options ->
    options.SwaggerDoc(
        "v1",
        OpenApiInfo(
            Title = "Baltic Sea Eel API",
            Version = "v1",
            Description = "Endpoints for high scores and vocabulary used by the Baltic Sea Eel game."
        )
    )

    options.DocumentFilter<ApiDocumentFilter>())
|> ignore

let app = builder.Build()

if app.Environment.IsDevelopment() then
    app.UseSwagger() |> ignore
    app.UseSwaggerUI() |> ignore

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore
app.UseCors("AllowClient") |> ignore
app.UseGiraffe HttpHandlers.webApp |> ignore

app.Run()

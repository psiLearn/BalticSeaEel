module Eel.Server.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open Microsoft.Extensions.Hosting
open Microsoft.OpenApi.Models
open Giraffe
open Giraffe.EndpointRouting
open Giraffe.OpenApi
open System.Text.Json
open System.Text.Json.Serialization
open Serilog
open System
open Eel.Server.Services
open Eel.Server.HttpHandlers
open Shared

Log.Logger <-
    LoggerConfiguration()
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .CreateLogger()

let builder = WebApplication.CreateBuilder()

let defaultConnection = "Host=localhost;Port=5432;Username=eel;Password=eel;Database=eel"

let connectionString =
    match builder.Configuration.GetValue<string>("POSTGRES_CONNECTION") with
    | null -> defaultConnection
    | value when String.IsNullOrWhiteSpace value -> defaultConnection
    | value -> value

builder.Host.UseSerilog() |> ignore

builder.Services.AddGiraffe() |> ignore
builder.Services
    .AddOptions<JsonSerializerOptions>()
    .Configure(fun options ->
        options.PropertyNameCaseInsensitive <- true
        options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        options.Converters.Add(JsonFSharpConverter()))
|> ignore

let createSerializer (options: JsonSerializerOptions) =
    let serializer = Giraffe.Json.Serializer(options)
    if obj.ReferenceEquals(serializer, null) then
        invalidOp "Failed to configure JSON serializer."
    serializer

let serializerFactory =
    Func<IServiceProvider, obj>(fun sp ->
        let options = sp.GetRequiredService<IOptionsSnapshot<JsonSerializerOptions>>().Value
        createSerializer options :> obj)

builder.Services.AddScoped(typeof<Giraffe.Json.ISerializer>, serializerFactory) |> ignore
builder.Services.AddSingleton<IScoreRepository>(fun _ -> PostgresScoreRepository(connectionString) :> IScoreRepository) |> ignore
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
)
|> ignore

let app = builder.Build()

let endpoints =
    [ GET [
          route "/" (htmlFile "wwwroot/index.html")
      ]
      GET [
          route "/api/highscore" HttpHandlers.getHighScoreHandler
          |> addOpenApiSimple<unit, HighScore>
      ]
      GET [
          route "/api/scores" HttpHandlers.getScoresHandler
          |> addOpenApiSimple<unit, HighScore list>
      ]
      POST [
          route "/api/highscore" HttpHandlers.saveHighScoreHandler
          |> addOpenApiSimple<HighScore, HighScore>
      ]
      GET [
          route "/api/vocabulary" HttpHandlers.getVocabularyHandler
          |> addOpenApiSimple<unit, VocabularyEntry>
      ] ]

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore
app.UseRouting() |> ignore
app.UseCors("AllowClient") |> ignore

if app.Environment.IsDevelopment() then
    app.UseSwagger() |> ignore
    app.UseSwaggerUI() |> ignore

app.UseEndpoints(fun endpointBuilder ->
    endpointBuilder.MapGiraffeEndpoints endpoints |> ignore)
|> ignore

app.Lifetime.ApplicationStopped.Register(fun () -> Log.CloseAndFlush()) |> ignore

app.Run()

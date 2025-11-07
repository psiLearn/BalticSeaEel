module HandlersTests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.Extensions.Options
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Xunit
open Eel.Server
open Eel.Server.Services
open Shared

let private noop : HttpFunc = fun _ -> task { return None }

let private createContext () =
    let services = ServiceCollection()
    services.AddGiraffe() |> ignore
    services
        .AddOptions<JsonSerializerOptions>()
        .Configure(fun options ->
            options.PropertyNameCaseInsensitive <- true
            options.Converters.Add(JsonFSharpConverter()))
    |> ignore
    services.AddScoped<Giraffe.Json.ISerializer>(fun sp ->
        let options = sp.GetRequiredService<IOptionsSnapshot<JsonSerializerOptions>>().Value
        Giraffe.Json.Serializer(options) :> Giraffe.Json.ISerializer)
    |> ignore
    services.AddSingleton<IScoreRepository>(fun _ -> InMemoryScoreRepository() :> IScoreRepository) |> ignore
    services.AddSingleton<HighScoreStore>() |> ignore

    let ctx = DefaultHttpContext()
    let provider = services.BuildServiceProvider()
    let scope = provider.CreateScope()
    ctx.RequestServices <- scope.ServiceProvider
    ctx.Response.RegisterForDispose(scope) |> ignore
    ctx.Response.Body <- new MemoryStream()
    ctx

let private readBody (ctx: HttpContext) =
    ctx.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
    use reader = new StreamReader(ctx.Response.Body, Encoding.UTF8)
    reader.ReadToEnd()

[<Fact>]
let ``GET /highscore returns stored score`` () =
    let ctx = createContext ()
    let store = ctx.RequestServices.GetRequiredService<HighScoreStore>()
    store.Upsert { Name = "Ina"; Score = 42 } |> ignore

    let task = HttpHandlers.getHighScoreHandler noop ctx
    task.Wait()

    Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode)

    let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
    let body = readBody ctx
    let decoded = JsonSerializer.Deserialize<HighScore>(body, options)
    Assert.Equal(42, decoded.Score)
    Assert.Equal("Ina", decoded.Name)

[<Fact>]
let ``POST /highscore upgrades to higher score`` () =
    let ctx = createContext ()
    ctx.Request.Method <- "POST"
    ctx.Request.ContentType <- "application/json"

    let payload = JsonSerializer.Serialize({ Name = "Mika"; Score = 5 })
    let bytes = Encoding.UTF8.GetBytes(payload)
    let ms = new MemoryStream(bytes)
    ctx.Request.Body <- ms
    ctx.Request.ContentLength <- Nullable<int64>(int64 bytes.Length)
    ctx.Request.Body.Seek(0L, SeekOrigin.Begin) |> ignore

    let task = HttpHandlers.saveHighScoreHandler noop ctx
    task.Wait()

    let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
    let response = readBody ctx |> fun body -> JsonSerializer.Deserialize<HighScore>(body, options)
    Assert.Equal(5, response.Score)
    Assert.Equal("Mika", response.Name)

    let store = ctx.RequestServices.GetRequiredService<HighScoreStore>()
    let stored = store.Get()

    Assert.Equal(5, stored.Score)
    Assert.Equal("Mika", stored.Name)

[<Fact>]
let ``GET /scores returns ordered scoreboard`` () =
    let ctx = createContext ()
    let store = ctx.RequestServices.GetRequiredService<HighScoreStore>()
    store.Upsert { Name = "Ina"; Score = 42 } |> ignore
    store.Upsert { Name = "Tom"; Score = 15 } |> ignore

    let task = HttpHandlers.getScoresHandler noop ctx
    task.Wait()

    Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode)

    let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
    let body = readBody ctx
    let decoded = JsonSerializer.Deserialize<HighScore list>(body, options)

    let top =
        decoded
        |> List.tryFind (fun score -> score.Name = "Ina")
        |> Option.defaultWith (fun _ -> failwith "Expected score for Ina")

    Assert.Equal(42, top.Score)

[<Fact>]
let ``Upsert replaces lower score for same player`` () =
    let repo = InMemoryScoreRepository() :> IScoreRepository
    let store = HighScoreStore(repo)
    store.Upsert { Name = "Alex"; Score = 10 } |> ignore
    let first = store.Get()
    Assert.Equal(10, first.Score)

    store.Upsert { Name = "Alex"; Score = 25 } |> ignore
    let second = store.Get()
    Assert.Equal(25, second.Score)

    let all = store.GetAll()
    let alexEntries = all |> List.filter (fun s -> s.Name = "Alex")
    Assert.Single(alexEntries)

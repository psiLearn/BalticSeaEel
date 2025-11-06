module HandlersTests

open System.IO
open System.Text
open System.Text.Json
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
    services.AddSingleton<HighScoreStore>() |> ignore

    let ctx = DefaultHttpContext()
    ctx.RequestServices <- services.BuildServiceProvider()
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
    ctx.Request.Body <- new MemoryStream(bytes)

    let task = HttpHandlers.saveHighScoreHandler noop ctx
    task.Wait()

    let store = ctx.RequestServices.GetRequiredService<HighScoreStore>()
    let stored = store.Get()

    Assert.Equal(5, stored.Score)
    Assert.Equal("Mika", stored.Name)

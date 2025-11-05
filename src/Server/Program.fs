module Ale.Server.Program

open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Giraffe
open Shared

type HighScoreStore() =
    let mutable highScore = { Name = "Anonymous"; Score = 0 }

    member _.Get() = highScore

    member _.Upsert candidate =
        if candidate.Score > highScore.Score then
            highScore <- candidate

        highScore

let getHighScoreHandler : HttpHandler =
    fun next ctx ->
        let store = ctx.GetService<HighScoreStore>()
        json (store.Get()) next ctx

let saveHighScoreHandler : HttpHandler =
    fun next ctx ->
        task {
            let store = ctx.GetService<HighScoreStore>()
            let! candidate = ctx.BindJsonAsync<HighScore>()
            let updated = store.Upsert candidate
            return! json updated next ctx
        }

let apiRoutes =
    choose [
        GET >=> route "/highscore" >=> getHighScoreHandler
        POST >=> route "/highscore" >=> saveHighScoreHandler
    ]

let webApp =
    choose [
        subRoute "/api" apiRoutes
        GET >=> htmlFile "wwwroot/index.html"
    ]

let builder = WebApplication.CreateBuilder()

builder.Services.AddGiraffe() |> ignore
builder.Services.AddSingleton<HighScoreStore>() |> ignore

let app = builder.Build()

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore
app.UseGiraffe webApp |> ignore

app.Run()

namespace Eel.Server
namespace Eel.Server

open Giraffe
open Shared
open Serilog
open Eel.Server.Services

module HttpHandlers =
    let getHighScoreHandler: HttpHandler =
        fun next ctx ->
            let store = ctx.GetService<HighScoreStore>()
            let score = store.Get()
            Log.Information("Returning high score {Name} -> {Score}", score.Name, score.Score)
            json score next ctx

    let getScoresHandler: HttpHandler =
        fun next ctx ->
            let store = ctx.GetService<HighScoreStore>()
            let scores = store.GetAll()
            Log.Information("Returning {Count} scores.", scores.Length)
            json scores next ctx

    let saveHighScoreHandler: HttpHandler =
        fun next ctx ->
            task {
                let store = ctx.GetService<HighScoreStore>()
                let! candidate = ctx.BindJsonAsync<HighScore>()
                Log.Information("Received high score submission {Name} -> {Score}", candidate.Name, candidate.Score)
                let updated = store.Upsert candidate
                Log.Information("Updated high score now {Name} -> {Score}", updated.Name, updated.Score)
                return! json updated next ctx
            }

    let getVocabularyHandler: HttpHandler =
        fun next ctx ->
            let entry = Vocabulary.getRandom ()
            Log.Information("Serving vocabulary topic {Topic}", entry.Topic)
            json entry next ctx

    let apiRoutes: HttpHandler =
        choose [ GET >=> route "/highscore" >=> getHighScoreHandler
                 GET >=> route "/scores" >=> getScoresHandler
                 POST >=> route "/highscore" >=> saveHighScoreHandler
                 GET >=> route "/vocabulary" >=> getVocabularyHandler ]

    let webApp: HttpHandler =
        choose [ subRoute "/api" apiRoutes
                 GET >=> htmlFile "wwwroot/index.html" ]

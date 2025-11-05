namespace Eel.Server

namespace Eel.Server

open Giraffe
open Shared
open Eel.Server.Services

module HttpHandlers =
    let getHighScoreHandler: HttpHandler =
        fun next ctx ->
            let store = ctx.GetService<HighScoreStore>()
            json (store.Get()) next ctx

    let saveHighScoreHandler: HttpHandler =
        fun next ctx ->
            task {
                let store = ctx.GetService<HighScoreStore>()
                let! candidate = ctx.BindJsonAsync<HighScore>()
                let updated = store.Upsert candidate
                return! json updated next ctx
            }

    let getVocabularyHandler: HttpHandler =
        fun next ctx ->
            json (Vocabulary.getRandom ()) next ctx

    let apiRoutes: HttpHandler =
        choose [ GET >=> route "/highscore" >=> getHighScoreHandler
                 POST >=> route "/highscore" >=> saveHighScoreHandler
                 GET >=> route "/vocabulary" >=> getVocabularyHandler ]

    let webApp: HttpHandler =
        choose [ subRoute "/api" apiRoutes
                 GET >=> htmlFile "wwwroot/index.html" ]

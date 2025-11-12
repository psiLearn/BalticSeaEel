module Eel.Client.Api

open System
open Browser.Dom
open Elmish
open Fable.Core
open Fable.Core.JsInterop
open Shared
open Eel.Client.Model

module JS = Fable.Core.JS

let inline private ofJson<'T> (json: string) : 'T = json |> JS.JSON.parse |> unbox<'T>

let inline private toJson (value: obj) = JS.JSON.stringify value

let private log category message = printfn "[Api|%s] %s" category message

let private apiBaseUrl =
    let configured: obj = window?__API_BASE_URL

    match configured with
    | :? string as value when not (String.IsNullOrWhiteSpace value) ->
        log "Config" $"Using explicit API base URL '{value}'."
        value
    | _ ->
        let location = window.location
        let origin = location.origin

        match location.port = "5173" with
        | true ->
            let protocol =
                match location.protocol = "https:" with
                | true -> "https"
                | false -> "http"

            let baseUrl = $"{protocol}://{location.hostname}:5000"
            log "Config" $"Deriving API base URL '{baseUrl}' for dev server."
            baseUrl
        | false ->
            log "Config" $"Using origin '{origin}' as API base URL."
            origin

let private combineUrl (baseUrl: string) (path: string) =
    match path.StartsWith("http://") || path.StartsWith("https://") with
    | true -> path
    | false ->
        match String.IsNullOrWhiteSpace baseUrl with
        | true -> path
        | false ->
            match baseUrl.EndsWith("/") with
            | true -> baseUrl.TrimEnd('/') + path
            | false -> baseUrl + path

let private fetch (path: string) (init: obj option) : JS.Promise<obj> =
    let url = combineUrl apiBaseUrl path

    match init with
    | Some initObj ->
        window?fetch (url, initObj)
        |> unbox<JS.Promise<obj>>
    | None -> window?fetch (url) |> unbox<JS.Promise<obj>>

let private withTimeout timeoutMs (promise: JS.Promise<'T>) : JS.Promise<'T> =
    let timeoutPromise: JS.Promise<'T> =
        createNew JS.Constructors.Promise (fun _resolve reject ->
            window.setTimeout(
                (fun _ ->
                    reject (Exception $"Request timed out after {timeoutMs} ms" :> obj)),
                timeoutMs)
            |> ignore)
        |> unbox<JS.Promise<'T>>

    JS.Constructors.Promise.race [| promise :> obj; timeoutPromise :> obj |]
    |> unbox<JS.Promise<'T>>

let fetchHighScore (_: unit) =
    promise {
        try
            log "HighScore" "Requesting current high score."
            let! response =
                fetch "/api/highscore" None
                |> withTimeout 5000

            match response?ok with
            | true ->
                let! text = response?text () |> unbox<JS.Promise<string>>
                log "HighScore" "Successfully fetched high score."
                log "HighScore" $"Parsed high score payload: {text}"

                let raw = text |> JS.JSON.parse

                let nameValue =
                    match isNullOrUndefined raw?name with
                    | true -> "Anonymous"
                    | false ->
                        let name: string = raw?name
                        match System.String.IsNullOrWhiteSpace name with
                        | true -> "Anonymous"
                        | false -> name

                let scoreValue =
                    match isNullOrUndefined raw?score with
                    | true -> 0
                    | false ->
                        let score: float = raw?score
                        score |> System.Math.Round |> int

                return Some { Name = nameValue; Score = scoreValue }
            | false ->
                let status: int = response?status |> unbox<int>
                log "HighScore" $"Request failed with status {status}."
                return None
        with ex ->
            log "HighScore" $"Request failed: {ex.Message}"
            return None
    }

let saveHighScore (name, score) =
    promise {
        log "HighScore" $"Saving score for '{name}' ({score})."
        let payload = {| name = name; score = score |} |> toJson

        let init =
            [ "method" ==> "POST"
              "headers"
              ==> createObj [ "Content-Type" ==> "application/json" ]
              "body" ==> payload ]
            |> createObj

        let! response = fetch "/api/highscore" (Some init)

        match response?ok with
        | true ->
            let! text = response?text () |> unbox<JS.Promise<string>>
            log "HighScore" "Score saved successfully."
            return text |> ofJson<HighScore> |> Some
        | false ->
            let status: int = response?status |> unbox<int>
            log "HighScore" $"Failed to save score (status {status})."
            return None
    }

let fetchVocabulary (_: unit) =
    promise {
        log "Vocabulary" "Requesting new vocabulary entry."
        let! response =
            fetch "/api/vocabulary" None
            |> withTimeout 5000

        match response?ok with
        | true ->
            let! text = response?text () |> unbox<JS.Promise<string>>
            log "Vocabulary" $"Received vocabulary payload: {text}"

            let raw = text |> JS.JSON.parse

            let entry: VocabularyEntry =
                { Topic = raw?topic |> string
                  Language1 = raw?language1 |> string
                  Language2 = raw?language2 |> string
                  Example = raw?example |> string }

            log "Vocabulary" $"Parsed entry topic '{entry.Topic}'."
            return entry
        | false ->
            let status: int = response?status |> unbox<int>
            log "Vocabulary" $"Failed to fetch vocabulary (status {status}); falling back."
            return defaultVocabularyEntry
    }

let fetchScores (_: unit) =
    promise {
        log "Scores" "Requesting scoreboard."
        let! response =
            fetch "/api/scores" None
            |> withTimeout 5000

        match response?ok with
        | true ->
            let! text = response?text () |> unbox<JS.Promise<string>>
            log "Scores" $"Received scoreboard payload: {text}"

            let entries =
                text
                |> JS.JSON.parse
                |> unbox<obj array>
                |> Array.choose (fun raw ->
                    let nameValue =
                        match isNullOrUndefined raw?name with
                        | true -> "Anonymous"
                        | false ->
                            let name: string = raw?name
                            match String.IsNullOrWhiteSpace name with
                            | true -> "Anonymous"
                            | false -> name.Trim()

                    let scoreValue =
                        match isNullOrUndefined raw?score with
                        | true -> 0
                        | false ->
                            let score: float = raw?score
                            score |> Math.Round |> int

                    Some { Name = nameValue; Score = scoreValue })
                |> Array.toList

            return entries
        | false ->
            let status: int = response?status |> unbox<int>
            return raise (Exception $"Failed to fetch scores (status {status}).")
    }

let fetchHighScoreCmd =
    Cmd.OfPromise.either fetchHighScore () HighScoreLoaded (fun _ -> HighScoreLoaded None)

let saveHighScoreCmd name score =
    Cmd.OfPromise.either saveHighScore (name, score) HighScoreSaved (fun _ -> HighScoreSaved None)

let fetchVocabularyCmd =
    Cmd.OfPromise.either
        fetchVocabulary
        ()
        VocabularyLoaded
        (fun _ -> defaultVocabularyEntry |> VocabularyLoaded)

let fetchScoresCmd =
    Cmd.OfPromise.either fetchScores () ScoresLoaded (fun ex -> ScoresFailed(ex.Message))

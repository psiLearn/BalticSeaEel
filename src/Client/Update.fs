module Eel.Client.Update

open System
open Browser.Dom
open Browser.Types
open Elmish
open Fable.Core.JsInterop
open Eel.Client.Model
open Eel.Client.Api
open Shared
open Shared.Game

module JS = Fable.Core.JS

let private cleanupKey = "__eelCleanup"
let private highScoreStorageKey = "eel:highscore"

let private log category message = printfn "[Update|%s] %s" category message

let private readStoredHighScore () =
    try
        let storage = window.localStorage
        let raw = storage.getItem(highScoreStorageKey)

        if isNull raw then
            None
        else
            raw
            |> JS.JSON.parse
            |> unbox<HighScore>
            |> Some
    with _ ->
        None

let private writeStoredHighScore (score: HighScore option) =
    try
        let storage = window.localStorage

        match score with
        | Some hs -> storage.setItem(highScoreStorageKey, JS.JSON.stringify hs)
        | None -> storage.removeItem(highScoreStorageKey)
    with _ -> ()

let private tryCleanupPrevious () =
    let cleanupObj: obj = window?(cleanupKey)

    if not (isNullOrUndefined cleanupObj) then
        match cleanupObj with
        | :? (unit -> unit) as cleanup -> cleanup ()
        | _ -> ()

        window?cleanupKey <- null

let startLoopCmd (speedMs: int) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        log "Loop" $"Starting loop with speed {speedMs} ms."
        tryCleanupPrevious ()

        let intervalId = window.setInterval ((fun _ -> dispatch Tick), speedMs)

        let handleKey (ev: KeyboardEvent) =
            let isTypingTarget =
                match ev.target with
                | :? HTMLInputElement
                | :? HTMLTextAreaElement
                | :? HTMLSelectElement -> true
                | _ -> false

            if isTypingTarget then
                ()
            else
                match ev.key with
                | "ArrowUp"
                | "w"
                | "W" ->
                    ev.preventDefault ()
                    dispatch (ChangeDirection Direction.Up)
                | "ArrowDown"
                | "s"
                | "S" ->
                    ev.preventDefault ()
                    dispatch (ChangeDirection Direction.Down)
                | "ArrowLeft"
                | "a"
                | "A" ->
                    ev.preventDefault ()
                    dispatch (ChangeDirection Direction.Left)
                | "ArrowRight"
                | "d"
                | "D" ->
                    ev.preventDefault ()
                    dispatch (ChangeDirection Direction.Right)
                | " " ->
                    if ev.repeat |> not then
                        dispatch Restart
                | _ -> ()

        let handleKeyListener = fun (ev: Event) -> handleKey (ev :?> KeyboardEvent)

        window.addEventListener ("keydown", handleKeyListener)

        let rec cleanup () =
            log "Loop" "Cleaning up loop and event handlers."
            window.clearInterval intervalId
            window.removeEventListener ("keydown", handleKeyListener)
            window.removeEventListener ("beforeunload", unloadHandler)
            window?cleanupKey <- null

        and unloadHandler (_: Event) = cleanup ()

        window.addEventListener ("beforeunload", unloadHandler)
        window?cleanupKey <- cleanup)

let init () =
    log "Init" "Initializing model and starting commands."
    let storedHighScore = readStoredHighScore () |> Option.orElse initModel.HighScore
    let model = { initModel with HighScore = storedHighScore }

    model,
    Cmd.batch [ fetchHighScoreCmd
                fetchVocabularyCmd
                startLoopCmd initModel.SpeedMs ]

let update msg model =
    log "Update" $"Processing message: {msg}"
    match msg with
    | Tick ->
        let nextGame = Game.move model.Game
        let updatedHighScore =
            match model.HighScore with
            | Some hs when nextGame.Score > hs.Score ->
                let updated = { hs with Score = nextGame.Score }
                writeStoredHighScore (Some updated)
                Some updated
            | None when nextGame.Score > 0 ->
                let name = if String.IsNullOrWhiteSpace model.PlayerName then "Anonymous" else model.PlayerName
                let updated = { Name = name; Score = nextGame.Score }
                writeStoredHighScore (Some updated)
                Some updated
            | _ -> model.HighScore

        let updatedModel =
            { model with
                Game = nextGame
                HighScore = updatedHighScore }

        if nextGame.GameOver then
            log "Game" "Game over detected."
            updatedModel, Cmd.none
        else
            let collectedLetter = nextGame.Score > model.Game.Score

            if collectedLetter && model.TargetText.Length > 0 then
                let nextIndex = model.TargetIndex + 1

                if nextIndex >= model.TargetText.Length then
                    let newSpeed =
                        model.SpeedMs
                        |> float
                        |> (*) 0.95
                        |> int
                        |> max 50

                    let restartedGame =
                        Game.restart ()
                        |> fun g -> { g with Score = nextGame.Score }

                    log "Game" $"Phrase completed. Speed increased to {newSpeed}."

                    { updatedModel with
                        Game = restartedGame
                        TargetText = ""
                        TargetIndex = 0
                        Vocabulary = None
                        UseExampleNext = not model.UseExampleNext
                        SpeedMs = newSpeed },
                    Cmd.batch [ startLoopCmd newSpeed
                                fetchVocabularyCmd ]
                else
                    log "Game" $"Collected letter at index {nextIndex}."
                    { updatedModel with TargetIndex = nextIndex }, Cmd.none
            else
                updatedModel, Cmd.none
    | ChangeDirection direction -> { model with Game = Game.changeDirection direction model.Game }, Cmd.none
    | Restart ->
        log "Game" "Restart requested."
        let resetModel =
            { model with
                Game = Game.restart ()
                Error = None
                Vocabulary = None
                TargetText = ""
                TargetIndex = 0
                UseExampleNext = false
                SpeedMs = initialSpeed }

        resetModel,
        Cmd.batch [ startLoopCmd resetModel.SpeedMs
                    fetchVocabularyCmd ]
    | SetPlayerName name -> { model with PlayerName = name }, Cmd.none
    | SaveHighScore ->
        if model.PlayerName.Trim() = "" then
            { model with Error = Some "Enter a name before saving your score." }, Cmd.none
        elif not model.Game.GameOver then
            { model with Error = Some "You can only submit a score after the game ends." }, Cmd.none
        elif model.Saving then
            model, Cmd.none
        else
            { model with
                Saving = true
                Error = None },
            saveHighScoreCmd model.PlayerName model.Game.Score
    | HighScoreSaved result ->
        match result with
        | Some score ->
            log "HighScore" $"High score saved for {score.Name} ({score.Score})."
            writeStoredHighScore (Some score)
            { model with
                Saving = false
                HighScore = Some score
                Error = None },
            Cmd.none
        | None ->
            log "HighScore" "Failed to save high score."
            { model with
                Saving = false
                Error = Some "Unable to save your high score. Please try again." },
            Cmd.none
    | HighScoreLoaded hs ->
        let combined =
            match hs, model.HighScore with
            | Some serverScore, Some localScore when localScore.Score > serverScore.Score ->
                log "HighScore" $"Keeping higher local score ({localScore.Score}) over server score ({serverScore.Score})."
                Some localScore
            | Some serverScore, _ ->
                let sanitizedName =
                    if String.IsNullOrWhiteSpace serverScore.Name then "Anonymous" else serverScore.Name

                log "HighScore" $"Loaded server high score {sanitizedName} ({serverScore.Score})."
                Some { serverScore with Name = sanitizedName }
            | None, _ ->
                log "HighScore" "Server returned no high score; retaining local snapshot."
                model.HighScore

        writeStoredHighScore combined

        { model with HighScore = combined }, Cmd.none
    | VocabularyLoaded entry ->
        log "Vocabulary" $"Loaded vocabulary entry for topic '{entry.Topic}'."
        let target =
            if model.UseExampleNext then
                entry.Example
            else
                $"{entry.Language1} - {entry.Language2}"

        let cleaned =
            let trimmed = target.Trim()
            if trimmed = "" then target else trimmed

        let finalTarget =
            if cleaned.Trim() = "" then
                fallbackTargetText
            else
                cleaned

        log "Vocabulary" $"Using target phrase '{finalTarget}'."

        { model with
            Vocabulary = Some entry
            TargetText = finalTarget
            TargetIndex = 0
            Error = None },
        Cmd.none
    | VocabularyFailed message ->
        log "Vocabulary" $"Failed to load vocabulary: {message}"
        { model with
            Error = Some message
            TargetText = fallbackTargetText
            TargetIndex = 0 },
        Cmd.none

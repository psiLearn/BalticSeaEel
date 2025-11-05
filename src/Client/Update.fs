module Eel.Client.Update

open Browser.Dom
open Browser.Types
open Elmish
open Fable.Core.JsInterop
open Eel.Client.Model
open Eel.Client.Api
open Shared
open Shared.Game

let private cleanupKey = "__eelCleanup"

let private log category message = printfn "[Update|%s] %s" category message

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
    initModel,
    Cmd.batch [ fetchHighScoreCmd
                fetchVocabularyCmd
                startLoopCmd initModel.SpeedMs ]

let update msg model =
    log "Update" $"Processing message: {msg}"
    match msg with
    | Tick ->
        let nextGame = Game.move model.Game
        let updatedModel = { model with Game = nextGame }

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
                        |> (*) 0.9
                        |> int
                        |> max 50

                    let restartedGame = Game.restart ()

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
    | HighScoreLoaded hs -> { model with HighScore = hs }, Cmd.none
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

module Eel.Client.Update

open System
open Browser.Dom
open Browser.Types
open Elmish
open Fable.Core.JsInterop
open Eel.Client.Model
open Eel.Client.Api
open Eel.Client.GameLoop
open Shared
open Shared.Game

module JS = Fable.Core.JS

let private cleanupKey = "__eelCleanup"
let private countdownKey = "__eelCountdown"
let private tickKey = "__eelTick"
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

let private tryStopATick key =
    let countdownObj: obj = window?(key)

    if not (isNullOrUndefined countdownObj) then
        match countdownObj with
        | :? float as id ->
            window.clearInterval id
            window.clearTimeout id
        | :? int as id ->
            window.clearInterval id
            window.clearTimeout id
        | _ -> ()

        window?key <- null
//        window?loopTokenKey <- null
let private tryStopCountdown () =
    tryStopATick countdownKey
let private tryStopTick () =
    tryStopATick tickKey

let private tryCleanupPrevious () =
    let cleanupObj: obj = window?(cleanupKey)

    if not (isNullOrUndefined cleanupObj) then
        match cleanupObj with
        | :? (unit -> unit) as cleanup -> cleanup ()
        | _ -> ()

        window?cleanupKey <- null
        window?loopTokenKey <- null

let private stopLoopCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> tryCleanupPrevious ())

let scheduleCountdownCmd () : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        tryStopCountdown ()
        let timeoutId =
            window.setTimeout(
                (fun _ -> dispatch CountdownTick),
                1000)
        window?countdownKey <- timeoutId)

let private stopCountdownCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> tryStopCountdown ())

let startLoopCmd (speedMs: int) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        log "Loop" $"Starting loop with speed {speedMs} ms."
        tryCleanupPrevious ()

        let loopToken = System.Guid.NewGuid().ToString()
        window?loopTokenKey <- loopToken

        let intervalId =
            window.setInterval (
                (fun _ ->
                    if window?loopTokenKey = loopToken then
                        dispatch Tick),
                speedMs)
        window?(tickKey) <- intervalId

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
            window.clearTimeout intervalId
            window.removeEventListener ("keydown", handleKeyListener)
            window.removeEventListener ("beforeunload", unloadHandler)
            window?loopTokenKey <- null
            tryStopTick()
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
                scheduleCountdownCmd () ]

let update msg model =
    match msg with
    | CountdownTick ->
        log "Countdown" "Tick."
        if model.GameRunning then
            model, Cmd.none
        else
            let remaining = max 0 (model.CountdownMs - 1000)
            if remaining <= 0 then
                tryStopCountdown ()
                { model with CountdownMs = 0 }, Cmd.ofMsg CountdownFinished
            else
                { model with CountdownMs = remaining }, scheduleCountdownCmd ()
    | CountdownFinished ->
        log "Countdown" "Finished."
        if model.GameRunning || model.CountdownMs > 0 then
            model, Cmd.none
        else
            let updatedModel = { model with GameRunning = true; CountdownMs = 0 }
            log "Loop" $"Countdown complete. Starting loop at {model.SpeedMs} ms."
            updatedModel, Cmd.batch [ stopCountdownCmd
                                      startLoopCmd model.SpeedMs ]
    | Tick when not model.GameRunning ->
        tryStopTick ()
        log "Loop" "Ignoring tick while game is paused."
        model, Cmd.none
    | Tick ->
        log "Loop" "Processing tick."
        let result = GameLoop.applyTick model

        result.Events
        |> List.iter (function
            | GameOver -> log "Game" "Game over detected."
            | LetterCollected idx -> log "Game" $"Collected letter at index {idx}."
            | PhraseCompleted newSpeed -> log "Game" $"Phrase completed. Speed increased to {newSpeed}.")

        result.Effects
        |> List.iter (function
            | CleanupLoop -> tryCleanupPrevious ()
            | PersistHighScore score -> writeStoredHighScore score
            | _ -> ())

        let commands =
            result.Effects
            |> List.choose (function
                | StopLoop -> Some stopLoopCmd
                | ScheduleCountdown -> Some (scheduleCountdownCmd ())
                | FetchVocabulary -> Some fetchVocabularyCmd
                | _ -> None)

        let command =
            match commands with
            | [] -> Cmd.none
            | [ single ] -> single
            | _ -> Cmd.batch commands

        result.Model, command
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
                SpeedMs = initialSpeed
                CountdownMs = 5000
                GameRunning = false }

        resetModel,
        Cmd.batch [ stopLoopCmd
                    stopCountdownCmd
                    scheduleCountdownCmd ()
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
            let trimmedName = model.PlayerName.Trim()
            let safeName = if trimmedName = "" then "Anonymous" else trimmedName
            { model with
                Saving = true
                Error = None },
            saveHighScoreCmd safeName model.Game.Score
    | HighScoreSaved result ->
        match result with
        | Some score ->
            log "HighScore" $"High score saved for {score.Name} ({score.Score})."
            let sanitizedName =
                if String.IsNullOrWhiteSpace score.Name then "Anonymous" else score.Name

            let sanitized =
                { score with Name = sanitizedName }

            writeStoredHighScore (Some sanitized)
            { model with
                Saving = false
                HighScore = Some sanitized
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

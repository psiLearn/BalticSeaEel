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
let private tickIntervalMs = 40

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

let private withFreshLastEel (model: Model) =
    { model with LastEel = model.Game.Eel }

let private movementProgress model =
    if model.GameRunning && model.SpeedMs > 0 then
        model.PendingMoveMs
        |> float
        |> fun v -> v / float model.SpeedMs
        |> max 0.0
        |> min 0.999
    else
        0.0

let private handleTickResult (result: GameLoop.TickResult) =
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

let startLoopCmd () : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        log "Loop" $"Starting loop with tick interval {tickIntervalMs} ms."
        tryCleanupPrevious ()

        let loopToken = System.Guid.NewGuid().ToString()
        window?loopTokenKey <- loopToken

        let intervalId =
            window.setInterval (
                (fun _ ->
                    if window?loopTokenKey = loopToken then
                        dispatch Tick),
                tickIntervalMs)
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
    let model =
        { initModel with HighScore = storedHighScore }
        |> ensureFoodsForModel

    model,
    Cmd.batch [ fetchHighScoreCmd
                fetchVocabularyCmd
                fetchScoresCmd ]

let update msg model =
    match msg with
    | StartGame when model.ScoresLoading ->
        log "Game" "Start requested while scores still loading; waiting."
        model, Cmd.none
    | StartGame when not model.SplashVisible ->
        log "Game" "Start requested but splash already dismissed."
        model, Cmd.none
    | StartGame ->
        log "Game" "Start requested from splash screen."
        let updated =
            { model with
                SplashVisible = false
                CountdownMs = 5000
                GameRunning = false
                PendingMoveMs = 0
                QueuedDirection = None
                Error = None
                ScoresError = None }
            |> withFreshLastEel
        updated, scheduleCountdownCmd ()
    | CountdownTick ->
        log "Countdown" "Tick."
        if model.SplashVisible || model.GameRunning then
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
        if model.SplashVisible || model.GameRunning || model.CountdownMs > 0 then
            model, Cmd.none
        else
            let updatedModel =
                { model with
                    GameRunning = true
                    CountdownMs = 0
                    PendingMoveMs = 0
                    QueuedDirection = None }
                |> ensureFoodsForModel
                |> withFreshLastEel
            log "Loop" $"Countdown complete. Starting loop at {model.SpeedMs} ms."
            updatedModel, Cmd.batch [ stopCountdownCmd
                                      startLoopCmd () ]
    | Tick when not model.GameRunning ->
        tryStopTick ()
        log "Loop" "Ignoring tick while game is paused."
        { model with
            PendingMoveMs = 0
            QueuedDirection = None
            LastEel = model.Game.Eel },
        Cmd.none
    | Tick ->
        if not model.GameRunning then
            { model with PendingMoveMs = 0; QueuedDirection = None }, Cmd.none
        else
            let accumulator = model.PendingMoveMs + tickIntervalMs

            if accumulator < model.SpeedMs then
                { model with PendingMoveMs = accumulator }, Cmd.none
            else
                let modelForMove =
                    match model.QueuedDirection with
                    | Some queued ->
                        { model with
                            Game = Game.changeDirection queued model.Game
                            QueuedDirection = None }
                    | None -> model

                let startSegments = modelForMove.Game.Eel
                let result = GameLoop.applyTick modelForMove
                let updatedModel, cmd = handleTickResult result
                let remaining =
                    accumulator - model.SpeedMs
                    |> max 0
                    |> min (model.SpeedMs - 1)

                let updatedModel =
                    { updatedModel with
                        PendingMoveMs = remaining
                        LastEel = startSegments }

                updatedModel, cmd
    | ChangeDirection direction ->
        if model.GameRunning then
            let updatedQueued =
                match model.QueuedDirection with
                | Some existing when existing = direction -> model.QueuedDirection
                | _ -> Some direction

            if model.PendingMoveMs = 0 then
                let updatedGame = Game.changeDirection direction model.Game
                { model with
                    Game = updatedGame
                    LastEel = updatedGame.Eel
                    QueuedDirection = None },
                Cmd.none
            else
                { model with QueuedDirection = updatedQueued }, Cmd.none
        else
            let updatedGame = Game.changeDirection direction model.Game
            { model with
                Game = updatedGame
                LastEel = updatedGame.Eel
                QueuedDirection = Some direction },
            Cmd.none
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
                PendingMoveMs = 0
                QueuedDirection = None
                CountdownMs = 5000
                GameRunning = false
                BoardLetters = createBoardLetters () }
            |> ensureFoodsForModel
            |> withFreshLastEel

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
            ({ model with
                Saving = false
                HighScore = Some sanitized
                Error = None
                ScoresLoading = true
                SplashVisible = true
                CountdownMs = 5000
                GameRunning = false
                PendingMoveMs = 0
                QueuedDirection = None }
             |> withFreshLastEel),
            fetchScoresCmd
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

        let resetGame = { model.Game with Foods = [] }

        let updatedModel =
            { model with
                Vocabulary = Some entry
                TargetText = finalTarget
                TargetIndex = 0
                Error = None
                Game = resetGame
                BoardLetters = createBoardLetters () }
            |> ensureFoodsForModel
            |> withFreshLastEel

        updatedModel, Cmd.none
    | VocabularyFailed message ->
        log "Vocabulary" $"Failed to load vocabulary: {message}"
        let resetGame = { model.Game with Foods = [] }

        let updatedModel =
            { model with
                Error = Some message
                TargetText = fallbackTargetText
                TargetIndex = 0
                Game = resetGame
                BoardLetters = createBoardLetters () }
            |> ensureFoodsForModel
            |> withFreshLastEel

        updatedModel, Cmd.none
    | ScoresLoaded scores ->
        log "Scores" $"Loaded scoreboard with {scores.Length} entries."
        let sanitized =
            scores
            |> List.map (fun score ->
                let name = if String.IsNullOrWhiteSpace score.Name then "Anonymous" else score.Name.Trim()
                { score with Name = name })
            |> List.sortByDescending (fun score -> score.Score)

        let updatedHigh =
            match sanitized |> List.tryHead, model.HighScore with
            | Some top, None -> Some top
            | Some top, Some current when top.Score > current.Score -> Some top
            | _ -> model.HighScore

        let updatedModel =
            { model with
                Scores = sanitized
                ScoresLoading = false
                ScoresError = None
                HighScore = updatedHigh }

        updatedModel, Cmd.none
    | ScoresFailed message ->
        log "Scores" $"Failed to load scoreboard: {message}"
        { model with
            ScoresLoading = false
            ScoresError = Some message },
        Cmd.none

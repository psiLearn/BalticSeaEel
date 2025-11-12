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

module ModelState = Eel.Client.Model

module JS = Fable.Core.JS

let private cleanupKey = "__eelCleanup"
let private countdownKey = "__eelCountdown"
let private tickKey = "__eelTick"
let private highScoreStorageKey = "eel:highscore"
let private startHotkeyKey = "__eelStartHotkey"
let private tickIntervalMs = 40
let private startCountdownMs = Config.gameplay.StartCountdownMs
let private levelCountdownMs = Config.gameplay.LevelCountdownMs
let private highlightSpeedSegmentsPerMs = 0.004

let private log category message = printfn "[Update|%s] %s" category message

let private readStoredHighScore () =
    try
        let storage = window.localStorage
        let raw = storage.getItem(highScoreStorageKey)

        match raw with
        | null -> None
        | _ ->
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

#if FABLE_COMPILER
let private registerStartHotkey dispatch =
    let existing: obj = window?(startHotkeyKey)
    if isNull existing then
        let listener =
            fun (ev: Event) ->
                let keyEvent = ev :?> KeyboardEvent
                if keyEvent.key = " " then
                    keyEvent.preventDefault ()
                    dispatch StartGame

        window.addEventListener ("keydown", listener)
        window?(startHotkeyKey) <- listener

let private unregisterStartHotkey () =
    let existing: obj = window?(startHotkeyKey)
    if not (isNull existing) then
        let listener = existing :?> (Event -> unit)
        window.removeEventListener ("keydown", listener)
        window?(startHotkeyKey) <- null

let private enableStartHotkeyCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> registerStartHotkey dispatch)

let private disableStartHotkeyCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> unregisterStartHotkey ())
#else
let private enableStartHotkeyCmd : Cmd<Msg> = Cmd.none
let private disableStartHotkeyCmd : Cmd<Msg> = Cmd.none
#endif

let private updateHighlightWave trigger deltaMs (model: Model) =
    let activated =
        if trigger then
            { model with
                HighlightActive = true
                HighlightProgress = 0.0 }
        else
            model

    if activated.HighlightActive then
        let newProgress =
            activated.HighlightProgress
            + highlightSpeedSegmentsPerMs * float deltaMs

        let maxSegments = float (max 1 activated.Game.Eel.Length) + 1.0
        if newProgress >= maxSegments then
            { activated with
                HighlightActive = false
                HighlightProgress = 0.0 }
        else
            { activated with HighlightProgress = newProgress }
    else
        activated

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
    match (ModelState.isRunning model.Phase) && model.SpeedMs > 0 with
    | true ->
        model.PendingMoveMs
        |> float
        |> fun v -> v / float model.SpeedMs
        |> max 0.0
        |> min 0.999
    | false -> 0.0

let private advancePhrase model =
    let next, rest = Model.takeNextPhrase model.PhraseQueue
    { model with
        TargetText = next
        TargetIndex = 0
        PhraseQueue = rest
        NeedsNextPhrase = false }

let private ensureNextPhrase model =
    if model.NeedsNextPhrase || String.IsNullOrWhiteSpace model.TargetText then
        advancePhrase model
    else
        model

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

            match isTypingTarget with
            | true -> ()
            | false ->
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
                        ev.preventDefault ()
                        dispatch TogglePause
                | "r"
                | "R" ->
                    if ev.repeat |> not then
                        ev.preventDefault ()
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
    Cmd.batch [ enableStartHotkeyCmd
                fetchHighScoreCmd
                fetchVocabularyCmd
                fetchScoresCmd ]

let update msg model =
    match msg with
    | StartGame when model.ScoresLoading ->
        log "Game" "Start requested while scores still loading; waiting."
        model, Cmd.none
    | StartGame when model.Phase <> GamePhase.Splash ->
        log "Game" "Start requested but splash already dismissed."
        model, Cmd.none
    | StartGame ->
        log "Game" "Start requested from splash screen."
        let updated =
            { model with
                CountdownMs = Config.gameplay.StartCountdownMs
                Phase = GamePhase.Countdown
                PendingMoveMs = 0
                QueuedDirection = None
                Error = None
                ScoresError = None }
            |> withFreshLastEel
        updated, Cmd.batch [ 
            disableStartHotkeyCmd 
            scheduleCountdownCmd () 
        ]
    | TogglePause ->
        match model.Phase with
        | GamePhase.Running ->
            log "Game" "Pausing loop."
            tryStopTick ()
            { model with
                Phase = GamePhase.Paused
                PendingMoveMs = 0
                QueuedDirection = None
                LastEel = model.Game.Eel },
            Cmd.none
        | GamePhase.Paused ->
            log "Game" "Resuming loop."
            { model with
                Phase = GamePhase.Running
                PendingMoveMs = 0
                QueuedDirection = None
                LastEel = model.Game.Eel },
            startLoopCmd ()
        | _ -> model, Cmd.none
    | CountdownTick ->
        log "Countdown" "Tick."
        match model.Phase with
        | GamePhase.Countdown ->
            let remaining = max 0 (model.CountdownMs - 1000)
            if remaining <= 0 then
                tryStopCountdown ()
                { model with CountdownMs = 0 }, Cmd.ofMsg CountdownFinished
            else
                { model with CountdownMs = remaining }, scheduleCountdownCmd ()
        | _ -> model, Cmd.none
    | CountdownFinished ->
        log "Countdown" "Finished."
        match model.Phase with
        | GamePhase.Countdown when model.CountdownMs <= 0 ->
            let updatedModel =
                { model with
                    Phase = GamePhase.Running
                    CountdownMs = 0
                    PendingMoveMs = 0
                    QueuedDirection = None }
                |> ensureNextPhrase
                |> ensureFoodsForModel
                |> withFreshLastEel
            log "Loop" $"Countdown complete. Starting loop at {model.SpeedMs} ms."
            updatedModel, Cmd.batch [ stopCountdownCmd
                                      startLoopCmd () ]
        | _ -> model, Cmd.none
    | Tick when not (ModelState.isRunning model.Phase) ->
        tryStopTick ()
        log "Loop" "Ignoring tick while game is paused."
        { model with
            PendingMoveMs = 0
            QueuedDirection = None
            LastEel = model.Game.Eel },
        Cmd.none
    | Tick ->
        match ModelState.isRunning model.Phase with
        | false -> { model with PendingMoveMs = 0; QueuedDirection = None }, Cmd.none
        | true ->
            let accumulator = model.PendingMoveMs + tickIntervalMs

            match accumulator < model.SpeedMs with
            | true ->
                let updated =
                    { model with PendingMoveMs = accumulator }
                    |> updateHighlightWave false tickIntervalMs
                updated, Cmd.none
            | false ->
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

                let letterCollected =
                    result.Events
                    |> List.exists (function
                        | LetterCollected _ -> true
                        | _ -> false)

                let updatedModel =
                    { updatedModel with
                        PendingMoveMs = remaining
                        LastEel = startSegments }
                    |> updateHighlightWave letterCollected accumulator

                updatedModel, cmd
    | ChangeDirection direction ->
        let currentDirection = model.Game.Direction

        if direction = currentDirection then
            model, Cmd.none
        else
            match ModelState.isRunning model.Phase with
            | true ->
                let updatedQueued =
                    match model.QueuedDirection with
                    | Some existing when existing = direction -> model.QueuedDirection
                    | _ -> Some direction

                match model.PendingMoveMs = 0 with
                | true ->
                    let updatedGame = Game.changeDirection direction model.Game
                    { model with
                        Game = updatedGame
                        LastEel = updatedGame.Eel
                        QueuedDirection = None },
                    Cmd.none
                | false -> { model with QueuedDirection = updatedQueued }, Cmd.none
            | false ->
                let updatedGame = Game.changeDirection direction model.Game
                { model with
                    Game = updatedGame
                    LastEel = updatedGame.Eel
                    QueuedDirection = Some direction },
                Cmd.none
    | Restart ->
        log "Game" "Restart requested."
        let restartQueue =
            if String.IsNullOrWhiteSpace model.TargetText then model.PhraseQueue
            else model.TargetText :: model.PhraseQueue
        let resetModel =
            { model with
                Game = Game.restart ()
                Error = None
                TargetText = ""
                TargetIndex = 0
                SpeedMs = initialSpeed
                PendingMoveMs = 0
                QueuedDirection = None
                CountdownMs = levelCountdownMs
                Phase = GamePhase.Countdown
                PhraseQueue = restartQueue
                BoardLetters = createBoardLetters () }
            |> advancePhrase
            |> ensureFoodsForModel
            |> withFreshLastEel

        resetModel,
        Cmd.batch [ stopLoopCmd
                    stopCountdownCmd
                    scheduleCountdownCmd ()
                    fetchVocabularyCmd ]
    | SetPlayerName name -> { model with PlayerName = name }, Cmd.none
    | SaveHighScore ->
        match () with
        | _ when model.PlayerName.Trim() = "" ->
            { model with Error = Some "Enter a name before saving your score." }, Cmd.none
        | _ when not model.Game.GameOver ->
            { model with Error = Some "You can only submit a score after the game ends." }, Cmd.none
        | _ when ModelState.isSaving model.Phase -> model, Cmd.none
        | _ ->
            let trimmedName = model.PlayerName.Trim()
            let safeName =
                match trimmedName with
                | "" -> "Anonymous"
                | _ -> trimmedName

            { model with
                Phase = GamePhase.SavingHighScore
                Error = None },
            saveHighScoreCmd safeName model.Game.Score
    | HighScoreSaved result ->
        match result with
        | Some score ->
            log "HighScore" $"High score saved for {score.Name} ({score.Score})."
            let sanitizedName =
                match String.IsNullOrWhiteSpace score.Name with
                | true -> "Anonymous"
                | false -> score.Name

            let sanitized =
                { score with Name = sanitizedName }

            let mergedScores =
                let existingWithoutPlayer =
                    model.Scores
                    |> List.filter (fun entry ->
                        not (
                            entry.Name.Equals(
                                sanitized.Name,
                                StringComparison.OrdinalIgnoreCase
                            )))

                sanitized :: existingWithoutPlayer
                |> List.sortByDescending (fun entry -> entry.Score)
                |> List.truncate 10

            writeStoredHighScore (Some sanitized)
            ({ model with
                Phase = GamePhase.Splash
                HighScore = Some sanitized
                Error = None
                Scores = mergedScores
                ScoresLoading = false
                CountdownMs = startCountdownMs
                PendingMoveMs = 0
                QueuedDirection = None }
             |> withFreshLastEel),
            Cmd.batch [ enableStartHotkeyCmd
                        fetchScoresCmd ]
        | None ->
            log "HighScore" "Failed to save high score."
            { model with
                Phase = GamePhase.GameOver
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
                    match String.IsNullOrWhiteSpace serverScore.Name with
                    | true -> "Anonymous"
                    | false -> serverScore.Name

                log "HighScore" $"Loaded server high score {sanitizedName} ({serverScore.Score})."
                Some { serverScore with Name = sanitizedName }
            | None, _ ->
                log "HighScore" "Server returned no high score; retaining local snapshot."
                model.HighScore

        writeStoredHighScore combined

        { model with HighScore = combined }, Cmd.none
    | VocabularyLoaded entry ->
        log "Vocabulary" $"Loaded vocabulary entry for topic '{entry.Topic}'."
        let resetGame = { model.Game with Foods = [] }
        let phraseQueue =
            entry
            |> Model.phrasesFromVocabulary
            |> Model.preparePhraseQueue
        let nextPhrase, remainingQueue = Model.takeNextPhrase phraseQueue

        let updatedModel =
            { model with
                Vocabulary = Some entry
                TargetText = nextPhrase
                TargetIndex = 0
                PhraseQueue = remainingQueue
                NeedsNextPhrase = false
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
                TargetText = Model.fallbackTargetText
                TargetIndex = 0
                NeedsNextPhrase = false
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
                let name =
                    match String.IsNullOrWhiteSpace score.Name with
                    | true -> "Anonymous"
                    | false -> score.Name.Trim()
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

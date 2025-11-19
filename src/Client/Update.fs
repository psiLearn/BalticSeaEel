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
let private celebrationDelayKey = "__eelCelebrationDelay"
let private loopTokenKey = "__eelLoopToken"
let private resizeHandlerKey = "__eelResize"
let private highScoreStorageKey = "eel:highscore"
let private startHotkeyKey = "__eelStartHotkey"
let private tickIntervalMs = 40
let private startCountdownMs = Config.gameplay.StartCountdownMs
let private levelCountdownMs = Config.gameplay.LevelCountdownMs
let private celebrationDelayMs = Config.gameplay.CelebrationDelayMs
let private countdownStepMs = 1000
let private minTickDeltaMs = 1
let private maxTickDeltaMs = 500
let private foodBurstConfig = Config.gameplay.FoodBurst
let private highlightSpeedSegmentsPerMs = foodBurstConfig.WaveSpeedSegmentsPerMs
let private maxDirectionQueue = 3

module private WindowStore =
#if FABLE_COMPILER
    let tryGet<'T> key =
        let raw: obj = window?(key)
        if isNullOrUndefined raw then None else Some(unbox<'T> raw)

    let set key (value: obj) = window?(key) <- value
    let clear key = window?(key) <- null
#else
    let tryGet<'T> _ = None
    let set _ _ = ()
    let clear _ = ()
#endif

let private log category message = printfn "[Update|%s] %s" category message

let private updateGameplay updater model =
    { model with Gameplay = updater model.Gameplay }

let private updateIntermission updater model =
    { model with Intermission = updater model.Intermission }

let private updateCelebration updater model =
    { model with Celebration = updater model.Celebration }

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
    match WindowStore.tryGet<Event -> unit> startHotkeyKey with
    | Some _ -> ()
    | None ->
        let listener =
            fun (ev: Event) ->
                let keyEvent = ev :?> KeyboardEvent
                if keyEvent.key = " " then
                    keyEvent.preventDefault ()
                    dispatch StartGame

        window.addEventListener ("keydown", listener)
        WindowStore.set startHotkeyKey listener

let private enableStartHotkeyCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> registerStartHotkey dispatch)
#else
let private enableStartHotkeyCmd : Cmd<Msg> = Cmd.none
#endif

#if FABLE_COMPILER
let private registerResizeListener dispatch =
    match WindowStore.tryGet<Event -> unit> resizeHandlerKey with
    | Some _ -> ()
    | None ->
        let handler =
            fun (_: Event) ->
                let width = window.innerWidth |> float
                let height = window.innerHeight |> float
                dispatch (WindowResized(width, height))

        window.addEventListener ("resize", handler)
        WindowStore.set resizeHandlerKey handler
        dispatch (WindowResized(window.innerWidth |> float, window.innerHeight |> float))

let private enableResizeListenerCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> registerResizeListener dispatch)

let private unregisterResizeListener () =
    match WindowStore.tryGet<Event -> unit> resizeHandlerKey with
    | Some handler ->
        window.removeEventListener ("resize", handler)
        WindowStore.clear resizeHandlerKey
    | None -> ()

#else
let private enableResizeListenerCmd : Cmd<Msg> = Cmd.none
#endif

let private updateHighlightWave trigger deltaMs (model: Model) =
    if not foodBurstConfig.Enabled then
        model |> updateGameplay (fun g -> { g with HighlightWaves = [] })
    else
        let progressedWaves =
            model.Gameplay.HighlightWaves
            |> List.map (fun wave -> wave + highlightSpeedSegmentsPerMs * float deltaMs)

        let withNewWave =
            if trigger then
                0.0 :: progressedWaves
            else
                progressedWaves

        let limitedWaves =
            match foodBurstConfig.MaxConcurrentWaves with
            | Some limit when limit > 0 -> withNewWave |> List.truncate limit
            | _ -> withNewWave

        let maxSegments = float (max 1 model.Game.Eel.Length) + 1.0

        let remainingWaves =
            limitedWaves
            |> List.filter (fun wave -> wave < maxSegments)

        model |> updateGameplay (fun g -> { g with HighlightWaves = remainingWaves })

let private tryStopATick key =
#if FABLE_COMPILER
    match WindowStore.tryGet<obj> key with
    | Some countdownObj ->
        match countdownObj with
        | :? float as id ->
            window.cancelAnimationFrame id
            window.clearInterval id
            window.clearTimeout id
        | :? int as id ->
            window.cancelAnimationFrame (float id)
            window.clearInterval id
            window.clearTimeout id
        | _ -> ()

        WindowStore.clear key
    | None -> ()
#else
    ()
#endif

let private tryStopCountdown () =
    tryStopATick countdownKey
let private tryStopTick () =
    tryStopATick tickKey
let private tryStopCelebrationDelay () =
    tryStopATick celebrationDelayKey

let private isOppositeDirection a b =
    match a, b with
    | Direction.Up, Direction.Down
    | Direction.Down, Direction.Up
    | Direction.Left, Direction.Right
    | Direction.Right, Direction.Left -> true
    | _ -> false

let private enqueueDirection queue direction =
    let appended = queue @ [ direction ]
    if appended.Length <= maxDirectionQueue then
        appended
    else
        appended |> List.skip (appended.Length - maxDirectionQueue)

let private sanitizeViewportDimension fallback value =
    if Double.IsNaN value || Double.IsInfinity value || value <= 0.0 then
        fallback
    else
        value

let private tryCleanupPrevious () =
    match WindowStore.tryGet<unit -> unit> cleanupKey with
    | Some cleanup ->
        cleanup ()
        WindowStore.clear cleanupKey
        WindowStore.clear loopTokenKey
    | None ->
        WindowStore.clear loopTokenKey
#if FABLE_COMPILER
    unregisterResizeListener ()
#endif

let private stopLoopCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> tryCleanupPrevious ())

let scheduleCountdownCmd () : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        tryStopCountdown ()
        let timeoutId =
            window.setTimeout(
                (fun _ -> dispatch CountdownTick),
                countdownStepMs)
        WindowStore.set countdownKey timeoutId)

let private stopCountdownCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> tryStopCountdown ())

let private scheduleCelebrationCmd delayMs : Cmd<Msg> =
#if FABLE_COMPILER
    Cmd.ofEffect (fun dispatch ->
        tryStopCelebrationDelay ()
        let timeoutId =
            window.setTimeout(
                (fun _ -> dispatch CelebrationDelayElapsed),
                delayMs)
        WindowStore.set celebrationDelayKey timeoutId)
#else
    ignore delayMs
    Cmd.none
#endif

let private stopCelebrationCmd : Cmd<Msg> =
#if FABLE_COMPILER
    Cmd.ofEffect (fun _ -> tryStopCelebrationDelay ())
#else
    Cmd.none
#endif

let private withFreshLastEel (model: Model) =
    model |> updateGameplay (fun g -> { g with LastEel = model.Game.Eel })

let private movementProgress model =
    match (ModelState.isRunning model.Phase) && model.Gameplay.SpeedMs > 0 with
    | true ->
        model.Gameplay.PendingMoveMs
        |> float
        |> fun v -> v / float model.Gameplay.SpeedMs
        |> max 0.0
        |> min 0.999
    | false -> 0.0

let private advancePhrase model =
    let next, rest = Model.takeNextPhrase model.PhraseQueue
    { model with
        TargetText = next
        TargetIndex = 0
        PhraseQueue = rest
        NeedsNextPhrase = false
        Celebration = { model.Celebration with LastPhrase = None } }

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

    let celebrationCommands =
        if result.Events |> List.exists (function PhraseCompleted _ -> true | _ -> false) then
            [ scheduleCelebrationCmd celebrationDelayMs ]
        elif result.Events |> List.exists (function GameOver -> true | _ -> false) then
            [ stopCelebrationCmd ]
        else
            []

    let allCommands = commands @ celebrationCommands

    let command =
        match allCommands with
        | [] -> Cmd.none
        | [ single ] -> single
        | _ -> Cmd.batch allCommands

    result.Model, command

let startLoopCmd () : Cmd<Msg> =
#if FABLE_COMPILER
    Cmd.ofEffect (fun dispatch ->
        log "Loop" $"Starting loop with target tick interval {tickIntervalMs} ms."
        tryCleanupPrevious ()

        let loopToken = System.Guid.NewGuid().ToString()
        WindowStore.set loopTokenKey loopToken

        let rec schedule lastTimestamp =
            let frameId =
                window.requestAnimationFrame(fun timestamp ->
                    match WindowStore.tryGet<string> loopTokenKey with
                    | Some value when value = loopToken ->
                        let elapsed = match lastTimestamp with | Some previous -> timestamp - previous | None -> float tickIntervalMs
                        let deltaMs =
                            elapsed
                            |> max (float minTickDeltaMs)
                            |> min (float maxTickDeltaMs)
                            |> int
                        dispatch (Tick deltaMs)
                        schedule (Some timestamp)
                    | _ -> ())

            WindowStore.set tickKey frameId

        schedule None

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
            tryStopTick()
            window.removeEventListener ("keydown", handleKeyListener)
            window.removeEventListener ("beforeunload", unloadHandler)
            WindowStore.clear loopTokenKey
            WindowStore.clear cleanupKey

        and unloadHandler (_: Event) = cleanup ()

        window.addEventListener ("beforeunload", unloadHandler)
        WindowStore.set cleanupKey cleanup)
#else
    Cmd.none
#endif

let private startCountdownModel (model: Model) =
    { model with
        Intermission = { model.Intermission with CountdownMs = startCountdownMs }
        Phase = GamePhase.Countdown
        Gameplay =
            { model.Gameplay with
                PendingMoveMs = 0
                DirectionQueue = [] }
        Error = None
        ScoresError = None
        Celebration = { model.Celebration with Visible = false } }
    |> withFreshLastEel

let private handleStartGame model =
    match model.ScoresLoading, model.Phase with
    | true, _ ->
        log "Game" "Start requested while scores still loading; waiting."
        model, Cmd.none
    | _, GamePhase.GameOver ->
        log "Game" "Start requested during game over; restarting."
        model, Cmd.ofMsg Restart
    | _, phase when phase <> GamePhase.Splash ->
        log "Game" "Start requested but splash already dismissed."
        model, Cmd.none
    | _ ->
        log "Game" "Start requested from splash screen."
        startCountdownModel model, scheduleCountdownCmd ()

let private handleTogglePause model =
    match model.Phase with
    | GamePhase.Running ->
        log "Game" "Pausing loop."
        tryStopTick ()
        { model with
            Phase = GamePhase.Paused
            Gameplay =
                { model.Gameplay with
                    PendingMoveMs = 0
                    DirectionQueue = []
                    LastEel = model.Game.Eel } },
        Cmd.none
    | GamePhase.Paused ->
        log "Game" "Resuming loop."
        { model with
            Phase = GamePhase.Running
            Gameplay =
                { model.Gameplay with
                    PendingMoveMs = 0
                    DirectionQueue = []
                    LastEel = model.Game.Eel } },
        startLoopCmd ()
    | _ -> model, Cmd.none

let private handleCountdownTick model =
    log "Countdown" "Tick."
    match model.Phase with
    | GamePhase.Countdown ->
        let remaining = max 0 (model.Intermission.CountdownMs - countdownStepMs)
        if remaining <= 0 then
            tryStopCountdown ()
            { model with Intermission = { model.Intermission with CountdownMs = 0 } }, Cmd.ofMsg CountdownFinished
        else
            { model with Intermission = { model.Intermission with CountdownMs = remaining } }, scheduleCountdownCmd ()
    | _ -> model, Cmd.none

let private handleCountdownFinished model =
    log "Countdown" "Finished."
    match model.Phase with
    | GamePhase.Countdown when model.Intermission.CountdownMs <= 0 ->
        let updatedModel =
            { model with
                Phase = GamePhase.Running
                Intermission = { model.Intermission with CountdownMs = 0 }
                Gameplay =
                    { model.Gameplay with
                        PendingMoveMs = 0
                        DirectionQueue = [] } }
            |> ensureNextPhrase
            |> ensureFoodsForModel
            |> withFreshLastEel

        log "Loop" $"Countdown complete. Starting loop at {model.Gameplay.SpeedMs} ms."
        updatedModel, Cmd.batch [ stopCountdownCmd
                                  startLoopCmd () ]
    | _ -> model, Cmd.none

let private handleCelebrationDelayElapsed model =
    let hasPhrase =
        model.Celebration.LastPhrase
        |> Option.exists (fun phrase -> not (String.IsNullOrWhiteSpace phrase))

    match model.Phase = GamePhase.Countdown && hasPhrase with
    | true ->
        { model with Celebration = { model.Celebration with Visible = true } }, Cmd.none
    | false ->
        model, stopCelebrationCmd

let private processTickMovement delta budget currentModel =
    let rec loop pending deltaBudget accModel commands =
        if pending < accModel.Gameplay.SpeedMs then
            let updated =
                { accModel with
                    Gameplay = { accModel.Gameplay with PendingMoveMs = pending } }
                |> updateHighlightWave false deltaBudget
            updated, pending, commands
        else
            let modelForMove =
                match accModel.Gameplay.DirectionQueue with
                | next :: rest ->
                    { accModel with
                        Game = Game.changeDirection next accModel.Game
                        Gameplay = { accModel.Gameplay with DirectionQueue = rest } }
                | [] -> accModel

            let startSegments = modelForMove.Game.Eel
            let result = GameLoop.applyTick modelForMove
            let updatedModel, cmd = handleTickResult result
            let remaining = max 0 (pending - accModel.Gameplay.SpeedMs)

            let letterCollected =
                result.Events
                |> List.exists (function
                    | LetterCollected _ -> true
                    | _ -> false)

            let refreshedModel =
                { updatedModel with
                    Gameplay =
                        { updatedModel.Gameplay with
                            PendingMoveMs = remaining
                            LastEel = startSegments } }
                |> updateHighlightWave letterCollected accModel.Gameplay.SpeedMs

            let consumedDelta = min deltaBudget accModel.Gameplay.SpeedMs
            loop remaining (max 0 (deltaBudget - consumedDelta)) refreshedModel (cmd :: commands)

    loop (currentModel.Gameplay.PendingMoveMs + delta) budget currentModel []

let private handleTick deltaMs model =
    if not (ModelState.isRunning model.Phase) then
        tryStopTick ()
        log "Loop" "Ignoring tick while game is paused."
        { model with
            Gameplay =
                { model.Gameplay with
                    PendingMoveMs = 0
                    DirectionQueue = []
                    LastEel = model.Game.Eel } },
        Cmd.none
    else
        let boundedDelta =
            deltaMs
            |> max minTickDeltaMs
            |> min maxTickDeltaMs
        let finalModel, remainder, cmdList = processTickMovement boundedDelta boundedDelta model

        let command =
            match cmdList |> List.rev with
            | [] -> Cmd.none
            | [ single ] -> single
            | _ -> Cmd.batch cmdList

        { finalModel with Gameplay = { finalModel.Gameplay with PendingMoveMs = remainder } }, command

let init () =
    log "Init" "Initializing model and starting commands."
    let storedHighScore = readStoredHighScore () |> Option.orElse initModel.HighScore
    let model =
        { initModel with HighScore = storedHighScore }
        |> ensureFoodsForModel

    model,
    Cmd.batch [ enableStartHotkeyCmd
                enableResizeListenerCmd
                fetchHighScoreCmd
                fetchVocabularyCmd
                fetchScoresCmd ]

let update msg model =
    match msg with
    | StartGame -> handleStartGame model
    | TogglePause -> handleTogglePause model
    | CountdownTick -> handleCountdownTick model
    | CountdownFinished -> handleCountdownFinished model
    | WindowResized (width, height) ->
        let safeWidth = sanitizeViewportDimension Model.defaultViewportWidth width
        let safeHeight = sanitizeViewportDimension Model.defaultViewportHeight height
        let widthDelta = abs (safeWidth - model.ViewportWidth)
        let heightDelta = abs (safeHeight - model.ViewportHeight)

        if widthDelta < 0.5 && heightDelta < 0.5 then
            model, Cmd.none
        else
            let updatedModel =
                { model with
                    ViewportWidth = safeWidth
                    ViewportHeight = safeHeight }

            updatedModel, Cmd.none
    | Tick deltaMs -> handleTick deltaMs model
    | ChangeDirection direction ->
        let queue = model.Gameplay.DirectionQueue
        let referenceDirection =
            match queue with
            | [] -> model.Game.Direction
            | _ -> queue |> List.last

        let ignoreInput =
            direction = referenceDirection
            || isOppositeDirection direction referenceDirection

        if ignoreInput then
            model, Cmd.none
        else
            let isRunning = ModelState.isRunning model.Phase
            let queueWasEmpty = List.isEmpty queue
            let updatedQueue =
                if isRunning then
                    enqueueDirection queue direction
                else
                    []

            let shouldUpdateDirectionNow = (not isRunning) || queueWasEmpty

            let updatedGame =
                if shouldUpdateDirectionNow then
                    Game.changeDirection direction model.Game
                else
                    model.Game

            { model with
                Game = updatedGame
                Gameplay =
                    { model.Gameplay with
                        LastEel = updatedGame.Eel
                        DirectionQueue = updatedQueue } },
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
                Gameplay =
                    { model.Gameplay with
                        SpeedMs = initialSpeed
                        PendingMoveMs = 0
                        DirectionQueue = [] }
                Intermission = { model.Intermission with CountdownMs = levelCountdownMs }
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
                Intermission = { model.Intermission with CountdownMs = startCountdownMs }
                Gameplay =
                    { model.Gameplay with
                        PendingMoveMs = 0
                        DirectionQueue = [] } }
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
    | CelebrationDelayElapsed -> handleCelebrationDelayElapsed model

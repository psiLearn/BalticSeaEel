module Eel.Client.GameLoop

open System
open Eel.Client.Model
open Shared
open Shared.Game

let private levelCountdownMs = Config.gameplay.LevelCountdownMs

type TickEvent =
    | GameOver
    | LetterCollected of int
    | PhraseCompleted of int

type TickEffect =
    | StopLoop
    | ScheduleCountdown
    | FetchVocabulary
    | CleanupLoop
    | PersistHighScore of HighScore option

type TickResult =
    { Model: Model
      Events: TickEvent list
      Effects: TickEffect list }

let private computeHighScore (playerName: string) (current: HighScore option) (nextGame: GameState) =
    match current with
    | Some existing when nextGame.Score > existing.Score ->
        let updated = { existing with Score = nextGame.Score }
        Some updated, [ PersistHighScore (Some updated) ]
    | None when nextGame.Score > 0 ->
        let trimmed = playerName.Trim()
        let name =
            match String.IsNullOrWhiteSpace trimmed with
            | true -> "Anonymous"
            | false -> trimmed

        let updated = { Name = name; Score = nextGame.Score }
        Some updated, [ PersistHighScore (Some updated) ]
    | _ -> current, []

let private phraseCompleted (model: Model) (currentGame: GameState) =
    let newSpeed =
        model.Gameplay.SpeedMs
        |> float
        |> (*) 0.95
        |> int
        |> max 50

    let restartedGame =
        Game.restart ()
        |> fun g ->
            { g with
                Score = currentGame.Score
                Direction = currentGame.Direction }

    { model with
        Game = restartedGame
        Gameplay = { model.Gameplay with SpeedMs = newSpeed }
        Intermission = { model.Intermission with CountdownMs = levelCountdownMs }
        Phase = GamePhase.Countdown
        NeedsNextPhrase = true
        TargetIndex = 0
        Celebration =
            { model.Celebration with
                LastPhrase = Some model.TargetText
                Visible = false } },
    newSpeed

let maxVisibleFoods = Config.gameplay.MaxVisibleFoods

let ensureUpcomingFoods (model: Model) (state: GameState) =
    match model.TargetText with
    | "" -> state
    | _ ->
        let lastIndex =
            min (model.TargetText.Length - 1) (model.TargetIndex + maxVisibleFoods - 1)

        match lastIndex < model.TargetIndex with
        | true -> state
        | false ->
            [ model.TargetIndex .. lastIndex ]
            |> List.fold (fun acc idx -> Game.spawnFood idx acc) state

let ensureFoodsForModel (model: Model) =
    let withBoard =
        { model with BoardLetters = ensureBoardLetters model.BoardLetters }

    if model.NeedsNextPhrase then
        withBoard
    else
        { withBoard with Game = ensureUpcomingFoods model withBoard.Game }

type private MoveOutcome =
    { Game: GameState
      LetterCollected: bool }

let private ensureActiveFood (model: Model) (state: GameState) =
    let hasActive =
        state.Foods |> List.exists (fun token -> token.Status = FoodStatus.Active)

    match hasActive || model.TargetIndex >= model.TargetText.Length with
    | true -> state
    | false -> Game.spawnFood model.TargetIndex state

let private advanceGame (model: Model) : MoveOutcome =
    let movedGame = Game.move model.Game
    let letterCollected = movedGame.Score > model.Game.Score
    let nextLetterIndex = model.TargetIndex + 1

    let withNewSpawn =
        match letterCollected && nextLetterIndex < model.TargetText.Length with
        | true -> Game.spawnFood nextLetterIndex movedGame
        | false -> movedGame

    { Game = ensureActiveFood model withNewSpawn
      LetterCollected = letterCollected }

let private handleGameOver (baseModel: Model) (effects: TickEffect list) =
    { Model = { baseModel with Phase = GamePhase.GameOver }
      Events = [ GameOver ]
      Effects = StopLoop :: effects }

let private handlePhraseProgress
    (model: Model)
    (baseModel: Model)
    (effects: TickEffect list)
    : TickResult
    =
    let nextIndex = model.TargetIndex + 1

    match nextIndex >= model.TargetText.Length with
    | true ->
        let completedModel, newSpeed = phraseCompleted baseModel baseModel.Game

        let fetchEffects =
            match model.PhraseQueue with
            | [] -> [ FetchVocabulary ]
            | _ -> []

        { Model = completedModel
          Events = [ PhraseCompleted newSpeed ]
          Effects =
            [ CleanupLoop
              StopLoop
              ScheduleCountdown ]
            @ fetchEffects
            @ effects }
    | false ->
        { Model = { baseModel with TargetIndex = nextIndex }
          Events = [ LetterCollected nextIndex ]
          Effects = effects }

let applyTick (model: Model) : TickResult =
    let outcome = advanceGame model
    let highScore, highScoreEffects =
        computeHighScore model.PlayerName model.HighScore outcome.Game

    let baseModel =
        { model with
            Game = outcome.Game
            HighScore = highScore }

    let result =
        match outcome.Game.GameOver with
        | true -> handleGameOver baseModel highScoreEffects
        | false ->
            match outcome.LetterCollected && model.TargetText.Length > 0 with
            | true -> handlePhraseProgress model baseModel highScoreEffects
            | false ->
                { Model = baseModel
                  Events = []
                  Effects = highScoreEffects }

    let ensuredModel = ensureFoodsForModel result.Model
    { result with Model = ensuredModel }

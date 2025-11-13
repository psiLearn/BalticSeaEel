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
        model.SpeedMs
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
        SpeedMs = newSpeed
        CountdownMs = levelCountdownMs
        Phase = GamePhase.Countdown
        NeedsNextPhrase = true
        TargetIndex = 0 },
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

let applyTick (model: Model) : TickResult =
    let movedGame = Game.move model.Game

    let nextLetterIndex = model.TargetIndex + 1

    let withNewSpawn =
        match movedGame.Score > model.Game.Score && nextLetterIndex < model.TargetText.Length with
        | true -> Game.spawnFood nextLetterIndex movedGame
        | false -> movedGame

    let ensuredGame =
        let hasActive =
            withNewSpawn.Foods |> List.exists (fun token -> token.Status = FoodStatus.Active)

        match not hasActive && model.TargetIndex < model.TargetText.Length with
        | true -> Game.spawnFood model.TargetIndex withNewSpawn
        | false -> withNewSpawn

    let highScore, highScoreEffects =
        computeHighScore model.PlayerName model.HighScore ensuredGame

    let baseModel =
        { model with
            Game = ensuredGame
            HighScore = highScore }

    let result =
        match ensuredGame.GameOver with
        | true ->
            { Model = { baseModel with Phase = GamePhase.GameOver }
              Events = [ GameOver ]
              Effects = StopLoop :: highScoreEffects }
        | false ->
            let collectedLetter = movedGame.Score > model.Game.Score

            match collectedLetter && model.TargetText.Length > 0 with
            | true ->
                let nextIndex = model.TargetIndex + 1

                match nextIndex >= model.TargetText.Length with
                | true ->
                    let completedModel, newSpeed = phraseCompleted baseModel ensuredGame

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
                        @ highScoreEffects }
                | false ->
                    { Model = { baseModel with TargetIndex = nextIndex }
                      Events = [ LetterCollected nextIndex ]
                      Effects = highScoreEffects }
            | false ->
                { Model = baseModel
                  Events = []
                  Effects = highScoreEffects }

    let ensuredModel = ensureFoodsForModel result.Model
    { result with Model = ensuredModel }

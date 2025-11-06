module Eel.Client.GameLoop

open System
open Eel.Client.Model
open Shared
open Shared.Game

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
            if String.IsNullOrWhiteSpace trimmed then
                "Anonymous"
            else
                trimmed

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
        |> fun g -> { g with Score = currentGame.Score }

    { model with
        Game = restartedGame
        TargetText = ""
        TargetIndex = 0
        Vocabulary = None
        UseExampleNext = not model.UseExampleNext
        SpeedMs = newSpeed
        CountdownMs = 3000
        GameRunning = false },
    newSpeed

let applyTick (model: Model) : TickResult =
    let nextGame = Game.move model.Game

    let highScore, highScoreEffects =
        computeHighScore model.PlayerName model.HighScore nextGame

    let baseModel =
        { model with
            Game = nextGame
            HighScore = highScore }

    if nextGame.GameOver then
        { Model = { baseModel with GameRunning = false }
          Events = [ GameOver ]
          Effects = StopLoop :: highScoreEffects }
    else
        let collectedLetter = nextGame.Score > model.Game.Score

        if collectedLetter && model.TargetText.Length > 0 then
            let nextIndex = model.TargetIndex + 1

            if nextIndex >= model.TargetText.Length then
                let completedModel, newSpeed = phraseCompleted baseModel nextGame

                { Model = completedModel
                  Events = [ PhraseCompleted newSpeed ]
                  Effects =
                    [ CleanupLoop
                      StopLoop
                      ScheduleCountdown
                      FetchVocabulary ]
                    @ highScoreEffects }
            else
                { Model = { baseModel with TargetIndex = nextIndex }
                  Events = [ LetterCollected nextIndex ]
                  Effects = highScoreEffects }
        else
            { Model = baseModel
              Events = []
              Effects = highScoreEffects }

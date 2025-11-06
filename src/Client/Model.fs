module Eel.Client.Model

open Shared
open Shared.Game

type Model =
    { Game: GameState
      PlayerName: string
      HighScore: HighScore option
      Saving: bool
      Error: string option
      Vocabulary: VocabularyEntry option
      TargetText: string
      TargetIndex: int
      UseExampleNext: bool
      SpeedMs: int
      CountdownMs: int
      GameRunning: bool }

type Msg =
    | Tick
    | CountdownTick
    | CountdownFinished
    | ChangeDirection of Direction
    | Restart
    | SetPlayerName of string
    | HighScoreLoaded of HighScore option
    | SaveHighScore
    | HighScoreSaved of HighScore option
    | VocabularyLoaded of VocabularyEntry
    | VocabularyFailed of string

let defaultVocabularyEntry: VocabularyEntry =
    { Topic = "Mer Baltique"
      Language1 = "anguille de la Baltique"
      Language2 = "Ostseeaal"
      Example = "L'anguille de la Baltique nage dans le détroit de Fehmarn" }

let fallbackTargetText = "Baltc Sea Eel"

let initialSpeed = 200

let initModel =
    { Game = Game.initialState ()
      PlayerName = ""
      HighScore = None
      Saving = false
      Error = None
      Vocabulary = Some defaultVocabularyEntry
      TargetText = $"{defaultVocabularyEntry.Language1} - {defaultVocabularyEntry.Language2}"
      TargetIndex = 0
      UseExampleNext = false
      SpeedMs = initialSpeed
      CountdownMs = 5000
      GameRunning = false }

let nextTargetChar model =
    if model.TargetIndex < model.TargetText.Length then
        Some model.TargetText.[model.TargetIndex]
    else
        None

let displayChar c = if c = ' ' then "·" else string c

let progressParts model =
    if model.TargetText = "" then
        "", ""
    else
        let completedLength = min model.TargetIndex model.TargetText.Length
        let built = model.TargetText.Substring(0, completedLength)

        let remaining =
            if completedLength < model.TargetText.Length then
                model.TargetText.Substring(completedLength)
            else
                ""

        built, remaining

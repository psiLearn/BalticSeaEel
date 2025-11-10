module Eel.Client.Model

open System
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
      PendingMoveMs: int
      QueuedDirection: Direction option
      LastEel: Point list
      CountdownMs: int
      GameRunning: bool
      BoardLetters: string array
      Scores: HighScore list
      ScoresLoading: bool
      ScoresError: string option
      SplashVisible: bool }

type Msg =
    | Tick
    | CountdownTick
    | CountdownFinished
    | StartGame
    | ChangeDirection of Direction
    | Restart
    | SetPlayerName of string
    | HighScoreLoaded of HighScore option
    | SaveHighScore
    | HighScoreSaved of HighScore option
    | VocabularyLoaded of VocabularyEntry
    | VocabularyFailed of string
    | ScoresLoaded of HighScore list
    | ScoresFailed of string

let defaultVocabularyEntry: VocabularyEntry =
    { Topic = "Mer Baltique"
      Language1 = "anguille de la Baltique"
      Language2 = "Ostseeaal"
      Example = "L'anguille de la Baltique nage dans le détroit de Fehmarn" }

let fallbackTargetText = "Baltc Sea Eel"

let initialSpeed = Config.gameplay.InitialSpeedMs

let boardLetterCount = Game.boardWidth * Game.boardHeight

let private randomSource = Random()

let private letterPool: char array =
    [| yield ' '
       yield '-'
       yield '\''
       yield! [ 'A' .. 'Z' ]
       yield! [ 'a' .. 'z' ]
       yield! [ 'À'; 'Á'; 'Â'; 'Ã'; 'Ä'; 'Å'; 'Æ'; 'Ç'; 'È'; 'É'; 'Ê'; 'Ë'; 'Ì'; 'Í'; 'Î'; 'Ï'; 'Ñ'; 'Ò'; 'Ó'; 'Ô'; 'Õ'; 'Ö'; 'Ø'; 'Ù'; 'Ú'; 'Û'; 'Ü'; 'Ý'; 'ß'; 'à'; 'á'; 'â'; 'ã'; 'ä'; 'å'; 'æ'; 'ç'; 'è'; 'é'; 'ê'; 'ë'; 'ì'; 'í'; 'î'; 'ï'; 'ñ'; 'ò'; 'ó'; 'ô'; 'õ'; 'ö'; 'ø'; 'ù'; 'ú'; 'û'; 'ü'; 'ý'; 'ÿ' ] |]

let createBoardLetters () =
    let maxIndex = letterPool.Length

    Array.init boardLetterCount (fun _ ->
        let value = letterPool.[randomSource.Next(maxIndex)]
        string value)

let ensureBoardLetters (letters: string array) =
    if isNull letters || letters.Length <> boardLetterCount then
        createBoardLetters ()
    else
        letters

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
      PendingMoveMs = 0
      QueuedDirection = None
      LastEel = (Game.initialState ()).Eel
      CountdownMs = 5000
      GameRunning = false
      BoardLetters = createBoardLetters ()
      Scores = []
      ScoresLoading = true
      ScoresError = None
      SplashVisible = true }

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

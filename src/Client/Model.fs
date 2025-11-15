module Eel.Client.Model

open System
open Shared
open Shared.Game

let defaultViewportWidth = 1200.0
let defaultViewportHeight = 800.0
let compactScreenThreshold = 900.0

type GamePhase =
    | Splash
    | Countdown
    | Running
    | Paused
    | GameOver
    | SavingHighScore

type Model =
    { Game: GameState
      PlayerName: string
      HighScore: HighScore option
      Error: string option
      Vocabulary: VocabularyEntry option
      TargetText: string
      TargetIndex: int
      PhraseQueue: string list
      NeedsNextPhrase: bool
      LastCompletedPhrase: string option
      CelebrationVisible: bool
      HighlightWaves: float list
      SpeedMs: int
      PendingMoveMs: int
      DirectionQueue: Direction list
      LastEel: Point list
      CountdownMs: int
      Phase: GamePhase
      BoardLetters: string array
      Scores: HighScore list
      ScoresLoading: bool
      ScoresError: string option
      ViewportWidth: float
      ViewportHeight: float }

let isRunning phase =
    match phase with
    | GamePhase.Running -> true
    | _ -> false

let isPaused phase =
    match phase with
    | GamePhase.Paused -> true
    | _ -> false

let isSplash phase =
    match phase with
    | GamePhase.Splash -> true
    | _ -> false

let isSaving phase =
    match phase with
    | GamePhase.SavingHighScore -> true
    | _ -> false

let isCompactScreen (model: Model) =
    model.ViewportWidth < compactScreenThreshold

let shouldHideStats (model: Model) =
    isRunning model.Phase && isCompactScreen model

type Msg =
    | Tick of int
    | CountdownTick
    | CountdownFinished
    | StartGame
    | TogglePause
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
    | CelebrationDelayElapsed
    | WindowResized of float * float

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

let sanitizePhrase (text: string) =
    if obj.ReferenceEquals(text, null) then
        None
    else
        let trimmed = text.Trim()
        if String.IsNullOrWhiteSpace trimmed then None else Some trimmed

let phrasesFromVocabulary (entry: VocabularyEntry) =
    [ $"{entry.Language1} - {entry.Language2}"
      entry.Example ]
    |> List.choose sanitizePhrase

let preparePhraseQueue phrases =
    match phrases |> List.choose sanitizePhrase with
    | [] -> [ fallbackTargetText ]
    | sanitized -> sanitized

let takeNextPhrase queue =
    match queue with
    | next :: rest -> next, rest
    | [] -> fallbackTargetText, []

let initModel =
    let initialQueue =
        defaultVocabularyEntry
        |> phrasesFromVocabulary
        |> preparePhraseQueue
    let initialTarget, remainingQueue = takeNextPhrase initialQueue

    { Game = Game.initialState ()
      PlayerName = ""
      HighScore = None
      Error = None
      Vocabulary = Some defaultVocabularyEntry
      TargetText = initialTarget
      TargetIndex = 0
      PhraseQueue = remainingQueue
      NeedsNextPhrase = false
      LastCompletedPhrase = None
      CelebrationVisible = false
      HighlightWaves = []
      SpeedMs = initialSpeed
      PendingMoveMs = 0
      DirectionQueue = []
      LastEel = (Game.initialState ()).Eel
      CountdownMs = Config.gameplay.StartCountdownMs
      Phase = Splash
      BoardLetters = createBoardLetters ()
      Scores = []
      ScoresLoading = true
      ScoresError = None
      ViewportWidth = defaultViewportWidth
      ViewportHeight = defaultViewportHeight }

let nextTargetChar model =
    if model.TargetIndex < model.TargetText.Length then
        Some model.TargetText.[model.TargetIndex]
    else
        None

let displayChar = string

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

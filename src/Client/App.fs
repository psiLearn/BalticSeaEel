module Eel.Client.App

open Elmish
open Elmish.React
open Fable.React
open Fable.React.Props
open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

module JS = Fable.Core.JS

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
      SpeedMs: int }

type Msg =
    | Tick
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

let inline private ofJson<'T> (json: string) : 'T = json |> JS.JSON.parse |> unbox<'T>

let inline private toJson (value: obj) = JS.JSON.stringify value

let private fetch (url: string) (init: obj option) : JS.Promise<obj> =
    match init with
    | Some initObj ->
        window?fetch (url, initObj)
        |> unbox<JS.Promise<obj>>
    | None -> window?fetch (url) |> unbox<JS.Promise<obj>>

let fetchHighScore (_: unit) =
    promise {
        let! response = fetch "/api/highscore" None

        if response?ok then
            let! text = response?text () |> unbox<JS.Promise<string>>
            return text |> ofJson<HighScore> |> Some
        else
            return None
    }

let saveHighScore (name, score) =
    promise {
        let payload = {| name = name; score = score |} |> toJson

        let init =
            [ "method" ==> "POST"
              "headers"
              ==> createObj [ "Content-Type" ==> "application/json" ]
              "body" ==> payload ]
            |> createObj

        let! response = fetch "/api/highscore" (Some init)

        if response?ok then
            let! text = response?text () |> unbox<JS.Promise<string>>
            return text |> ofJson<HighScore> |> Some
        else
            return None
    }

let fetchVocabulary (_: unit) =
    promise {
        let! response = fetch "/api/vocabulary" None

        if response?ok then
            let! text = response?text () |> unbox<JS.Promise<string>>
            return text |> ofJson<VocabularyEntry>
        else
            return defaultVocabularyEntry
            // let status: int = response?status |> unbox<int>
            // return failwithf "Failed to fetch vocabulary (status %i)" status
    }

let fetchHighScoreCmd =
    Cmd.OfPromise.either fetchHighScore () HighScoreLoaded (fun _ -> HighScoreLoaded None)

let saveHighScoreCmd name score =
    Cmd.OfPromise.either saveHighScore (name, score) HighScoreSaved (fun _ -> HighScoreSaved None)

let fetchVocabularyCmd =
    //Cmd.OfPromise.either fetchVocabulary () VocabularyLoaded (fun ex -> VocabularyFailed ex.Message)
    defaultVocabularyEntry |> VocabularyLoaded |> Cmd.ofMsg   
    
let private nextTargetChar model =
    if model.TargetIndex < model.TargetText.Length then
        Some model.TargetText.[model.TargetIndex]
    else
        None

let private displayChar c = if c = ' ' then "·" else string c

let private progressParts model =
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

let initialSpeed = 200

let private cleanupKey = "__eelCleanup"

let private tryCleanupPrevious () =
    let cleanupObj: obj = window?(cleanupKey)

    if not (isNullOrUndefined cleanupObj) then
        match cleanupObj with
        | :? (unit -> unit) as cleanup -> cleanup ()
        | _ -> ()

        window?cleanupKey <- null

let startLoopCmd (speedMs: int) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        tryCleanupPrevious ()

        let intervalId = window.setInterval ((fun _ -> dispatch Tick), speedMs)

        let handleKey (ev: KeyboardEvent) =
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
            window.clearInterval intervalId
            window.removeEventListener ("keydown", handleKeyListener)
            window.removeEventListener ("beforeunload", unloadHandler)
            window?cleanupKey <- null

        and unloadHandler (_: Event) = cleanup ()

        window.addEventListener ("beforeunload", unloadHandler)
        window?cleanupKey <- cleanup)

let init () =
    let model =
        { Game = Game.initialState ()
          PlayerName = ""
          HighScore = None
          Saving = false
          Error = None
          Vocabulary = Some defaultVocabularyEntry
          TargetText = ""
          TargetIndex = 0
          UseExampleNext = false
          SpeedMs = initialSpeed }

    model,
    Cmd.batch [ fetchHighScoreCmd
                fetchVocabularyCmd
                startLoopCmd model.SpeedMs ]

let update msg model =
    match msg with
    | Tick ->
        let nextGame = Game.move model.Game
        let updatedModel = { model with Game = nextGame }

        if nextGame.GameOver then
            updatedModel, Cmd.none
        else
            let collectedLetter = nextGame.Score > model.Game.Score

            if collectedLetter && model.TargetText.Length > 0 then
                let nextIndex = model.TargetIndex + 1

                if nextIndex >= model.TargetText.Length then
                    let newSpeed = model.SpeedMs |> float |> (*) 0.9 |> int |> max 50

                    let restartedGame = Game.restart ()

                    { updatedModel with
                        Game = restartedGame
                        TargetText = ""
                        TargetIndex = 0
                        Vocabulary = None
                        UseExampleNext = not model.UseExampleNext
                        SpeedMs = newSpeed },
                    Cmd.batch [ startLoopCmd newSpeed
                                fetchVocabularyCmd ]
                else
                    { updatedModel with TargetIndex = nextIndex }, Cmd.none
            else
                updatedModel, Cmd.none
    | ChangeDirection direction -> { model with Game = Game.changeDirection direction model.Game }, Cmd.none
    | Restart ->
        let resetModel =
            { model with
                Game = Game.restart ()
                Error = None
                Vocabulary = None
                TargetText = ""
                TargetIndex = 0
                UseExampleNext = false
                SpeedMs = initialSpeed }

        resetModel,
        Cmd.batch [ startLoopCmd resetModel.SpeedMs
                    fetchVocabularyCmd ]
    | SetPlayerName name -> { model with PlayerName = name }, Cmd.none
    | HighScoreLoaded hs -> { model with HighScore = hs }, Cmd.none
    | SaveHighScore ->
        if model.PlayerName.Trim() = "" then
            { model with Error = Some "Enter a name before saving your score." }, Cmd.none
        elif not model.Game.GameOver then
            { model with Error = Some "You can only submit a score after the game ends." }, Cmd.none
        elif model.Saving then
            model, Cmd.none
        else
            { model with
                Saving = true
                Error = None },
            saveHighScoreCmd model.PlayerName model.Game.Score
    | HighScoreSaved result ->
        match result with
        | Some score ->
            { model with
                Saving = false
                HighScore = Some score
                Error = None },
            Cmd.none
        | None ->
            { model with
                Saving = false
                Error = Some "Unable to save your high score. Please try again." },
            Cmd.none
    | VocabularyLoaded entry ->
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

        { model with
            Vocabulary = Some entry
            TargetText = finalTarget
            TargetIndex = 0
            Error = None },
        Cmd.none
    | VocabularyFailed message -> { model with Error = Some message }, Cmd.none

let private boardView model =
    let eelCells: Set<Point> = model.Game.Eel |> Set.ofList
    let foodChar = nextTargetChar model |> Option.map displayChar
    let built, _ = progressParts model
    let builtChars = built |> Seq.toList

    let eelLetterMap =
        (model.Game.Eel, builtChars)
        ||> List.zip
        |> List.map (fun (point, ch) -> point, displayChar ch)
        |> Map.ofList

    let cells =
        [ for y in 0 .. Game.boardHeight - 1 do
              for x in 0 .. Game.boardWidth - 1 do
                  let point = { X = x; Y = y }

                  let className =
                      if Set.contains point eelCells then
                          "cell eel"
                      elif point = model.Game.Food then
                          "cell food"
                      else
                          "cell"

                  let children =
                      if point = model.Game.Food then
                          match foodChar with
                          | Some letter -> [ str letter ]
                          | None -> []
                      elif Set.contains point eelCells then
                          match Map.tryFind point eelLetterMap with
                          | Some letter -> [ str letter ]
                          | None -> []
                      else
                          []

                  yield div [ Key $"{x}-{y}"; ClassName className ] children ]

    div
        [ ClassName "board"
          Style [ CSSProp.GridTemplateColumns $"repeat({Game.boardWidth}, 1fr)" ] ]
        cells

let private statsView model dispatch =
    let highScoreText =
        match model.HighScore with
        | Some score -> $"{score.Name} — {score.Score}"
        | None -> "No high score yet"

    let built, remaining = progressParts model

    let nextLetterDisplay =
        match nextTargetChar model |> Option.map displayChar with
        | Some letter -> letter
        | None -> ""

    div [ ClassName "sidebar" ] [
        h1 [] [ str "Baltic Sea Eel" ]
        p [] [
            str $"Score: {model.Game.Score}"
        ]
        p [] [
            str $"High score: {highScoreText}"
        ]
        (match model.Vocabulary with
         | Some vocab ->
             div [ ClassName "vocab-card" ] [
                 h2 [] [ str $"Thème: {vocab.Topic}" ]
                 p [] [
                     str $"{vocab.Language1} → {vocab.Language2}"
                 ]
                 p [] [ str vocab.Example ]
             ]
         | None -> p [] [ str "Fetching vocabulary…" ])
        (if model.TargetText = "" then
             p [] [ str "Waiting for phrase…" ]
         else
             p [] [
                 str "Phrase: "
                 span [ ClassName "built" ] [ str built ]
                 span [ ClassName "cursor" ] [ str "▮" ]
                 span [ ClassName "remaining" ] [
                     str remaining
                 ]
             ])
        p [] [
            str $"Next letter: {nextLetterDisplay}"
        ]
        p [] [
            str $"Speed: {model.SpeedMs} ms"
        ]
        div [ ClassName "controls" ] [
            label [] [ str "Name" ]
            input [ ClassName "name-input"
                    Value model.PlayerName
                    Placeholder "Your name"
                    OnChange(fun ev -> dispatch (SetPlayerName((ev.target :?> HTMLInputElement).value)))
                    Disabled model.Saving ]
            button [ ClassName "action"
                     Disabled(model.Saving || not model.Game.GameOver)
                     OnClick(fun _ -> dispatch SaveHighScore) ] [
                if model.Saving then
                    str "Saving..."
                else
                    str "Save score"
            ]
            button [ ClassName "action secondary"
                     OnClick(fun _ -> dispatch Restart) ] [
                str "Restart"
            ]
        ]
        if model.Game.GameOver then
            div [ ClassName "banner" ] [
                h2 [] [ str "Game Over" ]
                p [] [
                    str "Press restart or hit the spacebar to play again."
                ]
            ]
        match model.Error with
        | Some errorMessage ->
            p [ ClassName "error" ] [
                str errorMessage
            ]
        | None -> fragment [] []
    ]

let view model dispatch =
    div [ ClassName "layout" ] [
        boardView model
        statsView model dispatch
    ]

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run

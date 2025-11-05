module Ale.Client.App

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
      Error: string option }

type Msg =
    | Tick
    | ChangeDirection of Direction
    | Restart
    | SetPlayerName of string
    | HighScoreLoaded of HighScore option
    | SaveHighScore
    | HighScoreSaved of HighScore option

let inline private ofJson<'T> (json: string) : 'T =
    json
    |> JS.JSON.parse
    |> unbox<'T>

let inline private toJson (value: obj) =
    JS.JSON.stringify value

let private fetch (url: string) (init: obj option) : JS.Promise<obj> =
    match init with
    | Some initObj -> window?fetch (url, initObj) |> unbox<JS.Promise<obj>>
    | None -> window?fetch (url) |> unbox<JS.Promise<obj>>

let fetchHighScore (_: unit) =
    promise {
        let! response = fetch "/api/highscore" None

        if response?ok then
            let! text = response?text() |> unbox<JS.Promise<string>>
            return text |> ofJson<HighScore> |> Some
        else
            return None
    }

let saveHighScore (name, score) =
    promise {
        let payload =
            {| name = name
               score = score |}
            |> toJson

        let init =
            [ "method" ==> "POST"
              "headers" ==> createObj [ "Content-Type" ==> "application/json" ]
              "body" ==> payload ]
            |> createObj

        let! response = fetch "/api/highscore" (Some init)

        if response?ok then
            let! text = response?text() |> unbox<JS.Promise<string>>
            return text |> ofJson<HighScore> |> Some
        else
            return None
    }

let fetchHighScoreCmd =
    Cmd.OfPromise.either fetchHighScore () HighScoreLoaded (fun _ -> HighScoreLoaded None)

let saveHighScoreCmd name score =
    Cmd.OfPromise.either saveHighScore (name, score) HighScoreSaved (fun _ -> HighScoreSaved None)

let private cleanupKey = "__snakeCleanup"

let private tryCleanupPrevious () =
    let cleanupObj: obj = window?(cleanupKey)

    if not (isNullOrUndefined cleanupObj) then
        match cleanupObj with
        | :? (unit -> unit) as cleanup ->
            cleanup ()
        | _ -> ()

        window?(cleanupKey) <- null

let startLoopCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        tryCleanupPrevious ()

        let intervalId = window.setInterval ((fun _ -> dispatch Tick), 150)

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

        let handleKeyListener =
            fun (ev: Event) -> handleKey (ev :?> KeyboardEvent)

        window.addEventListener ("keydown", handleKeyListener)

        let rec cleanup () =
            window.clearInterval intervalId
            window.removeEventListener ("keydown", handleKeyListener)
            window.removeEventListener ("beforeunload", unloadHandler)
            window?(cleanupKey) <- null
        and unloadHandler (_: Event) =
            cleanup ()

        window.addEventListener ("beforeunload", unloadHandler)
        window?(cleanupKey) <- cleanup
    )

let init () =
    let model =
        { Game = Game.initialState ()
          PlayerName = ""
          HighScore = None
          Saving = false
          Error = None }

    model, Cmd.batch [ fetchHighScoreCmd; startLoopCmd ]

let update msg model =
    match msg with
    | Tick ->
        let nextGame = Game.move model.Game
        { model with Game = nextGame }, Cmd.none
    | ChangeDirection direction ->
        { model with Game = Game.changeDirection direction model.Game }, Cmd.none
    | Restart ->
        { model with
            Game = Game.restart ()
            Error = None }, Cmd.none
    | SetPlayerName name ->
        { model with PlayerName = name }, Cmd.none
    | HighScoreLoaded hs ->
        { model with HighScore = hs }, Cmd.none
    | SaveHighScore ->
        if model.PlayerName.Trim() = "" then
            { model with Error = Some "Enter a name before saving your score." }, Cmd.none
        elif not model.Game.GameOver then
            { model with Error = Some "You can only submit a score after the game ends." }, Cmd.none
        elif model.Saving then
            model, Cmd.none
        else
            { model with Saving = true; Error = None }, saveHighScoreCmd model.PlayerName model.Game.Score
    | HighScoreSaved result ->
        match result with
        | Some score ->
            { model with
                Saving = false
                HighScore = Some score
                Error = None }, Cmd.none
        | None ->
            { model with
                Saving = false
                Error = Some "Unable to save your high score. Please try again." }, Cmd.none

let private boardView model =
    let snakeCells = model.Game.Snake |> Set.ofList

    let cells =
        [ for y in 0 .. Game.boardHeight - 1 do
            for x in 0 .. Game.boardWidth - 1 do
                let point = { X = x; Y = y }

                let className =
                    if Set.contains point snakeCells then
                        "cell snake"
                    elif point = model.Game.Food then
                        "cell food"
                    else
                        "cell"

                yield div [ Key $"{x}-{y}"; ClassName className ] [] ]

    div [ ClassName "board"; Style [ CSSProp.GridTemplateColumns $"repeat({Game.boardWidth}, 1fr)" ] ] cells

let private statsView model dispatch =
    let highScoreText =
        match model.HighScore with
        | Some score -> $"{score.Name} — {score.Score}"
        | None -> "No high score yet"

    div [ ClassName "sidebar" ] [
        h1 [] [ str "SAFE Snake" ]
        p [] [ str $"Score: {model.Game.Score}" ]
        p [] [ str $"High score: {highScoreText}" ]
        div [ ClassName "controls" ] [
            label [] [ str "Name" ]
            input [
                ClassName "name-input"
                Value model.PlayerName
                Placeholder "Your name"
                OnChange (fun ev -> dispatch (SetPlayerName ((ev.target :?> HTMLInputElement).value)))
                Disabled model.Saving
            ]
            button [
                ClassName "action"
                Disabled (model.Saving || not model.Game.GameOver)
                OnClick (fun _ -> dispatch SaveHighScore)
            ] [
                if model.Saving then
                    str "Saving..."
                else
                    str "Save score"
            ]
            button [
                ClassName "action secondary"
                OnClick (fun _ -> dispatch Restart)
            ] [ str "Restart" ]
        ]
        if model.Game.GameOver then
            div [ ClassName "banner" ] [
                h2 [] [ str "Game Over" ]
                p [] [ str "Press restart or hit the spacebar to play again." ]
            ]
        match model.Error with
        | Some errorMessage -> p [ ClassName "error" ] [ str errorMessage ]
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

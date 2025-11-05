module Eel.Client.View

open Fable.React
open Fable.React.Props
open Elmish
open Eel.Client.Model
open Shared
open Shared.Game

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
        | Some score -> $"{score.Name} - {score.Score}"
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
                     str $"{vocab.Language1} -> {vocab.Language2}"
                 ]
                 p [] [ str vocab.Example ]
             ]
         | None -> p [] [ str "Fetching vocabulary." ])
        (if model.TargetText = "" then
             p [] [ str "Waiting for phrase." ]
         else
             p [] [
                 str "Phrase: "
                 span [ ClassName "built" ] [ str built ]
                 span [ ClassName "cursor" ] [ str "?" ]
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
                    OnChange(fun ev -> dispatch (SetPlayerName((ev.target :?> Browser.Types.HTMLInputElement).value)))
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

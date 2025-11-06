module Eel.Client.View

open Fable.React
open Fable.React.Props
open Elmish
open Eel.Client.Model
open Shared
open Shared.Game

let private boardView model =
    let eelCells: Set<Point> = model.Game.Eel |> Set.ofList
    let built, _ = progressParts model
    let builtChars = built |> Seq.toList
    let nextLetter = nextTargetChar model |> Option.map displayChar
    let boardWidth = Game.boardWidth

    let eelLetterMap =
        (model.Game.Eel, builtChars)
        ||> List.zip
        |> List.map (fun (point, ch) -> point, displayChar ch)
        |> Map.ofList

    let boardLetterFor (point: Point) =
        let index = point.Y * boardWidth + point.X
        if index >= 0 && index < model.BoardLetters.Length then
            model.BoardLetters.[index]
        else
            ""

    let foodMap =
        model.Game.Foods
        |> List.map (fun token -> token.Position, token)
        |> Map.ofList

    let tokenLetter token =
        match token.Status, nextLetter with
        | FoodStatus.Active, Some letter -> letter
        | FoodStatus.Active, None -> ""
        | FoodStatus.Collected, _ ->
            if token.LetterIndex >= 0 && token.LetterIndex < model.TargetText.Length then
                displayChar model.TargetText.[token.LetterIndex]
            else
                "?"

    let cells =
        [ for y in 0 .. Game.boardHeight - 1 do
              for x in 0 .. Game.boardWidth - 1 do
                  let point = { X = x; Y = y }

                  let tokenOpt = Map.tryFind point foodMap

                  let className, children =
                      if Set.contains point eelCells then
                          let letterChild =
                              match Map.tryFind point eelLetterMap with
                              | Some letter -> [ str letter ]
                              | None -> []

                          "cell eel", letterChild
                      else
                          match tokenOpt with
                          | Some token ->
                              let letter = tokenLetter token
                              let cls =
                                  match token.Status with
                                  | FoodStatus.Active -> "cell food"
                                  | FoodStatus.Collected -> "cell letter"

                              cls, [ str letter ]
                          | None ->
                              let letter = boardLetterFor point
                              let children = if letter = "" then [] else [ str letter ]
                              "cell board-letter", children

                  yield div [ Key $"{x}-{y}"; ClassName className ] children ]

    let overlay =
        if not model.GameRunning then
            let seconds = max 0 ((model.CountdownMs + 999) / 1000)
            let label = if seconds > 0 then string seconds else "GO!"

            [ div [ ClassName "board-overlay"
                    Style [ CSSProp.Custom("position", "absolute")
                            CSSProp.Custom("top", "0")
                            CSSProp.Custom("left", "0")
                            CSSProp.Custom("right", "0")
                            CSSProp.Custom("bottom", "0")
                            CSSProp.Custom("display", "flex")
                            CSSProp.Custom("align-items", "center")
                            CSSProp.Custom("justify-content", "center")
                            CSSProp.BackgroundColor "rgba(0, 0, 0, 0.6)"
                            CSSProp.FontSize "3rem"
                            CSSProp.FontWeight "bold"
                            CSSProp.Color "#fff" ] ]
                  [ str label ] ]
        else
            []

    div
        [ ClassName "board"
          Style [ CSSProp.GridTemplateColumns $"repeat({Game.boardWidth}, 1fr)"
                  CSSProp.Custom("position", "relative") ] ]
        (cells @ overlay)

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

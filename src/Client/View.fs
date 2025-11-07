module Eel.Client.View

open Fable.React
open Fable.React.Props
open Elmish
open Eel.Client.Model
open Shared
open Shared.Game

let private scoreboardEntries model =
    if model.ScoresLoading then
        [ div [ ClassName "scoreboard-row placeholder" ] [ str "Loading scores..." ] ]
    elif model.ScoresError |> Option.isSome then
        [ div [ ClassName "scoreboard-row placeholder" ] [ str (model.ScoresError |> Option.defaultValue "Unable to load scores.") ] ]
    elif model.Scores.IsEmpty then
        [ div [ ClassName "scoreboard-row placeholder" ] [ str "No scores yet. Be the first to record a run!" ] ]
    else
        model.Scores
        |> List.mapi (fun idx score ->
            div [ ClassName "scoreboard-row"; Key $"score-{idx}" ] [
                span [ ClassName "scoreboard-rank" ] [ str $"{idx + 1}." ]
                span [ ClassName "scoreboard-name" ] [ str score.Name ]
                span [ ClassName "scoreboard-value" ] [ str $"{score.Score}" ]
            ])

let private boardView model =
    let built, _ = progressParts model
    let builtChars = built |> Seq.toList
    let nextLetter = nextTargetChar model |> Option.map displayChar
    let boardWidth = Game.boardWidth
    let boardHeight = Game.boardHeight
    let cellSize = 26.0
    let cellGap = 6.0
    let boardPadding = 18.0
    let cellStep = cellSize + cellGap
    let boardInnerWidth =
        float boardWidth * cellSize + float (boardWidth - 1) * cellGap
    let boardInnerHeight =
        float boardHeight * cellSize + float (boardHeight - 1) * cellGap

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
        [ for y in 0 .. boardHeight - 1 do
              for x in 0 .. boardWidth - 1 do
                  let point = { X = x; Y = y }

                  let tokenOpt = Map.tryFind point foodMap

                  let className, children =
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
                          let cls = if letter = "" then "cell" else "cell board-letter"
                          cls, children

                  yield div [ Key $"{x}-{y}"; ClassName className ] children ]

    let countdownOverlay =
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

    let progress =
        if model.GameRunning && model.SpeedMs > 0 then
            model.PendingMoveMs
            |> float
            |> fun v -> v / float model.SpeedMs
            |> max 0.0
            |> min 0.999
        else
            0.0

    let futureGame = Game.move model.Game
    let currentSegments = model.Game.Eel |> List.toArray
    let futureSegments = futureGame.Eel |> List.toArray
    let maxSegments = max currentSegments.Length futureSegments.Length

    let eelLetterMap =
        List.zip model.Game.Eel builtChars
        |> List.mapi (fun idx (_, ch) -> idx, displayChar ch)
        |> Map.ofList

    let segmentElements =
        [ for idx in 0 .. maxSegments - 1 do
              let currentPoint =
                  if idx < currentSegments.Length then
                      currentSegments.[idx]
                  else
                      futureSegments.[idx]

              let futurePoint =
                  if idx < futureSegments.Length then
                      futureSegments.[idx]
                  else
                      currentPoint

              let interpolate currentValue futureValue =
                  currentValue + (futureValue - currentValue) * progress

              let x =
                  interpolate (float currentPoint.X) (float futurePoint.X)
                  * cellStep

              let y =
                  interpolate (float currentPoint.Y) (float futurePoint.Y)
                  * cellStep

              let translate = sprintf "translate(%fpx, %fpx)" x y

              let letterChildren =
                  match Map.tryFind idx eelLetterMap with
                  | Some letter -> [ str letter ]
                  | None -> []

              let className = if idx = 0 then "eel-segment head" else "eel-segment"

              yield
                  div [ Key $"eel-{idx}"
                        ClassName className
                        Style [ CSSProp.Position PositionOptions.Absolute
                                CSSProp.Left "0"
                                CSSProp.Top "0"
                                CSSProp.Width (sprintf "%.1fpx" cellSize)
                                CSSProp.Height (sprintf "%.1fpx" cellSize)
                                CSSProp.Transform translate ] ]
                      letterChildren ]

    let eelOverlay =
        if maxSegments = 0 then
            []
        else
            [ div [ ClassName "eel-overlay"
                    Style [ CSSProp.Position PositionOptions.Absolute
                            CSSProp.Left (sprintf "%.1fpx" boardPadding)
                            CSSProp.Top (sprintf "%.1fpx" boardPadding)
                            CSSProp.Width (sprintf "%.1fpx" boardInnerWidth)
                            CSSProp.Height (sprintf "%.1fpx" boardInnerHeight)
                            CSSProp.PointerEvents "none" ] ]
                  segmentElements ]

    div
        [ ClassName "board"
          Style [ CSSProp.GridTemplateColumns $"repeat({Game.boardWidth}, 26px)"
                  CSSProp.GridTemplateRows $"repeat({Game.boardHeight}, 26px)"
                  CSSProp.Position PositionOptions.Relative ] ]
        (cells @ eelOverlay @ countdownOverlay)

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

    let collectedLetters =
        model.Game.Foods
        |> List.filter (fun token -> token.Status = FoodStatus.Collected)
        |> List.map (fun token ->
            let letter =
                if token.LetterIndex >= 0 && token.LetterIndex < model.TargetText.Length then
                    displayChar model.TargetText.[token.LetterIndex]
                else
                    "?"

            div [ ClassName "collected-letter"
                  Key $"token-{token.Position.X}-{token.Position.Y}-{token.LetterIndex}" ]
                [ str letter ])

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
        div [ ClassName "scoreboard" ] (
            h2 [] [ str "Top Scores" ] :: scoreboardEntries model
        )
        if not model.SplashVisible then
            div [ ClassName "collected-letters-section" ] (
                [
                    h3 [] [ str "Collected Letters" ]
                ]
                @ (if List.isEmpty collectedLetters then
                        [ div [ ClassName "collected-letters-empty" ] [ str "Eat food to collect letters." ] ]
                   else
                        [ div [ ClassName "collected-letters-grid" ] collectedLetters ])
            )
        else
            fragment [] []
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

let private splashView model dispatch =
    div [ ClassName "splash-screen" ] [
        div [ ClassName "splash-content" ] [
            h1 [] [ str "Dive into the Baltic" ]
            p [ ClassName "splash-description" ]
              [ str "Steer the eel, collect letters, and spell each phrase before the tide speeds up." ]
            div [ ClassName "scoreboard" ] (
                h2 [] [ str "Top Scores" ] :: scoreboardEntries model
            )
            button [
                ClassName "action large"
                Disabled model.ScoresLoading
                OnClick(fun _ -> dispatch StartGame)
            ] [
                if model.ScoresLoading then
                    str "Preparing..."
                else
                    str "Start Game"
            ]
        ]
    ]

let view model dispatch =
    let children =
        [ boardView model
          statsView model dispatch ]

    let allChildren =
        if model.SplashVisible then
            children @ [ splashView model dispatch ]
        else
            children

    div [ ClassName "layout" ] allChildren

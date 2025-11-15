module Eel.Client.View

open System
#if FABLE_COMPILER
open Browser.Types
open Browser.Dom
#endif

open Fable.Core
open Fable.Core.JsInterop
open Fable.React
open Fable.React.Props
open Elmish
open Eel.Client.Model
open Shared
open Shared.Game
open Eel.Client.Rendering.Shared

module CanvasRenderer = Eel.Client.Rendering.CanvasRenderer
#if FABLE_COMPILER
module KonvaRenderer = Eel.Client.Rendering.KonvaRenderer
#endif

module ModelState = Eel.Client.Model

let private viewportPadding = 32.0
let private minViewportSpan = 200.0
let private miniOverlayMinSize = 32.0

let private filteredLetters (phrase: string) =
    phrase
    |> Seq.filter (fun ch -> not (Char.IsWhiteSpace ch))
    |> Seq.map string
    |> Seq.toArray

let private countdownRadius lettersLength =
    let spacing = 46.0
    (float lettersLength * spacing) / (2.0 * Math.PI)
    |> max 70.0
    |> min 220.0

let private countdownSegment idx total radius letter =
    let angle = (float idx / float total) * 2.0 * Math.PI
    let x = radius * Math.Cos angle
    let y = radius * Math.Sin angle
    let rotation = (angle * 180.0 / Math.PI) + 90.0
    let classes =
        [ "countdown-eel-segment"
          if idx = 0 then "head" else ""
          if idx = total - 1 then "tail" else "" ]
        |> List.filter (fun c -> c <> "")
        |> String.concat " "

    div [ ClassName classes
          Style [ CSSProp.Left (sprintf "calc(50%% + %.1fpx)" x)
                  CSSProp.Top (sprintf "calc(50%% + %.1fpx)" y)
                  CSSProp.Transform (sprintf "translate(-50%%, -50%%) rotate(%.1fdeg)" rotation) ] ]
        [ str letter ]

let private countdownEelCircle (phrase: string) =
    let letters = filteredLetters phrase

    if letters.Length = 0 then
        None
    else
        let radius = countdownRadius letters.Length
        let segments =
            letters
            |> Array.mapi (fun idx letter -> countdownSegment idx letters.Length radius letter)
            |> Array.toList

        Some(
            div [ ClassName "countdown-eel" ]
                [ div [ ClassName "countdown-eel-track" ] segments ])

let private countdownCelebration model =
    match model.Phase, model.CelebrationVisible, model.LastCompletedPhrase with
    | GamePhase.Countdown, true, Some phrase when not (String.IsNullOrWhiteSpace phrase) ->
        countdownEelCircle phrase
    | _ -> None
let private boardPropsEqual (prev: Model) (next: Model) =
    obj.ReferenceEquals(prev, next)
    || (prev.Game = next.Game
        && prev.BoardLetters = next.BoardLetters
        && prev.PendingMoveMs = next.PendingMoveMs
        && prev.TargetText = next.TargetText
        && prev.TargetIndex = next.TargetIndex
        && prev.Phase = next.Phase
        && prev.CountdownMs = next.CountdownMs
        && prev.HighlightWaves = next.HighlightWaves
        && prev.DirectionQueue = next.DirectionQueue
        && prev.LastEel = next.LastEel
        && prev.SpeedMs = next.SpeedMs
        && prev.LastCompletedPhrase = next.LastCompletedPhrase)

let private boardCanvas =
    FunctionComponent.Of(
        (fun (model: Model) ->
#if FABLE_COMPILER
            match detectRenderEngine () with
            | RenderEngine.Konva -> KonvaRenderer.view model
            | RenderEngine.Canvas -> CanvasRenderer.view model
#else
            CanvasRenderer.view model
#endif
        ),
        memoizeWith = boardPropsEqual)

let private nameInputOnChange dispatch =
#if FABLE_COMPILER
    OnChange(fun ev -> dispatch (SetPlayerName((ev.target :?> Browser.Types.HTMLInputElement).value)))
#else
    OnChange(fun _ -> ())
#endif

let private countdownOverlay model =
    if not (ModelState.isRunning model.Phase) then
        let seconds = max 0 ((model.CountdownMs + 999) / 1000)
        let label = if seconds > 0 then string seconds else "GO!"
        let baseOverlay = div [ ClassName "board-overlay board-overlay--countdown" ] [ str label ]
        match countdownCelebration model with
        | Some celebration -> [ baseOverlay; celebration ]
        | None -> [ baseOverlay ]
    else
        []

let private clampFloat minValue maxValue value =
    value |> max minValue |> min maxValue

let headPixelCenter (model: Model) =
    let interpolated =
        buildSegmentInfos model
        |> Array.tryFind (fun info -> info.IsHead)
        |> Option.map (fun info -> info.X + (cellSize / 2.), info.Y + (cellSize / 2.))

    match interpolated with
    | Some coords -> coords
    | None ->
        match model.Game.Eel with
        | head :: _ ->
            let cx = canvasPadding + float head.X * cellStep
            let cy = canvasPadding + float head.Y * cellStep
            cx, cy
        | [] ->
            canvasPadding, canvasPadding

let private clampShift size visible headCoord =
    let maxShift = max 0.0 (size - visible)
    if maxShift <= 0.0 then
        0.0
    else
        headCoord - (visible / 2.0)
        |> clampFloat 0.0 maxShift

type private BoardViewportInfo =
    { Element: ReactElement
      ViewportWidth: float
      ViewportHeight: float
      VisibleWidth: float
      VisibleHeight: float
      ShouldShowMiniOverlay: bool }

let private boardViewportElement (model: Model) : BoardViewportInfo =
    let rawWidth =
        if model.ViewportWidth > 0.0 then model.ViewportWidth else Model.defaultViewportWidth

    let rawHeight =
        if model.ViewportHeight > 0.0 then model.ViewportHeight else Model.defaultViewportHeight
    let viewportWidth = max minViewportSpan (rawWidth - viewportPadding)
    let viewportHeight = max minViewportSpan (rawHeight - viewportPadding)
    let boardWidth = boardPixelWidth
    let boardHeight = boardPixelHeight
    let visibleWidth = min boardWidth viewportWidth
    let visibleHeight = min boardHeight viewportHeight

    let headX, headY = headPixelCenter model
    let shiftX = clampShift boardWidth visibleWidth headX
    let shiftY = clampShift boardHeight visibleHeight headY

    let viewportStyle =
        Style [ CSSProp.Width (sprintf "%.1fpx" visibleWidth)
                CSSProp.Height (sprintf "%.1fpx" visibleHeight) ]

    let innerStyle =
        Style [ CSSProp.Width (sprintf "%.1fpx" boardWidth)
                CSSProp.Height (sprintf "%.1fpx" boardHeight)
                CSSProp.Transform (sprintf "translate(-%.1fpx, -%.1fpx)" shiftX shiftY) ]

    let shouldShowMini =
        visibleWidth < boardPixelWidth || visibleHeight < boardPixelHeight

    { Element =
        div [ ClassName "board-viewport"; viewportStyle ] [
            div [ ClassName "board-viewport__inner"; innerStyle ] [
                boardCanvas model
            ]
        ]
      ViewportWidth = viewportWidth
      ViewportHeight = viewportHeight
      VisibleWidth = visibleWidth
      VisibleHeight = visibleHeight
      ShouldShowMiniOverlay = shouldShowMini }

let private miniOverlayView (model: Model) visibleWidth visibleHeight =
    let desiredSize =
        min visibleWidth visibleHeight * Config.gameplay.MiniOverlayScale

    let maxAllowed =
        min (visibleWidth - 8.0) (visibleHeight - 8.0)

    let baseSize = min desiredSize maxAllowed

    if baseSize < miniOverlayMinSize then
        None
    else
        let cellWidth = baseSize / float Game.boardWidth
        let cellHeight = baseSize / float Game.boardHeight

        let eelSegments =
            model.Game.Eel
            |> List.mapi (fun idx segment ->
                let left = float segment.X * cellWidth
                let top = float segment.Y * cellHeight
                let className =
                    if idx = 0 then
                        "mini-overlay__segment mini-overlay__head"
                    else
                        "mini-overlay__segment"

                div [ ClassName className
                      Key $"mini-eel-{idx}-{segment.X}-{segment.Y}"
                      Style [ CSSProp.Left (sprintf "%.2fpx" left)
                              CSSProp.Top (sprintf "%.2fpx" top)
                              CSSProp.Width (sprintf "%.2fpx" cellWidth)
                              CSSProp.Height (sprintf "%.2fpx" cellHeight) ] ] [])

        let foods =
            model.Game.Foods
            |> List.filter (fun token -> token.Status = FoodStatus.Active)
            |> List.mapi (fun idx token ->
                let left = float token.Position.X * cellWidth
                let top = float token.Position.Y * cellHeight
                div [ ClassName "mini-overlay__food"
                      Key $"mini-food-{idx}-{token.Position.X}-{token.Position.Y}"
                      Style [ CSSProp.Left (sprintf "%.2fpx" left)
                              CSSProp.Top (sprintf "%.2fpx" top)
                              CSSProp.Width (sprintf "%.2fpx" cellWidth)
                              CSSProp.Height (sprintf "%.2fpx" cellHeight) ] ] [])

        Some(
            div [ ClassName "mini-overlay"
                  Style [ CSSProp.Width (sprintf "%.1fpx" baseSize)
                          CSSProp.Height (sprintf "%.1fpx" baseSize)
                          CSSProp.Right "1rem"
                          CSSProp.Bottom "1rem" ] ] [
                div [ ClassName "mini-overlay__grid" ] (eelSegments @ foods)
            ])

let private boardView model =
    let collectedLetters =
        if String.IsNullOrEmpty model.TargetText then
            []
        else
            let builtLength = min model.TargetIndex model.TargetText.Length

            model.TargetText.Substring(0, builtLength)
            |> Seq.mapi (fun idx ch ->
                div [ ClassName "collected-letter"
                      Key $"collected-{idx}-{ch}" ]
                    [ str (displayChar ch) ])
            |> Seq.toList

    let collectedSection =
        if ModelState.isSplash model.Phase then
            fragment [] []
        else
            let body =
                if List.isEmpty collectedLetters then
                    [ div [ ClassName "collected-letters-empty" ] [ str "Eat food to collect letters." ] ]
                else
                    [ div [ ClassName "collected-letters-grid" ] collectedLetters ]

            div [ ClassName "collected-letters-section board-collected" ]
                (h3 [] [ str "Collected Letters" ] :: body)

    let viewportInfo = boardViewportElement model
    let overlayMini =
        match viewportInfo.ShouldShowMiniOverlay with
        | true -> miniOverlayView model viewportInfo.VisibleWidth viewportInfo.VisibleHeight
        | false -> None

    let viewportWithOverlay =
        match overlayMini with
        | Some overlay ->
            div [ ClassName "board-viewport-wrapper" ] [ viewportInfo.Element; overlay ]
        | None ->
            viewportInfo.Element

    let boardElements =
        viewportWithOverlay :: countdownOverlay model

    div [ ClassName "board-wrapper" ]
        [ div [ ClassName "board" ] boardElements
          collectedSection ]

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
        p [] [ str $"Score: {model.Game.Score}" ]
        p [] [ str $"High score: {highScoreText}" ]
        (match model.Vocabulary with
         | Some vocab ->
             div [ ClassName "vocab-card" ] [
                 h2 [] [ str $"Thème: {vocab.Topic}" ]
                 p [] [ str $"{vocab.Language1} -> {vocab.Language2}" ]
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
                 span [ ClassName "remaining" ] [ str remaining ]
             ])
        p [] [ str $"Next letter: {nextLetterDisplay}" ]
        p [] [ str $"Speed: {model.SpeedMs} ms" ]
        div [ ClassName "controls" ] [
            label [] [ str "Name" ]
            input [ ClassName "name-input"
                    Value model.PlayerName
                    Placeholder "Your name"
                    nameInputOnChange dispatch
                    Disabled (ModelState.isSaving model.Phase) ]
            button [ ClassName "action"
                     Disabled(ModelState.isSaving model.Phase || not model.Game.GameOver)
                     OnClick(fun _ -> dispatch SaveHighScore) ] [
                if ModelState.isSaving model.Phase then str "Saving..." else str "Save score"
            ]
            button [ ClassName "action secondary"
                     OnClick(fun _ -> dispatch Restart) ] [ str "Restart" ]
        ]
        div [ ClassName "scoreboard" ] (h2 [] [ str "Top Scores" ] :: scoreboardEntries model)
        if model.Game.GameOver then
            div [ ClassName "banner" ] [
                h2 [] [ str "Game Over" ]
                p [] [
                    str "Score: "
                    strong [] [ str $"{model.Game.Score}" ]
                ]
            ]
        else fragment [] []
    ] 

let view model dispatch =
    if ModelState.isSplash model.Phase then
        div [ ClassName "layout layout-splash" ] [
            div [ ClassName "splash-screen" ] [
                div [ ClassName "splash-content" ] [
                    h1 [] [ str "Baltic Sea Eel" ]
                    p [ ClassName "splash-description" ] [ str "Collect vocabulary letters while avoiding collisions." ]
                    button [ ClassName "action"
                             Disabled model.ScoresLoading
                             OnClick(fun _ -> dispatch StartGame) ] [ str "Start" ]
                    div [ ClassName "splash-scores" ] (
                        h2 [] [ str "Top Scores" ] :: scoreboardEntries model)
                ]
            ]
        ]
    else
        let hideStats = ModelState.shouldHideStats model
        let layoutClass =
            if hideStats then "layout layout-compact"
            else "layout"

        div [ ClassName layoutClass ] [
            boardView model
            if hideStats then
                fragment [] []
            else
                statsView model dispatch
        ]



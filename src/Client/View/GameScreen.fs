module Eel.Client.ViewParts.GameScreen

open System
#if FABLE_COMPILER
open Browser.Types
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
open Eel.Client.ViewParts.IntermissionView
open Eel.Client.ViewParts.CelebrationView

module CanvasRenderer = Eel.Client.Rendering.CanvasRenderer
#if FABLE_COMPILER
module KonvaRenderer = Eel.Client.Rendering.KonvaRenderer
#endif

module ModelState = Eel.Client.Model

let private viewportPadding = 32.0
let private minViewportSpan = 200.0
let private miniOverlayMinSize = 32.0

let private boardPropsEqual (prev: Model) (next: Model) =
    obj.ReferenceEquals(prev, next)
    || (prev.Game = next.Game
        && prev.BoardLetters = next.BoardLetters
        && prev.Gameplay.PendingMoveMs = next.Gameplay.PendingMoveMs
        && prev.TargetText = next.TargetText
        && prev.TargetIndex = next.TargetIndex
        && prev.Phase = next.Phase
        && prev.Intermission.CountdownMs = next.Intermission.CountdownMs
        && prev.Gameplay.HighlightWaves = next.Gameplay.HighlightWaves
        && prev.Gameplay.DirectionQueue = next.Gameplay.DirectionQueue
        && prev.Gameplay.LastEel = next.Gameplay.LastEel
        && prev.Gameplay.SpeedMs = next.Gameplay.SpeedMs
        && prev.Celebration.LastPhrase = next.Celebration.LastPhrase)

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
    OnChange(fun ev -> dispatch (SetPlayerName((ev.target :?> HTMLInputElement).value)))
#else
    OnChange(fun _ -> ())
#endif

let private clampFloat minValue maxValue value =
    value |> max minValue |> min maxValue

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

let private headPixelCenter (model: Model) =
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

let private boardViewportElement (model: Model) : BoardViewportInfo =
    let rawWidth =
        if model.ViewportWidth > 0.0 then model.ViewportWidth else ModelState.defaultViewportWidth

    let rawHeight =
        if model.ViewportHeight > 0.0 then model.ViewportHeight else ModelState.defaultViewportHeight
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

        let hideMiniEel = ModelState.shouldHideCelebrationEel model

        let eelSegmentsSource =
            if hideMiniEel then
                []
            elif ModelState.isRunning model.Phase then
                model.Game.Eel
            elif not (List.isEmpty model.Gameplay.LastEel) then
                model.Gameplay.LastEel
            else
                model.Game.Eel

        let eelSegments =
            eelSegmentsSource
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

let statsView model dispatch =
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
        p [] [ str $"Speed: {model.Gameplay.SpeedMs} ms" ]
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

let boardView model =
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

    let overlays = countdownOverlay countdownCelebrationOverlay model
    let boardElements = viewportWithOverlay :: overlays

    div [ ClassName "board-wrapper" ]
        [ div [ ClassName "board" ] boardElements
          collectedSection ]

let splashScores model = scoreboardEntries model

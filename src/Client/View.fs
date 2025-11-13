module Eel.Client.View

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

module CanvasRenderer = Eel.Client.Rendering.CanvasRenderer
#if FABLE_COMPILER
module KonvaRenderer = Eel.Client.Rendering.KonvaRenderer
#endif

module ModelState = Eel.Client.Model

let private boardPropsEqual (prev: Model) (next: Model) =
    obj.ReferenceEquals(prev, next)
    || (prev.Game = next.Game
        && prev.BoardLetters = next.BoardLetters
        && prev.PendingMoveMs = next.PendingMoveMs
        && prev.TargetText = next.TargetText
        && prev.TargetIndex = next.TargetIndex
        && prev.Phase = next.Phase
        && prev.CountdownMs = next.CountdownMs
        && prev.HighlightActive = next.HighlightActive
        && prev.HighlightProgress = next.HighlightProgress
        && prev.DirectionQueue = next.DirectionQueue
        && prev.LastEel = next.LastEel
        && prev.SpeedMs = next.SpeedMs)

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
        [ div [ ClassName "board-overlay" ] [ str label ] ]
    else
        []

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

    div [ ClassName "board-wrapper" ]
        [ div [ ClassName "board" ] (boardCanvas model :: countdownOverlay model)
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
        div [ ClassName "layout" ] [
            boardView model
            statsView model dispatch
        ]






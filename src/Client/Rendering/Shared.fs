module Eel.Client.Rendering.Shared

open System
open Fable.Core.JsInterop
open Eel.Client.Model
open Shared
open Shared.Game

module ModelState = Eel.Client.Model

let canvasPadding = 18.0
let cellSize = 26.0
let cellGap = 6.0
let cellStep = cellSize + cellGap

let foodBurstConfig = Config.gameplay.FoodBurst
let boardPixelWidth =
    (float Game.boardWidth * cellStep) - cellGap + (2.0 * canvasPadding)

let boardPixelHeight =
    (float Game.boardHeight * cellStep) - cellGap + (2.0 * canvasPadding)

type BoardDimensions =
    { PixelWidth: float
      PixelHeight: float }

let boardDimensions =
    { PixelWidth = boardPixelWidth
      PixelHeight = boardPixelHeight }

type SegmentRenderInfo =
    { Index: int
      X: float
      Y: float
      Rotation: float
      Highlight: float
      Letter: string option
      IsHead: bool
      IsTail: bool }

type RenderEngine =
    | Canvas
    | Konva

let highlightForSegment model idx =
    let baseHighlight =
        model.Gameplay.HighlightWaves
        |> List.map (fun progress ->
            let distance = abs (progress - float idx)
            if distance < 1.0 then 1.0 - distance else 0.0)
        |> List.fold max 0.0

    let weighted = baseHighlight * foodBurstConfig.LetterWeightFactor
    weighted |> max 0.0 |> min 1.0

let boardLetterFor (model: Model) (point: Point) =
    let index = point.Y * Game.boardWidth + point.X
    if index >= 0 && index < model.BoardLetters.Length then
        model.BoardLetters.[index]
    else
        ""

let collectAssignedLetters segmentCount model =
    if segmentCount <= 1 then
        Map.empty
    else
        let built, _ = progressParts model
        built
        |> Seq.toList
        |> List.mapi (fun idx ch -> idx, displayChar ch)
        |> List.fold
            (fun acc (idx, letter) ->
                if idx < segmentCount - 1 then
                    acc |> Map.add idx letter
                else
                    acc)
            Map.empty

let buildSegmentInfos (model: Model) =
    let renderSegments =
        if ModelState.isRunning model.Phase then
            model.Game.Eel
        elif not (List.isEmpty model.Gameplay.LastEel) then
            model.Gameplay.LastEel
        else
            model.Game.Eel

    let currentSegmentsRaw = renderSegments |> List.toArray
    let previousSegmentsRaw = model.Gameplay.LastEel |> List.toArray
    let maxSegments = max 1 (max currentSegmentsRaw.Length previousSegmentsRaw.Length)
    let assignedLetters = collectAssignedLetters maxSegments model

    let previewDirection =
        match model.Gameplay.DirectionQueue with
        | next :: _ -> next
        | [] -> model.Game.Direction

    let previewActive =
        ModelState.isRunning model.Phase
        && not (List.isEmpty model.Gameplay.DirectionQueue)

    let padSegments (segments: Point array) =
        match segments.Length with
        | 0 -> Array.init maxSegments (fun _ -> { X = 0; Y = 0 })
        | len when len = maxSegments -> segments
        | len ->
            Array.init maxSegments (fun idx ->
                match idx < len with
                | true -> segments.[idx]
                | false -> segments.[len - 1])

    let currentSegments = padSegments currentSegmentsRaw
    let previousSegments =
        if ModelState.isRunning model.Phase then padSegments previousSegmentsRaw else currentSegments

    let progress =
        if ModelState.isRunning model.Phase && model.Gameplay.SpeedMs > 0 then
            model.Gameplay.PendingMoveMs |> float |> fun v -> v / float model.Gameplay.SpeedMs |> max 0.0 |> min 0.999
        else
            0.0

    [|
        for idx in 0 .. maxSegments - 1 do
            let prev = previousSegments.[idx]
            let curr = currentSegments.[idx]

            let interpolate currentValue futureValue =
                currentValue + (futureValue - currentValue) * progress

            let x = canvasPadding + interpolate (float prev.X) (float curr.X) * cellStep
            let y = canvasPadding + interpolate (float prev.Y) (float curr.Y) * cellStep

            let baseRotation =
                let dx = float curr.X - float prev.X
                let dy = float curr.Y - float prev.Y
                if abs dx < 0.001 && abs dy < 0.001 then 0.0 else Math.Atan2(dy, dx)

            let appliedRotation =
                if idx = 0 && previewActive then
                    match previewDirection with
                    | Direction.Up -> -Math.PI / 2.
                    | Direction.Down -> Math.PI / 2.
                    | Direction.Left -> Math.PI
                    | Direction.Right -> 0.0
                else
                    baseRotation

            let letter =
                match Map.tryFind idx assignedLetters with
                | Some value -> Some value
                | None when idx <> maxSegments - 1 ->
                    let fallback = boardLetterFor model curr
                    Some(if String.IsNullOrWhiteSpace fallback then "·" else fallback)
                | _ -> None

            yield
                { Index = idx
                  X = x
                  Y = y
                  Rotation = appliedRotation
                  Highlight = highlightForSegment model idx
                  Letter = letter
                  IsHead = idx = 0
                  IsTail = idx = maxSegments - 1 }
    |]

let detectRenderEngine () =
#if FABLE_COMPILER
    let configured: obj = Browser.Dom.window?__RENDER_ENGINE__
    match configured with
    | :? string as value when String.Equals(value, "konva", StringComparison.OrdinalIgnoreCase) -> RenderEngine.Konva
    | _ -> RenderEngine.Canvas
#else
    RenderEngine.Canvas
#endif

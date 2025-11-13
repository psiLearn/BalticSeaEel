module Eel.Client.Rendering.CanvasRenderer

open System
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Fable.React
open Fable.React.Props
open Eel.Client.Model
open Shared
open Shared.Game
open Eel.Client.Rendering.Shared

module ModelState = Eel.Client.Model

#if FABLE_COMPILER
[<Import("useRef", "react")>]
let private reactUseRef<'T> (value: 'T) : obj = jsNative

[<Import("useEffect", "react")>]
let private reactUseEffect (callback: unit -> obj) (deps: obj array) : unit = jsNative

let private prepareCanvas (canvas: HTMLCanvasElement) boardWidth boardHeight =
    let pixelWidth = (float boardWidth * cellStep) - cellGap + (2.0 * canvasPadding)
    let pixelHeight = (float boardHeight * cellStep) - cellGap + (2.0 * canvasPadding)
    let dpr =
        if isNull Browser.Dom.window || Browser.Dom.window.devicePixelRatio <= 0.0 then 1.0 else Browser.Dom.window.devicePixelRatio

    canvas?style?width <- $"{pixelWidth}px"
    canvas?style?height <- $"{pixelHeight}px"

    let scaledWidth = pixelWidth * dpr |> int
    let scaledHeight = pixelHeight * dpr |> int

    if canvas.width <> scaledWidth then canvas.width <- scaledWidth
    if canvas.height <> scaledHeight then canvas.height <- scaledHeight

    let ctx = canvas.getContext_2d()
    ctx.setTransform(1., 0., 0., 1., 0., 0.)
    ctx.scale(dpr, dpr)
    ctx, pixelWidth, pixelHeight

let private drawLetter (ctx: CanvasRenderingContext2D) text x y rotation highlight =
    ctx?save()
    ctx?translate(x + cellSize / 2., y + cellSize / 2.)
    if rotation <> 0.0 then
        ctx?rotate(rotation)

    let baseColor = "#e6ecec"
    let color =
        if highlight <= 0.0 then
            baseColor
        else
            let alpha = min 1.0 (0.6 + (0.4 * highlight))
            $"rgba(255,255,255,{alpha})"

    let fontSize = 14.0 + (6.0 * highlight)
    ctx?fillStyle <- color
    ctx.font <- $"bold {fontSize}px 'Segoe UI'"
    ctx?textAlign <- "center"
    ctx?textBaseline <- "middle"
    ctx.fillText (text, 0., 0.)
    ctx?restore()

let private drawGridCells (ctx: CanvasRenderingContext2D) (model: Model) =
    for y in 0 .. Game.boardHeight - 1 do
        for x in 0 .. Game.boardWidth - 1 do
            let drawX = canvasPadding + float x * cellStep
            let drawY = canvasPadding + float y * cellStep
            ctx?fillStyle <- "rgba(255,255,255,0.04)"
            ctx.fillRect (drawX, drawY, cellSize, cellSize)

            let letter = boardLetterFor model { X = x; Y = y }
            if not (String.IsNullOrWhiteSpace letter) then
                drawLetter ctx letter drawX drawY 0.0 0.0

let private drawFoodTokens (ctx: CanvasRenderingContext2D) (model: Model) =
    let nextLetter = nextTargetChar model |> Option.map displayChar

    let tokenLetter token =
        match token.Status, nextLetter with
        | FoodStatus.Active, Some letter -> letter
        | FoodStatus.Active, None -> ""
        | FoodStatus.Collected, _ ->
            if token.LetterIndex >= 0 && token.LetterIndex < model.TargetText.Length then
                displayChar model.TargetText.[token.LetterIndex]
            else
                "?"

    model.Game.Foods
    |> List.iter (fun token ->
        let drawX = canvasPadding + float token.Position.X * cellStep
        let drawY = canvasPadding + float token.Position.Y * cellStep
        let letter = tokenLetter token

        match token.Status with
        | FoodStatus.Active ->
            ctx?fillStyle <- "rgba(88,161,107,0.8)"
            ctx.beginPath()
            ctx.arc (drawX + cellSize / 2., drawY + cellSize / 2., cellSize / 2.6, 0., Math.PI * 2.)
            ctx.fill()
            if letter <> "" then drawLetter ctx letter drawX drawY 0.0 0.0
        | FoodStatus.Collected ->
            ctx?fillStyle <- "rgba(255,255,255,0.15)"
            ctx.fillRect (drawX, drawY, cellSize, cellSize)
            if letter <> "" then drawLetter ctx letter drawX drawY 0.0 0.0)

let private drawSegments (ctx: CanvasRenderingContext2D) (model: Model) =
    buildSegmentInfos model
    |> Array.iter (fun info ->
        ctx.save()
        ctx.translate (info.X + cellSize / 2., info.Y + cellSize / 2.)
        ctx.rotate (info.Rotation + (if info.IsTail then -Math.PI / 2. else 0.0))
        ctx?fillStyle <- (if info.IsHead then "#4c7e7b" else "#2b4b4e")
        ctx.fillRect (-cellSize / 2., -cellSize / 2., cellSize, cellSize)
        ctx.restore()

        match info.Letter with
        | Some letter -> drawLetter ctx letter info.X info.Y (info.Rotation + Math.PI) info.Highlight
        | None -> ())

let private drawBoard (canvas: HTMLCanvasElement) (model: Model) =
    let ctx, pixelWidth, pixelHeight = prepareCanvas canvas Game.boardWidth Game.boardHeight
    ctx.clearRect (0., 0., pixelWidth, pixelHeight)
    drawGridCells ctx model
    drawFoodTokens ctx model
    drawSegments ctx model

let view (model: Model) =
    let canvasRef = reactUseRef (None: HTMLCanvasElement option)

    reactUseEffect
        (fun () ->
            let current = canvasRef?current |> unbox<HTMLCanvasElement option>
            match current with
            | Some canvas -> drawBoard canvas model
            | None -> ()
            box JS.undefined)
        [| box model.Game
           box model.BoardLetters
           box model.PendingMoveMs
           box model.TargetText
           box model.LastEel
           box model.SpeedMs
           box (ModelState.isRunning model.Phase) |]

    canvas [ Ref(fun el ->
                let value =
                    if isNull el then None
                    else Some (el :?> HTMLCanvasElement)
                canvasRef?current <- value)
             ClassName "board-canvas" ] []
#else
let view (_: Model) =
    div [] [ str "Canvas renderer available only in browser build." ]
#endif

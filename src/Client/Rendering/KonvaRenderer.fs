#if FABLE_COMPILER
module Eel.Client.Rendering.KonvaRenderer

open System
open System.Collections.Generic
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

[<ImportDefault("konva")>]
let private konva: obj = jsNative

[<Import("useRef", "react")>]
let private reactUseRef<'T> (value: 'T) : obj = jsNative

[<Import("useEffect", "react")>]
let private reactUseEffect (callback: unit -> obj) (deps: obj array) : unit = jsNative

let private ensureStage (hostRef: obj) (stageRef: obj) width height =
    match stageRef?current |> unbox<obj option> with
    | Some stage ->
        stage?width(width) |> ignore
        stage?height(height) |> ignore
        stage
    | None ->
        let host =
            hostRef?current
            |> unbox<HTMLDivElement option>
            |> Option.defaultWith (fun _ -> failwith "Missing Konva host element")

        let stage =
            createNew
                (konva?Stage)
                (createObj [ "container" ==> host
                             "width" ==> width
                             "height" ==> height ])
        stageRef?current <- Some stage
        stage

let private ensureLayer (stage: obj) (layerRef: obj) (initializer: obj -> unit) =
    match layerRef?current |> unbox<obj option> with
    | Some layer -> layer
    | None ->
        let layer = createNew (konva?Layer) (createObj [])
        stage?add(layer) |> ignore
        layerRef?current <- Some layer
        initializer layer
        layer

let private drawBackground (layer: obj) width height =
    let bg =
        createNew
            (konva?Rect)
            (createObj [ "x" ==> 0
                         "y" ==> 0
                         "width" ==> width
                         "height" ==> height
                         "fill" ==> "#0d2228" ])
    layer?add(bg) |> ignore
    layer?batchDraw() |> ignore

let private ensureDynamicLayer (stage: obj) (layerRef: obj) =
    match layerRef?current |> unbox<obj option> with
    | Some layer -> layer
    | None ->
        let layer =
            createNew
                (konva?Layer)
                (createObj [ "listening" ==> false
                             "clearBeforeDraw" ==> false ])
        stage?add(layer) |> ignore
        layerRef?current <- Some layer
        layer

let private foodBurstConfig = Config.gameplay.FoodBurst
let private foodVisualConfig = Config.gameplay.FoodVisuals
let private boardConfig = Config.gameplay.BoardVisuals

let private clamp01 value = value |> max 0.0 |> min 1.0

let private blendWithWhite (hex: string) (intensity: float) =
    let factor = clamp01 intensity
    if factor <= 0.0 then
        hex
    else
        let clean = hex.Trim().TrimStart('#')
        if clean.Length <> 6 then hex
        else
            let parse idx = Convert.ToInt32(clean.Substring(idx, 2), 16)
            let mix comp =
                let compFloat = float comp
                compFloat + (255.0 - compFloat) * factor |> round |> int |> min 255 |> max 0
            let r = parse 0 |> mix
            let g = parse 2 |> mix
            let b = parse 4 |> mix
            $"#{r:X2}{g:X2}{b:X2}"

let private ensureNodeList (refObj: obj) =
    match refObj?current |> unbox<ResizeArray<obj> option> with
    | Some nodes -> nodes
    | None ->
        let nodes = ResizeArray<obj>()
        refObj?current <- Some nodes
        nodes

let private tryFindChild (parent: obj) (selector: string) =
    parent?find(selector)
    |> unbox<obj array>
    |> Array.tryHead

let private ensureChild (parent: obj) (selector: string) (factory: unit -> obj) =
    match tryFindChild parent selector with
    | Some child -> child
    | None ->
        let child = factory()
        parent?add(child) |> ignore
        child

let private ensureNode (layer: obj) (nodes: ResizeArray<obj>) (index: int) (createNode: unit -> obj) =
    if index < nodes.Count then
        nodes.[index]
    else
        let node = createNode()
        nodes.Add node
        layer?add(node) |> ignore
        node

let private pruneNodes (nodes: ResizeArray<obj>) (desiredCount: int) =
    while nodes.Count > desiredCount do
        let lastIndex = nodes.Count - 1
        let node = nodes.[lastIndex]
        node?destroy() |> ignore
        nodes.RemoveAt(lastIndex)

let private syncBoardCells (layer: obj) (nodes: ResizeArray<obj>) (letters: string array) =
    let highlightFill = $"rgba(255,255,255,{boardConfig.CellHighlightOpacity})"
    let totalCells = Game.boardWidth * Game.boardHeight

    for idx in 0 .. totalCells - 1 do
        let node =
            ensureNode
                layer
                nodes
                idx
                (fun () ->
                    createNew
                        (konva?Rect)
                        (createObj [ "width" ==> cellSize
                                     "height" ==> cellSize
                                     "listening" ==> false ]))

        let x = idx % Game.boardWidth
        let y = idx / Game.boardWidth
        let drawX = canvasPadding + float x * cellStep
        let drawY = canvasPadding + float y * cellStep
        let letter =
            if idx < letters.Length then letters.[idx] else ""

        let fillColor =
            if String.IsNullOrWhiteSpace letter then
                boardConfig.CellBaseColor
            else
                highlightFill

        node?setAttrs(
            createObj [ "x" ==> drawX
                        "y" ==> drawY
                        "fill" ==> fillColor
                        "opacity" ==> 1.0 ])

    pruneNodes nodes totalCells

let private syncBoardLetters (layer: obj) (nodes: ResizeArray<obj>) (letters: string array) =
    let totalCells = Game.boardWidth * Game.boardHeight
    let alpha = clamp01 boardConfig.LetterAlpha

    for idx in 0 .. totalCells - 1 do
        let node =
            ensureNode
                layer
                nodes
                idx
                (fun () ->
                    createNew
                        (konva?Text)
                        (createObj [ "fontStyle" ==> "bold"
                                     "align" ==> "center"
                                     "width" ==> cellSize
                                     "height" ==> cellSize
                                     "offsetX" ==> cellSize / 2.
                                     "offsetY" ==> cellSize / 2.
                                     "listening" ==> false ]))

        let x = idx % Game.boardWidth
        let y = idx / Game.boardWidth
        let drawX = canvasPadding + float x * cellStep
        let drawY = canvasPadding + float y * cellStep
        let letter =
            if idx < letters.Length then letters.[idx] else ""

        node?setAttrs(
            createObj [ "x" ==> (drawX + cellSize / 2.)
                        "y" ==> (drawY + cellSize / 2.)
                        "text" ==> letter
                        "visible" ==> (not (String.IsNullOrWhiteSpace letter))
                        "fontSize" ==> boardConfig.LetterSize
                        "fill" ==> boardConfig.LetterColor
                        "opacity" ==> alpha ])

    pruneNodes nodes totalCells

let private syncFoodNodes (layer: obj) (nodes: ResizeArray<obj>) foods model =
    let letterFor token =
        match token.LetterIndex with
        | idx when idx >= 0 && idx < model.TargetText.Length -> displayChar model.TargetText.[idx]
        | _ -> ""

    foods
    |> List.iteri (fun idx token ->
        let node =
            ensureNode
                layer
                nodes
                idx
                (fun () ->
                    createNew
                        (konva?Group)
                        (createObj [ "listening" ==> false ]))

        let drawX = canvasPadding + float token.Position.X * cellStep
        let drawY = canvasPadding + float token.Position.Y * cellStep
        let circle =
            ensureChild
                node
                ".food-circle"
                (fun () ->
                    createNew
                        (konva?Circle)
                        (createObj [ "name" ==> "food-circle" ]))

        let fillColor =
            match token.Status with
            | FoodStatus.Collected -> foodVisualConfig.CollectedFill
            | _ -> foodVisualConfig.ActiveFill

        circle?setAttrs(
            createObj [ "x" ==> drawX + cellSize / 2.
                        "y" ==> drawY + cellSize / 2.
                        "radius" ==> cellSize / 2.6
                        "fill" ==> fillColor ])

        let textValue = letterFor token

        if foodVisualConfig.ShowLetters && textValue <> "" then
            let textNode =
                ensureChild
                    node
                    ".food-letter"
                    (fun () ->
                        createNew
                            (konva?Text)
                            (createObj [ "name" ==> "food-letter"
                                         "fontStyle" ==> "bold"
                                         "align" ==> "center"
                                         "width" ==> cellSize
                                         "height" ==> cellSize
                                         "offsetX" ==> cellSize / 2.
                                         "offsetY" ==> cellSize / 2.
                                         "fill" ==> "#ffffff" ]))

            textNode?setAttrs(
                createObj [ "x" ==> (drawX + cellSize / 2.)
                            "y" ==> (drawY + cellSize / 2.)
                            "text" ==> textValue
                            "visible" ==> true
                            "fontSize" ==> boardConfig.LetterSize ])
        else
            tryFindChild node ".food-letter"
            |> Option.iter (fun text -> text?visible(false) |> ignore))

    pruneNodes nodes (List.length foods)

let private syncSegmentRects (layer: obj) (nodes: ResizeArray<obj>) (segments: SegmentRenderInfo list) =
    segments
    |> List.iteri (fun idx info ->
        let node =
            ensureNode
                layer
                nodes
                idx
                (fun () ->
                    createNew
                        (konva?Rect)
                        (createObj [ "width" ==> cellSize
                                     "height" ==> cellSize
                                     "offsetX" ==> cellSize / 2.
                                     "offsetY" ==> cellSize / 2.
                                     "cornerRadius" ==> 4
                                     "listening" ==> false ]))

        let baseColor = if info.IsHead then "#4c7e7b" else "#2b4b4e"
        let highlightFactor = clamp01 (info.Highlight * foodBurstConfig.SegmentWeightFactor)
        let fillColor = blendWithWhite baseColor highlightFactor

        node?setAttrs(
            createObj [ "x" ==> (info.X + cellSize / 2.)
                        "y" ==> (info.Y + cellSize / 2.)
                        "rotation" ==> (info.Rotation * 180.0 / Math.PI)
                        "fill" ==> fillColor ]))

    pruneNodes nodes (List.length segments)

let private syncSegmentLetters (layer: obj) (nodes: ResizeArray<obj>) (segments: SegmentRenderInfo list) =
    segments
    |> List.iteri (fun idx info ->
        let node =
            ensureNode
                layer
                nodes
                idx
                (fun () ->
                    createNew
                        (konva?Text)
                        (createObj [ "fontStyle" ==> "bold"
                                     "align" ==> "center"
                                     "width" ==> cellSize
                                     "height" ==> cellSize
                                     "offsetX" ==> cellSize / 2.
                                     "offsetY" ==> cellSize / 2.
                                     "listening" ==> false ]))

        let letter = info.Letter |> Option.defaultValue ""
        let highlight = max 0.0 info.Highlight
        let alpha = if highlight <= 0.0 then 0.85 else min 1.0 (0.6 + (0.4 * highlight))
        let fontBase = (14.0 + (6.0 * highlight)) * foodBurstConfig.LetterSizeFactor

        node?setAttrs(
            createObj [ "x" ==> (info.X + cellSize / 2.)
                        "y" ==> (info.Y + cellSize / 2.)
                        "text" ==> letter
                        "visible" ==> (not (String.IsNullOrWhiteSpace letter))
                        "fontSize" ==> fontBase
                        "fill" ==> $"rgba(255,255,255,{alpha})" ]))

    pruneNodes nodes (List.length segments)

let private renderDynamicLayer
    (layer: obj)
    (foodNodes: ResizeArray<obj>)
    (segmentNodes: ResizeArray<obj>)
    (letterNodes: ResizeArray<obj>)
    (model: Model)
    =
    let activeFoods = model.Game.Foods |> List.filter (fun token -> token.Status = FoodStatus.Active)
    syncFoodNodes layer foodNodes model.Game.Foods model

    let segments = model |> buildSegmentInfos |> Array.toList
    syncSegmentRects layer segmentNodes segments
    syncSegmentLetters layer letterNodes segments

    layer?batchDraw() |> ignore

let private renderKonvaBoard
    (hostRef: obj)
    (stageRef: obj)
    (bgLayerRef: obj)
    (dynamicLayerRef: obj)
    (boardCellNodesRef: obj)
    (boardLetterNodesRef: obj)
    (foodNodesRef: obj)
    (segmentNodesRef: obj)
    (letterNodesRef: obj)
    (model: Model)
    =
    let width = (float Game.boardWidth * cellStep) - cellGap + (2.0 * canvasPadding)
    let height = (float Game.boardHeight * cellStep) - cellGap + (2.0 * canvasPadding)
    let stage = ensureStage hostRef stageRef width height
    let backgroundLayer = ensureLayer stage bgLayerRef (fun layer -> drawBackground layer width height)
    let dynamicLayer = ensureDynamicLayer stage dynamicLayerRef
    let boardCellNodes = ensureNodeList boardCellNodesRef
    let boardLetterNodes = ensureNodeList boardLetterNodesRef
    let foodNodes = ensureNodeList foodNodesRef
    let segmentNodes = ensureNodeList segmentNodesRef
    let letterNodes = ensureNodeList letterNodesRef

    syncBoardCells dynamicLayer boardCellNodes model.BoardLetters
    syncBoardLetters dynamicLayer boardLetterNodes model.BoardLetters
    renderDynamicLayer dynamicLayer foodNodes segmentNodes letterNodes model
    dynamicLayer?moveToTop() |> ignore
    backgroundLayer?moveToBottom() |> ignore

let view (model: Model) =
    let hostRef = reactUseRef (None: HTMLDivElement option)
    let stageRef = reactUseRef (None: obj option)
    let backgroundLayerRef = reactUseRef (None: obj option)
    let dynamicLayerRef = reactUseRef (None: obj option)
    let boardCellNodesRef = reactUseRef (None: ResizeArray<obj> option)
    let boardLetterNodesRef = reactUseRef (None: ResizeArray<obj> option)
    let foodNodesRef = reactUseRef (None: ResizeArray<obj> option)
    let segmentNodesRef = reactUseRef (None: ResizeArray<obj> option)
    let letterNodesRef = reactUseRef (None: ResizeArray<obj> option)

    reactUseEffect
        (fun () ->
            match hostRef?current |> unbox<HTMLDivElement option> with
            | Some _ ->
                renderKonvaBoard
                    hostRef
                    stageRef
                    backgroundLayerRef
                    dynamicLayerRef
                    boardCellNodesRef
                    boardLetterNodesRef
                    foodNodesRef
                    segmentNodesRef
                    letterNodesRef
                    model
            | None -> ()

            box JS.undefined)
        [| box model.Game
           box model.BoardLetters
           box model.PendingMoveMs
           box model.TargetText
           box model.LastEel
           box model.SpeedMs
           box model.HighlightWaves
           box model.DirectionQueue
           box model.Phase
           box model.TargetIndex |]

    reactUseEffect
        (fun () ->
            box (
                fun () ->
                    match stageRef?current |> unbox<obj option> with
                    | Some stage ->
                        stage?destroyChildren() |> ignore
                        stage?destroy() |> ignore
                        stageRef?current <- None
                        backgroundLayerRef?current <- None
                        dynamicLayerRef?current <- None
                        boardCellNodesRef?current <- None
                        boardLetterNodesRef?current <- None
                        foodNodesRef?current <- None
                        segmentNodesRef?current <- None
                        letterNodesRef?current <- None
                    | None -> ())
            )
        [||]

    div [ ClassName "board board-konva"
          Ref(fun el ->
              hostRef?current <-
                  if isNull el then None
                  else Some (el :?> HTMLDivElement)) ] []
#else
module Eel.Client.Rendering.KonvaRenderer

open Eel.Client.Model

let view (_: Model) : obj = obj ()
#endif

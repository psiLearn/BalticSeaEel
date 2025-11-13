#if FABLE_COMPILER
module Eel.Client.Rendering.KonvaRenderer

open System
open Browser.Types
open System.Collections.Generic
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

let private ensureNodeList (refObj: obj) =
    match refObj?current |> unbox<ResizeArray<obj> option> with
    | Some nodes -> nodes
    | None ->
        let nodes = ResizeArray<obj>()
        refObj?current <- Some nodes
        nodes

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

let private syncFoodNodes (layer: obj) (nodes: ResizeArray<obj>) foods =
    foods
    |> List.iteri (fun idx token ->
        let node =
            ensureNode
                layer
                nodes
                idx
                (fun () ->
                    createNew
                        (konva?Circle)
                        (createObj [ "listening" ==> false ]))

        let drawX = canvasPadding + float token.Position.X * cellStep
        let drawY = canvasPadding + float token.Position.Y * cellStep

        node?setAttrs(
            createObj [ "x" ==> drawX + cellSize / 2.
                        "y" ==> drawY + cellSize / 2.
                        "radius" ==> cellSize / 2.6
                        "fill" ==> "rgba(88,161,107,0.85)" ]))

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

        node?setAttrs(
            createObj [ "x" ==> (info.X + cellSize / 2.)
                        "y" ==> (info.Y + cellSize / 2.)
                        "rotation" ==> (info.Rotation * 180.0 / Math.PI)
                        "fill" ==> (if info.IsHead then "#4c7e7b" else "#2b4b4e") ]))

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

        node?setAttrs(
            createObj [ "x" ==> (info.X + cellSize / 2.)
                        "y" ==> (info.Y + cellSize / 2.)
                        "text" ==> letter
                        "visible" ==> (not (String.IsNullOrWhiteSpace letter))
                        "fontSize" ==> (14.0 + (6.0 * highlight))
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
    syncFoodNodes layer foodNodes activeFoods

    let segments = model |> buildSegmentInfos |> Array.toList
    syncSegmentRects layer segmentNodes segments
    syncSegmentLetters layer letterNodes segments

    layer?batchDraw() |> ignore

let private renderKonvaBoard
    (hostRef: obj)
    (stageRef: obj)
    (bgLayerRef: obj)
    (dynamicLayerRef: obj)
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
    let foodNodes = ensureNodeList foodNodesRef
    let segmentNodes = ensureNodeList segmentNodesRef
    let letterNodes = ensureNodeList letterNodesRef

    renderDynamicLayer dynamicLayer foodNodes segmentNodes letterNodes model
    dynamicLayer?moveToTop() |> ignore
    backgroundLayer?moveToBottom() |> ignore

let view (model: Model) =
    let hostRef = reactUseRef (None: HTMLDivElement option)
    let stageRef = reactUseRef (None: obj option)
    let backgroundLayerRef = reactUseRef (None: obj option)
    let dynamicLayerRef = reactUseRef (None: obj option)
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
           box model.HighlightActive
           box model.HighlightProgress
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

open Fable.React
open Eel.Client.Model

let view (_: Model) =
    div [ ClassName "board board-konva" ]
        [ str "Konva renderer unavailable on .NET runtime." ]
#endif

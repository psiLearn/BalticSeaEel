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

module ModelState = Eel.Client.Model

let private canvasPadding = 18.0
let private cellSize = 26.0
let private cellGap = 6.0
let private cellStep = cellSize + cellGap
let private directionToAngle direction =
    match direction with
    | Direction.Up -> -Math.PI / 2.
    | Direction.Down -> Math.PI / 2.
    | Direction.Left -> Math.PI
    | Direction.Right -> 0.0

let private highlightForSegment model idx =
    if model.HighlightActive then
        let distance = abs (model.HighlightProgress - float idx)
        if distance < 1.0 then 1.0 - distance else 0.0
    else
        0.0

#if FABLE_COMPILER
[<Import("useRef", "react")>]
let private reactUseRef<'T> (value: 'T) : obj = jsNative

[<Import("useEffect", "react")>]
let private reactUseEffect (callback: unit -> unit) (deps: obj array) : unit = jsNative

let private boardPixelSize dimension =
    (float dimension * cellStep) - cellGap + (2.0 * canvasPadding)

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

let private prepareCanvas (canvas: HTMLCanvasElement) boardWidth boardHeight =
    let pixelWidth = boardPixelSize boardWidth
    let pixelHeight = boardPixelSize boardHeight
    let dpr =
        if isNull window || window.devicePixelRatio <= 0.0 then 1.0 else window.devicePixelRatio

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

let private boardLetterFor (model: Model) (point: Point) =
    let index = point.Y * Game.boardWidth + point.X
    if index >= 0 && index < model.BoardLetters.Length then
        model.BoardLetters.[index]
    else
        ""

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
    let currentSegmentsRaw = model.Game.Eel |> List.toArray
    let previousSegmentsRaw = model.LastEel |> List.toArray
    let maxSegments = max 1 (max currentSegmentsRaw.Length previousSegmentsRaw.Length)
    let previewDirection =
        match model.DirectionQueue with
        | next :: _ -> next
        | [] -> model.Game.Direction
    let previewActive =
        ModelState.isRunning model.Phase
        && not (List.isEmpty model.DirectionQueue)

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
        if ModelState.isRunning model.Phase && model.SpeedMs > 0 then
            model.PendingMoveMs |> float |> fun v -> v / float model.SpeedMs |> max 0.0 |> min 0.999
        else
            0.0

    let fallbackLetter point =
        let baseLetter = boardLetterFor model point
        if String.IsNullOrWhiteSpace baseLetter then "·" else baseLetter

    let built, _ = progressParts model
    let builtChars = built |> Seq.toList

    let assignedLetters =
        builtChars
        |> List.mapi (fun idx ch -> idx, displayChar ch)
        |> List.fold
            (fun state (idx, letter) ->
                if idx < model.Game.Eel.Length - 1 then
                    state |> Map.add idx letter
                else
                    state)
            Map.empty

    for idx in 0 .. maxSegments - 1 do
        let prev = previousSegments.[idx]
        let prevX = float prev.X
        let prevY = float prev.Y
        let currentPoint = currentSegments.[idx]
        let currX = float currentPoint.X
        let currY = float currentPoint.Y
        let curr = currentPoint

        let interpolate currentValue futureValue =
            currentValue + (futureValue - currentValue) * progress

        let x = canvasPadding + interpolate prevX currX * cellStep
        let y = canvasPadding + interpolate prevY currY * cellStep

        let baseRotation =
            let dx = currX - prevX
            let dy = currY - prevY
            if abs dx < 0.001 && abs dy < 0.001 then 0.0 else Math.Atan2(dy, dx)

        let appliedRotation =
            if idx = 0 && previewActive then
                directionToAngle previewDirection
            else
                baseRotation

        ctx.save()
        ctx.translate (x + cellSize / 2., y + cellSize / 2.)
        ctx.rotate (appliedRotation + (if idx = maxSegments - 1 then -Math.PI / 2. else 0.))
        ctx?fillStyle <- (if idx = 0 then "#4c7e7b" else "#2b4b4e")
        ctx.fillRect (-cellSize / 2., -cellSize / 2., cellSize, cellSize)
        ctx.restore()

        let highlightStrength = highlightForSegment model idx

        match Map.tryFind idx assignedLetters with
        | Some letter when not (String.IsNullOrWhiteSpace letter) ->
            drawLetter ctx letter x y (appliedRotation + Math.PI) highlightStrength
        | _ ->
            if idx <> maxSegments - 1 then
                let fallback = fallbackLetter curr
                if fallback <> "" then drawLetter ctx fallback x y (appliedRotation + Math.PI) highlightStrength
let private drawBoard (canvas: HTMLCanvasElement) (model: Model) =
    let boardWidth = Game.boardWidth
    let boardHeight = Game.boardHeight
    let ctx, pixelWidth, pixelHeight = prepareCanvas canvas boardWidth boardHeight

    ctx.clearRect (0., 0., pixelWidth, pixelHeight)
    drawGridCells ctx model
    drawFoodTokens ctx model
    drawSegments ctx model



let private boardCanvas =
    FunctionComponent.Of(
        (fun (model: Model) ->
            let canvasRef = reactUseRef (None: HTMLCanvasElement option)

            reactUseEffect
                (fun () ->
                    let current = canvasRef?current |> unbox<HTMLCanvasElement option>
                    match current with
                    | Some canvas -> drawBoard canvas model
                    | None -> ())
                [| box model.Game
                   box model.BoardLetters
                   box model.PendingMoveMs
                   box model.TargetText
                   box model.LastEel
                   box model.SpeedMs
                   box (ModelState.isRunning model.Phase) |]

            canvas [ Ref(fun el ->
                        let value =
                            if isNull el then
                                None
                            else
                                Some (el :?> HTMLCanvasElement)
                        canvasRef?current <- value)
                     ClassName "board-canvas" ] []),
        memoizeWith = equalsButFunctions)
#else
let private boardCanvas =
    FunctionComponent.Of(
        (fun (_: Model) ->
            div [ ClassName "board-canvas board-canvas--placeholder" ]
                [ str "Canvas renderer is only available in the browser build." ]),
        memoizeWith = equalsButFunctions)
#endif

let private nameInputOnChange dispatch =
#if FABLE_COMPILER
    OnChange(fun ev -> dispatch (SetPlayerName((ev.target :?> HTMLInputElement).value)))
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








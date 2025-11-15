module Tests

open System
open Xunit
open Elmish
open Eel.Client.Model
open Shared

module ModelState = Eel.Client.Model

module Game = Shared.Game
module Update = Eel.Client.Update
module Loop = Eel.Client.GameLoop
module RenderingShared = Eel.Client.Rendering.Shared
module View = Eel.Client.View

let tickDelta = 40

let private withTarget target index =
    { initModel with
        TargetText = target
        TargetIndex = index
        PhraseQueue = []
        Phase = GamePhase.Running
        ScoresLoading = false }

let private withPhase phase model = { model with Phase = phase }

let private foodToken position letterIndex status =
    { LetterIndex = letterIndex
      Position = position
      Status = status }

let private cmdHasEffects cmd =
    match cmd with
    | [] -> false
    | _ -> true

let private cmdIsEmpty cmd = not (cmdHasEffects cmd)

[<Fact>]
let ``nextTargetChar returns current character when index is in range`` () =
    let model = withTarget "Eel" 1
    let actual = nextTargetChar model
    Assert.Equal<char option>(Some 'e', actual)

[<Fact>]
let ``nextTargetChar returns none when phrase is completed`` () =
    let model = withTarget "Eel" 3
    let actual = nextTargetChar model
    Assert.Equal<char option>(None, actual)

[<Fact>]
let ``progressParts splits built and remaining segments`` () =
    let model = withTarget "abc" 2
    let built, remaining = progressParts model
    Assert.Equal("ab", built)
    Assert.Equal("c", remaining)

[<Fact>]
let ``progressParts clamps built portion to phrase length`` () =
    let model = withTarget "snack" 99
    let built, remaining = progressParts model
    Assert.Equal("snack", built)
    Assert.Equal("", remaining)

[<Fact>]
let ``changeDirection ignores opposite direction`` () =
    let state =
        { Game.initialState () with
            Direction = Direction.Left }

    let updated = Game.changeDirection Direction.Right state
    Assert.Equal(Direction.Left, updated.Direction)

[<Fact>]
let ``move marks game over when head crosses board boundary`` () =
    let state =
        { Game.initialState () with
            Eel = [ { X = Game.boardWidth - 1; Y = 3 } ]
            Direction = Direction.Right
            Score = 0
            GameOver = false }

    let next = Game.move state
    Assert.True(next.GameOver)

[<Fact>]
let ``move grows eel and updates score when food is collected`` () =
    let head = { X = 5; Y = 5 }
    let foodPos = { X = head.X + 1; Y = head.Y }

    let state =
        { Game.initialState () with
            Eel = [ head ]
            Direction = Direction.Right
            Foods = [ foodToken foodPos 0 FoodStatus.Active ]
            Score = 0
            GameOver = false }

    let next = Game.move state

    Assert.False(next.GameOver)
    Assert.Equal(10, next.Score)
    Assert.Equal(head.X + 1, next.Eel.Head.X)
    Assert.Equal(2, next.Eel.Length)
    let updatedToken = next.Foods |> List.find (fun token -> token.LetterIndex = 0)
    Assert.Equal(FoodStatus.Collected, updatedToken.Status)

[<Fact>]
let ``update Tick stops loop when game already over`` () =
    let game =
        { Game.initialState () with
            Score = 20
            GameOver = true }

    let model =
        { initModel with
            Game = game
            Phase = GamePhase.Running
            HighScore = Some { Name = "A"; Score = 999 }
            ScoresLoading = false }
    let model = { model with PendingMoveMs = model.SpeedMs }

    let updated, cmd = Update.update (Tick tickDelta) model

    Assert.False(ModelState.isRunning updated.Phase)
    Assert.True(updated.Game.GameOver)
    Assert.Equal(game.Score, updated.Game.Score)
    Assert.NotEmpty(cmd)

[<Fact>]
let ``update Tick advances target when collecting letter`` () =
    let head = { X = 5; Y = 5 }
    let foodPos = { X = head.X + 1; Y = head.Y }

    let modelGame =
        { Game.initialState () with
            Eel = [ head ]
            Direction = Direction.Right
            Foods = [ foodToken foodPos 0 FoodStatus.Active ]
            Score = 0
            GameOver = false }

    let model =
        { initModel with
            Game = modelGame
            Phase = GamePhase.Running
            HighScore = Some { Name = "A"; Score = 999 }
            TargetText = "ok"
            PhraseQueue = []
            TargetIndex = 0
            ScoresLoading = false }
    let model = { model with PendingMoveMs = model.SpeedMs }

    let updated, cmd = Update.update (Tick tickDelta) model

    Assert.True(ModelState.isRunning updated.Phase)
    Assert.Equal(1, updated.TargetIndex)
    Assert.Equal("ok", updated.TargetText)
    Assert.Empty(cmd)
    let collectedToken = updated.Game.Foods |> List.find (fun token -> token.LetterIndex = 0)
    Assert.Equal(FoodStatus.Collected, collectedToken.Status)
    Assert.True(updated.Game.Foods |> List.exists (fun token -> token.LetterIndex = 1 && token.Status = FoodStatus.Active))
    Assert.True(updated.Game.Foods |> List.filter (fun token -> token.Status = FoodStatus.Active) |> List.length <= Loop.maxVisibleFoods)

[<Fact>]
let ``update Tick accumulates pending before movement`` () =
    let game =
        { Game.initialState () with
            Direction = Direction.Right }

    let model =
        { initModel with
            Game = game
            Phase = GamePhase.Running
            ScoresLoading = false
            PendingMoveMs = 0 }

    let updated, cmd = Update.update (Tick tickDelta) model

    Assert.Equal<Point list>(game.Eel, updated.Game.Eel)
    Assert.Equal(model.PendingMoveMs + 40, updated.PendingMoveMs)
    Assert.True(cmdIsEmpty cmd)

[<Fact>]
let ``update Tick catches up when pending spans multiple moves`` () =
    let head = { X = 10; Y = 10 }

    let game =
        { Game.initialState () with
            Eel = [ head ]
            Direction = Direction.Right
            GameOver = false }

    let model =
        { initModel with
            Game = game
            Phase = GamePhase.Running
            ScoresLoading = false
            PendingMoveMs = initModel.SpeedMs * 2 }

    let updated, _ = Update.update (Tick tickDelta) model
    let movedHead = updated.Game.Eel |> List.head

    Assert.Equal(head.X + 2, movedHead.X)
    Assert.True(updated.PendingMoveMs < updated.SpeedMs)

[<Fact>]
let ``ChangeDirection queues when mid step`` () =
    let model =
        { initModel with
            Game = { Game.initialState () with Direction = Direction.Right }
            Phase = GamePhase.Running
            ScoresLoading = false
            PendingMoveMs = 20 }

    let updated, _ = Update.update (ChangeDirection Direction.Up) model

    Assert.Equal(Direction.Up, updated.Game.Direction)
    Assert.Equal<Direction list>([ Direction.Up ], updated.DirectionQueue)

[<Fact>]
let ``queued direction applies after full step`` () =
    let model =
        { initModel with
            Game = { Game.initialState () with Direction = Direction.Right }
            Phase = GamePhase.Running
            ScoresLoading = false
            PendingMoveMs = 10 }

    let queued, _ = Update.update (ChangeDirection Direction.Up) model
    let ready = { queued with PendingMoveMs = queued.SpeedMs }

    let updated, _ = Update.update (Tick tickDelta) ready

    Assert.Equal(Direction.Up, updated.Game.Direction)
    Assert.Equal<Direction list>([], updated.DirectionQueue)

[<Fact>]
let ``ChangeDirection applies immediately when aligned`` () =
    let model =
        { initModel with
            Game = { Game.initialState () with Direction = Direction.Right }
            Phase = GamePhase.Running
            ScoresLoading = false
            PendingMoveMs = 0 }

    let updated, _ = Update.update (ChangeDirection Direction.Up) model

    Assert.Equal(Direction.Up, updated.Game.Direction)
    Assert.Equal<Direction list>([ Direction.Up ], updated.DirectionQueue)

[<Fact>]
let ``ChangeDirection queues while mid step`` () =
    let midModel =
        { initModel with
            Game = { Game.initialState () with Direction = Direction.Right }
            Phase = GamePhase.Running
            ScoresLoading = false
            PendingMoveMs = initModel.SpeedMs / 2 }

    let updated, _ = Update.update (ChangeDirection Direction.Up) midModel

    Assert.Equal(Direction.Up, updated.Game.Direction)
    Assert.Equal<Direction list>([ Direction.Up ], updated.DirectionQueue)

[<Fact>]
let ``multiple inputs buffer sequential turns`` () =
    let baseModel =
        { initModel with
            Game = { Game.initialState () with Direction = Direction.Right }
            Phase = GamePhase.Running
            ScoresLoading = false
            PendingMoveMs = initModel.SpeedMs / 2 }

    let first, _ = Update.update (ChangeDirection Direction.Up) baseModel
    let second, _ = Update.update (ChangeDirection Direction.Left) first

    Assert.Equal(Direction.Up, second.Game.Direction)
    Assert.Equal<Direction list>([ Direction.Up; Direction.Left ], second.DirectionQueue)

    let ready = { second with PendingMoveMs = second.SpeedMs }
    let afterFirstTurn, _ = Update.update (Tick tickDelta) ready
    Assert.Equal(Direction.Up, afterFirstTurn.Game.Direction)
    Assert.Equal<Direction list>([ Direction.Left ], afterFirstTurn.DirectionQueue)

[<Fact>]
let ``TogglePause switches between running and paused`` () =
    let running =
        { initModel with
            Phase = GamePhase.Running
            ScoresLoading = false
            PendingMoveMs = 10 }

    let paused, _ = Update.update TogglePause running
    Assert.Equal(GamePhase.Paused, paused.Phase)
    Assert.Equal(0, paused.PendingMoveMs)
    Assert.Equal<Direction list>([], paused.DirectionQueue)

    let resumed, cmd = Update.update TogglePause paused
    Assert.Equal(GamePhase.Running, resumed.Phase)
    Assert.Equal(0, resumed.PendingMoveMs)
    Assert.Equal<Direction list>([], resumed.DirectionQueue)
    Assert.True(cmdIsEmpty cmd)

[<Fact>]
let ``Restart clears pending state and direction queue`` () =
    let head = { X = 5; Y = 5 }
    let model =
        { initModel with
            Phase = GamePhase.Running
            ScoresLoading = false
            PendingMoveMs = 80
            DirectionQueue = [ Direction.Up; Direction.Left ]
            Game =
                { Game.initialState () with
                    Eel = [ head ]
                    Direction = Direction.Right } }

    let reset, cmd = Update.update Restart model
    Assert.Equal(GamePhase.Countdown, reset.Phase)
    Assert.Equal(0, reset.PendingMoveMs)
    Assert.Equal<Direction list>([], reset.DirectionQueue)
    Assert.True(cmdHasEffects cmd)


[<Fact>]
let ``applyTick marks game over and requests loop stop`` () =
    let game =
        { Game.initialState () with
            Score = 30
            GameOver = true }

    let model =
        { initModel with
            Game = game
            Phase = GamePhase.Running
            HighScore = Some { Name = "A"; Score = 25 }
            ScoresLoading = false }

    let result = Loop.applyTick model

    Assert.False(ModelState.isRunning result.Model.Phase)
    Assert.Contains(Loop.GameOver, result.Events)
    Assert.Contains(Loop.StopLoop, result.Effects)

[<Fact>]
let ``applyTick completes phrase and prepares next round`` () =
    let head = { X = 2; Y = 2 }
    let collectedPos = { X = head.X - 1; Y = head.Y }
    let activePos = { X = head.X + 1; Y = head.Y }

    let modelGame =
        { Game.initialState () with
            Eel = [ head ]
            Direction = Direction.Right
            Foods =
                [ foodToken collectedPos 0 FoodStatus.Collected
                  foodToken activePos 1 FoodStatus.Active ]
            Score = 10
            GameOver = false }

    let model =
        { initModel with
            Game = modelGame
            Phase = GamePhase.Running
            TargetText = "hi"
            TargetIndex = 1
            SpeedMs = 200
            PhraseQueue = [ "example phrase" ]
            ScoresLoading = false }

    let result = Loop.applyTick model

    Assert.False(ModelState.isRunning result.Model.Phase)
    Assert.Equal("hi", result.Model.TargetText)
    Assert.Equal(0, result.Model.TargetIndex)
    Assert.Equal(Config.gameplay.LevelCountdownMs, result.Model.CountdownMs)
    Assert.Equal<string list>([ "example phrase" ], result.Model.PhraseQueue)
    Assert.Contains(Loop.CleanupLoop, result.Effects)
    Assert.DoesNotContain(Loop.FetchVocabulary, result.Effects)
    Assert.Contains(Loop.ScheduleCountdown, result.Effects)
    Assert.Contains(Loop.StopLoop, result.Effects)
    Assert.Empty(result.Model.Game.Foods)

    let speed =
        match result.Events with
        | [ Loop.PhraseCompleted value ] -> value
        | _ -> failwith "Expected phrase completed event"

    Assert.Equal(result.Model.SpeedMs, speed)

[<Fact>]
let ``applyTick emits high score persistence when score improves`` () =
    let head = { X = 1; Y = 1 }

    let activePos = { X = head.X + 1; Y = head.Y }

    let modelGame =
        { Game.initialState () with
            Eel = [ head ]
            Direction = Direction.Right
            Foods = [ foodToken activePos 0 FoodStatus.Active ]
            Score = 10
            GameOver = false }

    let model =
        { initModel with
            Game = modelGame
            Phase = GamePhase.Running
            PlayerName = "  Finn  "
            HighScore = Some { Name = "A"; Score = 5 }
            ScoresLoading = false }

    let result = Loop.applyTick model

    let expected = { Name = "A"; Score = 20 }

    match result.Effects |> List.tryFind (function | Loop.PersistHighScore _ -> true | _ -> false) with
    | Some (Loop.PersistHighScore (Some hs)) ->
        Assert.Equal(expected.Score, hs.Score)
    | _ -> failwith "Expected PersistHighScore effect"

[<Fact>]
let ``ensureFoodsForModel spawns multiple upcoming tokens`` () =
    let head = { X = 3; Y = 3 }
    let game =
        { Game.initialState () with
            Eel = [ head ]
            Direction = Direction.Right
            Foods = []
            Score = 0
            GameOver = false }

    let model =
        { initModel with
            Game = game
            TargetText = "abcd"
            PhraseQueue = []
            TargetIndex = 1 }
        |> Loop.ensureFoodsForModel

    let expectedIndexes =
        [ model.TargetIndex .. min (model.TargetText.Length - 1) (model.TargetIndex + Loop.maxVisibleFoods - 1) ]

    for idx in expectedIndexes do
        Assert.True(model.Game.Foods |> List.exists (fun token -> token.LetterIndex = idx && token.Status = FoodStatus.Active),
                    $"Missing active food for letter index {idx}")

[<Fact>]
let ``StartGame hides splash and queues countdown`` () =
    let model =
        { initModel with
            Phase = GamePhase.Splash
            ScoresLoading = false
            CountdownMs = Config.gameplay.StartCountdownMs
            CelebrationVisible = true }

    let updated, cmd = Update.update StartGame model

    Assert.Equal(GamePhase.Countdown, updated.Phase)
    Assert.Equal(Config.gameplay.StartCountdownMs, updated.CountdownMs)
    Assert.False(updated.CelebrationVisible)
    Assert.True(cmdHasEffects cmd)

[<Fact>]
let ``StartGame ignored when splash already hidden`` () =
    let model =
        { initModel with
            Phase = GamePhase.Running
            ScoresLoading = false
            CountdownMs = 2000 }

    let updated, cmd = Update.update StartGame model
    Assert.Equal(model, updated)
    Assert.True(cmdIsEmpty cmd)

[<Fact>]
let ``StartGame ignored while scores still loading`` () =
    let model =
        { initModel with
            Phase = GamePhase.Splash
            ScoresLoading = true
            CountdownMs = Config.gameplay.StartCountdownMs }

    let updated, cmd = Update.update StartGame model
    Assert.Equal(model, updated)
    Assert.True(cmdIsEmpty cmd)

[<Fact>]
let ``CelebrationDelayElapsed reveals overlay only during countdown`` () =
    let model =
        { initModel with
            Phase = GamePhase.Countdown
            ScoresLoading = false
            LastCompletedPhrase = Some "Demo"
            CelebrationVisible = false }

    let updated, cmd = Update.update CelebrationDelayElapsed model
    Assert.True(updated.CelebrationVisible)
    Assert.True(cmdIsEmpty cmd)

[<Fact>]
let ``CelebrationDelayElapsed stops pending timer when inactive`` () =
    let model =
        { initModel with
            Phase = GamePhase.Running
            ScoresLoading = false
            LastCompletedPhrase = Some "Demo"
            CelebrationVisible = false }

    let updated, cmd = Update.update CelebrationDelayElapsed model
    Assert.Equal(model, updated)
    Assert.True(cmdIsEmpty cmd)

[<Fact>]
let ``buildSegmentInfos uses last eel during countdown`` () =
    let lastPath = [ { X = 4; Y = 7 }; { X = 4; Y = 8 }; { X = 4; Y = 9 } ]
    let model =
        { initModel with
            Phase = GamePhase.Countdown
            ScoresLoading = false
            LastEel = lastPath
            Game =
                { initModel.Game with
                    Eel = [ { X = 1; Y = 1 } ]
                    Direction = Direction.Right } }

    let infos = RenderingShared.buildSegmentInfos model
    Assert.True(infos.Length >= lastPath.Length)
    let head = infos.[0]
    let expectedX =
        RenderingShared.canvasPadding + float lastPath.Head.X * RenderingShared.cellStep
    let expectedY =
        RenderingShared.canvasPadding + float lastPath.Head.Y * RenderingShared.cellStep

    Assert.Equal(expectedX, head.X, 5)
    Assert.Equal(expectedY, head.Y, 5)

[<Fact>]
let ``ScoresLoaded sorts entries and updates high score`` () =
    let entries =
        [ { Name = "Mika"; Score = 15 }
          { Name = "Ina"; Score = 42 }
          { Name = ""; Score = 30 } ]

    let model =
        { initModel with
            Phase = GamePhase.Splash
            ScoresLoading = true
            HighScore = None }

    let updated, _ = Update.update (ScoresLoaded entries) model
    Assert.False(updated.ScoresLoading)
    Assert.Equal(None, updated.ScoresError)
    Assert.Equal("Ina", updated.Scores.Head.Name)
    Assert.Equal(42, updated.Scores.Head.Score)
    Assert.Equal(Some updated.Scores.Head, updated.HighScore)

[<Fact>]
let ``ScoresFailed records error and stops loading`` () =
    let model =
        { initModel with
            ScoresLoading = true
            ScoresError = None }

    let updated, _ = Update.update (ScoresFailed "boom") model
    Assert.False(updated.ScoresLoading)
    Assert.Equal(Some "boom", updated.ScoresError)

[<Fact>]
let ``initModel speed honors config`` () =
    Assert.Equal(Config.gameplay.InitialSpeedMs, initModel.SpeedMs)

[<Fact>]
let ``ensureFoodsForModel seeds up to configured maximum`` () =
    let target = "ABCDE"

    let seeded =
        { initModel with
            TargetText = target
            PhraseQueue = []
            TargetIndex = 0
            Phase = GamePhase.Running
            ScoresLoading = false
            Game = Game.initialState () }
        |> Loop.ensureFoodsForModel

    let active =
        seeded.Game.Foods
        |> List.filter (fun token -> token.Status = FoodStatus.Active)
        |> List.sortBy (fun token -> token.LetterIndex)

    let expectedCount = min Config.gameplay.MaxVisibleFoods target.Length
    Assert.Equal(expectedCount, active.Length)
    Assert.Equal< int list >([0 .. expectedCount - 1], active |> List.map (fun token -> token.LetterIndex))

[<Fact>]
let ``boardDimensions matches calculated size`` () =
    let expectedWidth =
        (float Game.boardWidth * RenderingShared.cellStep)
        - RenderingShared.cellGap
        + (2.0 * RenderingShared.canvasPadding)

    let expectedHeight =
        (float Game.boardHeight * RenderingShared.cellStep)
        - RenderingShared.cellGap
        + (2.0 * RenderingShared.canvasPadding)

    Assert.Equal(expectedWidth, RenderingShared.boardDimensions.PixelWidth, 5)
    Assert.Equal(expectedHeight, RenderingShared.boardDimensions.PixelHeight, 5)

[<Fact>]
let ``mini overlay scale defaults to ten percent`` () =
    Assert.Equal(0.1, Config.gameplay.MiniOverlayScale, 5)

[<Fact>]
let ``WindowResized updates viewport dimensions`` () =
    let width, height = 800.0, 600.0
    let updated, _ = Update.update (WindowResized(width, height)) initModel
    Assert.Equal(width, updated.ViewportWidth, 5)
    Assert.Equal(height, updated.ViewportHeight, 5)

[<Fact>]
let ``WindowResized falls back for invalid values`` () =
    let updated, _ = Update.update (WindowResized(-10.0, Double.NaN)) initModel
    Assert.Equal(defaultViewportWidth, updated.ViewportWidth, 5)
    Assert.Equal(defaultViewportHeight, updated.ViewportHeight, 5)

[<Fact>]
let ``WindowResized ignores insignificant changes`` () =
    let model = { initModel with ViewportWidth = 900.0; ViewportHeight = 600.0 }
    let updated, cmd = Update.update (WindowResized(900.1, 600.1)) model
    Assert.True(Object.ReferenceEquals(model, updated))
    Assert.True(cmdIsEmpty cmd)

[<Fact>]
let ``WindowResized updates when screen differs`` () =
    let model = { initModel with ViewportWidth = 900.0; ViewportHeight = 600.0 }
    let newWidth, newHeight = 1200.0, 800.0
    let updated, _ = Update.update (WindowResized(newWidth, newHeight)) model
    Assert.Equal(newWidth, updated.ViewportWidth, 5)
    Assert.Equal(newHeight, updated.ViewportHeight, 5)

[<Fact>]
let ``shouldHideStats true only when running on compact screen`` () =
    let runningCompact =
        { initModel with
            Phase = GamePhase.Running
            ViewportWidth = ModelState.compactScreenThreshold - 10.0 }
    Assert.True(ModelState.shouldHideStats runningCompact)

    let runningWide =
        { runningCompact with ViewportWidth = ModelState.compactScreenThreshold + 50.0 }
    Assert.False(ModelState.shouldHideStats runningWide)

    let pausedCompact =
        { runningCompact with Phase = GamePhase.Paused }
    Assert.False(ModelState.shouldHideStats pausedCompact)

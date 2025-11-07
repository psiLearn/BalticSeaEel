module Tests

open Xunit
open Elmish
open Eel.Client.Model
open Shared

module Game = Shared.Game
module Update = Eel.Client.Update
module Loop = Eel.Client.GameLoop

let private withTarget target index =
    { initModel with
        TargetText = target
        TargetIndex = index
        SplashVisible = false
        ScoresLoading = false }

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
            GameRunning = true
            HighScore = Some { Name = "A"; Score = 999 }
            SplashVisible = false
            ScoresLoading = false }
    let model = { model with PendingMoveMs = model.SpeedMs }

    let updated, cmd = Update.update Tick model

    Assert.False(updated.GameRunning)
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
            GameRunning = true
            HighScore = Some { Name = "A"; Score = 999 }
            TargetText = "ok"
            TargetIndex = 0
            SplashVisible = false
            ScoresLoading = false }
    let model = { model with PendingMoveMs = model.SpeedMs }

    let updated, cmd = Update.update Tick model

    Assert.True(updated.GameRunning)
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
            GameRunning = true
            SplashVisible = false
            ScoresLoading = false
            PendingMoveMs = 0 }

    let updated, cmd = Update.update Tick model

    Assert.Equal<Point list>(game.Eel, updated.Game.Eel)
    Assert.Equal(model.PendingMoveMs + 40, updated.PendingMoveMs)
    Assert.True(cmdIsEmpty cmd)

[<Fact>]
let ``update Tick processes multiple moves when accumulator is large`` () =
    let head = { X = 10; Y = 10 }

    let game =
        { Game.initialState () with
            Eel = [ head ]
            Direction = Direction.Right
            GameOver = false }

    let model =
        { initModel with
            Game = game
            GameRunning = true
            SplashVisible = false
            ScoresLoading = false
            PendingMoveMs = initModel.SpeedMs * 2 }

    let updated, _ = Update.update Tick model
    let movedHead = updated.Game.Eel |> List.head

    Assert.True(movedHead.X - head.X >= 2)
    Assert.True(updated.PendingMoveMs < updated.SpeedMs)

[<Fact>]
let ``ChangeDirection queues when mid step`` () =
    let model =
        { initModel with
            Game = { Game.initialState () with Direction = Direction.Right }
            GameRunning = true
            SplashVisible = false
            ScoresLoading = false
            PendingMoveMs = 20 }

    let updated, _ = Update.update (ChangeDirection Direction.Up) model

    Assert.Equal(Direction.Right, updated.Game.Direction)
    Assert.Equal(Some Direction.Up, updated.QueuedDirection)

[<Fact>]
let ``queued direction applies after full step`` () =
    let model =
        { initModel with
            Game = { Game.initialState () with Direction = Direction.Right }
            GameRunning = true
            SplashVisible = false
            ScoresLoading = false
            PendingMoveMs = 10 }

    let queued, _ = Update.update (ChangeDirection Direction.Up) model
    let ready = { queued with PendingMoveMs = queued.SpeedMs }

    let updated, _ = Update.update Tick ready

    Assert.Equal(Direction.Up, updated.Game.Direction)
    Assert.Equal(None, updated.QueuedDirection)

[<Fact>]
let ``ChangeDirection updates immediately when not mid step`` () =
    let model =
        { initModel with
            Game = { Game.initialState () with Direction = Direction.Right }
            GameRunning = true
            SplashVisible = false
            ScoresLoading = false
            PendingMoveMs = 0 }

    let updated, _ = Update.update (ChangeDirection Direction.Up) model

    Assert.Equal(Direction.Up, updated.Game.Direction)
    Assert.Equal(None, updated.QueuedDirection)


[<Fact>]
let ``applyTick marks game over and requests loop stop`` () =
    let game =
        { Game.initialState () with
            Score = 30
            GameOver = true }

    let model =
        { initModel with
            Game = game
            GameRunning = true
            HighScore = Some { Name = "A"; Score = 25 }
            SplashVisible = false
            ScoresLoading = false }

    let result = Loop.applyTick model

    Assert.False(result.Model.GameRunning)
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
            GameRunning = true
            TargetText = "hi"
            TargetIndex = 1
            SpeedMs = 200
            UseExampleNext = false
            SplashVisible = false
            ScoresLoading = false }

    let result = Loop.applyTick model

    Assert.False(result.Model.GameRunning)
    Assert.Equal("", result.Model.TargetText)
    Assert.Equal(0, result.Model.TargetIndex)
    Assert.Equal(3000, result.Model.CountdownMs)
    Assert.True(result.Model.UseExampleNext)
    Assert.Contains(Loop.CleanupLoop, result.Effects)
    Assert.Contains(Loop.FetchVocabulary, result.Effects)
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
            GameRunning = true
            PlayerName = "  Finn  "
            HighScore = Some { Name = "A"; Score = 5 }
            SplashVisible = false
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
            SplashVisible = true
            ScoresLoading = false
            CountdownMs = 5000 }

    let updated, cmd = Update.update StartGame model

    Assert.False(updated.SplashVisible)
    Assert.False(updated.GameRunning)
    Assert.Equal(5000, updated.CountdownMs)
    Assert.True(cmdHasEffects cmd)

[<Fact>]
let ``StartGame ignored when splash already hidden`` () =
    let model =
        { initModel with
            SplashVisible = false
            ScoresLoading = false
            CountdownMs = 2000 }

    let updated, cmd = Update.update StartGame model
    Assert.Equal(model, updated)
    Assert.True(cmdIsEmpty cmd)

[<Fact>]
let ``StartGame ignored while scores still loading`` () =
    let model =
        { initModel with
            SplashVisible = true
            ScoresLoading = true
            CountdownMs = 5000 }

    let updated, cmd = Update.update StartGame model
    Assert.Equal(model, updated)
    Assert.True(cmdIsEmpty cmd)

[<Fact>]
let ``ScoresLoaded sorts entries and updates high score`` () =
    let entries =
        [ { Name = "Mika"; Score = 15 }
          { Name = "Ina"; Score = 42 }
          { Name = ""; Score = 30 } ]

    let model =
        { initModel with
            SplashVisible = true
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


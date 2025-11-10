namespace Shared

open System

type Point = { X: int; Y: int }

type Direction =
    | Up
    | Down
    | Left
    | Right

type FoodStatus =
    | Active
    | Collected

type FoodToken =
    { LetterIndex: int
      Position: Point
      Status: FoodStatus }

type GameState =
    { Eel: Point list
      Direction: Direction
      Foods: FoodToken list
      Score: int
      GameOver: bool }

type HighScore = { Name: string; Score: int }

module Game =
    let boardWidth = 20
    let boardHeight = 20

    let private rng = Random()

    let private randomPoint (occupied: Point list) =
        let rec loop () =
            let candidate =
                { X = rng.Next(0, boardWidth)
                  Y = rng.Next(0, boardHeight) }

            if occupied |> List.exists ((=) candidate) then
                loop ()
            else
                candidate

        loop ()

    let initialState () =
        let start =
            [ { X = boardWidth / 2
                Y = boardHeight / 2 } ]

        { Eel = start
          Direction = Direction.Left
          Foods = []
          Score = 0
          GameOver = false }

    let private advanceHead direction head =
        match direction with
        | Direction.Up -> { head with Y = head.Y - 1 }
        | Direction.Down -> { head with Y = head.Y + 1 }
        | Direction.Left -> { head with X = head.X - 1 }
        | Direction.Right -> { head with X = head.X + 1 }

    let private collides point eel = eel |> List.exists ((=) point)

    let private occupiedPoints state =
        state.Eel @ (state.Foods |> List.map (fun token -> token.Position))

    let move state =
        if state.GameOver then
            state
        else
            let nextHead = advanceHead state.Direction (List.head state.Eel)

            let hitsWall =
                nextHead.X < 0
                || nextHead.Y < 0
                || nextHead.X >= boardWidth
                || nextHead.Y >= boardHeight

            let hitsSelf = collides nextHead state.Eel

            if hitsWall || hitsSelf then
                { state with GameOver = true }
            else
                let hitToken =
                    state.Foods
                    |> List.tryFind (fun token -> token.Position = nextHead && token.Status = FoodStatus.Active)

                let foods =
                    state.Foods
                    |> List.map (fun token ->
                        if token.Position = nextHead then
                            match token.Status with
                            | FoodStatus.Active -> { token with Status = FoodStatus.Collected }
                            | _ -> token
                        else
                            token)

                let growing = hitToken |> Option.isSome

                let eel =
                    if growing then
                        nextHead :: state.Eel
                    else
                        nextHead
                        :: (state.Eel |> List.take (state.Eel.Length - 1))

                let score =
                    if growing then
                        state.Score + 10
                    else
                        state.Score

                { state with
                    Eel = eel
                    Foods = foods
                    Score = score }

    let spawnFood letterIndex state =
        if state.Foods |> List.exists (fun token -> token.LetterIndex = letterIndex) then
            state
        else
            let occupied = occupiedPoints state
            let position = randomPoint occupied

            let token =
                { LetterIndex = letterIndex
                  Position = position
                  Status = FoodStatus.Active }

            { state with Foods = state.Foods @ [ token ] }

    let changeDirection direction state =
        let isOpposite a b =
            match a, b with
            | Direction.Up, Direction.Down
            | Direction.Down, Direction.Up
            | Direction.Left, Direction.Right
            | Direction.Right, Direction.Left -> true
            | _ -> false

        if isOpposite state.Direction direction then
            state
        else
            { state with Direction = direction }

    let restart () = initialState ()

namespace Shared

open System

type Point =
    { X: int
      Y: int }

type Direction =
    | Up
    | Down
    | Left
    | Right

type GameState =
    { Snake: Point list
      Direction: Direction
      Food: Point
      Score: int
      GameOver: bool }

type HighScore =
    { Name: string
      Score: int }

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

        { Snake = start
          Direction = Direction.Right
          Food = randomPoint start
          Score = 0
          GameOver = false }

    let private advanceHead direction head =
        match direction with
        | Direction.Up -> { head with Y = head.Y - 1 }
        | Direction.Down -> { head with Y = head.Y + 1 }
        | Direction.Left -> { head with X = head.X - 1 }
        | Direction.Right -> { head with X = head.X + 1 }

    let private collides point snake =
        snake |> List.exists ((=) point)

    let move state =
        if state.GameOver then
            state
        else
            let nextHead = advanceHead state.Direction (List.head state.Snake)

            let hitsWall =
                nextHead.X < 0
                || nextHead.Y < 0
                || nextHead.X >= boardWidth
                || nextHead.Y >= boardHeight

            let hitsSelf = collides nextHead state.Snake

            if hitsWall || hitsSelf then
                { state with GameOver = true }
            else
                let growing = nextHead = state.Food

                let snake =
                    if growing then
                        nextHead :: state.Snake
                    else
                        nextHead :: (state.Snake |> List.take (state.Snake.Length - 1))

                let food =
                    if growing then
                        randomPoint snake
                    else
                        state.Food

                let score =
                    if growing then
                        state.Score + 10
                    else
                        state.Score

                { state with
                    Snake = snake
                    Food = food
                    Score = score }

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

module Eel.Client.ViewParts.CelebrationView

open System
open Fable.React
open Fable.React.Props
open Eel.Client.Model
open Shared

module ModelState = Eel.Client.Model

let private filteredLetters (phrase: string) =
    phrase
    |> Seq.filter (fun ch -> not (Char.IsWhiteSpace ch))
    |> Seq.map string
    |> Seq.toArray

let private countdownRadius lettersLength =
    let spacing = 46.0
    (float lettersLength * spacing) / (2.0 * Math.PI)
    |> max 70.0
    |> min 220.0

let private countdownSegment idx total radius letter =
    let angle = (float idx / float total) * 2.0 * Math.PI
    let x = radius * Math.Cos angle
    let y = radius * Math.Sin angle
    let rotation = (angle * 180.0 / Math.PI) + 90.0
    let classes =
        [ "countdown-eel-segment"
          if idx = 0 then "head" else ""
          if idx = total - 1 then "tail" else "" ]
        |> List.filter (fun c -> c <> "")
        |> String.concat " "

    div [ ClassName classes
          Style [ CSSProp.Left (sprintf "calc(50%% + %.1fpx)" x)
                  CSSProp.Top (sprintf "calc(50%% + %.1fpx)" y)
                  CSSProp.Transform (sprintf "translate(-50%%, -50%%) rotate(%.1fdeg)" rotation) ] ]
        [ str letter ]

let private countdownEelCircle (phrase: string) =
    let letters = filteredLetters phrase

    if letters.Length = 0 then
        None
    else
        let radius = countdownRadius letters.Length
        let segments =
            letters
            |> Array.mapi (fun idx letter -> countdownSegment idx letters.Length radius letter)
            |> Array.toList

        Some(
            div [ ClassName "countdown-eel" ] [
                div [ ClassName "countdown-eel-track" ] segments
            ])

let countdownCelebrationOverlay model =
    match ModelState.isCelebrationActive model, ModelState.celebrationPhrase model with
    | true, Some phrase -> countdownEelCircle phrase
    | _ -> None

module Eel.Client.View

open Fable.React
open Fable.React.Props
open Elmish
open Eel.Client.Model
open Eel.Client.ViewParts.GameScreen

module ModelState = Eel.Client.Model

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
                        h2 [] [ str "Top Scores" ] :: splashScores model)
                ]
            ]
        ]
    else
        let hideStats = ModelState.shouldHideStats model
        let layoutClass =
            if hideStats then "layout layout-compact"
            else "layout"

        div [ ClassName layoutClass ] [
            boardView model
            if hideStats then
                fragment [] []
            else
                statsView model dispatch
        ]

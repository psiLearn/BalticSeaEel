module Eel.Client.ViewParts.IntermissionView

open Fable.React
open Fable.React.Props
open Eel.Client.Model

module ModelState = Eel.Client.Model

let countdownOverlay (celebrationOverlayProvider: Model -> ReactElement option) (model: Model) =
    if not (ModelState.isRunning model.Phase) then
        let seconds = max 0 ((model.Intermission.CountdownMs + 999) / 1000)
        let label = if seconds > 0 then string seconds else "GO!"
        let baseOverlay = div [ ClassName "board-overlay board-overlay--countdown" ] [ str label ]
        match celebrationOverlayProvider model with
        | Some celebration -> [ baseOverlay; celebration ]
        | None -> [ baseOverlay ]
    else
        []

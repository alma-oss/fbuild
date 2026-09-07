module App

open Fable.Core

[<Emit("document.getElementById('app').textContent = 'Fixture client';")>]
let render (): unit = jsNative

render ()

module View

open Fable.Core

[<Emit("document.getElementById($0).textContent = $1")>]
let render (elementId: string) (text: string): unit = jsNative

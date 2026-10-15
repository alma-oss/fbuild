module Fixture.Library

open Fable.Core

[<Emit("document.title = $0")>]
let setTitle (title: string): unit = jsNative

[<Emit("document.title")>]
let title (): string = jsNative

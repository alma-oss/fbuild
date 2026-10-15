module Vite

open Fable.Core

/// Public base path the app is served from, as Vite's `base` option sets it.
[<Emit("import.meta.env.BASE_URL")>]
let baseUrl: string = jsNative

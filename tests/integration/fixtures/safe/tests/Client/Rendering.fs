module Fixture.Test.Rendering

open Fable.Core
open Alma.Testing

[<Emit("document.body.appendChild(Object.assign(document.createElement('div'), { id: $0 }))")>]
let private mount (elementId: string): unit = jsNative

[<Emit("document.getElementById($0).textContent")>]
let private textOf (elementId: string): string = jsNative

let tests =
    testList "Rendering" [
        testCase "should pass the fixture assertion" <| fun _ ->
            Expect.equal 1 1 "Fixture assertion must pass"

        testCase "should replace Vite's environment constants" <| fun _ ->
            Expect.equal Vite.baseUrl "/" "Vite should substitute its default base URL"

        testCase "should render into the DOM" <| fun _ ->
            mount "app"
            View.render "app" "Fixture client"

            Expect.equal (textOf "app") "Fixture client" "The view should write into the mounted element"
    ]

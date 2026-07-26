open System
open Microsoft.AspNetCore.Builder
open Fixture.Shared

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    let app = builder.Build ()

    app.MapGet ("/", Func<string> (fun () -> { Message = "Fixture server" }.Message)) |> ignore

    app.Run ()
    0

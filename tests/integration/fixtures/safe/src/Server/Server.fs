open System
open Microsoft.AspNetCore.Builder
open Fable.Remoting.Server
open Fable.Remoting.AspNetCore
open Fixture.Shared

/// Fails on a payload that differs from the shared sample, so a client-side encoding fault surfaces
/// here even when the client would decode its own faulty encoding back into the sample.
let echoApi: IEchoApi = {
    Echo =
        fun payload -> async {
            if payload <> Payload.sample then
                failwith $"Server received %A{payload}, expected %A{Payload.sample}"

            return payload
        }
}

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    let app = builder.Build ()

    app.MapGet ("/", Func<string> (fun () -> { Message = "Fixture server" }.Message)) |> ignore

    Remoting.createApi ()
    |> Remoting.fromValue echoApi
    |> Remoting.withErrorHandler (fun error _ -> Propagate error.Message)
    |> app.UseRemoting

    app.Run ()
    0

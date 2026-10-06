module RoundTrip

open Fable.Core
open Fable.Remoting.Client
open Fixture.Shared

/// Sends the shared sample to the echo API at `baseUrl` and fails unless the response decodes back
/// into the same value.
let run (baseUrl: string) =
    async {
        let api =
            Remoting.createApi ()
            |> Remoting.withBaseUrl baseUrl
            |> Remoting.buildProxy<IEchoApi>

        let! echoed = api.Echo Payload.sample

        if echoed <> Payload.sample then
            failwith $"Client received %A{echoed}, expected %A{Payload.sample}"
    }
    |> Async.StartAsPromise

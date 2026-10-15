# Fable warnings in the library

Read this when the report counts `warning FABLE` lines. They come from Fable's first compile of the library
and reach every consumer (`NumberStyle … is ignored`, `provider argument is ignored`).

When the ignored argument matters on .NET, split the call; a pipe after `#endif` serves both branches:

```fsharp
#if FABLE_COMPILER
    Double.TryParse value
#else
    Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture)
#endif
    |> function
        | true, number -> Some number
        | _ -> None
```

With several such sites, put the split in one internal helper module returning an `option`, so call sites
stay free of `#if`. Otherwise drop the argument.

Done when `./build.sh -t Tests` prints no `warning FABLE`.

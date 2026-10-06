// Fable.Remoting.Client sends through XMLHttpRequest, which Node does not provide.
import XMLHttpRequest from "xhr2";
globalThis.XMLHttpRequest = XMLHttpRequest;

const { run } = await import("./output/RoundTrip.js");
await run(process.argv[2]);
console.log("round-trip ok");

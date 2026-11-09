# eShop device support client

This is a standalone Blazor WebAssembly app served by the Blazor Gateway, not a Blazor Server app. Keep component logic inline in `@code` blocks.

Raw diagnostic bundles, parsed events, and support notes stay in the browser. The API serves only the diagnostic catalog. Never put local file contents, filenames, notes, or device identifiers in logs, spans, or HTTP requests.

Use the generated Blazor Web Worker client for background .NET processing. The UI-thread comparison must call the same parser and correlation algorithm, without artificial delays or redundant work.

Keep expected input errors visible. Do not suppress experimental warnings, make remote CDN requests, or install additional SDKs.

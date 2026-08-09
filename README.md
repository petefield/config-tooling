# config-tooling

Copies each JSON file from `./configs` into `./root/{tenant}/{environment}` based on its `featureFlags`, then writes a `history-index.json` file with one entry for each generated file and the last five git modifications of its source config file. Before each run, the destination root folder is deleted and recreated.

## Usage

```bash
dotnet run
```

Optional arguments:

```bash
dotnet run -- <source-config-directory> <destination-root-directory>
```

An environment value of `all` is expanded into `dev`, `uat`, and `prd`.

When no source directory is supplied, the app searches upward from the current directory for a `configs` folder.

## Browser app

A standalone Blazor WebAssembly browser app lives in `src/config-browser`.

1. Generate fresh output with the console app so `src/config-tooling/root` contains the latest files.
2. Start the browser app from `src/config-browser` with `dotnet run`.

The browser app syncs `src/config-tooling/root` into its own `wwwroot/data` during build, then lets you filter by tenant and environment, browse the generated files, and open a detail page for each config.

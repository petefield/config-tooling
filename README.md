# config-tooling

Copies each JSON file from `./configs` into `./root/{tenant}/{environment}` based on its `featureFlags`.

## Usage

```bash
dotnet run
```

Optional arguments:

```bash
dotnet run -- <source-config-directory> <destination-root-directory>
```

An environment value of `all` is expanded into `dev`, `uat`, and `prd`.

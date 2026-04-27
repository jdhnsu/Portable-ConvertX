# Portable ConvertX

Windows desktop wrapper for the portable converters collected in `TestTools`.

The first version is intentionally small:

- WPF desktop UI
- `TestTools/tools.json` for local tool discovery
- `conversions.json` for conversion rules
- child-process execution with stdout/stderr logging
- cancellation support for long-running conversions

## Run

From this repository root:

```powershell
dotnet run --project .\ConvertXPortable\ConvertXPortable.csproj
```

The app searches upward from the current directory and its own executable
directory until it finds `TestTools/tools.json`.

## Add or change conversions

Edit `conversions.json`. Each rule maps an input extension set to output
extensions and a portable executable under `TestTools`.

`argumentTemplate` supports these placeholders:

- `{input}`
- `{output}`
- `{outputDir}`
- `{format}`

Arguments are split and passed through `ProcessStartInfo.ArgumentList`, so paths
with spaces and Chinese characters are handled without shell quoting.

For tools that write the converted content to stdout, set
`writeStdoutToOutput` to `true`.

## ConvertX reference

ConvertX converter sources are referenced at:

https://github.com/C4illin/ConvertX/tree/main/src/converters

The local app does not compile or copy ConvertX source code. If those files are
downloaded into `References/ConvertX/converters/`, keep the AGPL-3.0 license
requirements in mind before redistributing.

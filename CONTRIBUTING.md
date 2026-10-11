# Contributing to UML Wizard

Thanks for your interest. Bug reports, ideas and pull requests are welcome.

## Reporting a bug or asking for a feature

Open an [issue](https://github.com/joseanicetoz/umlwizard/issues/new/choose). For a bug, give the version (Help →
About), the operating system, the steps to reproduce it and, if you can, the project file or a screenshot. Report
security issues privately: see [SECURITY.md](SECURITY.md).

## Building and testing

Requires the .NET 9 SDK (or later).

```bash
dotnet build
dotnet test
```

CI builds and tests every pull request on Windows, macOS and Linux.

## Pull requests

- For anything larger than a small fix, open an issue first so we can agree on the approach.
- Keep a pull request to one change, and add or update tests for it.
- Follow the style of the surrounding code.
- **User interface texts** are in `src/UmlWizard.App/Resources/Strings.resx`; add a new text to every
  `Strings.*.resx` translation as well (in English if you cannot translate it).
- **NuGet packages:** when you add or upgrade one, add or update its entry in
  [`docs/THIRD-PARTY-NOTICES.txt`](docs/THIRD-PARTY-NOTICES.txt). A test fails when a shipped package or its version is
  missing.
- **New code generation or import languages** can be written as plug-ins: see the
  [code generator](docs/plugins/writing-a-codegeneration-plugin.md) and
  [code import](docs/plugins/writing-a-codeimport-plugin.md) guides.

## Licence

By contributing, you agree that your contribution is licensed under the [Apache License 2.0](LICENSE), the licence of
the project.

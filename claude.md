# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

The NServiceBus wizard: a Blazor WebAssembly app deployed to
https://nservicebuscommunity.github.io/NServiceBusConfigHelper/ by `.github/workflows/deploy.yml`. It is
based on the Verify wizard (https://github.com/VerifyTests/Wizard), and keeps its structure.

## Build & test

```pwsh
dotnet build src/Wizard.Tests --configuration Release
dotnet run --project src/Wizard.Tests --configuration Release --no-build
```

Filter to a single test (TUnit uses `--treenode-filter`, not `--filter`):

```pwsh
dotnet run --project src/Wizard.Tests --configuration Release --no-build -- --treenode-filter '/*/*/HomeTests/*'
```

`Integration/GeneratedSolutionTests` is `[Explicit]`. It writes generated solutions to the temp directory,
then runs `dotnet build` and `dotnet test` on them (restoring from nuget.org), and starts the learning
endpoint to see the sample order go through. Run it after changing anything in `Wizard.Core/Generation`
or `Wizard.Core/Registry`:

```pwsh
dotnet run --project src/Wizard.Tests --configuration Release --no-build -- --treenode-filter '/*/*/GeneratedSolutionTests/*'
```

It proves that every transport, persistence and feature compiles. Only the learning transport and
persistence are run; nothing connects to a real broker or database.

Building `Wizard.Tests` also publishes `Wizard.Web` into `src/Wizard.Tests/bin/<Configuration>/blazor-publish`
(the `PublishBlazorForTests` target). `PublishedWizard` serves that output from Kestrel for the Playwright
tests, with a `{*path}` fallback that mirrors GitHub Pages serving `404.html` for deep links.

The Playwright tests share one browser context, so each page from `PublishedWizard.NewPage()` gets an
in-memory localStorage of its own: the wizard restores remembered answers on load, and a shared
storage would let parallel tests leak into each other. A test about remembering between visits uses
`NewIsolatedPage()`, which has real storage in a context of its own, and closes that context when done.

## Layout

- `src/Wizard.Core`: models, registry, generators. A plain class library with no Blazor dependency, so
  generator tests stay fast. `GenerateWizardDefaults` in its csproj bakes the SDK version (from `global.json`)
  and the target framework (`GeneratedTargetFramework` in `src/Directory.Build.props`) into
  `WizardDefaults`. Generated solutions use those values.
  - `Registry/Transports.cs`, `Registry/Persistences.cs` and `Registry/Features.Data.cs` hold one
    definition each. They share `ComponentDefinition`: packages, usings, the code for `Program.cs`,
    connection strings, containers, choices and notes. Read `ComponentDefinition.cs` and
    `FeatureDefinition.cs` before adding an entry: their xml docs are the contract, and `RegistryTests`
    enforces most of it.
  - `Registry/InteractionRules.cs` holds what follows from a combination of answers: conflicts,
    requirements, and things that have to exist outside the generated code.
  - Code in the registry is a template. `Generation/Template.cs` replaces the tokens in `Plan.Tokens`
    (`{Namespace}`, `{Commands}`, a choice's `{id}` and so on).
  - Every package id an entry names needs a version in `Versions/package-versions.json`.
    `src/Wizard.VersionRefresh` rewrites that file with the newest stable versions
    (`refresh-versions.yml` runs it weekly); run it locally rather than editing versions by hand:
    `dotnet run --project src/Wizard.VersionRefresh -- src/Wizard.Core/Versions/package-versions.json summary.md`.
  - Generator snapshots use `GeneratorTests.Versions`, with every package at 1.0.0, so a refresh never
    changes them; the browser tests answer for nuget.org through `FakeNuGet`.
- `src/Wizard.Web`: the Blazor UI.
- `src/Wizard.Tests`: TUnit tests. There are bunit component tests, Playwright screen snapshots (PNG and
  HTML), and `RepoContractTests` anti-rot checks.

## Conventions

- Snapshot everything the generators produce with Verify; generators are pure functions of their inputs.
- Generated code is written in string literals, so nothing may leak the line endings of the file holding
  them. Render registry code with `Template.Render`, which normalizes to lf.
  `GeneratorTests.GeneratedTextIsClean` checks every generated file.
- A statement about how NServiceBus behaves, in a description, a note or a rule, has to be checked
  against docs.particular.net or the package's source before it is written. The wizard's output is read
  as documentation.
- The bundled fonts in `wwwroot/fonts` keep screenshots identical across OSes.
  `RepoContractTests.ShippedFontsCoverRenderedText` fails if rendered text uses a character the fonts
  don't cover.
- Lambda parameters are `_` (nested lambdas get a descriptive name). Generated code is the exception:
  it is written for its reader, with descriptive names.
- No single-character locals other than loop counters.
- No abbreviations in identifiers.
- Never return a ternary; use an `if` that returns, then the fallback `return`.
- No `else` after a branch that returns.

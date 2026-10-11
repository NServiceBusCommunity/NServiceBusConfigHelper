# NServiceBus Wizard

A wizard for configuring an [NServiceBus](https://docs.particular.net/nservicebus/) endpoint, hosted at
https://nservicebuscommunity.github.io/NServiceBusConfigHelper/.

For a new endpoint it asks for the hosting, transport, persistence, serializer and test framework, then
for any features: sagas, the outbox, auditing and monitoring, OpenTelemetry, and the
[NServiceBus Community](https://github.com/NServiceBusCommunity/Home) extensions. It then produces:

 * a guide rendered in the page,
 * a downloadable zipped solution that builds and runs,
 * markdown instructions for an AI coding assistant.

A second entry point adds features to an existing endpoint, and produces only what has to be merged in.

The site is a Blazor WebAssembly app that runs entirely in the browser; nothing is sent to a server.
It is based on the [Verify wizard](https://github.com/VerifyTests/Wizard).


## Running locally

```
dotnet run --project src/Wizard.Web
```

Then browse to http://localhost:5179.


## Tests

```
dotnet build src/Wizard.Tests --configuration Release
dotnet run --project src/Wizard.Tests --configuration Release --no-build
```

The test project publishes the Blazor app after it builds. The Playwright tests serve that published
output and drive it in Chromium.

One explicit suite needs network, and is run by a workflow rather than on every build. It writes
generated solutions to the temp directory, builds them, runs their tests, and starts the one endpoint
that needs nothing installed:

```
dotnet run --project src/Wizard.Tests --configuration Release --no-build -- --treenode-filter "/*/*/GeneratedSolutionTests/*"
```

What it does not do is run an endpoint against a real broker or database: every transport and
persistence other than the learning ones is proven to compile, not to connect.


## Linking to the wizard

Every page's address holds all the answers given so far, so any point in the wizard can be bookmarked or
shared. Three entry points are stable:

 * https://nservicebuscommunity.github.io/NServiceBusConfigHelper/new starts a new endpoint.
 * https://nservicebuscommunity.github.io/NServiceBusConfigHelper/add adds features to an existing one.
 * `https://nservicebuscommunity.github.io/NServiceBusConfigHelper/add/{FeatureId}` adds one feature,
   already selected. An extension's readme can link here, for example
   https://nservicebuscommunity.github.io/NServiceBusConfigHelper/add/Serilog.

The browser remembers the transport and the persistence of the endpoint features were last added to.
The home page has a button that forgets them.


## Adding a feature

 1. Add a `FeatureDefinition` to `src/Wizard.Core/Registry/Features.Data.cs`. The xml docs in
    `FeatureDefinition.cs` and `ComponentDefinition.cs` describe each member, and `RegistryTests` checks
    most of them. Its position in the list is its position in the generated `Program.cs`.
 2. Add each of its packages to `src/Wizard.Core/Versions/package-versions.json` with any version, then
    run the refresh tool, which moves every package to its newest stable version:
    `dotnet run --project src/Wizard.VersionRefresh -- src/Wizard.Core/Versions/package-versions.json summary.md`.
 3. If it cannot be combined with another feature, or needs something outside the generated code, add a
    rule to `src/Wizard.Core/Registry/InteractionRules.cs`.
 4. Run the tests, accept the new snapshots, then run the generated solution tests to prove its code
    compiles.

A transport, a persistence or a serializer is added the same way, as an enum member in
`src/Wizard.Core/Model/Enums.cs` and an entry in `Registry/Transports.cs` or `Registry/Persistences.cs`.


## Workflows

 * `deploy.yml` runs the tests and deploys the site on every push to main.
 * `integration.yml` builds a generated solution per transport, persistence, serializer and feature,
   nightly and on pull requests that change the generators.
 * `refresh-versions.yml` moves every package to its newest stable version each Monday, and opens a pull
   request saying whether the generated solutions build with them.

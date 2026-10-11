# Bundled webfonts

The wizard serves its own fonts rather than naming system families like `Segoe UI` or `Consolas`.
Chromium shapes text with HarfBuzz on every platform, so a font shipped with the app measures
identically on Windows, Linux and macOS. A system font does not: fontconfig on the CI Linux image
resolves the stack to different faces with different advance widths, prose re-wraps, and the height
of a full-page screenshot moves — which is what `ScreenSnapshotTests` compares. Pinning the faces is
what lets those PNG baselines hold on any OS.

The files are the subsets built for the SponsorCheck wizard (see `contributing.md` in
[SimonCropp/SponsorCheck](https://github.com/SimonCropp/SponsorCheck) for how they are produced with
`fonttools`), served here under the family names `Wizard Sans` and `Wizard Mono`. Coverage is declared
by the `unicode-range` descriptors on the `@font-face` rules in `css/app.css`, and
`RepoContractTests.ShippedFontsCoverRenderedText` fails if the wizard renders a character those ranges
do not cover.

`css/app.css` also pins how the faces are measured (`text-rendering: geometricPrecision`, an explicit
`font-family` and `line-height: 1` on `code, kbd, samp`, and `font-family: inherit` on form controls);
the comments there explain why each rule is needed for the Linux render to match the Windows baselines.

| File | Upstream | Version | Licence |
| ---- | -------- | ------- | ------- |
| `open-sans.woff2` | [Open Sans](https://github.com/googlefonts/opensans) | 3.003 | SIL Open Font License 1.1 |
| `work-sans-arrows.woff2` | [Work Sans](https://github.com/weiweihuanghuang/Work-Sans) | 2.009 | SIL Open Font License 1.1 |
| `ubuntu-mono.woff2` | Ubuntu Mono, Canonical Ltd. | 0.862 | Ubuntu Font Licence 1.0 |

## Copyright notices

Reproduced from each font's own `name` table, which remains intact in the shipped subsets:

- Open Sans — `Copyright 2020 The Open Sans Project Authors (https://github.com/googlefonts/opensans)`
- Work Sans — `Copyright 2019 The Work Sans Project Authors (https://github.com/weiweihuanghuang/Work-Sans)`
- Ubuntu Mono — `Copyright 2011, 2022 Canonical Ltd. Licensed under the Ubuntu Font Licence 1.0`

Open Sans and Work Sans are licensed under the SIL Open Font License, Version 1.1
(<https://scripts.sil.org/OFL>); neither declares a Reserved Font Name. The Ubuntu Font Licence 1.0 is
at <https://ubuntu.com/legal/font-licence>.

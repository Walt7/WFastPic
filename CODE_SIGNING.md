# Code signing policy

[Italiano più sotto](#italiano)

> **Status:** an application to the [SignPath Foundation](https://signpath.org) for free code signing has
> been submitted and is pending. **Current releases are not signed yet**, so Windows SmartScreen may warn
> on first start. Once approved, signed releases will state:
> *Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
> [SignPath Foundation](https://signpath.org).*

## What is signed

- Only binaries built from this repository's source code: the WFastPic executable and libraries
  (`WFastPic.*`) and the installer `WFastPic-<version>-win-x64-setup.exe`.
- Third-party components shipped with the app (.NET runtime, Windows App SDK, ONNX Runtime, SQLite,
  WebView2 loader) are included as published by their maintainers and are **not** signed by this project.

## How releases are built and signed

- Every release is built from a tagged commit by the [`Release`](.github/workflows/release.yml) GitHub
  Actions workflow (tests, publish, start-up check, zip and installer). Binaries built on a developer's
  machine are never signed.
- Each signing request is approved manually by an approver (see below).
- Product name and version in the signed files are always `WFastPic` and the release version.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers (changes from non-committers are reviewed before merging) | [Walt7](https://github.com/Walt7) |
| Approvers (approve each signing request) | [Walt7](https://github.com/Walt7) |

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy

WFastPic works on the user's PC: no accounts, no telemetry, and photos, faces, people's names and file
names never leave the PC. The few network requests (place names from OpenStreetMap, map tiles, people
recognition models, new version check, error reports sent by the user) are listed, with how to turn them
off, in the [privacy policy](PRIVACY.en.md), including the privacy policies of the third-party services
involved.

---

<a name="italiano"></a>
## Italiano

**Stato:** è stata inviata alla SignPath Foundation la richiesta di firma gratuita del codice ed è in
attesa: **le release attuali non sono ancora firmate**, quindi al primo avvio Windows può mostrare un
avviso. Dopo l'approvazione le release firmate riporteranno *"Free code signing provided by SignPath.io,
certificate by SignPath Foundation"*.

- Si firmano solo i file costruiti da questo repository (`WFastPic.*` e il setup). Le librerie di terzi
  (.NET, Windows App SDK, ONNX Runtime, SQLite, WebView2) sono incluse così come le pubblicano i loro autori.
- Ogni release viene costruita dal workflow GitHub Actions [`Release`](.github/workflows/release.yml) a
  partire da un tag; mai file compilati sul PC di chi sviluppa. Ogni firma è approvata a mano.
- Ruoli: autore, revisore e approvatore è [Walt7](https://github.com/Walt7), con autenticazione a due
  fattori su GitHub e SignPath.
- Privacy: cosa esce dal PC e come spegnerlo è nella [nota sulla privacy](PRIVACY.md).

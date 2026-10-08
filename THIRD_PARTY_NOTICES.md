# Third-Party Notices

## Codex Auto Resume

- Project: https://github.com/progressrdx/codex-auto-resume
- Source reviewed: main branch, 2026-10-06 (`codex_resume/app.py`, `policy.py`, `tasks.py`)
- License: MIT, Copyright (c) 2026 progressrdx
- Adaptation: framed Desktop follower transport and conservative usage-limit policy
  ported to Windows C#/.NET; protocol profile independently checked against the
  installed Windows Codex 26.930.6422.0 and 26.930.7945.0. Later packages
  are checked against static protocol declarations and settings inheritance
  before use. No upstream executable is installed.
- Complete permission and warranty notice: `docs/licenses/codex-auto-resume-MIT.txt`.

Other implementations compared: Autumnpoem/codex-autocontinue and
ravhello/claude-codex-queue. Their executables and account synchronization code
are not included. This remains an unofficial compatibility integration.

## Codex Usage Overlay

This repository is a modified work based on Codex Usage Overlay v1.3.6:

- Project: https://github.com/ivan51769/CodexUsageOverlay
- Base revision: `e8615da7293b1523c3a9dd7f398a99e1c8c6ab85`
- MSIX updater backend revision: `26dfa7f94cc784bd8c724ddbcbde511c8bcf0e0f`
- Original license: GNU Affero General Public License, version 3

See `NOTICE.md` for the modifications and attribution details.

## Codex Runway

The Tibo reset-feed schema validation and local-day reset evaluation behavior in
`ResetRadarService.cs` were adapted for Windows from Codex Runway:

- Project: https://github.com/Licoy/codex-runway
- Upstream revision reviewed: `7b7e33cec91091d68a10a3fea8ce78e0e3c899d7`
- Original license: GNU Affero General Public License, version 3
- Windows adaptation and modifications: 2026-08-10

The Windows UI, Codex `app-server` integration, installer, and operating-system
adapters in this repository are implemented in C#/.NET Framework for Codex Usage
Overlay. This project is distributed under the GNU Affero General Public License,
version 3. See `LICENSE` for the complete terms.

Codex Runway and its public status feed are independent, non-official resources.
Their inclusion or use does not imply endorsement by OpenAI, and a public reset
announcement does not guarantee that every account receives quota at the same
time.

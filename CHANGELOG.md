# Changelog

## 1.1.3 — 2026-10-25

- Added offline Whisper Tiny, Base, Small Q5, and Underdog Bark 3.0 Clean models.
- Automated verified model downloads and CPU/GPU backend setup, with Repair and Delete.
- Added independent one-to-three-key hold and toggle shortcuts.
- Added phrase-by-phrase continuous typing alongside insert-when-finished output.
- Refreshed Home, Settings, History, About, themes, and monochrome app branding.
- Fixed History navigation crashes and fresh Bark runtime setup failures.
- Reduced unnecessary model reloads, limited worker CPU usage, and added a decoded
  Bark weight cache for faster subsequent launches.
- Extended idle model retention to 15 minutes; Keep model ready disables idle unloading.
- Made Esc dismiss the notch immediately in every state, with one-press shortcut
  reactivation and protection against stale transcription results.
- Added a smooth, microphone-driven waveform flowing left to right.
- Fixed rapid microphone handoffs, bounded recording memory, rotated logs, and
  ensured workers exit with the desktop app.
- Updated the README banner, logo, download links, and release packaging.

Validation: 96 application tests passed. Bark cache tests and real local Bark/Whisper
transcription smoke checks also passed during this release's development.

Packages: self-contained Windows x64 installer and portable ZIP. Local model weights
and optional backends download on first use. Installer is unsigned.

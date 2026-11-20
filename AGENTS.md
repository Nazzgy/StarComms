# StarComms instructions for Codex

## Objective

Implement the complete StarComms Windows voice application described in
`STARCOMMS_SPEC.md`. The final user distribution is exactly **one self-contained
`StarComms.exe`**. The user will archive and share this EXE themselves. There is no
installer and no separate server application, runtime installation or cloud
service to administer.

This directory is a specification package, not an implemented application.

## Read before implementation

1. Read `STARCOMMS_SPEC.md` completely, including the protocol, acceptance tests
   and definition of done.
2. Inspect the seven UI images and the communication diagram in `assets/`. They are visual references. The written
   specification takes precedence for exact behavior and updated labels.
3. Read `CODEX_START.md` for the requested execution and reporting workflow.

## Work autonomously within the task

- Build the actual application end to end. A UI mock, simulated microphone,
  TODO-filled skeleton or networking pseudocode is not a completed result.
- Continue across the implementation stages without asking the user to approve
  routine class names, component boundaries, colors or library API choices.
- Preserve the fixed product decisions in the specification. Resolve small
  implementation details yourself and record meaningful decisions in
  `docs/DECISIONS.md`.
- Do not introduce accounts, a web dashboard, paid hosting, telemetry, STUN,
  TURN, a public signaling server or an online IP-check service.
- Do not create an installer, MSI, MSIX, updater, Windows service or background
  autostart entry. Deliver one portable, self-contained Windows x64 EXE.
- Logging defaults to OFF. Implement a `Logs on` setting that writes log files
  beside the actual EXE only when enabled. Never silently fall back to AppData
  or the runtime extraction directory if that folder is not writable.
- Show the app version and build ID permanently in the bottom-right corner of
  every screen. Use one central version source for UI, handshake and logs.
- Do not publish, deploy or send anything to other people as part of this task.
  Follow the environment's actual permissions and approval rules.

## Engineering boundaries

- Use the selected C#/.NET/WPF architecture. Do not start a second implementation
  in Electron, a browser, Unity, Rust or a Mumble fork.
- Keep the host inside the application. Host authority applies to every voice
  frame, including the host player's own client.
- Keep UI, domain rules and transport separable. Do not put networking loops or
  codec operations on the UI thread.
- Use the platform cryptographic primitives exactly as specified. Do not replace
  certificate checking with an accept-all callback or send unencrypted audio as
  a shortcut.
- Bound queues, packet lengths and timeouts. Drop stale speech instead of
  storing it for later playback.
- Do not modify unrelated files in an existing repository. Extend an existing
  compatible solution rather than generating a duplicate one.
- Keep production code small and direct. Add interfaces where they isolate OS
  input/audio, time or networking for meaningful tests; avoid a plugin framework,
  generic event bus or enterprise scaffolding for this twelve-player app.

## Completion and evidence

- Implement every in-scope requirement and run the relevant automated tests.
- Build and publish the single EXE; verify it on Windows without an installed
  .NET runtime and without sibling DLL/JSON/resource files.
- A build command in a README is not a substitute for an actual successful build.
- Do not claim that loopback tests prove internet reachability, actual microphone
  quality, global input inside Star Citizen or all twelve home networks.
- If the available environment cannot perform a Windows or real-network check,
  complete everything possible, record that exact check as `NOT RUN` and provide
  the precise next command or manual steps. Do not report an untested build as
  fully verified, and do not ask the user to do tasks you can run yourself.
- Keep `docs/IMPLEMENTATION_STATUS.md` with requirement/test IDs, outcomes and
  evidence. Finish with the actual EXE path, its SHA-256, test summary, remaining
  environment checks and a short Czech quick-start.

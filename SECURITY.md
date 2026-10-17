# Security

Neon Rift is a single-player offline game: it has no servers, accounts, network play, telemetry upload or in-app
purchases. Saves and settings are local `PlayerPrefs`. Security problems are still possible (for example in the build
tools, the Android package, or files committed by mistake).

## Reporting

Please **do not open a public issue** for a security problem. Use GitHub's private vulnerability reporting
(*Security ▸ Report a vulnerability* on the repository page) or contact the owner,
[@soyebmohammad03-dev](https://github.com/soyebmohammad03-dev), through GitHub. Include what you found, where (file,
commit, build), and how to reproduce it. You can expect an acknowledgement within a week.

## Scope notes

- The Android APK is signed with Unity's debug keystore and is not distributed publicly.
- If you find credentials, personal data or third-party content that should not be in the repository, report it the
  same way so it can be removed.

## Supported versions

Only the latest commit on `main` is maintained.

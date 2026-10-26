# ZekPcSayac

Windows WPF application for a daily PC usage countdown.

## Firebase device synchronization

The app signs in with the Firebase **device account** (email/password), reads the configured daily allowance and bonus from `deviceConfig/zekeriyas-pc`, and synchronizes the current remaining seconds and pause state to `deviceState/zekeriyas-pc`.

- Firebase project: `zekpcsayac`
- Device document ID: `zekeriyas-pc`
- Credentials are stored encrypted for the current Windows user with Windows DPAPI in `%LOCALAPPDATA%\ZekPcSayac\firebase-login.bin`.
- The local countdown keeps working if Firebase is unavailable. Synchronization retries periodically.
- The Firebase Authentication Email/Password provider must be enabled.
- The account UID must match `deviceConfig/zekeriyas-pc.deviceAuthUid`.
- Firestore security rules must permit this device account to read its config/state and update the allowed state fields.

On first launch, enter the device account's email and password in the Firebase connection dialog. Do not use the administrator's account unless that account's UID is explicitly assigned as the device UID.

The app currently imports the remote remaining time when it starts, then writes the live remaining time, pause status, date key, last-seen timestamp and client version about every 15 seconds. Editing the remaining time from the web panel is a separate next step; this version does not yet poll for manual remaining-time overrides.

<!--
SPDX-FileCopyrightText: 2026 QSI-Banking

SPDX-License-Identifier: GPL-3.0-only
-->

# Test plan

Covers the server (this repository) and the client ([RemoteUsbEmulator-Client](https://github.com/QSI-Banking/RemoteUsbEmulator-Client)).
Run it before a release and after changes to the area a section covers.

## Setup

- **Server**: Windows x64 with SQL Server (Express is fine), the server MSI, test certificates (see the README).
  Two USB devices: one with a serial number (for example a webcam) and one without (for example a basic mouse or a
  license dongle). A third device of a different kind for the swap tests.
- **Client**: a second PC or a Hyper-V VM with test signing on, the client installer, a client certificate.
- **Users**: one test user with its secret enrolled in an authenticator app.
- The server log is in Event Viewer: Applications and Services Logs > usbipd > Operational. For debug detail, run
  `Stop-Service usbipd; usbipd server` in an elevated console instead (Ctrl+C, then `Start-Service usbipd`).
- `$usbipd` below is `usbipd.exe` in the install folder; run server commands elevated.

Each test: **steps**, then the **expected** result. A test fails if anything else happens.

## 1. Install and configuration

| # | Steps | Expected |
|---|---|---|
| 1.1 | Install the server MSI interactively; fill in the TLS and database pages | Service running; `appsettings.json` has the values; firewall rule for the port |
| 1.2 | Install the server MSI silently with all properties | Same as 1.1 |
| 1.3 | Set a wrong server certificate thumbprint in `appsettings.json`, restart the service | Service does not start; the error names the setting |
| 1.4 | Put a thumbprint in the retired `usbipd:Tls:AllowedClientThumbprints` | Service does not start; the error points to `usbipd client add` |
| 1.5 | Install the client interactively, then silently (`/VERYSILENT /SERVER= /PORT= /SERVERNAME= /SERVERPIN= /CLIENTCERT=`) | Both succeed; the settings are in the driver's Parameters key |
| 1.6 | Install the client on a PC where PowerShell scripts are blocked by policy | Install succeeds |
| 1.7 | `$usbipd db migrate` twice | Applies migrations, then reports the schema is up to date; roles exist both times |
| 1.8 | `$usbipd --help` | No `attach`, `policy` or `wsl`; no WSL wording |

## 2. TLS and client certificates

| # | Steps | Expected |
|---|---|---|
| 2.1 | Attach with an allowed client certificate | Works; server log shows the client certificate |
| 2.2 | Client server-certificate name that does not match | Client refuses before sending the user or code |
| 2.3 | Client pin that does not match | Client refuses before sending the user or code |
| 2.4 | Client certificate from the right CA but not in `usbipd client list` | TLS refused; log: not on the client certificate list |
| 2.5 | Client certificate from another CA | TLS refused; log: not issued by a configured CA |
| 2.6 | `Test-NetConnection` succeeds, but connect with plain TCP (for example `telnet`) and send nothing | Connection closed after the handshake timeout |
| 2.7 | While attached: `$usbipd client disable --thumbprint <client>` | Session ends within a few seconds; new attaches refused until `client enable` |
| 2.8 | Stop SQL Server, wait 10 seconds (the allow-list cache), then attach | TLS refused (allow-list cannot be read); log says why |

## 3. Sign-in and one-time codes

| # | Steps | Expected |
|---|---|---|
| 3.1 | Attach with the current code | Works |
| 3.2 | Detach, attach again within 30 s with the same code | "Authentication failed"; the next code works |
| 3.3 | Wrong code; unknown user | "Authentication failed" for both, with no difference between them |
| 3.4 | User added while the console output is redirected (no secret shown) | User has no secret (`user list`: OTP Not set); attach fails |
| 3.5 | Mapped user, then `$usbipd user remove`; attach | "No device is bound to user" |
| 3.6 | Stop SQL Server after TLS is up (or revoke the service's SQL rights), sign in | "Server unavailable"; service keeps running |

## 4. Sessions

| # | Steps | Expected |
|---|---|---|
| 4.1 | Attach and wait for the session limit (set 1 minute for the test) | Device detaches at the limit; log names the reason |
| 4.2 | While attached: `user remove`, then (new session) `user add --force` to another device | Session ends within a few seconds each time |
| 4.3 | While attached: `$usbipd detach --busid <busid>` | Session ends within a few seconds |
| 4.4 | While attached: `$usbipd unbind --busid <busid>` | Session ends; device back on its Windows driver |
| 4.5 | Two clients attach the same device at the same moment | Exactly one succeeds; the other gets "device busy" |
| 4.6 | Sign in, then do not import for longer than the import timeout (needs a test client that pauses after sign-in; skip if none) | Connection closed; log: import timeout |
| 4.7 | Attach and detach 3 times; also let 4.1 and 4.3 happen | No other USB device on the server resets or re-plugs |

## 5. Binds and device watching

| # | Steps | Expected |
|---|---|---|
| 5.1 | `$usbipd bind --busid <serial device>` | Device on VBoxUSB; identity printed (serial, port, interfaces, fingerprint); `list`: Shared with the real product name |
| 5.2 | Bind the device without a serial | Bound, with a warning that it is identified by its port |
| 5.3 | While attached, unplug the device | Session ends; log: bind dropped; client cannot attach |
| 5.4 | Plug it back in | `list`: Quarantined; Device Manager shows it on VBoxUSB ("(shared by usbipd)"); attach gives "cannot be attached until an administrator checks it" |
| 5.5 | `$usbipd bind` the quarantined device; or `$usbipd unbind` it | Shared again; or back on its Windows driver |
| 5.6 | Move a bound device to another port | Bind dropped as in 5.3 |
| 5.7 | `Restart-Service usbipd`; attach | `list`: Needs review; client gets "cannot be attached until an administrator checks it" |
| 5.8 | `$usbipd bind --review`, confirm | Shared; attach works |
| 5.9 | Reboot the server | Same as 5.7 and 5.8 |
| 5.10 | Stop the service; swap the serial-less device for a different device in the same port; start; `bind --review` | Refused: descriptors differ |
| 5.11 | Same with a device of the same model in the same port | Confirmed only if its descriptors are identical; record the result (known limit) |
| 5.12 | `bind --review --yes` with one matching and one missing device | The matching one confirmed, the missing one refused; non-zero exit code |

## 6. Client: no automatic reconnect

| # | Steps | Expected |
|---|---|---|
| 6.1 | While attached: `Restart-Service usbipd` on the server | Client device disappears and does not come back |
| 6.2 | While attached: disconnect the client's network for a minute, reconnect | Device does not come back |
| 6.3 | Reboot the client | No device attached after the reboot |
| 6.4 | `usbip port --stash` | Not an option |

## 7. Administration

| # | Steps | Expected |
|---|---|---|
| 7.1 | In an elevated window: `$usbipd client disable` | Windows sign-in dialog; works after an administrator signs in |
| 7.2 | Cancel the dialog; wrong password 3 times; a non-administrator account | Refused each time; nothing changed |
| 7.3 | `$usbipd list`, `user list`, `client list` | No dialog |
| 7.4 | `$usbipd admin`, several changes | One dialog; `lock` signs out; `exit` ends |
| 7.5 | In `usbipd admin`, wait 10 minutes; make a change | "The admin session was locked: no command for 10 minutes", then the dialog |
| 7.6 | `SELECT TOP 10 * FROM usbipd.AdminAudit ORDER BY Id DESC` | Every change, with the account that signed in |
| 7.7 | Not elevated: any change | "Access denied" before any dialog |

## 8. OTP enrollment

| # | Steps | Expected |
|---|---|---|
| 8.1 | `$usbipd user otp reset --name <user>`; scan the QR code | App shows "QSI USB/IP: <user>"; its codes work; old codes do not |
| 8.2 | After Enter | Secret gone from the window and the scrollback |
| 8.3 | Small window | QR code and text on separate screens, or a request to enlarge the window; never cut off |
| 8.4 | `... user otp reset --name <user> > out.txt` | Refused before anything changes; the old secret still works |

## 9. Console output

| # | Steps | Expected |
|---|---|---|
| 9.1 | Successful `bind`, `client list`, `user add` in PowerShell | No red text; `$LASTEXITCODE` is 0 |
| 9.2 | A failing command, for example `unbind --busid 99-99` | Red error; non-zero exit code |
| 9.3 | `$usbipd state \| ConvertFrom-Json` | Parses; no info or warning text mixed in |

## 10. Uninstall

| # | Steps | Expected |
|---|---|---|
| 10.1 | Uninstall the server with devices bound | Devices back on their Windows drivers |
| 10.2 | Same, with SQL Server stopped | Drivers still restored; binds remain in the database |
| 10.3 | Uninstall the client | Driver and PATH entry removed |

## 11. Devices

| # | Steps | Expected |
|---|---|---|
| 11.1 | Webcam (isochronous): attach, open the camera app on the client for 2 minutes, detach | Video works; clean detach |
| 11.2 | License dongle: bind, attach, run the software that needs it | The software finds its license |
| 11.3 | NCR Sentinel key: check the instance path, then bind | Bound; identified by port unless it has a serial |

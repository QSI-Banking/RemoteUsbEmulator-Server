<!--
SPDX-FileCopyrightText: 2026 QSI Banking

SPDX-License-Identifier: GPL-3.0-only
-->

# Test plan

Covers the server (this repository) and the client ([RemoteUsbEmulator-Client](https://github.com/QSI-Banking/RemoteUsbEmulator-Client)).
Run it before a release and after changes to the area a section covers.

## Setup

- **Server**: Windows x64 with SQL Server (Express is fine), the server MSI, test certificates (see the README).
  Two USB devices: one with a serial number (for example a webcam) and one without (for example a basic mouse or the
  NCR Sentinel key). A third device of a different kind for the swap tests.
- **Client**: a second PC or a Hyper-V VM with test signing on, the client installer, a client certificate.
  From a Hyper-V VM, the server address is `<host computer name>.mshome.net`.
- **Users**: two test users, each mapped to one of the two devices and enrolled in an authenticator app.
- **Administrators group**: a local group (for example `QSI USB Operators`) with one Windows account that is not a local
  administrator; its name is entered in the installer. A local administrator outside the group for the refusal tests.
- The server log is in Event Viewer: Applications and Services Logs > qsiusbd > Operational. For debug detail, run
  `Stop-Service qsiusbd; qsiusbd server` in an elevated console instead (Ctrl+C, then `Start-Service qsiusbd`).
- Run server commands in an elevated window.

Each test: **steps**, then the **expected** result. A test fails if anything else happens.

## 1. Install and configuration

| # | Steps | Expected |
|---|---|---|
| 1.1 | Install the server MSI interactively; fill in the TLS and database pages, including the administrators group | Service `qsiusbd` running; `appsettings.json` has the values, including `qsiusbd:Admin:Group`; firewall rule for the port |
| 1.2 | Install the server MSI silently with all properties | Same as 1.1 |
| 1.3 | `qsiusbd db migrate` twice | Applies migrations, then reports the schema is up to date; roles exist both times |
| 1.4 | Set a wrong server certificate thumbprint in `appsettings.json`, restart the service | Service does not start; the event log names the setting |
| 1.5 | Set `"Port": 0` in `appsettings.json`, restart the service | Service does not start; the event log says the port must be between 1 and 65535 |
| 1.6 | Paste the server certificate thumbprint from the Windows certificate dialog into `appsettings.json`, restart | Service starts (the dialog's invisible character is removed) |
| 1.7 | Install the client interactively, then silently (`/VERYSILENT /SERVER= /PORT= /SERVERNAME= /SERVERPIN= /CLIENTCERT=`) | Both succeed; the settings are in the driver's Parameters key; Device Manager shows "QSI USB/IP Emulated Host Controller" |
| 1.8 | Install the client on a PC where PowerShell scripts are blocked by policy | Install succeeds |

## 2. TLS, client certificates and connections

| # | Steps | Expected |
|---|---|---|
| 2.1 | Attach with an allowed client certificate | Works; server log shows the client certificate |
| 2.2 | Client server-certificate name that does not match | Client refuses before sending the user or code |
| 2.3 | Client pin that does not match | Client refuses before sending the user or code |
| 2.4 | Client certificate from the right CA but not in `qsiusbd client list` | TLS refused; log: not on the client certificate list |
| 2.5 | Client certificate from another CA | TLS refused; log: not issued by a configured CA |
| 2.6 | Connect with plain TCP (for example `telnet <server> 3241`) and send nothing | Closed after the 10 s handshake timeout |
| 2.7 | Connect with a valid client certificate and send nothing (script below) | Closed after 10 s; log: did not sign in within 10 seconds |
| 2.8 | Open 101 connections that stay idle (script below) within 10 s | The 101st is closed at once; log once: limit of 100 connections; attaches work again after the idle ones time out |
| 2.9 | While attached: `qsiusbd client disable --thumbprint <client>` | Session ends within a few seconds; new attaches refused until `client enable` |
| 2.10 | Stop SQL Server, wait 10 seconds (the allow-list cache), then attach | TLS refused (allow-list cannot be read); log says why |

For 2.7 and 2.8, in an elevated PowerShell on a client PC (uses its client certificate):

```powershell
$server = '<server name>'; $port = 3241
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object Thumbprint -eq '<client certificate thumbprint>'
function Open-Idle {
    $tcp = [Net.Sockets.TcpClient]::new($server, $port)
    $ssl = [Net.Security.SslStream]::new($tcp.GetStream(), $false, { $true })
    $ssl.AuthenticateAsClient($server, [Security.Cryptography.X509Certificates.X509CertificateCollection]::new(@($cert)), $false)
    $ssl
}
# 2.7: prints about 10 seconds
$sw = [Diagnostics.Stopwatch]::StartNew(); $s = Open-Idle; try { [void]$s.ReadByte() } catch { }; $sw.Elapsed
# 2.8: the last one is refused
$all = 1..101 | ForEach-Object { try { Open-Idle } catch { "refused: $_" } }; $all | Where-Object { $_ -is [string] }
```

## 3. Sign-in and one-time codes

| # | Steps | Expected |
|---|---|---|
| 3.1 | `qsiusb attach --user <user>` with the current code | Works |
| 3.2 | Detach, attach again within 30 s with the same code | "Authentication failed"; the next code works |
| 3.3 | Attach as a user that does not exist, then as a real user with a wrong code; time both | Both "Authentication failed" after about one second, with no difference between them |
| 3.4 | Three wrong codes for a user within a minute | Third attempt: log "3 wrong one-time passwords ... the bind is dropped"; `list` shows the device Quarantined; a correct code then gets "cannot be attached until an administrator checks it"; `bind` shares it again |
| 3.5 | Two wrong codes, wait more than a minute, one wrong code, then a correct code | Still shared; the correct code attaches |
| 3.6 | While user A is attached, three wrong codes for user A from another client | A's session ends and the bind is dropped |
| 3.7 | User added while the console output is redirected (no secret shown) | User has no secret (`user list`: OTP Not set); attach fails |
| 3.8 | Mapped user, then `qsiusbd user remove`; attach | "No device is bound to user" |
| 3.9 | Stop SQL Server after TLS is up (or revoke the service's SQL rights), sign in | "Server unavailable"; service keeps running |

## 4. Sessions

| # | Steps | Expected |
|---|---|---|
| 4.1 | Attach and wait for the session limit (set 1 minute for the test) | Device detaches at the limit; log names the reason |
| 4.2 | While attached: `user remove`, then (new session) `user add --force` to another device | Session ends within a few seconds each time |
| 4.3 | While attached: `qsiusbd detach --busid <busid>` | Session ends within a few seconds |
| 4.4 | While attached: `qsiusbd unbind --busid <busid>` | Session ends; device back on its Windows driver |
| 4.5 | Two clients attach the same device at the same moment | Exactly one succeeds; the other gets "device busy" |
| 4.6 | From one client, attach both test users | Both attach; `qsiusb port` shows two ports |
| 4.7 | Attach, detach, attach again without touching the device | Both attaches work |
| 4.8 | Sign in, then do not import for longer than the import timeout (needs a test client that pauses after sign-in; skip if none) | Connection closed; log: import timeout |
| 4.9 | Attach and detach 3 times; also let 4.1 and 4.3 happen | No other USB device on the server resets or re-plugs |

## 5. Binds and device watching

| # | Steps | Expected |
|---|---|---|
| 5.1 | `qsiusbd bind --busid <serial device>` | Device on VBoxUSB; identity printed (serial, port, plug-in time, interfaces, fingerprint); `list`: Shared with the real product name |
| 5.2 | Bind the device without a serial | Bound, with a warning that it is identified by its port |
| 5.3 | While attached, unplug the device | Session ends; log: bind dropped; client cannot attach |
| 5.4 | Plug it back in | `list`: Quarantined; Device Manager shows it on VBoxUSB ("(shared by qsiusbd)"); attach gives "cannot be attached until an administrator checks it" |
| 5.5 | `qsiusbd bind` the quarantined device; or `qsiusbd unbind` it | Shared again; or back on its Windows driver |
| 5.6 | Move a bound device to another port | Bind dropped as in 5.3 |
| 5.7 | Simulate a replug the service did not see (as happens to a device in an error state): `UPDATE usbipd.Binds SET ArrivedUtc = DATEADD(second, -30, ArrivedUtc)` for the bound device; attach | Refused: unplugged and plugged in again; bind dropped; `list` shows it Quarantined |
| 5.8 | `Restart-Service qsiusbd`; attach | `list`: Needs review; client gets "cannot be attached until an administrator checks it" |
| 5.9 | `qsiusbd bind --review`, confirm | Shared; attach works |
| 5.10 | Reboot the server | Same as 5.8 and 5.9 |
| 5.11 | Stop the service; unplug and replug the same device; start; `bind --review` | Refused: unplugged and plugged in again since it was bound |
| 5.12 | Stop the service; swap the serial-less device for a different device in the same port; start; `bind --review` | Refused: descriptors differ, or unplugged and plugged in again |
| 5.13 | `bind --review --yes` with one matching and one missing device | The matching one confirmed, the missing one refused; non-zero exit code |

## 6. Client

| # | Steps | Expected |
|---|---|---|
| 6.1 | While attached: `Restart-Service qsiusbd` on the server | Client device disappears and does not come back |
| 6.2 | While attached: disconnect the client's network for a minute, reconnect | Device does not come back |
| 6.3 | Reboot the client | No device attached after the reboot |
| 6.4 | On the server run a listener that accepts and stays silent (`$l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Any, 3299); $l.Start(); $c = $l.AcceptTcpClient()`), point a client at port 3299, attach | The attach fails after about 30 s with a time-out |
| 6.5 | Attach normally, leave the device idle for 2 minutes, use it | Still attached and working |

## 7. Administration

| # | Steps | Expected |
|---|---|---|
| 7.1 | In an elevated window: `qsiusbd client disable`; sign in as a member of the administrators group who is not a local administrator | Windows sign-in dialog names the group; the change works |
| 7.2 | Cancel the dialog; wrong password 3 times; a local administrator who is not in the group | Refused each time ("is not a member of"); nothing changed |
| 7.3 | `qsiusbd list`, `user list`, `client list` | No dialog |
| 7.4 | `qsiusbd admin`, several changes | One dialog; `lock` signs out; `exit` ends |
| 7.5 | In `qsiusbd admin`: `client add --certificate "C:\path with spaces\pc.cer" --name "Lab PC"` | Works; quotes group the words, backslashes are kept |
| 7.6 | In `qsiusbd admin`, wait 10 minutes; make a change | "The admin session was locked: no command for 10 minutes", then the dialog |
| 7.7 | `SELECT TOP 10 * FROM usbipd.AdminAudit ORDER BY Id DESC` | Every change, with the account that signed in |
| 7.8 | Not elevated: any change | "Access denied" before any dialog |
| 7.9 | `qsiusbd client disable --thumbprint <thumbprint pasted from the certificate dialog>` | Finds the certificate |
| 7.10 | Empty `qsiusbd:Admin:Group` in `appsettings.json`; then a group name that does not exist; make a change | Refused before any dialog, with a message naming the setting or the group |
| 7.11 | `bind --busid <busid>` in an elevated window, signed in as the group member from 7.1 | Device bound; the audit names the group member |

## 8. OTP enrollment and secrets

| # | Steps | Expected |
|---|---|---|
| 8.1 | `qsiusbd user otp reset --name <user>`; scan the QR code | App shows "QSI USB/IP: <user>"; its codes work; old codes do not |
| 8.2 | After Enter | Secret gone from the window and the scrollback |
| 8.3 | Small window | QR code and text on separate screens, or a request to enlarge the window; never cut off |
| 8.4 | `qsiusbd user otp reset --name <user> > out.txt` | Refused before anything changes; the old secret still works |
| 8.5 | `SELECT ProtectedSecret, CertificateThumbprint FROM usbipd.OtpSecrets` | Values start with `enc:`; thumbprint of the configured certificate |
| 8.6 | Copy one user's `ProtectedSecret` into another user's row; sign in as that user | Refused; log: the secret cannot be decrypted |
| 8.7 | Install a second RSA certificate, set `qsiusbd:Otp:CertificateThumbprint` to it, restart; `user otp status` | All secrets on the new certificate; the old one "unused (can be removed)"; codes keep working |
| 8.8 | Set the thumbprint to a certificate the service has no private key for, restart, sign in | Sign-in fails; log says the private key is not usable |

## 9. Console output

| # | Steps | Expected |
|---|---|---|
| 9.1 | Successful `bind`, `client list`, `user add` in PowerShell | No red text; `$LASTEXITCODE` is 0 |
| 9.2 | A failing command, for example `unbind --busid 99-99` | Red error; non-zero exit code |
| 9.3 | `qsiusbd state \| ConvertFrom-Json` | Parses; no info or warning text mixed in |

## 10. Uninstall

| # | Steps | Expected |
|---|---|---|
| 10.1 | Uninstall the server with devices bound | Devices back on their Windows drivers |
| 10.2 | Same, with SQL Server stopped | Drivers still restored; binds remain in the database |
| 10.3 | Uninstall the client | Drivers and PATH entry removed |

## 11. Devices

| # | Steps | Expected |
|---|---|---|
| 11.1 | Connection integrity: attach the webcam, open the camera app on the client for 2 minutes, detach. A webcam streams continuously (isochronous transfers), so dropped or corrupted data shows up at once | Video is smooth for the whole 2 minutes, with no freezes or artifacts; clean detach |
| 11.2 | NCR Sentinel key: check the instance path, then bind | Bound; identified by port unless it has a serial |
| 11.3 | NCR Sentinel key: attach, enter diagnostics mode. | The machine can enter diagnostics. |

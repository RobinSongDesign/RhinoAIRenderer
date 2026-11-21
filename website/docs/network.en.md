# Cannot connect to the internet

Generating images, signing in and checking your balance all connect to `https://api.rhchameleon.top`. When the connection fails, the plug-in shows one of two messages:

| Message | Meaning |
|---|---|
| **Rhino cannot connect to the internet: a firewall or security software on this computer blocks it** | The request never left your computer; a setting on this computer is blocking Rhino. Follow the [Windows Firewall](#windows-firewall) and [Security software](#security-software) sections |
| **Could not reach the provider — check your network** | The request left but did not reach the server: no network, a proxy or company network restriction, a hosts file entry and so on. Start with [Step 1](#step-1-check-the-network-itself) |

Click "Details" next to the message to see the system's original error. If it says "An attempt was made to access a socket in a way forbidden by its access permissions" (error 10013), it is the first case.

## Step 1: check the network itself

Open <https://api.rhchameleon.top> in your browser.

- **It does not open**: the problem is the network, not Rhino. Check your connection, proxy and VPN, see [Company networks, proxies and VPNs](#company-networks-proxies-and-vpns).
- **It opens, but the plug-in cannot connect**: only Rhino is being blocked; continue with the sections below.

## Windows Firewall

Windows Firewall can block a single program from the internet. Note: **as long as one "Block" rule exists for a program, adding an "Allow" rule has no effect**; the block rule must be disabled or deleted.

### Option 1: graphical interface

1. Press `Win + R`, type `wf.msc` and press Enter to open "Windows Defender Firewall with Advanced Security".
2. Click **Outbound Rules** on the left.
3. Look for rules whose "Program" column contains `Rhino` (click the "Program" column header to sort). Rhino 8 is usually at `C:\Program Files\Rhino 8\System\Rhino.exe`.
4. For rules whose "Action" is **Block** (a red circle icon): right-click → **Disable Rule**.
5. Click **Inbound Rules** on the left and check the same way (the plug-in does not need inbound connections, but some rules apply to both directions).
6. Go back to Rhino and open the Chameleon panel again. There is no need to restart Rhino.

### Option 2: PowerShell

Search for "PowerShell" in the Start menu, right-click it and choose **Run as administrator**, then run:

List all rules that block Rhino:

```powershell
Get-NetFirewallApplicationFilter | Where-Object { $_.Program -like '*Rhino*' } |
  Get-NetFirewallRule | Where-Object { $_.Action -eq 'Block' } |
  Format-Table DisplayName, Direction, Enabled
```

Disable the outbound block rules among them:

```powershell
Get-NetFirewallApplicationFilter | Where-Object { $_.Program -like '*Rhino*' } |
  Get-NetFirewallRule | Where-Object { $_.Action -eq 'Block' -and $_.Direction -eq 'Outbound' } |
  Disable-NetFirewallRule
```

If your computer blocks all outbound traffic by default (common on company computers), also allow Rhino:

```powershell
New-NetFirewallRule -DisplayName "Rhino 8" -Direction Outbound -Action Allow `
  -Program "C:\Program Files\Rhino 8\System\Rhino.exe"
```

If Rhino is installed elsewhere, use the actual path of `Rhino.exe` (while Rhino is running, open Task Manager → Details → right-click Rhino.exe → Open file location).

## Security software

Security software such as Huorong, 360 Total Security, Tencent PC Manager, Kaspersky or ESET has its own network control, separate from Windows Firewall. Look for a feature called **network control**, **firewall** or **application network access**, find Rhino in the program list and set it to **allow internet access**.

Menu names differ between products and versions. If you cannot find the setting, quit the security software temporarily and try again: if Rhino connects then, it was the one blocking it.

## Hosts file

The hosts file can point an address to the wrong server, making connections fail or time out.

1. Search for "Notepad" in the Start menu, right-click it and choose **Run as administrator**.
2. Open `C:\Windows\System32\drivers\etc\hosts` (set the file type to "All files").
3. Look for lines containing `rhchameleon`. Put a `#` at the start of each such line or delete it, then save.
4. Open Command Prompt as administrator and run `ipconfig /flushdns`.

On macOS the hosts file is `/etc/hosts`; edit it with `sudo nano /etc/hosts` and run `sudo dscacheutil -flushcache` afterwards.

## Company networks, proxies and VPNs

- Company or school networks may restrict access to outside websites. Ask your network administrator to allow `api.rhchameleon.top` on port 443 (HTTPS).
- The plug-in uses the system proxy settings (Windows: Settings → Network & Internet → Proxy). If the browser can connect but the plug-in cannot, check whether the browser has its own proxy setting or extension.
- If a VPN or network accelerator is on, try turning it off; conversely, if your network is restricted, try turning one on.

On Windows, you can check with PowerShell whether the computer can reach the server:

```powershell
Test-NetConnection api.rhchameleon.top -Port 443
```

`TcpTestSucceeded : True` means the computer itself can connect and something is blocking Rhino specifically (firewall or security software); `False` means the whole computer cannot connect and the problem is the network, proxy or hosts file.

## macOS

The built-in macOS firewall only manages **incoming** connections and does not stop Rhino from connecting out. If you use an outbound firewall such as Little Snitch or LuLu, find Rhino 8 in its rules and allow connections to `api.rhchameleon.top`.

## Still cannot connect

Please send us:

1. A screenshot of the error in the plug-in (with "Details" open).
2. Whether your browser can open <https://api.rhchameleon.top>.
3. The result of `Test-NetConnection` above (Windows).
4. Your operating system and Rhino versions, and whether you use security software or a company network.

# Install and get started

Chameleon is an AI rendering plug-in for Rhino: capture the current viewport, describe the look you want in words, and get a photorealistic rendering that you can keep refining.

## Requirements

- Rhino 8 (Windows or macOS)
- Internet access (the plug-in connects to `api.rhchameleon.top`; if it cannot, see [Cannot connect to the internet](/docs/network))
- A Chameleon account with a balance (see [Account, top-up and balance](/docs/account))

## Install

> The plug-in is in testing and test builds are provided as a zip file. Once released, it can be installed from Rhino's Package Manager (command `PackageManager`) by searching for Chameleon; this page will be updated then.

1. If you installed the old AIRenderer: run `PluginManager` in Rhino, find AIRenderer, untick or uninstall it, then **quit Rhino completely**. The new and old versions cannot be loaded together.
2. Unzip the file into a permanent folder, for example `Documents\Chameleon\`. Do not move or delete this folder afterwards: Rhino loads the plug-in from it every time.
3. Open Rhino 8 and drag `Chameleon.rhp` from that folder into the Rhino window.
4. Run the `Chameleon` command to open the panel. (The old `AIRender` command works too.)

To update a test build, quit Rhino completely, then overwrite the folder with the files from the new zip.

## Sign in

1. In the panel, choose **Chameleon account** as the provider.
2. Click **Sign in** and enter the user name (or e-mail) and password you registered on the website; accounts with two-factor authentication also enter a code.
3. Once signed in, your account name and balance appear below the provider.

Your password is used only for this sign-in and is not stored. The plug-in creates a dedicated key named "Chameleon for Rhino (computer name)" in your account and generates with your account balance. See [Account, top-up and balance](/docs/account).

## Your first image

1. Set up the view in Rhino.
2. Click **Capture active view** in the panel.
3. Choose a model and image size (1K is the fastest and cheapest, good for trying things out).
4. Describe the look you want in the prompt, for example "dusk, warm lighting, realistic architectural photography".
5. Click **Generate image**. It usually takes 30 to 60 seconds; 4K takes longer.

Every image you generate stays in the history panel on the left, where you can compare, refine or save it.

## Common questions

**The panel keeps showing "Loading" or stays blank**: close the panel and run `Chameleon` again; if that does not help, restart Rhino.

**"Rhino cannot connect to the internet"**: see [Cannot connect to the internet](/docs/network).

**Insufficient balance**: top up or redeem on the website's wallet page, see [Account, top-up and balance](/docs/account).

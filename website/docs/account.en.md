# Account, top-up and balance

The plug-in charges per image, deducted from your Chameleon account balance. Accounts, top-ups and usage are managed on the website <https://api.rhchameleon.top>.

## Register and sign in

- Create an account on the [sign-up page](https://api.rhchameleon.top/sign-up).
- In the plug-in, choose "Chameleon account", click **Sign in** and enter your user name (or e-mail) and password.
- Accounts registered with WeChat or similar must first set a password in the website's personal settings before signing in from the plug-in.
- Forgot your password: click "Forgot password" in the sign-in box, or open the [password recovery page](https://api.rhchameleon.top/forgot-password).

> The website allows at most 20 sign-in attempts per network within 20 minutes. If you see "Too many sign-in attempts", wait 20 minutes before trying again and do not keep clicking Sign in.

## Top-up and redemption

Click **Top up** next to the balance in the plug-in, or open the website's [wallet page](https://api.rhchameleon.top/wallet).

- **Redemption codes**: paste the code into the redemption box on the wallet page and click "Redeem"; the credit is added immediately.
- Where to buy redemption codes is announced on the website.

## Balance and prices

- Once signed in, the plug-in shows your account balance below the provider; it refreshes after each successful generation, and you can click ↻ to refresh it yourself.
- Prices depend on the model and image size (1K / 2K / 4K), see the [pricing page](https://api.rhchameleon.top/pricing).
- Failed generations (including content moderation rejections and timeouts) are not charged.
- When the balance runs out, the plug-in shows "Not enough credits or balance — redeem a code or top up".

## Signing out and several computers

- Clicking **Sign out** next to the account name in the plug-in only deletes the key stored on this computer; the key stays on the website and is reused the next time you sign in.
- Each computer creates its own key when it signs in. To disable a computer for good, delete its "Chameleon for Rhino (computer name)" key under [Token management](https://api.rhchameleon.top/keys) on the website.

## Entering a key by hand (optional)

If signing in with a password is not convenient (for example, an account that only signs in by QR code), create a key under [Token management](https://api.rhchameleon.top/keys), copy it, and paste it in the plug-in under Settings → Chameleon account → API key.

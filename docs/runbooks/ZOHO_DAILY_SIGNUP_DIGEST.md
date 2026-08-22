# Zoho daily signup digest

The production application sends one email each morning for the preceding calendar day in `America/Indiana/Indianapolis` time.

- If registrations exist, the email contains two sections: that day's new registrations and the complete current registration list.
- If no registrations exist, the email is plain text stating that no new registrations were received. It does not build either report.
- Recipients are BCC'd so their addresses are not disclosed to one another.

## Create the Zoho credentials

1. Sign in to Zoho as `outreach@savefw.com` and open [Zoho API Console](https://api-console.zoho.com/).
2. Select **Get Started** (or **Add Client**), choose **Self Client**, and select **Create Now**.
3. Open the new client's **Client Secret** tab. Copy the Client ID and Client Secret into a temporary private location.
4. Open **Generate Code** and enter these exact scopes, separated by a comma:

   `ZohoMail.messages.CREATE,ZohoMail.accounts.READ`

5. Choose a short authorization-code lifetime, enter a description such as `SaveNEIN daily signup digest`, and select **Create**. If Zoho asks which account or portal to authorize, select the one containing `outreach@savefw.com`.
6. Immediately exchange the generated code. In PowerShell, fill the four values locally and run:

   ```powershell
   $body = @{
     client_id = "YOUR_CLIENT_ID"
     client_secret = "YOUR_CLIENT_SECRET"
     grant_type = "authorization_code"
     code = "YOUR_GENERATED_CODE"
   }
   Invoke-RestMethod -Method Post -Uri "https://accounts.zoho.com/oauth/v2/token" -Body $body
   ```

7. Save the returned `refresh_token`. The generated code is short-lived, the access token lasts about one hour, and the application automatically exchanges the refresh token for new access tokens.
8. Find the sending account ID using the returned access token:

   ```powershell
   $headers = @{ Authorization = "Zoho-oauthtoken YOUR_ACCESS_TOKEN" }
   Invoke-RestMethod -Method Get -Uri "https://mail.zoho.com/api/accounts" -Headers $headers
   ```

   Select the `accountId` whose email address is `outreach@savefw.com`.

If the Zoho account is hosted outside the United States, replace both `.com` endpoints with the data-center-specific endpoints shown by Zoho.

## Configure the VPS

Copy the variable names from `deploy/signup-digest.env.example` into the existing private `deploy/.env` on the VPS. Set the five recipient variables there, along with the Zoho account ID, client ID, client secret, and refresh token. Keep `DAILY_SIGNUP_DIGEST_ENABLED=false` until all values are present, then change it to `true` and recreate the app container.

The default delivery time is 8:00 AM Indiana Eastern Time. Change `DAILY_SIGNUP_DIGEST_DELIVERY_LOCAL_TIME` in the private VPS environment if a different time is preferred.

The application records each successful or failed delivery in `daily_signup_digest_deliveries`. This prevents routine restarts from resending a completed date and allows failed or missed dates to be retried.

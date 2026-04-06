# Premium Downloader Bot

ASP.NET Core web app plus Telegram bot for handling direct-file links.

## What it does

- Accepts a URL from Telegram or the website.
- Detects whether the URL points to a direct file.
- Sends direct files through Telegram when size limits allow.
- Redirects non-direct links to the website download page.

## Required environment variables

- `Downloader__TelegramBotToken`
- `Downloader__TelegramBotUsername`
- `Downloader__WebsiteBaseUrl`

Optional:

- `Downloader__MaxTelegramFileSizeBytes`
- `Downloader__MaxWebDownloadSizeBytes`

## Local run

```bash
dotnet run
```

## Deploy on Render

- Runtime: `Docker` or `.NET`
- Build command: `dotnet build`
- Start command: `dotnet run`
- Add the environment variables above in Render dashboard

## Notes

- The repository intentionally excludes local build output and development-only settings.
- Rotate any Telegram token that was ever shared in chat or committed anywhere public.

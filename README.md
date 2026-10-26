# Wallpaper Scheduler

Wallpaper Scheduler changes the wallpaper on a chosen monitor at a date and time you pick, and puts it back when the time is up. It can repeat every year. You can also set a default picture for whenever nothing is scheduled.

It runs on Windows 10 and Windows 11. Your pictures and dates stay on your PC, in `%AppData%\WallpaperScheduler`.

## Download

Download **WallpaperScheduler-1.0.0-windows.zip** from the [latest release](https://github.com/captainbikk/WallpaperScheduler/releases/latest).

1. Unzip the folder.
2. Run `WallpaperScheduler.exe`.
3. Windows may say it protected your PC, because this download is not code-signed. Choose **More info**, then **Run anyway**.

.NET Framework 4.8 is required. It is already included on current Windows 10 and Windows 11 installs.

## Use it

- **Add schedule** chooses a picture, a monitor (or every monitor), a start date and time, and how long it stays up. Durations are in minutes, hours, days, or weeks.
- Turn on **Repeat every year** for the same month and day each year. A February 29 date uses February 28 in other years.
- If two schedules cover the same monitor, the one that started later is used.
- **Default wallpaper** is the picture shown when a monitor has nothing scheduled. Clear it to go back to the wallpaper from before this program changed it.
- Closing the window leaves it in the notification area. Dates still change while it is there.
- **Start with Windows** opens it in the tray when you sign in.
- **Keep working after this program is closed** checks about once a minute, so a date can still change after you exit.
- Right-click the tray icon and choose **Exit** to quit.

Windows Spotlight and the wallpaper slideshow can override a scheduled picture. Turn those off in Settings, Personalization, Background.

## Build

Install the .NET SDK, then from this folder run:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

That builds `WallpaperScheduler.exe` and adds a shortcut on the desktop and in the Start menu.

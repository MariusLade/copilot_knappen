using CopilotKnappen.Core;
using Windows.Media.Control;

namespace CopilotKnappen.Services;

/// <summary>
/// Controls music playback through the Windows media session API (the same one the volume flyout uses),
/// so commands go to Spotify even when another app has focus. Falls back to media keys.
/// </summary>
public sealed class MediaController
{
    public async Task<string> SendAsync(MediaCommand command, bool preferSpotify, Func<bool> launchSpotify)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = preferSpotify ? FindSpotify(manager) : null;

            // "play" while Spotify is closed: start it and resume playback once its session appears.
            if (session is null && preferSpotify && command == MediaCommand.Play && launchSpotify())
            {
                for (var i = 0; i < 40 && session is null; i++)
                {
                    await Task.Delay(250);
                    session = FindSpotify(manager);
                }
                if (session is not null && await session.TryPlayAsync()) return "Playing on Spotify";
                return "Opened Spotify";
            }

            session ??= manager.GetCurrentSession();
            if (session is not null && await Send(session, command))
            {
                var player = session.SourceAppUserModelId.Contains("spotify", StringComparison.OrdinalIgnoreCase) ? "Spotify" : "media";
                return $"{Describe(command)} ({player})";
            }
        }
        catch
        {
            // Media session API unavailable; use keys below.
        }

        NativeMethods.PressKey(command switch
        {
            MediaCommand.Next => NativeMethods.VK_MEDIA_NEXT_TRACK,
            MediaCommand.Previous => NativeMethods.VK_MEDIA_PREV_TRACK,
            _ => NativeMethods.VK_MEDIA_PLAY_PAUSE,
        });
        return Describe(command);
    }

    private static GlobalSystemMediaTransportControlsSession? FindSpotify(GlobalSystemMediaTransportControlsSessionManager manager) =>
        manager.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId.Contains("spotify", StringComparison.OrdinalIgnoreCase));

    private static async Task<bool> Send(GlobalSystemMediaTransportControlsSession session, MediaCommand command) => command switch
    {
        MediaCommand.Play => await session.TryPlayAsync(),
        MediaCommand.Pause => await session.TryPauseAsync(),
        MediaCommand.Next => await session.TrySkipNextAsync(),
        MediaCommand.Previous => await session.TrySkipPreviousAsync(),
        _ => await session.TryTogglePlayPauseAsync(),
    };

    private static string Describe(MediaCommand command) => command switch
    {
        MediaCommand.Play => "Playing",
        MediaCommand.Pause => "Paused",
        MediaCommand.Next => "Skipped to next track",
        MediaCommand.Previous => "Previous track",
        _ => "Toggled play/pause",
    };
}

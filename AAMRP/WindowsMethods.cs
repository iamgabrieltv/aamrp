using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace AAMRP;

public static class WindowsMethods
{
    public static Program.SongData GetAppleMusicInfo()
    {
        try
        {
            var amProcesses = Process.GetProcessesByName("AppleMusic");
            if (amProcesses.Length == 0)
            {
                Console.WriteLine("Couldn't find AppleMusic");
                return new Program.SongData();
            }

            var windows = new List<AutomationElement>();
            var automation = new UIA3Automation();
            var processId = amProcesses[0].Id;
            windows = [.. automation.GetDesktop().FindAllChildren(c => c.ByProcessId(processId))];


            if (windows.Count == 0)
            {
                Console.WriteLine("No windows found on desktop, trying alternative search");
                var vdesktopWin = FlaUI.Core.Application.Attach(processId)
                    .GetMainWindow(automation, TimeSpan.FromSeconds(3));
                if (vdesktopWin != null)
                {
                    windows.Add(vdesktopWin);
                }
            }


            AutomationElement? amSongPanel = null;
            bool isMiniPlayer = false;
            foreach (var window in windows)
            {
                isMiniPlayer = window.Name == "Mini Player";
                if (isMiniPlayer)
                {
                    amSongPanel = window.FindFirstDescendant(cf => cf.ByClassName("InputSiteWindowClass"));
                    if (amSongPanel != null)
                    {
                        break;
                    }
                }
                else
                {
                    amSongPanel = window.FindFirstDescendant(cf => cf.ByAutomationId("TransportBar")) ?? amSongPanel;
                }
            }

            if (amSongPanel == null)
            {
                Console.WriteLine("Couldn't find songpanel");
                return null;
            }

            var songFieldsPanel = isMiniPlayer ? amSongPanel : amSongPanel.FindFirstChild("LCD");
            var songFields = songFieldsPanel?.FindAllChildren(cf => cf.ByAutomationId("myScrollViewer")) ?? [];

            if (!isMiniPlayer && songFields.Length != 2)
            {
                Console.WriteLine(("nothing playing"));
                return new Program.SongData();
            }

            var songNameElement = songFields[0];
            var songAlbumArtistElement = songFields[1];

            if (songNameElement.BoundingRectangle.Bottom > songAlbumArtistElement.BoundingRectangle.Bottom)
            {
                songNameElement = songFields[1];
                songAlbumArtistElement = songFields[0];
            }

            var songName = songNameElement.Name;
            var songAlbumArtist = songAlbumArtistElement.Name;

            string songArtist = "";
            string songAlbum = "";
            var songSplit = songAlbumArtist.Split(" \u2014 ");
            if (songSplit.Length > 1)
            {
                songArtist = songSplit[0];
                songAlbum = songSplit[1];
            }
            else
            {
                songArtist = songSplit[0];
                songAlbum = songSplit[0];
            }

            var playPauseButton = amSongPanel.FindFirstChild("TransportControl_PlayPauseStop");
            var isPaused = playPauseButton!.Name == "Play";

            return new Program.SongData(songName, songArtist, songAlbum, isPaused);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting data from Apple Music: {ex.Message}");
            return new Program.SongData();
        }
    }
}
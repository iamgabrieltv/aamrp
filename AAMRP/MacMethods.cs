using System.Diagnostics;

namespace AAMRP;

public class MacMethods
{
    public static async Task<Program.SongData> GetAppleMusicInfo()
    {
        /*const string appleScript = $"""
                                    set output to ""
                                    tell application "Music"
                                        set t_name to name of current track
                                        set t_artist to artist of current track
                                        set t_album to album of current track
                                        set output to "" & "
                                    " & t_name & "
                                    " & t_artist & "
                                    " & t_album & "
                                    "
                                    end tell
                                    return output
                                    """;*/
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            Arguments = $"{AppDomain.CurrentDomain.BaseDirectory + "applemusic_mac.scpt"}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        
        try
        {
            Process process = new Process { StartInfo = startInfo };
            process.Start();
            
            string result = await process.StandardOutput.ReadToEndAsync();
            result = result.Trim();
            
            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"osascript failed with exit code {process.ExitCode}: {error.Trim()}");
            }

            var lines = result.Replace("\r", "").Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).ToArray();
            if (lines.Length < 3)
            {
                return new Program.SongData();
            }

            var isPaused = lines[3] != "playing";
            return new Program.SongData(lines[0], lines[1], lines[2], isPaused);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            return new Program.SongData();
        }
    }
}
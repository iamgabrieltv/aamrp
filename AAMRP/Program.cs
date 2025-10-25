using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Web;
using DiscordRPC;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Newtonsoft.Json.Linq;
using HtmlAgilityPack;

namespace AAMRP;

class Program
{
    private static DiscordRpcClient? RpcClient { get; set; }
    private static readonly HttpClient HttpClient = new HttpClient();

    static void Setup() {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");

        RpcClient = new DiscordRpcClient("1423726101519274056");
        RpcClient.Initialize();
    }

    private class ArtworkUrls
    {
        public required string AlbumArt;
        public required string ArtistUrl;
        public required string CollectionUrl;
    }

    private class SongData(string name, string artist, string album)
    {
        public string Name = name;
        public string Artist = artist;
        public string Album = album;
    }
    
    static async Task<ArtworkUrls> FetchArtworkUrl(string song, string album, string artist)
    {
        Console.WriteLine(Environment.NewLine + $"Fetching Data for {song} - {artist} from {album}...");

        var uri = new UriBuilder("https://amp-api-edge.music.apple.com/v1/catalog/us/search");
        var query = HttpUtility.ParseQueryString(uri.Query);
        query["platform"] = "web";
        query["l"] = "en-US";
        query["limit"] = "1";
        query["with"] = "serverBubbles";
        query["types"] = "songs";
        query["term"] = $"{song} {artist} {album}";
        query["include[songs]"] = "artists";
        uri.Query = query.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, uri.Uri);
        request.Headers.Accept.ParseAdd("*/*");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse("Bearer eyJhbGciOiJFUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6IldlYlBsYXlLaWQifQ.eyJpc3MiOiJBTVBXZWJQbGF5IiwiaWF0IjoxNzU5OTcwNjMxLCJleHAiOjE3NjcyMjgyMzEsInJvb3RfaHR0cHNfb3JpZ2luIjpbImFwcGxlLmNvbSJdfQ.2olNgPLuL51wQBjlYwZWslVBxqV65I921NlgdXHazA9DL_-zksa42Lr4aiGC0TV3SAe4vs9FSRtdKe9gTCCiwQ");
        request.Headers.Add("Origin", "https://music.apple.com");
        
        // Make the request
        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var jsonString = await response.Content.ReadAsStringAsync();
        var data = JObject.Parse(jsonString);
        var songData = data["results"]?["song"]?["data"]?[0];
        Console.WriteLine("Done fetching");

        var coverUrl = songData?["attributes"]?["artwork"]?["url"]?.ToString().Replace("{w}x{h}bb.jpg", "512x512bb.jpg");
        var artistUrl = songData?["relationships"]?["artists"]?["data"]?[0]?["attributes"]?["artwork"]?["url"]?.ToString().Replace("{w}x{h}bb.jpg", "512x512bb.jpg");
        var collectionUrl = songData["attributes"]?["url"]?.ToString();

        return new ArtworkUrls
        {
            AlbumArt = coverUrl,
            ArtistUrl = artistUrl,
            CollectionUrl = collectionUrl
        };
    }

    /*static async Task<string> FetchArtistArtworkUrl(string artistUrl)
    {
        Console.WriteLine("Fetching Artist Artwork...");

        var request = new HttpRequestMessage(HttpMethod.Get, artistUrl);
        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var htmlContent = await response.Content.ReadAsStringAsync();

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(htmlContent);

        HtmlNode imageNode = htmlDoc.DocumentNode.SelectSingleNode("//meta[@property='og:image']");
        if (imageNode != null)
        {
            Console.WriteLine("Done fetching");
            string url = imageNode.GetAttributeValue("content", string.Empty);
            return Regex.Replace(url, "[0-9]+x.+", "512x512bb.png");
        }
        Console.WriteLine("Couldn't fetch Artist Artwork");
        return String.Empty;
    }*/

    static async Task<string> FetchAnimatedArtworkUrl(string collectionUrl)
    {
        Console.WriteLine("Fetching Animated Artwork...");

        var request = new HttpRequestMessage(HttpMethod.Get, collectionUrl);
        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var htmlContent = await response.Content.ReadAsStringAsync();

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(htmlContent);

        HtmlNode videoNode = htmlDoc.DocumentNode.SelectSingleNode("//amp-ambient-video[1]");
        if (videoNode != null)
        {
            Console.WriteLine("Done fetching");
            string url = videoNode.GetAttributeValue("src", string.Empty);
            return url;
        }
        Console.WriteLine("Couldn't find Animated Album Cover!");
        return string.Empty;
    }

    static async Task<string> ConvertToAvif(string url, string filename)
    {
        filename = filename.Replace(" ", "");
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://aamrp.iamgabriel.dev/{filename}.avif");
        var response = await HttpClient.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Console.WriteLine("File already exists!");
            return $"https://aamrp.iamgabriel.dev/{filename}.avif";
        }

        string filepath = AppDomain.CurrentDomain.BaseDirectory + $"{filename}.avif";
        string arguments =
            $"-protocol_whitelist file,http,https,tcp,tls,crypto -i {url} -c:v libsvtav1 -preset 10 -crf 35 -r 30 -an -vf \"crop=min(iw\\,ih):min(iw\\,ih),scale=512:512\" \"{filepath}\"";
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = true,
            CreateNoWindow = true
        };

        try
        {
            Console.WriteLine($"Starting Conversion with FFMPEG... Writing to {filepath}");
            Process process = new Process { StartInfo = startInfo };
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"FFmpeg failed with exit code {process.ExitCode}");
                return String.Empty;
            }

            Console.WriteLine("Conversion completed successfully. Uploading...");
            request = new HttpRequestMessage(HttpMethod.Get, $"https://api.aamrp.iamgabriel.dev/{filename}.avif");
            response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var presignedUrl = await response.Content.ReadAsStringAsync();
                
            if (!File.Exists(filepath))
            {
                Console.WriteLine($"Error: Local file not found!");
                return string.Empty;
            }
            byte[] fileBytes = await File.ReadAllBytesAsync(filepath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/avif");

            request = new HttpRequestMessage(HttpMethod.Put, presignedUrl)
            {
                Content = fileContent
            };
            response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            Console.WriteLine("File uploaded successfully!");
            await Task.Delay(5000);
            File.Delete(filepath);
            return $"https://aamrp.iamgabriel.dev/{filename}.avif";
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return String.Empty;
        }
    }

    static SongData GetAppleMusicInfo()
    {
        var amProcesses = Process.GetProcessesByName("AppleMusic");
        if (amProcesses.Length == 0)
        {
            Console.WriteLine("Couldn't find AppleMusic");
            return null;
        }
        var windows = new List<AutomationElement>();
        var automation = new UIA3Automation();
        var processId = amProcesses[0].Id;
        windows = [.. automation.GetDesktop().FindAllChildren(c => c.ByProcessId(processId))];
        
                
        if (windows.Count == 0) {
            Console.WriteLine("No windows found on desktop, trying alternative search");
            var vdesktopWin = FlaUI.Core.Application.Attach(processId).GetMainWindow(automation, TimeSpan.FromSeconds(3));
            if (vdesktopWin != null) {
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
            return null;
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
        return new SongData(songName, songArtist, songAlbum);
    }

    static async Task Start()
    {
       var currentSong = new SongData(null, null, null);
       var objectUrl = "";
       var i = 0;
       while (true)
       {
           var newSong = GetAppleMusicInfo();

           if (newSong.Name == currentSong.Name)
           {
               Console.WriteLine($"Status unchanged. Current: {currentSong.Name}");
           }
           else
           {
               currentSong = newSong;
               var song = currentSong.Name;
               var album = currentSong.Album;
               var artist = currentSong.Artist;
                
               var result = await FetchArtworkUrl(song, album, artist);
               // var artistUrl = await FetchArtistArtworkUrl(result.ArtistUrl);
               RpcClient!.SetPresence(new RichPresence
               {
                   Type = ActivityType.Listening,
                   Details = song,
                   State = artist,
                   Assets = new Assets()
                   {
                       LargeImageKey = result.AlbumArt,
                       LargeImageText = album,
                       SmallImageKey = result.ArtistUrl,
                       SmallImageText = artist
                   }
               });

               if (newSong.Album == currentSong.Album && i != 0)
               {
                   // avoid checking for animated cover again
                   RpcClient.SetPresence(new RichPresence
                   {
                       Type = ActivityType.Listening,
                       Details = song,
                       State = artist,
                       Assets = new Assets()
                       {
                           LargeImageKey = objectUrl,
                           LargeImageText = album,
                           SmallImageKey = result.ArtistUrl,
                           SmallImageText = artist
                       }
                   });
                   return;
               }
               var animatedUrl = await FetchAnimatedArtworkUrl(result.CollectionUrl);
               if (animatedUrl != String.Empty)
               {
                   Console.WriteLine(animatedUrl);
                   objectUrl = await ConvertToAvif(animatedUrl, $"{album}-{artist}");
                   if (objectUrl != String.Empty)
                   {
                       Console.WriteLine($"Updating RPC to {objectUrl}");
                       RpcClient.SetPresence(new RichPresence
                       {
                           Type = ActivityType.Listening,
                           Details = song,
                           State = artist,
                           Assets = new Assets()
                           {
                               LargeImageKey = objectUrl,
                               LargeImageText = album,
                               SmallImageKey = result.ArtistUrl,
                               SmallImageText = artist
                           }
                       });
                   }
               }
           }

           await Task.Delay(5000);
           i++;
       }
    }
    
    static void Main()
    {
        Console.WriteLine("Welcome to AAMRP!" + Environment.NewLine + "Animated Apple Music Rich Presence");
        Setup();
        Thread staThread = new Thread(() =>
        {
            Start().Wait();
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true; 
        staThread.Start();
        
        Console.WriteLine("Background polling thread started. Press any key to exit.");
        
        // Cleanup
        Console.ReadKey();
        RpcClient.Dispose();
    }
}
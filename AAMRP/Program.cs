using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Web;
using DiscordRPC;
using Newtonsoft.Json.Linq;
using HtmlAgilityPack;

namespace AAMRP;

public class Program
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

    public class SongData(string? name = null, string? artist = null, string? album = null, bool isPaused = false)
    {
        public string? Name = name;
        public string? Artist = artist;
        public string? Album = album;
        public bool IsPaused = isPaused;
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

    static async void UpdatePresence(string song, string artist, string album, string albumUrl, string artistUrl)
    {
        RpcClient!.SetPresence(new RichPresence
        {
            Type = ActivityType.Listening,
            Details = song,
            State = artist,
            Assets = new Assets()
            {
                LargeImageKey = albumUrl,
                LargeImageText = album,
                SmallImageKey = artistUrl,
                SmallImageText = artist
            }
        });
        await Task.Delay(15000);
    }

    static async Task StartWindows()
    {
       var currentSong = new SongData();
       var objectUrl = "";
       var i = 0;
       while (true)
       {
           var newSong = WindowsMethods.GetAppleMusicInfo();

           if (newSong.Name == currentSong.Name && newSong.IsPaused == currentSong.IsPaused)
           {
               Console.WriteLine($"Status unchanged. Current: {currentSong.Name}");
           }
           else
           {
               var albumIsSame = currentSong.Album == newSong.Album;
               currentSong = newSong;
               var song = currentSong.Name;
               var album = currentSong.Album;
               var artist = currentSong.Artist;

               if (song == null || currentSong.IsPaused)
               {
                   Console.WriteLine("Nothing playing");
                   RpcClient.ClearPresence();
                   await Task.Delay(5000);
                   i++;
                   continue;
               }
               
               if (!albumIsSame) RpcClient!.ClearPresence();
                
               var result = await FetchArtworkUrl(song, album, artist);
               // var artistUrl = await FetchArtistArtworkUrl(result.ArtistUrl);
               UpdatePresence(song, artist, album, result.AlbumArt, result.ArtistUrl);

               if (albumIsSame && i != 0 && objectUrl != "")
               {
                   // avoid checking for animated cover again
                   UpdatePresence(song, artist, album, objectUrl, result.ArtistUrl);
                   i++;
                   continue;
               }
               var animatedUrl = await FetchAnimatedArtworkUrl(result.CollectionUrl);
               if (animatedUrl != String.Empty)
               {
                   Console.WriteLine(animatedUrl);
                   objectUrl = await ConvertToAvif(animatedUrl, $"{album}-{artist}");
                   if (objectUrl != String.Empty)
                   {
                       Console.WriteLine($"Updating RPC to {objectUrl}");
                       UpdatePresence(song, artist, album, objectUrl, result.ArtistUrl);
                   }
               }
           }

           await Task.Delay(5000);
           i++;
       }
    }
    
    static async Task StartMac()
    {
       var currentSong = new SongData();
       var objectUrl = "";
       var i = 0;
       while (true)
       {
           var newSong = await MacMethods.GetAppleMusicInfo();
           if (newSong.Name == null) newSong.IsPaused = true;

           if (newSong.Name == currentSong.Name && newSong.IsPaused == currentSong.IsPaused)
           {
               Console.WriteLine($"Status unchanged. Current: {currentSong.Name}");
           }
           else
           {
               var albumIsSame = currentSong.Album == newSong.Album;
               currentSong = newSong;
               var song = currentSong.Name;
               var album = currentSong.Album;
               var artist = currentSong.Artist;

               if (song == null || currentSong.IsPaused)
               {
                   Console.WriteLine("Nothing playing");
                   RpcClient.ClearPresence();
                   await Task.Delay(5000);
                   i++;
                   continue;
               }
               
               if (!albumIsSame) RpcClient!.ClearPresence();
                
               var result = await FetchArtworkUrl(song, album, artist);
               // var artistUrl = await FetchArtistArtworkUrl(result.ArtistUrl);
               UpdatePresence(song, artist, album, result.AlbumArt, result.ArtistUrl);

               if (albumIsSame && i != 0 && objectUrl != "")
               {
                   // avoid checking for animated cover again
                   UpdatePresence(song, artist, album, objectUrl, result.ArtistUrl);
                   i++;
                   continue;
               }
               var animatedUrl = await FetchAnimatedArtworkUrl(result.CollectionUrl);
               if (animatedUrl != String.Empty)
               {
                   Console.WriteLine(animatedUrl);
                   objectUrl = await ConvertToAvif(animatedUrl, $"{album}-{artist}");
                   if (objectUrl != String.Empty)
                   {
                       Console.WriteLine($"Updating RPC to {objectUrl}");
                       UpdatePresence(song, artist, album, objectUrl, result.ArtistUrl);
                   }
               }
           }

           await Task.Delay(5000);
           i++;
       }
    }
    
    static void Main()
    {
        Console.WriteLine("Welcome to AAMRP!" + Environment.NewLine + "Animated Apple Music Rich Presence" + Environment.NewLine);
        Setup();

        #if WINDOWS
            Thread staThread = new Thread(() =>
            {
                StartWindows().Wait();
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.IsBackground = true; 
            staThread.Start();
            Console.WriteLine("Background polling thread started. Press any key to exit.");
        #elif MACOS
        Task backgroundTask = StartMac();
        #endif
        
        // Cleanup
        Console.ReadKey();
        RpcClient!.ClearPresence();
        RpcClient.Dispose();
    }
}
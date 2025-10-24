using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using DiscordRPC;
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
    
    static async Task<ArtworkUrls> FetchArtworkUrl(string song, string album, string artist)
    {
        Console.WriteLine(Environment.NewLine + $"Fetching Data for {song} - {artist} from {album}...");

        var request = new HttpRequestMessage(HttpMethod.Get, $"https://itunes.apple.com/search?term={song} {album} {artist}&media=music&entity=song");
        
        // Make the request
        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var jsonString = await response.Content.ReadAsStringAsync();
        var data = JObject.Parse(jsonString);
        var songData = data["results"]?.First(s => s["collectionName"] != null && s["collectionName"]!.ToString().Equals(album, StringComparison.OrdinalIgnoreCase));
        if (songData == null) throw new Exception("Song not found");
        Console.WriteLine("Done fetching");
        var coverUrl = songData["artworkUrl100"]!.ToString().Replace("100x100", "512x512");
        var artistUrl = songData["artistViewUrl"]!.ToString();
        var collectionUrl = songData["collectionViewUrl"]!.ToString();

        return new ArtworkUrls
        {
            AlbumArt = coverUrl,
            ArtistUrl = artistUrl,
            CollectionUrl = collectionUrl
        };
    }

    static async Task<string> FetchArtistArtworkUrl(string artistUrl)
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
    }

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

        string filepath = $"/home/gabriel/Downloads/{filename}.avif";
        string arguments =
            $"-protocol_whitelist file,http,https,tcp,tls,crypto -i {url} -c:v libsvtav1 -preset 10 -crf 35 -r 30 -an -vf \"crop=min(iw\\,ih):min(iw\\,ih),scale=512:512\" \"{filepath}\"";
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = true,
            CreateNoWindow = false
        };

        try
        {
            Console.WriteLine("Starting Conversion with FFMPEG...");
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
    
    static void Main()
    {
        Console.WriteLine("Welcome to AAMRP!" + Environment.NewLine + "Animated Apple Music Rich Presence");

        const string song = "Blue Moon";
        const string album = "Midnight Sun";
        const string artist = "Zara Larsson";

        Setup();
        var result = FetchArtworkUrl(song, album, artist).Result;
        var artistUrl = FetchArtistArtworkUrl(result.ArtistUrl).Result;
        RpcClient!.SetPresence(new RichPresence
        {
            Type = ActivityType.Listening,
            Details = song,
            State = artist,
            Assets = new Assets()
            {
                LargeImageKey = result.AlbumArt,
                LargeImageText = album,
                SmallImageKey = artistUrl,
                SmallImageText = artist
            }
        });
        
        var animatedUrl = FetchAnimatedArtworkUrl(result.CollectionUrl).Result;
        if (animatedUrl != String.Empty)
        {
            Console.WriteLine(animatedUrl);
            var objectUrl = ConvertToAvif(animatedUrl, $"{album}-{artist}").Result;
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
                        SmallImageKey = artistUrl,
                        SmallImageText = artist
                    }
                });
            }
        }
        
        // Cleanup
        Console.ReadKey();
        RpcClient.Dispose();
    }
}
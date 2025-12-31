using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;

class Program
{
    static async Task Main()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        var html = await client.GetStringAsync("https://gamescriptions.com/subscription/platform/ea");
        File.WriteAllText("ea_dump.html", html);
        Console.WriteLine("Downloaded ea_dump.html");
    }
}
using System.Net;

namespace PcDashboard.Services;

/// <summary>Uygulama genelinde tek bir HttpClient.</summary>
public static class Http
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        // Bazı RSS sunucuları tarayıcı dışı istemcileri reddediyor
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) PcDashboard/0.1");
        return client;
    }
}

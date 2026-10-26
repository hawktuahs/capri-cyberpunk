// Smoke-test the app's actual request factory without API keys or paid inference.
using System;
using System.Net;
using System.Reflection;

internal static class CloudTransportCheck
{
    private static int Main(string[] args)
    {
        try
        {
            Assembly app = Assembly.LoadFrom(args[0]);
            MethodInfo create = app.GetType("CapriDesktop.Brain").GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string endpoint in new[] { "https://api.groq.com/openai/v1/models", "https://integrate.api.nvidia.com/v1/models" })
            {
                HttpWebRequest request = (HttpWebRequest)create.Invoke(null, new object[] { endpoint, 15000 });
                int status;
                try { using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) status = (int)response.StatusCode; }
                catch (WebException error)
                {
                    if (error.Response == null) throw;
                    using (HttpWebResponse response = (HttpWebResponse)error.Response) status = (int)response.StatusCode;
                }
                Console.WriteLine("PASS HTTPS transport: " + new Uri(endpoint).Host + " returned HTTP " + status + " without credentials");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}

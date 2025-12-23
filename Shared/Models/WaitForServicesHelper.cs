namespace Shared.ServicesHelpers
{
    public class WaitForServicesHelper
    {
        public static async Task WaitForSensorInfoAsync(List<string> urls, CancellationToken ct)
        {
            foreach (var url in urls)
            {
                while (!ct.IsCancellationRequested)
                {

                    try
                    {
                        using var http = new HttpClient();
                        var response = await http.GetAsync($"{url}/alive", ct);
                        if (response.IsSuccessStatusCode)
                            break;
                    }
                    catch { }

                    await Task.Delay(100, ct);
                }
                
            }
        }
    }
}



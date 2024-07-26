namespace LanguageServer;

static class Program
{
    static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // UTF8N for non-Windows platform

        try
        {
            OmniSharpService service = new();
            await service.CreateAsync();
            return 0;
        }
        catch (AggregateException ex)
        {
            await Console.Error.WriteLineAsync(ex.InnerExceptions[0].ToString());
            return -1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.ToString());
            return -1;
        }
    }
}

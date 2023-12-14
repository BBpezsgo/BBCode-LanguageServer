namespace LanguageServer
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            OmniSharpService service = new();
            Console.OutputEncoding = new System.Text.UTF8Encoding(); // UTF8N for non-Windows platform
            try
            {
                await service.CreateAsync();
                return 0;
            }
            catch (AggregateException ex)
            {
                await Console.Error.WriteLineAsync(ex.InnerExceptions[0].ToString());
                return -1;
            }
        }
    }
}

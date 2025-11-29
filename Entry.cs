namespace LanguageServer;

static class Program
{
    static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            OmniSharpService service = new();
            await service.CreateAsync().ConfigureAwait(false);
            return 0;
        }
        catch (AggregateException ex)
        {
            foreach (Exception item in ex.Flatten().InnerExceptions)
            {
                await Console.Error.WriteLineAsync(item.ToString()).ConfigureAwait(false);
            }
            return -1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return -1;
        }
    }
}

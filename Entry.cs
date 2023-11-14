namespace LanguageServer
{
    class Program
    {
        static void Main(string[] args)
        {
            OmniSharpService serviceAppInterface = new();
            Console.OutputEncoding = new System.Text.UTF8Encoding(); // UTF8N for non-Windows platform
            try
            {
#pragma warning disable VSTHRD002
                serviceAppInterface.CreateAsync().Wait();
#pragma warning restore VSTHRD002
            }
            catch (AggregateException ex)
            {
                Console.Error.WriteLine(ex.InnerExceptions[0]);
                Environment.Exit(-1);
            }
        }
    }
}

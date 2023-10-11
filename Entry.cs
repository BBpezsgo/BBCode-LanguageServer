using System;

namespace LanguageServer
{
    using Interface;

    class Program
    {
#if true
        static void Main(string[] args)
        {
            ServiceAppInterfaceOmniSharp serviceAppInterface = new();
            Console.OutputEncoding = new System.Text.UTF8Encoding(); // UTF8N for non-Windows platform
            _ = new DocumentInterface(serviceAppInterface);
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
#else
        static void Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(); // UTF8N for non-Windows platform
            ServiceApp app = new(new ServiceAppInterface1(Console.OpenStandardInput(), Console.OpenStandardOutput()));
            Logger.Instance.Attach((app.Interface as ServiceAppInterface1).Connection);
            try
            {
                (app.Interface as ServiceAppInterface1).Listen().Wait();
            }
            catch (AggregateException ex)
            {
                Console.Error.WriteLine(ex.InnerExceptions[0]);
                Environment.Exit(-1);
            }
        }
#endif
    }
}

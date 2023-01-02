using BBCodeLanguageServer.Interface;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;

using System;
using System.Threading.Tasks;

namespace BBCodeLanguageServer
{
    class Program
    {
#if true
        static void Main(string[] args)
        {
            ServiceAppInterface2 serviceAppInterface = new();
            Console.OutputEncoding = new System.Text.UTF8Encoding(); // UTF8N for non-Windows platform
            DocumentInterface app = new(serviceAppInterface);
            try
            {
                serviceAppInterface.Create().Wait();
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

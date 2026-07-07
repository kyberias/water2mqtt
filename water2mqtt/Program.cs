using System.Runtime.InteropServices.JavaScript;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace water2mqtt
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            var builder = 
                Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton(TimeProvider.System);

                    services.AddSingleton<ZennerMnk>();
                    services.AddSingleton<IWaterMeterRaw>(x => x.GetRequiredService<ZennerMnk>());
                    services.AddSingleton<IHostedService>(x => x.GetRequiredService<ZennerMnk>());

                    services.AddSingleton<ErrorCorrectingProxy>();
                    services.AddSingleton<IWaterMeter>(x => x.GetRequiredService<ErrorCorrectingProxy>());
                    services.AddSingleton<IHostedService>(x => x.GetRequiredService<ErrorCorrectingProxy>());

                    //services.AddSingleton<IHostedService, ErrorCorrectingProxy>();
                    services.AddSingleton<IReadingStorage, ReadingStorageFile>();

                    services.AddLogging(builder => builder.AddFile("water2mqtt-{Date}.log", LogLevel.Trace));
                    services.AddHostedService<Meter2MqttService>();

                    //services.AddHostedService<ZennerMnk>().AddSingleton<IWaterMeterRaw>();
                    //services.AddHostedService<ErrorCorrectingProxy>().AddSingleton<IWaterMeter>();
                })
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;
                    config.AddCommandLine(args);
                    config.AddEnvironmentVariables();
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
                });

            await builder.Build().RunAsync();
        }
    }
}

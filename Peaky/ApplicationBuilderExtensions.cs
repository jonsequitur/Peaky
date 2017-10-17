using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Peaky
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UsePeaky(this IApplicationBuilder app)
        {
            if (!Trace.Listeners.OfType<PeakyTraceListener>().Any())
            {
                Trace.Listeners.Add(new PeakyTraceListener());
;            }

            app.UseRouter(builder =>
            {
                var sensorRegistry = builder.ServiceProvider.GetService<SensorRegistry>();

                if (sensorRegistry != null)
                {
                    builder.Routes.Add(
                        new SensorRouter(
                            sensorRegistry,
                            builder.ServiceProvider.GetRequiredService<AuthorizeSensors>()
                        ).AllowVerbs("GET"));
                }

                var testTargets = builder.ServiceProvider.GetService<TestTargetRegistry>();

                var testDefinitions = builder.ServiceProvider.GetService<TestDefinitionRegistry>();

                if (testTargets != null &&
                    testDefinitions != null)
                {
                    builder.Routes.Add(
                        new TestRouter(
                            testTargets,
                            testDefinitions));
                }


            });

            return app;
        }
    }
}

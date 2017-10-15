using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using Pocket;
using static Pocket.Logger<Peaky.TestRouter>;

namespace Peaky
{
    public class TestRouter : IRouter
    {
        private readonly TestTargetRegistry testTargets;
        private readonly TestDefinitionRegistry testDefinitions;

        public TestRouter(TestTargetRegistry testTargets, TestDefinitionRegistry testDefinitions)
        {
            this.testDefinitions = testDefinitions ??
                                   throw new ArgumentNullException(nameof(testDefinitions));
            this.testTargets = testTargets ??
                               throw new ArgumentNullException(nameof(testTargets));
        }

        public async Task RouteAsync(RouteContext context)
        {
            var path = context.HttpContext.Request.Path;

            var testRootPath = "/tests";

            if (!path.StartsWithSegments(new PathString(testRootPath)))
            {
                return;
            }

            var segments = path.Value
                               .Substring(testRootPath.Length)
                               .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            switch (segments.Length)
            {
                case 0:
                case 1:
                case 2:
                    ListTests(
                        segments.ElementAtOrDefault(0),
                        segments.ElementAtOrDefault(1),
                        context);
                    break;

                case 3:
                    RunTest(
                        segments[0],
                        segments[1],
                        segments[2],
                        context);
                    break;
            }
        }

        public VirtualPathData GetVirtualPath(VirtualPathContext context) => null;

        private void ListTests(
            string environment,
            string application,
            RouteContext context)
        {
            using (Log.OnEnterAndExit())
            {
                if (environment != null)
                {
                    if (!testTargets.Any(tt => tt.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase)))
                    {
                        return;
                    }
                }

                if (application != null &&
                    !testTargets.Any(tt => tt.Application.Equals(application, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                context.Handler = async httpContext =>
                {
                    var applicableTargets = testTargets
                        .Where(
                            tt => environment == null ||
                                  tt.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase))
                        .Where(
                            tt => application == null ||
                                  tt.Application.Equals(application, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    var rootPath = context.HttpContext.Request.PathBase;

                    var tests = testDefinitions
                        .SelectMany(
                            t =>
                                applicableTargets
                                    .Where(t.AppliesTo)
                                    .Select(
                                        ea =>
                                            new Test
                                            {
                                                Environment = ea.Environment,
                                                Application = ea.Application,
                                                Url = $"{rootPath}/{ea.Environment}/{ea.Application}/{t.TestName}",
                                                Tags = t.Tags,
                                                Parameters = t.Parameters.Any()
                                                                 ? t.Parameters.ToArray()
                                                                 : null
                                            })
                                    .Where(l => l.Url != null))
                        .OrderBy(t => t.Url.ToString());

                    var json = JsonConvert.SerializeObject(new { Tests = tests });

                    await httpContext.Response.WriteAsync(json);
                };
            }
        }

        private void RunTest(string environment, string application, string testName, RouteContext context)
        {
            using (Log.OnEnterAndExit())
            {
                TestTarget target;

                try
                {
                    target = testTargets.Get(environment, application);
                }
                catch (TestNotDefinedException)
                {
                    context.Handler = async httpContext =>
                    {
                        httpContext.Response.StatusCode = (int) HttpStatusCode.NotFound;
                    };
                    return;
                }

                var test = testDefinitions.Get(testName);

                context.Handler = async httpContext =>
                {
                    TestResult result;

                    try
                    {
                        var returnValue = await test.Run(
                                              httpContext,
                                              target.ResolveDependency);

                        if (returnValue is Task task)
                        {
                            await task;

                            if (task.GetType().IsGenericType)
                            {
                                returnValue = ((dynamic) task).Result;
                            }
                        }

                        result = TestResult.Pass(returnValue);
                    }
                    catch (Exception exception)
                    {
                        result = TestResult.Fail(exception);
                        httpContext.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                    }

                    var json = JsonConvert.SerializeObject(result);

                    await httpContext.Response.WriteAsync(json);
                };
            }
        }
    }
}

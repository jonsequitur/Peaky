// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Pocket;
using static Pocket.Logger<Peaky.TestRouter>;

namespace Peaky;

internal class TestRouter : PeakyRouter
{
    private readonly TestTargetRegistry testTargets;
    private readonly TestDefinitionRegistry testDefinitions;
    private readonly string pathBase;
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Converters = new List<JsonConverter>
        {
            new StringEnumConverter
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        },
        Error = (_, args) =>
        {
            args.ErrorContext.Handled = true;
        }
    };

    public TestRouter(
        TestTargetRegistry testTargets,
        TestDefinitionRegistry testDefinitions,
        string pathBase = "/tests") : base(pathBase)
    {
        this.pathBase = pathBase ??
                        throw new ArgumentNullException(nameof(pathBase));

        this.testDefinitions = testDefinitions ??
                               throw new ArgumentNullException(nameof(testDefinitions));
        this.testTargets = testTargets ??
                           throw new ArgumentNullException(nameof(testTargets));
    }

    public override async Task RouteAsync(RouteContext context)
    {
        var (environment, application, test) = ParseUrl(context);

        if (test is null)
        {
            ListTests(environment,
                      application,
                      context);
        }
        else
        {
            await RunTest(environment,
                          application,
                          test,
                          context);
        }
    }

    private (string environment, string application, string test) ParseUrl(RouteContext context)
    {
        var segments = context.HttpContext
                              .Request
                              .Path
                              .Value
                              .Substring(pathBase.Length)
                              .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        string application = null;
        string environment = null;
        string test = null;

        if (segments.Length < 3)
        {
            var firstSegment = segments.ElementAtOrDefault(0);

            environment = testTargets.FirstOrDefault(t => t.Environment.Equals(firstSegment, StringComparison.OrdinalIgnoreCase))?.Environment;

            application = segments.ElementAtOrDefault(1);

            if (environment is null)
            {
                application = firstSegment;
            }
        }
        else if (segments.Length == 3)
        {
            environment = segments.ElementAt(0);
            application = segments.ElementAt(1);
            test = segments.ElementAt(2);
        }

        return (environment, application, test);
    }

    private void ListTests(
        string environment,
        string application,
        RouteContext context)
    {
        using var _ = Log.OnEnterAndExit();

        if (environment is not null)
        {
            if (!testTargets.Any(tt => tt.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
        }

        if (application is not null &&
            !testTargets.Any(tt => tt.Application.Equals(application, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        context.Handler = async httpContext =>
        {
            var applicableTargets = testTargets
                                    .Where(
                                        tt => environment is null ||
                                              tt.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase))
                                    .Where(
                                        tt => application is null ||
                                              tt.Application.Equals(application, StringComparison.OrdinalIgnoreCase))
                                    .ToArray();

            DiscoverParameterizedTestCases(context, applicableTargets);

            var tests = testDefinitions
                        .SelectMany(
                            definition =>
                                applicableTargets
                                    .Where(definition.AppliesTo)
                                    .Where(_ =>
                                               MatchesFilter(
                                                   definition.Tags,
                                                   context.HttpContext.Request.Query))
                                    .SelectMany(
                                        target => Test.CreateTests(target,definition, context.HttpContext.Request))
                                    .Where(l => l.Url is not null))
                        .OrderBy(t => t.Url.ToString());

            var json = JsonConvert.SerializeObject(new { Tests = tests }, SerializerSettings);

            await httpContext.Response.WriteAsync(json);
        };
    }

    private void DiscoverParameterizedTestCases(RouteContext context, TestTarget[] applicableTargets)
    {
        foreach (var parameterizedTestCases in testDefinitions
                                               .SelectMany(
                                                   definition =>
                                                       applicableTargets
                                                           .Where(definition.AppliesTo)
                                                           .Where(_ => MatchesFilter(definition.Tags, context.HttpContext.Request.Query))
                                                           .Select(target => (type: definition.TestType, target: target)))
                                               .GroupBy(e => e.type)
                                               .Where(e => e.Key.GetInterfaces().Contains(typeof(IParameterizedTestCases)))
                                               .Select(e => (type: e.Key, targets: e.Select(g => g.target))))
        {
            foreach (var testTarget in parameterizedTestCases.targets)
            {
                var testClassInstance =
                    (IParameterizedTestCases) testTarget.DependencyRegistry.Container.Resolve(parameterizedTestCases.type);
                testClassInstance.RegisterTestCasesTo(testTarget.DependencyRegistry);
            }
        }
    }

    private Task RunTest(
        string environment,
        string application,
        string testName,
        RouteContext routeContext)
    {
        using var exit = Log.OnEnterAndExit();

        TestTarget target;
        
        try
        {
            target = testTargets.Get(environment, application);
        }
        catch (TestNotDefinedException)
        {
            routeContext.Handler = httpContext =>
            {
                httpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return Task.CompletedTask;
            };
            return Task.CompletedTask;
        }

        var testDefinition = testDefinitions.Get(testName);

        if (!testDefinition.AppliesTo(target))
        {
            return Task.CompletedTask;
        }

        var url = routeContext.HttpContext.Request.GetLink(target, testDefinition);
        var test = new Test(
            application: target.Application,
            environment: target.Environment,
            name: testDefinition.TestName,
            url: url)
        {
            Tags = testDefinition.Tags
        };

        routeContext.Handler = async httpContext =>
        {
            TestResult result;

            var stopwatch = Stopwatch.StartNew();

            try
            {
                TraceBuffer.Initialize();

                var container = target.DependencyRegistry.Container;

                if (target.RequiresServiceWarmup)
                {
                    var warmup = container.Resolve<ServiceWarmupTracker>();
                    await warmup.WarmUp();
                }

                var returnValue = testDefinition.Run(
                                      httpContext,
                                      container.Resolve,
                                      target);

                if (returnValue is Task task)
                {
                    await task;

                    if (task.GetType().IsGenericType)
                    {
                        var genericTypeParameter = task.GetType().GenericTypeArguments.First();

                        if (genericTypeParameter.IsPublic)
                        {
                            // task is Task<T> so await to get its return value
                            returnValue = await (dynamic) task;
                        }
                        else
                        {
                            returnValue = null;
                        }
                    }
                }

                result = TestResult.CreatePassedResult(returnValue, stopwatch.Elapsed, test);
            }
            catch (ParameterFormatException exception)
            {
                result = TestResult.CreateFailedResult(exception, stopwatch.Elapsed, test);
                httpContext.Response.StatusCode = (int) HttpStatusCode.BadRequest;
            }
            catch (TestInconclusiveException tie)
            {
                result = TestResult.CreateInconclusiveResult(tie, stopwatch.Elapsed, test);
                httpContext.Response.StatusCode = (int) HttpStatusCode.ServiceUnavailable;
            }
            catch (TestTimeoutException tte)
            {
                result = TestResult.CreateTimeoutResult(tte, stopwatch.Elapsed, test);
                httpContext.Response.StatusCode = (int) HttpStatusCode.GatewayTimeout;
            }
            catch (TestFailedException tfe)
            {
                result = TestResult.CreateFailedResult(tfe, stopwatch.Elapsed, test);
                httpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            catch (Exception exception)
            {
                result = TestResult.CreateFailedResult(exception, stopwatch.Elapsed, test);
                httpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }

            string responseText = null;

            switch (httpContext.Request.ContentType)
            {
                case "text/html":

                    break;

                case "application/json":
                default:
                    responseText = JsonConvert.SerializeObject(result, SerializerSettings);

                    break;
            }

            httpContext.Response.ContentType ??= "application/json";

            await httpContext.Response.WriteAsync(responseText);
        };

        return Task.CompletedTask;
    }

    private static bool MatchesFilter(
        string[] testTags,
        IQueryCollection query)
    {
        //If no tags were requested, then it is a match
        if (query.Count is 0)
        {
            return true;
        }

        var includeTags = query.Where(t => string.Equals(
                                          t.Value.FirstOrDefault(),
                                          "true",
                                          StringComparison.OrdinalIgnoreCase))
                               .Select(t => t.Key)
                               .ToArray();

        var excludeTags = query.Where(t =>
                                          string.Equals(t.Value.FirstOrDefault(), "false", StringComparison.OrdinalIgnoreCase))
                               .Select(t => t.Key)
                               .ToArray();

        return !excludeTags.Intersect(testTags, StringComparer.OrdinalIgnoreCase).Any() &&
               includeTags.Intersect(testTags, StringComparer.OrdinalIgnoreCase).Count() == includeTags.Length;
    }
}